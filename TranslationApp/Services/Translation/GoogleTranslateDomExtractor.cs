using System.Text.Json;

namespace TranslationApp.Services.Translation;

internal sealed record GoogleTranslateDomResult(string Text, string Selector);

internal static class GoogleTranslateDomExtractor
{
    internal static readonly string[] TranslationResultSelectors =
    [
        "c-wiz[role=\"region\"][data-node-index] span[jsname=\"W297wb\"]",
        "[jsname=\"jqKxS\"] span[jsname=\"W297wb\"]",
        "[jsname=\"txFAF\"] > span[jsname=\"W297wb\"]",
        "[data-result-index=\"0\"] span[jsname=\"W297wb\"]",
        "[data-result-index] span[jsname=\"W297wb\"]",
        "div[aria-live=\"polite\"] span[jsname=\"W297wb\"]",
        "[data-language-for-alternatives] span[jsname=\"W297wb\"]",
        "[data-result-index=\"0\"] span[lang]",
        "[data-result-index] span[lang]",
        "span[jsname=\"W297wb\"]"
    ];

    internal const string DiagnosticScript = """
        (() => ({
          readyState: document.readyState,
          path: location.pathname,
          documentLanguage: document.documentElement.lang || null,
          bodyChildren: document.body?.childElementCount ?? 0,
          resultNodes: document.querySelectorAll('span[jsname="W297wb"], .ryNqvb').length,
          consentDialog: Boolean(document.querySelector('[role="dialog"]')),
          captcha: Boolean(document.querySelector('iframe[src*="recaptcha"], [id*="captcha" i]'))
        }))()
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string BuildScript(string sourceText, string targetLanguageCode)
    {
        var sourceJson = JsonSerializer.Serialize(sourceText);
        var targetJson = JsonSerializer.Serialize(targetLanguageCode);
        var selectorsJson = JsonSerializer.Serialize(TranslationResultSelectors);
        return ScriptTemplate
            .Replace("__SOURCE_TEXT__", sourceJson, StringComparison.Ordinal)
            .Replace("__TARGET_LANGUAGE__", targetJson, StringComparison.Ordinal)
            .Replace("__SELECTORS__", selectorsJson, StringComparison.Ordinal);
    }

    internal static GoogleTranslateDomResult? Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        var result = JsonSerializer.Deserialize<GoogleTranslateDomResult>(json, JsonOptions);
        return result is null || string.IsNullOrWhiteSpace(result.Text) || string.IsNullOrWhiteSpace(result.Selector)
            ? null
            : result with { Text = result.Text.Trim() };
    }

    private const string ScriptTemplate = """
        (() => {
          const sourceText = __SOURCE_TEXT__;
          const targetLanguage = __TARGET_LANGUAGE__;
          const selectors = __SELECTORS__;
          const normalize = value => (value || '').replace(/\s+/gu, ' ').trim();
          const normalizedSource = normalize(sourceText);
          const isExcludedContainer = node => Boolean(node.closest(
            'header, nav, [role="button"], [role="menu"], [role="listbox"], [role="dialog"]'));
          const isRendered = node => {
            const style = window.getComputedStyle(node);
            return node.isConnected && style.display !== 'none' && style.visibility !== 'hidden';
          };
          const hasExpectedLanguage = node => {
            const languageNode = node.closest('[lang]');
            if (!languageNode) return true;
            const language = (languageNode.getAttribute('lang') || '').toLowerCase();
            return !language || language === targetLanguage || language.startsWith(targetLanguage + '-');
          };

          for (const selector of selectors) {
            const values = Array.from(document.querySelectorAll(selector))
              .filter(node => !isExcludedContainer(node) && isRendered(node) && hasExpectedLanguage(node))
              .map(node => (node.innerText || node.textContent || '').trim())
              .filter(Boolean);
            const text = Array.from(new Set(values)).join('\n').trim();
            const normalized = normalize(text);
            if (text && normalized && normalized !== normalizedSource && text.length <= 100000)
              return { text, selector };
          }
          return null;
        })()
        """;
}
