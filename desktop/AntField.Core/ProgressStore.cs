using System.Text.Json;

namespace AntField.Core;

public sealed record SavedProgress(string RunId, DateTimeOffset SavedAt, ColonySnapshot Colony, double Speed = 1, bool Paused = false, bool ShowTrails = false, bool ShowWalls = false);

public sealed class ProgressStore(string folder)
{
    public string PathName => Path.Combine(folder, "colony.json");
    public bool RecoveredBackup { get; private set; }
    public bool LoadFailed { get; private set; }
    static SavedProgress Read(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var saved = json.RootElement.TryGetProperty("Colony", out _)
            ? json.RootElement.Deserialize<SavedProgress>()!
            : new SavedProgress(Guid.NewGuid().ToString("N"), File.GetLastWriteTimeUtc(path), json.RootElement.Deserialize<ColonySnapshot>()!);
        if (saved is null || saved.Colony is null || !Guid.TryParse(saved.RunId, out _) || !double.IsFinite(saved.Speed)) throw new ArgumentException("无效的进度记录。");
        _ = DesktopColony.Restore(saved.Colony, saved.Colony.Width, saved.Colony.Height);
        return saved with { Speed = Math.Clamp(saved.Speed, .5, 4) };
    }
    public SavedProgress? Load()
    {
        RecoveredBackup = LoadFailed = false;
        foreach (string path in new[] { PathName, Path.Combine(folder, "colony.backup.json") })
        {
            if (!File.Exists(path)) continue;
            try { var saved = Read(path); RecoveredBackup = path != PathName; LoadFailed = false; return saved; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) { LoadFailed = true; }
        }
        return null;
    }
    public void Save(SavedProgress progress)
    {
        Directory.CreateDirectory(folder);
        string temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(progress));
        if (RecoveredBackup) { File.Move(temporary, PathName, true); RecoveredBackup = false; }
        else if (File.Exists(PathName)) File.Replace(temporary, PathName, Path.Combine(folder, "colony.backup.json"));
        else File.Move(temporary, PathName);
    }
    public SavedProgress[] History()
    {
        string history = Path.Combine(folder, "history");
        if (!Directory.Exists(history)) return [];
        var records = new List<SavedProgress>();
        foreach (string path in Directory.EnumerateFiles(history, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(10))
            try { records.Add(Read(path)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) { }
        return records.OrderByDescending(r => r.SavedAt).ToArray();
    }
}
