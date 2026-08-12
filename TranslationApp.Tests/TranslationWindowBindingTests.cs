using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Notifications;
using TranslationApp.Services.Translation;
using TranslationApp.ViewModels;
using TranslationApp.Views;

namespace TranslationApp.Tests;

public sealed class TranslationWindowBindingTests
{
    [Fact]
    public void OriginalTextBindingIsOneWay()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                application.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();

                var session = new TranslationSession
                {
                    OriginalText = "original",
                    SourceLanguage = TargetLanguage.English,
                    TargetLanguage = TargetLanguage.Korean,
                    Source = InputSource.Clipboard
                };
                var viewModel = new TranslationViewModel(
                    session,
                    new StubTranslator(),
                    null,
                    new NotificationService(),
                    new AppLogger());

                var window = new TranslationWindow(viewModel);
                var originalTextBox = Assert.IsType<TextBox>(window.FindName("OriginalTextBox"));
                var binding = BindingOperations.GetBinding(originalTextBox, TextBox.TextProperty);

                Assert.NotNull(binding);
                Assert.Equal(BindingMode.OneWay, binding.Mode);
                Assert.True(window.Topmost);
                Assert.False(window.ShowActivated);

                window.Close();
                application.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private sealed class StubTranslator : ITranslator
    {
        public Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
