using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace SteamInputAddonforClaw.UI;

internal static class MainUiStrings
{
    private static readonly Lazy<ResourceManager?> Resource = new(CreateResourceManager);

    internal static string Get(string key, string englishFallback)
    {
        try
        {
            var value = Resource.Value?.MainResourceMap
                .GetValue("Resources/" + key)
                .ValueAsString;
            return string.IsNullOrEmpty(value) ? englishFallback : value;
        }
        catch (Exception)
        {
            return englishFallback;
        }
    }

    internal static string Format(string key, string englishFallback, params object?[] args)
    {
        var value = Get(key, englishFallback);
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, value, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentUICulture, englishFallback, args);
        }
    }

    private static ResourceManager? CreateResourceManager()
    {
        try
        {
            var priPath = Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.UI.pri");
            return new ResourceManager(priPath);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
