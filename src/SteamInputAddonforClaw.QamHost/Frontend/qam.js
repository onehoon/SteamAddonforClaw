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
  const ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
  const ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
  // Defensive fallback only (SF-V2-05/08): every real product row supplies a valid
  // row.commitPolicy.delayMilliseconds, so this is never expected to be hit in practice.
  const QS_FALLBACK_COMMIT_DELAY_MS = 2000;

  // Shared Quick Settings transport/ABI vocabulary (SF-V2-05/08). These mirror the closed C# enums
  // the bridge serializes numerically -- they are NOT a duplicated copy of Device/Profile product
  // presentation policy (labels/order/options/ranges/policy all come from the page payload).
  const QS_PAGE_DEVICE = 0;
  const QS_PAGE_PROFILE = 1;
  const QS_CONTROL_TOGGLE = 0;
  const QS_CONTROL_SLIDER = 1;
  const QS_SLIDER_NUMERIC = 0;
  const QS_SLIDER_DISCRETE = 1;
  const QS_COMMIT_TRAILING = 1;
  const QS_VALUE_BOOLEAN = 0;
  const QS_VALUE_INTEGER = 1;

  function log(message) {
    console.log("[SteamInputAddon:QAM] " + message);
  }

  function logOnce(key, message) {
    state.diagnostics ??= {};
    if (state.diagnostics[key]) return;
    state.diagnostics[key] = true;
    log(message);
  }

  function logStateChange(key, signature, message) {
    state.runtimeDiagnostics ??= {};
    if (state.runtimeDiagnostics[key] === signature) return;
    state.runtimeDiagnostics[key] = signature;
    log(message);
  }

  function quickSettingsPageName(pageId) {
    if (pageId === QS_PAGE_DEVICE) return "Device";
    if (pageId === QS_PAGE_PROFILE) return "Profile";
    return String(pageId);
  }

  function quickSettingsAppId(appId) {
    return appId == null ? "none" : String(appId);
  }

  function logQuickSettingsPageState(page) {
    const rows = (page?.sections ?? []).flatMap(section => section?.rows ?? []);
    const availableRows = rows.filter(row => row?.available === true).length;
    const writableRows = rows.filter(row => row?.writable === true).length;
    const pageName = quickSettingsPageName(page?.pageId);
    const appId = quickSettingsAppId(page?.appId ?? null);
    const signature = [pageName, appId, page?.available === true, rows.length, availableRows, writableRows].join("|");
    logStateChange(
      "quickSettingsPage",
      signature,
      `QAM page state: Page=${pageName} AppId=${appId} Available=${page?.available === true} Rows=${rows.length} AvailableRows=${availableRows} WritableRows=${writableRows}`);
  }

  function logQuickSettingsMutationRequest(pageId, appId, rowId) {
    log(`QAM mutation request: Page=${quickSettingsPageName(pageId)} AppId=${quickSettingsAppId(appId)} Row=${rowId}`);
  }

  function logQuickSettingsMutationResult(pageId, appId, rowId, succeeded, message = null) {
    const suffix = message ? ` Message=${message}` : "";
    log(`QAM mutation result: Page=${quickSettingsPageName(pageId)} AppId=${quickSettingsAppId(appId)} Row=${rowId} Succeeded=${succeeded}${suffix}`);
  }

  function quickSettingsRowMutationBlockReason(row, busy) {
    if (!row?.available) return "row-unavailable";
    if (!row?.writable) return "row-readonly";
    if (busy) return "busy";
    return null;
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

  function findUniqueFactory(webpackRequire, requiredTokens) {
    const matches = Object.entries(webpackRequire.m || {}).filter(([, factory]) => {
      const source = String(factory);
      return requiredTokens.every(token => source.includes(token));
    });
    return matches.length === 1 ? matches[0] : null;
  }

  function findUniqueFunction(exports, requiredTokens) {
    const matches = Object.values(exports || {}).filter(value => {
      if (typeof value !== "function") return false;
      const source = String(value);
      return requiredTokens.every(token => source.includes(token));
    });
    return matches.length === 1 ? matches[0] : null;
  }

  function findUniqueObject(exports, predicate) {
    const matches = Object.values(exports || {}).filter(value =>
      value && typeof value === "object" && predicate(value));
    return matches.length === 1 ? matches[0] : null;
  }

  function findNativeQamComponents(webpackRequire) {
    const fieldsFactory = findUniqueFactory(webpackRequire, [
      "DialogSlider_Container",
      "DropDownField",
      "SliderField",
    ]);
    if (!fieldsFactory) {
      logOnce("nativeFieldsFactory", "QAM native fields factory discovery failed (expected exactly one semantic match).");
      return null;
    }

    const layoutFactory = findUniqueFactory(webpackRequire, [
      "PanelSectionTitle",
      "PanelSectionRow",
      "spinner",
    ]);
    if (!layoutFactory) {
      logOnce("nativeLayoutFactory", "QAM native layout factory discovery failed (expected exactly one semantic match).");
      return null;
    }

    let fields;
    let layout;
    try {
      fields = webpackRequire(fieldsFactory[0]);
      layout = webpackRequire(layoutFactory[0]);
    } catch (err) {
      logOnce("nativeExports", `QAM native semantic module loading failed: ${String(err)}`);
      return null;
    }

    const SliderField = findUniqueFunction(fields, [
      "onChangeComplete",
      "notchCount",
      "valueSuffix",
      "explainerTitle",
    ]);
    if (!SliderField) {
      logOnce("nativeSliderField", "QAM native SliderField discovery failed (expected exactly one semantic match).");
      return null;
    }

    const ToggleField = findUniqueFunction(fields, [
      "OnToggleChange",
      "this.Toggle()",
    ]);
    if (!ToggleField) {
      logOnce("nativeToggleField", "QAM native ToggleField discovery failed (expected exactly one semantic match).");
      return null;
    }

    const PanelSection = findUniqueFunction(layout, [
      "PanelSectionTitle",
      "spinner",
    ]);
    if (!PanelSection) {
      logOnce("nativePanelSection", "QAM native PanelSection discovery failed (expected exactly one semantic match).");
      return null;
    }

    const PanelSectionRow = findUniqueObject(
      layout,
      value => value.$$typeof && typeof value.render === "function");
    if (!PanelSectionRow) {
      logOnce("nativePanelSectionRow", "QAM native PanelSectionRow discovery failed (expected exactly one semantic match).");
      return null;
    }

    logOnce("nativeControls", "QAM native semantic controls resolved.");
    return { SliderField, ToggleField, PanelSection, PanelSectionRow };
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

  function addonTabKey(tab) {
    const marker = tab?.[TAB_MARKER];
    if (marker === true) return "legacy";
    if (marker === ADDON_DEVICE_TAB_KEY) return ADDON_DEVICE_TAB_KEY;
    if (marker === ADDON_PROFILE_TAB_KEY) return ADDON_PROFILE_TAB_KEY;
    return null;
  }

  function resolveNativeTabSelection(owner) {
    const props = owner?.props;
    if (!props) return null;

    // The supported Steam tab owner exposes one selected-tab value and one matching callback.
    // Keep the allow-list explicit: an ambiguous or unknown prop shape must remain fail-open.
    const candidates = [
      ["selectedTab", "onTabSelected"],
      ["selectedTabKey", "onTabSelected"],
      ["activeTab", "onTabSelected"],
      ["activeTabKey", "onTabSelected"],
      ["selectedTab", "onTabChange"],
      ["selectedTabKey", "onTabChange"],
      ["activeTab", "onTabChange"],
      ["activeTabKey", "onTabChange"],
    ].filter(([valueKey, setterKey]) =>
      Object.prototype.hasOwnProperty.call(props, valueKey) && typeof props[setterKey] === "function");

    if (candidates.length !== 1) return null;
    const [valueKey, setterKey] = candidates[0];
    return {
      current: props[valueKey],
      set: (key, descriptor) => {
        const current = props[valueKey];
        const next = current && typeof current === "object" ? descriptor : key;
        props[setterKey](next);
      },
    };
  }

  function selectInitialAddonTab(owner, tabs, descriptors) {
    state.initialTabSelectionOwners ??= new WeakSet();
    if (state.initialTabSelectionOwners.has(tabs)) return;
    state.initialTabSelectionOwners.add(tabs);

    const authority = resolveNativeTabSelection(owner);
    if (!authority) {
      logOnce("initialTabSelectionUnavailable", "QAM initial Addon tab selection unavailable; tabs remain usable.");
      return;
    }

    void request("captureStatus").then(status => {
      const appId = Number(status?.steam?.appId || 0);
      const key = appId > 0 ? ADDON_PROFILE_TAB_KEY : ADDON_DEVICE_TAB_KEY;
      const descriptor = descriptors[key];
      if (!descriptor) return;
      try {
        authority.set(key, descriptor);
        log(`QAM initial Addon tab selection: ${key === ADDON_DEVICE_TAB_KEY ? "Device Reason=NoActiveGame" : `Profile AppId=${appId}`}`);
      } catch (error) {
        logOnce("initialTabSelectionFailure", `QAM initial Addon tab selection unavailable; tabs remain usable. Reason=${String(error)}`);
      }
    }).catch(() => {
      logOnce("initialTabSelectionBridgeFailure", "QAM initial Addon tab selection unavailable; tabs remain usable.");
    });
  }

  function ensureAddonTabs(owner, React, native) {
    const tabs = owner?.props?.tabs;
    if (!Array.isArray(tabs)) return null;

    const descriptors = {
      [ADDON_DEVICE_TAB_KEY]: buildAddonTab(React, native, QS_PAGE_DEVICE),
      [ADDON_PROFILE_TAB_KEY]: buildAddonTab(React, native, QS_PAGE_PROFILE),
    };
    const seen = new Set();
    let legacyRemoved = 0;
    let duplicatesRemoved = 0;
    const steamTabs = [];
    for (const tab of tabs) {
      const key = addonTabKey(tab);
      if (!key) {
        steamTabs.push(tab);
        continue;
      }
      if (key === "legacy") legacyRemoved++;
      else if (seen.has(key)) duplicatesRemoved++;
      else seen.add(key);
    }

    const desired = [descriptors[ADDON_DEVICE_TAB_KEY], descriptors[ADDON_PROFILE_TAB_KEY]];
    const nextTabs = [...steamTabs, ...desired];
    const unchanged = tabs.length === nextTabs.length && tabs.every((tab, index) => tab === nextTabs[index]);
    if (!unchanged) tabs.splice(0, tabs.length, ...nextTabs);
    logOnce(
      "stableTabs",
      `QAM stable Addon tabs ensured. Device=${!!descriptors[ADDON_DEVICE_TAB_KEY]} Profile=${!!descriptors[ADDON_PROFILE_TAB_KEY]} LegacyRemoved=${legacyRemoved} DuplicatesRemoved=${duplicatesRemoved}`
    );
    selectInitialAddonTab(owner, tabs, descriptors);
    return tabs;
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
          logOnce("tabsOwner", `tabs owner found. ExistingTabs=${owner.props.tabs.length}`);
          record.tabs = ensureAddonTabs(owner, React, native);
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
          if (addonTabKey(record.tabs[index])) record.tabs.splice(index, 1);
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

  function buildAddonTab(React, native, pageId) {
    state.addonTabDescriptors ??= {};
    const key = pageId === QS_PAGE_DEVICE ? ADDON_DEVICE_TAB_KEY : ADDON_PROFILE_TAB_KEY;
    if (state.addonTabDescriptors[key]) return state.addonTabDescriptors[key];

    const icon = React.createElement(
      "svg",
      { viewBox: "0 0 24 24", width: 24, height: 24, fill: "currentColor" },
      React.createElement("path", { d: "M5.1 7.1C3.2 7.7 2.2 9.7 1.6 12.1l-1 4.1c-.4 1.8.7 3.4 2.5 3.4 1 0 1.9-.5 2.4-1.3l1.4-2.1h9.9l1.4 2.1c.5.8 1.4 1.3 2.4 1.3 1.8 0 2.9-1.6 2.5-3.4l-1-4.1c-.6-2.4-1.6-4.4-3.5-5-1.1-.4-2.8-.5-4.2-.5h-2.7c-1.4 0-3.1.1-4.2.5Z" })
    );

    function QuickSettingsPanel({ pageId }) {
      // SF-V2-08: Device and Profile are now the SAME shared Quick Settings product model -- one
      // current generic page state serves both, replacing the previous separate devicePage /
      // legacy-Profile-object-plus-drafts state machines (work order section 10.1).
      const [quickSettingsPage, setQuickSettingsPage] = React.useState(null);
      const quickSettingsPageRef = React.useRef(null);
      // The current page/AppId context this panel is showing (section 10.2). Retiring pending work
      // on a context change, and ignoring a stale settlement, both key off this.
      const quickSettingsContextRef = React.useRef(null);
      // The generic pending draft lives in state.qamSliderCommits (outside React). Bump this to
      // force one renderer-local pass so an immediate slider preview / linked paired correction is
      // visible before the trailing commit settles.
      const [, bumpQuickSettingsDraftRender] = React.useState(0);
      const [busy, setBusy] = React.useState(false);
      const [error, setError] = React.useState(null);
      const refreshInFlight = React.useRef(false);
      const refreshDirty = React.useRef(false);
      const mutationDepthRef = React.useRef(0);
      const deferredInvalidationRef = React.useRef(false);

      const failClosed = React.useCallback(message => {
        cancelQuickSettingsPendingForContext(quickSettingsContextRef.current);
        quickSettingsContextRef.current = null;
        setQuickSettingsPage(null); quickSettingsPageRef.current = null; setError(message);
      }, []);

      const refresh = React.useCallback(async () => {
        if (refreshInFlight.current) { refreshDirty.current = true; return; }
        refreshInFlight.current = true;
        try {
          let nextContext;
          if (pageId === QS_PAGE_DEVICE) {
            nextContext = { pageId: QS_PAGE_DEVICE, appId: null };
          } else {
            const nextStatus = await request("captureStatus");
            const nextAppId = Number(nextStatus?.steam?.appId || 0);
            nextContext = { pageId: QS_PAGE_PROFILE, appId: nextAppId > 0 ? nextAppId : null };
          }
          const previousContext = quickSettingsContextRef.current;
          if (pageId === QS_PAGE_PROFILE && previousContext && !sameQuickSettingsContext(previousContext, nextContext)) {
            // Profile context changes retire only Profile pending work. The independent Device
            // panel owns its own fixed context and drafts.
            cancelQuickSettingsPendingForContext(previousContext);
          }
          quickSettingsContextRef.current = nextContext;

          const nextPage = await request("captureQuickSettingsPage", { pageId: nextContext.pageId, appId: nextContext.appId });
          // Late-result guard (section 16): only install this fetch if the visible context has not
          // already moved on again while the request was in flight.
          if (sameQuickSettingsContext(quickSettingsContextRef.current, nextContext)) {
            pruneQuickSettingsPendingAgainstPage(nextPage);
            logQuickSettingsPageState(nextPage);
            setQuickSettingsPage(nextPage); quickSettingsPageRef.current = nextPage;
          }
          setError(null);
        } catch (_) { failClosed("QAM bridge unavailable"); }
        finally {
          refreshInFlight.current = false;
          if (refreshDirty.current) { refreshDirty.current = false; void refresh(); }
        }
      }, [failClosed, pageId]);

      const beginMutation = React.useCallback(() => { mutationDepthRef.current++; }, []);
      const endMutation = React.useCallback(() => {
        mutationDepthRef.current = Math.max(0, mutationDepthRef.current - 1);
        if (mutationDepthRef.current === 0 && deferredInvalidationRef.current) {
          deferredInvalidationRef.current = false;
          void refresh();
        }
      }, [refresh]);
      React.useEffect(() => { void refresh(); }, [refresh]);

      React.useEffect(() => {
        const handler = () => {
          if (mutationDepthRef.current > 0) {
            deferredInvalidationRef.current = true;
            return;
          }
          // Keep all pending slider drafts authoritative across invalidation.
          void refresh();
        };
        return subscribeStateInvalidation(handler);
      }, [refresh]);

      const displayError = error || quickSettingsPage?.message || null;

      // --- Generic Device/Profile Quick Settings renderer (SF-V2-05/08) ------------------------
      // Steam native ToggleField / SliderField driven entirely by the shared page payload:
      // section/row order, labels, control kind, options, ranges, commit policy, grouping, and
      // linked constraints all come from the page -- no Device/Profile product table lives here.
      const quickSettingsRowEffectiveValue = row => quickSettingsPendingValue(quickSettingsPage, row.rowId) ?? row.value;
      const canMutateQuickSettingsRow = row => quickSettingsRowMutationBlockReason(row, busy) == null;
      const requireQuickSettingsRowMutation = row => {
        const reason = quickSettingsRowMutationBlockReason(row, busy);
        if (!reason) return true;
        log(`QAM mutation blocked: Row=${row?.rowId ?? "unknown"} Reason=${reason}`);
        return false;
      };

      const applyQuickSettingsResult = result => {
        // Section 16: ignore a settlement whose page context no longer matches what is currently
        // visible (the user already moved to a different game/Device before this arrived) -- it
        // must never replace the newer context's page or surface its own error.
        if (!sameQuickSettingsContext(quickSettingsContextOf(result?.page), quickSettingsContextRef.current)) return;
        // The adapter always returns a fresh authoritative page -- it wins on success AND on a
        // typed feature failure (a failed Windows apply may still have persisted the desired value).
        pruneQuickSettingsPendingAgainstPage(result.page);
        setQuickSettingsPage(result.page); quickSettingsPageRef.current = result.page;
        setError(!result?.succeeded ? (result?.failureMessage || "Quick Settings update failed") : null);
      };

      const commitQuickSettingsImmediate = async (page, section, row, nextValue) => {
        if (!state.installed || !requireQuickSettingsRowMutation(row)) return;
        const value = makeQuickSettingsValue(row, nextValue);
        if (!value) return;
        // An immediate parent toggle retires any still-pending delayed edit in the same section of
        // the same page context (CPU Boost OFF -> pending AC/DC slider; TDP OFF -> pending group
        // commit; the overall Profile toggle OFF -> every Profile subfeature timer; etc.).
        cancelQamSliderCommits((key, pending) => pending?.pageId === page.pageId && (pending?.appId ?? null) === (page.appId ?? null) && pending?.sectionId === section.sectionId);
        setBusy(true); setError(null);
        try {
          beginMutation();
          logQuickSettingsMutationRequest(page.pageId, page.appId ?? null, row.rowId);
          const result = await request("mutateQuickSetting", { pageId: page.pageId, appId: page.appId ?? null, editedRowId: row.rowId, values: [{ rowId: row.rowId, value }] });
          logQuickSettingsMutationResult(page.pageId, page.appId ?? null, row.rowId, result?.succeeded === true, result?.failureMessage || null);
          applyQuickSettingsResult(result);
          deferredInvalidationRef.current = false;
        } catch (error) {
          logQuickSettingsMutationResult(page.pageId, page.appId ?? null, row.rowId, false, error?.message || "bridge-error");
          failClosed("Quick Settings update failed");
        }
        finally { endMutation(); setBusy(false); }
      };

      const scheduleQuickSettingsCommit = (page, section, row, nextProductValue) => {
        if (!state.installed || !requireQuickSettingsRowMutation(row)) return;
        const key = quickSettingsPendingKey(page, row);
        const existing = state.qamSliderCommits?.get(key);
        let draft = existing?.quickSettingsValues
          ? { values: { ...existing.quickSettingsValues }, order: existing.quickSettingsOrder }
          : row.commitGroupId == null
            ? (() => { const seeded = makeQuickSettingsValue(row, nextProductValue); return seeded ? { values: { [row.rowId]: seeded }, order: [row.rowId] } : null; })()
            : seedQuickSettingsSectionDraft(section);
        if (!draft) return;
        const edited = makeQuickSettingsValue(row, nextProductValue);
        if (!edited) return;
        draft.values[row.rowId] = edited;
        if (row.commitGroupId != null) applyQuickSettingsLinkedConstraints(quickSettingsPageRef.current, draft.values, row.rowId);
        const values = draft.order.map(rowId => ({ rowId, value: draft.values[rowId] }));
        const delayMs = row.commitPolicy?.mode === QS_COMMIT_TRAILING ? Number(row.commitPolicy.delayMilliseconds) : 0;
        scheduleQamSliderCommit(
          key,
          { pageId: page.pageId, appId: page.appId ?? null, quickSettingsValues: draft.values, quickSettingsOrder: draft.order, sectionId: section.sectionId },
          "mutateQuickSetting",
          { pageId: page.pageId, appId: page.appId ?? null, editedRowId: row.rowId, values },
          async (result, failure) => {
            // Consume the mutation's own deferred invalidation before endMutation() can launch a
            // refresh that would overwrite this settlement (and its failureMessage).
            deferredInvalidationRef.current = false;
            if (failure) { failClosed("Quick Settings update failed"); return; }
            applyQuickSettingsResult(result);
          },
          delayMs,
          beginMutation,
          endMutation);
        // The pending Map is outside React -- force one render so quickSettingsRowEffectiveValue()
        // and any linked paired value show immediately.
        bumpQuickSettingsDraftRender(value => value + 1);
      };

      const renderQuickSettingsRow = (page, section, row) => {
        if (row.controlKind === QS_CONTROL_TOGGLE) {
          return React.createElement(native.ToggleField, {
            label: row.label,
            checked: quickSettingsRowEffectiveValue(row)?.booleanValue === true,
            controlled: true,
            disabled: !canMutateQuickSettingsRow(row),
            onChange: value => void commitQuickSettingsImmediate(page, section, row, !!value),
          });
        }
        if (row.controlKind !== QS_CONTROL_SLIDER || !row.sliderSpec) return null;
        const effective = quickSettingsRowEffectiveValue(row);
        if (effective == null) return null;
        if (row.sliderSpec.kind === QS_SLIDER_NUMERIC) {
          const numeric = Number(effective.integerValue);
          return React.createElement(native.SliderField, {
            label: row.label,
            min: row.sliderSpec.minimum, max: row.sliderSpec.maximum, step: row.sliderSpec.step || 1,
            value: numeric,
            valueSuffix: row.sliderSpec.suffix ?? "",
            showValue: true,
            showBookendLabels: true,
            disabled: !canMutateQuickSettingsRow(row),
            onChange: next => scheduleQuickSettingsCommit(page, section, row, Number(next)),
          });
        }
        const options = row.sliderSpec.options ?? [];
        const optionIndex = options.findIndex(option => Number(option.value) === Number(effective.integerValue));
        if (optionIndex < 0) return null;
        const notchLabels = options.map((option, notchIndex) => ({
          notchIndex,
          label: option.label,
          value: option.value,
        }));
        return React.createElement(native.SliderField, {
          label: row.label,
          min: 0, max: Math.max(0, options.length - 1), step: 1, value: optionIndex,
          notchCount: options.length, notchLabels, notchTicksVisible: true,
          disabled: !canMutateQuickSettingsRow(row),
          onChange: next => { const option = options[Math.round(Number(next))]; if (option) scheduleQuickSettingsCommit(page, section, row, option.value); },
        });
      };

      const sections = (quickSettingsPage?.sections ?? []).map(section => {
        const rows = (section.rows ?? [])
          .map(row => ({ key: `qs-row-${row.rowId}`, node: renderQuickSettingsRow(quickSettingsPage, section, row) }))
          .filter(entry => entry.node);
        return React.createElement(native.PanelSection, { key: `qs-section-${section.sectionId}`, title: section.label || undefined },
          ...rows.map(entry => React.createElement(native.PanelSectionRow, { key: entry.key }, entry.node)));
      });

      return React.createElement(React.Fragment, null,
        displayError ? React.createElement("p", { key: "error" }, displayError) : null,
        ...sections);
    }

    state.addonTabDescriptors[key] = {
      [TAB_MARKER]: key,
      key,
      title: key === ADDON_DEVICE_TAB_KEY ? "Device" : "Profile",
      tab: icon,
      panel: React.createElement(QuickSettingsPanel, { pageId }),
    };
    return state.addonTabDescriptors[key];
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

  // SF-V2-08 section 10.2: retires every pending entry belonging to one (PageId, AppId) context.
  function cancelQuickSettingsPendingForContext(context) {
    if (!context) return;
    cancelQamSliderCommits((key, pending) => pending?.pageId === context.pageId && (pending?.appId ?? null) === (context.appId ?? null));
  }

  function quickSettingsContextOf(page) {
    return page ? { pageId: page.pageId, appId: page.appId ?? null } : null;
  }

  function sameQuickSettingsContext(a, b) {
    return !!a && !!b && a.pageId === b.pageId && (a.appId ?? null) === (b.appId ?? null);
  }

  function subscribeStateInvalidation(callback) {
    state.stateInvalidationSubscribers ??= new Set();
    state.stateInvalidationSubscribers.add(callback);
    return () => state.stateInvalidationSubscribers?.delete(callback);
  }

  function notifyStateInvalidated() {
    for (const callback of [...(state.stateInvalidationSubscribers ?? [])]) {
      try {
        callback();
      } catch (error) {
        logOnce("stateInvalidationSubscriberFailure", `QAM state invalidation subscriber failed: ${String(error)}`);
      }
    }
  }

  // SF-V2-08 sections 13.1/14/25: a fresh authoritative same-context page retires a same-context
  // pending entry once its own edited row is no longer valid against THAT page (absent or
  // non-writable) -- this is the generic way real parent-state transitions (e.g. ProfileEnabled OFF
  // making ProfileTdp/ProfileCpuBoost/ProfilePowerMode rows non-writable) retire a stale child draft
  // without any cross-section policy or per-feature special-casing. A still-writable pending row is
  // left untouched, so it survives ordinary same-context invalidation. The central mutation adapter
  // remains the final backstop if a timer still reaches Runtime after this.
  function pruneQuickSettingsPendingAgainstPage(page) {
    if (!page) return;
    for (const [key, pending] of state.qamSliderCommits ?? []) {
      if (pending?.pageId !== page.pageId || (pending?.appId ?? null) !== (page.appId ?? null)) continue;
      const editedRowId = pending?.payload?.editedRowId;
      const row = findQuickSettingsRow(page, editedRowId);
      if (row?.available === true && row?.writable === true) continue;
      clearTimeout(pending.timer);
      state.qamSliderCommits.delete(key);
    }
  }

  // onRequestStart / onRequestEnd wrap ONLY the actual delayed RPC execution (never the debounce
  // window), so the caller can put just the in-flight mutation inside the component's existing
  // beginMutation()/endMutation() invalidation gate while the pending draft stays refreshable.
  function scheduleQamSliderCommit(key, pending, method, payload, onSettled, delayMs = QS_FALLBACK_COMMIT_DELAY_MS, onRequestStart = null, onRequestEnd = null) {
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
        let requestStarted = false;
        try {
          if (entry.pageId === QS_PAGE_PROFILE && entry.appId) {
            // A Profile draft's game must still be the active game right before the delayed RPC
            // fires -- an extra local guard alongside the central Runtime adapter's own AppId check.
            // Reuses the same generic capture seam Device uses (section 9.1) rather than a
            // legacy Profile-only bridge method.
            const currentPage = await request("captureQuickSettingsPage", { pageId: QS_PAGE_PROFILE, appId: entry.appId });
            if (!currentPage?.available || Number(currentPage.appId || 0) !== entry.appId) {
              if (state.qamSliderCommits.get(key)?.token === token) {
                state.qamSliderCommits.delete(key);
                notifyStateInvalidated();
              }
              return;
            }
          }
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          requestStarted = true;
          onRequestStart?.();
          logQuickSettingsMutationRequest(entry.pageId, entry.appId, entry.payload?.editedRowId);
          const result = await request(method, payload);
          logQuickSettingsMutationResult(entry.pageId, entry.appId, entry.payload?.editedRowId, result?.succeeded === true, result?.failureMessage || null);
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          state.qamSliderCommits.delete(key);
          await onSettled(result, null, entry);
        } catch (error) {
          if (state.qamSliderCommits.get(key)?.token !== token) return;
          state.qamSliderCommits.delete(key);
          logQuickSettingsMutationResult(entry.pageId, entry.appId, entry.payload?.editedRowId, false, error?.message || "bridge-error");
          await onSettled(null, error, entry);
        } finally {
          if (requestStarted) onRequestEnd?.();
        }
    }, Number.isFinite(delayMs) && delayMs > 0 ? delayMs : QS_FALLBACK_COMMIT_DELAY_MS);
    state.qamSliderCommits.set(key, entry);
  }

  // --- Shared Quick Settings Device/Profile helpers (SF-V2-05/08) -------------------------------
  // Pure functions over the shared page payload. The renderer treats PageId / AppId / rowId /
  // sectionId / commitGroupId as opaque stable identities; it never reconstructs Device/Profile
  // labels/order/options.

  // Section 12: identity is PageId + AppId + (RowId or CommitGroupId) so Device and every Profile
  // AppId's drafts stay isolated from one another; the exact string is a QAM-local scheduler key,
  // never product state.
  function quickSettingsPendingKey(page, row) {
    const appKey = page.appId ?? "none";
    return row.commitGroupId == null
      ? `qs-page:${page.pageId}:app:${appKey}:row:${row.rowId}`
      : `qs-page:${page.pageId}:app:${appKey}:group:${row.commitGroupId}`;
  }

  function quickSettingsPendingValue(page, rowId) {
    for (const entry of state.qamSliderCommits?.values?.() ?? []) {
      if (!entry?.quickSettingsValues) continue;
      if (entry.pageId !== page?.pageId || (entry.appId ?? null) !== (page?.appId ?? null)) continue;
      if (Object.prototype.hasOwnProperty.call(entry.quickSettingsValues, rowId)) return entry.quickSettingsValues[rowId];
    }
    return null;
  }

  function findQuickSettingsRow(page, rowId) {
    for (const section of page?.sections ?? []) {
      for (const row of section?.rows ?? []) {
        if (row.rowId === rowId) return row;
      }
    }
    return null;
  }

  function makeQuickSettingsValue(row, next) {
    const kind = row?.value?.kind;
    if (kind === QS_VALUE_BOOLEAN) return { kind, booleanValue: !!next, integerValue: null };
    if (kind === QS_VALUE_INTEGER) return { kind, booleanValue: null, integerValue: Number(next) };
    return null;
  }

  // Seeds the whole-section mutation draft in section/row payload order. For the current closed
  // Device/Profile model the only grouped sections are the two TDP sections, whose section carries
  // exactly the five values (Enabled + four PL sliders) that QuickSettingsMutationAdapter requires --
  // with no JS knowledge of the individual TDP RowIds.
  function seedQuickSettingsSectionDraft(section) {
    const values = {};
    const order = [];
    for (const row of section?.rows ?? []) {
      if (row.value == null) return null;
      values[row.rowId] = { ...row.value };
      order.push(row.rowId);
    }
    return { values, order };
  }

  function applyQuickSettingsLinkedConstraints(page, values, editedRowId) {
    for (const constraint of page?.linkedSliderConstraints ?? []) {
      if (constraint.lowerRowId !== editedRowId && constraint.upperRowId !== editedRowId) continue;
      const lowerRow = findQuickSettingsRow(page, constraint.lowerRowId);
      const upperRow = findQuickSettingsRow(page, constraint.upperRowId);
      if (!lowerRow?.sliderSpec || !upperRow?.sliderSpec) continue;
      const lower = values[constraint.lowerRowId]?.integerValue;
      const upper = values[constraint.upperRowId]?.integerValue;
      const gap = Number(constraint.minimumGap);
      if (!Number.isFinite(lower) || !Number.isFinite(upper) || !(gap > 0)) continue;
      if (constraint.lowerRowId === editedRowId && upper < lower + gap) {
        if (lower + gap <= upperRow.sliderSpec.maximum) values[constraint.upperRowId] = { ...values[constraint.upperRowId], integerValue: lower + gap };
        else values[constraint.lowerRowId] = { ...values[constraint.lowerRowId], integerValue: upperRow.sliderSpec.maximum - gap };
      } else if (constraint.upperRowId === editedRowId && lower > upper - gap) {
        if (upper - gap >= lowerRow.sliderSpec.minimum) values[constraint.lowerRowId] = { ...values[constraint.lowerRowId], integerValue: upper - gap };
        else values[constraint.upperRowId] = { ...values[constraint.upperRowId], integerValue: lowerRow.sliderSpec.minimum + gap };
      }
    }
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
    if (kind === "state-invalidated") notifyStateInvalidated();
  }

  function retireBridgeConsumers() {
    cancelQamSliderCommits();
    for (const pending of state.bridgePending?.values() ?? []) {
      try { pending.reject(new Error("QAM bridge stopped")); } catch (_) {}
    }
    state.bridgePending?.clear();
    state.stateInvalidationSubscribers?.clear();
  }

  function install() {
    if (state.installed) {
      log("install() called but already installed; no-op.");
      return true;
    }

    // A tab descriptor closes over the React/native components and panel implementation from the
    // script generation that created it. Never reuse it across uninstall/reinstall or upgrades.
    state.addonTabDescriptor = null;
    state.addonTabDescriptors = null;
    state.initialTabSelectionOwners = new WeakSet();
    state.stateInvalidationSubscribers?.clear();
    state.diagnostics = {};
    state.runtimeDiagnostics = {};
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
    state.addonTabDescriptor = null;
    state.addonTabDescriptors = null;
    state.initialTabSelectionOwners = null;
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
      addonTabDescriptor: null,
      addonTabDescriptors: null,
      initialTabSelectionOwners: null,
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
