using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

public sealed record QamGeometryClassNames(string PanelOuterNav, string TabGroupPanel, string? ViewPlaceholder = null);

/// <summary>Builds the read-only DOM measurement used only in Steam Quick Access targets.</summary>
public static class QuickAccessGeometryDiagnostic
{
    public static string CreateExpression(QamGeometryClassNames classNames, string? activeTab = null) => $$"""
        (() => {
          const panelClass = {{JsonSerializer.Serialize(classNames.PanelOuterNav)}};
          const tabGroupClass = {{JsonSerializer.Serialize(classNames.TabGroupPanel)}};
          const requestedActiveTab = {{JsonSerializer.Serialize(activeTab)}};
          if (typeof document === "undefined" || typeof document.getElementsByClassName !== "function")
            return JSON.stringify({ Realm: "QuickAccessTarget", Error: "document-unavailable" });

          function describe(element) {
            if (!element) return null;
            try {
              const rect = element.getBoundingClientRect();
              const computed = window.getComputedStyle(element);
              return {
                tag: element.tagName,
                id: element.id || "",
                className: String(element.className || ""),
                x: rect.x,
                y: rect.y,
                width: rect.width,
                right: rect.right,
                height: rect.height,
                bottom: rect.bottom,
                widthCss: computed.width,
                maxWidth: computed.maxWidth,
                minWidth: computed.minWidth,
                paddingLeft: computed.paddingLeft,
                paddingRight: computed.paddingRight,
                marginLeft: computed.marginLeft,
                marginRight: computed.marginRight,
                gap: computed.gap,
                rowGap: computed.rowGap,
                columnGap: computed.columnGap,
                display: computed.display,
                boxSizing: computed.boxSizing,
                flexDirection: computed.flexDirection,
                transform: computed.transform,
                position: computed.position,
                overflow: computed.overflow,
                overflowX: computed.overflowX,
                overflowY: computed.overflowY,
                visible: rect.width > 0 && rect.height > 0,
              };
            } catch (error) {
              return { error: String(error) };
            }
          }

          function ancestors(element) {
            const result = [];
            let current = element;
            for (let depth = 0; current && depth < 6; depth++, current = current.parentElement)
              result.push({ depth, geometry: describe(current) });
            return result;
          }

          function candidates(className) {
            return Array.from(document.getElementsByClassName(className), (element, index) => ({
              index,
              geometry: describe(element),
              ancestors: ancestors(element),
            }));
          }

          function layoutTree(root) {
            if (!root) return [];
            const result = [];
            const queue = [{ element: root, depth: 0, path: "root" }];
            const visited = new Set();
            const budget = 160;
            while (queue.length > 0 && result.length < budget) {
              const current = queue.shift();
              const element = current.element;
              if (!element || visited.has(element)) continue;
              visited.add(element);
              result.push({
                path: current.path,
                depth: current.depth,
                geometry: describe(element),
                role: element.getAttribute?.("role") || "",
                ariaSelected: element.getAttribute?.("aria-selected") || "",
                ariaHidden: element.getAttribute?.("aria-hidden") || "",
              });
              if (current.depth >= 5) continue;
              Array.from(element.children || []).slice(0, 32).forEach((child, index) =>
                queue.push({ element: child, depth: current.depth + 1, path: `${current.path}.${index}` }));
            }
            return result;
          }

          const tabGroupElements = Array.from(document.getElementsByClassName(tabGroupClass));
          const visibleTabGroup = tabGroupElements.find(element => describe(element)?.visible) || tabGroupElements[0] || null;

          return JSON.stringify({
            Realm: "QuickAccessTarget",
            ActiveTab: requestedActiveTab,
            PanelOuterNav: { className: panelClass, candidates: candidates(panelClass) },
            TabGroupPanel: {
              className: tabGroupClass,
              candidates: candidates(tabGroupClass),
              layoutTree: layoutTree(visibleTabGroup),
            },
          });
        })()
        """;
}
