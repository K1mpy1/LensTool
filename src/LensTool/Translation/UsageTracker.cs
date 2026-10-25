using System.IO;
using System.Text.Json;

namespace LensTool.Translation;

/// <summary>
/// Counts characters sent to each translation engine in the current calendar month,
/// persisted in a small JSON file so the count survives restarts.
/// </summary>
public sealed class UsageTracker(string filePath, Func<DateTime>? clock = null)
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LensTool", "usage.json");

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private readonly object _sync = new();
    private UsageData? _data;

    public long Used(string engine)
    {
        lock (_sync)
            return Current().Characters.GetValueOrDefault(engine);
    }

    public void Add(string engine, long characters)
    {
        lock (_sync)
        {
            var data = Current();
            data.Characters[engine] = data.Characters.GetValueOrDefault(engine) + characters;
            Save(data);
        }
    }

    /// <summary>Replaces the local count with an authoritative value reported by the engine.</summary>
    public void Set(string engine, long characters)
    {
        lock (_sync)
        {
            var data = Current();
            if (data.Characters.GetValueOrDefault(engine) == characters)
                return;
            data.Characters[engine] = characters;
            Save(data);
        }
    }

    /// <summary>Loaded data for this month; a new month starts from zero.</summary>
    private UsageData Current()
    {
        string month = _clock().ToString("yyyy-MM");
        _data ??= Load();
        if (_data.Month != month)
            _data = new UsageData { Month = month };
        return _data;
    }

    private UsageData Load()
    {
        try
        {
            if (File.Exists(filePath))
                return JsonSerializer.Deserialize<UsageData>(File.ReadAllText(filePath)) ?? new UsageData();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Error($"Не удалось прочитать {filePath}, счётчик начат заново", ex);
        }
        return new UsageData();
    }

    private void Save(UsageData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"Не удалось сохранить {filePath}", ex);
        }
    }

    private sealed class UsageData
    {
        public string Month { get; set; } = "";
        public Dictionary<string, long> Characters { get; set; } = [];
    }
}
