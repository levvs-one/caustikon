using System.Numerics;
using Caustikon;
using Caustikon.Glasses;

namespace Caustikon.Site.Services;

/// <summary>
/// A spectral ray tracer for one glass sphere resting on a checkered ground, built only from the packages' own
/// refraction, Fresnel reflectance and absorption. Nine wavelengths are traced per pixel; each carries the glass's index
/// and extinction at that wavelength, so chromatic fringes and the tint of thick glass appear because the physics puts
/// them there, not because they were painted.
/// </summary>
/// <remarks>
/// What is simulated: pinhole camera; one sphere; Fresnel-weighted reflection off the surface (one bounce into the
/// environment); refraction in, up to four internal reflections, refraction out at each exit; Beer–Lambert absorption over
/// every internal chord from the glass's tabulated k; a ground plane and a sky as the environment.
/// What is not: shadows, caustics on the ground, polarization beyond the unpolarized power split, diffraction, coatings.
/// </remarks>
public sealed class GlassRenderer
{
    private const int SampleCount = 9;
    private static readonly double[] Wavelengths = Enumerable.Range(0, SampleCount).Select(i => 400d + 300d * i / (SampleCount - 1)).ToArray();
    private static readonly Vector3[] SampleWeights = BuildWeights();

    private readonly Glass glass;
    private readonly double[] indices = new double[SampleCount];
    private readonly double radiusMillimeters;
    private readonly TabulatedExtinction? extinction;

    public GlassRenderer(Glass glass, double radiusMillimeters)
    {
        this.glass = glass;
        this.radiusMillimeters = radiusMillimeters;
        extinction = glass.Extinction;
        for (int i = 0; i < SampleCount; i++)
        {
            double wavelength = Wavelengths[i];
            double clamped = Math.Clamp(wavelength, glass.Model.MinimumWavelengthNanometers, glass.Model.MaximumWavelengthNanometers);
            indices[i] = glass.Model.EvaluateNanometers(clamped, out double n) == DispersionStatus.Success ? n : 1.5d;
        }
    }

    public static IReadOnlyList<double> SampledWavelengths => Wavelengths;

    /// <summary>Renders rows [<paramref name="firstRow"/>, <paramref name="lastRow"/>) of a <paramref name="size"/>×<paramref name="size"/> image into RGBA bytes.</summary>
    public void RenderRows(byte[] rgba, int size, int firstRow, int lastRow)
    {
        // Camera: slightly above the sphere's equator, looking down at it; the sphere has radius 1 and rests on y = -1.
        Vector3 eye = new(0f, 0.55f, -3.6f);
        Vector3 target = new(0f, -0.15f, 0f);
        Vector3 forward = Vector3.Normalize(target - eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);
        double tanHalf = Math.Tan(30d * Math.PI / 360d);

        for (int y = firstRow; y < lastRow; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double sx = ((x + 0.5d) / size * 2d - 1d) * tanHalf;
                double sy = (1d - (y + 0.5d) / size * 2d) * tanHalf;
                Vector3 direction = Vector3.Normalize(forward + right * (float)sx + up * (float)sy);

                Vector3 colour = Vector3.Zero;
                for (int i = 0; i < SampleCount; i++)
                {
                    Vector3 sample = Trace(eye, direction, i);
                    colour += sample * SampleWeights[i];
                }

                int offset = (y * size + x) * 4;
                rgba[offset] = ToByte(colour.X);
                rgba[offset + 1] = ToByte(colour.Y);
                rgba[offset + 2] = ToByte(colour.Z);
                rgba[offset + 3] = 255;
            }
        }
    }

    private Vector3 Trace(Vector3 origin, Vector3 direction, int sample)
    {
        if (!HitSphere(origin, direction, out float tEntry))
        {
            return Environment(origin, direction);
        }

        float n = (float)indices[sample];
        Vector3 p = origin + direction * tEntry;
        Vector3 normal = Vector3.Normalize(p);
        float cosIncident = Math.Clamp(-Vector3.Dot(direction, normal), 0f, 1f);
        FresnelPower entry = Dielectric.Fresnel(cosIncident, 1f, n);
        Vector3 result = Environment(p, Vector3.Reflect(direction, normal)) * entry.Unpolarized;

        if (Dielectric.RefractUnit(direction, normal, 1f, n, out Vector3 inside) != RefractionKind.Refracted)
        {
            return result;
        }

        double weight = 1d - entry.Unpolarized;
        Vector3 position = p;
        Vector3 travel = inside;
        for (int bounce = 0; bounce < 4 && weight > 1e-3; bounce++)
        {
            // Second intersection of a ray starting on the sphere and going inward.
            float chord = -2f * Vector3.Dot(position, travel);
            Vector3 q = position + travel * chord;
            weight *= Transmittance(sample, chord * radiusMillimeters);
            Vector3 outwardNormal = Vector3.Normalize(q);
            float cosInside = Math.Clamp(-Vector3.Dot(travel, -outwardNormal), 0f, 1f);
            RefractionKind kind = Dielectric.RefractUnit(travel, -outwardNormal, n, 1f, out Vector3 exit);
            if (kind == RefractionKind.Refracted)
            {
                FresnelPower leaving = Dielectric.Fresnel(cosInside, n, 1f);
                result += Environment(q, exit) * (float)(weight * (1d - leaving.Unpolarized));
                weight *= leaving.Unpolarized;
            }

            travel = Vector3.Reflect(travel, outwardNormal);
            position = q;
        }

        return result;
    }

    private double Transmittance(int sample, double pathMillimeters)
    {
        if (extinction is null)
        {
            return 1d;
        }

        double wavelength = Math.Clamp(Wavelengths[sample], extinction.MinimumWavelengthNanometers, extinction.MaximumWavelengthNanometers);
        extinction.InternalTransmittance(wavelength, pathMillimeters, out double t);
        return t;
    }

    private static bool HitSphere(Vector3 origin, Vector3 direction, out float t)
    {
        float b = Vector3.Dot(origin, direction);
        float c = Vector3.Dot(origin, origin) - 1f;
        float discriminant = b * b - c;
        if (discriminant < 0f)
        {
            t = 0f;
            return false;
        }

        t = -b - MathF.Sqrt(discriminant);
        return t > 1e-4f;
    }

    /// <summary>Linear RGB of the environment along a ray: a checkered ground at y = -1, otherwise a studio sky with a soft key light.</summary>
    private static Vector3 Environment(Vector3 origin, Vector3 direction)
    {
        if (direction.Y < -1e-4f)
        {
            float t = (-1f - origin.Y) / direction.Y;
            Vector3 hit = origin + direction * t;
            float distance = MathF.Sqrt(hit.X * hit.X + hit.Z * hit.Z);
            bool dark = ((int)MathF.Floor(hit.X / 0.55f) + (int)MathF.Floor(hit.Z / 0.55f) & 1) == 0;
            Vector3 tile = dark ? new Vector3(0.08f, 0.085f, 0.09f) : new Vector3(0.86f, 0.80f, 0.68f);
            // The ground is lit by the key light too: brighter toward it, so the sphere sits on something rather than floating.
            float lit = 0.75f + 0.35f * MathF.Max(0f, Vector3.Dot(Vector3.Normalize(KeyLight - hit), Vector3.UnitY));
            float fade = MathF.Exp(-distance * 0.18f);
            return tile * lit * fade + Sky(direction) * (1f - fade);
        }

        return Sky(direction);
    }

    private static readonly Vector3 KeyLight = new(-2.2f, 3.0f, -1.6f);
    private static readonly Vector3 KeyDirection = Vector3.Normalize(KeyLight);
    private static readonly Vector3 RimDirection = Vector3.Normalize(new Vector3(2.4f, 1.2f, 2.0f));

    private static Vector3 Sky(Vector3 direction)
    {
        float t = Math.Clamp(direction.Y * 0.5f + 0.5f, 0f, 1f);
        Vector3 horizon = new(0.30f, 0.33f, 0.36f);
        Vector3 zenith = new(0.035f, 0.045f, 0.06f);
        Vector3 sky = horizon * (1f - t) + zenith * t;

        // A large soft key light and a small cool rim light: what the Fresnel reflection has to show.
        float key = MathF.Max(0f, Vector3.Dot(direction, KeyDirection));
        sky += new Vector3(1.0f, 0.97f, 0.92f) * (3.2f * MathF.Pow(key, 40f) + 0.35f * MathF.Pow(key, 6f));
        float rim = MathF.Max(0f, Vector3.Dot(direction, RimDirection));
        sky += new Vector3(0.55f, 0.65f, 0.80f) * (1.6f * MathF.Pow(rim, 60f));
        return sky;
    }

    private static Vector3[] BuildWeights()
    {
        Vector3[] linear = new Vector3[SampleCount];
        Vector3 total = Vector3.Zero;
        for (int i = 0; i < SampleCount; i++)
        {
            (double r, double g, double b) = GlassColour.Monochromatic(Wavelengths[i]);
            linear[i] = new Vector3((float)Decompand(r), (float)Decompand(g), (float)Decompand(b));
            total += linear[i];
        }

        // Normalize per channel so a spectrally flat environment seen through nothing comes back unchanged.
        for (int i = 0; i < SampleCount; i++)
        {
            linear[i] = new Vector3(linear[i].X / total.X, linear[i].Y / total.Y, linear[i].Z / total.Z);
        }

        return linear;
    }

    private static double Decompand(double companded) =>
        companded <= 0.04045d ? companded / 12.92d : Math.Pow((companded + 0.055d) / 1.055d, 2.4d);

    private static byte ToByte(float linear)
    {
        double companded = TransmittedColour.Compand(Math.Clamp(linear, 0f, 1f));
        return (byte)Math.Clamp(Math.Round(companded * 255d), 0d, 255d);
    }
}
