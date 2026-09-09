using System.Globalization;

namespace Caustikon.Glasses.Tests;

[TestClass]
public sealed class GlassCatalogTests
{
    /// <summary>
    /// A manufacturer's dispersion fit reproduces its own printed indices to a few units in the fifth decimal; SCHOTT
    /// states 3e-6 in the visible for its Sellmeier fits, other catalogs are looser. Five units in the fifth decimal is
    /// the bound every imported glass meets once the printed value's own rounding is allowed for.
    /// </summary>
    private const double FitTolerance = 5e-5d;

    [TestMethod]
    public void CatalogIsLargeAndCoversTheMajorManufacturers()
    {
        Assert.IsTrue(GlassCatalog.All.Count > 1_500, $"Only {GlassCatalog.All.Count} glasses.");
        foreach (string vendor in new[] { "schott", "ohara", "hoya", "cdgm", "hikari", "sumita" })
        {
            Assert.IsTrue(GlassCatalog.Vendors.Contains(vendor), vendor);
            Assert.IsTrue(GlassCatalog.ByVendor(vendor).Count() > 30, vendor);
        }
    }

    [TestMethod]
    public void EveryGlassReproducesItsPrintedIndexAtTheDLine()
    {
        List<string> failures = [];
        int checkedCount = 0;
        foreach (Glass glass in GlassCatalog.All)
        {
            if (glass.CatalogIndexD is not { } printed)
            {
                continue;
            }

            checkedCount++;
            double tolerance = Math.Max(FitTolerance, 0.6d * Math.Pow(10d, -PrintedDecimals(printed)));
            double fitted = glass.IndexD;
            if (double.IsNaN(fitted) || Math.Abs(fitted - printed) > tolerance)
            {
                failures.Add($"{glass.Vendor}/{glass.Name}: fitted {fitted:F6}, printed {printed}, tolerance {tolerance:E1}");
            }
        }

        Assert.IsTrue(checkedCount > 1_400, $"Only {checkedCount} glasses print nd.");
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    [TestMethod]
    public void EveryGlassReproducesItsPrintedAbbeNumber()
    {
        // ν_d = (n_d − 1) / (n_F − n_C). With each index allowed FitTolerance, the first-order bound on ν_d is
        // ν_d · (FitTolerance / (n_d − 1) + 2·FitTolerance / (n_F − n_C)), plus the rounding of the printed value.
        List<string> failures = [];
        int checkedCount = 0;
        foreach (Glass glass in GlassCatalog.All)
        {
            if (glass.CatalogAbbeD is not { } printed || glass.CatalogIndexD is null)
            {
                continue;
            }

            checkedCount++;
            double nd = glass.IndexD;
            double nf = glass.IndexF;
            double nc = glass.IndexC;
            double fitted = glass.AbbeD;
            double propagated = printed * (FitTolerance / (nd - 1d) + 2d * FitTolerance / (nf - nc));
            double tolerance = propagated + 0.6d * Math.Pow(10d, -PrintedDecimals(printed));
            if (double.IsNaN(fitted) || Math.Abs(fitted - printed) > tolerance)
            {
                failures.Add($"{glass.Vendor}/{glass.Name}: fitted {fitted:F4}, printed {printed}, tolerance {tolerance:F4}");
            }
        }

        Assert.IsTrue(checkedCount > 1_400, $"Only {checkedCount} glasses print Vd.");
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    [TestMethod]
    public void EveryGlassCarriesCompleteProvenance()
    {
        foreach (Glass glass in GlassCatalog.All)
        {
            GlassProvenance provenance = glass.Provenance;
            Assert.IsFalse(string.IsNullOrWhiteSpace(provenance.Source), glass.Name);
            Assert.IsTrue(provenance.Source.Contains("CC0", StringComparison.Ordinal), glass.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(provenance.Citation), glass.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(provenance.Path), glass.Name);
            Assert.IsTrue(provenance.Path!.StartsWith(glass.Vendor == "liquids" ? "data/" : "data/specs/" + glass.Vendor + "/", StringComparison.Ordinal), provenance.Path);
            Assert.AreEqual(new DateOnly(2026, 9, 5), provenance.RetrievedOn, glass.Name);
            Assert.AreNotEqual(DispersionFormula.Unspecified, glass.Formula, glass.Name);
        }
    }

    [TestMethod]
    public void EveryGlassNameResolvesThroughFind()
    {
        foreach (Glass glass in GlassCatalog.All)
        {
            Glass? found = GlassCatalog.Find(glass.Vendor, glass.Name);
            Assert.IsNotNull(found, glass.Name);
            Assert.AreSame(glass, found);
        }
    }

    [TestMethod]
    public void FindIgnoresCaseSpacesAndHyphens()
    {
        Glass? a = GlassCatalog.Find("SCHOTT", "N-BK7");
        Glass? b = GlassCatalog.Find("schott", "n bk7");
        Glass? c = GlassCatalog.Find("Schott", "NBK7");

        Assert.IsNotNull(a);
        Assert.AreSame(a, b);
        Assert.AreSame(a, c);
        Assert.IsNull(GlassCatalog.Find("schott", "no such glass"));
        Assert.IsNull(GlassCatalog.Find("nobody", "N-BK7"));
        Assert.IsTrue(GlassCatalog.TryFind("ohara", "S-BSL7", out Glass sbsl7));
        Assert.AreEqual("S-BSL7", sbsl7.Name);
    }

    [TestMethod]
    public void TypedConstantAndCatalogEntryAreTheSameModel()
    {
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;

        Assert.AreEqual(Schott.NBK7, (Sellmeier)glass.Model);
        Assert.AreEqual(DispersionFormula.Sellmeier, glass.Formula);
        Assert.AreEqual(1.5168d, glass.CatalogIndexD);
        Assert.AreEqual(64.17d, glass.CatalogAbbeD);
        Assert.AreEqual(GlassStatus.Standard, glass.Status);
        Assert.AreEqual("517642.251", glass.GlassCode);
        Assert.AreEqual(2_510d, glass.DensityKgPerM3);
        Assert.AreEqual(293d, glass.ReferenceTemperatureKelvin);
        Assert.AreEqual(1.5168d, Schott.NBK7.EvaluateNanometers(FraunhoferLines.DNanometers, out double nd) == DispersionStatus.Success ? nd : double.NaN, 5e-7d);
    }

    [TestMethod]
    [DataRow(350d, 0.967d, 0.92d)]
    [DataRow(400d, 0.997d, 0.992d)]
    [DataRow(460d, 0.997d, 0.993d)]
    [DataRow(546d, 0.998d, 0.996d)]
    [DataRow(700d, 0.998d, 0.996d)]
    [DataRow(1_060d, 0.999d, 0.997d)]
    [DataRow(2_325d, 0.79d, 0.56d)]
    public void NBk7InternalTransmittanceMatchesTheDatasheet(double wavelength, double printed10mm, double printed25mm)
    {
        // SCHOTT N-BK7 datasheet (as of 01-Dec-2023), "Internal Transmittance τi" columns for 10 mm and 25 mm.
        // Each printed value is allowed half a unit in its last printed digit. The 300 nm row is not used: its 25 mm
        // value is printed as 0.05, one significant figure, which leaves the absorption coefficient uncertain by 10 %
        // and makes the two printed columns disagree with each other by more than that allowance.
        TabulatedExtinction extinction = GlassCatalog.Find("schott", "N-BK7")!.Extinction!;

        Assert.AreEqual(DispersionStatus.Success, extinction.InternalTransmittance(wavelength, 10d, out double t10));
        Assert.AreEqual(printed10mm, t10, 0.6d * Math.Pow(10d, -PrintedDecimals(printed10mm)));
        Assert.AreEqual(DispersionStatus.Success, extinction.InternalTransmittance(wavelength, 25d, out double t25));
        Assert.AreEqual(printed25mm, t25, 0.6d * Math.Pow(10d, -PrintedDecimals(printed25mm)));
    }

    [TestMethod]
    public void InternalTransmittanceHandlesZeroPathAndOutOfRange()
    {
        TabulatedExtinction extinction = GlassCatalog.Find("schott", "N-BK7")!.Extinction!;

        Assert.AreEqual(DispersionStatus.Success, extinction.InternalTransmittance(546.1d, 0d, out double none));
        Assert.AreEqual(1d, none);
        Assert.AreEqual(DispersionStatus.OutsideModelRange, extinction.InternalTransmittance(200d, 10d, out double outside));
        Assert.IsTrue(double.IsNaN(outside));
    }

    [TestMethod]
    public void ExtinctionInterpolatesGeometricallyBetweenPositiveSamples()
    {
        TabulatedExtinction table = new([400d, 500d], [1e-8d, 1e-6d]);

        Assert.AreEqual(DispersionStatus.Success, table.Evaluate(450d, out double mid));
        Assert.AreEqual(1e-7d, mid, 1e-15d);
        Assert.AreEqual(DispersionStatus.Success, table.Evaluate(400d, out double first));
        Assert.AreEqual(1e-8d, first);
        Assert.ThrowsExactly<ArgumentException>(() => new TabulatedExtinction([500d, 400d], [1d, 1d]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TabulatedExtinction([400d, 500d], [-1d, 1d]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.InternalTransmittance(450d, -1d, out _));
    }

    [TestMethod]
    public void ThermalDispersionEvaluatesTheSchottFormula()
    {
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;
        ThermalDispersion thermal = glass.Thermal!.Value;
        glass.IndexAt(FraunhoferLines.ENanometers, out double ne);

        // Hand evaluation of Δn_abs = (n² − 1)/(2n) · [D0 ΔT + D1 ΔT² + D2 ΔT³ + (E0 ΔT + E1 ΔT²)/(λ² − λTK²)] at +20 K.
        double lambda2 = 0.5460740d * 0.5460740d;
        double bracket = 1.86e-6d * 20d + 1.31e-8d * 400d + -1.37e-11d * 8_000d + (4.34e-7d * 20d + 6.27e-10d * 400d) / (lambda2 - 0.17d * 0.17d);
        double expected = (ne * ne - 1d) / (2d * ne) * bracket;

        Assert.AreEqual(expected, thermal.AbsoluteIndexShift(ne, FraunhoferLines.ENanometers, 40d), 1e-12d);
        Assert.AreEqual(0d, thermal.AbsoluteIndexShift(ne, FraunhoferLines.ENanometers, 20d));
        Assert.IsTrue(thermal.AbsoluteSlopeAtReference(ne, FraunhoferLines.ENanometers) > 0d);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => thermal.AbsoluteIndexShift(0d, 546d, 30d));
    }

    [TestMethod]
    [DataRow(1_060d, -40d, -20d, 0.3d)]
    [DataRow(546.074d, -40d, -20d, 0.8d)]
    [DataRow(435.8343d, -40d, -20d, 1.2d)]
    [DataRow(1_060d, 20d, 40d, 1.1d)]
    [DataRow(546.074d, 20d, 40d, 1.6d)]
    [DataRow(435.8343d, 20d, 40d, 2.1d)]
    [DataRow(1_060d, 60d, 80d, 1.5d)]
    [DataRow(546.074d, 60d, 80d, 2.1d)]
    [DataRow(435.8343d, 60d, 80d, 2.7d)]
    public void NBk7ThermalCoefficientsMatchTheDatasheet(double wavelength, double fromCelsius, double toCelsius, double printedPerMillionKelvin)
    {
        // SCHOTT N-BK7 datasheet (as of 01-Dec-2023), "Temperature Coefficients of the Refractive Index", Δn_abs/ΔT in
        // 10⁻⁶/K for the ranges −40/−20, +20/+40 and +60/+80 °C at 1060.0 nm, the e line and the g line, printed to one decimal.
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;
        ThermalDispersion thermal = glass.Thermal!.Value;
        Assert.AreEqual(DispersionStatus.Success, glass.IndexAt(wavelength, out double index));

        double shift = thermal.AbsoluteIndexShift(index, wavelength, toCelsius) - thermal.AbsoluteIndexShift(index, wavelength, fromCelsius);
        double perMillionKelvin = shift / (toCelsius - fromCelsius) * 1e6d;

        Assert.AreEqual(printedPerMillionKelvin, perMillionKelvin, 0.06d);
    }

    [TestMethod]
    public void CallerDefinedGlassHasTheSameStandingAsACatalogEntry()
    {
        Sellmeier model = new(0d, [1.03961212d, 0.231792344d, 1.01046945d], [0.00600069867d, 0.0200179144d, 103.560653d], 300d, 2_500d);

        Glass glass = Glass.Define("my BK7 melt", in model, "Coefficients from the SCHOTT N-BK7 datasheet, 2017", new DateOnly(2026, 9, 5), DispersionFormula.Sellmeier);

        Assert.AreEqual("custom", glass.Vendor);
        Assert.AreEqual("Supplied by the caller", glass.Provenance.Source);
        Assert.AreEqual("Coefficients from the SCHOTT N-BK7 datasheet, 2017", glass.Provenance.Citation);
        Assert.IsNull(glass.Provenance.Path);
        Assert.AreEqual(GlassCatalog.Find("schott", "N-BK7")!.IndexD, glass.IndexD, 1e-12d);
        Assert.AreEqual(64.17d, glass.AbbeD, 0.01d);
        Assert.ThrowsExactly<ArgumentException>(() => Glass.Define(" ", in model, "x", new DateOnly(2026, 9, 5)));
        Assert.ThrowsExactly<ArgumentException>(() => Glass.Define("x", in model, " ", new DateOnly(2026, 9, 5)));
    }

    [TestMethod]
    public void GlassesOutsideTheVisibleReportNaNForTheDLine()
    {
        Glass irg = GlassCatalog.Find("schott", "IRG24")!;

        Assert.IsTrue(double.IsNaN(irg.IndexD));
        Assert.IsTrue(double.IsNaN(irg.AbbeD));
        Assert.AreEqual(DispersionStatus.Success, irg.IndexAt(3_000d, out double n3um));
        Assert.IsTrue(n3um > 2d);
    }

    private static int PrintedDecimals(double value)
    {
        string text = value.ToString("G15", CultureInfo.InvariantCulture);
        int point = text.IndexOf('.', StringComparison.Ordinal);
        return point < 0 ? 0 : text.Length - point - 1;
    }
}
