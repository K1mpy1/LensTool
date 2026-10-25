using System.IO;
using System.Windows.Input;

namespace LensTool.Settings;

/// <summary>Key combination for a global hotkey.</summary>
public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>
/// Application settings. Defaults only for now;
/// persistence and a settings window come in a later stage.
/// </summary>
public sealed class AppSettings
{
    public HotkeyGesture TranslateHotkey { get; init; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.T);

    public HotkeyGesture ImageSearchHotkey { get; init; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.S);

    /// <summary>OCR language (BCP-47, e.g. "ru" or "en-US"); null = Windows profile languages.</summary>
    public string? OcrLanguage { get; init; }

    /// <summary>Translation target language (DeepL code).</summary>
    public string TargetLanguage { get; init; } = "RU";

    /// <summary>Target for text that is already in <see cref="TargetLanguage"/>.</summary>
    public string FallbackTargetLanguage { get; init; } = "EN-US";

    /// <summary>Folder where captured regions are saved.</summary>
    public string CaptureFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LensTool");
}
