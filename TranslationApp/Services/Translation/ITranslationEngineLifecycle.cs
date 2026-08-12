namespace TranslationApp.Services.Translation;

public enum TranslationEngineState { NotStarted, Loading, Ready, Failed }

public interface ITranslationEngineLifecycle
{
    TranslationEngineState State { get; }
    string StatusMessage { get; }
    event EventHandler? StateChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
}
