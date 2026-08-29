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
  const QAM_SLIDER_COMMIT_DELAY_MS = 2000;
  const SHOW_INTEL_FPS_LIMIT = false;

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
      const [fpsDraft, setFpsDraft] = React.useState({ ac: 60, dc: 60 });
      const [profileTdpDraft, setProfileTdpDraft] = React.useState(null);
      const profileTdpDraftRef = React.useRef(null);
      const activeProfileAppIdRef = React.useRef(0);
      const [tdpDraft, setTdpDraft] = React.useState(null);
      const [previewAc, setPreviewAc] = React.useState(null);
      const [previewDc, setPreviewDc] = React.useState(null);
      const [powerPreview, setPowerPreview] = React.useState({});
      const [busy, setBusy] = React.useState(false);
      const [error, setError] = React.useState(null);
      const refreshInFlight = React.useRef(false);
      const refreshDirty = React.useRef(false);
      const tdpDraftRef = React.useRef(null);
      const tdpWritableRef = React.useRef(false);
      const modeWritableRef = React.useRef(false);
      const mutationDepthRef = React.useRef(0);
      const deferredInvalidationRef = React.useRef(false);
      const powerModeLabels = ["Best power efficiency", "Balanced", "Best performance"];
      const powerModeNames = ["BestPowerEfficiency", "Balanced", "BestPerformance"];
      const powerModeIndex = value => typeof value === "number" ? value : powerModeNames.indexOf(value);
      const powerModeValue = value => Number(value);

      const failClosed = React.useCallback(message => {
        cancelQamSliderCommits();
        activeProfileAppIdRef.current = 0;
          setStatus(null); setCpu(null); setPowerMode(null); setTdp(null); setProfile(null); profileTdpDraftRef.current = null; setProfileTdpDraft(null); setTdpDraft(null); tdpDraftRef.current = null; setPreviewAc(null); setPreviewDc(null); setPowerPreview({}); setError(message);
          setFpsDraft({ ac: 60, dc: 60 });
      }, []);

      const refresh = React.useCallback(async () => {
        if (refreshInFlight.current) { refreshDirty.current = true; return; }
        refreshInFlight.current = true;
        try {
          const nextStatus = await request("captureStatus");
          const nextProfile = await request("captureActiveGameProfile");
          const nextAppId = Number(nextProfile?.appId || 0);
          if (activeProfileAppIdRef.current !== nextAppId) {
            cancelQamSliderCommits(key => key.startsWith("profile-"));
            setPowerPreview(current => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith("profile-"))));
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
          setFpsDraft({ ac: state.qamSliderCommits?.get("profile-fps-ac")?.value ?? nextProfile?.fpsLimit?.acFps ?? 60, dc: state.qamSliderCommits?.get("profile-fps-dc")?.value ?? nextProfile?.fpsLimit?.dcFps ?? 60 });
          if (nextProfile?.tdp) {
            const authoritativeProfileDraft = { ac: { ...nextProfile.tdp.ac }, dc: { ...nextProfile.tdp.dc } };
            const effectiveProfileDraft = state.qamSliderCommits?.get("profile-tdp")?.draft ?? authoritativeProfileDraft;
            profileTdpDraftRef.current = effectiveProfileDraft;
            setProfileTdpDraft(effectiveProfileDraft);
          }
          const effectiveDeviceDraft = state.qamSliderCommits?.get("device-tdp")?.draft ?? nextDraft;
          setTdpDraft(effectiveDeviceDraft); tdpDraftRef.current = effectiveDeviceDraft;
          const cpuScope = activeGame ? "profile" : "device";
          setPreviewAc(state.qamSliderCommits?.get(`${cpuScope}-cpu-ac`)?.value ?? null);
          setPreviewDc(state.qamSliderCommits?.get(`${cpuScope}-cpu-dc`)?.value ?? null);
          setError(null);
        } catch (_) { failClosed("QAM bridge unavailable"); }
        finally {
          refreshInFlight.current = false;
          if (refreshDirty.current) { refreshDirty.current = false; void refresh(); }
        }
      }, [failClosed]);

      const beginMutation = React.useCallback(() => { mutationDepthRef.current++; }, []);
      const endMutation = React.useCallback(() => {
        mutationDepthRef.current = Math.max(0, mutationDepthRef.current - 1);
        if (mutationDepthRef.current === 0 && deferredInvalidationRef.current) {
          deferredInvalidationRef.current = false;
          void refresh();
        }
      }, [refresh]);
      const runPowerMutation = React.useCallback(async (method, payload) => {
        if (!state.installed) return;
        if (method.endsWith("PowerModeEnabled") && payload?.enabled === false) {
          cancelQamSliderCommits(key => key.includes("power-"));
          setPowerPreview(current => Object.fromEntries(Object.entries(current).filter(([key]) => !key.includes("power-"))));
        }
        try {
          beginMutation();
          setError(null);
          const result = await request(method, payload);
          const failure = !result?.succeeded
            ? (result?.failureMessage || "Power Mode update failed")
            : null;
          await refresh();
          // The explicit refresh consumed the mutation's invalidation. Preserve
          // the operation failure after refreshing Runtime authority.
          deferredInvalidationRef.current = false;
          if (failure) setError(failure);
        }
        catch (_) { failClosed("Power Mode update failed"); }
        finally { endMutation(); }
      }, [beginMutation, endMutation, failClosed, refresh]);

      React.useEffect(() => { void refresh(); }, [refresh]);

      React.useEffect(() => {
        const previous = state.onStateInvalidated;
        const handler = () => {
          previous?.();
          if (mutationDepthRef.current > 0) {
            deferredInvalidationRef.current = true;
            return;
          }
          // Keep all pending slider drafts authoritative across invalidation.
          void refresh();
        };
        state.onStateInvalidated = handler;
        return () => { if (state.onStateInvalidated === handler) state.onStateInvalidated = previous || null; };
      }, [refresh]);

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
      const labelRow = (label, value) => React.createElement("div", { className: native.FieldLabelRowClass, style: { display: "flex", width: "100%", justifyContent: "space-between" } }, React.createElement("span", { className: native.FieldLabelClass }, label), React.createElement("span", { className: native.FieldLabelValueClass }, value));
      const scheduleMode = (side, value) => {
        if (!state.installed || !modeWritableRef.current) return;
        const key = side === "ac" ? "ac" : "dc";
        side === "ac" ? setPreviewAc(value) : setPreviewDc(value);
        scheduleQamSliderCommit(`device-cpu-${key}`, { value }, side === "ac" ? "setDeviceCpuBoostAc" : "setDeviceCpuBoostDc", { mode: value }, async (result, failure) => {
          if (failure) { failClosed("CPU Boost update failed"); return; }
          if (result?.snapshot) setCpu(result.snapshot);
          side === "ac" ? setPreviewAc(null) : setPreviewDc(null);
          if (!result?.succeeded) setError(result?.failureMessage || "CPU Boost update failed");
          await refresh();
        });
      };
      const setEnabled = async value => {
        cancelQamSliderCommits(key => key.startsWith("device-cpu-"));
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
      const scheduleTdp = (nextDraft) => {
        if (!tdpWritableRef.current) return;
        tdpDraftRef.current = nextDraft; setTdpDraft(nextDraft);
        scheduleQamSliderCommit("device-tdp", { draft: nextDraft }, "setDeviceTdp", { configuration: nextDraft }, async (result, failure) => {
          if (failure) { failClosed("TDP update failed"); return; }
          const authoritative = result?.snapshot?.configuration;
          if (authoritative) { setTdp(result.snapshot); setTdpDraft(authoritative); tdpDraftRef.current = authoritative; }
          if (!result?.succeeded) setError(result?.failureMessage || "TDP update failed");
          await refresh();
        });
      };
      const setTdpEnabled = async enabled => {
        if (!state.installed || !tdpMutationAvailable) return;
        cancelQamSliderCommits(key => key === "device-tdp"); setBusy(true); setError(null);
        try {
          beginMutation();
          const result = await request("setDeviceTdpEnabled", { enabled });
          setTdp(result.snapshot); setTdpDraft(result.snapshot.configuration); tdpDraftRef.current = result.snapshot.configuration;
          if (!result.succeeded) setError(result.failureMessage || "TDP update failed");
        } catch (_) { failClosed("TDP update failed"); }
        finally { endMutation(); setBusy(false); }
      };
      const tdpSlider = (label, source, limit, value, separator) => value == null || !limit ? null : React.createElement(native.SliderField, {
        label: `${source === "ac" ? "Plugged in" : "On battery"} · ${label}`,
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
        label: labelRow(title, labelFor(value)),
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

      const powerInitialized = powerMode?.ac?.desired != null && powerMode?.dc?.desired != null;
      const powerWritable = !!powerMode?.persistenceWritable && powerInitialized && !status?.steam?.appId && !busy;
      const schedulePowerMode = (key, method, value, appId = 0) => {
        setPowerPreview(current => ({ ...current, [key]: value }));
        scheduleQamSliderCommit(key, { value, appId }, method, { mode: powerModeValue(value) }, async (result, failure) => {
          if (failure) { failClosed("Power Mode update failed"); return; }
          await refresh();
          setPowerPreview(current => { const next = { ...current }; delete next[key]; return next; });
          if (!result?.succeeded) setError(result?.failureMessage || "Power Mode update failed");
        });
      };
      const powerSlider = (label, value, key, method, disabled) => {
        if (value == null) return null;
        const pendingValue = state.qamSliderCommits?.get(key)?.value;
        const currentValue = powerPreview[key] ?? pendingValue ?? value;
        return React.createElement(native.SliderField, { label: labelRow(label, powerModeLabels[powerModeIndex(currentValue)] ?? "Unknown"), min: 0, max: 2, step: 1, value: powerModeIndex(currentValue), notchCount: 3, notchTicksVisible: true, disabled, onChange: next => schedulePowerMode(key, method, Number(next), activeProfile ? Number(profile?.appId || 0) : 0) });
      };
      const powerControls = [{ key: "power-toggle", node: React.createElement(native.ToggleField, { label: "Windows Power Mode", checked: !!powerMode?.enabled, disabled: !powerWritable, onChange: value => void runPowerMutation("setDevicePowerModeEnabled", { enabled: !!value }) }) }];
      if (powerMode?.enabled) { powerControls.push({ key: "power-ac", node: powerSlider("Plugged in", powerMode.ac?.desired ?? powerMode.ac?.current, "device-power-ac", "setDevicePowerModeAc", !powerWritable) }); powerControls.push({ key: "power-dc", node: powerSlider("On battery", powerMode.dc?.desired ?? powerMode.dc?.current, "device-power-dc", "setDevicePowerModeDc", !powerWritable) }); }

      const tdpControls = [{ key: "tdp-toggle", node: React.createElement(native.ToggleField, {
        label: "TDP Control",
        checked: !!tdpDraft?.enabled,
        disabled: !tdpMutationAvailable,
        onChange: value => void setTdpEnabled(!!value),
      }) }];
      if (tdpDraft?.enabled && tdpLimits) {
        tdpControls.push({ key: "tdp-ac-pl1", node: tdpSlider("PL1", "ac", tdpLimits, tdpDraft.ac?.pl1Watts, "none") });
        tdpControls.push({ key: "tdp-ac-pl2", node: tdpSlider("PL2", "ac", tdpLimits, tdpDraft.ac?.pl2Watts, "none") });
        tdpControls.push({ key: "tdp-dc-pl1", node: tdpSlider("PL1", "dc", tdpLimits, tdpDraft.dc?.pl1Watts, "none") });
        tdpControls.push({ key: "tdp-dc-pl2", node: tdpSlider("PL2", "dc", tdpLimits, tdpDraft.dc?.pl2Watts, "standard") });
      }

      if (activeProfile) {
        const writable = profile.persistenceWritable && !busy;
        const enabled = !!profile.enabled;
        const scheduleProfileMode = (side, value) => {
          if (!state.installed || !profile.persistenceWritable || !enabled) return;
          const key = side === "ac" ? "ac" : "dc";
          side === "ac" ? setPreviewAc(value) : setPreviewDc(value);
          scheduleQamSliderCommit(`profile-cpu-${key}`, { value, appId: Number(profile.appId || 0) }, side === "ac" ? "setActiveGameCpuBoostAc" : "setActiveGameCpuBoostDc", { mode: value }, async (result, failure) => {
            if (failure) { failClosed("CPU Boost update failed"); return; }
            if (result?.snapshot) setProfile(result.snapshot);
            side === "ac" ? setPreviewAc(null) : setPreviewDc(null);
            if (!result?.succeeded) setError(result?.failureMessage || "CPU Boost update failed");
            await refresh();
          });
        };
        const toggleProfile = async value => {
          if (!state.installed || !writable) return;
          cancelQamSliderCommits(key => key.startsWith("profile-"));
          setPowerPreview(current => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith("profile-"))));
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
        const toggleProfileFeature = async (feature, value, method, pendingPredicate) => {
          if (!state.installed || !writable || !enabled) return;
          if (!value) {
            cancelQamSliderCommits(pendingPredicate);
            if (feature === "CPU Boost") { setPreviewAc(null); setPreviewDc(null); }
            if (feature === "Power Mode") setPowerPreview(current => Object.fromEntries(Object.entries(current).filter(([key]) => !pendingPredicate(key))));
          }
          setBusy(true); setError(null);
          beginMutation();
          try {
            const result = await request(method, { enabled: !!value });
            if (feature === "TDP" && result.snapshot?.tdp) {
              const nextDraft = { ac: { ...result.snapshot.tdp.ac }, dc: { ...result.snapshot.tdp.dc } };
              profileTdpDraftRef.current = nextDraft; setProfileTdpDraft(nextDraft);
            }
            const failure = !result?.succeeded ? (result.failureMessage || `${feature} update failed`) : null;
            await refresh();
            deferredInvalidationRef.current = false;
            if (failure) setError(failure);
          } catch (_) { failClosed(`${feature} update failed`); }
          finally { endMutation(); setBusy(false); }
        };
        const scheduleProfileTdp = draft => {
          if (!state.installed || !profile.persistenceWritable || !enabled || !profile.limits) return;
          profileTdpDraftRef.current = draft;
          setProfileTdpDraft(draft);
          scheduleQamSliderCommit("profile-tdp", { draft, appId: Number(profile.appId || 0) }, "setActiveGameTdp", { configuration: draft }, async (result, failure) => {
            if (failure) { failClosed("Profile TDP update failed"); return; }
            if (result?.snapshot?.tdp) { setProfile(result.snapshot); const nextDraft = { ac: { ...result.snapshot.tdp.ac }, dc: { ...result.snapshot.tdp.dc } }; profileTdpDraftRef.current = nextDraft; setProfileTdpDraft(nextDraft); }
            if (!result?.succeeded) setError(result?.failureMessage || "TDP update failed");
            await refresh();
          });
        };
        const profileSlider = (label, side, value, preview, separator) => value == null ? null : React.createElement(native.SliderField, {
          label: labelRow(label, labelFor(preview ?? value)),
          min: 0, max: 6, step: 1, value: preview ?? value, notchCount: modes.length,
          disabled: !profile.persistenceWritable || !enabled || busy, notchTicksVisible: true, bottomSeparator: separator,
          onChange: next => scheduleProfileMode(side, Number(next)),
        });
        const profileTdpSlider = (label, side, value, separator) => value == null || !profile.limits ? null : React.createElement(native.SliderField, {
          label, min: label.includes("PL1") ? profile.limits.pl1MinimumWatts : profile.limits.pl2MinimumWatts,
          max: profile.limits.pl2MaximumWatts,
          step: 1, value: state.qamSliderCommits?.get("profile-tdp")?.draft?.[side]?.[label.includes("PL1") ? "pl1Watts" : "pl2Watts"] ?? value, showValue: true, disabled: !profile.persistenceWritable || !enabled || busy, bottomSeparator: separator,
          onChange: next => {
            const draft = profileTdpDraftRef.current || profileTdpDraft || { ac: { ...profile.tdp.ac }, dc: { ...profile.tdp.dc } };
            const pair = { ...draft[side] }; let numeric = Number(next);
            if (label.includes("PL1")) numeric = Math.min(numeric, profile.limits.pl1MaximumWatts);
            pair[label.includes("PL1") ? "pl1Watts" : "pl2Watts"] = numeric;
            const adjusted = adjustTdpPair(label.includes("PL1"), pair.pl1Watts, pair.pl2Watts, profile.limits);
            scheduleProfileTdp({ ...draft, [side]: adjusted });
          },
        });
        const profileCpuControls = [
          { key: "profile-cpu-toggle", node: React.createElement(native.ToggleField, { label: "CPU Boost", checked: !!profile.cpuBoost?.enabled, disabled: !writable || !enabled, onChange: value => void toggleProfileFeature("CPU Boost", !!value, "setActiveGameCpuBoostEnabled", key => key.startsWith("profile-cpu-")) }) },
          ...(!profile.cpuBoost?.enabled ? [] : [
          { key: "profile-ac", node: profileSlider("Plugged in", "ac", profile.cpuBoost?.ac, previewAc, "none") },
          { key: "profile-dc", node: profileSlider("On battery", "dc", profile.cpuBoost?.dc, previewDc, "standard") },
          ]),
        ];
        const profilePowerControls = [
          { key: "profile-power-toggle", node: profile.powerMode ? React.createElement(native.ToggleField, { label: "Windows Power Mode", checked: !!profile.powerMode.enabled, disabled: !writable || !enabled, onChange: value => void toggleProfileFeature("Power Mode", !!value, "setActiveGamePowerModeEnabled", key => key.startsWith("profile-power-")) }) : null },
          ...(!profile.powerMode?.enabled ? [] : [
          { key: "profile-power-ac", node: profile.powerMode ? powerSlider("Plugged in", profile.powerMode.ac, "profile-power-ac", "setActiveGamePowerModeAc", !enabled || !writable) : null },
          { key: "profile-power-dc", node: profile.powerMode ? powerSlider("On battery", profile.powerMode.dc, "profile-power-dc", "setActiveGamePowerModeDc", !enabled || !writable) : null },
          ]),
        ];
        const profileTdpControls = profile.limits ? [
          { key: "profile-tdp-toggle", node: React.createElement(native.ToggleField, { label: "TDP Control", checked: !!profile.tdp?.enabled, disabled: !writable || !enabled, onChange: value => void toggleProfileFeature("TDP", !!value, "setActiveGameTdpEnabled", key => key === "profile-tdp") }) },
          ...(!profile.tdp?.enabled ? [] : [
          { key: "profile-tdp-ac-pl1", node: profileTdpSlider("Plugged in · PL1", "ac", profileTdpDraft?.ac?.pl1Watts, "none") },
          { key: "profile-tdp-ac-pl2", node: profileTdpSlider("Plugged in · PL2", "ac", profileTdpDraft?.ac?.pl2Watts, "none") },
          { key: "profile-tdp-dc-pl1", node: profileTdpSlider("On battery · PL1", "dc", profileTdpDraft?.dc?.pl1Watts, "none") },
          { key: "profile-tdp-dc-pl2", node: profileTdpSlider("On battery · PL2", "dc", profileTdpDraft?.dc?.pl2Watts, "standard") },
          ]),
        ] : [];
        const fps = profile.fpsLimit || { enabled: false, acFps: 60, dcFps: 60, available: false, unavailableReason: "Intel FPS Limit is unavailable." };
        const runFpsMutation = async (method, payload) => {
          if (!state.installed || !fps.available || !writable || !enabled) return;
          beginMutation();
          setError(null);
          try {
            const result = await request(method, payload);
            const failure = !result?.succeeded ? (result.failureMessage || "Intel FPS Limit update failed") : null;
            await refresh();
            deferredInvalidationRef.current = false;
            if (failure) setError(failure);
          } catch (_) { failClosed("Intel FPS Limit update failed"); }
          finally { endMutation(); }
        };
        const scheduleFps = (side, value) => {
          if (!fps.available || !profile.persistenceWritable || !enabled || !fps.enabled || busy) return;
          setFpsDraft(current => ({ ...current, [side]: value }));
          scheduleQamSliderCommit(`profile-fps-${side}`, { value, appId: Number(profile.appId || 0) }, side === "ac" ? "setActiveGameFpsLimitAc" : "setActiveGameFpsLimitDc", { fps: value }, async (result, failure) => {
            if (failure) { failClosed("Intel FPS Limit update failed"); return; }
            if (!result?.succeeded) setError(result?.failureMessage || "Intel FPS Limit update failed");
            await refresh();
          });
        };
        const fpsSlider = (label, side, value) => { const currentValue = state.qamSliderCommits?.get(`profile-fps-${side}`)?.value ?? fpsDraft[side] ?? value; return React.createElement(native.SliderField, { label: labelRow(label, `${currentValue} FPS`), min: 40, max: 120, step: 1, value: currentValue, disabled: !fps.available || !profile.persistenceWritable || !enabled || !fps.enabled || busy, onChange: next => scheduleFps(side, Number(next)) }); };
        const fpsControls = [
          { key: "fps-toggle", node: React.createElement(native.ToggleField, { label: "Intel FPS Limit", checked: !!fps.enabled, disabled: !fps.available || !writable || !enabled, onChange: value => { if (!value) cancelQamSliderCommits(key => key.startsWith("profile-fps-")); void runFpsMutation("setActiveGameFpsLimitEnabled", { enabled: !!value }); } }) },
          ...(!fps.available ? [{ key: "fps-unavailable", node: React.createElement("div", null, fps.unavailableReason || "Intel FPS Limit is unavailable.") }] : []),
          ...(fps.available && fps.enabled ? [
          { key: "fps-ac", node: fpsSlider("Plugged in", "ac", fps.acFps ?? 60) },
          { key: "fps-dc", node: fpsSlider("On battery", "dc", fps.dcFps ?? 60) },
          ] : []),
        ];
        return React.createElement(React.Fragment, null,
          displayError ? React.createElement("p", { key: "error" }, displayError) : null,
          React.createElement(native.PanelSection, { key: "profile-header", title: profile.displayName || `Game ${profile.appId}` }, React.createElement(native.PanelSectionRow, { key: "profile-toggle" }, React.createElement(native.ToggleField, { label: "Profile", checked: enabled, disabled: !writable, onChange: value => void toggleProfile(value) }))),
          React.createElement(native.PanelSection, { key: "profile-tdp-section" }, ...profileTdpControls.filter(x => x.node).map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))),
          SHOW_INTEL_FPS_LIMIT ? React.createElement(native.PanelSection, { key: "profile-fps-section" }, ...fpsControls.map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))) : null,
          React.createElement(native.PanelSection, { key: "profile-cpu-section" }, ...profileCpuControls.filter(x => x.node).map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))),
          profilePowerControls.some(x => x.node) ? React.createElement(native.PanelSection, { key: "profile-power-section" }, ...profilePowerControls.filter(x => x.node).map(x => React.createElement(native.PanelSectionRow, { key: x.key }, x.node))) : null);
      }

      return React.createElement(React.Fragment, null,
        React.createElement("div", { className: native.QamTitleClass }, "Steam Addon for Claw"),
        unavailable ? React.createElement("p", { key: "unavailable" }, status?.steam?.appId ? "Unavailable while a game is running" : "CPU Boost unavailable") : null,
        displayError ? React.createElement("p", { key: "error" }, displayError) : null,
        React.createElement(native.PanelSection, { key: "cpu-section" },
          ...controls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key }, control.node))),
        React.createElement(native.PanelSection, { key: "power-section", title: "Windows Power Mode" },
          ...powerControls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key }, control.node))),
        React.createElement(native.PanelSection, { key: "tdp-section" },
          ...tdpControls.filter(control => control.node).map(control => React.createElement(native.PanelSectionRow, { key: control.key }, control.node))));
    }

    state.addonTabDescriptor = {
      [TAB_MARKER]: true,
      key: "steam-input-addon",
      title: null,
      tab: icon,
      panel: React.createElement(React.Fragment, null,
        React.createElement("div", { style: { paddingTop: "16px" } },
          React.createElement(CpuBoostPanel))),
    };
    return state.addonTabDescriptor;
  }

  // One stable, Addon-owned state object. install()/uninstall() mutate it in place rather than
  // replacing it, so the functions exposed on it below always remain callable.
  const state = window[GLOBAL_KEY] || (window[GLOBAL_KEY] = {});

  function cancelQamSliderCommits(predicate = () => true) {
    for (const [key, pending] of state.qamSliderCommits ?? []) {
      if (!predicate(key, pending)) continue;
      clearTimeout(pending.timer);
      state.qamSliderCommits.delete(key);
    }
  }

  function scheduleQamSliderCommit(key, pending, method, payload, onSettled) {
    state.qamSliderCommits ??= new Map();
    const previous = state.qamSliderCommits.get(key);
    if (previous) clearTimeout(previous.timer);
    const token = (state.qamSliderCommitToken || 0) + 1;
    state.qamSliderCommitToken = token;
      const entry = { ...pending, method, payload, token, timer: null };
      entry.timer = setTimeout(async () => {
        if (state.qamSliderCommits.get(key)?.token !== token) return;
        entry.timer = null;
        if (!state.installed) return;
        try {
          if (entry.appId) {
            const activeProfile = await request("captureActiveGameProfile");
            if (Number(activeProfile?.appId || 0) !== entry.appId) {
              if (state.qamSliderCommits.get(key)?.token === token) {
                state.qamSliderCommits.delete(key);
                state.onStateInvalidated?.();
              }
              return;
            }
          }
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          const result = await request(method, payload);
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          state.qamSliderCommits.delete(key);
          await onSettled(result, null, entry);
        } catch (error) {
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          state.qamSliderCommits.delete(key);
          await onSettled(null, error, entry);
        }
    }, QAM_SLIDER_COMMIT_DELAY_MS);
    state.qamSliderCommits.set(key, entry);
  }

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
    cancelQamSliderCommits();
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
