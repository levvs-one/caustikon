using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Caustikon.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Fresh expected arrays keep each mutation test self-contained.")]
public sealed class DielectricFresnelTests
{
    [TestMethod]
    public void FresnelPowerAlwaysComputesMeanFromCurrentPolarizations()
    {
        FresnelPower power = new(0.2f, 0.8f);

        Assert.AreEqual(0.5f, power.Unpolarized);
        Assert.AreEqual(0.4f, (power with { S = 0f }).Unpolarized);
    }

    [TestMethod]
    public void FresnelLargeFiniteIndicesPreserveScaleInvariance()
    {
        float[] cosines = [0f, 0.2f, 0.6f, 1f];

        foreach (float cosine in cosines)
        {
            FresnelPower expected = Dielectric.Fresnel(cosine, 1f, 2f);
            FresnelPower actual = Dielectric.Fresnel(cosine, float.MaxValue / 2f, float.MaxValue);

            Assert.AreEqual(expected.S, actual.S, 2e-6f);
            Assert.AreEqual(expected.P, actual.P, 2e-6f);
        }

        Assert.AreEqual(1f / 9f,
            Dielectric.NormalReflectance(float.MaxValue / 2f, float.MaxValue), 1e-7f);
    }

    [TestMethod]
    public void FresnelNearlyMatchedLowerToHigherIndicesDoNotSnapToCriticalReflection()
    {
        FresnelPower power = Dielectric.Fresnel(0.0005f, 1f, MathF.BitIncrement(1f));

        Assert.AreEqual(0.02751645748f, power.S, 2e-7f);
        Assert.AreEqual(0.02751641902f, power.P, 2e-7f);
    }

    [TestMethod]
    public void FresnelExtremeFiniteRatioReturnsFinitePowersAtNormalIncidence()
    {
        FresnelPower first = Dielectric.Fresnel(1f, float.MaxValue, float.Epsilon);
        FresnelPower second = Dielectric.Fresnel(1f, float.Epsilon, float.MaxValue);

        Assert.AreEqual(new FresnelPower(1f, 1f), first);
        Assert.AreEqual(new FresnelPower(1f, 1f), second);
        Assert.AreEqual(1f, Dielectric.NormalReflectance(float.MaxValue, float.Epsilon));
        Assert.AreEqual(1f, Dielectric.NormalReflectance(float.Epsilon, float.MaxValue));
    }

    [TestMethod]
    public void FresnelAirToGlassThirtyDegreesMatchesReferencePowers()
    {
        FresnelPower power = Dielectric.Fresnel(MathF.Sqrt(0.75f), 1f, 1.5f);

        Assert.AreEqual(0.0577961f, power.S, 2e-7f);
        Assert.AreEqual(0.02524915f, power.P, 2e-7f);
        Assert.AreEqual(0.04152263f, power.Unpolarized, 2e-7f);
    }

    [TestMethod]
    public void FresnelGlassToAirNearCriticalAngleDoesNotUseSchlickApproximation()
    {
        float cosine = MathF.Cos(40f * MathF.PI / 180f);

        Assert.AreEqual(0.2452912f, Dielectric.Fresnel(cosine, 1.5f, 1f).Unpolarized, 2e-6f);
        Assert.AreEqual(0.04067288f, Dielectric.Schlick(cosine, 1.5f, 1f), 2e-7f);
    }

    [TestMethod]
    public void FresnelBatchRejectsCosinesAliasingPowerBytesBeforeWriting()
    {
        float[] storage = [1f, 0.5f, 7f, 7f];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.Fresnel(storage.AsSpan(0, 2), 1f, 1.5f,
                MemoryMarshal.Cast<float, FresnelPower>(storage.AsSpan())));

        CollectionAssert.AreEqual(new[] { 1f, 0.5f, 7f, 7f }, storage);
    }

    [TestMethod]
    public void FresnelBatchRejectsIndicesAliasingPowerBytesBeforeWriting()
    {
        float[] storage = [1f, 1f, 7f, 7f];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.Fresnel(new[] { 1f, 0.5f }, storage.AsSpan(0, 2), new[] { 1.5f, 1.5f },
                MemoryMarshal.Cast<float, FresnelPower>(storage.AsSpan())));

        CollectionAssert.AreEqual(new[] { 1f, 1f, 7f, 7f }, storage);
    }

    [TestMethod]
    public void FresnelNormalIncidenceMatchesFourPercentGlassReflectance()
    {
        FresnelPower power = Dielectric.Fresnel(1f, 1f, 1.5f);

        Assert.AreEqual(0.04f, power.S, 1e-7f);
        Assert.AreEqual(0.04f, power.P, 1e-7f);
        Assert.AreEqual(0.04f, power.Unpolarized, 1e-7f);
        Assert.AreEqual(Dielectric.NormalReflectance(1f, 1.5f), power.Unpolarized);
    }

    [TestMethod]
    public void FresnelAtBrewsterAnglePPolarizationVanishes()
    {
        float cosBrewster = 1f / MathF.Sqrt(1f + (1.5f * 1.5f));

        FresnelPower power = Dielectric.Fresnel(cosBrewster, 1f, 1.5f);

        Assert.AreEqual(0f, power.P, 2e-13f);
        Assert.AreEqual(0.14792899f, power.S, 2e-6f);
        Assert.AreEqual(0.07396449f, power.Unpolarized, 2e-6f);
    }

    [TestMethod]
    public void FresnelTotalInternalReflectionReturnsUnity()
    {
        float cosFiftyDegrees = MathF.Cos(50f * MathF.PI / 180f);

        FresnelPower power = Dielectric.Fresnel(cosFiftyDegrees, 1.5f, 1f);

        Assert.AreEqual(new FresnelPower(1f, 1f), power);
    }

    [TestMethod]
    public void FresnelCriticalBoundaryReturnsUnity()
    {
        float criticalCosine = MathF.Sqrt(1f - ((1f / 1.5f) * (1f / 1.5f)));

        FresnelPower power = Dielectric.Fresnel(criticalCosine, 1.5f, 1f);

        Assert.AreEqual(new FresnelPower(1f, 1f), power);
    }

    [TestMethod]
    public void FresnelEqualIndicesReturnsZeroIncludingGrazingIncidence()
    {
        Assert.AreEqual(default(FresnelPower), Dielectric.Fresnel(0f, 1.33f, 1.33f));
        Assert.AreEqual(default(FresnelPower), Dielectric.Fresnel(0.42f, 1.33f, 1.33f));
        Assert.AreEqual(default(FresnelPower), Dielectric.Fresnel(1f, 1.33f, 1.33f));
    }

    [TestMethod]
    public void FresnelUnpolarizedIsMeanOfPolarizations()
    {
        FresnelPower power = Dielectric.Fresnel(0.73f, 1f, 1.52f);

        Assert.AreEqual((power.S + power.P) * 0.5f, power.Unpolarized);
        Assert.IsTrue(power.S is >= 0f and <= 1f);
        Assert.IsTrue(power.P is >= 0f and <= 1f);
    }

    [TestMethod]
    public void FresnelThrowsWithExactParameterNames()
    {
        ArgumentOutOfRangeException cosine = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Dielectric.Fresnel(float.NaN, 1f, 1.5f));
        ArgumentOutOfRangeException incident = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Dielectric.Fresnel(1f, 0f, 1.5f));
        ArgumentOutOfRangeException transmitted = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Dielectric.Fresnel(1f, 1f, float.PositiveInfinity));

        Assert.AreEqual("cosIncident", cosine.ParamName);
        Assert.AreEqual("nIncident", incident.ParamName);
        Assert.AreEqual("nTransmitted", transmitted.ParamName);
    }

    [TestMethod]
    public void FresnelBatchMatchesScalarResults()
    {
        float[] cosines = [1f, 0.8f, MathF.Cos(50f * MathF.PI / 180f)];
        float[] incidents = [1f, 1f, 1.5f];
        float[] transmitteds = [1.5f, 1.5f, 1f];
        FresnelPower[] powers = new FresnelPower[3];

        Dielectric.Fresnel(cosines, incidents, transmitteds, powers);

        for (int i = 0; i < powers.Length; i++)
        {
            Assert.AreEqual(Dielectric.Fresnel(cosines[i], incidents[i], transmitteds[i]), powers[i]);
        }
    }

    [TestMethod]
    public void FresnelBatchInvalidLaneThrowsBeforeWriting()
    {
        FresnelPower sentinel = new(0.2f, 0.3f);
        FresnelPower[] powers = [sentinel, sentinel, sentinel];

        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Dielectric.Fresnel(new[] { 1f, -0.1f, 0.5f }, 1f, 1.5f, powers));

        Assert.AreEqual("cosIncidents", exception.ParamName);
        CollectionAssert.AreEqual(new[] { sentinel, sentinel, sentinel }, powers);
    }

    [TestMethod]
    public void FresnelBatchShapeErrorThrowsBeforeWriting()
    {
        FresnelPower sentinel = new(0.2f, 0.3f);
        FresnelPower[] powers = [sentinel];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.Fresnel(new[] { 1f, 0.5f }, 1f, 1.5f, powers));

        Assert.AreEqual("powers", exception.ParamName);
        Assert.AreEqual(sentinel, powers[0]);
    }

    [TestMethod]
    public void NormalReflectanceIsDirectionSymmetric()
    {
        Assert.AreEqual(Dielectric.NormalReflectance(1f, 1.5f), Dielectric.NormalReflectance(1.5f, 1f));
        Assert.AreEqual(0f, Dielectric.NormalReflectance(1.5f, 1.5f));
    }

    [TestMethod]
    public void NormalReflectanceBatchAllowsExactInPlace()
    {
        float[] incidentIndices = [1f, 1.33f, 1.5f];
        float[] transmittedIndices = [1.5f, 1f, 1f];
        float[] expected =
        [
            Dielectric.NormalReflectance(incidentIndices[0], transmittedIndices[0]),
            Dielectric.NormalReflectance(incidentIndices[1], transmittedIndices[1]),
            Dielectric.NormalReflectance(incidentIndices[2], transmittedIndices[2])
        ];

        Dielectric.NormalReflectance(incidentIndices, transmittedIndices, incidentIndices);

        CollectionAssert.AreEqual(expected, incidentIndices);
    }

    [TestMethod]
    public void NormalReflectanceBatchRejectsPartialOverlapBeforeWriting()
    {
        float[] storage = [1f, 7f, 7f, 7f];
        float[] transmittedIndices = [1.5f, 1.5f, 1.5f];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.NormalReflectance(storage.AsSpan(0, 3), transmittedIndices, storage.AsSpan(1, 3)));

        Assert.AreEqual("reflectances", exception.ParamName);
        CollectionAssert.AreEqual(new[] { 1f, 7f, 7f, 7f }, storage);
    }

    [TestMethod]
    public void SchlickHasCorrectEndpointsAndKnownMidpoint()
    {
        const float normal = 0.04f;

        Assert.AreEqual(1f, Dielectric.Schlick(0f, normal));
        Assert.AreEqual(normal, Dielectric.Schlick(1f, normal));
        Assert.AreEqual(0.07f, Dielectric.Schlick(0.5f, normal), 1e-7f);
        Assert.AreEqual(Dielectric.Schlick(0.5f, normal), Dielectric.Schlick(0.5f, 1f, 1.5f));
    }

    [TestMethod]
    public void SchlickBatchAllowsExactInPlace()
    {
        float[] cosinesAndResults = [0f, 0.5f, 1f];

        Dielectric.Schlick(cosinesAndResults, 0.04f, cosinesAndResults);

        CollectionAssert.AreEqual(new[] { 1f, 0.07f, 0.04f }, cosinesAndResults);
    }

    [TestMethod]
    public void SchlickRejectsInvalidReflectance()
    {
        ArgumentOutOfRangeException below = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Dielectric.Schlick(0.5f, -0.1f));
        ArgumentOutOfRangeException above = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Dielectric.Schlick(0.5f, 1.1f));

        Assert.AreEqual("normalReflectance", below.ParamName);
        Assert.AreEqual("normalReflectance", above.ParamName);
    }
}
