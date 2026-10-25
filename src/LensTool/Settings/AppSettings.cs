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

    /// <summary>Translation engines in the order they are tried ("Google", "DeepL", "Offline").</summary>
    public IReadOnlyList<string> TranslationEngines { get; init; } = ["Google", "DeepL", "Offline"];

    /// <summary>Free monthly character limit per engine (Google Basic and DeepL API Free: 500 000).</summary>
    public long FreeCharactersPerMonth { get; init; } = 500_000;

    /// <summary>Share of the free limit after which the next engine is used, to stay out of paid usage.</summary>
    public double SwitchEngineAtShare { get; init; } = 0.9;

    /// <summary>Folder where captured regions are saved.</summary>
    public string CaptureFolder { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LensTool");
}
