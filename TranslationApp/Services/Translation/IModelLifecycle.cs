namespace TranslationApp.Services.Translation;

public enum ModelState { NotStarted, Loading, Ready, Failed }

public interface IModelLifecycle
{
    ModelState State { get; }
    string StatusMessage { get; }
    event EventHandler? StateChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
}
