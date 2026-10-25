using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LensTool.Capture;
using LensTool.Ocr;
using System.Net.Http;
using LensTool.Settings;
using LensTool.Translation;
using LensTool.UI;

namespace LensTool;

public partial class App : Application
{
    private const string InstanceId = "LensTool.{9C3F6A52-4E1B-4F7D-9A0E-2B7C5D8E1F34}";

    private readonly AppSettings _settings = new();
    private readonly RegionSelector _selector = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly UsageTracker _usage = new(UsageTracker.DefaultFilePath);
    private readonly DeepLTranslator _deepl;
    private readonly Dictionary<string, (ITranslator Engine, Func<string?> Key, string KeyVariable)> _engines;
    private readonly TranslationService _translation;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _secondInstanceEvent;
    private RegisteredWaitHandle? _secondInstanceWait;
    private TrayIcon? _tray;
    private GlobalHotkeys? _hotkeys;
    private IOcrEngine? _ocr;

    public App()
    {
        _deepl = new DeepLTranslator(_http, DeepLTranslator.ReadApiKeyFromEnvironment);
        var google = new GoogleCloudTranslator(_http, GoogleCloudTranslator.ReadApiKeyFromEnvironment);
        _engines = new(StringComparer.OrdinalIgnoreCase)
        {
            [google.Name] = (google, GoogleCloudTranslator.ReadApiKeyFromEnvironment, GoogleCloudTranslator.ApiKeyVariable),
            [_deepl.Name] = (_deepl, DeepLTranslator.ReadApiKeyFromEnvironment, DeepLTranslator.ApiKeyVariable),
        };

        // Engines are tried in the order from settings: Google first, DeepL when Google is unavailable or near its limit.
        var chain = new FallbackTranslator(
            _settings.TranslationEngines.Select(name => _engines[name].Engine).ToList(),
            _usage, _settings.FreeCharactersPerMonth, _settings.SwitchEngineAtShare);
        _translation = new TranslationService(chain, _settings.TargetLanguage, _settings.FallbackTargetLanguage);
    }

    /// <summary>"Google: 12 345 / 500 000 (2,5%)" or a hint that the key is missing.</summary>
    private string UsageLine(string engine)
    {
        var (_, key, variable) = _engines[engine];
        if (key() is null)
            return $"{engine}: нет ключа ({variable})";

        long used = _usage.Used(engine);
        long limit = _settings.FreeCharactersPerMonth;
        return $"{engine}: {used:N0} / {limit:N0} ({100.0 * used / limit:0.#}%)";
    }

    private void UpdateUsageDisplay() =>
        _tray?.SetTooltip("LensTool — " + string.Join(", ", _settings.TranslationEngines.Select(e => $"{e} {_usage.Used(e):N0}")));

    /// <summary>DeepL reports its own count for the billing period; it overrides the local estimate.</summary>
    private async Task SyncDeepLUsageAsync()
    {
        var usage = await _deepl.GetUsageAsync();
        if (usage is null)
            return;
        _usage.Set(_deepl.Name, usage.CharacterCount);
        AppLog.Info($"DeepL: по данным сервиса израсходовано {usage.CharacterCount} из {usage.CharacterLimit}");
        UpdateUsageDisplay();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch only signals the first one and exits.
        _instanceMutex = new Mutex(true, $@"Local\{InstanceId}.Mutex", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (EventWaitHandle.TryOpenExisting($@"Local\{InstanceId}.Activate", out var existing))
                using (existing)
                    existing.Set();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _secondInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceId}.Activate");
        _secondInstanceWait = ThreadPool.RegisterWaitForSingleObject(_secondInstanceEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
                _tray?.ShowBalloon("LensTool уже запущен", "Программа работает в трее.")),
            null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandledException;
        AppLog.Info("Запуск LensTool");

        _tray = new TrayIcon("LensTool");
        _tray.AddMenuItem("Перевести область", () => StartCapture(CaptureMode.Translate), _settings.TranslateHotkey.ToString(), bold: true);
        _tray.AddMenuItem("Поиск по картинке", () => StartCapture(CaptureMode.ImageSearch), _settings.ImageSearchHotkey.ToString());
        _tray.AddSeparator();
        _tray.AddInfoItem(() => "Символов перевода за месяц:");
        foreach (string engine in _settings.TranslationEngines)
            _tray.AddInfoItem(() => "    " + UsageLine(engine));
        _tray.MenuOpening += () => _ = SyncDeepLUsageAsync();
        _tray.AddSeparator();
        _tray.AddMenuItem("Открыть папку снимков", OpenCaptureFolder);
        _tray.AddSeparator();
        _tray.AddMenuItem("Выход", Shutdown);

        UpdateUsageDisplay();
        _ = SyncDeepLUsageAsync();

        _hotkeys = new GlobalHotkeys();
        var failed = new List<string>();
        if (!_hotkeys.TryRegister(_settings.TranslateHotkey, () => StartCapture(CaptureMode.Translate)))
            failed.Add(_settings.TranslateHotkey.ToString());
        if (!_hotkeys.TryRegister(_settings.ImageSearchHotkey, () => StartCapture(CaptureMode.ImageSearch)))
            failed.Add(_settings.ImageSearchHotkey.ToString());

        if (failed.Count > 0)
            _tray.ShowBalloon("Горячие клавиши заняты",
                $"Не удалось назначить {string.Join(", ", failed)} — их использует другая программа. Доступно меню в трее.",
                isError: true);
        else
            _tray.ShowBalloon("LensTool запущен",
                $"{_settings.TranslateHotkey} — перевод, {_settings.ImageSearchHotkey} — поиск по картинке.");
    }

    private async void StartCapture(CaptureMode mode)
    {
        // Pressing the hotkey again during selection cancels it.
        if (_selector.IsActive)
        {
            _selector.Cancel();
            return;
        }

        try
        {
            string hint = mode == CaptureMode.Translate
                ? "Перевод: выделите область с текстом  ·  Esc — отмена"
                : "Поиск по картинке: выделите область  ·  Esc — отмена";

            var result = await _selector.SelectAsync(hint);
            if (result is null)
            {
                AppLog.Info("Выделение отменено");
                return;
            }

            if (mode == CaptureMode.Translate)
            {
                await TranslateCaptureAsync(result);
                return;
            }

            // Image search is stage 6; for now just save the capture.
            string path = CaptureStorage.SavePng(result.Image, _settings.CaptureFolder, mode);
            AppLog.Info($"Снимок сохранён: {path}");
            _tray?.ShowBalloon("Снимок сохранён", $"{result.Bounds.Width}×{result.Bounds.Height} px\n{path}",
                onClick: () => Process.Start("explorer.exe", $"/select,\"{path}\""));
        }
        catch (OcrUnavailableException ex)
        {
            AppLog.Error("OCR недоступен", ex);
            _tray?.ShowBalloon("Распознавание недоступно", ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка захвата", ex);
            _tray?.ShowBalloon("Ошибка захвата", ex.Message, isError: true);
        }
    }

    private async Task TranslateCaptureAsync(CaptureResult capture)
    {
        // The engine is created on first use, so the app still starts when no OCR languages are installed.
        _ocr ??= new WindowsOcrEngine(_settings.OcrLanguage);
        var ocr = await _ocr.RecognizeAsync(capture.Image);
        if (ocr.IsEmpty)
        {
            _tray?.ShowBalloon("Текст не найден", "В выделенной области не удалось распознать текст.");
            return;
        }

        string text = OcrTextLayout.ToParagraphs(ocr.Lines);
        TranslationResult translation;
        try
        {
            translation = await _translation.TranslateAsync(text);
        }
        catch (TranslationException ex)
        {
            // Recognized text is still useful without a translation.
            AppLog.Error("Перевод не удался", ex);
            CopyToClipboard(text);
            _tray?.ShowBalloon("Перевод недоступен — скопирован исходный текст", ex.Message, isError: true);
            return;
        }

        // Until the translation overlay exists (stage 5), hand the result over via the clipboard.
        CopyToClipboard(translation.Text);
        UpdateUsageDisplay();
        string engine = translation.Engine ?? "";
        _tray?.ShowBalloon(
            $"Перевод {translation.DetectedSourceLanguage} → {translation.TargetLanguage} ({engine}) — скопирован",
            Preview(translation.Text, 160) + Environment.NewLine + UsageLine(engine));
        if (engine == _deepl.Name)
            _ = SyncDeepLUsageAsync();
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex) // clipboard is locked by another app
        {
            AppLog.Error("Не удалось записать в буфер обмена", ex);
        }
    }

    private static string Preview(string text, int maxLength = 200) =>
        text.Length > maxLength ? text[..maxLength] + "…" : text;

    private void OpenCaptureFolder()
    {
        Directory.CreateDirectory(_settings.CaptureFolder);
        Process.Start("explorer.exe", $"\"{_settings.CaptureFolder}\"");
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Необработанное исключение", e.Exception);
        _tray?.ShowBalloon("LensTool: ошибка", e.Exception.Message, isError: true);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _http.Dispose();
        _secondInstanceWait?.Unregister(null);
        _secondInstanceEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            AppLog.Info("Выход");
        }
        base.OnExit(e);
    }
}
