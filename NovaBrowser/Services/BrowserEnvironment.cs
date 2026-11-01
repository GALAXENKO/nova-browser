using Microsoft.Web.WebView2.Core;

namespace NovaBrowser.Services;

public static class BrowserEnvironment
{
    private static readonly object Sync = new();
    private static Task<CoreWebView2Environment>? _sharedTask;

    public static Task<CoreWebView2Environment> GetSharedAsync()
    {
        lock (Sync)
        {
            return _sharedTask ??= CoreWebView2Environment.CreateAsync(null, BrowserData.ProfileDirectory);
        }
    }

    public static Task<CoreWebView2Environment> CreatePrivateAsync(string profileDirectory)
        => CoreWebView2Environment.CreateAsync(null, profileDirectory);
}
