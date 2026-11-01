using System.IO;
using System.Text.Json.Serialization;

namespace NovaBrowser.Models;

public sealed class BrowserSettings
{
    public string SearchEngine { get; set; } = "Google";
    public string HomePage { get; set; } = "";
    public string DownloadsFolder { get; set; } = DefaultDownloadsFolder;
    public bool ShowBookmarksBar { get; set; } = true;
    public bool RestoreSession { get; set; } = true;
    public string Theme { get; set; } = "Light";

    [JsonIgnore]
    public static string DefaultDownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public BrowserSettings Copy() => new()
    {
        SearchEngine = SearchEngine,
        HomePage = HomePage,
        DownloadsFolder = DownloadsFolder,
        ShowBookmarksBar = ShowBookmarksBar,
        RestoreSession = RestoreSession,
        Theme = Theme
    };
}
