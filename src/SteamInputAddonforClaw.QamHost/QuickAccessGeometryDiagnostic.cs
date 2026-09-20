using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

public sealed record QamGeometryClassNames(string PanelOuterNav, string TabGroupPanel, string? ViewPlaceholder = null);

/// <summary>Builds the read-only DOM measurement used only in Steam Quick Access targets.</summary>
public static class QuickAccessGeometryDiagnostic
{
    public static string CreateExpression(QamGeometryClassNames classNames) => $$"""
        (() => {
          const panelClass = {{JsonSerializer.Serialize(classNames.PanelOuterNav)}};
          const tabGroupClass = {{JsonSerializer.Serialize(classNames.TabGroupPanel)}};
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
                transform: computed.transform,
                position: computed.position,
                overflow: computed.overflow,
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

          return JSON.stringify({
            Realm: "QuickAccessTarget",
            PanelOuterNav: { className: panelClass, candidates: candidates(panelClass) },
            TabGroupPanel: { className: tabGroupClass, candidates: candidates(tabGroupClass) },
          });
        })()
        """;
}
