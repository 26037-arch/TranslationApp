using System.IO;
using System.Windows;
using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Translation;
using TranslationApp.Views;

namespace TranslationApp.Tests;

public sealed class GoogleTranslateLiveTests
{
    [Fact]
    [Trait("Category", "WebIntegration")]
    public void LiveGoogleTranslatePageReturnsExpectedText()
    {
        if (Environment.GetEnvironmentVariable("TRANSLATIONAPP_RUN_WEB_TESTS") != "1") return;

        Exception? failure = null;
        string? translatedText = null;
        var userDataFolder = Path.Combine(Path.GetTempPath(), "TranslationApp.Tests", Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var logger = new AppLogger();
            using var runtimeInstaller = new WebView2RuntimeInstaller(logger);
            var host = new GoogleTranslateHostWindow(runtimeInstaller, logger, lifetime.Token, userDataFolder);
            host.Show();

            application.Dispatcher.BeginInvoke((Action)(async () =>
            {
                try
                {
                    var service = new GoogleTranslateService(host, logger);
                    var request = TranslationRequest.Create(
                        "This is a test.",
                        TargetLanguage.English,
                        TargetLanguage.Korean,
                        InputSource.Clipboard);
                    translatedText = (await service.TranslateAsync(request, lifetime.Token)).Primary.Text;
                    var browser = Assert.IsType<Microsoft.Web.WebView2.Wpf.WebView2>(host.FindName("Browser"));
                    var structure = await browser.CoreWebView2.ExecuteScriptAsync("""
                        (() => {
                          let node = document.querySelector('span[jsname="W297wb"]');
                          const result = [];
                          for (let depth = 0; node && depth < 14; depth++, node = node.parentElement) {
                            result.push({
                              tag: node.tagName,
                              attributes: Object.fromEntries(Array.from(node.attributes)
                                .filter(attribute => attribute.name === 'jsname'
                                  || attribute.name === 'role'
                                  || attribute.name.startsWith('aria-')
                                  || attribute.name === 'data-node-index'
                                  || attribute.name === 'data-result-index'
                                  || attribute.name === 'data-language-for-alternatives')
                                .map(attribute => [attribute.name, attribute.value]))
                            });
                          }
                          return result;
                        })()
                        """);
                    logger.Info($"Live Google DOM structure (no text): {structure}");
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    host.Dispose();
                    application.Shutdown();
                }
            }));
            application.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "Live Google Translate test did not finish.");
            Assert.Null(failure);
            Assert.NotNull(translatedText);
            Assert.NotEqual("This is a test.", translatedText);
        }
        finally
        {
            try { Directory.Delete(userDataFolder, recursive: true); } catch { }
        }
    }
}
