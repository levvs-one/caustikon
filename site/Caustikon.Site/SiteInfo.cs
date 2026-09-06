using System.Globalization;
using System.Reflection;
using Caustikon.Glasses;

namespace Caustikon.Site;

/// <summary>Numbers the pages quote, taken from the packages themselves rather than typed into the pages.</summary>
public static class SiteInfo
{
    public static string Version { get; } =
        typeof(Dielectric).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(Dielectric).Assembly.GetName().Version?.ToString(3)
        ?? "";

    public static int GlassCount => GlassCatalog.All.Count;

    public static int VendorCount => GlassCatalog.Vendors.Count;

    private static readonly NumberFormatInfo Russian = Build(",", " ");

    /// <summary>How numbers are written for the reader: a comma decimal in Russian, a point otherwise. Code samples keep the point.</summary>
    public static NumberFormatInfo Format { get; private set; } = NumberFormatInfo.InvariantInfo;

    public static void UseLanguage(string code) => Format = code == "ru" ? Russian : NumberFormatInfo.InvariantInfo;

    private static NumberFormatInfo Build(string decimalSeparator, string groupSeparator)
    {
        NumberFormatInfo info = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        info.NumberDecimalSeparator = decimalSeparator;
        info.NumberGroupSeparator = groupSeparator;
        info.PercentDecimalSeparator = decimalSeparator;
        return info;
    }

    public static string Number(double value, int decimals) =>
        double.IsNaN(value) ? "—" : value.ToString("F" + decimals, Format);

    public static string Number(double value, string pattern) =>
        double.IsNaN(value) ? "—" : value.ToString(pattern, Format);

    public static string Compact(double value) =>
        double.IsNaN(value) ? "—" : value.ToString("0.###", Format);
}
