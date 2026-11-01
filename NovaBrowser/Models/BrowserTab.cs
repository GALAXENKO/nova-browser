using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace NovaBrowser.Models;

public sealed class BrowserTab : INotifyPropertyChanged
{
    private string _title = "Новая вкладка";
    private string _url = "";
    private ImageSource? _favicon;
    private bool _isActive;
    private bool _isLoading;
    private bool _isAudible;

    public Guid Id { get; } = Guid.NewGuid();
    public WebView2 View { get; set; } = null!;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, string.IsNullOrWhiteSpace(value) ? "Новая вкладка" : value);
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetField(ref _url, value))
            {
                OnPropertyChanged(nameof(IsInternal));
                OnPropertyChanged(nameof(Initial));
                OnPropertyChanged(nameof(AccentBrush));
                OnPropertyChanged(nameof(Host));
            }
        }
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set => SetField(ref _favicon, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public bool IsAudible
    {
        get => _isAudible;
        set => SetField(ref _isAudible, value);
    }

    public bool IsInternal => Url.StartsWith("https://nova.home/", StringComparison.OrdinalIgnoreCase);

    public string Host
    {
        get
        {
            if (IsInternal)
            {
                return "Новая вкладка";
            }

            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            }

            return Url;
        }
    }

    public string Initial
    {
        get
        {
            var source = IsInternal ? "N" : Host;
            return string.IsNullOrWhiteSpace(source) ? "N" : source.Trim()[..1].ToUpperInvariant();
        }
    }

    public Brush AccentBrush
    {
        get
        {
            var seed = IsInternal ? 0 : Math.Abs(Host.GetHashCode());
            var palette = new[]
            {
                "#4E6BFF", "#0EA5A4", "#E8590C", "#8B5CF6", "#D6336C", "#2F9E44", "#1971C2", "#B7791F"
            };
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[seed % palette.Length]));
            brush.Freeze();
            return brush;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
