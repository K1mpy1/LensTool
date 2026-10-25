using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LensTool.Capture;
using LensTool.Ocr;
using LensTool.Settings;
using LensTool.UI;

namespace LensTool;

public partial class App : Application
{
    private const string InstanceId = "LensTool.{9C3F6A52-4E1B-4F7D-9A0E-2B7C5D8E1F34}";

    private readonly AppSettings _settings = new();
    private readonly RegionSelector _selector = new();

    private Mutex? _instanceMutex;
    private EventWaitHandle? _secondInstanceEvent;
    private RegisteredWaitHandle? _secondInstanceWait;
    private TrayIcon? _tray;
    private GlobalHotkeys? _hotkeys;
    private IOcrEngine? _ocr;

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
        _tray.AddMenuItem("Открыть папку снимков", OpenCaptureFolder);
        _tray.AddSeparator();
        _tray.AddMenuItem("Выход", Shutdown);

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
                await RecognizeAsync(result);
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

    private async Task RecognizeAsync(CaptureResult capture)
    {
        // The engine is created on first use, so the app still starts when no OCR languages are installed.
        _ocr ??= new WindowsOcrEngine(_settings.OcrLanguage);
        var ocr = await _ocr.RecognizeAsync(capture.Image);
        if (ocr.IsEmpty)
        {
            _tray?.ShowBalloon("Текст не найден", "В выделенной области не удалось распознать текст.");
            return;
        }

        // Until the translation overlay exists (stage 5), hand the text over via the clipboard.
        string text = ocr.Text;
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex) // clipboard is locked by another app
        {
            AppLog.Error("Не удалось записать в буфер обмена", ex);
        }

        const int previewLength = 200;
        _tray?.ShowBalloon($"Распознано ({ocr.Language}) — скопировано",
            text.Length > previewLength ? text[..previewLength] + "…" : text);
    }

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
