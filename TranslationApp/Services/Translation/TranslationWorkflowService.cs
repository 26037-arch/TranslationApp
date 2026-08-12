using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using TranslationApp.Models;
using TranslationApp.Services.Clipboard;
using TranslationApp.Services.Hotkeys;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Notifications;
using TranslationApp.Services.Ocr;
using TranslationApp.Services.UiAutomation;
using TranslationApp.Services.Windows;
using TranslationApp.ViewModels;
using TranslationApp.Views;

namespace TranslationApp.Services.Translation;

public sealed class TranslationWorkflowService(
    ClipboardTextReader clipboardTextReader,
    ITranslator translator,
    IOcrService ocrService,
    ScreenCaptureService screenCapture,
    SelectionBoundsService selectionBounds,
    WindowRegistry windows,
    TranslationWindowPresenter presenter,
    NotificationService notifications,
    AppLogger logger)
{
    public async Task HandleHotkeyAsync(HotkeyAction action)
    {
        try
        {
            if (action == HotkeyAction.CaptureOcr) { await CaptureOcrAsync(); return; }
            var target = action == HotkeyAction.TranslateToEnglish ? TargetLanguage.English : TargetLanguage.Korean;
            var foreground = GetForegroundWindow();
            var ocrWindow = windows.OcrWindows.FirstOrDefault(x => new WindowInteropHelper(x).Handle == foreground);
            if (ocrWindow is not null) await TranslateOcrSelectionAsync(ocrWindow, target);
            else await TranslateClipboardTextAsync(foreground, target);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.Error("단축키 작업 실패", ex);
            notifications.Show(UserMessage(ex), true);
        }
    }

    private async Task TranslateClipboardTextAsync(IntPtr foreground, TargetLanguage target)
    {
        var anchor = selectionBounds.GetSelectionOrCursor(foreground);
        var text = await clipboardTextReader.ReadTextAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(text))
        {
            notifications.Show("클립보드에 번역할 텍스트가 없습니다.", true);
            return;
        }
        TranslateAndShow(text, target, InputSource.Clipboard, null, null, anchor);
    }

    private Task TranslateOcrSelectionAsync(OcrSourceWindow sourceWindow, TargetLanguage target)
    {
        if (!sourceWindow.TryGetSelection(out var text, out var start, out var length))
        {
            notifications.Show("번역할 텍스트를 선택하세요.");
            return Task.CompletedTask;
        }
        var anchor = sourceWindow.GetSelectionScreenBounds() ?? selectionBounds.GetSelectionOrCursor(sourceWindow.Handle);
        var segment = sourceWindow.Controller.CreateSegment(start, length, target);
        TranslateAndShow(text, target, InputSource.OcrDocument, segment, sourceWindow, anchor);
        return Task.CompletedTask;
    }

    private void TranslateAndShow(
        string text,
        TargetLanguage target,
        InputSource source,
        TranslationSegment? segment,
        OcrSourceWindow? sourceWindow,
        Rectangle anchor)
    {
        var sourceLanguage = GoogleTranslateLanguageCodes.SourceFor(target);
        var request = TranslationRequest.Create(text, sourceLanguage, target, source);
        var session = new TranslationSession
        {
            OriginalText = text,
            SourceLanguage = sourceLanguage,
            TargetLanguage = target,
            Source = source,
            Segment = segment
        };
        var viewModel = new TranslationViewModel(
            session,
            translator,
            sourceWindow?.Controller,
            notifications,
            logger);
        var window = new TranslationWindow(viewModel);
        presenter.ShowNear(window, anchor);
        _ = viewModel.StartTranslationAsync(request);
    }

    private async Task CaptureOcrAsync()
    {
        using var capture = screenCapture.CaptureCurrentMonitor();
        var overlay = new OcrSelectionOverlay(capture);
        var selection = await overlay.SelectAsync();
        if (selection is not { Width: >= 5, Height: >= 5 }) return;
        using var crop = capture.Crop(selection.Value);
        var text = OcrResultValidator.Normalize(await ocrService.RecognizeAsync(crop, CancellationToken.None));
        if (text is null)
        {
            notifications.Show("OCR 결과가 없습니다.");
            return;
        }
        var document = new OcrDocument(text);
        var window = new OcrSourceWindow(new OcrSourceViewModel(document));
        windows.Add(window);
        window.Show();
        window.Activate();
    }

    private static string UserMessage(Exception ex)
    {
        if (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException) return ex.Message;
        if (ex.Message.Contains("Tesseract", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase)) return ex.Message;
        return "작업을 완료하지 못했습니다. 로그를 확인하세요.";
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
