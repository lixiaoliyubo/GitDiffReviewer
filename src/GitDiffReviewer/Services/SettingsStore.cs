using System.IO;
using System.Text;
using System.Text.Json;
using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// settings.json 读写（§9 / §7.6）：临时文件 + 原子替换。
/// </summary>
public sealed class SettingsStore
{
    private readonly string _file;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public SettingsStore(string appDataDir)
    {
        _file = Path.Combine(appDataDir, "settings.json");
    }

    public async Task<Settings> LoadAsync()
    {
        if (!File.Exists(_file)) return new Settings();
        try
        {
            var json = await File.ReadAllTextAsync(_file);
            return JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public async Task SaveAsync(Settings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var json = JsonSerializer.Serialize(s, JsonOpts);
        var tmp = _file + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, _file, overwrite: true);
    }
}
