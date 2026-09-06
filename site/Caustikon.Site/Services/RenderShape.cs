using System.Numerics;

namespace Caustikon.Site.Services;

/// <summary>A closed solid the renderer can trace: a sphere or a convex polyhedron given by its face planes.</summary>
/// <remarks>
/// Every shape is centred so that its lowest point touches the ground at y = -1 and its extent is about one unit, so the
/// camera and the light frame it the same way. The caller supplies the size in millimetres separately; it scales path lengths only.
/// </remarks>
public abstract class RenderShape
{
    /// <summary>Half of the shape's extent in scene units; a sphere's radius, a polyhedron's circumradius.</summary>
    public abstract float Extent { get; }

    /// <summary>First intersection of a ray starting outside; returns false on a miss.</summary>
    public abstract bool Enter(Vector3 origin, Vector3 direction, out float t, out Vector3 normal);

    /// <summary>Where a ray starting inside leaves, and the outward normal there.</summary>
    public abstract void Leave(Vector3 origin, Vector3 direction, out float t, out Vector3 normal);

    /// <summary>The solid as numbers for a shader: a unit sphere at the origin, or a centre and the face planes in local space.</summary>
    public abstract void Describe(out bool sphere, out Vector3 centre, out float extent, out IReadOnlyList<(Vector3 Normal, float Distance)> planes);

    public static RenderShape Sphere() => new SphereShape();

    public static RenderShape Cube() => ConvexShape.Prism(4, 0.62f, 0.62f, 45f);

    public static RenderShape Prism(int sides)
    {
        int n = Math.Clamp(sides, 3, 12);
        // Cross-section circumradius 0.85 whatever the face count, so a triangle and a dodecagon fill the frame alike.
        return ConvexShape.Prism(n, 0.85f * MathF.Cos(MathF.PI / n), 0.6f, 90f / n);
    }

    public static RenderShape Octahedron()
    {
        float k = 1f / MathF.Sqrt(3f);
        List<(Vector3 Normal, float Distance)> planes = [];
        foreach (float x in new[] { -1f, 1f })
        {
            foreach (float y in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -1f, 1f })
                {
                    planes.Add((new Vector3(x, y, z) * k, 0.85f * k));
                }
            }
        }

        // Vertices at ±0.85 on the axes; the bottom vertex touches the ground.
        return new ConvexShape(planes, 0.85f, new Vector3(0f, 0f, 0f), 20f);
    }

    public static RenderShape Dodecahedron()
    {
        float phi = (1f + MathF.Sqrt(5f)) / 2f;
        List<(Vector3 Normal, float Distance)> planes = [];
        foreach (float a in new[] { -1f, 1f })
        {
            foreach (float b in new[] { -phi, phi })
            {
                planes.Add((Vector3.Normalize(new Vector3(0f, a, b)), 0f));
                planes.Add((Vector3.Normalize(new Vector3(a, b, 0f)), 0f));
                planes.Add((Vector3.Normalize(new Vector3(b, 0f, a)), 0f));
            }
        }

        // Inradius over circumradius for the dodecahedron is 0.7947; circumradius 0.85 keeps the top corner in frame.
        return new ConvexShape(planes.Select(p => (p.Normal, 0.85f * 0.7947f)).ToList(), 0.85f, Vector3.Zero, 0f);
    }

    public static RenderShape Icosahedron()
    {
        float phi = (1f + MathF.Sqrt(5f)) / 2f;
        List<(Vector3 Normal, float Distance)> planes = [];
        foreach (float x in new[] { -1f, 1f })
        {
            foreach (float y in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -1f, 1f })
                {
                    planes.Add((Vector3.Normalize(new Vector3(x, y, z)), 0f));
                }
            }
        }

        foreach (float a in new[] { -1f / phi, 1f / phi })
        {
            foreach (float b in new[] { -phi, phi })
            {
                planes.Add((Vector3.Normalize(new Vector3(0f, a, b)), 0f));
                planes.Add((Vector3.Normalize(new Vector3(a, b, 0f)), 0f));
                planes.Add((Vector3.Normalize(new Vector3(b, 0f, a)), 0f));
            }
        }

        return new ConvexShape(planes.Select(p => (p.Normal, 0.85f * 0.7947f)).ToList(), 0.85f, Vector3.Zero, 0f);
    }

    private sealed class SphereShape : RenderShape
    {
        public override float Extent => 1f;

        public override bool Enter(Vector3 origin, Vector3 direction, out float t, out Vector3 normal)
        {
            float b = Vector3.Dot(origin, direction);
            float c = Vector3.Dot(origin, origin) - 1f;
            float discriminant = b * b - c;
            if (discriminant < 0f)
            {
                t = 0f;
                normal = Vector3.Zero;
                return false;
            }

            t = -b - MathF.Sqrt(discriminant);
            normal = Vector3.Normalize(origin + direction * t);
            return t > 1e-4f;
        }

        public override void Leave(Vector3 origin, Vector3 direction, out float t, out Vector3 normal)
        {
            t = -2f * Vector3.Dot(origin, direction);
            normal = Vector3.Normalize(origin + direction * t);
        }

        public override void Describe(out bool sphere, out Vector3 centre, out float extent, out IReadOnlyList<(Vector3 Normal, float Distance)> planes)
        {
            sphere = true;
            centre = Vector3.Zero;
            extent = 1f;
            planes = [];
        }
    }

    private sealed class ConvexShape : RenderShape
    {
        private readonly (Vector3 Normal, float Distance)[] planes;
        private readonly Vector3 centre;

        public ConvexShape(IReadOnlyList<(Vector3 Normal, float Distance)> planes, float extent, Vector3 centre, float yawDegrees)
        {
            float yaw = yawDegrees * MathF.PI / 180f;
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            this.planes = planes.Select(p => (new Vector3(p.Normal.X * c - p.Normal.Z * s, p.Normal.Y, p.Normal.X * s + p.Normal.Z * c), p.Distance)).ToArray();
            this.centre = centre;
            Extent = extent;
            // Rest the solid on the ground: find its lowest point along -y by probing the planes.
            float lowest = float.PositiveInfinity;
            foreach ((Vector3 normal, float distance) in this.planes)
            {
                if (normal.Y < -1e-3f)
                {
                    lowest = MathF.Min(lowest, -distance / normal.Y);
                }
            }

            // For a polyhedron the lowest point is a vertex, not a face centre; extent below the centre is at most the
            // circumradius, so use that when the faces alone would leave it floating.
            float drop = float.IsFinite(lowest) ? MathF.Min(lowest, extent) : extent;
            this.centre = new Vector3(centre.X, -1f + drop, centre.Z);
        }

        public override float Extent { get; }

        public override void Describe(out bool sphere, out Vector3 centre, out float extent, out IReadOnlyList<(Vector3 Normal, float Distance)> planes)
        {
            sphere = false;
            centre = this.centre;
            extent = Extent;
            planes = this.planes;
        }

        /// <summary>A right prism with a regular <paramref name="sides"/>-gon cross-section standing on the ground.</summary>
        public static ConvexShape Prism(int sides, float apothem, float halfHeight, float yawDegrees)
        {
            List<(Vector3 Normal, float Distance)> planes = [(Vector3.UnitY, halfHeight), (-Vector3.UnitY, halfHeight)];
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * MathF.PI * i / sides;
                planes.Add((new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)), apothem));
            }

            float circumradius = MathF.Sqrt(halfHeight * halfHeight + apothem * apothem / (MathF.Cos(MathF.PI / sides) * MathF.Cos(MathF.PI / sides)));
            return new ConvexShape(planes, circumradius, Vector3.Zero, yawDegrees);
        }

        public override bool Enter(Vector3 origin, Vector3 direction, out float t, out Vector3 normal)
        {
            Vector3 o = origin - centre;
            float tEntry = float.NegativeInfinity, tExit = float.PositiveInfinity;
            normal = Vector3.Zero;
            foreach ((Vector3 n, float d) in planes)
            {
                float denominator = Vector3.Dot(n, direction);
                float distance = d - Vector3.Dot(n, o);
                if (MathF.Abs(denominator) < 1e-7f)
                {
                    if (distance < 0f)
                    {
                        t = 0f;
                        return false;
                    }

                    continue;
                }

                float tPlane = distance / denominator;
                if (denominator < 0f)
                {
                    if (tPlane > tEntry)
                    {
                        tEntry = tPlane;
                        normal = n;
                    }
                }
                else
                {
                    tExit = MathF.Min(tExit, tPlane);
                }
            }

            t = tEntry;
            return tEntry > 1e-4f && tEntry <= tExit;
        }

        public override void Leave(Vector3 origin, Vector3 direction, out float t, out Vector3 normal)
        {
            Vector3 o = origin - centre;
            t = float.PositiveInfinity;
            normal = Vector3.UnitY;
            foreach ((Vector3 n, float d) in planes)
            {
                float denominator = Vector3.Dot(n, direction);
                if (denominator <= 1e-7f)
                {
                    continue;
                }

                float tPlane = (d - Vector3.Dot(n, o)) / denominator;
                if (tPlane < t)
                {
                    t = tPlane;
                    normal = n;
                }
            }

            if (!float.IsFinite(t) || t < 0f)
            {
                t = 0f;
            }
        }
    }
}
