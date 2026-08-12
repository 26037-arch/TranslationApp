using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Translation;

namespace TranslationApp.Tests;

public sealed class GoogleTranslateTests
{
    [Fact]
    public void BuildsEncodedGoogleTranslateUrlFromSnapshottedLanguages()
    {
        const string text = "안녕하세요 & \"English\"\n둘째 줄";
        var request = TranslationRequest.Create(
            text,
            TargetLanguage.Korean,
            TargetLanguage.English,
            InputSource.Clipboard);

        var address = GoogleTranslateUrlBuilder.Build(request);

        Assert.Equal("https", address.Scheme);
        Assert.Equal("translate.google.com", address.Host);
        var decodedQuery = Uri.UnescapeDataString(address.Query);
        Assert.Contains("sl=ko", decodedQuery);
        Assert.Contains("tl=en", decodedQuery);
        Assert.Contains($"text={text}", decodedQuery);
        Assert.Contains("op=translate", decodedQuery);
        Assert.Equal(TargetLanguage.Korean, request.SourceLanguage);
        Assert.Equal(TargetLanguage.English, request.TargetLanguage);
    }

    [Fact]
    public void RejectsSameLanguageAndOversizedInputWithoutTruncating()
    {
        Assert.Throws<ArgumentException>(() => TranslationRequest.Create(
            "same",
            TargetLanguage.Korean,
            TargetLanguage.Korean,
            InputSource.Clipboard));

        var oversized = TranslationRequest.Create(
            new string('한', GoogleTranslateUrlBuilder.MaximumInputCharacters + 1),
            TargetLanguage.Korean,
            TargetLanguage.English,
            InputSource.Clipboard);
        var exception = Assert.Throws<ArgumentException>(() => GoogleTranslateUrlBuilder.Build(oversized));
        Assert.Contains(GoogleTranslateUrlBuilder.MaximumInputCharacters.ToString("N0"), exception.Message);
    }

    [Fact]
    public void DecodesUnicodeQuotesAndNewlinesFromWebViewJson()
    {
        const string json = "{\"text\":\"안녕 \\\"친구\\\"\\nsecond line 😀\",\"selector\":\"span[jsname=\\\"W297wb\\\"]\"}";

        var result = GoogleTranslateDomExtractor.Decode(json);

        Assert.NotNull(result);
        Assert.Equal("안녕 \"친구\"\nsecond line 😀", result.Text);
        Assert.Contains("W297wb", result.Selector);
    }

    [Fact]
    public void DomExtractionUsesCentralizedFallbackSelectors()
    {
        Assert.True(GoogleTranslateDomExtractor.TranslationResultSelectors.Length >= 5);
        Assert.Contains(GoogleTranslateDomExtractor.TranslationResultSelectors, value => value.Contains("data-result-index"));
        Assert.Contains(GoogleTranslateDomExtractor.TranslationResultSelectors, value => value.Contains("aria-live"));
        Assert.Contains(GoogleTranslateDomExtractor.TranslationResultSelectors, value => value.Contains("lang"));
        var script = GoogleTranslateDomExtractor.BuildScript("source", "ko");
        Assert.Contains("node.isConnected", script);
        Assert.DoesNotContain("getBoundingClientRect", script);
    }

    [Fact]
    public void ProductionAttemptAllowsSlowPageNavigation() =>
        Assert.Equal(TimeSpan.FromSeconds(15), GoogleTranslateService.TimeoutPerAttempt);

    [Fact]
    public async Task RetriesOnceThenReturnsSuccessfulResult()
    {
        var client = new SequenceWebClient(
            _ => Task.FromException<GoogleTranslateDomResult>(new InvalidOperationException("first")),
            _ => Task.FromResult(new GoogleTranslateDomResult("translated", "selector-2")));
        var service = new GoogleTranslateService(client, new AppLogger(), TimeSpan.FromMilliseconds(100));
        var request = CreateRequest();

        var result = await service.TranslateAsync(request, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal("translated", result.Primary.Text);
    }

    [Fact]
    public async Task TwoTimeoutsBecomeOneRequestFailure()
    {
        var client = new SequenceWebClient(
            token => NeverCompletes(token),
            token => NeverCompletes(token));
        var service = new GoogleTranslateService(client, new AppLogger(), TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranslateAsync(CreateRequest(), CancellationToken.None));
        Assert.Equal(GoogleTranslateService.MaximumAttempts, client.CallCount);
    }

    [Theory]
    [InlineData("https://go.microsoft.com/fwlink/p/?LinkId=2124703", true)]
    [InlineData("https://download.microsoft.com/file.exe", true)]
    [InlineData("http://go.microsoft.com/file.exe", false)]
    [InlineData("https://example.com/file.exe", false)]
    public void RuntimeInstallerOnlyTrustsOfficialHttpsAddresses(string value, bool expected) =>
        Assert.Equal(expected, WebView2RuntimeInstaller.IsOfficialMicrosoftDownload(new Uri(value)));

    private static TranslationRequest CreateRequest() => TranslationRequest.Create(
        "hello",
        TargetLanguage.English,
        TargetLanguage.Korean,
        InputSource.Clipboard);

    private static async Task<GoogleTranslateDomResult> NeverCompletes(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    private sealed class SequenceWebClient(
        params Func<CancellationToken, Task<GoogleTranslateDomResult>>[] attempts) : IGoogleTranslateWebClient
    {
        public int CallCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GoogleTranslateDomResult> TranslateOnceAsync(
            TranslationRequest request,
            Uri address,
            int attempt,
            CancellationToken cancellationToken)
        {
            var index = CallCount++;
            return attempts[index](cancellationToken);
        }
    }
}
