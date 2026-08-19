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

  // Finds the React element whose `.type` render function is Steam's QAM renderer, by matching
  // the signature strings against the function's own source (not the module's export names).
  function findQamRenderer(webpackRequire) {
    const cache = webpackRequire.c;
    if (!cache) return null;

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
          return { renderer: candidate, originalType: render };
        }
      }
    }
    return null;
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

  function findTabsPropOwner(element, depth) {
    if (!element || typeof element !== "object" || depth > 12) return null;

    if (element.props && Array.isArray(element.props.tabs)) {
      return element;
    }

    const children = element.props && element.props.children;
    const candidates = Array.isArray(children) ? children : [children];
    for (const child of candidates) {
      const found = findTabsPropOwner(child, depth + 1);
      if (found) return found;
    }
    return null;
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

  function install() {
    const state = window[GLOBAL_KEY];
    if (state && state.installed) {
      log("install() called but already installed; no-op.");
      return true;
    }

    const webpackRequire = findWebpackRequire();
    if (!webpackRequire) {
      log("QAM integration unavailable (webpack runtime not found).");
      return false;
    }

    const found = findQamRenderer(webpackRequire);
    if (!found) {
      log("QAM integration unavailable (renderer not found).");
      return false;
    }

    const React = findReact(webpackRequire);
    if (!React) {
      log("QAM integration unavailable (React not found).");
      return false;
    }

    const { renderer, originalType } = found;

    function patchedType(...args) {
      const result = originalType.apply(this, args);
      const owner = findTabsPropOwner(result, 0);
      if (owner && !owner.props.tabs.some((t) => t && t[TAB_MARKER])) {
        owner.props.tabs = owner.props.tabs.concat([buildAddonTab(React)]);
      }
      return result;
    }

    renderer.type = patchedType;

    window[GLOBAL_KEY] = {
      installed: true,
      renderer,
      originalType,
    };

    log("QAM hook installed.");
    return true;
  }

  function uninstall() {
    const state = window[GLOBAL_KEY];
    if (!state || !state.installed) {
      log("uninstall() called but not installed; no-op.");
      return true;
    }

    state.renderer.type = state.originalType;
    window[GLOBAL_KEY] = { installed: false };
    log("QAM hook uninstalled.");
    return true;
  }

  window[GLOBAL_KEY] = window[GLOBAL_KEY] || { installed: false };
  window[GLOBAL_KEY].install = install;
  window[GLOBAL_KEY].uninstall = uninstall;

  return install();
})();
