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
        rays.Add(new(wavelength, color, index, entry, exit, end, deviation));
        Console.WriteLine($"{wavelength,8:F1}   {index:F8}   {exitDegrees,9:F5}   {deviation,13:F5}   {throughput,16:F6}");
    }

    string output = Path.GetFullPath(args.Length == 1 ? args[0] : "prism.svg");
    DrawPlate(vertices, origin, rays).Save(output);
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

static XDocument DrawPlate(Vector2[] vertices, Vector2 origin, List<RaySample> rays)
{
    XNamespace ns = "http://www.w3.org/2000/svg";
    XElement svg = new(ns + "svg", new XAttribute("width", 1280), new XAttribute("height", 840),
        new XAttribute("viewBox", "0 0 1280 840"), new XAttribute("role", "img"),
        new XAttribute("aria-labelledby", "title description"),
        new XElement(ns + "title", new XAttribute("id", "title"), "Caustikon - N-BK7 prism dispersion"),
        new XElement(ns + "desc", new XAttribute("id", "description"),
            "Six coincident incident rays refract through a 60 degree N-BK7 prism. " +
            "The drawing preserves geometry. A separate chart resolves their calculated angular deviations."),
        new XElement(ns + "rect", new XAttribute("width", 1280), new XAttribute("height", 840), new XAttribute("fill", "#faf9f5")),
        new XElement(ns + "style", "text { fill: #252824; font-family: 'Consolas', 'Liberation Mono', monospace; font-size: 13px; } " +
            ".title { font-family: 'Georgia', 'Liberation Serif', serif; font-size: 42px; } " +
            ".muted { fill: #666b62; } .small { font-size: 11px; }"));

    AddText(48, 42, "CAUSTIKON  /  GEOMETRIC OPTICS", "small");
    AddText(48, 98, "One prism. Six wavelengths.", "title");
    AddText(48, 130, "N-BK7  |  60 deg apex  |  45 deg incidence  |  air / glass / air", "muted");
    AddText(1116, 42, "PLATE 01", "small");
    AddLine(48, 152, 1232, 152, "#d5d7ce");

    Vector2[] drawing = [.. vertices.Select(Project)];
    svg.Add(new XElement(ns + "polygon", new XAttribute("points", string.Join(" ", drawing.Select(Point))),
        new XAttribute("fill", "#eceee5"), new XAttribute("stroke", "#464b41"), new XAttribute("stroke-width", 1.5)));
    AddText(402, 277, "60 deg", "small");
    AddText(395, 340, "N-BK7");
    AddText(355, 363, "homogeneous glass", "small muted");
    AddText(69, 423, "coincident input", "small muted");
    AddText(69, 443, "+15 deg", "small muted");
    Vector2 source = Project(origin);
    Vector2 entry = Project(rays[0].Entry);
    AddLine(source.X, source.Y, entry.X, entry.Y, "#252824", 1.6);
    foreach (RaySample ray in rays)
    {
        Vector2 inside = Project(ray.Exit);
        Vector2 end = Project(ray.End);
        svg.Add(new XElement(ns + "polyline", new XAttribute("points", $"{Point(entry)} {Point(inside)} {Point(end)}"),
            new XAttribute("fill", "none"), new XAttribute("stroke", ray.Color), new XAttribute("stroke-width", 1.25)));
    }

    AddText(60, 560, "Ray geometry is to scale. The narrow fan is physical; colors identify wavelengths.", "small muted");
    AddText(934, 192, "CALCULATED OUTPUT", "small");
    AddText(934, 218, "nm", "small muted");
    AddText(1020, 218, "n", "small muted");
    AddText(1130, 218, "delta", "small muted");
    for (int i = 0; i < rays.Count; i++)
    {
        RaySample ray = rays[i];
        double y = 250 + i * 35;
        AddLine(912, y - 5, 925, y - 5, ray.Color, 3);
        AddText(934, y, $"{ray.Wavelength:F1}");
        AddText(1020, y, $"{ray.Index:F5}");
        AddText(1130, y, $"{ray.Deviation:F3}");
    }

    AddText(934, 493, "delta = angular deviation", "small muted");
    AddText(934, 514, "from the incident ray, deg", "small muted");
    AddLine(48, 582, 1232, 582, "#d5d7ce");
    AddText(48, 612, "DISPERSION RESOLVED", "small");
    double minimum = Math.Floor(rays.Min(ray => ray.Deviation) * 2) / 2;
    double maximum = Math.Ceiling(rays.Max(ray => ray.Deviation) * 2) / 2;
    for (int tick = 0; tick <= 4; tick++)
    {
        double value = minimum + (maximum - minimum) * tick / 4;
        double y = 744 - tick * 26;
        AddLine(105, y, 868, y, "#d5d7ce");
        AddText(48, y + 4, $"{value:F2}", "small muted");
    }

    string points = string.Join(" ", rays.Select(ray => $"{ChartX(ray.Wavelength):F3},{ChartY(ray.Deviation):F3}"));
    svg.Add(new XElement(ns + "polyline", new XAttribute("points", points), new XAttribute("fill", "none"),
        new XAttribute("stroke", "#777d71"), new XAttribute("stroke-width", 1)));
    foreach (RaySample ray in rays)
    {
        svg.Add(new XElement(ns + "circle", new XAttribute("cx", ChartX(ray.Wavelength)), new XAttribute("cy", ChartY(ray.Deviation)),
            new XAttribute("r", 4), new XAttribute("fill", ray.Color)));
        AddText(ChartX(ray.Wavelength) - 18, 768, $"{ray.Wavelength:F1}", "small muted");
    }

    AddText(934, 639, "degrees / nanometers", "small muted");
    AddText(934, 682, "Sellmeier dispersion", "small");
    AddText(934, 703, "Vector Snell refraction", "small");
    AddText(934, 724, "Exact dielectric Fresnel", "small");
    AddLine(48, 792, 1232, 792, "#d5d7ce");
    AddText(48, 818, "Source: SCHOTT N-BK7 datasheet, 01-Dec-2023. No coatings, bulk absorption or secondary reflections.", "small muted");
    return new XDocument(new XDeclaration("1.0", "utf-8", null), svg);

    double ChartX(double wavelength) => 105 + (wavelength - 400) / 260 * 763;
    double ChartY(double deviation) => 744 - (deviation - minimum) / (maximum - minimum) * 104;
    void AddText(double x, double y, string content, string? style = null) =>
        svg.Add(new XElement(ns + "text", new XAttribute("x", x), new XAttribute("y", y),
            style is null ? null : new XAttribute("class", style), content));
    void AddLine(double x1, double y1, double x2, double y2, string color, double width = 1) =>
        svg.Add(new XElement(ns + "line", new XAttribute("x1", x1), new XAttribute("y1", y1),
            new XAttribute("x2", x2), new XAttribute("y2", y2), new XAttribute("stroke", color), new XAttribute("stroke-width", width)));
}

static Vector2 Project(Vector2 point) => new(255 + point.X * 330, 526 - point.Y * 330);
static string Point(Vector2 point) => $"{point.X:F3},{point.Y:F3}";

internal sealed record RaySample(double Wavelength, string Color, double Index, Vector2 Entry,
    Vector2 Exit, Vector2 End, double Deviation);
