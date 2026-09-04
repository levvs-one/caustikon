using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Caustikon.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Fresh expected arrays keep each mutation test self-contained.")]
public sealed class Sellmeier3Tests
{
    private static readonly Sellmeier3 Bk7 = new(
        1.03961212d, 0.00600069867d,
        0.231792344d, 0.0200179144d,
        1.01046945d, 103.560653d,
        300d, 2_500d);

    [TestMethod]
    public void ConstructorStoresInterleavedCoefficientPairsAndRange()
    {
        Sellmeier3 model = new(1d, 0.01d, 2d, 0.02d, 3d, 0.03d, 400d, 700d);

        Assert.AreEqual(1d, model.B1);
        Assert.AreEqual(0.01d, model.C1Um2);
        Assert.AreEqual(2d, model.B2);
        Assert.AreEqual(0.02d, model.C2Um2);
        Assert.AreEqual(3d, model.B3);
        Assert.AreEqual(0.03d, model.C3Um2);
        Assert.AreEqual(400d, model.MinimumWavelengthNanometers);
        Assert.AreEqual(700d, model.MaximumWavelengthNanometers);
    }

    [TestMethod]
    public void EvaluateNanometersBk7FraunhoferDLineMatchesReferenceIndex()
    {
        DispersionStatus status = Bk7.EvaluateNanometers(587.5618d, out double index);

        Assert.AreEqual(DispersionStatus.Success, status);
        Assert.AreEqual(1.5168d, index, 5e-7d);
    }

    [TestMethod]
    [DataRow(400d, 700d)]
    [DataRow(500d, 700d)]
    [DataRow(400d, 500d)]
    [DataRow(500d, 500d)]
    public void ConstructorRejectsActiveResonanceInsideInclusiveRange(double minimum, double maximum)
    {
        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Sellmeier3(1d, 0.25d, 0d, 0d, 0d, 0d, minimum, maximum));

        Assert.AreEqual("c1Um2", exception.ParamName);
    }

    [TestMethod]
    public void ConstructorAllowsInactiveResonanceInsideRange()
    {
        Sellmeier3 model = new(0d, 0.25d, 1d, 0d, 0d, 0d, 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(500d, out double index));
        Assert.AreEqual(Math.Sqrt(2d), index, 1e-14d);
    }

    [TestMethod]
    public void EvaluateNanometersZeroResonanceIsConstantEvenAtSubnormalWavelength()
    {
        Sellmeier3 model = new(1d, 0d, 0d, 1d, 0d, 2d, double.Epsilon, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(double.Epsilon, out double index));
        Assert.AreEqual(Math.Sqrt(2d), index, 1e-14d);
    }

    [TestMethod]
    public void EvaluateNanometersVeryLongWavelengthApproachesFiniteLimit()
    {
        Sellmeier3 model = new(1d, 1d, 2d, 2d, 3d, 3d, 10_000d, double.MaxValue);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(double.MaxValue, out double index));
        Assert.AreEqual(Math.Sqrt(7d), index, 1e-14d);
    }

    [TestMethod]
    public void EvaluateNanometersAvoidsOverflowWithoutLosingFiniteResonanceContribution()
    {
        Sellmeier3 model = new(1d, 1e308d, 0d, 0d, 0d, 0d, 1.4e157d, 1.6e157d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(1.5e157d, out double index));
        Assert.AreEqual(1.6733200530681511d, index, 2e-14d);
    }

    [TestMethod]
    [DataRow(1d, double.Epsilon, 1e-159d)]
    [DataRow(1e308d, 2e-18d, 1e-160d)]
    [DataRow(1d, double.Epsilon, 2.23e-159d)]
    public void EvaluateNanometersUnderflowedOrSubnormalWavelengthSquareReturnsNonPhysical(
        double strength, double resonance, double wavelength)
    {
        Sellmeier3 model = new(strength, resonance, 0d, 0d, 0d, 0d, wavelength, wavelength * 1.1d);

        Assert.AreEqual(DispersionStatus.NonPhysical, model.EvaluateNanometers(wavelength, out double index));
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void ConstructorAllowsActiveResonanceOutsideRange()
    {
        Sellmeier3 model = new(1d, 0.25d, 0d, 0d, 0d, 0d, 600d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(600d, out double index));
        Assert.AreEqual(Math.Sqrt(1d + (0.36d / 0.11d)), index, 1e-14d);
    }

    [TestMethod]
    public void EvaluateNanometersDistinguishesNonPhysicalFromSingular()
    {
        Sellmeier3 model = new(-2d, 0d, 0d, 0.01d, 0d, 0.02d, 400d, 700d);

        DispersionStatus status = model.EvaluateNanometers(500d, out double index);

        Assert.AreEqual(DispersionStatus.NonPhysical, status);
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void EvaluateNanometersDistinguishesInvalidAndOutsideRange()
    {
        Assert.AreEqual(DispersionStatus.InvalidInput, Bk7.EvaluateNanometers(double.PositiveInfinity, out double invalid));
        Assert.AreEqual(DispersionStatus.OutsideModelRange, Bk7.EvaluateNanometers(299d, out double outside));
        Assert.IsTrue(double.IsNaN(invalid));
        Assert.IsTrue(double.IsNaN(outside));
    }

    [TestMethod]
    public void DefaultModelIsInvalidRatherThanProducingAValue()
    {
        Sellmeier3 model = default;

        DispersionStatus status = model.EvaluateNanometers(500d, out double index);

        Assert.AreEqual(DispersionStatus.InvalidInput, status);
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void ConstructorRejectsInvalidResonanceAndRangeWithExactNames()
    {
        ArgumentOutOfRangeException resonance = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Sellmeier3(1d, -0.1d, 0d, 0d, 0d, 0d, 400d, 700d));
        ArgumentOutOfRangeException coefficient = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Sellmeier3(double.NaN, 0.1d, 0d, 0d, 0d, 0d, 400d, 700d));
        ArgumentOutOfRangeException range = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Sellmeier3(1d, 0.1d, 0d, 0d, 0d, 0d, 700d, 400d));

        Assert.AreEqual("c1Um2", resonance.ParamName);
        Assert.AreEqual("b1", coefficient.ParamName);
        Assert.AreEqual("maximumWavelengthNanometers", range.ParamName);
    }

    [TestMethod]
    public void BatchReportsSuccessOutsideAndInvalidPerLane()
    {
        Sellmeier3 model = new(1d, 0.25d, 0d, 0d, 0d, 0d, 600d, 700d);
        double[] wavelengths = [600d, 500d, 701d, double.NaN];
        double[] indices = new double[4];
        DispersionStatus[] statuses = new DispersionStatus[4];

        model.EvaluateNanometers(wavelengths, indices, statuses);

        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Success, DispersionStatus.OutsideModelRange, DispersionStatus.OutsideModelRange, DispersionStatus.InvalidInput },
            statuses);
        Assert.IsTrue(double.IsFinite(indices[0]));
        Assert.IsTrue(double.IsNaN(indices[1]));
        Assert.IsTrue(double.IsNaN(indices[2]));
        Assert.IsTrue(double.IsNaN(indices[3]));
    }

    [TestMethod]
    [DataRow(365d, 1.53626962297d)]
    [DataRow(587.6d, 1.51679843791d)]
    [DataRow(2325.4d, 1.48921172544d)]
    public void EvaluateNanometersBk7MatchesReferenceAcrossSpectrum(double wavelength, double expected)
    {
        Assert.AreEqual(DispersionStatus.Success, Bk7.EvaluateNanometers(wavelength, out double index));
        Assert.AreEqual(expected, index, 5e-9d);
    }

    [TestMethod]
    public void BatchRejectsStatusesAliasingInputBytesBeforeWriting()
    {
        double[] wavelengths = [500d, 600d];
        double[] indices = [7d, 7d];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Bk7.EvaluateNanometers(wavelengths, indices,
                MemoryMarshal.Cast<double, DispersionStatus>(wavelengths.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { 500d, 600d }, wavelengths);
        CollectionAssert.AreEqual(new[] { 7d, 7d }, indices);
    }

    [TestMethod]
    public void BatchRejectsStatusesAliasingOutputBytesBeforeWriting()
    {
        double[] wavelengths = [500d, 600d];
        double[] indices = [7d, 7d];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Bk7.EvaluateNanometers(wavelengths, indices,
                MemoryMarshal.Cast<double, DispersionStatus>(indices.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { 7d, 7d }, indices);
    }

    [TestMethod]
    public void BatchAllowsExactInPlace()
    {
        double[] wavelengthsAndIndices = [486.1327d, 587.5618d, 656.2725d];
        double[] expected = new double[3];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(DispersionStatus.Success, Bk7.EvaluateNanometers(wavelengthsAndIndices[i], out expected[i]));
        }

        DispersionStatus[] statuses = new DispersionStatus[3];
        Bk7.EvaluateNanometers(wavelengthsAndIndices, wavelengthsAndIndices, statuses);

        CollectionAssert.AreEqual(expected, wavelengthsAndIndices);
        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Success, DispersionStatus.Success, DispersionStatus.Success },
            statuses);
    }

    [TestMethod]
    public void BatchRejectsPartialOverlapBeforeWriting()
    {
        double[] storage = [400d, 7d, 7d, 7d];
        DispersionStatus[] statuses = [DispersionStatus.Singular, DispersionStatus.Singular, DispersionStatus.Singular];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Bk7.EvaluateNanometers(storage.AsSpan(0, 3), storage.AsSpan(1, 3), statuses));

        Assert.AreEqual("refractiveIndices", exception.ParamName);
        CollectionAssert.AreEqual(new[] { 400d, 7d, 7d, 7d }, storage);
        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Singular, DispersionStatus.Singular, DispersionStatus.Singular },
            statuses);
    }
}
