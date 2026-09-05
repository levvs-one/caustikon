namespace Caustikon.Tests;

[TestClass]
public sealed class SellmeierTests
{
    private static readonly double[] Bk7Strengths = [1.03961212d, 0.231792344d, 1.01046945d];
    private static readonly double[] Bk7ResonancesUm2 = [0.00600069867d, 0.0200179144d, 103.560653d];

    private static readonly Sellmeier Bk7 = new(0d, Bk7Strengths, Bk7ResonancesUm2, 300d, 2_500d);

    private static readonly Sellmeier3 Bk7Fixed = new(
        Bk7Strengths[0], Bk7ResonancesUm2[0],
        Bk7Strengths[1], Bk7ResonancesUm2[1],
        Bk7Strengths[2], Bk7ResonancesUm2[2],
        300d, 2_500d);

    [TestMethod]
    [DataRow(300d)]
    [DataRow(365d)]
    [DataRow(486.1327d)]
    [DataRow(587.5618d)]
    [DataRow(656.2725d)]
    [DataRow(2_325.4d)]
    [DataRow(2_500d)]
    public void GeneralFormReproducesSellmeier3BitForBit(double wavelength)
    {
        DispersionStatus fixedStatus = Bk7Fixed.EvaluateNanometers(wavelength, out double expected);
        DispersionStatus status = Bk7.EvaluateNanometers(wavelength, out double actual);

        Assert.AreEqual(DispersionStatus.Success, fixedStatus);
        Assert.AreEqual(fixedStatus, status);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [TestMethod]
    public void ConstructorStoresOffsetTermsAndRange()
    {
        Sellmeier model = new(0.25d, [1d, 2d], [0.01d, 0.02d], 400d, 700d);

        Assert.AreEqual(0.25d, model.Offset);
        Assert.AreEqual(2, model.TermCount);
        Assert.AreEqual(1d, model.StrengthAt(0));
        Assert.AreEqual(0.01d, model.ResonanceUm2At(0));
        Assert.AreEqual(2d, model.StrengthAt(1));
        Assert.AreEqual(0.02d, model.ResonanceUm2At(1));
        Assert.AreEqual(400d, model.MinimumWavelengthNanometers);
        Assert.AreEqual(700d, model.MaximumWavelengthNanometers);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.StrengthAt(2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.ResonanceUm2At(-1));
    }

    [TestMethod]
    public void OffsetWithoutTermsGivesConstantIndex()
    {
        Sellmeier model = new(0.5d, [], [], 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(550d, out double index));
        Assert.AreEqual(Math.Sqrt(1.5d), index);
    }

    [TestMethod]
    public void NegativeResonanceIsAcceptedAndHasNoPole()
    {
        // A negative C places no pole on the real axis; catalog fits for several CDGM glasses carry one.
        Sellmeier model = new(0d, [1.2d, 0.5d], [0.01d, -0.0004d], 365d, 2_000d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(587.5618d, out double index));
        double lambda2 = 0.5875618d * 0.5875618d;
        double expected = Math.Sqrt(1d + 1.2d * lambda2 / (lambda2 - 0.01d) + 0.5d * lambda2 / (lambda2 + 0.0004d));
        Assert.AreEqual(expected, index, 1e-15d);
    }

    [TestMethod]
    public void ActivePoleInsideIntervalIsRejected()
    {
        // sqrt(0.25) um = 500 nm lies inside 400-700 nm.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, [1d], [0.25d], 400d, 700d));
        // The same pole with zero strength is inactive.
        Sellmeier inactive = new(0d, [0d], [0.25d], 400d, 700d);
        Assert.AreEqual(0, inactive.TermCount == 1 ? 0 : 1);
    }

    [TestMethod]
    public void ConstructorRejectsShapeAndValueErrors()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Sellmeier(0d, [1d, 2d], [0.01d], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, new double[9], new double[9], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(double.NaN, [], [], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, [double.PositiveInfinity], [0.01d], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, [1d], [double.NaN], 400d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, [], [], 0d, 700d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sellmeier(0d, [], [], 700d, 400d));
    }

    [TestMethod]
    public void StatusesFollowTheSharedContract()
    {
        Assert.AreEqual(DispersionStatus.OutsideModelRange, Bk7.EvaluateNanometers(299.9d, out double below));
        Assert.IsTrue(double.IsNaN(below));
        Assert.AreEqual(DispersionStatus.OutsideModelRange, Bk7.EvaluateNanometers(2_500.1d, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, Bk7.EvaluateNanometers(double.NaN, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, Bk7.EvaluateNanometers(-1d, out _));
        Assert.AreEqual(DispersionStatus.InvalidInput, default(Sellmeier).EvaluateNanometers(500d, out double uninitialized));
        Assert.IsTrue(double.IsNaN(uninitialized));
    }

    [TestMethod]
    public void NegativeSquaredIndexIsNonPhysical()
    {
        Sellmeier model = new(-3d, [], [], 400d, 700d);

        Assert.AreEqual(DispersionStatus.NonPhysical, model.EvaluateNanometers(500d, out double index));
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void BatchMatchesScalarAndPermitsInPlaceResults()
    {
        double[] wavelengths = [365d, 486.1327d, 587.5618d, 656.2725d, 2_325.4d, 100d];
        double[] expected = new double[wavelengths.Length];
        DispersionStatus[] expectedStatuses = new DispersionStatus[wavelengths.Length];
        for (int i = 0; i < wavelengths.Length; i++)
        {
            expectedStatuses[i] = Bk7.EvaluateNanometers(wavelengths[i], out expected[i]);
        }

        double[] results = new double[wavelengths.Length];
        DispersionStatus[] statuses = new DispersionStatus[wavelengths.Length];
        Bk7.EvaluateNanometers(wavelengths, results, statuses);
        CollectionAssert.AreEqual(expectedStatuses, statuses);
        for (int i = 0; i < wavelengths.Length; i++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(results[i]));
        }

        double[] inPlace = (double[])wavelengths.Clone();
        Bk7.EvaluateNanometers(inPlace, inPlace, statuses);
        for (int i = 0; i < wavelengths.Length; i++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(inPlace[i]));
        }
    }

    [TestMethod]
    public void BatchRejectsBadBuffersBeforeWriting()
    {
        double[] wavelengths = [500d, 600d, 700d];
        double[] results = new double[3];
        DispersionStatus[] statuses = new DispersionStatus[3];

        Assert.ThrowsExactly<ArgumentException>(() => Bk7.EvaluateNanometers(wavelengths, results.AsSpan(0, 2), statuses));
        Assert.ThrowsExactly<ArgumentException>(() => Bk7.EvaluateNanometers(wavelengths, results, statuses.AsSpan(0, 2)));
        double[] shared = new double[4];
        Assert.ThrowsExactly<ArgumentException>(() => Bk7.EvaluateNanometers(shared.AsSpan(0, 3), shared.AsSpan(1, 3), statuses));
        Assert.AreEqual(0d, results[0]);
    }

    [TestMethod]
    public void EqualityCoversOffsetTermsAndInterval()
    {
        Sellmeier a = new(0d, Bk7Strengths, Bk7ResonancesUm2, 300d, 2_500d);
        Sellmeier b = new(0d, Bk7Strengths, Bk7ResonancesUm2, 300d, 2_500d);
        Sellmeier c = new(0d, Bk7Strengths, Bk7ResonancesUm2, 300d, 2_400d);
        Sellmeier d = new(0d, [Bk7Strengths[0], Bk7Strengths[1]], [Bk7ResonancesUm2[0], Bk7ResonancesUm2[1]], 300d, 2_500d);

        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsTrue(a != c);
        Assert.IsTrue(a != d);
        Assert.IsFalse(a.Equals(new object()));
    }

    [TestMethod]
    public void GenericConsumerDoesNotBox()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        double index = IndexAtD(in Bk7);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(1.5168d, index, 5e-7d);
        Assert.AreEqual(before, after);
    }

    private static double IndexAtD<TModel>(in TModel model)
        where TModel : struct, IDispersionModel
    {
        model.EvaluateNanometers(587.5618d, out double index);
        return index;
    }
}
