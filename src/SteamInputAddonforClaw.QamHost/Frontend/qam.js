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
  const BRIDGE_BINDING = "__steamInputAddonQamHost";
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

  // Purpose-built, bounded walker for the specific React node shapes Steam exposes for QAM: plain
  // React elements and Fiber-like nodes. Not a generic object
  // graph crawler -- it only descends through these four named links, with a visited set and a
  // hard node budget so it can never loop or blow up on a large/cyclic tree.
  const REACT_WALK_NODE_BUDGET = 4000;
  const REACT_WALK_KEYS = ["props", "children", "child", "sibling"];

  function findReactNode(root, predicate) {
    const visited = new Set();
    const stack = [root];
    let budget = REACT_WALK_NODE_BUDGET;

    while (stack.length > 0 && budget > 0) {
      const node = stack.pop();
      if (node == null || typeof node !== "object") continue;
      if (visited.has(node)) continue;
      visited.add(node);
      budget--;

      if (Array.isArray(node)) {
        for (let index = node.length - 1; index >= 0; index--) {
          stack.push(node[index]);
        }
        continue;
      }

      if (predicate(node)) {
        return { node, visited: visited.size, budgetExhausted: false };
      }

      for (let index = REACT_WALK_KEYS.length - 1; index >= 0; index--) {
        const next = node[REACT_WALK_KEYS[index]];
        if (next != null) stack.push(next);
      }
    }

    return { node: null, visited: visited.size, budgetExhausted: budget === 0 && stack.length > 0 };
  }

  function findTabsPropOwner(node) {
    return findReactNode(node, (candidate) =>
      candidate.props && Array.isArray(candidate.props.tabs));
  }

  function preservePatchedFunctionShape(patched, original) {
    Object.assign(patched, original);
    patched.toString = () => Function.prototype.toString.call(original);
    return patched;
  }

  // Resolves the concrete function to patch/invoke for a discovered producer node's `.type`.
  // Handles only the shapes actually observed for Steam QAM nodes: a plain function component,
  // or a memo/forwardRef-like object wrapper exposing `.render` or `.type` as a function. Anything
  // else is reported unsupported rather than guessed at.
  function resolveComponentTarget(type) {
    if (typeof type === "function") return { kind: "function", target: type };
    if (type && typeof type === "object") {
      if (typeof type.render === "function") return { kind: "object.render", target: type.render };
      if (typeof type.type === "function") return { kind: "object.type", target: type.type };
    }
    return null;
  }

  function patchTabsProducer(outerResult, React) {
    // Discovery signal: presence of the QAM lifecycle prop, nothing else. Component shape
    // (function vs. object wrapper) is handled separately below -- it is not part of discovery.
    const producerSearch = findReactNode(
      outerResult,
      (candidate) => candidate.props?.onFocusNavDeactivated != null
    );
    const node = producerSearch.node;
    if (!node) {
      // Review fix: distinguishes "outer renderer invoked, but the live tree did not contain the
      // expected nested producer shape" from every other silent stop below -- without this, a log
      // ending at "QAM outer renderer patched." is ambiguous between "never invoked" and "invoked
      // but nested producer missing".
      logOnce("nestedProducerMissing", `Nested tabs producer not found. Visited=${producerSearch.visited} BudgetExhausted=${producerSearch.budgetExhausted}`);
      return false;
    }

    const nodeType = node.type;
    const typeKind = typeof nodeType;
    logOnce("nestedProducerFound", `Nested tabs producer found. Type=${typeKind}`);
    if (typeKind === "object" && nodeType) {
      logOnce(
        "nestedProducerShape",
        `Nested tabs producer shape. hasType=${typeof nodeType.type === "function"} hasRender=${typeof nodeType.render === "function"} hasPrototypeRender=${typeof nodeType.prototype?.render === "function"}`
      );
    }

    const resolved = resolveComponentTarget(nodeType);
    if (!resolved) {
      logOnce("nestedProducerUnsupported", "Nested tabs producer found but component type is unsupported.");
      return false;
    }

    const originalTarget = resolved.target;
    state.nestedPatches ??= new Map();
    let record = state.nestedPatches.get(originalTarget);
    if (!record) {
      record = { node: null, originalType: null, patchedType: null, tabs: null };
      const patchedTarget = preservePatchedFunctionShape(function patchedTabsProducer(...args) {
        const result = originalTarget.apply(this, args);
        if (!state.installed) return result;
        // Review fix: proves the patched nested producer actually rendered live, separating
        // "never invoked" from "invoked but props.tabs owner missing" below.
        logOnce("nestedProducerInvoked", "Nested tabs producer invoked.");
        try {
          const ownerSearch = findTabsPropOwner(result);
          const owner = ownerSearch.node;
          if (!owner) {
            logOnce("tabsOwnerMissing", `props.tabs owner not found. Visited=${ownerSearch.visited} BudgetExhausted=${ownerSearch.budgetExhausted}`);
            return result;
          }
          record.tabs = owner.props.tabs;
          logOnce("tabsOwner", `tabs owner found. ExistingTabs=${owner.props.tabs.length}`);
          if (!owner.props.tabs.some((tab) => tab && tab[TAB_MARKER])) {
            owner.props.tabs.push(buildAddonTab(React));
            logOnce("tabInserted", "Steam Input Addon tab inserted.");
          } else {
            logOnce("duplicateTab", "Duplicate tab already present; insertion skipped.");
          }
        } catch (err) {
          logOnce("nestedAugmentationFailed", `QAM nested augmentation failed: ${String(err)}`);
        }
        return result;
      }, originalTarget);

      // Rebuild the patched `.type` in the same shape the original was found in, so React keeps
      // treating it as the same kind of type (function component vs. object wrapper).
      let patchedType;
      if (resolved.kind === "function") {
        patchedType = patchedTarget;
      } else if (resolved.kind === "object.render") {
        patchedType = Object.assign({}, nodeType, { render: patchedTarget });
      } else {
        patchedType = Object.assign({}, nodeType, { type: patchedTarget });
      }

      record.originalType = nodeType;
      record.patchedType = patchedType;
      state.nestedPatches.set(originalTarget, record);
    }

    record.node = node;
    if (node.type === record.originalType) {
      node.type = record.patchedType;
      logOnce("nestedPatch", "Nested tabs producer patched.");
    }
    return true;
  }

  function findReactRootFiber() {
    const root = document.getElementById("root");
    if (!root) return null;

    for (const key of Object.keys(root)) {
      if (!key.startsWith("__reactContainer$")) continue;
      const container = root[key];
      if (container) return container.current ?? container;
    }

    return root._reactRootContainer?._internalRoot?.current ?? null;
  }

  function patchExistingQamFibers(patches) {
    const rootFiber = findReactRootFiber();
    if (!rootFiber) {
      log("React root fiber was not found.");
      return;
    }

    const visited = new Set();
    const stack = [rootFiber];
    let count = 0;
    while (stack.length > 0) {
      const fiber = stack.pop();
      if (!fiber || visited.has(fiber)) continue;
      visited.add(fiber);

      for (const patch of patches) {
        if (fiber.elementType !== patch.renderer) continue;
        const record = {
          fiber,
          previousType: fiber.type,
          alternate: fiber.alternate,
          alternatePreviousType: fiber.alternate?.type,
          patchedType: patch.patchedType,
        };
        fiber.type = patch.patchedType;
        if (fiber.alternate) fiber.alternate.type = patch.patchedType;
        state.liveFibers.push(record);
        count++;
        break;
      }

      if (fiber.sibling) stack.push(fiber.sibling);
      if (fiber.child) stack.push(fiber.child);
    }

    if (count === 0) log("Existing QAM fiber was not found.");
    log(`Existing QAM fiber patch count=${count}.`);
  }

  /*
   * The nested producer can remain mounted in an already-created React tree after
   * the outer renderer is restored. Keep the wrapper inert and remove only our tab
   * before releasing the records.
   */
  function restoreNestedPatches() {
    for (const record of state.nestedPatches?.values() ?? []) {
      if (record.node?.type === record.patchedType) {
        record.node.type = record.originalType;
      }

      if (Array.isArray(record.tabs)) {
        for (let index = record.tabs.length - 1; index >= 0; index--) {
          if (record.tabs[index]?.[TAB_MARKER]) record.tabs.splice(index, 1);
        }
      }
      record.node = null;
      record.tabs = null;
    }
  }

  function restoreLiveFibers() {
    for (const record of state.liveFibers ?? []) {
      if (record.fiber.type === record.patchedType) record.fiber.type = record.previousType;
      if (record.alternate && record.alternate.type === record.patchedType) {
        record.alternate.type = record.alternatePreviousType;
      }
    }
    state.liveFibers = [];
  }

  function buildAddonTab(React) {
    const icon = React.createElement(
      "svg",
      { viewBox: "0 0 24 24", width: 24, height: 24 },
      React.createElement("path", { fill: "currentColor", d: "M7.3 8.1h9.4c1.7 0 3.1 1.1 3.6 2.7l1.1 3.6c.5 1.8-.8 3.6-2.6 3.6-.8 0-1.5-.3-2-.9l-1.7-1.8H8.9l-1.7 1.8c-.5.6-1.2.9-2 .9-1.8 0-3.1-1.8-2.6-3.6l1.1-3.6c.5-1.6 1.9-2.7 3.6-2.7Zm1.2 2.2v1.5H7v1.3h1.5v1.5h1.3v-1.5h1.5v-1.3H9.8v-1.5H8.5Zm7.1 1.2a.8.8 0 1 0 0 1.6.8.8 0 0 0 0-1.6Zm2.2 1.7a.8.8 0 1 0 0 1.6.8.8 0 0 0 0-1.6Z" })
    );

    const modes = [
      [0, "Disabled"], [1, "Enabled"], [2, "Aggressive"],
      [3, "Efficient Enabled"], [4, "Efficient Aggressive"],
      [5, "Aggressive at Guaranteed"], [6, "Efficient Aggressive at Guaranteed"],
    ];

    function CpuBoostPanel() {
      const [status, setStatus] = React.useState(null);
      const [cpu, setCpu] = React.useState(null);
      const [previewAc, setPreviewAc] = React.useState(null);
      const [previewDc, setPreviewDc] = React.useState(null);
      const [busy, setBusy] = React.useState(false);
      const [error, setError] = React.useState(null);
      const refreshInFlight = React.useRef(false);
      const refreshDirty = React.useRef(false);
      const settleTimers = React.useRef({ ac: null, dc: null });

      const refresh = React.useCallback(async () => {
        if (refreshInFlight.current) { refreshDirty.current = true; return; }
        refreshInFlight.current = true;
        try {
          const nextStatus = await request("captureStatus");
          const nextCpu = await request("captureCpuBoost");
          setStatus(nextStatus); setCpu(nextCpu); setPreviewAc(null); setPreviewDc(null); setError(null);
        } catch (_) { setError("QAM bridge unavailable"); }
        finally {
          refreshInFlight.current = false;
          if (refreshDirty.current) { refreshDirty.current = false; void refresh(); }
        }
      }, []);

      React.useEffect(() => { void refresh(); return () => {
        for (const key of ["ac", "dc"]) if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
      }; }, [refresh]);

      React.useEffect(() => {
        const previous = state.onStateInvalidated;
        state.onStateInvalidated = () => { previous?.(); void refresh(); };
        return () => { if (state.onStateInvalidated) state.onStateInvalidated = previous || null; };
      }, [refresh]);

      const unavailable = !status || status.steam?.appId !== 0 || !status.steam?.active || status.steam?.source !== 1;
      const writable = !!cpu && cpu.persistenceWritable && !unavailable && !busy;
      const sideValue = (side, preview) => preview ?? side?.desired ?? (side?.currentStatus === 0 ? side.current : null);
      const labelFor = value => modes.find(item => item[0] === value)?.[1] || "Unknown / unset";
      const scheduleMode = (side, value) => {
        const key = side === "ac" ? "ac" : "dc";
        side === "ac" ? setPreviewAc(value) : setPreviewDc(value);
        if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
        settleTimers.current[key] = setTimeout(async () => {
          if (!writable) return;
          setBusy(true); setError(null);
          try {
            const result = await request(side === "ac" ? "setDeviceCpuBoostAc" : "setDeviceCpuBoostDc", { mode: value });
            setCpu(result.snapshot); side === "ac" ? setPreviewAc(null) : setPreviewDc(null);
            if (!result.succeeded) setError(result.failureMessage || "CPU Boost update failed");
          } catch (_) { setError("CPU Boost update failed"); }
          finally { setBusy(false); }
        }, 250);
      };
      const setEnabled = async value => {
        if (!writable) return;
        setBusy(true); setError(null);
        try {
          const result = await request("setDeviceCpuBoostEnabled", { enabled: value });
          setCpu(result.snapshot); if (!result.succeeded) setError(result.failureMessage || "CPU Boost update failed");
        } catch (_) { setError("CPU Boost update failed"); }
        finally { setBusy(false); }
      };
      const slider = (title, side, value) => React.createElement("label", { style: { display: "block", marginTop: "14px" } },
        React.createElement("span", { style: { display: "block", marginBottom: "5px" } }, `${title}: ${labelFor(value)}`),
        React.createElement("input", { type: "range", min: 0, max: 6, step: 1, value: value == null ? 0 : value, disabled: !writable || value == null,
          "aria-label": title, onChange: event => scheduleMode(side, Number(event.target.value)) }));

      return React.createElement("div", { style: { padding: "18px", color: "white", fontFamily: "sans-serif", minWidth: "300px" } },
        React.createElement("h3", { style: { margin: "0 0 14px" } }, "CPU Boost"),
        unavailable ? React.createElement("p", null, status?.steam?.appId ? "Unavailable while a game is running" : "CPU Boost unavailable") : null,
        error ? React.createElement("p", { style: { color: "#ffb4ab" } }, error) : null,
        React.createElement("label", { style: { display: "flex", justifyContent: "space-between", alignItems: "center" } },
          React.createElement("span", null, "Enabled"),
          React.createElement("input", { type: "checkbox", checked: !!cpu?.enabled, disabled: !writable, "aria-label": "Enabled", onChange: event => void setEnabled(event.target.checked) })),
        slider("AC Mode", "ac", sideValue(cpu?.ac, previewAc)),
        slider("DC Mode", "dc", sideValue(cpu?.dc, previewDc)));
    }

    return {
      [TAB_MARKER]: true,
      key: "steam-input-addon",
      title: "Steam Input Addon",
      tab: icon,
      panel: React.createElement(CpuBoostPanel),
    };
  }

  // One stable, Addon-owned state object. install()/uninstall() mutate it in place rather than
  // replacing it, so the functions exposed on it below always remain callable.
  const state = window[GLOBAL_KEY] || (window[GLOBAL_KEY] = {});

  function request(method, payload) {
    return new Promise((resolve, reject) => {
      state.bridgePending ??= new Map();
      state.bridgeNextId = (state.bridgeNextId || 0) + 1;
      const id = state.bridgeNextId;
      state.bridgePending.set(id, { resolve, reject });
      try { window[BRIDGE_BINDING](JSON.stringify({ id, method, payload })); }
      catch (error) { state.bridgePending.delete(id); reject(new Error("QAM bridge unavailable")); }
    });
  }

  function receiveBridgeResponse(response) {
    const pending = state.bridgePending?.get(response.id);
    if (!pending) return;
    state.bridgePending.delete(response.id);
    response.ok ? pending.resolve(response.payload) : pending.reject(new Error(response.error || "QAM bridge request failed"));
  }

  function receiveBridgeNotification(kind) {
    if (kind === "state-invalidated") state.onStateInvalidated?.();
  }

  function retireBridgeConsumers() {
    for (const pending of state.bridgePending?.values() ?? []) {
      try { pending.reject(new Error("QAM bridge stopped")); } catch (_) {}
    }
    state.bridgePending?.clear();
    state.onStateInvalidated = null;
  }

  function install() {
    if (state.installed) {
      log("install() called but already installed; no-op.");
      return true;
    }

    state.diagnostics = {};

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

      const patchedType = preservePatchedFunctionShape(function patchedType(...args) {
        const result = originalType.apply(this, args);
        if (!state.installed) return result;
        // Review fix: proves the patched outer renderer actually ran on live Steam, separating
        // "never invoked" from every failure mode further down the augmentation chain.
        logOnce("outerRendererInvoked", "QAM outer renderer invoked.");
        try {
          patchTabsProducer(result, React);
        } catch (err) {
          logOnce("outerAugmentationFailed", `QAM outer augmentation failed: ${String(err)}`);
        }
        return result;
      }, originalType);

      patch.patchedType = patchedType;
      patch.renderer.type = patchedType;
      logOnce("outerPatch", "QAM outer renderer patched.");
    }

    state.liveFibers = [];
    patchExistingQamFibers(patches);

    Object.assign(state, {
      installed: true,
      patches,
      nestedPatches: new Map(),
      install,
      uninstall,
    });

    log(`QAM hook installed (${patches.length} renderer variant(s)).`);
    return true;
  }

  function uninstall() {
    retireBridgeConsumers();
    if (!state.installed) {
      log("uninstall() called but not installed; no-op.");
      return true;
    }

    state.installed = false;
    restoreNestedPatches();
    restoreLiveFibers();

    for (const patch of state.patches) {
      // Only restore if nothing else re-patched the renderer after us.
      if (patch.renderer.type === patch.patchedType) {
        patch.renderer.type = patch.originalType;
        logOnce("outerRestore", "outer patch restored.");
      }
    }

    Object.assign(state, {
      patches: null,
      nestedPatches: null,
      install,
      uninstall,
    });

    log("QAM hook uninstalled.");
    logOnce("uninstall", "uninstall completed.");
    return true;
  }

  Object.assign(state, { install, uninstall, request, __receiveBridgeResponse: receiveBridgeResponse, __receiveBridgeNotification: receiveBridgeNotification });

  return install();
})();
