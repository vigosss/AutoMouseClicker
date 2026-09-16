using System.Text.Json;
using System.Text.Json.Serialization;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services;

public sealed class RecordingStorageService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public RecordingStorageService()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "recordings.json");
    }

    public List<Recording> LoadAll()
    {
        if (!File.Exists(_path)) { Write(new RecordingStore()); return new(); }
        try { return JsonSerializer.Deserialize<RecordingStore>(File.ReadAllText(_path), _options)?.Recordings ?? new(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[录制] 加载失败: {ex}");
            var backup = _path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Copy(_path, backup, overwrite: false); } catch { }
            return new();
        }
    }

    public void Save(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var store = new RecordingStore { Recordings = LoadAll() };
        var index = store.Recordings.FindIndex(r => r.Id == recording.Id);
        if (index >= 0) store.Recordings[index] = recording.DeepClone(); else store.Recordings.Add(recording.DeepClone());
        Write(store);
    }

    public void Delete(string id) { var s = new RecordingStore { Recordings = LoadAll() }; s.Recordings.RemoveAll(r => r.Id == id); Write(s); }
    public void Rename(string id, string name) { var r = LoadAll().FirstOrDefault(x => x.Id == id); if (r != null) { r.Name = name; Save(r); } }
    public void UpdateHotkey(string id, HotkeyGesture? hotkey) { var r = LoadAll().FirstOrDefault(x => x.Id == id); if (r != null) { r.Hotkey = hotkey?.Clone(); Save(r); } }

    public void ExportToFile(string id, string destination)
    {
        var recording = LoadAll().FirstOrDefault(r => r.Id == id) ?? throw new InvalidOperationException("Recording not found.");
        AtomicWrite(destination, JsonSerializer.Serialize(new RecordingStore { Recordings = new() { recording } }, _options));
    }

    public Recording ImportFromFile(string source)
    {
        var store = JsonSerializer.Deserialize<RecordingStore>(File.ReadAllText(source), _options);
        var recording = store?.Recordings.SingleOrDefault() ?? throw new InvalidDataException("The file must contain exactly one recording.");
        Validate(recording);
        recording.Id = Guid.NewGuid().ToString(); recording.CreatedAt = DateTime.Now;
        Save(recording); return recording;
    }

    private static void Validate(Recording r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Actions == null) throw new InvalidDataException("Invalid recording.");
        foreach (var a in r.Actions) if (a.DelayBeforeMs < 0 || a.DelayBeforeMs > 3_600_000) throw new InvalidDataException("Invalid action delay.");
    }
    private void Write(RecordingStore store) => AtomicWrite(_path, JsonSerializer.Serialize(store, _options));
    private static void AtomicWrite(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try { using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var sw = new StreamWriter(fs); sw.Write(json); sw.Flush(); fs.Flush(true); File.Move(temp, path, true); }
        catch { try { File.Delete(temp); } catch { } throw; }
    }
}
