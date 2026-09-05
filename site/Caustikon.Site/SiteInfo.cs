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

    public static string Number(double value, int decimals) =>
        double.IsNaN(value) ? "—" : value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string Compact(double value) =>
        double.IsNaN(value) ? "—" : value.ToString("0.###", CultureInfo.InvariantCulture);
}
