using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TranslationApp.Models;

public sealed class TranslationSegment : INotifyPropertyChanged
{
    private string _currentText;
    private TranslationAttachmentState _attachmentState;
    private int _start;
    private int _length;

    public TranslationSegment(string originalText, TargetLanguage targetLanguage, int start, int length)
    {
        Id = Guid.NewGuid();
        OriginalText = originalText;
        _currentText = originalText;
        TargetLanguage = targetLanguage;
        CreatedStart = start;
        CreatedLength = length;
        _start = start;
        _length = length;
        _attachmentState = TranslationAttachmentState.Attached;
        CreatedAt = DateTimeOffset.Now;
    }

    public Guid Id { get; }
    public string OriginalText { get; }
    public string CurrentText { get => _currentText; private set => Set(ref _currentText, value); }
    public TargetLanguage TargetLanguage { get; }
    public TranslationAttachmentState AttachmentState { get => _attachmentState; private set => Set(ref _attachmentState, value); }
    public int Start { get => _start; private set => Set(ref _start, value); }
    public int Length { get => _length; private set => Set(ref _length, value); }
    public int CreatedStart { get; }
    public int CreatedLength { get; }
    public DateTimeOffset CreatedAt { get; }
    public List<TranslationCandidate> Candidates { get; } = [];

    public void UpdateRange(int start, int length, string currentText)
    {
        Start = start;
        Length = length;
        CurrentText = currentText;
    }

    public void ReplaceCurrent(string text)
    {
        CurrentText = text;
        Length = text.Length;
    }

    public void Detach() => AttachmentState = TranslationAttachmentState.Detached;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
