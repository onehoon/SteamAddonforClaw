using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Builds the BPM-target expression that owns the native BrowserView bounds seam for
/// ViewPlaceholder. React Fiber types are read only to locate the owner; the expression does not
/// mutate current or alternate Fibers and does not rewrite React or DOM styles.
/// </summary>
public static class QamHostTransformPatcher
{
    public static string CreateApplyExpression(string viewPlaceholderClass, bool addonSelected) =>
        CreateExpression(viewPlaceholderClass, addonSelected, uninstall: false);

    public static string CreateUninstallExpression(string viewPlaceholderClass) =>
        CreateExpression(viewPlaceholderClass, addonSelected: false, uninstall: true);

    private static string CreateExpression(string viewPlaceholderClass, bool addonSelected, bool uninstall) => $$"""
        (() => {
          const CLASS_NAME = {{JsonSerializer.Serialize(viewPlaceholderClass)}};
          const ADDON_SELECTED = {{addonSelected.ToString().ToLowerInvariant()}};
          const UNINSTALL = {{uninstall.ToString().ToLowerInvariant()}};
          const ADDON_CONTENT_WIDTH = 400;
          const NATIVE_TAB_RAIL_WIDTH = 48;
          const GLOBAL_KEY = "__STEAM_INPUT_ADDON_QAM_HOST__";
          const state = window[GLOBAL_KEY] || (window[GLOBAL_KEY] = {
            installed: false,
            addonSelected: false,
            browser: null,
            originalSetBounds: null,
            patchedSetBounds: null,
            stockX: null,
            addonOffsetX: null,
            diagnostic: null,
          });

          function findPlaceholderElement() {
            if (typeof document === "undefined" || typeof document.getElementsByClassName !== "function") return null;
            return document.getElementsByClassName(CLASS_NAME)[0] || null;
          }

          function findReactFiber(element) {
            if (!element) return null;
            for (const key of Object.keys(element)) {
              if (key.startsWith("__reactFiber$") || key.startsWith("__reactInternalInstance$"))
                return element[key];
            }
            return null;
          }

          function isComponentType(type) {
            return typeof type === "function" ||
              (type && typeof type === "object" && typeof type.render === "function");
          }

          function findOwnerFiber(fiber) {
            for (let current = fiber?.return, depth = 0; current && depth < 24; current = current.return, depth++) {
              if (isComponentType(current.type)) return current;
            }
            return null;
          }

          function desiredLeft() {
            const viewportWidth = Number(window.innerWidth);
            if (!Number.isFinite(viewportWidth)) return null;
            return viewportWidth - NATIVE_TAB_RAIL_WIDTH - ADDON_CONTENT_WIDTH;
          }

          function browserForOwner(owner) {
            const props = owner?.memoizedProps || owner?.pendingProps || null;
            const browser = props?.browser;
            return browser && typeof browser.SetBounds === "function" ? browser : null;
          }

          function restoreBoundsPatch() {
            const browser = state.browser;
            if (browser && browser.SetBounds === state.patchedSetBounds) {
              try { browser.SetBounds = state.originalSetBounds; } catch (_) { }
            }
            state.browser = null;
            state.originalSetBounds = null;
            state.patchedSetBounds = null;
            state.stockX = null;
            state.addonOffsetX = null;
            state.installed = false;
          }

          function captureStockGeometry(element) {
            try {
              const rect = element?.getBoundingClientRect?.();
              const left = Number(rect?.left);
              const targetLeft = desiredLeft();
              if (Number.isFinite(left)) {
                state.stockX = left;
                state.addonOffsetX = Number.isFinite(targetLeft) ? targetLeft - left : null;
              }
            } catch (_) { }
          }

          function installBoundsPatch(owner, element) {
            const browser = browserForOwner(owner);
            if (!browser) return false;
            if (state.browser === browser && browser.SetBounds === state.patchedSetBounds) return true;

            restoreBoundsPatch();
            captureStockGeometry(element);
            const originalSetBounds = browser.SetBounds;
            const patchedSetBounds = function (x, y, width, height) {
              let nextX = x;
              const numericX = Number(x);
              if (state.addonSelected && Number.isFinite(state.addonOffsetX) && Number.isFinite(numericX)) {
                nextX = numericX + state.addonOffsetX;
              } else if (!state.addonSelected && Number.isFinite(numericX)) {
                state.stockX = numericX;
              }
              return originalSetBounds.call(this, nextX, y, width, height);
            };

            try { browser.SetBounds = patchedSetBounds; }
            catch (_) { return false; }
            if (browser.SetBounds !== patchedSetBounds) return false;
            state.browser = browser;
            state.originalSetBounds = originalSetBounds;
            state.patchedSetBounds = patchedSetBounds;
            state.installed = true;
            state.diagnostic = "bounds-patched";
            return true;
          }

          function applyCurrentBounds(element) {
            const browser = state.browser;
            const originalSetBounds = state.originalSetBounds;
            if (!browser || typeof originalSetBounds !== "function") return false;
            let rect;
            try { rect = element?.getBoundingClientRect?.() || null; } catch (_) { return false; }
            if (!rect || !Number.isFinite(Number(rect.width)) || Number(rect.width) <= 0) return false;
            const stockX = Number.isFinite(state.stockX) ? state.stockX : Number(rect.left);
            if (!Number.isFinite(stockX)) return false;
            try {
              if (state.addonSelected)
                browser.SetBounds(stockX, rect.top, rect.width, rect.height);
              else
                originalSetBounds.call(browser, stockX, rect.top, rect.width, rect.height);
              state.diagnostic = state.addonSelected ? "bounds-addon-applied" : "bounds-native-restored";
              return true;
            } catch (_) {
              return false;
            }
          }

          function applySelection(addonSelected) {
            const wasAddonSelected = state.addonSelected;
            state.addonSelected = addonSelected;
            const element = findPlaceholderElement();
            const fiber = findReactFiber(element);
            const owner = findOwnerFiber(fiber);
            if (addonSelected && !wasAddonSelected) captureStockGeometry(element);
            const boundsPatched = installBoundsPatch(owner, element);
            const immediateApplied = applyCurrentBounds(element);
            return JSON.stringify({
              Installed: state.installed,
              BoundsPatched: boundsPatched,
              ImmediateApplied: immediateApplied,
              AddonSelected: state.addonSelected,
              Diagnostic: state.diagnostic,
            });
          }

          if (UNINSTALL) {
            state.addonSelected = false;
            applyCurrentBounds(findPlaceholderElement());
            restoreBoundsPatch();
            return JSON.stringify({ Installed: false, Uninstalled: true, Diagnostic: state.diagnostic });
          }

          return applySelection(ADDON_SELECTED);
        })()
        """;
}
