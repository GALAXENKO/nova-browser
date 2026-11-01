using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NovaBrowser.Models;
using NovaBrowser.Services;

namespace NovaBrowser;

public partial class MainWindow : Window
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, ImageSource?> FaviconCache = new(StringComparer.OrdinalIgnoreCase);

    private const string ShortcutScript = """
        (() => {
          if (window.__novaShortcutInstalled) return;
          window.__novaShortcutInstalled = true;
          window.addEventListener('keydown', event => {
            if (!(window.chrome && window.chrome.webview)) return;
            const c = event.ctrlKey, s = event.shiftKey, a = event.altKey, k = event.keyCode;
            let intercept = false;
            if (c && s) {
              intercept = [84, 78, 79, 9, 73, 74, 66].includes(k);
            } else if (c && !s) {
              intercept = [84, 87, 78, 76, 68, 72, 74, 70, 82, 9, 48, 187, 189, 107, 109,
                           49, 50, 51, 52, 53, 54, 55, 56, 57].includes(k);
            } else if (a && !c) {
              intercept = [36, 37, 39, 69].includes(k);
            } else if (!c && !a) {
              intercept = [116, 122, 123].includes(k);
            }
            if (!intercept) return;
            event.preventDefault();
            event.stopPropagation();
            window.chrome.webview.postMessage({ type: 'shortcut', code: k, ctrl: c, shift: s, alt: a });
          }, true);
        })();
        """;

    private readonly bool _isPrivate;
    private readonly string? _initialUrl;
    private readonly string _privateProfile = "";
    private readonly Stack<ClosedTab> _closedTabs = new();
    private readonly DispatcherTimer _toastTimer;

    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private bool _suppressAddressText;
    private bool _isFullScreen;
    private WindowState _windowStateBeforeFullScreen = WindowState.Normal;

    public MainWindow(bool isPrivate = false, string? initialUrl = null)
    {
        _isPrivate = isPrivate;
        _initialUrl = initialUrl;
        if (_isPrivate)
        {
            _privateProfile = Path.Combine(Path.GetTempPath(), "NovaBrowserPrivate", Guid.NewGuid().ToString("N"));
        }

        InitializeComponent();
        DataContext = this;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastPopup.IsOpen = false;
        };

        Loaded += MainWindow_Loaded;
        SizeChanged += (_, _) =>
        {
            if (FindPopup.IsOpen)
            {
                PositionFindPopup();
            }

            if (ToastPopup.IsOpen)
            {
                PositionToast();
            }
        };

        Downloads.CollectionChanged += (_, _) => UpdateDownloadsEmpty();
        App.Data.Bookmarks.CollectionChanged += (_, _) => UpdateBookmarksEmpty();

        PrivateBadge.Visibility = _isPrivate ? Visibility.Visible : Visibility.Collapsed;
        ApplySettings();
        UpdateDownloadsEmpty();
        UpdateBookmarksEmpty();
        UpdateZoomText();
    }

    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public ObservableCollection<DownloadItem> Downloads { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks => App.Data.Bookmarks;
    public ObservableCollection<Suggestion> Suggestions { get; } = new();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _environment = _isPrivate
                ? await BrowserEnvironment.CreatePrivateAsync(_privateProfile)
                : await BrowserEnvironment.GetSharedAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось инициализировать WebView2", ex);
            MessageBox.Show(
                "Не удалось запустить движок Chromium (WebView2).\n\n" +
                "Установите Microsoft Edge WebView2 Runtime и запустите браузер снова.\n\n" +
                ex.Message,
                "Nova Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_initialUrl))
            {
                var url = NavigationHelper.NormalizeInput(_initialUrl, App.Data.Settings.SearchEngine);
                await CreateTabAsync(url, activate: true, focusAddress: true);
                return;
            }

            if (!_isPrivate && App.Data.Settings.RestoreSession)
            {
                var session = App.Data.LoadSession();
                if (session is { Tabs.Count: > 0 })
                {
                    var restored = new List<BrowserTab>();
                    foreach (var item in session.Tabs.Take(12))
                    {
                        var url = string.IsNullOrWhiteSpace(item.Url) ? BrowserData.NewTabUrl : item.Url;
                        restored.Add(AddTabShell(url, item.Title));
                    }

                    if (restored.Count > 0)
                    {
                        var index = Math.Clamp(session.ActiveIndex, 0, restored.Count - 1);
                        await ActivateTabAsync(restored[index], focusAddress: false);
                        return;
                    }
                }
            }

            await CreateTabAsync(BrowserData.NewTabUrl, activate: true, focusAddress: true);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось создать стартовую вкладку", ex);
            MessageBox.Show("Не удалось создать стартовую вкладку.\n\n" + ex.Message, "Nova Browser",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private BrowserTab AddTabShell(string url, string? title = null)
    {
        var webView = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = System.Drawing.Color.White
        };

        var tab = new BrowserTab
        {
            View = webView,
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? "Новая вкладка" : title
        };

        webView.Tag = tab;
        WebViewLayer.Children.Add(webView);
        Tabs.Add(tab);
        return tab;
    }

    private async Task<BrowserTab> CreateTabAsync(string url, bool activate = true, bool focusAddress = false, string? title = null)
    {
        var tab = AddTabShell(url, title);
        if (activate)
        {
            await ActivateTabAsync(tab, focusAddress);
        }

        return tab;
    }

    private async Task ActivateTabAsync(BrowserTab tab, bool focusAddress = false)
    {
        if (!Tabs.Contains(tab))
        {
            return;
        }

        foreach (var other in Tabs)
        {
            other.IsActive = false;
            other.View.Visibility = Visibility.Collapsed;
        }

        tab.IsActive = true;
        tab.View.Visibility = Visibility.Visible;
        Panel.SetZIndex(tab.View, 1);
        _activeTab = tab;
        UpdateChromeForActiveTab();

        if (tab.View.CoreWebView2 is null)
        {
            await InitializeTabAsync(tab);
        }

        if (focusAddress)
        {
            FocusAddressBar();
        }
        else
        {
            tab.View.Focus();
        }
    }

    private async Task InitializeTabAsync(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is not null || _environment is null)
        {
            return;
        }

        try
        {
            await tab.View.EnsureCoreWebView2Async(_environment);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка создания WebView2", ex);
            tab.Title = "Ошибка запуска";
            ShowToast("Не удалось запустить движок Chromium");
            return;
        }

        var core = tab.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        ConfigureCore(tab, core);
        StartupOverlay.Visibility = Visibility.Collapsed;

        var target = string.IsNullOrWhiteSpace(tab.Url) ? BrowserData.NewTabUrl : tab.Url;
        core.Navigate(target);
    }

    private void ConfigureCore(BrowserTab tab, CoreWebView2 core)
    {
        try
        {
            core.SetVirtualHostNameToFolderMapping(
                "nova.home",
                BrowserData.AssetsDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = true;
            core.Settings.IsGeneralAutofillEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Profile.PreferredColorScheme = ThemeManager.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;

            core.NavigationStarting += (_, e) => HandleNavigationStarting(tab, e);
            core.SourceChanged += (_, e) => HandleSourceChanged(tab, e);
            core.NavigationCompleted += (_, e) => HandleNavigationCompleted(tab, e);
            core.DocumentTitleChanged += (_, _) => HandleDocumentTitleChanged(tab);
            core.FaviconChanged += (_, _) => HandleFaviconChanged(tab);
            core.HistoryChanged += (_, _) =>
            {
                if (tab == _activeTab)
                {
                    UpdateNavButtons();
                }
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrWhiteSpace(e.Uri))
                {
                    _ = CreateTabAsync(e.Uri, activate: true, focusAddress: true);
                }
            };
            core.WindowCloseRequested += (_, _) => CloseTab(tab);
            core.DownloadStarting += (_, e) => HandleDownloadStarting(tab, e);
            core.WebMessageReceived += (_, e) => HandleWebMessage(tab, e);
            core.ProcessFailed += (_, e) =>
            {
                Log.Error($"Процесс WebView2 завершился: {e.ProcessFailedKind}");
                if (tab == _activeTab)
                {
                    ShowToast("Страница перестала отвечать. Обновите вкладку.");
                }
            };

            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);
            tab.View.ZoomFactorChanged += (_, _) =>
            {
                if (tab == _activeTab)
                {
                    UpdateZoomText();
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка настройки WebView2", ex);
        }
    }

    private void HandleNavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        tab.IsLoading = true;
        if (tab == _activeTab)
        {
            StartupOverlay.Visibility = Visibility.Collapsed;
            LoadProgress.Visibility = Visibility.Visible;
            ReloadButton.Content = "\uE711";
            ReloadButton.ToolTip = "Остановить загрузку";
            CloseSuggestions();
        }
    }

    private void HandleSourceChanged(BrowserTab tab, CoreWebView2SourceChangedEventArgs e)
    {
        var core = tab.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        tab.Url = core.Source ?? "";
        if (tab == _activeTab)
        {
            UpdateAddressBar();
            UpdateBookmarkButton();
            UpdateNavButtons();
        }
    }

    private void HandleNavigationCompleted(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        tab.IsLoading = false;
        var core = tab.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
        {
            tab.Title = core.DocumentTitle;
        }

        if (tab == _activeTab)
        {
            UpdateLoadingUi();
            UpdateNavButtons();
            UpdateBookmarkButton();
            UpdateWindowTitle();
            UpdateZoomText();
        }

        if (e.IsSuccess && !_isPrivate)
        {
            App.Data.RecordVisit(tab.Title, tab.Url);
        }
        else if (!e.IsSuccess && tab == _activeTab)
        {
            ShowToast("Не удалось открыть страницу");
        }
    }

    private void HandleDocumentTitleChanged(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
        {
            tab.Title = core.DocumentTitle;
        }

        if (tab == _activeTab)
        {
            UpdateWindowTitle();
        }
    }

    private void HandleFaviconChanged(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        var uri = core.FaviconUri;
        if (string.IsNullOrWhiteSpace(uri))
        {
            tab.Favicon = null;
            return;
        }

        _ = LoadFaviconAsync(tab, uri);
    }

    private async Task LoadFaviconAsync(BrowserTab tab, string uri)
    {
        try
        {
            if (FaviconCache.TryGetValue(uri, out var cached))
            {
                tab.Favicon = cached;
                return;
            }

            byte[]? bytes = null;
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = uri.IndexOf(',');
                if (comma > 0)
                {
                    var payload = uri[(comma + 1)..];
                    bytes = uri[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase)
                        ? Convert.FromBase64String(payload)
                        : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                }
            }
            else if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
                     (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                using var response = await HttpClient.GetAsync(parsed);
                if (response.IsSuccessStatusCode)
                {
                    bytes = await response.Content.ReadAsByteArrayAsync();
                }
            }

            if (bytes is null || bytes.Length == 0)
            {
                FaviconCache[uri] = null;
                return;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (FaviconCache.Count > 600)
            {
                FaviconCache.Clear();
            }

            FaviconCache[uri] = bitmap;
            tab.Favicon = bitmap;
        }
        catch
        {
            FaviconCache[uri] = null;
        }
    }

    private void HandleDownloadStarting(BrowserTab tab, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var folder = App.Data.Settings.DownloadsFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = BrowserSettings.DefaultDownloadsFolder;
            }

            Directory.CreateDirectory(folder);
            var suggested = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggested))
            {
                suggested = "download";
            }

            var path = GetUniqueFilePath(folder, SanitizeFileName(suggested));
            e.ResultFilePath = path;
            e.Handled = true;

            var operation = e.DownloadOperation;
            var item = new DownloadItem
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                Url = operation.Uri?.ToString() ?? "",
                Operation = operation
            };

            operation.StateChanged += (_, _) => RunOnUi(() =>
            {
                item.Refresh();
                UpdateDownloadsEmpty();
                if (item.IsComplete)
                {
                    ShowToast("Загрузка завершена: " + item.FileName);
                }
            });
            operation.BytesReceivedChanged += (_, _) => RunOnUi(() =>
            {
                item.Refresh();
                UpdateDownloadsEmpty();
            });

            item.Refresh();
            Downloads.Insert(0, item);
            UpdateDownloadsEmpty();
            ShowToast("Загрузка началась: " + item.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось начать загрузку", ex);
            ShowToast("Не удалось начать загрузку");
        }
    }

    private void HandleWebMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var fromNova = string.IsNullOrWhiteSpace(e.Source) ||
                           e.Source.StartsWith("https://nova.home/", StringComparison.OrdinalIgnoreCase);

            if (root.ValueKind == JsonValueKind.String)
            {
                var value = root.GetString();
                if (fromNova && string.Equals(value, "settings", StringComparison.OrdinalIgnoreCase))
                {
                    OpenSettings();
                }

                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (string.Equals(type, "shortcut", StringComparison.Ordinal))
            {
                if (root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var code))
                {
                    var ctrl = root.TryGetProperty("ctrl", out var ctrlElement) && ctrlElement.ValueKind == JsonValueKind.True;
                    var shift = root.TryGetProperty("shift", out var shiftElement) && shiftElement.ValueKind == JsonValueKind.True;
                    var alt = root.TryGetProperty("alt", out var altElement) && altElement.ValueKind == JsonValueKind.True;
                    HandleShortcut(code, ctrl, shift, alt);
                }

                return;
            }

            if (!fromNova)
            {
                return;
            }

            switch (type)
            {
                case "search":
                    if (root.TryGetProperty("query", out var queryElement))
                    {
                        var query = queryElement.GetString();
                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            NavigateFromInput(query);
                        }
                    }
                    break;
                case "navigate":
                    if (root.TryGetProperty("url", out var urlElement))
                    {
                        var url = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            NavigateActive(NavigationHelper.NormalizeInput(url, App.Data.Settings.SearchEngine));
                        }
                    }
                    break;
                case "newTab":
                    _ = CreateTabAsync(BrowserData.NewTabUrl, activate: true, focusAddress: true);
                    break;
                case "settings":
                    OpenSettings();
                    break;
                case "close":
                    CloseTab(tab);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка обработки сообщения внутренней страницы", ex);
        }
    }

    private bool HandleShortcut(int virtualKey, bool ctrl, bool shift, bool alt)
    {
        if (ctrl && shift)
        {
            switch (virtualKey)
            {
                case 0x54: // T
                    ReopenClosedTab();
                    return true;
                case 0x4E: // N
                    OpenNewWindow(privateMode: true);
                    return true;
                case 0x4F: // O
                    OpenBookmarks();
                    return true;
                case 0x42: // B
                    ToggleBookmarksBar();
                    return true;
                case 0x09: // Tab
                    CycleTab(-1);
                    return true;
                case 0x49: // I
                    OpenDevTools();
                    return true;
                case 0x4A: // J
                    OpenDevTools();
                    return true;
            }
        }

        if (ctrl && !shift)
        {
            switch (virtualKey)
            {
                case 0x54: // T
                    _ = CreateTabAsync(BrowserData.NewTabUrl, activate: true, focusAddress: true);
                    return true;
                case 0x57: // W
                    if (_activeTab is not null)
                    {
                        CloseTab(_activeTab);
                    }
                    return true;
                case 0x4E: // N
                    OpenNewWindow(privateMode: false);
                    return true;
                case 0x4C: // L
                    FocusAddressBar();
                    return true;
                case 0x44: // D
                    ToggleBookmark();
                    return true;
                case 0x48: // H
                    OpenHistory();
                    return true;
                case 0x4A: // J
                    OpenDownloads();
                    return true;
                case 0x46: // F
                    OpenFind();
                    return true;
                case 0x52: // R
                    ReloadActive();
                    return true;
                case 0x09: // Tab
                    CycleTab(1);
                    return true;
                case 0x30: // 0
                    SetZoom(1.0);
                    return true;
                case 0xBB: // OEM +
                case 0x6B: // numpad +
                    ZoomIn();
                    return true;
                case 0xBD: // OEM -
                case 0x6D: // numpad -
                    ZoomOut();
                    return true;
                case >= 0x31 and <= 0x38:
                    SelectTabByIndex(virtualKey - 0x31);
                    return true;
                case 0x39:
                    SelectTabByIndex(Tabs.Count - 1);
                    return true;
            }
        }

        if (alt && !ctrl)
        {
            if (virtualKey == 0x25)
            {
                GoBack();
                return true;
            }

            if (virtualKey == 0x27)
            {
                GoForward();
                return true;
            }

            if (virtualKey == 0x24) // Home
            {
                NavigateHome();
                return true;
            }

            if (virtualKey == 0x45) // E
            {
                OpenMenu();
                return true;
            }
        }

        if (!ctrl && !alt)
        {
            switch (virtualKey)
            {
                case 0x74: // F5
                    ReloadActive();
                    return true;
                case 0x7A: // F11
                    ToggleFullScreen();
                    return true;
                case 0x7B: // F12
                    OpenDevTools();
                    return true;
                case 0x1B: // Escape
                    if (CloseVisiblePopups())
                    {
                        return true;
                    }
                    if (_isFullScreen)
                    {
                        ToggleFullScreen();
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (HandleShortcut(virtualKey, ctrl, shift, alt))
        {
            e.Handled = true;
        }
    }

    private void UpdateChromeForActiveTab()
    {
        UpdateAddressBar();
        UpdateNavButtons();
        UpdateLoadingUi();
        UpdateBookmarkButton();
        UpdateWindowTitle();
        UpdateZoomText();
    }

    private void UpdateAddressBar()
    {
        var tab = _activeTab;
        _suppressAddressText = true;
        try
        {
            if (tab is null)
            {
                AddressBar.Text = "";
                SecurityIcon.Text = "\uE774";
                SecurityIcon.ToolTip = "Адрес сайта";
            }
            else if (tab.IsInternal)
            {
                AddressBar.Text = "";
                SecurityIcon.Text = "\uE80F";
                SecurityIcon.ToolTip = "Внутренняя страница Nova";
            }
            else
            {
                AddressBar.Text = tab.Url;
                if (tab.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    SecurityIcon.Text = "\uE72E";
                    SecurityIcon.ToolTip = "Защищённое соединение HTTPS";
                }
                else if (tab.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    SecurityIcon.Text = "\uE946";
                    SecurityIcon.ToolTip = "Соединение не защищено";
                }
                else
                {
                    SecurityIcon.Text = "\uE774";
                    SecurityIcon.ToolTip = "Адрес сайта";
                }
            }

            AddressPlaceholder.Visibility = string.IsNullOrEmpty(AddressBar.Text) ? Visibility.Visible : Visibility.Collapsed;
            ClearAddressButton.Visibility = string.IsNullOrEmpty(AddressBar.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            _suppressAddressText = false;
        }
    }

    private void UpdateNavButtons()
    {
        try
        {
            var core = _activeTab?.View.CoreWebView2;
            BackButton.IsEnabled = core?.CanGoBack == true;
            ForwardButton.IsEnabled = core?.CanGoForward == true;
        }
        catch
        {
            BackButton.IsEnabled = false;
            ForwardButton.IsEnabled = false;
        }

        UpdateLoadingUi();
    }

    private void UpdateLoadingUi()
    {
        var loading = _activeTab?.IsLoading == true;
        LoadProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.Content = loading ? "\uE711" : "\uE72C";
        ReloadButton.ToolTip = loading ? "Остановить загрузку" : "Обновить (F5)";
    }

    private void UpdateBookmarkButton()
    {
        var tab = _activeTab;
        var canBookmark = tab is not null &&
                          !tab.IsInternal &&
                          !string.IsNullOrWhiteSpace(tab.Url) &&
                          !tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
        BookmarkButton.IsEnabled = canBookmark;

        if (!canBookmark || tab is null)
        {
            BookmarkButton.Content = "\uE734";
            BookmarkButton.Foreground = (Brush)FindResource("TextSecondaryBrush");
            BookmarkButton.ToolTip = "Добавить закладку (Ctrl+D)";
            return;
        }

        var marked = App.Data.IsBookmarked(tab.Url);
        BookmarkButton.Content = marked ? "\uE735" : "\uE734";
        BookmarkButton.Foreground = marked ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush");
        BookmarkButton.ToolTip = marked ? "Удалить из закладок (Ctrl+D)" : "Добавить закладку (Ctrl+D)";
    }

    private void UpdateWindowTitle()
    {
        var title = _activeTab?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Nova Browser";
        }
        else
        {
            title += " — Nova Browser";
        }

        if (_isPrivate)
        {
            title += " (приватно)";
        }

        Title = title;
    }

    private void UpdateZoomText()
    {
        var factor = _activeTab?.View.ZoomFactor ?? 1.0;
        ZoomText.Text = $"{Math.Round(factor * 100)}%";
    }

    private void ApplySettings()
    {
        BookmarksBar.Visibility = App.Data.Settings.ShowBookmarksBar && !_isFullScreen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyThemeToWebViews()
    {
        foreach (var tab in Tabs)
        {
            try
            {
                if (tab.View.CoreWebView2 is null)
                {
                    continue;
                }

                tab.View.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.IsDark
                    ? CoreWebView2PreferredColorScheme.Dark
                    : CoreWebView2PreferredColorScheme.Light;
            }
            catch
            {
                // A tab may be closing.
            }
        }
    }

    private void FocusAddressBar()
    {
        CloseSuggestions();
        AddressBar.Focus();
        Keyboard.Focus(AddressBar);
        AddressBar.SelectAll();
    }

    private void NavigateFromAddressBar()
    {
        var text = AddressBar.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Equals("nova://settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
            return;
        }

        CloseSuggestions();
        NavigateFromInput(text);
    }

    private void NavigateFromInput(string input)
    {
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Equals("nova://settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
            return;
        }

        var url = NavigationHelper.NormalizeInput(text, App.Data.Settings.SearchEngine);
        NavigateActive(url);
    }

    private async void NavigateActive(string url)
    {
        if (_activeTab is null)
        {
            await CreateTabAsync(url, activate: true);
            return;
        }

        var tab = _activeTab;
        tab.Url = url;
        CloseSuggestions();

        if (tab.View.CoreWebView2 is null)
        {
            await InitializeTabAsync(tab);
        }
        else
        {
            tab.View.CoreWebView2.Navigate(url);
        }

        tab.View.Focus();
    }

    private void NavigateSuggestion(Suggestion suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion.Url))
        {
            return;
        }

        _suppressAddressText = true;
        AddressBar.Text = suggestion.Url;
        AddressPlaceholder.Visibility = Visibility.Collapsed;
        ClearAddressButton.Visibility = Visibility.Visible;
        _suppressAddressText = false;
        CloseSuggestions();
        NavigateActive(suggestion.Url);
    }

    private void UpdateSuggestions()
    {
        var text = AddressBar.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !AddressBar.IsKeyboardFocusWithin)
        {
            CloseSuggestions();
            return;
        }

        var items = new List<Suggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var likelyUrl = NavigationHelper.LooksLikeHost(text) ||
                        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

        if (likelyUrl)
        {
            var direct = NavigationHelper.NormalizeInput(text, App.Data.Settings.SearchEngine);
            items.Add(new Suggestion
            {
                Title = "Перейти на сайт",
                Subtitle = direct,
                Url = direct,
                Glyph = "\uE774"
            });
            seen.Add(direct);
        }

        var lower = text.ToLowerInvariant();
        foreach (var bookmark in App.Data.Bookmarks
                     .Where(b => b.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                 b.Url.Contains(text, StringComparison.OrdinalIgnoreCase))
                     .Take(4))
        {
            if (seen.Add(bookmark.Url))
            {
                items.Add(new Suggestion
                {
                    Title = bookmark.Title,
                    Subtitle = bookmark.Host,
                    Url = bookmark.Url,
                    Glyph = "\uE734"
                });
            }
        }

        foreach (var history in App.Data.History
                     .Where(h => h.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                 h.Url.Contains(text, StringComparison.OrdinalIgnoreCase))
                     .Take(7))
        {
            if (seen.Add(history.Url))
            {
                items.Add(new Suggestion
                {
                    Title = history.Title,
                    Subtitle = history.Host,
                    Url = history.Url,
                    Glyph = "\uE81C"
                });
            }
        }

        var searchUrl = NavigationHelper.SearchUrl(App.Data.Settings.SearchEngine, text);
        items.Add(new Suggestion
        {
            Title = $"Искать «{text}»",
            Subtitle = App.Data.Settings.SearchEngine,
            Url = searchUrl,
            Glyph = "\uE721",
            IsSearch = true
        });

        Suggestions.Clear();
        foreach (var item in items.Take(9))
        {
            Suggestions.Add(item);
        }

        SuggestionsList.SelectedIndex = 0;
        SuggestionsPopup.IsOpen = Suggestions.Count > 0;
    }

    private void CloseSuggestions()
    {
        SuggestionsPopup.IsOpen = false;
        Suggestions.Clear();
    }

    private bool CloseVisiblePopups()
    {
        var wasOpen = SuggestionsPopup.IsOpen || MenuPopup.IsOpen || DownloadsPopup.IsOpen ||
                      HistoryPopup.IsOpen || BookmarksPopup.IsOpen || FindPopup.IsOpen;
        CloseSuggestions();
        MenuPopup.IsOpen = false;
        DownloadsPopup.IsOpen = false;
        HistoryPopup.IsOpen = false;
        BookmarksPopup.IsOpen = false;
        FindPopup.IsOpen = false;
        return wasOpen;
    }

    private void CloseAllPopups()
    {
        CloseVisiblePopups();
    }

    private void OpenMenu()
    {
        CloseAllPopups();
        MenuPopup.IsOpen = true;
    }

    private void OpenDownloads()
    {
        CloseAllPopups();
        UpdateDownloadsEmpty();
        DownloadsPopup.IsOpen = true;
    }

    private void OpenHistory()
    {
        CloseAllPopups();
        HistoryItems.ItemsSource = null;
        HistoryItems.ItemsSource = App.Data.History;
        HistoryEmpty.Visibility = App.Data.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPopup.IsOpen = true;
    }

    private void OpenBookmarks()
    {
        CloseAllPopups();
        UpdateBookmarksEmpty();
        BookmarksPopup.IsOpen = true;
    }

    private void OpenFind()
    {
        CloseAllPopups();
        PositionFindPopup();
        FindPopup.IsOpen = true;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void OpenSettings()
    {
        CloseAllPopups();
        var window = new SettingsWindow(App.Data.Settings.Copy()) { Owner = this };
        if (window.ShowDialog() == true)
        {
            App.Data.Settings = window.ResultSettings;
            App.Data.SaveSettings();
            ThemeManager.Apply(App.Data.Settings.Theme);
            ApplySettings();
            ApplyThemeToWebViews();
            ShowToast("Настройки сохранены");
        }
    }

    private void OpenNewWindow(bool privateMode)
    {
        var window = new MainWindow(privateMode);
        window.Show();
    }

    private void OpenDevTools()
    {
        try
        {
            _activeTab?.View.CoreWebView2?.OpenDevToolsWindow();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть инструменты разработчика", ex);
        }
    }

    private void ToggleBookmarksBar()
    {
        App.Data.Settings.ShowBookmarksBar = !App.Data.Settings.ShowBookmarksBar;
        App.Data.SaveSettings();
        ApplySettings();
        ShowToast(App.Data.Settings.ShowBookmarksBar ? "Панель закладок показана" : "Панель закладок скрыта");
    }

    private void ToggleBookmark()
    {
        var tab = _activeTab;
        if (tab is null || tab.IsInternal || string.IsNullOrWhiteSpace(tab.Url))
        {
            return;
        }

        var added = App.Data.ToggleBookmark(tab.Title, tab.Url);
        UpdateBookmarkButton();
        UpdateBookmarksEmpty();
        ShowToast(added ? "Добавлено в закладки" : "Удалено из закладок");
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(42);
            RootGrid.RowDefinitions[1].Height = new GridLength(54);
            RootGrid.RowDefinitions[2].Height = GridLength.Auto;
            _isFullScreen = false;
            WindowState = _windowStateBeforeFullScreen;
            ApplySettings();
            return;
        }

        _windowStateBeforeFullScreen = WindowState;
        _isFullScreen = true;
        CloseAllPopups();
        RootGrid.RowDefinitions[0].Height = new GridLength(0);
        RootGrid.RowDefinitions[1].Height = new GridLength(0);
        RootGrid.RowDefinitions[2].Height = new GridLength(0);
        BookmarksBar.Visibility = Visibility.Collapsed;
        WindowState = WindowState.Maximized;
    }

    private void SetZoom(double factor)
    {
        var tab = _activeTab;
        if (tab?.View.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            tab.View.ZoomFactor = Math.Clamp(factor, 0.25, 5.0);
            UpdateZoomText();
        }
        catch
        {
            // Ignore zoom errors on pages that are closing.
        }
    }

    private void ZoomIn() => SetZoom((_activeTab?.View.ZoomFactor ?? 1.0) + 0.1);
    private void ZoomOut() => SetZoom((_activeTab?.View.ZoomFactor ?? 1.0) - 0.1);

    private void GoBack()
    {
        try
        {
            if (_activeTab?.View.CoreWebView2?.CanGoBack == true)
            {
                _activeTab.View.CoreWebView2.GoBack();
            }
        }
        catch
        {
            // Ignore navigation errors.
        }
    }

    private void GoForward()
    {
        try
        {
            if (_activeTab?.View.CoreWebView2?.CanGoForward == true)
            {
                _activeTab.View.CoreWebView2.GoForward();
            }
        }
        catch
        {
            // Ignore navigation errors.
        }
    }

    private void ReloadActive()
    {
        try
        {
            if (_activeTab?.IsLoading == true)
            {
                _activeTab.View.CoreWebView2?.Stop();
            }
            else
            {
                _activeTab?.View.CoreWebView2?.Reload();
            }
        }
        catch
        {
            // Ignore reload errors.
        }
    }

    private void CycleTab(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var index = _activeTab is null ? 0 : Tabs.IndexOf(_activeTab);
        index = (index + delta + Tabs.Count) % Tabs.Count;
        _ = ActivateTabAsync(Tabs[index]);
    }

    private void SelectTabByIndex(int index)
    {
        if (index >= 0 && index < Tabs.Count)
        {
            _ = ActivateTabAsync(Tabs[index]);
        }
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            ShowToast("Нет недавно закрытых вкладок");
            return;
        }

        var closed = _closedTabs.Pop();
        _ = CreateTabAsync(closed.Url, activate: true, focusAddress: true, title: closed.Title);
    }

    private void CloseTab(BrowserTab tab)
    {
        if (!Tabs.Contains(tab))
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (!tab.IsInternal && !string.IsNullOrWhiteSpace(tab.Url))
        {
            _closedTabs.Push(new ClosedTab(tab.Url, tab.Title));
            if (_closedTabs.Count > 20)
            {
                _closedTabs.TrimExcess();
            }
        }

        var wasActive = tab == _activeTab;
        Tabs.Remove(tab);
        WebViewLayer.Children.Remove(tab.View);
        try
        {
            tab.View.Dispose();
        }
        catch
        {
            // WebView may already be disposed.
        }

        if (!wasActive)
        {
            return;
        }

        _activeTab = null;
        if (Tabs.Count == 0)
        {
            Close();
            return;
        }

        var next = Tabs[Math.Min(index, Tabs.Count - 1)];
        _ = ActivateTabAsync(next);
    }

    private async void FindInPage(bool backwards)
    {
        var core = _activeTab?.View.CoreWebView2;
        if (core is null || string.IsNullOrWhiteSpace(FindBox.Text))
        {
            return;
        }

        var literal = JsonSerializer.Serialize(FindBox.Text);
        var backwardsLiteral = backwards ? "true" : "false";
        var script =
            "(function(){const q=" + literal + ";if(!q)return false;" +
            "try{return window.find(q,false," + backwardsLiteral + ",true,false,true,false);}catch(e){return false;}})()";

        try
        {
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка поиска на странице", ex);
        }
    }

    private void ShowToast(string message)
    {
        if (!IsLoaded)
        {
            return;
        }

        ToastText.Text = message;
        PositionToast();
        ToastPopup.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void PositionFindPopup()
    {
        FindPopup.HorizontalOffset = Math.Max(12, ContentHost.ActualWidth - 394);
        FindPopup.VerticalOffset = 12;
    }

    private void PositionToast()
    {
        ToastPopup.HorizontalOffset = Math.Max(12, ContentHost.ActualWidth / 2 - 150);
        ToastPopup.VerticalOffset = Math.Max(12, ContentHost.ActualHeight - 78);
    }

    private void UpdateDownloadsEmpty()
    {
        if (DownloadsEmpty is not null)
        {
            DownloadsEmpty.Visibility = Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateBookmarksEmpty()
    {
        if (BookmarksEmpty is not null)
        {
            BookmarksEmpty.Visibility = App.Data.Bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }

    private static string GetUniqueFilePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; index < 10000; index++)
        {
            path = Path.Combine(folder, $"{name} ({index}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(folder, $"{Guid.NewGuid():N}{extension}");
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private void CleanupPrivateProfile()
    {
        if (string.IsNullOrWhiteSpace(_privateProfile))
        {
            return;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(_privateProfile))
                {
                    Directory.Delete(_privateProfile, recursive: true);
                }

                return;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isPrivate)
        {
            return;
        }

        try
        {
            var tabs = Tabs.Select(t => new SessionTab
            {
                Url = t.Url,
                Title = t.Title
            }).ToList();
            var activeIndex = _activeTab is null ? 0 : Tabs.IndexOf(_activeTab);
            App.Data.SaveSession(tabs, Math.Max(0, activeIndex));
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось сохранить сессию", ex);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        foreach (var tab in Tabs.ToList())
        {
            try
            {
                tab.View.Dispose();
            }
            catch
            {
                // Ignore dispose errors.
            }
        }

        CleanupPrivateProfile();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        CloseAllPopups();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null)
        {
            return;
        }

        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse was released early.
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void TabsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabsScroll.ScrollToHorizontalOffset(TabsScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BrowserTab tab })
        {
            e.Handled = true;
            _ = ActivateTabAsync(tab);
        }
    }

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: BrowserTab tab })
        {
            e.Handled = true;
            CloseTab(tab);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            e.Handled = true;
            CloseTab(tab);
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        CloseAllPopups();
        _ = CreateTabAsync(BrowserData.NewTabUrl, activate: true, focusAddress: true);
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();

    private void Reload_Click(object sender, RoutedEventArgs e) => ReloadActive();

    private void Home_Click(object sender, RoutedEventArgs e) => NavigateHome();

    private void NavigateHome()
    {
        var home = App.Data.Settings.HomePage;
        if (string.IsNullOrWhiteSpace(home))
        {
            NavigateActive(BrowserData.NewTabUrl);
        }
        else
        {
            NavigateFromInput(home);
        }
    }

    private void ToggleBookmark_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void AddCurrentBookmark_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void Downloads_Click(object sender, RoutedEventArgs e) => OpenDownloads();

    private void Menu_Click(object sender, RoutedEventArgs e) => OpenMenu();

    private void History_Click(object sender, RoutedEventArgs e) => OpenHistory();

    private void Bookmarks_Click(object sender, RoutedEventArgs e) => OpenBookmarks();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = "недоступен";
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString() ?? version;
        }
        catch
        {
            // Runtime information is optional.
        }

        MessageBox.Show(
            "Nova Browser 1.0.0\n" +
            "Браузер на движке Chromium (Microsoft Edge WebView2).\n\n" +
            "Версия движка: " + version + "\n\n" +
            "Nova Browser не связан с Google. Chrome является товарным знаком Google LLC.\n" +
            "Лицензионное соглашение входит в установщик и находится рядом с программой.",
            "О браузере",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void NewWindow_Click(object sender, RoutedEventArgs e) => OpenNewWindow(privateMode: false);

    private void NewPrivateWindow_Click(object sender, RoutedEventArgs e) => OpenNewWindow(privateMode: true);

    private void Find_Click(object sender, RoutedEventArgs e) => OpenFind();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();

    private void ClosePopups_Click(object sender, RoutedEventArgs e) => CloseAllPopups();

    private void CloseFind_Click(object sender, RoutedEventArgs e)
    {
        FindPopup.IsOpen = false;
        _activeTab?.View.Focus();
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInPage((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindPopup.IsOpen = false;
            _activeTab?.View.Focus();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindInPage(false);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindInPage(true);

    private void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryEntry entry })
        {
            CloseAllPopups();
            NavigateActive(entry.Url);
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Удалить всю историю посещений?", "Nova Browser",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        App.Data.ClearHistory();
        HistoryItems.ItemsSource = null;
        HistoryItems.ItemsSource = App.Data.History;
        HistoryEmpty.Visibility = Visibility.Visible;
        ShowToast("История очищена");
    }

    private void BookmarkBar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Bookmark bookmark })
        {
            NavigateActive(bookmark.Url);
        }
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Bookmark bookmark })
        {
            CloseAllPopups();
            NavigateActive(bookmark.Url);
        }
    }

    private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Bookmark bookmark })
        {
            App.Data.RemoveBookmark(bookmark);
            UpdateBookmarkButton();
            UpdateBookmarksEmpty();
            ShowToast("Закладка удалена");
        }
    }

    private void OpenDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadItem item })
        {
            item.OpenFile();
        }
    }

    private void ShowDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadItem item })
        {
            item.ShowInFolder();
        }
    }

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = App.Data.Settings.DownloadsFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть папку загрузок", ex);
        }
    }

    private void ClearDownloads_Click(object sender, RoutedEventArgs e)
    {
        Downloads.Clear();
        UpdateDownloadsEmpty();
    }

    private void AddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBorder.Background = (Brush)FindResource("SurfaceBrush");
        AddressBorder.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    private void AddressBar_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBorder.Background = (Brush)FindResource("SurfaceAltBrush");
        AddressBorder.BorderBrush = (Brush)FindResource("BorderBrush");
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!SuggestionsList.IsKeyboardFocusWithin)
            {
                SuggestionsPopup.IsOpen = false;
            }
        }));
    }

    private void AddressBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddressPlaceholder.Visibility = string.IsNullOrEmpty(AddressBar.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearAddressButton.Visibility = string.IsNullOrEmpty(AddressBar.Text) ? Visibility.Collapsed : Visibility.Visible;

        if (_suppressAddressText)
        {
            return;
        }

        if (AddressBar.IsKeyboardFocusWithin)
        {
            UpdateSuggestions();
        }
        else
        {
            SuggestionsPopup.IsOpen = false;
        }
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is Suggestion suggestion)
            {
                NavigateSuggestion(suggestion);
            }
            else
            {
                NavigateFromAddressBar();
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSuggestions();
        }
    }

    private void AddressBar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestionsPopup.IsOpen)
        {
            SuggestionsList.SelectedIndex = Math.Min(SuggestionsList.SelectedIndex + 1, Suggestions.Count - 1);
            SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && SuggestionsPopup.IsOpen)
        {
            SuggestionsList.SelectedIndex = Math.Max(SuggestionsList.SelectedIndex - 1, 0);
            SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
            e.Handled = true;
        }
    }

    private void SuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SuggestionsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is Suggestion suggestion)
        {
            e.Handled = true;
            NavigateSuggestion(suggestion);
        }
    }

    private void SuggestionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionsList.SelectedItem is Suggestion suggestion)
        {
            e.Handled = true;
            NavigateSuggestion(suggestion);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSuggestions();
            FocusAddressBar();
        }
    }

    private void ClearAddress_Click(object sender, RoutedEventArgs e)
    {
        AddressBar.Clear();
        FocusAddressBar();
    }

    private sealed record ClosedTab(string Url, string Title);
}
