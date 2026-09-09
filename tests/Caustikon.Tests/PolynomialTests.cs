namespace Caustikon.Tests;

[TestClass]
public sealed class PolynomialTests
{
    // CDGM BAF2, Zemax catalog 2022-06 via RefractiveIndex.INFO (formula 3): n² = a0 + a1 λ² + a2 λ⁻² + a3 λ⁻⁴ + a4 λ⁻⁶ + a5 λ⁻⁸.
    // The catalog states nd = 1.569703 and Vd = 49.435926 independently of the fit.
    private static readonly Polynomial Baf2 = new(
        2.41667247d,
        [-0.00746725517d, 0.0168668464d, -1.29697272e-05d, 5.15992602e-05d, -1.81803614e-06d],
        [2d, -2d, -4d, -6d, -8d],
        365d, 1_014d);

    [TestMethod]
    public void Baf2ReproducesCatalogIndexAtTheDLine()
    {
        Assert.AreEqual(DispersionStatus.Success, Baf2.EvaluateNanometers(587.5618d, out double nd));
        Assert.AreEqual(1.569703d, nd, 1e-5d);
    }

    [TestMethod]
    public void Baf2ReproducesCatalogAbbeNumber()
    {
        Baf2.EvaluateNanometers(587.5618d, out double nd);
        Baf2.EvaluateNanometers(486.1327d, out double nF);
        Baf2.EvaluateNanometers(656.2725d, out double nC);

        Assert.AreEqual(49.435926d, (nd - 1d) / (nF - nC), 0.05d);
    }

    [TestMethod]
    public void EvenIntegerExponentsMatchExplicitArithmetic()
    {
        double lambda = 0.55d;
        double expected = Math.Sqrt(
            2.41667247d
            - 0.00746725517d * lambda * lambda
            + 0.0168668464d / (lambda * lambda)
            - 1.29697272e-05d / Math.Pow(lambda, 4)
            + 5.15992602e-05d / Math.Pow(lambda, 6)
            - 1.81803614e-06d / Math.Pow(lambda, 8));

        Assert.AreEqual(DispersionStatus.Success, Baf2.EvaluateNanometers(550d, out double index));
        Assert.AreEqual(expected, index, 1e-14d);
    }

    [TestMethod]
    public void ConstructorStoresOffsetTermsAndRange()
    {
        Assert.AreEqual(2.41667247d, Baf2.Offset);
        Assert.AreEqual(5, Baf2.TermCount);
        Assert.AreEqual(0.0168668464d, Baf2.CoefficientAt(1));
        Assert.AreEqual(-2d, Baf2.ExponentAt(1));
        Assert.AreEqual(365d, Baf2.MinimumWavelengthNanometers);
        Assert.AreEqual(1_014d, Baf2.MaximumWavelengthNanometers);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Baf2.CoefficientAt(5));
    }

    [TestMethod]
    public void NonPositiveSquaredIndexIsNonPhysical()
    {
        Polynomial model = new(-2d, [], [], 400d, 700d);

        Assert.AreEqual(DispersionStatus.NonPhysical, model.EvaluateNanometers(500d, out double index));
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void TinyWavelengthOverflowIsNonPhysicalNotInfinite()
    {
        Polynomial model = new(1d, [1d], [-8d], 1e-300d, 1d);

        Assert.AreEqual(DispersionStatus.NonPhysical, model.EvaluateNanometers(1e-300d, out double index));
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void ConstructorRejectsShapeAndValueErrors()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Polynomial(1d, [1d], [], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Polynomial(1d, new double[9], new double[9], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Polynomial(double.PositiveInfinity, [], [], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Polynomial(1d, [double.NaN], [2d], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Polynomial(1d, [], [], -1d, 700d));
    }

    [TestMethod]
    public void StatusesFollowTheSharedContract()
    {
        Assert.AreEqual(DispersionStatus.OutsideModelRange, Baf2.EvaluateNanometers(364.9d, out double below));
        Assert.IsTrue(double.IsNaN(below));
        Assert.AreEqual(DispersionStatus.OutsideModelRange, Baf2.EvaluateNanometers(1_014.1d, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, Baf2.EvaluateNanometers(double.NegativeInfinity, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, default(Polynomial).EvaluateNanometers(500d, out _));
    }

    [TestMethod]
    public void BatchMatchesScalarThroughTheGenericHelper()
    {
        double[] wavelengths = [365d, 486.1327d, 587.5618d, 656.2725d, 1_014d, 2_000d];
        double[] results = new double[wavelengths.Length];
        DispersionStatus[] statuses = new DispersionStatus[wavelengths.Length];

        Dispersion.EvaluateNanometers(in Baf2, wavelengths, results, statuses);

        for (int i = 0; i < wavelengths.Length; i++)
        {
            Assert.AreEqual(Baf2.EvaluateNanometers(wavelengths[i], out double expected), statuses[i]);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(results[i]));
        }
    }

    [TestMethod]
    public void EqualityCoversOffsetTermsAndInterval()
    {
        Polynomial same = new(
            2.41667247d,
            [-0.00746725517d, 0.0168668464d, -1.29697272e-05d, 5.15992602e-05d, -1.81803614e-06d],
            [2d, -2d, -4d, -6d, -8d],
            365d, 1_014d);
        Polynomial other = new(2.41667247d, [-0.00746725517d], [2d], 365d, 1_014d);

        Assert.IsTrue(same == Baf2);
        Assert.AreEqual(same.GetHashCode(), Baf2.GetHashCode());
        Assert.IsTrue(other != Baf2);
        Assert.IsFalse(Baf2.Equals(null));
    }
}
