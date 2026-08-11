using TranslationApp.Infrastructure;
using TranslationApp.Models;

namespace TranslationApp.ViewModels;

public sealed class OcrSourceViewModel(OcrDocument document) : ObservableObject
{
    private string _status = "텍스트를 선택한 뒤 Ctrl+Alt+E 또는 Ctrl+Alt+K를 누르세요.";
    public OcrDocument Document { get; } = document;
    public string Status { get => _status; set => Set(ref _status, value); }
}
