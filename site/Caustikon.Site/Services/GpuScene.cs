using System.Numerics;
using Caustikon.Glasses;

namespace Caustikon.Site.Services;

/// <summary>
/// Everything the WebGL renderer needs for one glass solid, as plain numbers: the same nine sampled wavelengths, indices,
/// per-millimetre absorption and colour weights the CPU renderer uses, plus the solid's planes and the lighting.
/// Serialized to JavaScript as camelCase.
/// </summary>
public sealed record GpuScene(
    bool Sphere,
    float[] Centre,
    float Extent,
    float[] Planes,
    int PlaneCount,
    double[] Indices,
    double[] Alphas,
    float[] Weights,
    double MillimetersPerUnit,
    float TileUnits,
    int Backdrop,
    float[] KeyPosition,
    float KeyIntensity,
    float Ambient,
    float Exposure,
    double[] SpectrumIndices,
    double[] SpectrumAlphas,
    float[] SpectrumWeights)
{
    public static GpuScene Build(
        Glass glass,
        RenderShape shape,
        double radiusMillimeters,
        Backdrop backdrop,
        double lightAzimuthDegrees,
        double lightElevationDegrees,
        double lightIntensity,
        double ambient,
        double exposure = 1)
    {
        shape.Describe(out bool sphere, out Vector3 centre, out float extent, out IReadOnlyList<(Vector3 Normal, float Distance)> planes);
        float[] flat = new float[planes.Count * 4];
        for (int i = 0; i < planes.Count; i++)
        {
            flat[i * 4] = planes[i].Normal.X;
            flat[i * 4 + 1] = planes[i].Normal.Y;
            flat[i * 4 + 2] = planes[i].Normal.Z;
            flat[i * 4 + 3] = planes[i].Distance;
        }

        IReadOnlyList<Vector3> weights = GlassRenderer.SampleColourWeights;
        float[] flatWeights = new float[weights.Count * 3];
        for (int i = 0; i < weights.Count; i++)
        {
            flatWeights[i * 3] = weights[i].X;
            flatWeights[i * 3 + 1] = weights[i].Y;
            flatWeights[i * 3 + 2] = weights[i].Z;
        }

        Vector3 key = GlassRenderer.KeyLightPosition(lightAzimuthDegrees, lightElevationDegrees);
        (double[] spectrumIndices, double[] spectrumAlphas, float[] spectrumWeights) = GlassRenderer.SpectrumTable(glass);
        return new GpuScene(
            sphere,
            [centre.X, centre.Y, centre.Z],
            extent,
            flat,
            planes.Count,
            GlassRenderer.IndicesFor(glass),
            GlassRenderer.AbsorptionPerMillimetre(glass),
            flatWeights,
            radiusMillimeters / extent,
            (float)(16d / radiusMillimeters * extent),
            (int)backdrop,
            [key.X, key.Y, key.Z],
            (float)Math.Clamp(lightIntensity, 0d, 4d),
            (float)Math.Clamp(ambient, 0d, 2d),
            (float)Math.Clamp(exposure, 0.1d, 8d),
            spectrumIndices,
            spectrumAlphas,
            spectrumWeights);
    }
}
