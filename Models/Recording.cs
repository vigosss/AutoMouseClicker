using System.Text.Json.Serialization;

namespace Ming_AutoClicker.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecordedActionType { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecordedMouseButton { None, Left, Right, Middle }

public sealed class DesktopBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class RecordedAction
{
    public RecordedActionType Type { get; set; }
    public long DelayBeforeMs { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public RecordedMouseButton Button { get; set; }
    public int WheelDelta { get; set; }
    public uint VirtualKey { get; set; }
    public uint ScanCode { get; set; }
    public bool IsExtendedKey { get; set; }
    public string? KeyName { get; set; }
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public string InputText
    {
        get => Type is RecordedActionType.KeyDown or RecordedActionType.KeyUp
            ? KeyName ?? $"VK {VirtualKey}"
            : $"{X}, {Y}";
        set
        {
            if (Type is RecordedActionType.KeyDown or RecordedActionType.KeyUp)
            {
                var text = value?.Trim();
                if (string.IsNullOrEmpty(text)) return;

                System.Windows.Input.Key key;
                if (text.Length == 1 && char.IsDigit(text[0]))
                    key = System.Windows.Input.Key.D0 + (text[0] - '0');
                else if (!Enum.TryParse(text, true, out key) || key == System.Windows.Input.Key.None)
                    return;

                VirtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                ScanCode = 0;
                IsExtendedKey = false;
                KeyName = key.ToString();
                return;
            }

            var parts = (value ?? string.Empty)
                .Replace('，', ',')
                .Split(new[] { ',', ' ', ';', '；', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            {
                X = x;
                Y = y;
            }
        }
    }

    public RecordedAction Clone() => (RecordedAction)MemberwiseClone();
}

public sealed class Recording
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public HotkeyGesture? Hotkey { get; set; }
    public DesktopBounds DesktopBounds { get; set; } = new();
    public string KeyboardLayout { get; set; } = string.Empty;
    public List<RecordedAction> Actions { get; set; } = new();

    [JsonIgnore] public long Duration => Actions.Sum(a => Math.Max(0, a.DelayBeforeMs));
    [JsonIgnore] public int ActionCount => Actions.Count(a => a.IsEnabled);

    public Recording DeepClone() => new()
    {
        Id = Id, Name = Name, CreatedAt = CreatedAt, Hotkey = Hotkey?.Clone(),
        KeyboardLayout = KeyboardLayout,
        DesktopBounds = new DesktopBounds { X = DesktopBounds.X, Y = DesktopBounds.Y, Width = DesktopBounds.Width, Height = DesktopBounds.Height },
        Actions = Actions.Select(a => a.Clone()).ToList()
    };
}

public sealed class RecordingStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<Recording> Recordings { get; set; } = new();
}
