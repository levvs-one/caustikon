using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Caustikon.Site.Localization;

/// <summary>Base for pages and components that show text: injects the locale and re-renders when the cascaded language code changes.</summary>
public abstract class LocalizedComponent : ComponentBase
{
    [Inject] public Locale L { get; set; } = default!;

    [CascadingParameter(Name = "Lang")] public string Lang { get; set; } = "en";

    /// <summary>The string for <paramref name="key"/> with <paramref name="args"/> substituted.</summary>
    protected string T(string key, params object[] args) => args.Length == 0 ? L[key] : string.Format(CultureInfo.InvariantCulture, L[key], args);

    /// <summary>The string for <paramref name="key"/> with <paramref name="args"/> substituted, rendered as the site's own markup.</summary>
    protected MarkupString H(string key, params object[] args) => new(T(key, args));
}
