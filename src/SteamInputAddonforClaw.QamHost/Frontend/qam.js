/*
 * Steam Input Addon - native QAM tab injection.
 *
 * Independent implementation (does not use or port Decky Loader / Millennium code).
 * Evaluated once inside Steam's GamepadUI CEF context via CDP Runtime.evaluate.
 *
 * Responsibilities:
 *   1. Locate Steam's webpack module runtime.
 *   2. Find the module exporting the QAM renderer (signature: QuickAccessMenuBrowserView /
 *      QuickAccessMenuEmbedded).
 *   3. Wrap that renderer so its returned React element tree gains one extra tab, keyed so
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

  function findQamModule(webpackRequire) {
    const cache = webpackRequire.c || webpackRequire.m;
    if (!cache) return null;

    for (const key of Object.keys(cache)) {
      const mod = cache[key] && cache[key].exports;
      if (!mod) continue;

      const text = safeStringifyModule(mod);
      if (QAM_SIGNATURES.some((sig) => text.includes(sig))) {
        return { key, exports: mod };
      }
    }
    return null;
  }

  function safeStringifyModule(mod) {
    try {
      return Object.keys(mod).join(" ") + " " + String(mod.default || "");
    } catch (err) {
      return "";
    }
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
    return {
      [TAB_MARKER]: true,
      key: "steam-input-addon",
      title: "Steam Input Addon",
      icon: React.createElement("svg", { viewBox: "0 0 24 24", width: 24, height: 24 },
        React.createElement("circle", { cx: 12, cy: 12, r: 9, fill: "currentColor" })),
      content: React.createElement(
        "div",
        { style: { padding: "16px" } },
        React.createElement("h2", null, "Steam Input Addon"),
        React.createElement("p", null, "QAM integration test")
      ),
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

    const found = findQamModule(webpackRequire);
    if (!found) {
      log("QAM integration unavailable (renderer module not found).");
      return false;
    }

    const React = webpackRequire.__addonReact || findReact(webpackRequire);
    if (!React) {
      log("QAM integration unavailable (React not found).");
      return false;
    }

    const moduleExports = found.exports;
    const rendererKey = Object.keys(moduleExports).find((k) => typeof moduleExports[k] === "function");
    if (!rendererKey) {
      log("QAM integration unavailable (no renderer function on module).");
      return false;
    }

    const originalRenderer = moduleExports[rendererKey];

    function patchedRenderer(...args) {
      const result = originalRenderer.apply(this, args);
      const owner = findTabsPropOwner(result, 0);
      if (owner && !owner.props.tabs.some((t) => t && t[TAB_MARKER])) {
        owner.props.tabs = owner.props.tabs.concat([buildAddonTab(React)]);
      }
      return result;
    }

    moduleExports[rendererKey] = patchedRenderer;

    window[GLOBAL_KEY] = {
      installed: true,
      moduleExports,
      rendererKey,
      originalRenderer,
    };

    log("QAM hook installed.");
    return true;
  }

  function findReact(webpackRequire) {
    const cache = webpackRequire.c || webpackRequire.m;
    if (!cache) return null;
    for (const key of Object.keys(cache)) {
      const mod = cache[key] && cache[key].exports;
      if (mod && mod.createElement && mod.Component) {
        return mod;
      }
    }
    return null;
  }

  function uninstall() {
    const state = window[GLOBAL_KEY];
    if (!state || !state.installed) {
      log("uninstall() called but not installed; no-op.");
      return true;
    }

    state.moduleExports[state.rendererKey] = state.originalRenderer;
    window[GLOBAL_KEY] = { installed: false };
    log("QAM hook uninstalled.");
    return true;
  }

  window[GLOBAL_KEY] = window[GLOBAL_KEY] || { installed: false };
  window[GLOBAL_KEY].install = install;
  window[GLOBAL_KEY].uninstall = uninstall;

  install();
})();
