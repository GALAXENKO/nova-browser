using System.Windows;
using System.Windows.Media;

namespace NovaBrowser.Services;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(string? theme)
    {
        var dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        IsDark = dark;

        if (dark)
        {
            Set("AppBackgroundBrush", "#0D1320");
            Set("TabStripBrush", "#151D2E");
            Set("SurfaceBrush", "#1A2436");
            Set("SurfaceAltBrush", "#121B2A");
            Set("SurfaceSunkenBrush", "#0F1725");
            Set("HoverBrush", "#243149");
            Set("PressedBrush", "#2D3C57");
            Set("TextPrimaryBrush", "#F2F5FB");
            Set("TextSecondaryBrush", "#B4C0D6");
            Set("TextMutedBrush", "#7E8BA3");
            Set("BorderBrush", "#2A3750");
            Set("AccentBrush", "#7C95FF");
            Set("AccentHoverBrush", "#6A85FF");
            Set("AccentSoftBrush", "#232F4D");
            Set("DangerBrush", "#FF6B70");
            Set("SuccessBrush", "#4ED39A");
            Set("WindowControlHoverBrush", "#243149");
            Set("WindowControlPressedBrush", "#2D3C57");
            Set("TabHoverBrush", "#202C42");
            Set("ScrollThumbBrush", "#3A4965");
        }
        else
        {
            Set("AppBackgroundBrush", "#F2F5FA");
            Set("TabStripBrush", "#E7ECF5");
            Set("SurfaceBrush", "#FFFFFF");
            Set("SurfaceAltBrush", "#F4F6FA");
            Set("SurfaceSunkenBrush", "#EEF2F8");
            Set("HoverBrush", "#E9EEF7");
            Set("PressedBrush", "#DCE5F3");
            Set("TextPrimaryBrush", "#182236");
            Set("TextSecondaryBrush", "#5B667A");
            Set("TextMutedBrush", "#8A94A7");
            Set("BorderBrush", "#DCE2EC");
            Set("AccentBrush", "#4E6BFF");
            Set("AccentHoverBrush", "#3E5BF0");
            Set("AccentSoftBrush", "#E9EDFF");
            Set("DangerBrush", "#E5484D");
            Set("SuccessBrush", "#1F9D68");
            Set("WindowControlHoverBrush", "#DCE3EF");
            Set("WindowControlPressedBrush", "#CED8E8");
            Set("TabHoverBrush", "#DDE5F0");
            Set("ScrollThumbBrush", "#C7D0DF");
        }
    }

    private static void Set(string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
