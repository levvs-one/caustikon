using Microsoft.AspNetCore.Components;

namespace Caustikon.Site.Localization;

/// <summary>The site's current language and its strings. One instance for the application; components re-render through the cascaded code.</summary>
public sealed class Locale
{
    private string code = "en";

    public string Code
    {
        get => code;
        set
        {
            string next = value.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
            if (next == code)
            {
                return;
            }

            code = next;
            SiteInfo.UseLanguage(code);
            Changed?.Invoke();
        }
    }

    public bool IsRussian => code == "ru";

    public event Action? Changed;

    /// <summary>The string for <paramref name="key"/> in the current language; the key itself when missing, so a gap is visible rather than silent.</summary>
    public string this[string key] => Strings.Table.TryGetValue(key, out (string En, string Ru) pair) ? (IsRussian ? pair.Ru : pair.En) : key;

    /// <summary>A string that carries inline markup (sub, sup, links, code) — the site's own text, never user input.</summary>
    public MarkupString Html(string key) => new(this[key]);
}
