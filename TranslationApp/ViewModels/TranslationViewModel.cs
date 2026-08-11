using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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
    private readonly ITranslator _translator;
    private readonly ITranslationDocumentHost? _documentHost;
    private readonly NotificationService _notifications;
    private readonly AppLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private string _currentText = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _suppressDocumentUpdate;

    public TranslationViewModel(
        TranslationSession session,
        TranslationResult primaryResult,
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
        foreach (var candidate in primaryResult.Candidates) AddCandidate(candidate);
        _currentText = primaryResult.Primary.Text;
        if (Session.Segment is not null)
        {
            Session.Segment.PropertyChanged += SegmentOnPropertyChanged;
            Raise(nameof(AttachmentStatus));
            Raise(nameof(CanRestore));
        }
        ShowAlternativesCommand = new AsyncRelayCommand(LoadAlternativesAsync, () => !IsBusy);
        CopyAllCommand = new AsyncRelayCommand(CopyAllAsync, () => !string.IsNullOrEmpty(CurrentText));
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
        }
    }
    public string AttachmentStatus => Session.Segment?.AttachmentState switch
    {
        TranslationAttachmentState.Attached => "OCR 원문과 연결됨",
        TranslationAttachmentState.Detached => "원문과 연결되지 않음",
        _ => "외부 선택"
    };
    public bool CanRestore => Session.Segment?.AttachmentState == TranslationAttachmentState.Attached;

    public void SelectCandidate(TranslationCandidate candidate)
    {
        if (candidate.Text == CurrentText) return;
        CurrentText = candidate.Text;
    }

    private async Task LoadAlternativesAsync()
    {
        IsBusy = true;
        Status = "다른 번역을 생성하는 중…";
        try
        {
            var result = await _translator.TranslateAsync(OriginalText, Session.TargetLanguage, TranslationOptions.Alternatives, _lifetime.Token);
            foreach (var candidate in result.Candidates) AddCandidate(candidate);
            Status = $"중복을 제외한 {Candidates.Count}개 번역";
        }
        catch (OperationCanceledException) { Status = "취소됨"; }
        catch (Exception ex)
        {
            _logger.Error("대안 번역 생성 실패", ex);
            Status = "다른 번역을 생성하지 못했습니다.";
            _notifications.Show(Status, true);
        }
        finally { IsBusy = false; }
    }

    private void AddCandidate(TranslationCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text) || Candidates.Any(x => string.Equals(x.Text, candidate.Text, StringComparison.Ordinal))) return;
        Candidates.Add(candidate);
        if (Session.Segment is { } segment && !segment.Candidates.Any(x => string.Equals(x.Text, candidate.Text, StringComparison.Ordinal)))
            segment.Candidates.Add(candidate);
    }

    private async Task CopyAllAsync()
    {
        try
        {
            await ClipboardRetry.RunAsync(() => { System.Windows.Clipboard.SetText(CurrentText); return true; }, _lifetime.Token);
            Status = "번역 전체를 복사했습니다.";
        }
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

    private void SetCurrentFromDocument(string text)
    {
        _suppressDocumentUpdate = true;
        try { CurrentText = text; }
        finally { _suppressDocumentUpdate = false; }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (Session.Segment is not null) Session.Segment.PropertyChanged -= SegmentOnPropertyChanged;
    }
}
