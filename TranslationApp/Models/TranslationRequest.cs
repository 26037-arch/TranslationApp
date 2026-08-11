namespace TranslationApp.Models;

public sealed record TranslationRequest(
    Guid Id,
    string Text,
    TargetLanguage TargetLanguage,
    TranslationOptions Options,
    InputSource Source);
