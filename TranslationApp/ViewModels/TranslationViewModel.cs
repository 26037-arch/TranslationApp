using System.Collections.ObjectModel;
using System.ComponentModel;
using TranslationApp.Infrastructure;
using TranslationApp.Models;
using TranslationApp.Services.Clipboard;
using TranslationApp.Services.Documents;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Notifications;
using TranslationApp.Services.Translation;

namespace TranslationApp.ViewModels;

public sealed class TranslationViewModel : ObservableObject, IDisposable
{
    private const string LoadingText = "번역 중…";
    private const string FailureText = "번역에 실패했습니다.";
    private readonly ITranslator _translator;
    private readonly ITranslationDocumentHost? _documentHost;
    private readonly NotificationService _notifications;
    private readonly AppLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private string _currentText = LoadingText;
    private string _status = LoadingText;
    private bool _isBusy = true;
    private bool _suppressDocumentUpdate;
    private bool _disposed;
    private Guid _activeRequestId;

    public TranslationViewModel(
        TranslationSession session,
        ITranslator translator,
        ITranslationDocumentHost? documentHost,
        NotificationService notifications,
        AppLogger logger)
    {
        Session = session;
        _translator = translator;
        _documentHost = documentHost;
        _notifications = notifications;
        _logger = logger;
        if (Session.Segment is not null)
        {
            Session.Segment.PropertyChanged += SegmentOnPropertyChanged;
            Raise(nameof(AttachmentStatus));
            Raise(nameof(CanRestore));
        }
        ShowAlternativesCommand = new AsyncRelayCommand(LoadAlternativesAsync, () => !IsBusy && Candidates.Count > 0);
        CopyAllCommand = new AsyncRelayCommand(CopyAllAsync, () => !IsBusy && Candidates.Count > 0);
        RestoreOriginalCommand = new RelayCommand(RestoreOriginal, () => CanRestore);
    }

    public TranslationSession Session { get; }
    public string OriginalText => Session.OriginalText;
    public ObservableCollection<TranslationCandidate> Candidates => Session.Candidates;
    public AsyncRelayCommand ShowAlternativesCommand { get; }
    public AsyncRelayCommand CopyAllCommand { get; }
    public RelayCommand RestoreOriginalCommand { get; }

    public string CurrentText
    {
        get => _currentText;
        set
        {
            if (!Set(ref _currentText, value)) return;
            if (!_suppressDocumentUpdate && Session.Segment is { AttachmentState: TranslationAttachmentState.Attached } segment)
                _documentHost?.ReplaceSegmentText(segment, value);
            CopyAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            ShowAlternativesCommand.RaiseCanExecuteChanged();
            CopyAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string AttachmentStatus => Session.Segment?.AttachmentState switch
    {
        TranslationAttachmentState.Attached => "OCR 원문과 연결됨",
        TranslationAttachmentState.Detached => "원문과 연결되지 않음",
        _ => "외부 선택"
    };

    public bool CanRestore => Session.Segment?.AttachmentState == TranslationAttachmentState.Attached;

    public async Task StartTranslationAsync(TranslationRequest request)
    {
        if (_disposed) return;
        _activeRequestId = request.RequestId;
        IsBusy = true;
        Status = LoadingText;
        SetDisplayedText(LoadingText, updateDocument: false);
        try
        {
            var result = await _translator.TranslateAsync(request, _lifetime.Token);
            if (!CanApply(result)) return;
            foreach (var candidate in result.Candidates) AddCandidate(candidate);
            SetDisplayedText(result.Primary.Text, updateDocument: true);
            Status = $"번역 완료 ({result.Elapsed.TotalSeconds:0.0}초)";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_disposed || request.RequestId != _activeRequestId) return;
            _logger.Error($"번역 실패 request={request.RequestId:N}", ex);
            SetDisplayedText(FailureText, updateDocument: false);
            Status = UserFailureMessage(ex);
            _notifications.Show(Status, true);
        }
        finally
        {
            if (!_disposed && request.RequestId == _activeRequestId) IsBusy = false;
        }
    }

    public void SelectCandidate(TranslationCandidate candidate)
    {
        if (IsBusy || candidate.Text == CurrentText) return;
        CurrentText = candidate.Text;
    }

    private async Task LoadAlternativesAsync()
    {
        var request = TranslationRequest.Create(
            OriginalText,
            Session.SourceLanguage,
            Session.TargetLanguage,
            Session.Source);
        _activeRequestId = request.RequestId;
        IsBusy = true;
        Status = "Google Translate에 다시 요청하는 중…";
        try
        {
            var previousCount = Candidates.Count;
            var result = await _translator.TranslateAsync(request, _lifetime.Token);
            if (!CanApply(result)) return;
            foreach (var candidate in result.Candidates) AddCandidate(candidate);
            Status = Candidates.Count == previousCount
                ? "동일한 번역 결과입니다."
                : $"중복을 제외한 {Candidates.Count}개 번역";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_disposed || request.RequestId != _activeRequestId) return;
            _logger.Error($"번역 재요청 실패 request={request.RequestId:N}", ex);
            Status = UserFailureMessage(ex);
            _notifications.Show(Status, true);
        }
        finally
        {
            if (!_disposed && request.RequestId == _activeRequestId) IsBusy = false;
        }
    }

    private bool CanApply(TranslationResult result) =>
        !_disposed
        && result.RequestId == _activeRequestId
        && result.SourceLanguage == Session.SourceLanguage
        && result.TargetLanguage == Session.TargetLanguage;

    private void AddCandidate(TranslationCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text)
            || Candidates.Any(x => string.Equals(x.Text, candidate.Text, StringComparison.Ordinal))) return;
        Candidates.Add(candidate);
        if (Session.Segment is { } segment
            && !segment.Candidates.Any(x => string.Equals(x.Text, candidate.Text, StringComparison.Ordinal)))
            segment.Candidates.Add(candidate);
        ShowAlternativesCommand.RaiseCanExecuteChanged();
        CopyAllCommand.RaiseCanExecuteChanged();
    }

    private async Task CopyAllAsync()
    {
        try
        {
            await ClipboardRetry.RunAsync(
                () => { System.Windows.Clipboard.SetText(CurrentText); return true; },
                _lifetime.Token);
            Status = "번역 전체를 복사했습니다.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.Error("번역 결과 복사 실패", ex);
            _notifications.Show("클립보드에 복사하지 못했습니다.", true);
        }
    }

    private void RestoreOriginal()
    {
        if (Session.Segment is not { } segment || _documentHost?.RestoreOriginal(segment) != true) return;
        SetCurrentFromDocument(segment.CurrentText);
    }

    private void SegmentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationSegment.CurrentText) && sender is TranslationSegment segment)
            SetCurrentFromDocument(segment.CurrentText);
        if (e.PropertyName == nameof(TranslationSegment.AttachmentState))
        {
            Raise(nameof(AttachmentStatus));
            Raise(nameof(CanRestore));
            RestoreOriginalCommand.RaiseCanExecuteChanged();
        }
    }

    private void SetDisplayedText(string text, bool updateDocument)
    {
        if (updateDocument) { CurrentText = text; return; }
        _suppressDocumentUpdate = true;
        try { CurrentText = text; }
        finally { _suppressDocumentUpdate = false; }
    }

    private void SetCurrentFromDocument(string text) => SetDisplayedText(text, updateDocument: false);

    private static string UserFailureMessage(Exception exception) => exception switch
    {
        ArgumentException => exception.Message,
        _ => "Google Translate 번역에 실패했습니다. 인터넷 연결을 확인하세요."
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (Session.Segment is not null) Session.Segment.PropertyChanged -= SegmentOnPropertyChanged;
        _lifetime.Dispose();
    }
}
