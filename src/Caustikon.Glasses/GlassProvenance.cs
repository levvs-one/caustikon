namespace Caustikon.Glasses;

/// <summary>Where a glass's numbers came from. Every catalogued glass carries one; a caller-defined glass supplies its own.</summary>
/// <param name="Source">The collection the entry was taken from, with its licence, for example the RefractiveIndex.INFO database at a named commit.</param>
/// <param name="Citation">The primary reference as the collection cites it, typically a manufacturer catalog release.</param>
/// <param name="Url">Address of the primary reference, or <see langword="null"/> when the source gives none.</param>
/// <param name="Path">Path of the entry inside the source collection, or <see langword="null"/> for caller-defined glass.</param>
/// <param name="RetrievedOn">The date the source was read.</param>
/// <param name="Notes">Transformations applied on import, such as squaring Sellmeier-1 resonances; empty when none.</param>
public sealed record GlassProvenance(
    string Source,
    string Citation,
    Uri? Url,
    string? Path,
    DateOnly RetrievedOn,
    string Notes = "")
{
    /// <summary>Provenance for a glass whose coefficients the caller supplied.</summary>
    /// <param name="description">Who measured or fitted the numbers and where they are published; recorded verbatim as the citation.</param>
    /// <param name="retrievedOn">When the caller took the numbers from that description's source.</param>
    public static GlassProvenance CallerSupplied(string description, DateOnly retrievedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new GlassProvenance("Supplied by the caller", description, null, null, retrievedOn);
    }
}
