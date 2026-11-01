using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using NovaBrowser.Models;

namespace NovaBrowser.Services;

public sealed class BrowserData
{
    public const string NewTabUrl = "https://nova.home/newtab.html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<HistoryEntry> _history = new();

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaBrowser");

    public static string ProfileDirectory { get; } = Path.Combine(DataRoot, "Profile");
    public static string AssetsDirectory { get; } = Path.Combine(DataRoot, "Assets");

    private static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    private static string BookmarksFile => Path.Combine(DataRoot, "bookmarks.json");
    private static string HistoryFile => Path.Combine(DataRoot, "history.json");
    private static string SessionFile => Path.Combine(DataRoot, "session.json");

    public BrowserSettings Settings { get; set; } = new();
    public ObservableCollection<Bookmark> Bookmarks { get; } = new();
    public IReadOnlyList<HistoryEntry> History => _history;

    public static IReadOnlyList<string> SearchEngines { get; } = new[] { "Google", "Яндекс", "Bing", "DuckDuckGo" };

    private BrowserData()
    {
    }

    public static BrowserData Load()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ProfileDirectory);
        ExtractAssets();

        var data = new BrowserData();
        data.Settings = ReadJson<BrowserSettings>(SettingsFile) ?? new BrowserSettings();
        data.Settings.SearchEngine = SearchEngines.Contains(data.Settings.SearchEngine)
            ? data.Settings.SearchEngine
            : "Google";
        data.Settings.Theme = string.Equals(data.Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? "Dark"
            : "Light";

        if (string.IsNullOrWhiteSpace(data.Settings.DownloadsFolder))
        {
            data.Settings.DownloadsFolder = BrowserSettings.DefaultDownloadsFolder;
        }

        var bookmarks = ReadJson<List<Bookmark>>(BookmarksFile) ?? new List<Bookmark>();
        foreach (var bookmark in bookmarks.Where(b => !string.IsNullOrWhiteSpace(b.Url)))
        {
            data.Bookmarks.Add(bookmark);
        }

        var history = ReadJson<List<HistoryEntry>>(HistoryFile) ?? new List<HistoryEntry>();
        data._history.AddRange(history.Where(h => !string.IsNullOrWhiteSpace(h.Url)));

        CleanupStalePrivateProfiles();
        return data;
    }

    private static void CleanupStalePrivateProfiles()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "NovaBrowserPrivate");
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // The profile may still be used by a running private window.
                }
            }
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    public static BrowserData CreateFallback()
    {
        var data = new BrowserData();
        try
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ProfileDirectory);
            ExtractAssets();
        }
        catch
        {
            // Continue with in-memory defaults.
        }

        return data;
    }

    public static string BuildSearchUrl(string engine, string query)
    {
        var encoded = Uri.EscapeDataString(query.Trim());
        return engine switch
        {
            "Яндекс" => "https://yandex.ru/search/?text=" + encoded,
            "Bing" => "https://www.bing.com/search?q=" + encoded,
            "DuckDuckGo" => "https://duckduckgo.com/?q=" + encoded,
            _ => "https://www.google.com/search?q=" + encoded
        };
    }

    public void SaveSettings()
    {
        WriteJson(SettingsFile, Settings);
    }

    public void SaveBookmarks()
    {
        WriteJson(BookmarksFile, Bookmarks.ToList());
    }

    public void SaveHistory()
    {
        WriteJson(HistoryFile, _history);
    }

    public bool IsBookmarked(string url)
        => Bookmarks.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    public bool ToggleBookmark(string title, string url)
    {
        var existing = Bookmarks.FirstOrDefault(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Bookmarks.Remove(existing);
            SaveBookmarks();
            return false;
        }

        Bookmarks.Insert(0, new Bookmark
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
            Url = url,
            AddedAt = DateTime.Now
        });
        SaveBookmarks();
        return true;
    }

    public void RemoveBookmark(Bookmark bookmark)
    {
        Bookmarks.Remove(bookmark);
        SaveBookmarks();
    }

    public void RecordVisit(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("https://nova.home/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var latest = _history.FirstOrDefault();
        if (latest is not null &&
            string.Equals(latest.Url, url, StringComparison.OrdinalIgnoreCase) &&
            DateTime.Now - latest.VisitedAt < TimeSpan.FromSeconds(15))
        {
            latest.VisitedAt = DateTime.Now;
            latest.Title = string.IsNullOrWhiteSpace(title) ? latest.Title : title;
        }
        else
        {
            _history.Insert(0, new HistoryEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                VisitedAt = DateTime.Now
            });
        }

        if (_history.Count > 3000)
        {
            _history.RemoveRange(3000, _history.Count - 3000);
        }

        SaveHistory();
    }

    public void RemoveHistory(HistoryEntry entry)
    {
        _history.Remove(entry);
        SaveHistory();
    }

    public void ClearHistory()
    {
        _history.Clear();
        SaveHistory();
    }

    public void SaveSession(IEnumerable<SessionTab> tabs, int activeIndex)
    {
        WriteJson(SessionFile, new SessionState
        {
            Tabs = tabs.ToList(),
            ActiveIndex = activeIndex
        });
    }

    public SessionState? LoadSession()
    {
        return ReadJson<SessionState>(SessionFile);
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось прочитать {Path.GetFileName(path)}", ex);
            return default;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось сохранить {Path.GetFileName(path)}", ex);
        }
    }

    private static void ExtractAssets()
    {
        Directory.CreateDirectory(AssetsDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("NovaBrowser.Assets.", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = name["NovaBrowser.Assets.".Length..];
            if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            using var file = File.Create(Path.Combine(AssetsDirectory, fileName));
            stream.CopyTo(file);
        }
    }
}
