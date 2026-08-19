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
        return capturedRequire;
      }
    }
    return null;
  }

  // Finds every React element whose `.type` render function is one of Steam's QAM renderer
  // variants (QuickAccessMenuBrowserView / QuickAccessMenuEmbedded), by matching the signature
  // strings against the function's own source (not the module's export names). Both variants are
  // patched because enumeration order does not tell us which one the current Steam build renders.
  function findQamRenderers(webpackRequire) {
    const cache = webpackRequire.c;
    if (!cache) return [];

    const matches = [];
    for (const moduleRecord of Object.values(cache)) {
      const moduleExports = moduleRecord && moduleRecord.exports;
      if (!moduleExports) continue;

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
    const cache = webpackRequire.c;
    if (!cache) return null;
    for (const moduleRecord of Object.values(cache)) {
      const mod = moduleRecord && moduleRecord.exports;
      if (mod && mod.createElement && mod.Component) {
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
        if (owner && !owner.props.tabs.some((tab) => tab && tab[TAB_MARKER])) {
          owner.props.tabs.push(buildAddonTab(React));
        }
        return result;
      };

      if (!state.nestedTypes) state.nestedTypes = new Map();
      state.nestedTypes.set(originalType, patchedType);
    }

    if (node.type === originalType) {
      node.type = patchedType;
    }
    if (!state.nestedPatches) state.nestedPatches = [];
    if (!state.nestedPatches.some((patch) => patch.node === node)) {
      state.nestedPatches.push({ node, originalType, patchedType });
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
    }

    Object.assign(state, {
      installed: true,
      patches,
      nestedTypes: new Map(),
      nestedPatches: [],
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
      }
    }

    for (const patch of state.nestedPatches || []) {
      if (patch.node.type === patch.patchedType) {
        patch.node.type = patch.originalType;
      }
    }

    Object.assign(state, {
      installed: false,
      patches: null,
      nestedTypes: null,
      nestedPatches: null,
      install,
      uninstall,
    });

    log("QAM hook uninstalled.");
    return true;
  }

  Object.assign(state, { install, uninstall });

  return install();
})();
