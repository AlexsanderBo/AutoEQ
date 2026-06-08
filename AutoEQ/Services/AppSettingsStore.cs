using System.IO;
using System.Text.Json;

namespace AutoEQ.Services;

/// <summary>Lưu cài đặt app cấp người dùng (hiện chỉ ngôn ngữ) vào %AppData%/AutoEQ/settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
}

public static class AppSettingsStore
{
    private static readonly string Path_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoEQ", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                string json = File.ReadAllText(Path_);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // File hỏng/không đọc được -> dùng mặc định.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path_);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Không ghi được settings thì bỏ qua, không chặn app.
        }
    }
}
