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
    // The lamp's light on the table: photons traced through the solid at three wavelengths and summed per cell,
    // normalized so that open table reads as one. Shadow where the solid takes photons away, caustic where it focuses them.
    private const int MapSize = 256;
    private const float Region = 3f;
    private readonly Vector3[] photonMap = new Vector3[MapSize * MapSize];
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
        BuildPhotonMap();
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

    /// <summary>
    /// A finer table for the GPU's stochastic spectrum: index, absorption per millimetre and linear-RGB weight at 33
    /// wavelengths from 400 to 700 nm. Weights are scaled so that nine samples with a random offset inside their bands
    /// estimate the same white as the nine fixed samples: summing nine interpolated weights gives (1, 1, 1) on average.
    /// </summary>
    public static (double[] Indices, double[] Alphas, float[] Weights) SpectrumTable(Glass glass)
    {
        const int count = 33;
        double[] indices = new double[count];
        double[] alphas = new double[count];
        Vector3[] linear = new Vector3[count];
        Vector3 total = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            double wavelength = 400d + 300d * i / (count - 1);
            double clamped = Math.Clamp(wavelength, glass.Model.MinimumWavelengthNanometers, glass.Model.MaximumWavelengthNanometers);
            indices[i] = glass.Model.EvaluateNanometers(clamped, out double n) == DispersionStatus.Success ? n : 1.5d;
            if (glass.Extinction is { } extinction)
            {
                double c = Math.Clamp(wavelength, extinction.MinimumWavelengthNanometers, extinction.MaximumWavelengthNanometers);
                extinction.InternalTransmittance(c, 1d, out double t);
                alphas[i] = t > 0d ? -Math.Log(t) : 50d;
            }

            (double r, double g, double b) = GlassColour.Monochromatic(wavelength);
            linear[i] = new Vector3((float)Decompand(r), (float)Decompand(g), (float)Decompand(b));
            // Trapezoid weights: the end points count half.
            total += linear[i] * (i == 0 || i == count - 1 ? 0.5f : 1f);
        }

        // total is the integral in table steps; nine samples spaced 32/9 steps apart each stand for 32/9 steps of it.
        float[] weights = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            weights[i * 3] = linear[i].X / total.X * (32f / 9f);
            weights[i * 3 + 1] = linear[i].Y / total.Y * (32f / 9f);
            weights[i * 3 + 2] = linear[i].Z / total.Z * (32f / 9f);
        }

        return (indices, alphas, weights);
    }

    /// <summary>Where the key light stands, 3.8 units from the solid: azimuth 0 behind the camera, 90 to its left.</summary>
    public static Vector3 KeyLightPosition(double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * Math.PI / 180d;
        double el = Math.Clamp(elevationDegrees, 5d, 85d) * Math.PI / 180d;
        return new Vector3((float)(-Math.Sin(az) * Math.Cos(el)), (float)Math.Sin(el), (float)(-Math.Cos(az) * Math.Cos(el))) * 3.8f;
    }

    /// <summary>Renders rows [<paramref name="firstRow"/>, <paramref name="lastRow"/>) of a <paramref name="size"/>×<paramref name="size"/> image: linear RGB into <paramref name="linear"/> and companded RGBA into <paramref name="rgba"/>.</summary>
    public void RenderRows(Vector3[] linear, byte[] rgba, int size, int firstRow, int lastRow) => RenderRows(linear, rgba, size, size, firstRow, lastRow);

    /// <summary>Renders rows [<paramref name="firstRow"/>, <paramref name="lastRow"/>) of a <paramref name="width"/>×<paramref name="height"/> image.</summary>
    public void RenderRows(Vector3[] linear, byte[] rgba, int width, int height, int firstRow, int lastRow)
    {
        for (int y = firstRow; y < lastRow; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 colour = Shade(x + 0.5d, y + 0.5d, width, height);
                linear[y * width + x] = colour;
                Store(rgba, y * width + x, colour);
            }
        }
    }

    /// <summary>
    /// Supersamples only the pixels whose neighbours differ noticeably: the solid's silhouette, the refracted pattern edges and
    /// the chromatic fringes. Four jittered samples replace one there; smooth areas keep their single sample.
    /// </summary>
    /// <returns>How many pixels were refined.</returns>
    public int RefineEdges(Vector3[] linear, byte[] rgba, int size, int firstRow, int lastRow) => RefineEdges(linear, rgba, size, size, firstRow, lastRow);

    public int RefineEdges(Vector3[] linear, byte[] rgba, int width, int height, int firstRow, int lastRow)
    {
        const float threshold = 0.035f;
        int refined = 0;
        for (int y = Math.Max(1, firstRow); y < Math.Min(height - 1, lastRow); y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int i = y * width + x;
                Vector3 c = linear[i];
                if (Contrast(c, linear[i - 1]) < threshold && Contrast(c, linear[i + 1]) < threshold &&
                    Contrast(c, linear[i - width]) < threshold && Contrast(c, linear[i + width]) < threshold)
                {
                    continue;
                }

                Vector3 sum = Shade(x + 0.25d, y + 0.25d, width, height) + Shade(x + 0.75d, y + 0.25d, width, height)
                    + Shade(x + 0.25d, y + 0.75d, width, height) + Shade(x + 0.75d, y + 0.75d, width, height);
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
    private static readonly Vector3 Eye = new(0f, 0.85f, -5.15f);
    private static readonly Vector3 Forward = Vector3.Normalize(new Vector3(0f, -0.15f, 0f) - Eye);
    private static readonly Vector3 Right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Forward));
    private static readonly Vector3 Up = Vector3.Cross(Forward, Right);
    private static readonly double TanHalf = Math.Tan(30d * Math.PI / 360d);

    private Vector3 Shade(double px, double py, int size) => Shade(px, py, size, size);

    private Vector3 Shade(double px, double py, int width, int height)
    {
        double sx = (px / width * 2d - 1d) * TanHalf * width / height;
        double sy = (1d - py / height * 2d) * TanHalf;
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
        if (weight > 2e-3)
        {
            result += (lastExit != Vector3.Zero ? Environment(lastPoint, lastExit) : Environment(position, travel)) * (float)weight;
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

    // A grid of photons on a plane facing the lamp, three per cell (one per channel), each traced through the solid with
    // the same rules as the camera rays and splatted onto the table. Photons that miss the solid land too and set the scale.
    private void BuildPhotonMap()
    {
        const int grid = 900;
        const float emitter = Region * 1.05f;
        Vector3 travel = -keyDirection;
        Vector3 a = Vector3.Normalize(Vector3.Cross(travel, MathF.Abs(travel.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        Vector3 b = Vector3.Cross(travel, a);
        int[] bands = [6, 4, 2];
        Random random = new(7);
        for (int iy = 0; iy < grid; iy++)
        {
            for (int ix = 0; ix < grid; ix++)
            {
                float u = ((ix + 0.5f + (float)random.NextDouble() - 0.5f) / grid * 2f - 1f) * emitter;
                float v = ((iy + 0.5f + (float)random.NextDouble() - 0.5f) / grid * 2f - 1f) * emitter;
                Vector3 start = -travel * 6f + a * u + b * v;
                for (int channel = 0; channel < 3; channel++)
                {
                    float n = (float)indices[bands[channel]];
                    Vector3 origin = start, direction = travel;
                    double weight = 1d;
                    if (shape.Enter(origin, direction, out float t, out Vector3 normal))
                    {
                        Vector3 p = origin + direction * t;
                        float cosI = Math.Clamp(-Vector3.Dot(direction, normal), 0f, 1f);
                        weight *= 1d - Dielectric.Fresnel(cosI, 1f, n).Unpolarized;
                        if (Dielectric.RefractUnit(direction, normal, 1f, n, out Vector3 inside) != RefractionKind.Refracted)
                        {
                            continue;
                        }

                        Vector3 position = p, going = inside, exit = Vector3.Zero, q = p;
                        bool left = false;
                        for (int bounce = 0; bounce < 8; bounce++)
                        {
                            shape.Leave(position, going, out float chord, out Vector3 outward);
                            q = position + going * chord;
                            weight *= Transmittance(bands[channel], chord * radiusMillimeters / shape.Extent);
                            float cosInside = Math.Clamp(Vector3.Dot(going, outward), 0f, 1f);
                            if (Dielectric.RefractUnit(going, -outward, n, 1f, out Vector3 e) == RefractionKind.Refracted)
                            {
                                weight *= 1d - Dielectric.Fresnel(cosInside, n, 1f).Unpolarized;
                                exit = e;
                                left = true;
                                break;
                            }

                            going = Vector3.Reflect(going, outward);
                            position = q + going * 1e-4f;
                        }

                        if (!left)
                        {
                            continue;
                        }

                        origin = q;
                        direction = exit;
                    }

                    if (direction.Y >= -1e-4f)
                    {
                        continue;
                    }

                    float tg = (-1f - origin.Y) / direction.Y;
                    Vector3 hit = origin + direction * tg;
                    if (MathF.Abs(hit.X) >= Region || MathF.Abs(hit.Z) >= Region)
                    {
                        continue;
                    }

                    // Splat over the 2×2 cells around the landing point, as the GPU's two-pixel points do.
                    float fx = (hit.X / Region * 0.5f + 0.5f) * MapSize - 0.5f, fz = (hit.Z / Region * 0.5f + 0.5f) * MapSize - 0.5f;
                    int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
                    Vector3 energy = (float)weight * 0.25f * (channel == 0 ? Vector3.UnitX : channel == 1 ? Vector3.UnitY : Vector3.UnitZ);
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        for (int dx = 0; dx <= 1; dx++)
                        {
                            int x = x0 + dx, z = z0 + dz;
                            if (x >= 0 && x < MapSize && z >= 0 && z < MapSize)
                            {
                                photonMap[z * MapSize + x] += energy;
                            }
                        }
                    }
                }
            }
        }

        // Open table: photons per cell from the grid's density on the emitter, projected onto the table.
        double perUnitEmitter = (double)grid * grid / (4d * emitter * emitter);
        double cell = 2d * Region / MapSize;
        float expected = (float)(perUnitEmitter * MathF.Abs(keyDirection.Y) * cell * cell);
        for (int i = 0; i < photonMap.Length; i++)
        {
            photonMap[i] /= expected;
        }

        // One 3×3 box pass takes the grain out of the map without moving the caustic.
        Vector3[] smoothed = new Vector3[photonMap.Length];
        for (int z = 0; z < MapSize; z++)
        {
            for (int x = 0; x < MapSize; x++)
            {
                Vector3 sum = Vector3.Zero;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx >= 0 && xx < MapSize && zz >= 0 && zz < MapSize)
                        {
                            sum += photonMap[zz * MapSize + xx];
                            count++;
                        }
                    }
                }

                smoothed[z * MapSize + x] = sum / count;
            }
        }

        Array.Copy(smoothed, photonMap, photonMap.Length);
    }

    private Vector3 Lamp(Vector3 hit)
    {
        if (MathF.Abs(hit.X) >= Region || MathF.Abs(hit.Z) >= Region)
        {
            return Vector3.One;
        }

        // Bilinear read of the map, matching the GPU's linear sampler.
        float fx = (hit.X / Region * 0.5f + 0.5f) * MapSize - 0.5f, fz = (hit.Z / Region * 0.5f + 0.5f) * MapSize - 0.5f;
        int x0 = Math.Clamp((int)MathF.Floor(fx), 0, MapSize - 1), z0 = Math.Clamp((int)MathF.Floor(fz), 0, MapSize - 1);
        int x1 = Math.Min(x0 + 1, MapSize - 1), z1 = Math.Min(z0 + 1, MapSize - 1);
        float tx = Math.Clamp(fx - x0, 0f, 1f), tz = Math.Clamp(fz - z0, 0f, 1f);
        Vector3 top = Vector3.Lerp(photonMap[z0 * MapSize + x0], photonMap[z0 * MapSize + x1], tx);
        Vector3 bottom = Vector3.Lerp(photonMap[z1 * MapSize + x0], photonMap[z1 * MapSize + x1], tx);
        return Vector3.Lerp(top, bottom, tz);
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
            // The lamp's share comes from the photon map: shadow where the solid takes photons away, caustic where it
            // focuses them, open table at one. The room's share is darkened a little under the solid.
            float contact = 1f - 0.35f * (1f - SmoothStep(0.1f * shape.Extent, 1.6f * shape.Extent, distance));
            Vector3 lit = new Vector3(ambient * 0.55f * contact) + Lamp(hit) * (keyIntensity * 0.6f * MathF.Max(0f, keyDirection.Y));
            float fade = MathF.Exp(-distance * 0.18f);
            return tile * lit * fade + Sky(direction) * (1f - fade);
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
                return dark ? new Vector3(0.42f, 0.43f, 0.44f) : new Vector3(0.86f, 0.80f, 0.68f);
            }
        }
    }

    // The studio: a gradient dome, a key softbox where the lamp stands, a cooler fill opposite, a thin rim from behind.
    private Vector3 Sky(Vector3 direction)
    {
        float t = Math.Clamp(direction.Y * 0.5f + 0.5f, 0f, 1f);
        Vector3 horizon = (backdrop == Backdrop.Night ? new Vector3(0.05f, 0.06f, 0.08f) : new Vector3(0.44f, 0.46f, 0.49f)) * ambient;
        Vector3 zenith = (backdrop == Backdrop.Night ? new Vector3(0.02f, 0.025f, 0.035f) : new Vector3(0.30f, 0.33f, 0.38f)) * ambient;
        Vector3 sky = horizon * (1f - t) + zenith * t;
        sky += new Vector3(1.0f, 0.97f, 0.92f) * keyIntensity * (3f * Softbox(direction, keyDirection, 0.45f) + 6f * Softbox(direction, keyDirection, 0.08f));
        Vector3 fill = Vector3.Normalize(new Vector3(-keyDirection.X, MathF.Max(0.25f, keyDirection.Y * 0.6f), -keyDirection.Z));
        sky += new Vector3(0.80f, 0.86f, 1.0f) * ambient * 1.4f * Softbox(direction, fill, 0.7f);
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
