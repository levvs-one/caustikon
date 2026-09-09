namespace Caustikon.Glasses;

/// <summary>A named optical material: its dispersion model, what the catalog says about it, and where every number came from.</summary>
/// <remarks>
/// Catalogued and caller-defined glasses are the same type; only <see cref="Provenance"/> differs. The model is held
/// behind <see cref="IDispersionModel"/>, so resolving a glass costs one boxed value; code that traces many rays should take
/// the concrete struct from the vendor class instead, for example <c>Schott.NBK7</c>, and stay generic over it.
/// </remarks>
public sealed record Glass
{
    /// <summary>Manufacturer key as used by <see cref="GlassCatalog.Find"/>, lower case, for example <c>schott</c>.</summary>
    public required string Vendor { get; init; }

    /// <summary>Manufacturer name as printed, for example <c>SCHOTT</c>.</summary>
    public required string VendorDisplayName { get; init; }

    /// <summary>Catalog name of the glass as printed, for example <c>N-BK7</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The section of the manufacturer catalog the entry belongs to: <c>optical</c>, <c>infrared</c>, <c>obsolete</c>, and so on.</summary>
    public required string Category { get; init; }

    /// <summary>The refractive-index model, relative to air at <see cref="ReferenceTemperatureKelvin"/> unless the citation says otherwise.</summary>
    public required IDispersionModel Model { get; init; }

    /// <summary>The algebraic form of <see cref="Model"/>.</summary>
    public required DispersionFormula Formula { get; init; }

    /// <summary>Where the numbers came from.</summary>
    public required GlassProvenance Provenance { get; init; }

    /// <summary>Availability class from the catalog.</summary>
    public GlassStatus Status { get; init; }

    /// <summary>The index at the helium d line (587.5618 nm) as the manufacturer prints it, independent of the fit; <see langword="null"/> when not published.</summary>
    public double? CatalogIndexD { get; init; }

    /// <summary>The Abbe number <c>ν_d</c> as the manufacturer prints it; <see langword="null"/> when not published.</summary>
    public double? CatalogAbbeD { get; init; }

    /// <summary>The six-digit or nine-digit glass code, when the catalog assigns one.</summary>
    public string? GlassCode { get; init; }

    /// <summary>Density in kilograms per cubic meter, when published.</summary>
    public double? DensityKgPerM3 { get; init; }

    /// <summary>Temperature the catalog data refers to, in kelvin, when stated.</summary>
    public double? ReferenceTemperatureKelvin { get; init; }

    /// <summary>Relative partial dispersion deviation <c>ΔP_g,F</c>, when published.</summary>
    public double? PartialDispersionDeviationGF { get; init; }

    /// <summary>Temperature coefficients of the absolute index, when published.</summary>
    public ThermalDispersion? Thermal { get; init; }

    /// <summary>Tabulated extinction coefficient, from which absorption and internal transmittance follow; <see langword="null"/> when not published.</summary>
    public TabulatedExtinction? Extinction { get; init; }

    /// <summary>Evaluates the refractive index at a wavelength through <see cref="Model"/>.</summary>
    public DispersionStatus IndexAt(double wavelengthNanometers, out double refractiveIndex) =>
        Model.EvaluateNanometers(wavelengthNanometers, out refractiveIndex);

    /// <summary>The fitted index at the helium d line, 587.5618 nm, or <see cref="double.NaN"/> when the model does not cover it.</summary>
    public double IndexD => IndexOrNaN(FraunhoferLines.DNanometers);

    /// <summary>The fitted index at the hydrogen F line, 486.1327 nm, or <see cref="double.NaN"/> when the model does not cover it.</summary>
    public double IndexF => IndexOrNaN(FraunhoferLines.FNanometers);

    /// <summary>The fitted index at the hydrogen C line, 656.2725 nm, or <see cref="double.NaN"/> when the model does not cover it.</summary>
    public double IndexC => IndexOrNaN(FraunhoferLines.CNanometers);

    /// <summary>The Abbe number <c>ν_d = (n_d − 1) / (n_F − n_C)</c> from the fitted model, or <see cref="double.NaN"/> when any line is outside the model.</summary>
    public double AbbeD
    {
        get
        {
            double nd = IndexD;
            double nf = IndexF;
            double nc = IndexC;
            return (nd - 1d) / (nf - nc);
        }
    }

    /// <summary>Defines a glass from a caller's own model and description, with the same standing as a catalogued one.</summary>
    /// <typeparam name="TModel">The model's value type.</typeparam>
    /// <param name="name">A name for the glass; printed, never parsed.</param>
    /// <param name="model">The dispersion model; boxed once here.</param>
    /// <param name="provenance">Who measured or fitted the numbers and where they are published.</param>
    /// <param name="retrievedOn">When the caller took the numbers from that source.</param>
    /// <param name="formula">The algebraic form, when the caller wants it recorded.</param>
    public static Glass Define<TModel>(
        string name,
        in TModel model,
        string provenance,
        DateOnly retrievedOn,
        DispersionFormula formula = DispersionFormula.Unspecified)
        where TModel : struct, IDispersionModel
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Glass
        {
            Vendor = "custom",
            VendorDisplayName = "Caller-defined",
            Name = name,
            Category = "custom",
            Model = model,
            Formula = formula,
            Provenance = GlassProvenance.CallerSupplied(provenance, retrievedOn),
        };
    }

    private double IndexOrNaN(double wavelengthNanometers) =>
        Model.EvaluateNanometers(wavelengthNanometers, out double index) == DispersionStatus.Success ? index : double.NaN;
}
