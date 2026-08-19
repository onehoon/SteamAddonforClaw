/*
 * Steam Input Addon - native QAM tab injection.
 *
 * Independent implementation (does not use or port Decky Loader / Millennium code).
 * Evaluated once inside Steam's GamepadUI CEF context via CDP Runtime.evaluate. The top-level
 * expression evaluates to the Boolean result of install(), so QamHost can tell success from
 * failure directly from the Runtime.evaluate response instead of assuming success.
 *
 * Responsibilities:
 *   1. Locate Steam's webpack module runtime.
 *   2. Find the QAM renderer element: a module export whose `.type` function source contains the
 *      QuickAccessMenuBrowserView / QuickAccessMenuEmbedded signature.
 *   3. Patch that element's `.type` so its returned React tree gains one extra tab, keyed so
 *      re-running this script is a no-op (idempotent).
 *   4. Expose install()/uninstall() on a single Addon-owned global so QamHost can clean up on
 *      graceful shutdown.
 *
 * Fails closed: if the expected Steam module/renderer signature is not found, this script logs
 * "QAM integration unavailable" and injects nothing. It never falls back to DOM scraping.
 */
(function () {
  "use strict";

  const GLOBAL_KEY = "__STEAM_INPUT_ADDON_QAM__";
  const TAB_MARKER = "steamInputAddonQam";
  const QAM_SIGNATURES = ["QuickAccessMenuBrowserView", "QuickAccessMenuEmbedded"];

  function log(message) {
    console.log("[SteamInputAddon:QAM] " + message);
  }

  function logOnce(key, message) {
    state.diagnostics ??= {};
    if (state.diagnostics[key]) return;
    state.diagnostics[key] = true;
    log(message);
  }

  function findWebpackRequire() {
    const chunkGlobalNames = ["webpackChunksteamui", "webpackChunk_steamclient"];
    for (const name of chunkGlobalNames) {
      const chunkArray = window[name];
      if (!Array.isArray(chunkArray)) continue;

      let capturedRequire = null;
      try {
        chunkArray.push([
          [Symbol("addon-probe")],
          {},
          (req) => {
            capturedRequire = req;
          },
        ]);
      } catch (err) {
        continue;
      }

      if (typeof capturedRequire === "function") {
        logOnce("webpack", `webpack runtime captured: ${name}`);
        return capturedRequire;
      }
    }
    return null;
  }

  function collectSearchableModules(webpackRequire) {
    const modules = [];
    const seen = new Set();
    let loadFailures = 0;

    const add = (moduleExports) => {
      if (!moduleExports || typeof moduleExports !== "object" || seen.has(moduleExports)) return;
      seen.add(moduleExports);
      modules.push(moduleExports);
      if (moduleExports.default && typeof moduleExports.default === "object" && !seen.has(moduleExports.default)) {
        seen.add(moduleExports.default);
        modules.push(moduleExports.default);
      }
    };

    for (const moduleRecord of Object.values(webpackRequire.c || {})) {
      add(moduleRecord && moduleRecord.exports);
    }

    for (const id of Object.keys(webpackRequire.m || {})) {
      try {
        add(webpackRequire(id));
      } catch (err) {
        // Some Steam modules have side effects or unmet prerequisites; skip only that module.
        loadFailures++;
      }
    }

    logOnce("moduleDiscovery", `webpack modules: cached=${Object.keys(webpackRequire.c || {}).length} registered=${Object.keys(webpackRequire.m || {}).length} loaded=${modules.length} loadFailures=${loadFailures}`);

    return modules;
  }

  // Finds every React element whose `.type` render function is one of Steam's QAM renderer
  // variants (QuickAccessMenuBrowserView / QuickAccessMenuEmbedded), by matching the signature
  // strings against the function's own source (not the module's export names). Both variants are
  // patched because enumeration order does not tell us which one the current Steam build renders.
  function findQamRenderers(webpackRequire) {
    const matches = [];
    for (const moduleExports of collectSearchableModules(webpackRequire)) {
      for (const candidate of Object.values(moduleExports)) {
        const render = candidate && typeof candidate.type === "function" ? candidate.type : null;
        if (!render) continue;

        let source;
        try {
          source = Function.prototype.toString.call(render);
        } catch (err) {
          continue;
        }

        if (QAM_SIGNATURES.some((sig) => source.includes(sig))) {
          matches.push({ renderer: candidate, originalType: render });
        }
      }
    }

    return [...new Map(matches.map((m) => [m.renderer, m])).values()];
  }

  function findReact(webpackRequire) {
    for (const mod of collectSearchableModules(webpackRequire)) {
      if (mod && mod.createElement && mod.Component) {
        logOnce("react", "React export found.");
        return mod;
      }
    }
    return null;
  }

  function findReactNode(node, predicate, depth) {
    if (node == null || depth > 16) return null;

    if (Array.isArray(node)) {
      for (const child of node) {
        const found = findReactNode(child, predicate, depth + 1);
        if (found) return found;
      }
      return null;
    }

    if (typeof node !== "object") return null;
    if (predicate(node)) return node;

    const children = node.props && node.props.children;
    return children == null ? null : findReactNode(children, predicate, depth + 1);
  }

  function findTabsPropOwner(node, depth) {
    return findReactNode(node, (candidate) =>
      candidate.props && Array.isArray(candidate.props.tabs), depth);
  }

  function patchTabsProducer(outerResult, React) {
    const node = findReactNode(
      outerResult,
      (candidate) =>
        candidate.props &&
        typeof candidate.props.onFocusNavDeactivated === "function" &&
        typeof candidate.type === "function",
      0
    );
    if (!node) return false;

    const originalType = node.type;
    let patchedType = state.nestedTypes && state.nestedTypes.get(originalType);
    if (!patchedType) {
      patchedType = function patchedTabsProducer(...args) {
        const result = originalType.apply(this, args);
        const owner = findTabsPropOwner(result, 0);
        if (!owner) return result;
        logOnce("tabsOwner", `tabs owner found. ExistingTabs=${owner.props.tabs.length}`);
        if (!owner.props.tabs.some((tab) => tab && tab[TAB_MARKER])) {
          owner.props.tabs.push(buildAddonTab(React));
          logOnce("tabInserted", "Steam Input Addon tab inserted.");
        } else {
          logOnce("duplicateTab", "Duplicate tab already present; insertion skipped.");
        }
        return result;
      };

      if (!state.nestedTypes) state.nestedTypes = new Map();
      state.nestedTypes.set(originalType, patchedType);
    }

    if (node.type === originalType) {
      node.type = patchedType;
      logOnce("nestedPatch", "Nested tabs producer patched.");
    }
    return true;
  }

  function buildAddonTab(React) {
    const icon = React.createElement(
      "svg",
      { viewBox: "0 0 24 24", width: 24, height: 24 },
      React.createElement("circle", { cx: 12, cy: 12, r: 9, fill: "currentColor" })
    );

    const panel = React.createElement(
      "div",
      { style: { padding: "16px" } },
      React.createElement("h2", null, "Steam Input Addon"),
      React.createElement("p", null, "QAM integration test")
    );

    return {
      [TAB_MARKER]: true,
      key: "steam-input-addon",
      title: "Steam Input Addon",
      tab: icon,
      panel,
    };
  }

  // One stable, Addon-owned state object. install()/uninstall() mutate it in place rather than
  // replacing it, so the functions exposed on it below always remain callable.
  const state = window[GLOBAL_KEY] || (window[GLOBAL_KEY] = {});

  function install() {
    if (state.installed) {
      log("install() called but already installed; no-op.");
      return true;
    }

    const webpackRequire = findWebpackRequire();
    if (!webpackRequire) {
      log("QAM integration unavailable (webpack runtime not found).");
      return false;
    }

    const patches = findQamRenderers(webpackRequire);
    logOnce("rendererCount", `QAM renderer count=${patches.length}.`);
    if (patches.length === 0) {
      log("QAM integration unavailable (renderer not found).");
      return false;
    }

    const React = findReact(webpackRequire);
    if (!React) {
      log("QAM integration unavailable (React not found).");
      return false;
    }

    for (const patch of patches) {
      const originalType = patch.originalType;

      function patchedType(...args) {
        const result = originalType.apply(this, args);
        patchTabsProducer(result, React);
        return result;
      }

      patch.patchedType = patchedType;
      patch.renderer.type = patchedType;
      logOnce("outerPatch", "QAM outer renderer patched.");
    }

    Object.assign(state, {
      installed: true,
      patches,
      nestedTypes: new Map(),
      install,
      uninstall,
    });

    log(`QAM hook installed (${patches.length} renderer variant(s)).`);
    return true;
  }

  function uninstall() {
    if (!state.installed) {
      log("uninstall() called but not installed; no-op.");
      return true;
    }

    for (const patch of state.patches) {
      // Only restore if nothing else re-patched the renderer after us.
      if (patch.renderer.type === patch.patchedType) {
        patch.renderer.type = patch.originalType;
        logOnce("outerRestore", "outer patch restored.");
      }
    }

    state.nestedTypes?.clear();

    Object.assign(state, {
      installed: false,
      patches: null,
      nestedTypes: null,
      install,
      uninstall,
    });

    log("QAM hook uninstalled.");
    logOnce("uninstall", "uninstall completed.");
    return true;
  }

  Object.assign(state, { install, uninstall });

  return install();
})();
