using System.Text.RegularExpressions;

namespace NovaBrowser.Services;

public static partial class NavigationHelper
{
    public static string NormalizeInput(string input, string searchEngine)
    {
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BrowserData.NewTabUrl;
        }

        if (text.Equals("nova://newtab", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("nova://home", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserData.NewTabUrl;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)))
        {
            return absolute.ToString();
        }

        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + text;
        }

        if (LooksLikeHost(text))
        {
            return "https://" + text;
        }

        return BrowserData.BuildSearchUrl(searchEngine, text);
    }

    public static bool LooksLikeHost(string text)
    {
        if (text.Any(char.IsWhiteSpace) || text.Contains('/') || text.Contains('\\'))
        {
            return false;
        }

        if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IpWithOptionalPort().IsMatch(text))
        {
            return true;
        }

        if (!text.Contains('.'))
        {
            return false;
        }

        return Uri.TryCreate("https://" + text, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Host) &&
               uri.Host.Contains('.');
    }

    public static string SearchUrl(string engine, string query) => BrowserData.BuildSearchUrl(engine, query);

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}(:\d{1,5})?$")]
    private static partial Regex IpWithOptionalPort();
}
