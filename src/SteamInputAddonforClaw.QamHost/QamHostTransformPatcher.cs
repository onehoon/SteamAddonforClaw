using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Builds the BPM-target expression that patches the React producer owning ViewPlaceholder.
/// The expression never writes DOM style/class state; it rewrites the producer's returned React
/// element and restores the exact original component type on uninstall.
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
            patchedFiber: null,
            originalType: null,
            originalElementType: null,
            patchedType: null,
            originalStyle: null,
            originalTransform: null,
            appliedTransform: null,
            diagnostic: null,
          });

          function hasPlaceholderClass(value) {
            return typeof value === "string" && value.split(/\s+/).includes(CLASS_NAME);
          }

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

          function parseMatrix(value) {
            if (typeof value !== "string") return null;
            const matrix = value.match(/^matrix\(\s*([^)]*)\)$/);
            if (matrix) {
              const values = matrix[1].split(",").map(Number);
              return values.length === 6 && values.every(Number.isFinite)
                ? { kind: "matrix", values }
                : null;
            }
            const matrix3d = value.match(/^matrix3d\(\s*([^)]*)\)$/);
            if (matrix3d) {
              const values = matrix3d[1].split(",").map(Number);
              return values.length === 16 && values.every(Number.isFinite)
                ? { kind: "matrix3d", values }
                : null;
            }
            const translateX = value.match(/^translateX\(\s*(-?\d+(?:\.\d+)?)px\s*\)$/);
            if (translateX) return { kind: "translateX", x: Number(translateX[1]) };
            const translate3d = value.match(/^translate3d\(\s*(-?\d+(?:\.\d+)?)px\s*,\s*(-?\d+(?:\.\d+)?)px\s*,\s*(-?\d+(?:\.\d+)?)px\s*\)$/);
            if (translate3d) return { kind: "translate3d", x: Number(translate3d[1]), y: Number(translate3d[2]), z: Number(translate3d[3]) };
            return null;
          }

          function transformX(parsed) {
            if (!parsed) return null;
            if (parsed.kind === "matrix" || parsed.kind === "matrix3d") return parsed.values[parsed.kind === "matrix" ? 4 : 12];
            return parsed.x;
          }

          function withTransformX(value, x) {
            const parsed = parseMatrix(value);
            if (!parsed || !Number.isFinite(x)) return null;
            if (parsed.kind === "matrix") {
              const values = [...parsed.values];
              values[4] = x;
              return `matrix(${values.join(", ")})`;
            }
            if (parsed.kind === "matrix3d") {
              const values = [...parsed.values];
              values[12] = x;
              return `matrix3d(${values.join(", ")})`;
            }
            if (parsed.kind === "translateX") return `translateX(${x}px)`;
            return `translate3d(${x}px, ${parsed.y}px, ${parsed.z}px)`;
          }

          function cloneWithChildren(value, children) {
            if (!value || typeof value !== "object" || !value.props) return value;
            return { ...value, props: { ...value.props, children } };
          }

          function currentComputedTransform(element) {
            try {
              return window.getComputedStyle(element).transform;
            } catch (_) {
              return null;
            }
          }

          function desiredTransform(element, sourceTransform) {
            const parsed = parseMatrix(sourceTransform);
            const sourceX = transformX(parsed);
            if (!Number.isFinite(sourceX)) return null;
            const viewportWidth = Number(window.innerWidth);
            const offsetLeft = Number(element.offsetLeft);
            if (!Number.isFinite(viewportWidth) || !Number.isFinite(offsetLeft)) return null;
            const desiredLeft = viewportWidth - NATIVE_TAB_RAIL_WIDTH - ADDON_CONTENT_WIDTH;
            const desiredX = desiredLeft - offsetLeft;
            return withTransformX(sourceTransform, desiredX);
          }

          function rewrite(value, addonSelected, context) {
            if (context.budget-- <= 0 || value == null) return value;
            if (Array.isArray(value)) return value.map(child => rewrite(child, addonSelected, context));
            if (typeof value !== "object" || !value.props) return value;

            const props = value.props;
            let rewritten = value;
            if (hasPlaceholderClass(props.className)) {
              const sourceStyle = props.style && typeof props.style === "object" ? props.style : null;
              const sourceTransform = sourceStyle?.transform || currentComputedTransform(context.element);
              if (!context.originalStyle || context.appliedTransform !== sourceTransform) {
                if (sourceStyle && sourceTransform !== context.appliedTransform) {
                  context.originalStyle = { ...sourceStyle };
                  context.originalTransform = sourceTransform;
                }
              }

              if (addonSelected) {
                const baseTransform = context.originalTransform || sourceTransform;
                const shiftedTransform = desiredTransform(context.element, baseTransform);
                if (shiftedTransform) {
                  const appliedStyle = { ...(context.originalStyle || sourceStyle || {}), transform: shiftedTransform };
                  context.appliedTransform = shiftedTransform;
                  rewritten = { ...value, props: { ...props, style: appliedStyle } };
                  state.diagnostic = `applied:${String(transformX(parseMatrix(baseTransform)))}->${String(transformX(parseMatrix(shiftedTransform)))}`;
                } else {
                  state.diagnostic = "transform-unresolved";
                }
              } else if (context.originalStyle && props.style?.transform === context.appliedTransform) {
                rewritten = { ...value, props: { ...props, style: context.originalStyle } };
                state.diagnostic = "restored";
              }
            }

            const children = rewritten.props?.children;
            if (Array.isArray(children)) {
              const nextChildren = children.map(child => rewrite(child, addonSelected, context));
              if (nextChildren.some((child, index) => child !== children[index])) rewritten = cloneWithChildren(rewritten, nextChildren);
            } else if (children && typeof children === "object") {
              const nextChild = rewrite(children, addonSelected, context);
              if (nextChild !== children) rewritten = cloneWithChildren(rewritten, nextChild);
            }
            return rewritten;
          }

          function rewriteResult(value) {
            const context = {
              budget: 128,
              element: findPlaceholderElement(),
              originalStyle: state.originalStyle,
              originalTransform: state.originalTransform,
              appliedTransform: state.appliedTransform,
            };
            const rewritten = rewrite(value, state.addonSelected, context);
            state.originalStyle = context.originalStyle;
            state.originalTransform = context.originalTransform;
            state.appliedTransform = context.appliedTransform;
            return rewritten;
          }

          function wrapOwner(owner) {
            if (!owner || !isComponentType(owner.type)) return false;
            if (state.patchedFiber === owner) return true;
            if (state.patchedFiber) restoreOwner();

            const originalType = owner.type;
            let patchedType;
            if (typeof originalType === "function" && originalType.prototype?.isReactComponent) {
              patchedType = class SteamInputAddonQamHostPatched extends originalType {
                render() {
                  return rewriteResult(super.render());
                }
              };
            } else if (typeof originalType === "function") {
              patchedType = function SteamInputAddonQamHostPatched(...args) {
                return rewriteResult(originalType.apply(this, args));
              };
            } else if (originalType && typeof originalType.render === "function") {
              patchedType = { ...originalType, render: function (...args) {
                return rewriteResult(originalType.render.apply(this, args));
              } };
            } else {
              return false;
            }

            state.patchedFiber = owner;
            state.originalType = originalType;
            state.originalElementType = owner.elementType;
            state.patchedType = patchedType;
            owner.type = patchedType;
            owner.elementType = patchedType;
            state.installed = true;
            return true;
          }

          function ensureOwnerPatched() {
            const element = findPlaceholderElement();
            const fiber = findReactFiber(element);
            const owner = findOwnerFiber(fiber);
            return wrapOwner(owner);
          }

          function restoreOwner() {
            const owner = state.patchedFiber;
            if (owner) {
              if (owner.type === state.patchedType) owner.type = state.originalType;
              if (owner.elementType === state.patchedType) owner.elementType = state.originalElementType;
            }
            state.patchedFiber = null;
            state.originalType = null;
            state.originalElementType = null;
            state.patchedType = null;
            state.originalStyle = null;
            state.originalTransform = null;
            state.appliedTransform = null;
            state.installed = false;
          }

          if (UNINSTALL) {
            restoreOwner();
            return JSON.stringify({ Installed: false, Uninstalled: true, Diagnostic: state.diagnostic });
          }

          state.addonSelected = ADDON_SELECTED;
          const patched = ensureOwnerPatched();
          return JSON.stringify({
            Installed: state.installed,
            Patched: patched,
            AddonSelected: state.addonSelected,
            Diagnostic: state.diagnostic,
          });
        })()
        """;
}
