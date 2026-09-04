using System.Numerics;
using System.Runtime.InteropServices;

namespace Caustikon;

/// <summary>Allocation-free geometric-optics operations for lossless dielectric interfaces.</summary>
public static class Dielectric
{
    private const float FloatMachineEpsilon = 1.1920929e-7f;

    /// <summary>
    /// Maximum accepted absolute error in a vector's squared length. The tolerance is tight enough
    /// to reject visibly non-unit input while accommodating ordinary single-precision normalization.
    /// </summary>
    public const float UnitLengthSquaredTolerance = 8f * FloatMachineEpsilon;

    /// <summary>Refracts a unit direction at a dielectric interface.</summary>
    /// <remarks>
    /// <paramref name="incidentUnit"/> travels toward the interface and <paramref name="normalUnit"/>
    /// points back into the incident medium, so their dot product must be non-positive. Invalid input
    /// and total internal reflection produce <see cref="Vector3.Zero"/>. The discriminant is classified
    /// with a boundary of eight binary32 machine epsilons scaled by transmitted sine squared.
    /// </remarks>
    public static RefractionKind RefractUnit(
        Vector3 incidentUnit,
        Vector3 normalUnit,
        float nIncident,
        float nTransmitted,
        out Vector3 refractedUnit)
    {
        if (!IsPositiveFinite(nIncident) || !IsPositiveFinite(nTransmitted))
        {
            refractedUnit = Vector3.Zero;
            return RefractionKind.InvalidInput;
        }

        return RefractCore(incidentUnit, normalUnit, (double)nIncident / nTransmitted, out refractedUnit);
    }

    /// <summary>Refracts a batch with one pair of refractive indices shared by every lane.</summary>
    public static void RefractUnit(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        float nIncident,
        float nTransmitted,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        ValidateRefractionShape(incidentUnits, normalUnits, refractedUnits, kinds);
        ValidateRefractionOverlap(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        double eta = (double)nIncident / nTransmitted;
        for (int i = 0; i < incidentUnits.Length; i++)
        {
            kinds[i] = RefractCore(incidentUnits[i], normalUnits[i], eta, out refractedUnits[i]);
        }
    }

    /// <summary>Refracts a batch whose refractive indices vary per lane.</summary>
    public static void RefractUnit(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        ValidateRefractionShape(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfLengthDiffers(nIncidents.Length, incidentUnits.Length, nameof(nIncidents));
        ThrowIfLengthDiffers(nTransmitteds.Length, incidentUnits.Length, nameof(nTransmitteds));
        ValidateRefractionOverlap(incidentUnits, normalUnits, refractedUnits, kinds);
        ThrowIfOverlap(nIncidents, refractedUnits, nameof(refractedUnits));
        ThrowIfOverlap(nTransmitteds, refractedUnits, nameof(refractedUnits));
        ThrowIfOverlap(nIncidents, kinds, nameof(kinds));
        ThrowIfOverlap(nTransmitteds, kinds, nameof(kinds));

        for (int i = 0; i < incidentUnits.Length; i++)
        {
            kinds[i] = RefractUnit(
                incidentUnits[i],
                normalUnits[i],
                nIncidents[i],
                nTransmitteds[i],
                out refractedUnits[i]);
        }
    }

    /// <summary>Computes exact S, P, and unpolarized power reflectance.</summary>
    /// <param name="cosIncident">Cosine of the angle to the interface normal, in the inclusive range [0, 1].</param>
    public static FresnelPower Fresnel(float cosIncident, float nIncident, float nTransmitted)
    {
        ThrowIfInvalidCosine(cosIncident, nameof(cosIncident));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        if (nIncident == nTransmitted)
        {
            return default;
        }

        double eta = (double)nIncident / nTransmitted;
        double sinIncidentSquared = (1d - cosIncident) * (1d + cosIncident);
        double sinTransmittedSquared = eta * eta * sinIncidentSquared;
        double transmittedCosineSquared = 1d - sinTransmittedSquared;
        double criticalTolerance = CriticalTolerance(sinTransmittedSquared);
        if (eta > 1d && transmittedCosineSquared <= criticalTolerance)
        {
            return new FresnelPower(1f, 1f);
        }

        double cosTransmitted = Math.Sqrt(transmittedCosineSquared);
        double sIncident = (double)nIncident * cosIncident;
        double sTransmitted = nTransmitted * cosTransmitted;
        double pIncident = (double)nTransmitted * cosIncident;
        double pTransmitted = nIncident * cosTransmitted;
        double s = (sIncident - sTransmitted) / (sIncident + sTransmitted);
        double p = (pIncident - pTransmitted) / (pIncident + pTransmitted);
        return new FresnelPower((float)(s * s), (float)(p * p));
    }

    /// <summary>Computes exact Fresnel power for a batch with shared refractive indices.</summary>
    public static void Fresnel(
        ReadOnlySpan<float> cosIncidents,
        float nIncident,
        float nTransmitted,
        Span<FresnelPower> powers)
    {
        ThrowIfLengthDiffers(powers.Length, cosIncidents.Length, nameof(powers));
        ThrowIfOverlap(cosIncidents, powers, nameof(powers));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            powers[i] = Fresnel(cosIncidents[i], nIncident, nTransmitted);
        }
    }

    /// <summary>Computes exact Fresnel power for a batch whose refractive indices vary per lane.</summary>
    public static void Fresnel(
        ReadOnlySpan<float> cosIncidents,
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<FresnelPower> powers)
    {
        int length = cosIncidents.Length;
        ThrowIfLengthDiffers(nIncidents.Length, length, nameof(nIncidents));
        ThrowIfLengthDiffers(nTransmitteds.Length, length, nameof(nTransmitteds));
        ThrowIfLengthDiffers(powers.Length, length, nameof(powers));
        ThrowIfOverlap(cosIncidents, powers, nameof(powers));
        ThrowIfOverlap(nIncidents, powers, nameof(powers));
        ThrowIfOverlap(nTransmitteds, powers, nameof(powers));

        for (int i = 0; i < length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
            ThrowIfNotPositiveFinite(nIncidents[i], nameof(nIncidents));
            ThrowIfNotPositiveFinite(nTransmitteds[i], nameof(nTransmitteds));
        }

        for (int i = 0; i < length; i++)
        {
            powers[i] = Fresnel(cosIncidents[i], nIncidents[i], nTransmitteds[i]);
        }
    }

    /// <summary>Computes power reflectance at normal incidence.</summary>
    public static float NormalReflectance(float nIncident, float nTransmitted)
    {
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));
        double amplitude = ((double)nIncident - nTransmitted) / ((double)nIncident + nTransmitted);
        return (float)(amplitude * amplitude);
    }

    /// <summary>Computes normal-incidence power reflectance for a batch.</summary>
    public static void NormalReflectance(
        ReadOnlySpan<float> nIncidents,
        ReadOnlySpan<float> nTransmitteds,
        Span<float> reflectances)
    {
        int length = nIncidents.Length;
        ThrowIfLengthDiffers(nTransmitteds.Length, length, nameof(nTransmitteds));
        ThrowIfLengthDiffers(reflectances.Length, length, nameof(reflectances));
        ValidateNoPartialOverlap(nIncidents, reflectances, nameof(reflectances));
        ValidateNoPartialOverlap(nTransmitteds, reflectances, nameof(reflectances));

        for (int i = 0; i < length; i++)
        {
            ThrowIfNotPositiveFinite(nIncidents[i], nameof(nIncidents));
            ThrowIfNotPositiveFinite(nTransmitteds[i], nameof(nTransmitteds));
        }

        for (int i = 0; i < length; i++)
        {
            reflectances[i] = NormalReflectance(nIncidents[i], nTransmitteds[i]);
        }
    }

    /// <summary>Applies Schlick's approximation using a supplied normal-incidence reflectance.</summary>
    public static float Schlick(float cosIncident, float normalReflectance)
    {
        ThrowIfInvalidCosine(cosIncident, nameof(cosIncident));
        if (!float.IsFinite(normalReflectance) || normalReflectance < 0f || normalReflectance > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalReflectance), normalReflectance, "Reflectance must be finite and in [0, 1].");
        }

        float complement = 1f - cosIncident;
        float complementSquared = complement * complement;
        float complementFifth = complementSquared * complementSquared * complement;
        return MathF.FusedMultiplyAdd(1f - normalReflectance, complementFifth, normalReflectance);
    }

    /// <summary>Applies Schlick's approximation using reflectance derived from the two indices.</summary>
    public static float Schlick(float cosIncident, float nIncident, float nTransmitted) =>
        Schlick(cosIncident, NormalReflectance(nIncident, nTransmitted));

    /// <summary>Applies Schlick's approximation to a batch using one shared normal reflectance.</summary>
    public static void Schlick(
        ReadOnlySpan<float> cosIncidents,
        float normalReflectance,
        Span<float> reflectances)
    {
        ThrowIfLengthDiffers(reflectances.Length, cosIncidents.Length, nameof(reflectances));
        ValidateNoPartialOverlap(cosIncidents, reflectances, nameof(reflectances));
        if (!float.IsFinite(normalReflectance) || normalReflectance < 0f || normalReflectance > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalReflectance), normalReflectance, "Reflectance must be finite and in [0, 1].");
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            reflectances[i] = Schlick(cosIncidents[i], normalReflectance);
        }
    }

    /// <summary>Applies Schlick's approximation to a batch using shared refractive indices.</summary>
    public static void Schlick(
        ReadOnlySpan<float> cosIncidents,
        float nIncident,
        float nTransmitted,
        Span<float> reflectances)
    {
        ThrowIfLengthDiffers(reflectances.Length, cosIncidents.Length, nameof(reflectances));
        ValidateNoPartialOverlap(cosIncidents, reflectances, nameof(reflectances));
        ThrowIfNotPositiveFinite(nIncident, nameof(nIncident));
        ThrowIfNotPositiveFinite(nTransmitted, nameof(nTransmitted));

        for (int i = 0; i < cosIncidents.Length; i++)
        {
            ThrowIfInvalidCosine(cosIncidents[i], nameof(cosIncidents));
        }

        float normalReflectance = NormalReflectance(nIncident, nTransmitted);
        for (int i = 0; i < cosIncidents.Length; i++)
        {
            reflectances[i] = Schlick(cosIncidents[i], normalReflectance);
        }
    }

    private static RefractionKind RefractCore(Vector3 incident, Vector3 normal, double eta, out Vector3 transmitted)
    {
        transmitted = Vector3.Zero;
        if (!IsUnitVector(incident) || !IsUnitVector(normal))
        {
            return RefractionKind.InvalidInput;
        }

        double dot = (double)incident.X * normal.X + (double)incident.Y * normal.Y + (double)incident.Z * normal.Z;
        if (dot > 0d)
        {
            return RefractionKind.InvalidInput;
        }

        if (eta == 1d)
        {
            transmitted = incident;
            return RefractionKind.Refracted;
        }

        // Cross products preserve small tangential components that 1 - cos^2 loses near normal incidence.
        double crossX = (double)incident.Y * normal.Z - (double)incident.Z * normal.Y;
        double crossY = (double)incident.Z * normal.X - (double)incident.X * normal.Z;
        double crossZ = (double)incident.X * normal.Y - (double)incident.Y * normal.X;
        double normalSquared = (double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z;
        double incidentSquared = (double)incident.X * incident.X + (double)incident.Y * incident.Y + (double)incident.Z * incident.Z;
        // Correct only the accepted unit-length rounding error, which matters at grazing incidence.
        double scale = eta / (normalSquared * Math.Sqrt(incidentSquared));
        double tangentX = (normal.Y * crossZ - normal.Z * crossY) * scale;
        double tangentY = (normal.Z * crossX - normal.X * crossZ) * scale;
        double tangentZ = (normal.X * crossY - normal.Y * crossX) * scale;
        double sinTransmittedSquared = tangentX * tangentX + tangentY * tangentY + tangentZ * tangentZ;
        double discriminant = 1d - sinTransmittedSquared;
        double tolerance = CriticalTolerance(sinTransmittedSquared);
        if (discriminant < -tolerance)
        {
            return RefractionKind.TotalInternalReflection;
        }

        bool critical = eta > 1d && Math.Abs(discriminant) <= tolerance;
        if (critical)
        {
            // Snapping the boundary also snaps the tangent to unit length.
            double tangentScale = 1d / Math.Sqrt(sinTransmittedSquared);
            transmitted = new Vector3((float)(tangentX * tangentScale), (float)(tangentY * tangentScale), (float)(tangentZ * tangentScale));
            return RefractionKind.CriticalAngle;
        }

        double normalScale = Math.Sqrt(discriminant / normalSquared);
        transmitted = new Vector3(
            (float)(tangentX - normalScale * normal.X),
            (float)(tangentY - normalScale * normal.Y),
            (float)(tangentZ - normalScale * normal.Z));
        return RefractionKind.Refracted;
    }

    private static double CriticalTolerance(double sinTransmittedSquared) =>
        8d * FloatMachineEpsilon * Math.Max(1d, sinTransmittedSquared);

    private static bool IsUnitVector(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            return false;
        }

        float lengthSquared = Vector3.Dot(value, value);
        return MathF.Abs(lengthSquared - 1f) <= UnitLengthSquaredTolerance;
    }

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;

    private static void ValidateRefractionShape(
        ReadOnlySpan<Vector3> incidentUnits,
        ReadOnlySpan<Vector3> normalUnits,
        Span<Vector3> refractedUnits,
        Span<RefractionKind> kinds)
    {
        int length = incidentUnits.Length;
        ThrowIfLengthDiffers(normalUnits.Length, length, nameof(normalUnits));
        ThrowIfLengthDiffers(refractedUnits.Length, length, nameof(refractedUnits));
        ThrowIfLengthDiffers(kinds.Length, length, nameof(kinds));
    }

    private static void ThrowIfLengthDiffers(int actual, int expected, string parameterName)
    {
        if (actual != expected)
        {
            throw new ArgumentException("All spans must have the same length.", parameterName);
        }
    }

    private static void ThrowIfInvalidCosine(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Cosine must be finite and in [0, 1].");
        }
    }

    private static void ThrowIfNotPositiveFinite(float value, string parameterName)
    {
        if (!IsPositiveFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Refractive index must be finite and greater than zero.");
        }
    }

    private static void ValidateNoPartialOverlap<T>(ReadOnlySpan<T> source, Span<T> destination, string parameterName)
    {
        if (source.Overlaps(destination, out int elementOffset) && elementOffset != 0)
        {
            throw new ArgumentException("Source and destination may be identical, but must not partially overlap.", parameterName);
        }
    }

    private static void ValidateRefractionOverlap(
        ReadOnlySpan<Vector3> incidents, ReadOnlySpan<Vector3> normals,
        Span<Vector3> transmitted, Span<RefractionKind> kinds)
    {
        ValidateNoPartialOverlap(incidents, transmitted, "refractedUnits");
        ThrowIfOverlap(normals, transmitted, "refractedUnits");
        ThrowIfOverlap(incidents, kinds, nameof(kinds));
        ThrowIfOverlap(normals, kinds, nameof(kinds));
        ThrowIfOverlap((ReadOnlySpan<Vector3>)transmitted, kinds, nameof(kinds));
    }

    private static void ThrowIfOverlap<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, string parameterName)
        where TSource : struct
        where TDestination : struct
    {
        if (MemoryMarshal.AsBytes(source).Overlaps(MemoryMarshal.AsBytes(destination)))
        {
            throw new ArgumentException("These buffers must not overlap.", parameterName);
        }
    }
}
