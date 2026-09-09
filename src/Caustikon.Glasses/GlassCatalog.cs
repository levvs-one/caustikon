namespace Caustikon.Glasses;

/// <summary>Lookup over every catalogued glass by manufacturer and name.</summary>
/// <remarks>
/// The first access builds the full list, which allocates one <see cref="Glass"/> per entry and boxes each model once;
/// after that lookups allocate nothing. Names are matched ignoring case, spaces, hyphens and underscores, so
/// <c>Find("schott", "n bk7")</c> and <c>Find("SCHOTT", "N-BK7")</c> resolve the same glass. Code on a hot path should
/// take the concrete model from the vendor class instead of resolving through here.
/// </remarks>
public static partial class GlassCatalog
{
    private static readonly Lazy<Glass[]> AllGlasses = new(BuildAll, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Dictionary<string, Glass>> Index = new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every catalogued glass, in manufacturer order then catalog order.</summary>
    public static IReadOnlyList<Glass> All => AllGlasses.Value;

    /// <summary>Manufacturer keys accepted by <see cref="Find"/>, lower case.</summary>
    public static IReadOnlyList<string> Vendors => VendorKeys;

    /// <summary>Finds a glass by manufacturer key and catalog name, or returns <see langword="null"/>.</summary>
    /// <param name="vendor">Manufacturer key such as <c>schott</c>; case-insensitive.</param>
    /// <param name="name">Catalog name such as <c>N-BK7</c>; case, spaces, hyphens and underscores are ignored.</param>
    public static Glass? Find(string vendor, string name)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(name);
        return Index.Value.TryGetValue(Key(vendor, name), out Glass? glass) ? glass : null;
    }

    /// <summary>Finds a glass by manufacturer key and catalog name.</summary>
    /// <returns><see langword="true"/> when found.</returns>
    public static bool TryFind(string vendor, string name, out Glass glass)
    {
        Glass? found = Find(vendor, name);
        glass = found!;
        return found is not null;
    }

    /// <summary>Every glass of one manufacturer, in catalog order; empty for an unknown key.</summary>
    /// <param name="vendor">Manufacturer key such as <c>schott</c>; case-insensitive.</param>
    public static IEnumerable<Glass> ByVendor(string vendor)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        string key = vendor.ToLowerInvariant();
        return All.Where(glass => glass.Vendor == key);
    }

    /// <summary>Normalizes a name the way <see cref="Find"/> does: lower case with spaces, hyphens and underscores removed.</summary>
    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        int length = 0;
        foreach (char c in name)
        {
            if (c is ' ' or '-' or '_')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    private static string Key(string vendor, string name) => vendor.ToLowerInvariant() + "/" + NormalizeName(name);

    private static Glass[] BuildAll()
    {
        List<Glass> glasses = [];
        foreach (Func<IEnumerable<Glass>> source in Sources)
        {
            glasses.AddRange(source());
        }

        return [.. glasses];
    }

    private static Dictionary<string, Glass> BuildIndex()
    {
        Dictionary<string, Glass> index = new(StringComparer.Ordinal);
        foreach (Glass glass in All)
        {
            index[Key(glass.Vendor, glass.Name)] = glass;
        }

        return index;
    }
}
