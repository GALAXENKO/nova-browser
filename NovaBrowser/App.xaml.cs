using System.Windows;
using NovaBrowser.Services;

namespace NovaBrowser;

public partial class App : Application
{
    public static BrowserData Data { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Необработанная ошибка UI", args.Exception);
            MessageBox.Show(
                "Произошла непредвиденная ошибка. Подробности сохранены в журнале приложения.\n\n" + args.Exception.Message,
                "Nova Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Необработанная ошибка", args.ExceptionObject as Exception);

        try
        {
            Data = BrowserData.Load();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось загрузить данные браузера", ex);
            Data = BrowserData.CreateFallback();
        }

        ThemeManager.Apply(Data.Settings.Theme);

        var isPrivate = e.Args.Any(a => a.Equals("--private", StringComparison.OrdinalIgnoreCase));
        var initialUrl = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var window = new MainWindow(isPrivate, initialUrl);
        MainWindow = window;
        window.Show();
    }
}
