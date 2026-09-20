using System.Text.Json;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>Builds the read-only DOM measurement used only in likely Steam QAM host targets.</summary>
public static class QamHostGeometryDiagnostic
{
    public static string CreateExpression(QamGeometryClassNames classNames) => $$"""
        (() => {
          const placeholderClass = {{JsonSerializer.Serialize(classNames.ViewPlaceholder)}};
          if (typeof document === "undefined" || typeof document.getElementsByClassName !== "function")
            return JSON.stringify({ Realm: "QamHostTarget", Error: "document-unavailable" });

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
                transformOrigin: computed.transformOrigin,
                position: computed.position,
                overflow: computed.overflow,
                overflowX: computed.overflowX,
                overflowY: computed.overflowY,
                display: computed.display,
                visibility: computed.visibility,
                opacity: computed.opacity,
                clipPath: computed.clipPath,
                clientWidth: element.clientWidth,
                scrollWidth: element.scrollWidth,
                offsetWidth: element.offsetWidth,
                clientLeft: element.clientLeft,
                offsetLeft: element.offsetLeft,
                visible: rect.width > 0 && rect.height > 0,
              };
            } catch (error) {
              return { error: String(error) };
            }
          }

          function ancestors(element) {
            const result = [];
            let current = element;
            for (let depth = 0; current && depth < 8; depth++, current = current.parentElement)
              result.push({ depth, geometry: describe(current) });
            return result;
          }

          function candidates(className) {
            if (!className) return [];
            return Array.from(document.getElementsByClassName(className), (element, index) => ({
              index,
              geometry: describe(element),
              ancestors: ancestors(element),
            }));
          }

          function cssVariable(name) {
            try {
              return window.getComputedStyle(document.documentElement).getPropertyValue(name).trim();
            } catch (_) {
              return "";
            }
          }

          return JSON.stringify({
            Realm: "QamHostTarget",
            ViewPlaceholder: {
              className: placeholderClass,
              candidates: candidates(placeholderClass),
            },
            Document: {
              innerWidth: window.innerWidth,
              innerHeight: window.innerHeight,
              devicePixelRatio: window.devicePixelRatio,
              documentElement: describe(document.documentElement),
              body: describe(document.body),
              cssVariables: {
                floatingSidePanelWidth: cssVariable("--vrgamepadui-floating-side-panel-width"),
              },
            },
          });
        })()
        """;
}
