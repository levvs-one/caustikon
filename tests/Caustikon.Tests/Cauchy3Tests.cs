using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Caustikon.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Fresh expected arrays keep each mutation test self-contained.")]
public sealed class Cauchy3Tests
{
    [TestMethod]
    public void EvaluateNanometersSupportsNegativeFittedCoefficients()
    {
        Cauchy3 cornea = new(1.362994d, 0.006009687d, -0.000676076d, 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, cornea.EvaluateNanometers(550d, out double index));
        Assert.AreEqual(1.37547242980671d, index, 1e-13d);
    }

    [TestMethod]
    public void EvaluateNanometersConstantModelHandlesSubnormalWavelength()
    {
        Cauchy3 model = new(1.5d, 0d, 0d, double.Epsilon, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(double.Epsilon, out double index));
        Assert.AreEqual(1.5d, index);
    }

    [TestMethod]
    public void EvaluateNanometersAvoidsOverflowInFourthPowerDenominator()
    {
        Cauchy3 model = new(1d, 0d, 1e308d, 1e80d, 2e80d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(1.3e80d, out double index));
        Assert.AreEqual(1.3501277966457756d, index, 2e-14d);
    }

    [TestMethod]
    public void BatchRejectsStatusesAliasingInputBytesBeforeWriting()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] wavelengths = [500d, 600d];
        double[] indices = [7d, 7d];

        Assert.ThrowsExactly<ArgumentException>(() =>
            model.EvaluateNanometers(wavelengths, indices,
                MemoryMarshal.Cast<double, DispersionStatus>(wavelengths.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { 500d, 600d }, wavelengths);
        CollectionAssert.AreEqual(new[] { 7d, 7d }, indices);
    }

    [TestMethod]
    public void BatchRejectsStatusesAliasingOutputBytesBeforeWriting()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] wavelengths = [500d, 600d];
        double[] indices = [7d, 7d];

        Assert.ThrowsExactly<ArgumentException>(() =>
            model.EvaluateNanometers(wavelengths, indices,
                MemoryMarshal.Cast<double, DispersionStatus>(indices.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { 7d, 7d }, indices);
    }

    [TestMethod]
    public void ConstructorStoresCoefficientsAndRange()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0.0001d, 400d, 700d);

        Assert.AreEqual(1.5d, model.A);
        Assert.AreEqual(0.004d, model.BUm2);
        Assert.AreEqual(0.0001d, model.CUm4);
        Assert.AreEqual(400d, model.MinimumWavelengthNanometers);
        Assert.AreEqual(700d, model.MaximumWavelengthNanometers);
    }

    [TestMethod]
    public void EvaluateNanometersConvertsNanometersToMicrometers()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0.0001d, 400d, 700d);

        DispersionStatus status = model.EvaluateNanometers(500d, out double index);

        Assert.AreEqual(DispersionStatus.Success, status);
        Assert.AreEqual(1.5176d, index, 1e-14d);
    }

    [TestMethod]
    public void EvaluateNanometersAcceptsInclusiveRangeEndpoints()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);

        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(400d, out double low));
        Assert.AreEqual(DispersionStatus.Success, model.EvaluateNanometers(700d, out double high));
        Assert.IsTrue(double.IsFinite(low));
        Assert.IsTrue(double.IsFinite(high));
    }

    [TestMethod]
    public void EvaluateNanometersDistinguishesInvalidAndOutsideRange()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);

        Assert.AreEqual(DispersionStatus.InvalidInput, model.EvaluateNanometers(0d, out double zero));
        Assert.AreEqual(DispersionStatus.InvalidInput, model.EvaluateNanometers(double.NaN, out double nan));
        Assert.AreEqual(DispersionStatus.OutsideModelRange, model.EvaluateNanometers(399d, out double below));
        Assert.AreEqual(DispersionStatus.OutsideModelRange, model.EvaluateNanometers(701d, out double above));
        Assert.IsTrue(double.IsNaN(zero));
        Assert.IsTrue(double.IsNaN(nan));
        Assert.IsTrue(double.IsNaN(below));
        Assert.IsTrue(double.IsNaN(above));
    }

    [TestMethod]
    public void EvaluateNanometersNonPositiveResultIsNonPhysical()
    {
        Cauchy3 model = new(-1d, 0d, 0d, 400d, 700d);

        DispersionStatus status = model.EvaluateNanometers(500d, out double index);

        Assert.AreEqual(DispersionStatus.NonPhysical, status);
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void DefaultModelIsInvalidRatherThanProducingAValue()
    {
        Cauchy3 model = default;

        DispersionStatus status = model.EvaluateNanometers(500d, out double index);

        Assert.AreEqual(DispersionStatus.InvalidInput, status);
        Assert.IsTrue(double.IsNaN(index));
    }

    [TestMethod]
    public void ConstructorRejectsInvalidCoefficientAndRangeWithExactNames()
    {
        ArgumentOutOfRangeException coefficient = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Cauchy3(1.5d, double.NaN, 0d, 400d, 700d));
        ArgumentOutOfRangeException minimum = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Cauchy3(1.5d, 0d, 0d, 0d, 700d));
        ArgumentOutOfRangeException maximum = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Cauchy3(1.5d, 0d, 0d, 700d, 400d));

        Assert.AreEqual("bUm2", coefficient.ParamName);
        Assert.AreEqual("minimumWavelengthNanometers", minimum.ParamName);
        Assert.AreEqual("maximumWavelengthNanometers", maximum.ParamName);
    }

    [TestMethod]
    public void BatchReportsPerLaneStatusesAndNanForFailures()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] wavelengths = [500d, 399d, 0d];
        double[] indices = new double[3];
        DispersionStatus[] statuses = new DispersionStatus[3];

        model.EvaluateNanometers(wavelengths, indices, statuses);

        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Success, DispersionStatus.OutsideModelRange, DispersionStatus.InvalidInput },
            statuses);
        Assert.AreEqual(1.516d, indices[0], 1e-14d);
        Assert.IsTrue(double.IsNaN(indices[1]));
        Assert.IsTrue(double.IsNaN(indices[2]));
    }

    [TestMethod]
    public void BatchAllowsExactInPlace()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] wavelengthsAndIndices = [400d, 500d, 700d];
        DispersionStatus[] statuses = new DispersionStatus[3];

        model.EvaluateNanometers(wavelengthsAndIndices, wavelengthsAndIndices, statuses);

        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Success, DispersionStatus.Success, DispersionStatus.Success },
            statuses);
        Assert.AreEqual(1.525d, wavelengthsAndIndices[0], 1e-14d);
        Assert.AreEqual(1.516d, wavelengthsAndIndices[1], 1e-14d);
        Assert.AreEqual(1.5081632653061225d, wavelengthsAndIndices[2], 1e-14d);
    }

    [TestMethod]
    public void BatchRejectsPartialOverlapBeforeWriting()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] storage = [400d, 7d, 7d, 7d];
        DispersionStatus[] statuses = [DispersionStatus.Singular, DispersionStatus.Singular, DispersionStatus.Singular];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            model.EvaluateNanometers(storage.AsSpan(0, 3), storage.AsSpan(1, 3), statuses));

        Assert.AreEqual("refractiveIndices", exception.ParamName);
        CollectionAssert.AreEqual(new[] { 400d, 7d, 7d, 7d }, storage);
        CollectionAssert.AreEqual(
            new[] { DispersionStatus.Singular, DispersionStatus.Singular, DispersionStatus.Singular },
            statuses);
    }

    [TestMethod]
    public void BatchShapeErrorOccursBeforeWriting()
    {
        Cauchy3 model = new(1.5d, 0.004d, 0d, 400d, 700d);
        double[] indices = [7d, 7d];
        DispersionStatus[] statuses = [DispersionStatus.Singular, DispersionStatus.Singular];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            model.EvaluateNanometers(new[] { 400d }, indices, statuses));

        Assert.AreEqual("refractiveIndices", exception.ParamName);
        CollectionAssert.AreEqual(new[] { 7d, 7d }, indices);
        CollectionAssert.AreEqual(new[] { DispersionStatus.Singular, DispersionStatus.Singular }, statuses);
    }
}
