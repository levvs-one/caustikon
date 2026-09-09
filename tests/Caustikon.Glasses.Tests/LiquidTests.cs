using Caustikon.Glasses;

namespace Caustikon.Glasses.Tests;

/// <summary>The liquids come from material measurements rather than manufacturer datasheets; these pin what they must still do.</summary>
[TestClass]
public sealed class LiquidTests
{
    [TestMethod]
    public void WaterIsTheDaimonFitAtTwentyDegrees()
    {
        Glass water = GlassCatalog.Find("liquids", "Water")!;
        Assert.AreEqual("liquid", water.Category);
        // Daimon and Masumura 2007, formula 2 in the database, evaluated at the d line.
        Assert.AreEqual(1.33340, water.IndexD, 5e-5);
        Assert.IsTrue(water.AbbeD is > 55 and < 57, water.AbbeD.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsNotNull(water.CatalogIndexD);
        Assert.IsTrue(water.Provenance.Notes.Contains("evaluated from the fit", StringComparison.Ordinal));
        Assert.IsTrue(water.Provenance.Notes.Contains("Hale", StringComparison.Ordinal), "the k table is Hale and Querry 1973");
    }

    [TestMethod]
    public void WaterTurnsBlueOverMetres()
    {
        Glass water = GlassCatalog.Find("liquids", "Water")!;
        TabulatedExtinction k = water.Extinction!;
        // Hale and Querry: absorption at 700 nm is roughly two orders above 450 nm, which is why deep water is blue.
        k.InternalTransmittance(450, 10_000, out double blue);
        k.InternalTransmittance(700, 10_000, out double red);
        Assert.IsTrue(blue > 0.5, blue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsTrue(red < 0.05, red.ToString(System.Globalization.CultureInfo.InvariantCulture));

        TransmittedColour tenMetres = GlassColour.Transmitted(water, 10_000)!.Value;
        Assert.IsTrue(tenMetres.LinearBlue > tenMetres.LinearRed * 3, tenMetres.Hex);

        // A glass of water is colourless: a centimetre passes almost everything at every visible wavelength.
        TransmittedColour glassOfWater = GlassColour.Transmitted(water, 10)!.Value;
        Assert.IsTrue(glassOfWater.Y > 0.999, glassOfWater.Hex);
    }

    [TestMethod]
    public void EveryLiquidResolvesByNameAndCoversTheVisible()
    {
        string[] names = ["Water", "Ethanol", "Methanol", "Acetone", "Glycerol", "Ethylene glycol", "Benzene", "Toluene", "Carbon disulfide"];
        foreach (string name in names)
        {
            Assert.IsTrue(GlassCatalog.TryFind("liquids", name, out Glass? liquid), name);
            Assert.IsTrue(liquid!.Model.MinimumWavelengthNanometers <= 486.2, name + " misses the F line");
            Assert.IsTrue(liquid.Model.MaximumWavelengthNanometers >= 656.2, name + " misses the C line");
            Assert.IsTrue(liquid.IndexD is > 1.3 and < 1.7, name);
        }

        Assert.IsTrue(GlassCatalog.Find("liquids", "Carbon disulfide")!.IndexD > GlassCatalog.Find("liquids", "Water")!.IndexD);
    }
}
