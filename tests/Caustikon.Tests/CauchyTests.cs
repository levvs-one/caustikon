namespace Caustikon.Tests;

[TestClass]
public sealed class CauchyTests
{
    // Cargille acrylic double matching liquid, RefractiveIndex.INFO formula 5, 25 °C: n = a0 + a1 λ^-2 + a2 λ^-4.
    private static readonly Cauchy MatchingLiquid = new(1.473749d, [6.5503120e-3d, -1.2694420e-5d], [-2d, -4d], 310d, 1_550d);

    [TestMethod]
    [DataRow(310d)]
    [DataRow(486.1327d)]
    [DataRow(587.5618d)]
    [DataRow(1_550d)]
    public void GeneralFormReproducesCauchy3BitForBit(double wavelength)
    {
        Cauchy3 fixedForm = new(1.473749d, 6.5503120e-3d, -1.2694420e-5d, 310d, 1_550d);

        DispersionStatus fixedStatus = fixedForm.EvaluateNanometers(wavelength, out double expected);
        DispersionStatus status = MatchingLiquid.EvaluateNanometers(wavelength, out double actual);

        Assert.AreEqual(DispersionStatus.Success, fixedStatus);
        Assert.AreEqual(fixedStatus, status);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [TestMethod]
    public void NonIntegerExponentUsesPow()
    {
        Cauchy model = new(1d, [0.5d], [-1.5d], 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(500d, out double index));
        Assert.AreEqual(1d + 0.5d * Math.Pow(0.5d, -1.5d), index, 1e-15d);
    }

    [TestMethod]
    public void PositiveExponentMultipliesTheCoefficient()
    {
        Cauchy model = new(1.5d, [-0.01d], [2d], 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(600d, out double index));
        Assert.AreEqual(1.5d - 0.01d * 0.36d, index, 1e-15d);
    }

    [TestMethod]
    public void NonPositiveIndexIsNonPhysical()
    {
        Cauchy model = new(-1d, [], [], 400d, 700d);

        Assert.AreEqual(DispersionStatus.NonPhysical, model.EvaluateNanometers(500d, out double index));
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void ConstructorRejectsShapeAndValueErrors()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Cauchy(1d, [1d], [], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Cauchy(1d, new double[9], new double[9], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Cauchy(double.NaN, [], [], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Cauchy(1d, [1d], [double.NaN], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Cauchy(1d, [], [], 700d, 400d));
    }

    [TestMethod]
    public void StatusesFollowTheSharedContract()
    {
        Assert.AreEqual(DispersionStatus.OutsideModelRange, MatchingLiquid.EvaluateNanometers(300d, out double below));
        Assert.IsTrue(double.IsNaN(below));
        Assert.AreEqual(DispersionStatus.InvalidInput, MatchingLiquid.EvaluateNanometers(0d, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, default(Cauchy).EvaluateNanometers(500d, out _));
    }

    [TestMethod]
    public void BatchMatchesScalar()
    {
        double[] wavelengths = [310d, 500d, 1_550d, 5d];
        double[] results = new double[4];
        DispersionStatus[] statuses = new DispersionStatus[4];

        MatchingLiquid.EvaluateNanometers(wavelengths, results, statuses);

        for (int i = 0; i < wavelengths.Length; i++)
        {
            Assert.AreEqual(MatchingLiquid.EvaluateNanometers(wavelengths[i], out double expected), statuses[i]);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(results[i]));
        }
    }

    [TestMethod]
    public void EqualityCoversOffsetTermsAndInterval()
    {
        Cauchy a = new(1.473749d, [6.5503120e-3d, -1.2694420e-5d], [-2d, -4d], 310d, 1_550d);
        Cauchy b = new(1.473749d, [6.5503120e-3d, -1.2694420e-5d], [-2d, -6d], 310d, 1_550d);

        Assert.IsTrue(a == MatchingLiquid);
        Assert.AreEqual(a.GetHashCode(), MatchingLiquid.GetHashCode());
        Assert.IsTrue(a != b);
    }
}
