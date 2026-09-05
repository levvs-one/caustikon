using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Caustikon;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: Prism [output.svg]");
    return 1;
}

try
{
    Vector2[] vertices = [new(0, 0), new(1, 0), new(0.5f, MathF.Sqrt(3) / 2)];
    Vector2 origin = new(-0.55f, 0.12f);
    Vector2 incident = new(MathF.Cos(MathF.PI / 12), MathF.Sin(MathF.PI / 12));

    // SCHOTT's relative indices use air as the reference medium, so the surrounding index is 1.
    Sellmeier3 glass = new(1.039612120, 0.006000699,
        0.231792344, 0.0200179144, 1.010469450, 103.56065300, 365, 2325.4);
    (double Wavelength, string Color)[] spectrum =
    [
        (404.7, "#794c9b"), (435.8, "#526caa"), (486.1, "#238994"),
        (546.1, "#497d44"), (587.6, "#af791c"), (656.3, "#b64c3e")
    ];
    List<RaySample> rays = [];

    Console.WriteLine("N-BK7 | apex 60 deg | incoming direction +15 deg from horizontal");
    Console.WriteLine("lambda_nm       n       exit_deg   deviation_deg   first_pass_power");
    foreach ((double wavelength, string color) in spectrum)
    {
        DispersionStatus dispersion = glass.EvaluateNanometers(wavelength, out double index);
        if (dispersion != DispersionStatus.Success)
        {
            throw new InvalidOperationException($"{wavelength} nm: dispersion returned {dispersion}.");
        }

        (Vector2 entry, int entryEdge) = IntersectBoundary(origin, incident, vertices, -1);
        Vector2 entryNormal = OutwardNormal(vertices, entryEdge);
        Vector2 inside = Refract(incident, entryNormal, 1, (float)index, wavelength, "entry");
        (Vector2 exit, int exitEdge) = IntersectBoundary(entry, inside, vertices, entryEdge);
        Vector2 exitNormal = -OutwardNormal(vertices, exitEdge);
        Vector2 outgoing = Refract(inside, exitNormal, (float)index, 1, wavelength, "exit");
        if (entryEdge != 2 || exitEdge != 1)
        {
            throw new InvalidOperationException("The ray did not traverse the two sloping prism faces.");
        }

        float cosEntry = Math.Clamp(-Vector2.Dot(incident, entryNormal), 0, 1);
        float cosExit = Math.Clamp(-Vector2.Dot(inside, exitNormal), 0, 1);
        FresnelPower entryPower = Dielectric.Fresnel(cosEntry, 1, (float)index);
        FresnelPower exitPower = Dielectric.Fresnel(cosExit, (float)index, 1);
        // The first interface polarizes the beam; averaging before the second would lose that state.
        double throughput = 0.5 * ((1 - entryPower.S) * (1 - exitPower.S) +
                                   (1 - entryPower.P) * (1 - exitPower.P));
        double exitDegrees = Math.Atan2(outgoing.Y, outgoing.X) * 180 / Math.PI;
        double deviation = 15 - exitDegrees;
        double prismDeviation = (Math.Acos(cosEntry) +
            Math.Acos(Math.Clamp(Vector2.Dot(outgoing, -exitNormal), -1, 1))) * 180 / Math.PI - 60;
        if (Math.Abs(deviation - prismDeviation) > 0.0001)
        {
            throw new InvalidOperationException("Vector result disagrees with the prism deviation identity.");
        }

        Vector2 end = exit + outgoing * ((1.6f - exit.X) / outgoing.X);
        rays.Add(new(wavelength, color, entry, exit, end, deviation));
        Console.WriteLine($"{wavelength,8:F1}   {index:F8}   {exitDegrees,9:F5}   {deviation,13:F5}   {throughput,16:F6}");
    }

    string output = Path.GetFullPath(args.Length == 1 ? args[0] : "prism.svg");
    DrawDiagram(vertices, origin, rays).Save(output);
    Console.WriteLine($"Wrote {output}");
    return 0;
}
catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static Vector2 Refract(Vector2 incident, Vector2 normal, float nIncident, float nTransmitted,
    double wavelength, string surface)
{
    RefractionKind kind = Dielectric.RefractUnit(new Vector3(incident, 0), new Vector3(normal, 0),
        nIncident, nTransmitted, out Vector3 transmitted);
    if (kind != RefractionKind.Refracted)
    {
        throw new InvalidOperationException($"{wavelength} nm, {surface}: refraction returned {kind}.");
    }

    return new(transmitted.X, transmitted.Y);
}

static (Vector2 Point, int Edge) IntersectBoundary(Vector2 origin, Vector2 direction,
    Vector2[] vertices, int excludedEdge)
{
    float nearest = float.PositiveInfinity;
    int edge = -1;
    for (int i = 0; i < vertices.Length; i++)
    {
        if (i == excludedEdge)
        {
            continue;
        }

        Vector2 segment = vertices[(i + 1) % vertices.Length] - vertices[i];
        float determinant = Cross(direction, segment);
        if (MathF.Abs(determinant) < 1e-7f)
        {
            continue;
        }

        Vector2 offset = vertices[i] - origin;
        float distance = Cross(offset, segment) / determinant;
        float fraction = Cross(offset, direction) / determinant;
        if (distance > 1e-6f && fraction >= 0 && fraction <= 1 && distance < nearest)
        {
            nearest = distance;
            edge = i;
        }
    }

    if (edge < 0)
    {
        throw new InvalidOperationException("The ray missed the prism boundary.");
    }

    return (origin + nearest * direction, edge);
}

static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;

static Vector2 OutwardNormal(Vector2[] vertices, int edge)
{
    Vector2 segment = vertices[(edge + 1) % vertices.Length] - vertices[edge];
    return Vector2.Normalize(new Vector2(segment.Y, -segment.X));
}

static XDocument DrawDiagram(Vector2[] vertices, Vector2 origin, List<RaySample> rays)
{
    XNamespace ns = "http://www.w3.org/2000/svg";
    XElement svg = new(ns + "svg", new XAttribute("width", 1200), new XAttribute("height", 580),
        new XAttribute("viewBox", "0 0 1200 580"), new XAttribute("role", "img"),
        new XAttribute("aria-labelledby", "title description"),
        new XElement(ns + "title", new XAttribute("id", "title"), "Caustikon - N-BK7 prism dispersion"),
        new XElement(ns + "desc", new XAttribute("id", "description"),
            "Six coincident rays at " + string.Join(", ", rays.Select(ray => $"{ray.Wavelength:F1}")) +
            " nanometers enter a 60 degree N-BK7 prism at 45 degrees to the surface normal. " +
            "Angles are to scale; the spectral spread is not exaggerated. " +
            $"{rays[0].Wavelength:F1} nm bends by {rays[0].Deviation:F3} degrees; " +
            $"{rays[^1].Wavelength:F1} nm bends by {rays[^1].Deviation:F3} degrees. " +
            "Colors identify wavelengths, not perceived color or transmitted power."),
        new XElement(ns + "rect", new XAttribute("width", 1200), new XAttribute("height", 580), new XAttribute("fill", "#ffffff")),
        new XElement(ns + "style", "text { fill: #282b30; font-family: Arial, Helvetica, sans-serif; font-size: 22px; }"));

    Vector2[] drawing = [.. vertices.Select(Project)];
    svg.Add(new XElement(ns + "polygon", new XAttribute("points", string.Join(" ", drawing.Select(Point))),
        new XAttribute("fill", "#f2f3f5"), new XAttribute("stroke", "#494e56"), new XAttribute("stroke-width", 1.5)));
    svg.Add(new XElement(ns + "text", new XAttribute("x", 545), new XAttribute("y", 250),
        new XAttribute("text-anchor", "middle"), "N-BK7"));
    Vector2 source = Project(origin);
    Vector2 entry = Project(rays[0].Entry);
    svg.Add(new XElement(ns + "line", new XAttribute("x1", source.X), new XAttribute("y1", source.Y),
        new XAttribute("x2", entry.X), new XAttribute("y2", entry.Y),
        new XAttribute("stroke", "#282b30"), new XAttribute("stroke-width", 2)));
    foreach (RaySample ray in rays)
    {
        Vector2 inside = Project(ray.Exit);
        Vector2 end = Project(ray.End);
        svg.Add(new XElement(ns + "polyline", new XAttribute("points", $"{Point(entry)} {Point(inside)} {Point(end)}"),
            new XAttribute("fill", "none"), new XAttribute("stroke", ray.Color), new XAttribute("stroke-width", 1.5)));
    }

    AddWavelengthLabel(rays[^1], -24);
    AddWavelengthLabel(rays[0], 24);
    return new XDocument(new XDeclaration("1.0", "utf-8", null), svg);

    void AddWavelengthLabel(RaySample ray, float offset)
    {
        Vector2 end = Project(ray.End);
        float labelY = end.Y + offset;
        svg.Add(new XElement(ns + "path",
            new XAttribute("d", $"M {end.X + 8:F3},{end.Y:F3} L {end.X + 32:F3},{labelY:F3}"),
            new XAttribute("fill", "none"), new XAttribute("stroke", ray.Color), new XAttribute("stroke-width", 1)),
            new XElement(ns + "text", new XAttribute("x", end.X + 42), new XAttribute("y", labelY + 7),
                $"{ray.Wavelength:F1} nm"));
    }
}

static Vector2 Project(Vector2 point) => new(330 + point.X * 430, 470 - point.Y * 430);
static string Point(Vector2 point) => $"{point.X:F3},{point.Y:F3}";

internal sealed record RaySample(double Wavelength, string Color, Vector2 Entry,
    Vector2 Exit, Vector2 End, double Deviation);
