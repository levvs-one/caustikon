using System.Numerics;

namespace Caustikon.Site.Services;

/// <summary>Cross-sections of the optical elements the bench can hold, as polygons in scene units (1 ≈ the bench's working height).</summary>
public static class BenchShapes
{
    private const int ArcSteps = 28;

    /// <summary>A regular polygon with one corner pointing up.</summary>
    public static Vector2[] Polygon(int sides, float circumradius = 0.55f, float rotationDegrees = 0f)
    {
        sides = Math.Clamp(sides, 3, 24);
        Vector2[] vertices = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            double a = Math.PI / 2 + rotationDegrees * Math.PI / 180 + 2 * Math.PI * i / sides;
            vertices[i] = new Vector2((float)(circumradius * Math.Cos(a)), (float)(circumradius * Math.Sin(a)));
        }

        return vertices;
    }

    /// <summary>
    /// A lens from two spherical surfaces. Curvatures are 1/R in inverse scene units, positive when the surface bulges
    /// outward (convex), negative when it caves in, zero for flat. Any combination is allowed: biconvex, plano-convex,
    /// biconcave, meniscus.
    /// </summary>
    /// <param name="leftCurvature">1/R of the left surface; positive bulges left.</param>
    /// <param name="rightCurvature">1/R of the right surface; positive bulges right.</param>
    /// <param name="centreThickness">Distance between the surfaces on the axis, before sag.</param>
    /// <param name="height">Full aperture height.</param>
    public static Vector2[] Lens(float leftCurvature, float rightCurvature, float centreThickness, float height)
    {
        float half = height / 2f;
        List<Vector2> vertices = [];
        // Right surface, bottom to top; left surface, top to bottom. x offsets are measured from the lens centre.
        for (int i = 0; i <= ArcSteps; i++)
        {
            float y = -half + height * i / ArcSteps;
            vertices.Add(new Vector2(centreThickness / 2f + Sag(rightCurvature, y, half), y));
        }

        for (int i = ArcSteps; i >= 0; i--)
        {
            float y = -half + height * i / ArcSteps;
            vertices.Add(new Vector2(-(centreThickness / 2f + Sag(leftCurvature, y, half)), y));
        }

        return [.. vertices];
    }

    // Surface x relative to the edge of the aperture: a convex surface is farther out at the axis than at the rim.
    private static float Sag(float curvature, float y, float half)
    {
        if (MathF.Abs(curvature) < 1e-4f)
        {
            return 0f;
        }

        float r = 1f / MathF.Abs(curvature);
        float clampedHalf = MathF.Min(half, r * 0.999f);
        float yy = MathF.Min(MathF.Abs(y), clampedHalf);
        float sagAtY = r - MathF.Sqrt(r * r - yy * yy);
        float sagAtRim = r - MathF.Sqrt(r * r - clampedHalf * clampedHalf);
        return MathF.Sign(curvature) * (sagAtRim - sagAtY);
    }

    /// <summary>A 45-45-90 prism with the hypotenuse on the right, as in a retroreflecting or beam-turning prism.</summary>
    public static Vector2[] RightAnglePrism(float leg = 0.9f) =>
        [new Vector2(-leg / 2f, -leg / 2f), new Vector2(leg / 2f, -leg / 2f), new Vector2(-leg / 2f, leg / 2f)];

    /// <summary>A flat plate of the given length and thickness, tilted about its centre.</summary>
    public static Vector2[] Plate(float tiltDegrees = 20f, float thickness = 0.22f, float length = 1.0f)
    {
        double a = tiltDegrees * Math.PI / 180;
        Vector2 along = new((float)Math.Sin(a), (float)Math.Cos(a));
        Vector2 across = new((float)Math.Cos(a), -(float)Math.Sin(a));
        float halfLength = length / 2f, halfThickness = thickness / 2f;
        return
        [
            -along * halfLength - across * halfThickness,
            -along * halfLength + across * halfThickness,
            along * halfLength + across * halfThickness,
            along * halfLength - across * halfThickness,
        ];
    }

    /// <summary>Scales a polygon about its centroid, uniformly by <paramref name="size"/> and vertically by <paramref name="height"/> on top.</summary>
    public static Vector2[] Scaled(Vector2[] vertices, float size, float height)
    {
        if (vertices.Length == 0)
        {
            return vertices;
        }

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 v in vertices)
        {
            centroid += v;
        }

        centroid /= vertices.Length;
        Vector2[] result = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 d = vertices[i] - centroid;
            result[i] = centroid + new Vector2(d.X * size, d.Y * size * height);
        }

        return result;
    }
}
