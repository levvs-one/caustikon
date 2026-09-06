using System.Numerics;
using Caustikon;
using Caustikon.Glasses;

namespace Caustikon.Site.Services;

/// <summary>The scenery behind and beneath the glass.</summary>
public enum Backdrop
{
    Checker,
    Stripes,
    Grid,
    Paper,
    Night,
}

/// <summary>
/// A spectral ray tracer for one glass solid resting on a ground, built only from the packages' own refraction, Fresnel
/// reflectance and absorption. Nine wavelengths are traced per pixel; each carries the glass's index and extinction at that
/// wavelength, so chromatic fringes and the tint of thick glass appear because the physics puts them there.
/// </summary>
/// <remarks>
/// What is simulated: pinhole camera; one convex solid; Fresnel-weighted reflection off the surface (one bounce into the
/// environment); refraction in, up to six internal reflections, refraction out at each exit; Beer–Lambert absorption over
/// every internal chord from the glass's tabulated k; a ground plane with a pattern of known size in millimetres and a sky.
/// What is not: shadows, caustics on the ground, polarization beyond the unpolarized power split, diffraction, coatings.
/// </remarks>
public sealed class GlassRenderer
{
    private const int SampleCount = 9;
    private static readonly double[] Wavelengths = Enumerable.Range(0, SampleCount).Select(i => 400d + 300d * i / (SampleCount - 1)).ToArray();
    private static readonly Vector3[] SampleWeights = BuildWeights();

    private readonly double[] indices;
    private readonly double radiusMillimeters;
    private readonly TabulatedExtinction? extinction;
    private readonly RenderShape shape;
    private readonly Backdrop backdrop;
    private readonly float tileUnits;
    private readonly Vector3 keyPosition;
    private readonly Vector3 keyDirection;
    private readonly float keyIntensity;
    private readonly float ambient;
    private readonly float exposure;
    private static readonly Vector3 RimDirection = Vector3.Normalize(new Vector3(2.4f, 1.2f, 2.0f));

    /// <param name="glass">The glass to render; its model and extinction table are sampled at nine wavelengths.</param>
    /// <param name="shape">The solid.</param>
    /// <param name="radiusMillimeters">Half the solid's extent in millimetres: sets every internal path length and the size of the ground pattern relative to the solid.</param>
    /// <param name="backdrop">The scenery.</param>
    /// <param name="lightAzimuthDegrees">Where the key light stands around the solid: 0 is behind the camera, 90 to the camera's left.</param>
    /// <param name="lightElevationDegrees">How high the key light sits above the ground, 5–85 degrees.</param>
    /// <param name="lightIntensity">Relative strength of the key light; 1 is the default lamp.</param>
    /// <param name="ambient">Relative brightness of the room, 0–2; 1 is the default.</param>
    public GlassRenderer(
        Glass glass,
        RenderShape shape,
        double radiusMillimeters,
        Backdrop backdrop = Backdrop.Checker,
        double lightAzimuthDegrees = 55,
        double lightElevationDegrees = 50,
        double lightIntensity = 1,
        double ambient = 1,
        double exposure = 1)
    {
        this.exposure = (float)Math.Clamp(exposure, 0.1d, 8d);
        this.shape = shape;
        this.radiusMillimeters = radiusMillimeters;
        this.backdrop = backdrop;
        extinction = glass.Extinction;
        // Ground pattern period: 16 mm tiles, so the same slab of checker is what a 10 mm marble and a 200 mm ball sit on.
        tileUnits = (float)(16d / radiusMillimeters * shape.Extent);
        keyPosition = KeyLightPosition(lightAzimuthDegrees, lightElevationDegrees);
        keyDirection = Vector3.Normalize(keyPosition);
        keyIntensity = (float)Math.Clamp(lightIntensity, 0d, 4d);
        this.ambient = (float)Math.Clamp(ambient, 0d, 2d);
        indices = IndicesFor(glass);
    }

    public static IReadOnlyList<double> SampledWavelengths => Wavelengths;

    /// <summary>Linear-RGB weight of each sampled wavelength; the nine sum to white.</summary>
    public static IReadOnlyList<Vector3> SampleColourWeights => SampleWeights;

    /// <summary>The glass's index at each sampled wavelength, clamped to the model's range.</summary>
    public static double[] IndicesFor(Glass glass)
    {
        double[] result = new double[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            double clamped = Math.Clamp(Wavelengths[i], glass.Model.MinimumWavelengthNanometers, glass.Model.MaximumWavelengthNanometers);
            result[i] = glass.Model.EvaluateNanometers(clamped, out double n) == DispersionStatus.Success ? n : 1.5d;
        }

        return result;
    }

    /// <summary>Beer–Lambert coefficient per millimetre at each sampled wavelength, from the glass's k table; zeros without one.</summary>
    public static double[] AbsorptionPerMillimetre(Glass glass)
    {
        double[] result = new double[SampleCount];
        if (glass.Extinction is not { } extinction)
        {
            return result;
        }

        for (int i = 0; i < SampleCount; i++)
        {
            double clamped = Math.Clamp(Wavelengths[i], extinction.MinimumWavelengthNanometers, extinction.MaximumWavelengthNanometers);
            extinction.InternalTransmittance(clamped, 1d, out double t);
            result[i] = t > 0d ? -Math.Log(t) : 50d;
        }

        return result;
    }

    /// <summary>Where the key light stands, 3.8 units from the solid: azimuth 0 behind the camera, 90 to its left.</summary>
    public static Vector3 KeyLightPosition(double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * Math.PI / 180d;
        double el = Math.Clamp(elevationDegrees, 5d, 85d) * Math.PI / 180d;
        return new Vector3((float)(-Math.Sin(az) * Math.Cos(el)), (float)Math.Sin(el), (float)(-Math.Cos(az) * Math.Cos(el))) * 3.8f;
    }

    /// <summary>Renders rows [<paramref name="firstRow"/>, <paramref name="lastRow"/>) of a <paramref name="size"/>×<paramref name="size"/> image: linear RGB into <paramref name="linear"/> and companded RGBA into <paramref name="rgba"/>.</summary>
    public void RenderRows(Vector3[] linear, byte[] rgba, int size, int firstRow, int lastRow)
    {
        for (int y = firstRow; y < lastRow; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector3 colour = Shade(x + 0.5d, y + 0.5d, size);
                linear[y * size + x] = colour;
                Store(rgba, y * size + x, colour);
            }
        }
    }

    /// <summary>
    /// Supersamples only the pixels whose neighbours differ noticeably: the solid's silhouette, the refracted pattern edges and
    /// the chromatic fringes. Four jittered samples replace one there; smooth areas keep their single sample.
    /// </summary>
    /// <returns>How many pixels were refined.</returns>
    public int RefineEdges(Vector3[] linear, byte[] rgba, int size, int firstRow, int lastRow)
    {
        const float threshold = 0.035f;
        int refined = 0;
        for (int y = Math.Max(1, firstRow); y < Math.Min(size - 1, lastRow); y++)
        {
            for (int x = 1; x < size - 1; x++)
            {
                int i = y * size + x;
                Vector3 c = linear[i];
                if (Contrast(c, linear[i - 1]) < threshold && Contrast(c, linear[i + 1]) < threshold &&
                    Contrast(c, linear[i - size]) < threshold && Contrast(c, linear[i + size]) < threshold)
                {
                    continue;
                }

                Vector3 sum = Shade(x + 0.25d, y + 0.25d, size) + Shade(x + 0.75d, y + 0.25d, size)
                    + Shade(x + 0.25d, y + 0.75d, size) + Shade(x + 0.75d, y + 0.75d, size);
                Store(rgba, i, sum * 0.25f);
                refined++;
            }
        }

        return refined;
    }

    private static float Contrast(Vector3 a, Vector3 b)
    {
        Vector3 d = Vector3.Abs(a - b);
        return MathF.Max(d.X, MathF.Max(d.Y, d.Z));
    }

    // Camera: slightly above the solid, looking down at it; the solid's lowest point touches y = -1.
    private static readonly Vector3 Eye = new(0f, 0.55f, -3.6f);
    private static readonly Vector3 Forward = Vector3.Normalize(new Vector3(0f, -0.15f, 0f) - Eye);
    private static readonly Vector3 Right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Forward));
    private static readonly Vector3 Up = Vector3.Cross(Forward, Right);
    private static readonly double TanHalf = Math.Tan(30d * Math.PI / 360d);

    private Vector3 Shade(double px, double py, int size)
    {
        double sx = (px / size * 2d - 1d) * TanHalf;
        double sy = (1d - py / size * 2d) * TanHalf;
        Vector3 direction = Vector3.Normalize(Forward + Right * (float)sx + Up * (float)sy);

        // A ray that misses the solid sees the environment, which is not dispersive: one lookup instead of nine.
        if (!shape.Enter(Eye, direction, out float tEntry, out Vector3 entryNormal))
        {
            return Environment(Eye, direction);
        }

        Vector3 colour = Vector3.Zero;
        for (int i = 0; i < SampleCount; i++)
        {
            colour += Trace(Eye, direction, tEntry, entryNormal, i) * SampleWeights[i];
        }

        return colour;
    }

    private void Store(byte[] rgba, int index, Vector3 colour)
    {
        int offset = index * 4;
        rgba[offset] = ToByte(Tone(colour.X));
        rgba[offset + 1] = ToByte(Tone(colour.Y));
        rgba[offset + 2] = ToByte(Tone(colour.Z));
        rgba[offset + 3] = 255;
    }

    private Vector3 Trace(Vector3 origin, Vector3 direction, float tEntry, Vector3 normal, int sample)
    {
        float n = (float)indices[sample];
        Vector3 p = origin + direction * tEntry;
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
        Vector3 lastExit = Vector3.Zero, lastPoint = p;
        for (int bounce = 0; bounce < 12 && weight > 2e-3; bounce++)
        {
            shape.Leave(position, travel, out float chord, out Vector3 outwardNormal);
            Vector3 q = position + travel * chord;
            weight *= Transmittance(sample, chord * radiusMillimeters / shape.Extent);
            float cosInside = Math.Clamp(Vector3.Dot(travel, outwardNormal), 0f, 1f);
            RefractionKind kind = Dielectric.RefractUnit(travel, -outwardNormal, n, 1f, out Vector3 exit);
            if (kind == RefractionKind.Refracted)
            {
                FresnelPower leaving = Dielectric.Fresnel(cosInside, n, 1f);
                result += Environment(q, exit) * (float)(weight * (1d - leaving.Unpolarized));
                weight *= leaving.Unpolarized;
                lastExit = exit;
                lastPoint = q;
            }

            travel = Vector3.Reflect(travel, outwardNormal);
            // Step just off the surface so the next exit search does not find the face we are leaving.
            position = q + travel * 1e-4f;
        }

        // Light still trapped after twelve bounces leaves the way the last escaping share did, rather than vanishing into black.
        if (weight > 2e-3 && lastExit != Vector3.Zero)
        {
            result += Environment(lastPoint, lastExit) * (float)weight;
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
        extinction.InternalTransmittance(wavelength, Math.Max(0d, pathMillimeters), out double t);
        return t;
    }

    /// <summary>Linear RGB of the environment along a ray: the ground at y = -1 with the chosen pattern, otherwise a sky with the key light.</summary>
    private Vector3 Environment(Vector3 origin, Vector3 direction)
    {
        if (direction.Y < -1e-4f)
        {
            float t = (-1f - origin.Y) / direction.Y;
            Vector3 hit = origin + direction * t;
            float distance = MathF.Sqrt(hit.X * hit.X + hit.Z * hit.Z);
            Vector3 tile = Ground(hit.X, hit.Z);
            float lit = ambient + keyIntensity * 0.12f * MathF.Max(0f, Vector3.Dot(Vector3.Normalize(keyPosition - hit), Vector3.UnitY));
            // Contact shadow: the table darkens under and around the solid, most where they touch.
            float contact = 1f - 0.62f * (1f - SmoothStep(0.1f * shape.Extent, 1.7f * shape.Extent, distance));
            float fade = MathF.Exp(-distance * 0.18f);
            return tile * lit * contact * fade + Sky(direction) * (1f - fade);
        }

        return Sky(direction);
    }

    private Vector3 Ground(float x, float z)
    {
        // Half a tile of offset puts a tile centre, not a tile edge, under the solid: an edge there is magnified into a seam.
        float u = x / tileUnits + 0.5f, v = z / tileUnits + 0.5f;
        switch (backdrop)
        {
            case Backdrop.Stripes:
            {
                // Five saturated bands, 16 mm each, so the fringes have colours to split.
                int band = ((int)MathF.Floor(v) % 5 + 5) % 5;
                return band switch
                {
                    0 => new Vector3(0.80f, 0.12f, 0.05f),
                    1 => new Vector3(0.92f, 0.62f, 0.05f),
                    2 => new Vector3(0.05f, 0.55f, 0.20f),
                    3 => new Vector3(0.03f, 0.25f, 0.75f),
                    _ => new Vector3(0.86f, 0.80f, 0.68f),
                };
            }

            case Backdrop.Grid:
            {
                // Millimetre paper: thin lines every 4 mm, heavier every 16 mm.
                float fu = MathF.Abs(u * 4f - MathF.Round(u * 4f)), fv = MathF.Abs(v * 4f - MathF.Round(v * 4f));
                float gu = MathF.Abs(u - MathF.Round(u)), gv = MathF.Abs(v - MathF.Round(v));
                bool heavy = gu < 0.012f || gv < 0.012f;
                bool light = fu < 0.05f || fv < 0.05f;
                return heavy ? new Vector3(0.10f, 0.28f, 0.45f) : light ? new Vector3(0.55f, 0.68f, 0.80f) : new Vector3(0.93f, 0.94f, 0.92f);
            }

            case Backdrop.Paper:
                return new Vector3(0.88f, 0.85f, 0.78f);

            case Backdrop.Night:
                return new Vector3(0.04f, 0.045f, 0.05f);

            default:
            {
                bool dark = ((int)MathF.Floor(u) + (int)MathF.Floor(v) & 1) == 0;
                return dark ? new Vector3(0.30f, 0.31f, 0.32f) : new Vector3(0.86f, 0.80f, 0.68f);
            }
        }
    }

    // The studio: a gradient dome, a key softbox where the lamp stands, a cooler fill opposite, a thin rim from behind.
    private Vector3 Sky(Vector3 direction)
    {
        float t = Math.Clamp(direction.Y * 0.5f + 0.5f, 0f, 1f);
        Vector3 horizon = (backdrop == Backdrop.Night ? new Vector3(0.05f, 0.06f, 0.08f) : new Vector3(0.34f, 0.36f, 0.39f)) * ambient;
        Vector3 zenith = (backdrop == Backdrop.Night ? new Vector3(0.02f, 0.025f, 0.035f) : new Vector3(0.10f, 0.12f, 0.15f)) * ambient;
        Vector3 sky = horizon * (1f - t) + zenith * t;
        sky += new Vector3(1.0f, 0.97f, 0.92f) * keyIntensity * (5f * Softbox(direction, keyDirection, 0.28f) + 12f * Softbox(direction, keyDirection, 0.06f));
        Vector3 fill = Vector3.Normalize(new Vector3(-keyDirection.X, MathF.Max(0.25f, keyDirection.Y * 0.6f), -keyDirection.Z));
        sky += new Vector3(0.80f, 0.86f, 1.0f) * ambient * 1.6f * Softbox(direction, fill, 0.5f);
        sky += new Vector3(0.55f, 0.65f, 0.80f) * 3f * Softbox(direction, RimDirection, 0.12f);
        return sky;
    }

    private static float Softbox(Vector3 d, Vector3 centre, float halfAngle)
    {
        float a = MathF.Acos(Math.Clamp(Vector3.Dot(d, centre), -1f, 1f));
        return 1f - SmoothStep(halfAngle * 0.7f, halfAngle, a);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ACES filmic curve (Narkowicz fit), applied after exposure and before companding.
    private float Tone(float c)
    {
        c *= exposure;
        return Math.Clamp((c * (2.51f * c + 0.03f)) / (c * (2.43f * c + 0.59f) + 0.14f), 0f, 1f);
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
