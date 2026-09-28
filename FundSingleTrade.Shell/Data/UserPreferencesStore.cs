using System.IO;
using System.Text.Json;

namespace FundSingleTrade.Shell.Data;

/// <summary>保存应用主题与强调色的本地偏好。</summary>
public sealed class UserPreferences
{
    /// <summary>主题选择：跟随系统、浅色或深色。</summary>
    public string ThemeMode { get; set; } = "跟随系统";

    /// <summary>主题强调色的十六进制颜色代码。</summary>
    public string AccentColor { get; set; } = "#2563EB";
}

/// <summary>负责将界面偏好写入数据库同目录的 JSON 文件。</summary>
public static class UserPreferencesStore
{
    private static readonly string PreferencesPath =
        Path.Combine(FundDbContext.DataDirectory, "settings.json");

    /// <summary>读取本地偏好；首次运行时返回默认主题设置。</summary>
    public static UserPreferences Load()
    {
        if (!File.Exists(PreferencesPath))
            return new UserPreferences();

        // 设置文件与数据库相邻，方便用户备份或迁移整个本地数据目录。
        var json = File.ReadAllText(PreferencesPath);
        return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
    }

    /// <summary>将当前偏好保存到本地设置文件。</summary>
    public static Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(PreferencesPath, json, cancellationToken);
    }
}
