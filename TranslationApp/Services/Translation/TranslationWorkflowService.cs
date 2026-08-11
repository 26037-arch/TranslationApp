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
    SelectionReader selectionReader,
    ITranslator translator,
    IModelLifecycle modelLifecycle,
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
            else await TranslateExternalSelectionAsync(foreground, target);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.Error("단축키 작업 실패", ex);
            notifications.Show(UserMessage(ex), true);
        }
    }

    private async Task TranslateExternalSelectionAsync(IntPtr foreground, TargetLanguage target)
    {
        var anchor = selectionBounds.GetSelectionOrCursor(foreground);
        var text = await selectionReader.ReadExternalSelectionAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(text))
        {
            notifications.Show("번역할 텍스트를 선택하세요.");
            return;
        }
        await TranslateAndShowAsync(text, target, InputSource.ExternalSelection, null, null, anchor);
    }

    private async Task TranslateOcrSelectionAsync(OcrSourceWindow sourceWindow, TargetLanguage target)
    {
        if (!sourceWindow.TryGetSelection(out var text, out var start, out var length))
        {
            notifications.Show("번역할 텍스트를 선택하세요.");
            return;
        }
        var anchor = sourceWindow.GetSelectionScreenBounds() ?? selectionBounds.GetSelectionOrCursor(sourceWindow.Handle);
        var segment = sourceWindow.Controller.CreateSegment(start, length, target);
        await TranslateAndShowAsync(text, target, InputSource.OcrDocument, segment, sourceWindow, anchor);
    }

    private async Task TranslateAndShowAsync(string text, TargetLanguage target, InputSource source,
        TranslationSegment? segment, OcrSourceWindow? sourceWindow, Rectangle anchor)
    {
        if (modelLifecycle.State == ModelState.Loading) notifications.Show("번역 모델을 불러오는 중입니다. 준비되면 요청을 처리합니다.");
        var result = await translator.TranslateAsync(text, target, TranslationOptions.Primary, CancellationToken.None);
        if (segment is { AttachmentState: TranslationAttachmentState.Attached } && sourceWindow is not null)
        {
            segment.Candidates.AddRange(result.Candidates);
            sourceWindow.Controller.ReplaceSegmentText(segment, result.Primary.Text);
        }
        var session = new TranslationSession { OriginalText = text, TargetLanguage = target, Source = source, Segment = segment };
        var viewModel = new TranslationViewModel(session, result, translator, sourceWindow?.Controller, notifications, logger);
        var window = new TranslationWindow(viewModel);
        presenter.ShowNear(window, anchor);
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
        if (ex is FileNotFoundException or DirectoryNotFoundException) return ex.Message;
        if (ex.Message.Contains("worker", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("NLLB", StringComparison.OrdinalIgnoreCase))
            return ex.Message;
        if (ex.Message.Contains("Tesseract", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase))
            return ex.Message;
        return "작업을 완료하지 못했습니다. 로그를 확인하세요.";
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
