using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Caustikon.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DielectricBenchmarks
{
    private const float AirIndex = 1.0f;
    private const float GlassIndex = 1.5f;
    private const float UnitIntervalScale = 1.0f / 16_777_216.0f;

    private Vector3[] ordinaryIncidentDirections = [];
    private Vector3[] totalInternalReflectionIncidentDirections = [];
    private Vector3[] normalsToIncidentMedium = [];
    private Vector3[] transmittedDirections = [];
    private RefractionKind[] kinds = [];

    [Params(1, 16, 1_024, 1_000_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ordinaryIncidentDirections = new Vector3[Count];
        totalInternalReflectionIncidentDirections = new Vector3[Count];
        normalsToIncidentMedium = new Vector3[Count];
        transmittedDirections = new Vector3[Count];
        kinds = new RefractionKind[Count];

        uint state = 0xA341316Cu;

        for (int i = 0; i < Count; i++)
        {
            float ordinaryAngle = Lerp(0.05f, 1.10f, NextUnitFloat(ref state));
            float tirAngle = Lerp(0.80f, 1.20f, NextUnitFloat(ref state));
            float azimuth = 2.0f * MathF.PI * NextUnitFloat(ref state);

            ordinaryIncidentDirections[i] = CreateIncidentDirection(ordinaryAngle, azimuth);
            totalInternalReflectionIncidentDirections[i] = CreateIncidentDirection(tirAngle, azimuth);
            normalsToIncidentMedium[i] = Vector3.UnitZ;
        }

        ValidateScenarios();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Refraction")]
    public void ScalarRefraction()
    {
        for (int i = 0; i < Count; i++)
        {
            kinds[i] = Dielectric.RefractUnit(
                ordinaryIncidentDirections[i],
                normalsToIncidentMedium[i],
                AirIndex,
                GlassIndex,
                out transmittedDirections[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Refraction")]
    public void SpanRefraction()
    {
        Dielectric.RefractUnit(
            ordinaryIncidentDirections,
            normalsToIncidentMedium,
            AirIndex,
            GlassIndex,
            transmittedDirections,
            kinds);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TotalInternalReflection")]
    public void ScalarTotalInternalReflection()
    {
        for (int i = 0; i < Count; i++)
        {
            kinds[i] = Dielectric.RefractUnit(
                totalInternalReflectionIncidentDirections[i],
                normalsToIncidentMedium[i],
                GlassIndex,
                AirIndex,
                out transmittedDirections[i]);
        }
    }

    [Benchmark]
    [BenchmarkCategory("TotalInternalReflection")]
    public void SpanTotalInternalReflection()
    {
        Dielectric.RefractUnit(
            totalInternalReflectionIncidentDirections,
            normalsToIncidentMedium,
            GlassIndex,
            AirIndex,
            transmittedDirections,
            kinds);
    }

    private void ValidateScenarios()
    {
        for (int i = 0; i < Count; i++)
        {
            RefractionKind ordinaryKind = Dielectric.RefractUnit(
                ordinaryIncidentDirections[i],
                normalsToIncidentMedium[i],
                AirIndex,
                GlassIndex,
                out _);

            if (ordinaryKind != RefractionKind.Refracted)
            {
                throw new InvalidOperationException($"Ordinary input {i} produced {ordinaryKind}.");
            }

            RefractionKind tirKind = Dielectric.RefractUnit(
                totalInternalReflectionIncidentDirections[i],
                normalsToIncidentMedium[i],
                GlassIndex,
                AirIndex,
                out _);

            if (tirKind != RefractionKind.TotalInternalReflection)
            {
                throw new InvalidOperationException($"TIR input {i} produced {tirKind}.");
            }
        }
    }

    private static Vector3 CreateIncidentDirection(float polarAngle, float azimuth)
    {
        float sinPolar = MathF.Sin(polarAngle);
        float cosPolar = MathF.Cos(polarAngle);

        return new Vector3(
            sinPolar * MathF.Cos(azimuth),
            sinPolar * MathF.Sin(azimuth),
            -cosPolar);
    }

    private static float NextUnitFloat(ref uint state)
    {
        state = (1_664_525u * state) + 1_013_904_223u;
        return (state >> 8) * UnitIntervalScale;
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }
}
