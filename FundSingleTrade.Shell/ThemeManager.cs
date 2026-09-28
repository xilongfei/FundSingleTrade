using FundSingleTrade.Shell.Data;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace FundSingleTrade.Shell;

/// <summary>集中应用 Material Design 主题、调色板和应用自定义颜色资源。</summary>
public static class ThemeManager
{
    /// <summary>根据用户偏好应用明暗主题与强调色。</summary>
    public static void Apply(UserPreferences preferences)
    {
        var isDark = preferences.ThemeMode switch
        {
            "深色" => true,
            "浅色" => false,
            _ => IsSystemDarkMode()
        };
        var accent = ParseColor(preferences.AccentColor);

        var palette = new PaletteHelper();
        var theme = palette.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor(accent);
        palette.SetTheme(theme);

        ApplyApplicationResources(isDark, accent);
    }

    /// <summary>检测 Windows 应用颜色模式；注册表不可用时遵循浅色默认值。</summary>
    public static bool IsSystemDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    /// <summary>解析用户保存的十六进制强调色。</summary>
    private static Color ParseColor(string color)
    {
        if (ColorConverter.ConvertFromString(color) is Color parsed)
            return parsed;
        throw new FormatException($"无效的强调色：{color}");
    }

    /// <summary>更新自定义控件使用的颜色资源，支持运行时立即切换。</summary>
    private static void ApplyApplicationResources(bool isDark, Color accent)
    {
        var resources = Application.Current.Resources;
        resources["WindowBackground"] = Brush(isDark ? "#111827" : "#F4F6FA");
        resources["SurfaceBackground"] = Brush(isDark ? "#1F2937" : "#FFFFFF");
        resources["PrimaryText"] = Brush(isDark ? "#F3F4F6" : "#172033");
        resources["SecondaryText"] = Brush(isDark ? "#AAB4C4" : "#667085");
        resources["InputBackground"] = Brush(isDark ? "#374151" : "#F8FAFC");
        resources["BorderBrush"] = Brush(isDark ? "#4B5563" : "#E4E7EC");
        resources["CardHover"] = Brush(isDark ? "#293548" : "#EFF6FF");
        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["PositiveChangeBrush"] = Brush(isDark ? "#F87171" : "#B42318");
        resources["NegativeChangeBrush"] = Brush(isDark ? "#4ADE80" : "#15803D");
    }

    /// <summary>从十六进制颜色创建冻结画刷，降低频繁重绘的开销。</summary>
    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush(ParseColor(color));
        brush.Freeze();
        return brush;
    }
}
