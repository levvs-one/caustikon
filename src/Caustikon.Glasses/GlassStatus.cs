namespace Caustikon.Glasses;

/// <summary>The manufacturer's availability class for a glass, as printed in its catalog.</summary>
public enum GlassStatus
{
    /// <summary>The catalog states no class, or the entry is not a manufacturer glass.</summary>
    Unspecified,

    /// <summary>Kept in stock and recommended for new designs.</summary>
    Preferred,

    /// <summary>Regular production, not always in stock.</summary>
    Standard,

    /// <summary>Produced on request.</summary>
    Special,

    /// <summary>No longer produced; the data describes past melts.</summary>
    Obsolete,
}
