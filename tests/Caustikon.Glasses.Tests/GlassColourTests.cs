namespace Caustikon.Glasses.Tests;

[TestClass]
public sealed class GlassColourTests
{
    [TestMethod]
    public void PerfectTransmitterReproducesD65White()
    {
        double[] ones = new double[471];
        Array.Fill(ones, 1d);

        TransmittedColour white = GlassColour.FromTransmittance(ones);

        // CIE D65 white under the 1931 2° observer: X 0.95047, Y 1, Z 1.08883.
        Assert.AreEqual(0.95047d, white.X, 5e-4d);
        Assert.AreEqual(1d, white.Y, 1e-12d);
        Assert.AreEqual(1.08883d, white.Z, 5e-4d);
        Assert.AreEqual((255, 255, 255), white.Rgb8);
        Assert.AreEqual("#ffffff", white.Hex);
        Assert.IsFalse(white.ClippedToGamut);
        Assert.AreEqual(360d, white.CoverageMinimumNanometers);
        Assert.AreEqual(830d, white.CoverageMaximumNanometers);
    }

    [TestMethod]
    public void OpaqueFilterIsBlack()
    {
        TransmittedColour black = GlassColour.FromTransmittance(new double[471]);

        Assert.AreEqual(0d, black.Y);
        Assert.AreEqual((0, 0, 0), black.Rgb8);
        Assert.AreEqual("#000000", black.Hex);
    }

    [TestMethod]
    public void ThinNBk7IsAlmostWhiteAndThickNBk7IsSlightlyDarker()
    {
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;

        TransmittedColour thin = GlassColour.Transmitted(glass, 10d)!.Value;
        TransmittedColour thick = GlassColour.Transmitted(glass, 250d)!.Value;

        Assert.IsTrue(thin.Y > 0.997d && thin.Y < 0.9995d, thin.Y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual((255, 255, 255), thin.Rgb8);
        Assert.IsTrue(thick.Y < thin.Y);
        Assert.IsTrue(thick.Y > 0.9d, thick.Y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsFalse(thin.ClippedToGamut);
        Assert.AreEqual(360d, thin.CoverageMinimumNanometers);
        Assert.AreEqual(830d, thin.CoverageMaximumNanometers);
    }

    [TestMethod]
    public void DenseFlintTransmitsLessBlueThanRed()
    {
        // Lead- and titanium-rich flints absorb toward the violet edge, so the transmitted tint leans warm.
        Glass flint = GlassCatalog.Find("schott", "SF57")!;

        TransmittedColour colour = GlassColour.Transmitted(flint, 25d)!.Value;
        TransmittedColour crown = GlassColour.Transmitted(GlassCatalog.Find("schott", "N-BK7")!, 25d)!.Value;

        Assert.IsTrue(colour.LinearRed > colour.LinearBlue, colour.Hex);
        Assert.IsTrue(colour.LinearRed - colour.LinearBlue > crown.LinearRed - crown.LinearBlue, colour.Hex);
        Assert.IsTrue(colour.Y < crown.Y);
    }

    [TestMethod]
    public void SurfaceLossesScaleTheWholeSpectrum()
    {
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;

        TransmittedColour bulk = GlassColour.Transmitted(glass, 10d)!.Value;
        TransmittedColour withSurfaces = GlassColour.Transmitted(glass, 10d, includeSurfaces: true)!.Value;

        // Two air–glass surfaces at n ≈ 1.52 each reflect about 4.2 %, so 0.958² ≈ 0.917 of the light is left.
        Assert.AreEqual(0.917d, withSurfaces.Y / bulk.Y, 0.004d);
    }

    [TestMethod]
    public void GlassWithoutExtinctionDataHasNoColour()
    {
        Sellmeier model = new(0d, [1.03961212d], [0.00600069867d], 400d, 700d);
        Glass glass = Glass.Define("no k data", in model, "test", new DateOnly(2026, 9, 5));

        Assert.IsNull(GlassColour.Transmitted(glass, 10d));
    }

    [TestMethod]
    public void ArgumentsAreValidated()
    {
        Glass glass = GlassCatalog.Find("schott", "N-BK7")!;

        Assert.ThrowsExactly<ArgumentNullException>(() => GlassColour.Transmitted(null!, 10d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlassColour.Transmitted(glass, -1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlassColour.Transmitted(glass, double.NaN));
        Assert.ThrowsExactly<ArgumentException>(() => GlassColour.FromTransmittance(new double[470]));
        double[] bad = new double[471];
        bad[10] = 1.5d;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlassColour.FromTransmittance(bad));
    }

    [TestMethod]
    public void IlluminantInterpolatesTheFiveNanometerTable()
    {
        Assert.AreEqual(100d, GlassColour.IlluminantAt(560d), 1e-12d);
        Assert.AreEqual(0d, GlassColour.IlluminantAt(299d));
        Assert.AreEqual(0d, GlassColour.IlluminantAt(781d));
        double mid = GlassColour.IlluminantAt(562.5d);
        double at560 = GlassColour.IlluminantAt(560d);
        double at565 = GlassColour.IlluminantAt(565d);
        Assert.AreEqual((at560 + at565) / 2d, mid, 1e-12d);
    }

    [TestMethod]
    public void CompandingMatchesTheSRgbCurve()
    {
        Assert.AreEqual(0d, TransmittedColour.Compand(0d));
        Assert.AreEqual(1d, TransmittedColour.Compand(1d), 1e-12d);
        Assert.AreEqual(12.92d * 0.002d, TransmittedColour.Compand(0.002d), 1e-15d);
        Assert.AreEqual(1.055d * Math.Pow(0.5d, 1d / 2.4d) - 0.055d, TransmittedColour.Compand(0.5d), 1e-15d);
    }
}
