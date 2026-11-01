using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NovaBrowser.Models;
using NovaBrowser.Services;

namespace NovaBrowser;

public partial class SettingsWindow : Window
{
    public SettingsWindow(BrowserSettings settings)
    {
        InitializeComponent();

        SearchEngineCombo.ItemsSource = BrowserData.SearchEngines;
        SearchEngineCombo.SelectedItem = settings.SearchEngine;
        HomePageBox.Text = settings.HomePage;
        DownloadsFolderBox.Text = settings.DownloadsFolder;
        ShowBookmarksCheck.IsChecked = settings.ShowBookmarksBar;
        RestoreSessionCheck.IsChecked = settings.RestoreSession;
        DarkThemeCheck.IsChecked = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
    }

    public BrowserSettings ResultSettings { get; private set; } = new();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var engine = SearchEngineCombo.SelectedItem as string ?? "Google";
        var home = HomePageBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(home) &&
            !home.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !home.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !home.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            !home.StartsWith("nova://", StringComparison.OrdinalIgnoreCase))
        {
            home = "https://" + home;
        }

        var downloads = DownloadsFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(downloads))
        {
            downloads = BrowserSettings.DefaultDownloadsFolder;
        }

        try
        {
            Directory.CreateDirectory(downloads);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось использовать выбранную папку для загрузок.\n\n" + ex.Message,
                "Nova Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultSettings = new BrowserSettings
        {
            SearchEngine = engine,
            HomePage = home,
            DownloadsFolder = downloads,
            ShowBookmarksBar = ShowBookmarksCheck.IsChecked == true,
            RestoreSession = RestoreSessionCheck.IsChecked == true,
            Theme = DarkThemeCheck.IsChecked == true ? "Dark" : "Light"
        };

        DialogResult = true;
    }

    private void BrowseDownloads_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для загрузок",
            InitialDirectory = Directory.Exists(DownloadsFolderBox.Text)
                ? DownloadsFolderBox.Text
                : BrowserSettings.DefaultDownloadsFolder
        };

        if (dialog.ShowDialog(this) == true)
        {
            DownloadsFolderBox.Text = dialog.FolderName;
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Удалить всю историю посещений?", "Nova Browser",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            App.Data.ClearHistory();
            MessageBox.Show("История очищена.", "Nova Browser", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SearchEngineCombo.SelectedItem = "Google";
        HomePageBox.Text = "";
        DownloadsFolderBox.Text = BrowserSettings.DefaultDownloadsFolder;
        ShowBookmarksCheck.IsChecked = true;
        RestoreSessionCheck.IsChecked = true;
        DarkThemeCheck.IsChecked = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore drag errors.
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
