using System.Numerics;
using System.Runtime.InteropServices;

namespace Caustikon.Tests;

[TestClass]
public sealed class DielectricRefractionTests
{
    private static readonly Vector3 Normal = Vector3.UnitY;

    [TestMethod]
    public void RefractUnitExtremeFiniteIndicesAtNormalIncidencePreserveDirection()
    {
        Assert.AreEqual(RefractionKind.Refracted,
            Dielectric.RefractUnit(-Normal, Normal, float.MaxValue, float.Epsilon, out Vector3 first));
        Assert.AreEqual(RefractionKind.Refracted,
            Dielectric.RefractUnit(-Normal, Normal, float.Epsilon, float.MaxValue, out Vector3 second));

        Assert.AreEqual(-Normal, first);
        Assert.AreEqual(-Normal, second);
    }

    [TestMethod]
    public void RefractUnitExtremeFiniteIndexRatioAtObliqueIncidenceIsTir()
    {
        Vector3 incident = new(0.5f, -MathF.Sqrt(0.75f), 0f);

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal,
            float.MaxValue, float.Epsilon, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.TotalInternalReflection, kind);
        Assert.AreEqual(Vector3.Zero, refracted);
    }

    [TestMethod]
    public void RefractUnitEqualIndicesPreserveGrazingAndNearGrazingDirectionsExactly()
    {
        Vector3[] incidents = [Vector3.UnitX, Vector3.Normalize(new Vector3(1f, -0.0001f, 0f))];

        foreach (Vector3 incident in incidents)
        {
            RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1.5f, out Vector3 refracted);

            Assert.AreEqual(RefractionKind.Refracted, kind);
            Assert.AreEqual(incident, refracted);
        }
    }

    [TestMethod]
    public void RefractUnitLowerToHigherIndicesHaveNoCriticalAngle()
    {
        Vector3 incident = Vector3.Normalize(new Vector3(1f, -0.0005f, 0f));

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal,
            1f, MathF.BitIncrement(1f), out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        Assert.IsTrue(refracted.Y < 0f);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitAcceptedNearUnitGrazingDirectionReturnsFiniteRefraction()
    {
        Vector3 incident = new(1.0000002f, 0f, 0f);

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal,
            1f, MathF.BitIncrement(1f), out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        Assert.IsTrue(float.IsFinite(refracted.X));
        Assert.IsTrue(float.IsFinite(refracted.Y));
        Assert.IsTrue(float.IsFinite(refracted.Z));
        Assert.IsTrue(refracted.Y < 0f);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitSubnormalAngleWithLargeRatioPreservesTangentialComponent()
    {
        Vector3 incident = new(float.Epsilon, -1f, 0f);

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal,
            1f, 2f * float.Epsilon, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        AssertVector(new Vector3(0.5f, -MathF.Sqrt(0.75f), 0f), refracted, 2e-6f);
    }

    [TestMethod]
    public void RefractUnitObliqueNormalsPreserveSnellsLawAndTheIncidencePlane()
    {
        Vector3 normal = Vector3.Normalize(new Vector3(1f, 2f, 3f));
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));

        for (int degrees = 0; degrees <= 85; degrees += 5)
        {
            float angle = degrees * (MathF.PI / 180f);
            Vector3 incident = (tangent * MathF.Sin(angle)) - (normal * MathF.Cos(angle));

            RefractionKind kind = Dielectric.RefractUnit(incident, normal, 1f, 1.5f, out Vector3 refracted);

            Assert.AreEqual(RefractionKind.Refracted, kind);
            Assert.AreEqual(1f, refracted.Length(), 2e-6f);
            Assert.AreEqual(Vector3.Cross(incident, normal).Length(),
                1.5f * Vector3.Cross(refracted, normal).Length(), 3e-6f);
            Assert.AreEqual(0f, Vector3.Dot(refracted, Vector3.Cross(normal, tangent)), 2e-6f);
            Assert.IsTrue(Vector3.Dot(refracted, normal) < 0f);
        }
    }

    [TestMethod]
    public void RefractUnitBatchRejectsExactNormalOutputOverlapBeforeWriting()
    {
        Vector3[] incidents = [-Normal, -Normal];
        Vector3[] normals = [Normal, Normal];
        RefractionKind[] kinds = [RefractionKind.CriticalAngle, RefractionKind.CriticalAngle];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(incidents, normals, 1f, 1.5f, normals, kinds));

        CollectionAssert.AreEqual(new[] { Normal, Normal }, normals);
        CollectionAssert.AreEqual(new[] { RefractionKind.CriticalAngle, RefractionKind.CriticalAngle }, kinds);
    }

    [TestMethod]
    public void RefractUnitBatchRejectsStatusesAliasingIncidentBytesBeforeWriting()
    {
        Vector3 sentinel = new(7f, 8f, 9f);
        Vector3[] incidents = [-Normal, -Normal];
        Vector3[] normals = [Normal, Normal];
        Vector3[] outputs = [sentinel, sentinel];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(incidents, normals, 1f, 1.5f, outputs,
                MemoryMarshal.Cast<Vector3, RefractionKind>(incidents.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { -Normal, -Normal }, incidents);
        CollectionAssert.AreEqual(new[] { sentinel, sentinel }, outputs);
    }

    [TestMethod]
    public void RefractUnitBatchRejectsStatusesAliasingOutputBytesBeforeWriting()
    {
        Vector3 sentinel = new(7f, 8f, 9f);
        Vector3[] incidents = [-Normal, -Normal];
        Vector3[] normals = [Normal, Normal];
        Vector3[] outputs = [sentinel, sentinel];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(incidents, normals, 1f, 1.5f, outputs,
                MemoryMarshal.Cast<Vector3, RefractionKind>(outputs.AsSpan()).Slice(0, 2)));

        CollectionAssert.AreEqual(new[] { sentinel, sentinel }, outputs);
    }

    [TestMethod]
    public void RefractUnitBatchRejectsPerLaneIndicesAliasingOutputBytesBeforeWriting()
    {
        Vector3 sentinel = new(1f, 1f, 9f);
        Vector3[] incidents = [-Normal, -Normal];
        Vector3[] normals = [Normal, Normal];
        Vector3[] outputs = [sentinel, sentinel];
        RefractionKind[] kinds = [RefractionKind.CriticalAngle, RefractionKind.CriticalAngle];

        Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(incidents, normals,
                MemoryMarshal.Cast<Vector3, float>(outputs.AsSpan()).Slice(0, 2),
                new[] { 1.5f, 1.5f }, outputs, kinds));

        CollectionAssert.AreEqual(new[] { sentinel, sentinel }, outputs);
        CollectionAssert.AreEqual(new[] { RefractionKind.CriticalAngle, RefractionKind.CriticalAngle }, kinds);
    }

    [TestMethod]
    public void RefractUnitNormalIncidencePreservesDirection()
    {
        RefractionKind kind = Dielectric.RefractUnit(-Vector3.UnitY, Normal, 1f, 1.5f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        AssertVector(-Vector3.UnitY, refracted, 1e-6f);
    }

    [TestMethod]
    public void RefractUnitAirToGlassObeysSnellsLaw()
    {
        Vector3 incident = new(0.5f, -MathF.Sqrt(0.75f), 0f);

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1f, 1.5f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        Assert.AreEqual(1f / 3f, refracted.X, 2e-6f);
        Assert.AreEqual(-MathF.Sqrt(8f / 9f), refracted.Y, 2e-6f);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitAboveCriticalAngleReturnsTirAndZero()
    {
        float angle = 50f * (MathF.PI / 180f);
        Vector3 incident = new(MathF.Sin(angle), -MathF.Cos(angle), 0f);

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.TotalInternalReflection, kind);
        Assert.AreEqual(Vector3.Zero, refracted);
    }

    [TestMethod]
    public void RefractUnitWithinNegativeCriticalBoundaryIsCriticalNotTir()
    {
        Vector3 incident = IncidentForTargetDiscriminant(-0.5f * CriticalTolerance());

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.CriticalAngle, kind);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
        Assert.AreEqual(0f, Vector3.Dot(refracted, Normal), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitBelowNegativeCriticalBoundaryIsTir()
    {
        Vector3 incident = IncidentForTargetDiscriminant(-4f * CriticalTolerance());

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.TotalInternalReflection, kind);
        Assert.AreEqual(Vector3.Zero, refracted);
    }

    [TestMethod]
    public void RefractUnitWithinPositiveCriticalBoundaryIsCritical()
    {
        Vector3 incident = IncidentForTargetDiscriminant(0.5f * CriticalTolerance());

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.CriticalAngle, kind);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitAbovePositiveCriticalBoundaryIsRefracted()
    {
        Vector3 incident = IncidentForTargetDiscriminant(4f * CriticalTolerance());

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1.5f, 1f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        Assert.IsTrue(Vector3.Dot(refracted, Normal) < 0f);
    }

    [TestMethod]
    public void RefractUnitRejectsWrongNormalHemisphere()
    {
        RefractionKind kind = Dielectric.RefractUnit(Vector3.UnitY, Normal, 1f, 1.5f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.InvalidInput, kind);
        Assert.AreEqual(Vector3.Zero, refracted);
    }

    [TestMethod]
    public void RefractUnitRejectsNonUnitAndNonFiniteVectors()
    {
        RefractionKind nonUnitIncident = Dielectric.RefractUnit(new Vector3(0f, -0.5f, 0f), Normal, 1f, 1.5f, out Vector3 first);
        RefractionKind nonUnitNormal = Dielectric.RefractUnit(-Vector3.UnitY, new Vector3(0f, 2f, 0f), 1f, 1.5f, out Vector3 second);
        RefractionKind nonFinite = Dielectric.RefractUnit(new Vector3(float.NaN, 0f, 0f), Normal, 1f, 1.5f, out Vector3 third);

        Assert.AreEqual(RefractionKind.InvalidInput, nonUnitIncident);
        Assert.AreEqual(RefractionKind.InvalidInput, nonUnitNormal);
        Assert.AreEqual(RefractionKind.InvalidInput, nonFinite);
        Assert.AreEqual(Vector3.Zero, first);
        Assert.AreEqual(Vector3.Zero, second);
        Assert.AreEqual(Vector3.Zero, third);
    }

    [TestMethod]
    public void RefractUnitRejectsInvalidIndices()
    {
        float[] invalid = [0f, -1f, float.NaN, float.PositiveInfinity];

        foreach (float index in invalid)
        {
            Assert.AreEqual(
                RefractionKind.InvalidInput,
                Dielectric.RefractUnit(-Vector3.UnitY, Normal, index, 1f, out Vector3 refracted));
            Assert.AreEqual(Vector3.Zero, refracted);
        }
    }

    [TestMethod]
    public void RefractUnitAcceptsOrdinaryNormalizedVector()
    {
        Vector3 incident = Vector3.Normalize(new Vector3(0.37f, -0.91f, 0.18f));

        RefractionKind kind = Dielectric.RefractUnit(incident, Normal, 1f, 1.33f, out Vector3 refracted);

        Assert.AreEqual(RefractionKind.Refracted, kind);
        Assert.AreEqual(1f, refracted.Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitBatchReportsEachLaneAndUsesZeroForInvalidOrTir()
    {
        Vector3 tir = Vector3.Normalize(new Vector3(0.9f, -0.4f, 0f));
        Vector3[] incidents = [-Vector3.UnitY, -Vector3.UnitY, tir, new(0f, -0.5f, 0f)];
        Vector3[] normals = [Normal, Normal, Normal, Normal];
        float[] nIncidents = [1f, 0f, 1.5f, 1f];
        float[] nTransmitteds = [1.5f, 1.5f, 1f, 1.5f];
        Vector3[] outputs = new Vector3[4];
        RefractionKind[] kinds = new RefractionKind[4];

        Dielectric.RefractUnit(incidents, normals, nIncidents, nTransmitteds, outputs, kinds);

        CollectionAssert.AreEqual(
            new[] { RefractionKind.Refracted, RefractionKind.InvalidInput, RefractionKind.TotalInternalReflection, RefractionKind.InvalidInput },
            kinds);
        AssertVector(-Vector3.UnitY, outputs[0], 1e-6f);
        Assert.AreEqual(Vector3.Zero, outputs[1]);
        Assert.AreEqual(Vector3.Zero, outputs[2]);
        Assert.AreEqual(Vector3.Zero, outputs[3]);
    }

    [TestMethod]
    public void RefractUnitBatchAllowsExactInPlace()
    {
        Vector3[] incidents = [-Vector3.UnitY, Vector3.Normalize(new Vector3(0.5f, -0.8f, 0.2f))];
        Vector3[] normals = [Normal, Normal];
        RefractionKind[] kinds = new RefractionKind[2];

        Dielectric.RefractUnit(incidents, normals, 1f, 1.5f, incidents, kinds);

        Assert.AreEqual(RefractionKind.Refracted, kinds[0]);
        Assert.AreEqual(RefractionKind.Refracted, kinds[1]);
        Assert.AreEqual(1f, incidents[0].Length(), 2e-6f);
        Assert.AreEqual(1f, incidents[1].Length(), 2e-6f);
    }

    [TestMethod]
    public void RefractUnitBatchRejectsPartialOverlapBeforeWriting()
    {
        Vector3 sentinel = new(7f, 8f, 9f);
        Vector3[] storage = [-Vector3.UnitY, sentinel, sentinel, sentinel];
        Vector3[] normals = [Normal, Normal, Normal];
        RefractionKind[] kinds = [RefractionKind.CriticalAngle, RefractionKind.CriticalAngle, RefractionKind.CriticalAngle];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(storage.AsSpan(0, 3), normals, 1f, 1.5f, storage.AsSpan(1, 3), kinds));

        Assert.AreEqual("refractedUnits", exception.ParamName);
        Assert.AreEqual(sentinel, storage[1]);
        CollectionAssert.AreEqual(
            new[] { RefractionKind.CriticalAngle, RefractionKind.CriticalAngle, RefractionKind.CriticalAngle },
            kinds);
    }

    [TestMethod]
    public void RefractUnitBatchShapeErrorOccursBeforeWriting()
    {
        Vector3 sentinel = new(7f, 8f, 9f);
        Vector3[] outputs = [sentinel, sentinel];
        RefractionKind[] kinds = [RefractionKind.CriticalAngle, RefractionKind.CriticalAngle];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
            Dielectric.RefractUnit(new[] { -Vector3.UnitY, -Vector3.UnitY }, new[] { Normal }, 1f, 1.5f, outputs, kinds));

        Assert.AreEqual("normalUnits", exception.ParamName);
        CollectionAssert.AreEqual(new[] { sentinel, sentinel }, outputs);
        CollectionAssert.AreEqual(new[] { RefractionKind.CriticalAngle, RefractionKind.CriticalAngle }, kinds);
    }

    [TestMethod]
    public void RefractUnitBatchInvalidSharedIndexThrowsBeforeWriting()
    {
        Vector3 sentinel = new(7f, 8f, 9f);
        Vector3[] outputs = [sentinel];
        RefractionKind[] kinds = [RefractionKind.CriticalAngle];

        ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Dielectric.RefractUnit(new[] { -Vector3.UnitY }, new[] { Normal }, 0f, 1.5f, outputs, kinds));

        Assert.AreEqual("nIncident", exception.ParamName);
        Assert.AreEqual(sentinel, outputs[0]);
        Assert.AreEqual(RefractionKind.CriticalAngle, kinds[0]);
    }

    [TestMethod]
    public void RefractUnitScalarHotPathAllocatesNothing()
    {
        for (int i = 0; i < 10; i++)
        {
            _ = Dielectric.RefractUnit(-Vector3.UnitY, Normal, 1f, 1.5f, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            _ = Dielectric.RefractUnit(-Vector3.UnitY, Normal, 1f, 1.5f, out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0L, allocated);
    }

    private static Vector3 IncidentForTargetDiscriminant(float targetDiscriminant)
    {
        const float eta = 1.5f;
        float sinSquared = (1f - targetDiscriminant) / (eta * eta);
        return new Vector3(MathF.Sqrt(sinSquared), -MathF.Sqrt(1f - sinSquared), 0f);
    }

    private static float CriticalTolerance()
    {
        const float unitRoundoff = 1.1920929e-7f;
        return 8f * unitRoundoff;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance);
        Assert.AreEqual(expected.Y, actual.Y, tolerance);
        Assert.AreEqual(expected.Z, actual.Z, tolerance);
    }
}
