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
    let loadFailures = 0;

    for (const id of Object.keys(webpackRequire.m || {})) {
      try {
        const module = webpackRequire(id);
        if (module) modules.push(module);
      } catch (err) {
        loadFailures++;
      }
    }

    logOnce("moduleDiscovery", `webpack modules: registered=${Object.keys(webpackRequire.m || {}).length} loaded=${modules.length} loadFailures=${loadFailures}`);

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

  function isCommonUiModule(candidate) {
    if (!candidate || typeof candidate !== "object") return false;
    for (const prop in candidate) {
      if (candidate[prop]?.contextType?._currentValue && Object.keys(candidate).length > 60) return true;
    }
    return false;
  }

  function findCommonUiModule(modules) {
    for (const module of modules) {
      if (module?.default && isCommonUiModule(module.default)) {
        logOnce("commonUi", `Steam CommonUIModule resolved. Exports=${Object.keys(module.default).length} Source=default`);
        return module.default;
      }
      if (isCommonUiModule(module)) {
        logOnce("commonUi", `Steam CommonUIModule resolved. Exports=${Object.keys(module).length} Source=root`);
        return module;
      }
    }
    logOnce("commonUi", "Steam CommonUIModule unavailable.");
    return null;
  }

  function findToggleField(commonUiModule) {
    if (!commonUiModule) return null;
    for (const candidate of Object.values(commonUiModule)) {
      const source = candidate?.render?.toString?.();
      if (source?.includes("ToggleField,fallback") || source?.includes('ToggleField",')) {
        logOnce("native-ToggleField", "QAM native ToggleField resolved.");
        return candidate;
      }
    }
    logOnce("native-ToggleField", "QAM native ToggleField unavailable.");
    return null;
  }

  function findSliderField(commonUiModule) {
    if (!commonUiModule) return null;
    for (const candidate of Object.values(commonUiModule)) {
      const source = candidate?.toString?.();
      if (source?.includes("SliderField,fallback") || source?.includes('SliderField",')) {
        logOnce("native-SliderField", "QAM native SliderField resolved.");
        return candidate;
      }
    }
    logOnce("native-SliderField", "QAM native SliderField unavailable.");
    return null;
  }

  function findPanelComponents(modules) {
    for (const module of modules) {
      let defaultCandidate = null;
      try { defaultCandidate = module?.default ?? null; } catch (_) { }
      for (const candidate of [defaultCandidate, module]) {
        if (!candidate || typeof candidate !== "object" || candidate === window) continue;
        let panelSection = null;
        for (const exportName of Object.keys(candidate)) {
          let value;
          try { value = candidate[exportName]; } catch (_) { continue; }
          if (!value) continue;
          let source;
          try { source = value?.toString?.(); } catch (_) { continue; }
          if (source?.includes(".PanelSection")) {
            panelSection = value;
            break;
          }
        }
        if (!panelSection) continue;
        let panelSectionRow = null;
        for (const exportName of Object.keys(candidate)) {
          let value;
          try { value = candidate[exportName]; } catch (_) { continue; }
          if (!value || value === panelSection) continue;
          let source;
          try { source = value?.toString?.(); } catch (_) { continue; }
          if (!source?.includes(".PanelSection")) {
            panelSectionRow = value;
            break;
          }
        }
        if (panelSectionRow) {
          logOnce("native-panel", "QAM native PanelSection and PanelSectionRow resolved.");
          return { PanelSection: panelSection, PanelSectionRow: panelSectionRow };
        }
      }
    }
    logOnce("native-panel", "QAM native PanelSection/PanelSectionRow unavailable.");
    return null;
  }

  function isSteamClassModule(candidate) {
    if (!candidate || typeof candidate !== "object" || candidate.__esModule) return false;
    const keys = Object.keys(candidate);
    return keys.length > 0 && keys.every(key => {
      const descriptor = Object.getOwnPropertyDescriptor(candidate, key);
      return !descriptor?.get && typeof candidate[key] === "string";
    });
  }

  function findNativeClassStyles(modules) {
    const classModules = [];
    for (const module of modules) {
      for (const candidate of [module?.default, module]) {
        if (isSteamClassModule(candidate)) classModules.push(candidate);
      }
    }
    const qam = classModules.find(candidate => candidate.Title && candidate.QuickAccessMenu && candidate.BatteryDetailsLabels);
    const field = classModules.find(candidate => candidate.FieldLabelRow && candidate.FieldLabel && candidate.FieldLabelValue);
    if (!qam?.Title || !field) {
      logOnce("native-styles", "QAM native title/slider class styles unavailable.");
      return null;
    }
    logOnce("native-styles", "QAM native title/slider class styles resolved.");
    return {
      QamTitleClass: qam.Title,
      FieldLabelRowClass: field.FieldLabelRow,
      FieldLabelClass: field.FieldLabel,
      FieldLabelValueClass: field.FieldLabelValue,
    };
  }

  function findNativeQamComponents(webpackRequire) {
    const modules = collectSearchableModules(webpackRequire);
    const commonUiModule = findCommonUiModule(modules);
    if (!commonUiModule) return null;
    const components = {
      ToggleField: findToggleField(commonUiModule),
      SliderField: findSliderField(commonUiModule),
      ...findPanelComponents(modules),
      ...findNativeClassStyles(modules),
    };
    if (!components.ToggleField || !components.SliderField || !components.PanelSection || !components.PanelSectionRow || !components.QamTitleClass) {
      logOnce("nativeControls", "QAM required native controls/layout unavailable; Addon tab is disabled.");
      return null;
    }
    logOnce("nativeControls", "QAM native ToggleField and SliderField resolved.");
    return components;
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

  function patchTabsProducer(outerResult, React, native) {
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
            owner.props.tabs.push(buildAddonTab(React, native));
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

  function buildAddonTab(React, native) {
    if (state.addonTabDescriptor) return state.addonTabDescriptor;

    const icon = React.createElement(
      "svg",
      { viewBox: "0 0 24 24", width: 24, height: 24, fill: "currentColor" },
      React.createElement("path", { d: "M5.1 7.1C3.2 7.7 2.2 9.7 1.6 12.1l-1 4.1c-.4 1.8.7 3.4 2.5 3.4 1 0 1.9-.5 2.4-1.3l1.4-2.1h9.9l1.4 2.1c.5.8 1.4 1.3 2.4 1.3 1.8 0 2.9-1.6 2.5-3.4l-1-4.1c-.6-2.4-1.6-4.4-3.5-5-1.1-.4-2.8-.5-4.2-.5h-2.7c-1.4 0-3.1.1-4.2.5Z" })
    );

    const modes = [
      [0, "Disabled"], [1, "Enabled"], [2, "Aggressive"],
      [3, "Efficient Enabled"], [4, "Efficient Aggressive"],
      [5, "Aggressive At Guaranteed"], [6, "Efficient Aggressive At Guaranteed"],
    ];

    function CpuBoostPanel() {
      const [status, setStatus] = React.useState(null);
      const [cpu, setCpu] = React.useState(null);
      const [powerMode, setPowerMode] = React.useState(null);
      const [tdp, setTdp] = React.useState(null);
      const [profile, setProfile] = React.useState(null);
      const [profileTdpDraft, setProfileTdpDraft] = React.useState(null);
      const profileTdpDraftRef = React.useRef(null);
      const profileTdpTimer = React.useRef(null);
      const profileTdpGeneration = React.useRef(0);
      const activeProfileAppIdRef = React.useRef(0);
      const [tdpDraft, setTdpDraft] = React.useState(null);
      const [previewAc, setPreviewAc] = React.useState(null);
      const [previewDc, setPreviewDc] = React.useState(null);
      const [busy, setBusy] = React.useState(false);
      const [error, setError] = React.useState(null);
      const refreshInFlight = React.useRef(false);
      const refreshDirty = React.useRef(false);
      const settleTimers = React.useRef({ ac: null, dc: null });
      const tdpTimer = React.useRef(null);
      const tdpDraftRef = React.useRef(null);
      const tdpWritableRef = React.useRef(false);
      const tdpEditGeneration = React.useRef(0);
      const modeWritableRef = React.useRef(false);
      const mutationDepthRef = React.useRef(0);
      const deferredInvalidationRef = React.useRef(false);
      const modeEditGeneration = React.useRef({ ac: 0, dc: 0 });
      const modeMutationInFlight = React.useRef({ ac: false, dc: false });
      const powerModeLabels = ["Best power efficiency", "Balanced", "Best performance"];
      const powerModeNames = ["BestPowerEfficiency", "Balanced", "BestPerformance"];
      const powerModeIndex = value => typeof value === "number" ? value : powerModeNames.indexOf(value);
      const powerModeValue = value => Number(value);

      const failClosed = React.useCallback(message => {
        for (const key of ["ac", "dc"]) {
          if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
          settleTimers.current[key] = null;
        }
        if (tdpTimer.current) clearTimeout(tdpTimer.current);
        tdpTimer.current = null;
        tdpEditGeneration.current = 0;
        if (profileTdpTimer.current) clearTimeout(profileTdpTimer.current);
        profileTdpTimer.current = null;
        profileTdpGeneration.current = 0;
        activeProfileAppIdRef.current = 0;
          setStatus(null); setCpu(null); setPowerMode(null); setTdp(null); setProfile(null); profileTdpDraftRef.current = null; setProfileTdpDraft(null); setTdpDraft(null); tdpDraftRef.current = null; setPreviewAc(null); setPreviewDc(null); setError(message);
      }, []);

      const refresh = React.useCallback(async () => {
        if (refreshInFlight.current) { refreshDirty.current = true; return; }
        refreshInFlight.current = true;
        try {
          const nextStatus = await request("captureStatus");
          const nextProfile = await request("captureActiveGameProfile");
          const nextAppId = Number(nextProfile?.appId || 0);
          if (activeProfileAppIdRef.current !== nextAppId) {
            if (profileTdpTimer.current) clearTimeout(profileTdpTimer.current);
            profileTdpTimer.current = null;
            profileTdpGeneration.current = 0;
            profileTdpDraftRef.current = null;
            setProfileTdpDraft(null);
          }
          activeProfileAppIdRef.current = nextAppId;
          const activeGame = nextAppId > 0;
          const nextCpu = activeGame ? null : await request("captureCpuBoost");
          const nextPowerMode = activeGame ? null : await request("capturePowerMode");
          const nextTdp = activeGame ? null : await request("captureTdp");
          const nextDraft = nextTdp?.configuration ? {
            enabled: nextTdp.configuration.enabled,
            ac: { ...nextTdp.configuration.ac },
            dc: { ...nextTdp.configuration.dc },
          } : null;
          setStatus(nextStatus); setCpu(nextCpu); setPowerMode(nextPowerMode); setTdp(nextTdp); setProfile(nextProfile);
          if (nextProfile?.tdp && profileTdpGeneration.current === 0) {
            const nextProfileDraft = { ac: { ...nextProfile.tdp.ac }, dc: { ...nextProfile.tdp.dc } };
            profileTdpDraftRef.current = nextProfileDraft;
            setProfileTdpDraft(nextProfileDraft);
          }
          if (tdpEditGeneration.current === 0) { setTdpDraft(nextDraft); tdpDraftRef.current = nextDraft; }
          setPreviewAc(null); setPreviewDc(null); setError(null);
        } catch (_) { failClosed("QAM bridge unavailable"); }
        finally {
          refreshInFlight.current = false;
          if (refreshDirty.current) { refreshDirty.current = false; void refresh(); }
        }
      }, [failClosed]);

      const cancelModeTimers = React.useCallback(() => {
        for (const key of ["ac", "dc"]) {
          if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
          settleTimers.current[key] = null;
        }
      }, []);

      const beginMutation = React.useCallback(() => { mutationDepthRef.current++; }, []);
      const endMutation = React.useCallback(() => {
        mutationDepthRef.current = Math.max(0, mutationDepthRef.current - 1);
        const modeEditPending = Object.values(settleTimers.current).some(Boolean) || Object.values(modeMutationInFlight.current).some(Boolean);
        if (mutationDepthRef.current === 0 && deferredInvalidationRef.current && !modeEditPending) {
          deferredInvalidationRef.current = false;
          cancelModeTimers();
          setPreviewAc(null); setPreviewDc(null);
          void refresh();
        }
      }, [cancelModeTimers, refresh]);
      const runPowerMutation = React.useCallback(async (method, payload) => {
        if (!state.installed) return;
        try { beginMutation(); setError(null); const result = await request(method, payload); if (!result?.succeeded) setError(result?.failureMessage || "Power Mode update failed"); await refresh(); }
        catch (_) { failClosed("Power Mode update failed"); }
        finally { endMutation(); }
      }, [beginMutation, endMutation, failClosed, refresh]);

      React.useEffect(() => { void refresh(); return cancelModeTimers; }, [refresh, cancelModeTimers]);

      React.useEffect(() => {
        const previous = state.onStateInvalidated;
        const handler = () => {
          previous?.();
          if (mutationDepthRef.current > 0) {
            deferredInvalidationRef.current = true;
            return;
          }
          cancelModeTimers();
          // Keep a dirty TDP draft's debounce alive across invalidation; DevicePage does the same.
          setPreviewAc(null); setPreviewDc(null);
          void refresh();
        };
        state.onStateInvalidated = handler;
        return () => { if (state.onStateInvalidated === handler) state.onStateInvalidated = previous || null; };
      }, [refresh, cancelModeTimers]);

      const unavailable = !status || status.steam?.appId !== 0 || !status.steam?.active || status.steam?.source !== 1;
      const activeProfile = Number(profile?.appId || 0) > 0;
      const mutationAvailable = !!cpu && cpu.persistenceWritable && !unavailable && !busy;
      const modeWritable = mutationAvailable && cpu.enabled;
      const tdpMutationAvailable = !!tdp && tdp.available && tdp.persistenceWritable && !unavailable && !busy;
      tdpWritableRef.current = tdpMutationAvailable;
      modeWritableRef.current = modeWritable;
      const snapshotMessage = !cpu ? null : !cpu.persistenceWritable
        ? "CPU Boost settings could not be loaded, so changes are disabled."
        : cpu.lastFailure ? `The last CPU Boost change could not be applied to Windows: ${cpu.lastFailure}` : null;
      const displayError = error || snapshotMessage;
      const sideValue = (side, preview) => preview ?? side?.desired ?? (side?.currentStatus === 0 ? side.current : null);
      const labelFor = value => modes.find(item => item[0] === value)?.[1] || "Unknown / unset";
      const scheduleMode = (side, value) => {
        if (!state.installed || !modeWritableRef.current) return;
        const key = side === "ac" ? "ac" : "dc";
        const generation = ++modeEditGeneration.current[key];
        side === "ac" ? setPreviewAc(value) : setPreviewDc(value);
        if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
        settleTimers.current[key] = setTimeout(async () => {
          settleTimers.current[key] = null;
          if (!state.installed || !modeWritableRef.current) return;
          setError(null);
          try {
            modeMutationInFlight.current[key] = true;
            beginMutation();
            const result = await request(side === "ac" ? "setDeviceCpuBoostAc" : "setDeviceCpuBoostDc", { mode: value });
            if (generation === modeEditGeneration.current[key]) {
              setCpu(result.snapshot); side === "ac" ? setPreviewAc(null) : setPreviewDc(null);
            }
            if (!result.succeeded) setError(result.failureMessage || "CPU Boost update failed");
          } catch (_) { failClosed("CPU Boost update failed"); }
          finally { modeMutationInFlight.current[key] = false; endMutation(); }
        }, 250);
      };
      const setEnabled = async value => {
        cancelModeTimers();
        setPreviewAc(null); setPreviewDc(null);
        if (!state.installed) return;
        if (!mutationAvailable) return;
        setBusy(true); setError(null);
        try {
          beginMutation();
          const result = await request("setDeviceCpuBoostEnabled", { enabled: value });
          setCpu(result.snapshot); if (!result.succeeded) setError(result.failureMessage || "CPU Boost update failed");
        } catch (_) { failClosed("CPU Boost update failed"); }
        finally { endMutation(); setBusy(false); }
      };
      const tdpLimits = tdp?.limits;
      const adjustTdpPair = (pl1WasEdited, pl1, pl2, limits = tdpLimits) => {
        if (!limits) return { pl1Watts: pl1, pl2Watts: pl2 };
        const gap = limits.pl1MinimumWatts === 8 && limits.pl1MaximumWatts === 30 && limits.pl2MinimumWatts === 8 && limits.pl2MaximumWatts === 37
          ? 1
          : limits.pl1MinimumWatts === 8 && limits.pl1MaximumWatts === 35 && limits.pl2MinimumWatts === 8 && limits.pl2MaximumWatts === 45 ? 2 : 0;
        if (!gap || pl1 == null || pl2 == null) return { pl1Watts: pl1, pl2Watts: pl2 };
        if (pl1WasEdited && pl2 < pl1 + gap) return pl1 + gap <= limits.pl2MaximumWatts ? { pl1Watts: pl1, pl2Watts: pl1 + gap } : { pl1Watts: limits.pl2MaximumWatts - gap, pl2Watts: pl2 };
        if (!pl1WasEdited && pl1 > pl2 - gap) return pl2 - gap >= limits.pl1MinimumWatts ? { pl1Watts: pl2 - gap, pl2Watts: pl2 } : { pl1Watts: limits.pl1MinimumWatts, pl2Watts: limits.pl1MinimumWatts + gap };
        return { pl1Watts: pl1, pl2Watts: pl2 };
      };
      const submitTdpDraft = async (draft, generation) => {
        if (!state.installed || !tdpWritableRef.current || !draft) return;
        setError(null);
        try {
          beginMutation();
          const result = await request("setDeviceTdp", { configuration: draft });
          if (generation === tdpEditGeneration.current) {
            tdpEditGeneration.current = 0;
            setTdp(result.snapshot); setTdpDraft(result.snapshot.configuration); tdpDraftRef.current = result.snapshot.configuration;
          }
          if (!result.succeeded) setError(result.failureMessage || "TDP update failed");
        } catch (_) { failClosed("TDP update failed"); }
        finally { endMutation(); }
      };
      const scheduleTdp = (nextDraft) => {
        if (!tdpWritableRef.current) return;
        const generation = ++tdpEditGeneration.current;
        tdpDraftRef.current = nextDraft; setTdpDraft(nextDraft);
        if (tdpTimer.current) clearTimeout(tdpTimer.current);
        tdpTimer.current = setTimeout(() => { tdpTimer.current = null; void submitTdpDraft(nextDraft, generation); }, 300);
      };
      const setTdpEnabled = async enabled => {
        if (!state.installed || !tdpMutationAvailable) return;
        if (tdpTimer.current) clearTimeout(tdpTimer.current);
        tdpTimer.current = null; tdpEditGeneration.current = 0; setBusy(true); setError(null);
        try {
          beginMutation();
          const result = await request("setDeviceTdpEnabled", { enabled });
          setTdp(result.snapshot); setTdpDraft(result.snapshot.configuration); tdpDraftRef.current = result.snapshot.configuration;
          if (!result.succeeded) setError(result.failureMessage || "TDP update failed");
        } catch (_) { failClosed("TDP update failed"); }
        finally { endMutation(); setBusy(false); }
      };
      const tdpSlider = (label, source, limit, value, separator) => value == null || !limit ? null : React.createElement(native.SliderField, {
        label,
        min: label === "PL1" ? limit.pl1MinimumWatts : limit.pl2MinimumWatts,
        max: label === "PL1" ? limit.pl2MaximumWatts : limit.pl2MaximumWatts,
        step: 1,
        value,
        disabled: !tdpMutationAvailable || !tdpDraft?.enabled,
        showValue: true,
        bottomSeparator: separator,
        onChange: next => {
          const draft = tdpDraftRef.current;
          if (!draft) return;
          const pair = { ...draft[source] };
          let numeric = Number(next);
          if (label === "PL1") numeric = Math.min(numeric, limit.pl1MaximumWatts);
          if (label === "PL1") pair.pl1Watts = numeric; else pair.pl2Watts = numeric;
          const adjusted = adjustTdpPair(label === "PL1", pair.pl1Watts, pair.pl2Watts);
          scheduleTdp({ ...draft, [source]: { pl1Watts: adjusted.pl1Watts, pl2Watts: adjusted.pl2Watts } });
        },
      });
      const slider = (title, side, value, bottomSeparator) => value == null ? null : React.createElement(native.SliderField, {
        label: React.createElement(React.Fragment, null,
          React.createElement("div", { className: native.FieldLabelRowClass },
            React.createElement("span", { className: native.FieldLabelClass }, title),
            React.createElement("span", { className: native.FieldLabelValueClass }, labelFor(value)))),
        min: 0,
        max: 6,
        step: 1,
        value,
        notchCount: modes.length,
        disabled: !modeWritable,
        notchTicksVisible: true,
        bottomSeparator,
        onChange: next => scheduleMode(side, Number(next)),
      });

      const controls = [{ key: "cpu-toggle", node: React.createElement(native.ToggleField, {
          label: "CPU Boost",
          checked: !!cpu?.enabled,
          disabled: !mutationAvailable,
          bottomSeparator: cpu?.enabled ? "none" : "standard",
          onChange: value => void setEnabled(!!value),
        }) }];
      if (cpu?.enabled) {
        controls.push({ key: "cpu-plugged-in", node: slider("Plugged in", "ac", sideValue(cpu.ac, previewAc), "none") });
        controls.push({ key: "cpu-on-battery", node: slider("On battery", "dc", sideValue(cpu.dc, previewDc), "standard") });
      }

      const powerWritable = !!powerMode?.persistenceWritable && !status?.steam?.appId && !busy;
      const powerSlider = (label, value, onChange, disabled) => value == null ? null : React.createElement(native.SliderField, { label: React.createElement(React.Fragment, null, React.createElement("div", { className: native.FieldLabelRowClass }, React.createElement("span", { className: native.FieldLabelClass }, label), React.createElement("span", { className: native.FieldLabelValueClass }, powerModeLabels[powerModeIndex(value)] ?? "Unknown"))), min: 0, max: 2, step: 1, value: powerModeIndex(value), notchCount: 3, notchTicksVisible: true, disabled, onChange: next => onChange(Number(next)) });
      const powerControls = [{ key: "power-toggle", node: React.createElement(native.ToggleField, { label: "Windows Power Mode", checked: !!powerMode?.enabled, disabled: !powerWritable, onChange: value => void runPowerMutation("setDevicePowerModeEnabled", { enabled: !!value }) }) }];
      if (powerMode?.enabled) { powerControls.push({ key: "power-ac", node: powerSlider("Plugged in", powerMode.ac?.desired ?? powerMode.ac?.current, value => void runPowerMutation("setDevicePowerModeAc", { mode: powerModeValue(value) }), !powerWritable) }); powerControls.push({ key: "power-dc", node: powerSlider("On battery", powerMode.dc?.desired ?? powerMode.dc?.current, value => void runPowerMutation("setDevicePowerModeDc", { mode: powerModeValue(value) }), !powerWritable) }); }

      const tdpControls = [{ key: "tdp-toggle", node: React.createElement(native.ToggleField, {
        label: "TDP Control",
        checked: !!tdpDraft?.enabled,
        disabled: !tdpMutationAvailable,
        onChange: value => void setTdpEnabled(!!value),
      }) }];
      if (tdpDraft?.enabled && tdpLimits) {
        tdpControls.push({ key: "tdp-ac-heading", node: React.createElement("div", null, "Plugged in") });
        tdpControls.push({ key: "tdp-ac-pl1", node: tdpSlider("PL1", "ac", tdpLimits, tdpDraft.ac?.pl1Watts, "none") });
        tdpControls.push({ key: "tdp-ac-pl2", compact: true, node: tdpSlider("PL2", "ac", tdpLimits, tdpDraft.ac?.pl2Watts, "none") });
        tdpControls.push({ key: "tdp-dc-heading", node: React.createElement("div", null, "On battery") });
        tdpControls.push({ key: "tdp-dc-pl1", node: tdpSlider("PL1", "dc", tdpLimits, tdpDraft.dc?.pl1Watts, "none") });
        tdpControls.push({ key: "tdp-dc-pl2", compact: true, node: tdpSlider("PL2", "dc", tdpLimits, tdpDraft.dc?.pl2Watts, "standard") });
      }

      if (activeProfile) {
        const writable = profile.persistenceWritable && !busy;
        const enabled = !!profile.enabled;
        const scheduleProfileMode = (side, value) => {
          if (!state.installed || !profile.persistenceWritable || !enabled) return;
          const key = side === "ac" ? "ac" : "dc";
          const generation = ++modeEditGeneration.current[key];
          side === "ac" ? setPreviewAc(value) : setPreviewDc(value);
          if (settleTimers.current[key]) clearTimeout(settleTimers.current[key]);
          settleTimers.current[key] = setTimeout(async () => {
            settleTimers.current[key] = null;
            if (!state.installed || !profile.persistenceWritable || !profile.enabled) return;
            try {
              modeMutationInFlight.current[key] = true; beginMutation();
              const result = await request(side === "ac" ? "setActiveGameCpuBoostAc" : "setActiveGameCpuBoostDc", { mode: value });
              if (generation === modeEditGeneration.current[key]) {
                setProfile(result.snapshot); side === "ac" ? setPreviewAc(null) : setPreviewDc(null);
              }
              if (!result.succeeded) setError(result.failureMessage || "CPU Boost update failed");
            } catch (_) { failClosed("CPU Boost update failed"); }
            finally { modeMutationInFlight.current[key] = false; endMutation(); }
          }, 250);
        };
        const toggleProfile = async value => {
          if (!state.installed || !writable) return;
          cancelModeTimers();
          if (profileTdpTimer.current) clearTimeout(profileTdpTimer.current);
          profileTdpTimer.current = null;
          profileTdpGeneration.current = 0;
          setPreviewAc(null); setPreviewDc(null);
          setBusy(true); setError(null);
          try {
            beginMutation();
            const result = await request("setActiveGameProfileEnabled", { enabled: !!value, displayName: profile.displayName });
            setProfile(result.snapshot);
            const nextDraft = result.snapshot.tdp ? { ac: { ...result.snapshot.tdp.ac }, dc: { ...result.snapshot.tdp.dc } } : null;
            profileTdpDraftRef.current = nextDraft; setProfileTdpDraft(nextDraft);
            if (!result.succeeded) setError(result.failureMessage || "Profile update failed");
          } catch (_) { failClosed("Profile update failed"); }
          finally { endMutation(); setBusy(false); }
        };
        const scheduleProfileTdp = draft => {
          if (!state.installed || !profile.persistenceWritable || !enabled || !profile.limits) return;
          profileTdpDraftRef.current = draft;
          setProfileTdpDraft(draft);
          const generation = ++profileTdpGeneration.current;
          if (profileTdpTimer.current) clearTimeout(profileTdpTimer.current);
          profileTdpTimer.current = setTimeout(async () => {
            profileTdpTimer.current = null;
            if (!state.installed || !profile.persistenceWritable || !profile.enabled) return;
            try {
              beginMutation();
              const result = await request("setActiveGameTdp", { configuration: draft });
              if (generation === profileTdpGeneration.current) { profileTdpGeneration.current = 0; setProfile(result.snapshot); const nextDraft = { ac: { ...result.snapshot.tdp.ac }, dc: { ...result.snapshot.tdp.dc } }; profileTdpDraftRef.current = nextDraft; setProfileTdpDraft(nextDraft); }
              if (!result.succeeded) setError(result.failureMessage || "TDP update failed");
            } catch (_) { failClosed("TDP update failed"); }
            finally { endMutation(); }
          }, 300);
        };
        const profileSlider = (label, side, value, preview, separator) => value == null ? null : React.createElement(native.SliderField, {
          label: React.createElement(React.Fragment, null,
            React.createElement("div", { className: native.FieldLabelRowClass },
              React.createElement("span", { className: native.FieldLabelClass }, label),
              React.createElement("span", { className: native.FieldLabelValueClass }, labelFor(preview ?? value)))),
          min: 0, max: 6, step: 1, value: preview ?? value, notchCount: modes.length,
          disabled: !profile.persistenceWritable || !enabled || busy, notchTicksVisible: true, bottomSeparator: separator,
          onChange: next => scheduleProfileMode(side, Number(next)),
        });
        const profileTdpSlider = (label, side, value, separator) => value == null || !profile.limits ? null : React.createElement(native.SliderField, {
          label, min: label === "PL1" ? profile.limits.pl1MinimumWatts : profile.limits.pl2MinimumWatts,
          max: profile.limits.pl2MaximumWatts,
          step: 1, value, showValue: true, disabled: !profile.persistenceWritable || !enabled || busy, bottomSeparator: separator,
          onChange: next => {
            const draft = profileTdpDraftRef.current || profileTdpDraft || { ac: { ...profile.tdp.ac }, dc: { ...profile.tdp.dc } };
            const pair = { ...draft[side] }; let numeric = Number(next);
            if (label === "PL1") numeric = Math.min(numeric, profile.limits.pl1MaximumWatts);
            pair[label === "PL1" ? "pl1Watts" : "pl2Watts"] = numeric;
            const adjusted = adjustTdpPair(label === "PL1", pair.pl1Watts, pair.pl2Watts, profile.limits);
            scheduleProfileTdp({ ...draft, [side]: adjusted });
          },
        });
        const profileControls = [
          { key: "profile-toggle", node: React.createElement(native.ToggleField, { label: "Profile", checked: enabled, disabled: !writable, onChange: value => void toggleProfile(value) }) },
          { key: "profile-ac", node: profileSlider("Plugged in", "ac", profile.cpuBoost?.ac, previewAc, "none") },
          { key: "profile-dc", node: profileSlider("On battery", "dc", profile.cpuBoost?.dc, previewDc, "standard") },
          { key: "profile-power-ac", node: profile.powerMode ? powerSlider("Power Mode plugged in", profile.powerMode.ac, value => void runPowerMutation("setActiveGamePowerModeAc", { mode: powerModeValue(value) }), !enabled || !writable) : null },
          { key: "profile-power-dc", node: profile.powerMode ? powerSlider("Power Mode on battery", profile.powerMode.dc, value => void runPowerMutation("setActiveGamePowerModeDc", { mode: powerModeValue(value) }), !enabled || !writable) : null },
        ];
        const profileTdpControls = profile.limits ? [
          { key: "profile-tdp-ac-heading", node: React.createElement("div", null, "Plugged in") },
          { key: "profile-tdp-ac-pl1", node: profileTdpSlider("PL1", "ac", profileTdpDraft?.ac?.pl1Watts, "none") },
          { key: "profile-tdp-ac-pl2", compact: true, node: profileTdpSlider("PL2", "ac", profileTdpDraft?.ac?.pl2Watts, "none") },
          { key: "profile-tdp-dc-heading", node: React.createElement("div", null, "On battery") },
          { key: "profile-tdp-dc-pl1", node: profileTdpSlider("PL1", "dc", profileTdpDraft?.dc?.pl1Watts, "none") },
          { key: "profile-tdp-dc-pl2", compact: true, node: profileTdpSlider("PL2", "dc", profileTdpDraft?.dc?.pl2Watts, "standard") },
        ] : [];
        return React.createElement(React.Fragment, null,
          displayError ? React.createElement("p", { key: "error" }, displayError) : null,
          React.createElement(native.PanelSection, { key: "profile-header", title: profile.displayName || `Game ${profile.appId}` }, ...profileControls.slice(0, 1).map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))),
          React.createElement(native.PanelSection, { key: "profile-cpu-section", title: "CPU Boost" }, ...profileControls.slice(1).filter(x => x.node).map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))),
          React.createElement(native.PanelSection, { key: "profile-tdp-section", title: "TDP Control" }, ...profileTdpControls.filter(x => x.node).map(x => React.createElement(native.PanelSectionRow, { key: x.key, style: x.compact ? { marginTop: "-4px" } : undefined }, x.node))));
      }

      return React.createElement(React.Fragment, null,
        unavailable ? React.createElement("p", { key: "unavailable" }, status?.steam?.appId ? "Unavailable while a game is running" : "CPU Boost unavailable") : null,
        displayError ? React.createElement("p", { key: "error" }, displayError) : null,
        React.createElement(native.PanelSection, { key: "cpu-section" },
          ...controls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key }, control.node))),
        React.createElement(native.PanelSection, { key: "power-section", title: "Windows Power Mode" },
          ...powerControls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key }, control.node))),
        React.createElement(native.PanelSection, { key: "tdp-section" },
          ...tdpControls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key, style: control.compact ? { marginTop: "-4px" } : undefined }, control.node))));
    }

    state.addonTabDescriptor = {
      [TAB_MARKER]: true,
      key: "steam-input-addon",
      title: null,
      tab: icon,
      panel: React.createElement(React.Fragment, null,
        React.createElement("div", { className: native.QamTitleClass }, "Steam Addon for Claw"),
        React.createElement("div", { style: { paddingTop: "16px" } },
          React.createElement(CpuBoostPanel))),
    };
    return state.addonTabDescriptor;
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
    state.installFailureKind = null;

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

    const native = findNativeQamComponents(webpackRequire);
    if (!native) {
      state.installFailureKind = "native-components";
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
          patchTabsProducer(result, React, native);
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
