# Conventions and contracts

This document defines how Caustikon interprets directions, refractive indices, wavelengths, boundary cases, and batch buffers. These rules are part of the public API contract.

## Media and refractive indices

Caustikon names the two sides of an interface by the ray's direction of travel:

- `nIncident` is the phase refractive index of the medium the ray is leaving.
- `nTransmitted` is the phase refractive index of the medium the ray is entering.
- Both indices must be finite and greater than zero.
- Both values must refer to comparable wavelength, temperature, pressure, and material conditions.
- Both indices must use the same reference medium. Do not mix a catalogue index relative to air with an absolute index relative to vacuum.

The refraction ratio is `eta = nIncident / nTransmitted`. The library never swaps the two media or changes the normal on the caller's behalf.

The supported media are homogeneous, isotropic, and nonabsorbing dielectrics. Refractive index is a real phase index.

## Direction convention

`Dielectric.RefractUnit` accepts two `System.Numerics.Vector3` values:

- `incidentUnit` points along ray travel toward the interface.
- `normalUnit` points away from the interface into the incident medium.
- A correctly oriented pair has `dot(incidentUnit, normalUnit) <= 0`.
- A successful output points away from the interface into the transmitted medium.

Both input vectors must be finite and satisfy `abs(lengthSquared - 1) <= Dielectric.UnitLengthSquaredTolerance`. The tolerance is eight times the spacing between `1f` and its next representable value, approximately `9.54e-7`. The orientation check rejects any positive dot product. Vectors outside the accepted length tolerance are invalid. After validation, the tangential projection compensates for residual length error within the accepted tolerance so it cannot create an impossible sine at grazing incidence.

For a conventional horizontal interface, a ray traveling downward can use `incidentUnit.Y < 0` and `normalUnit = Vector3.UnitY`.

## Refraction boundary classification

The calculation separates the incident vector into its tangential and normal components. For input vectors `I` and `N`:

```text
I_tangent = cross(N, cross(I, N)) / (dot(N, N) * length(I))
eta       = nIncident / nTransmitted
sin2T     = dot(eta * I_tangent, eta * I_tangent)
k         = 1 - sin2T
tol       = 8 * binary32-epsilon * max(1, sin2T)
```

Here `binary32-epsilon` is `2^-23`, the spacing immediately above `1f`, not .NET's `float.Epsilon`, which is the smallest positive subnormal value. The tangential projection and index ratio use `double` intermediates. This avoids cancellation at normal incidence and overflow from ratios of finite positive `float` indices.

Equal refractive indices return the original incident vector with `Refracted`, including at grazing incidence. Entering a higher-index medium also returns `Refracted`; it has no critical-angle boundary. For `nIncident > nTransmitted`, `RefractUnit` classifies the result as follows:

| Condition | `RefractionKind` | Output |
| --- | --- | --- |
| `k > tol` | `Refracted` | Unit transmitted direction |
| `abs(k) <= tol` | `CriticalAngle` | Unit direction tangent to the interface |
| `k < -tol` | `TotalInternalReflection` | `Vector3.Zero` |
| Invalid vector, orientation, or index | `InvalidInput` | `Vector3.Zero` |

This tolerance makes the critical boundary explicit and avoids changing classification because of a few floating-point rounding bits. It is not an angular tolerance supplied by the caller.

For `Refracted`, the transmitted vector is `eta * I_tangent - sqrt(k) * N / length(N)`. `CriticalAngle` snaps the normal component to zero and returns the normalized tangent. The implementation does not normalize the final refracted vector, which would change its tangential component and perturb Snell's law.

## Fresnel power reflectance

`Dielectric.Fresnel` accepts the nonnegative incident cosine rather than vectors. For exactly unit directions:

```text
cosIncident = -dot(incidentUnit, normalUnit)
```

The argument must be finite and in `[0, 1]`. If a vector pair carries accepted length error, derive its cosine from normalized directions before calling `Fresnel`. Both refractive indices follow the media convention above.

The returned `FresnelPower` properties are fractions of incident power:

- `S` is reflectance for s-polarized light.
- `P` is reflectance for p-polarized light.
- `Unpolarized` computes `(S + P) / 2` from the two stored values. It is not a separate constructor argument.

At the critical angle and under total internal reflection, all three properties are `1`. For `nIncident > nTransmitted`, the critical test uses the same `8 * binary32-epsilon * max(1, sin2T)` tolerance as refraction, with `sin2T` derived from the incident cosine. No critical-angle snapping is applied when entering a higher-index medium. When the two refractive indices are equal, all three properties are `0`, including at grazing incidence.

`Dielectric.NormalReflectance` returns

```text
R0 = ((nIncident - nTransmitted) / (nIncident + nTransmitted))^2
```

`Dielectric.Schlick(cosIncident, normalReflectance)` returns

```text
R = R0 + (1 - R0) * (1 - cosIncident)^5
```

This overload has no refractive-index ratio, so it cannot detect total internal reflection. Use `Dielectric.Fresnel` when exact dielectric power reflectance or critical-boundary handling is required. The Schlick overload that accepts both indices derives `R0` but has the same limitation.

Invalid scalar inputs to `Fresnel`, `NormalReflectance`, and `Schlick` raise `ArgumentOutOfRangeException`.

## Wavelength and dispersion units

`Cauchy3` and `Sellmeier3` accept wavelength in nanometers. The wavelength convention must match the coefficient source, including whether wavelengths are specified in air or vacuum. Caustikon does not convert between those conventions or change the reference medium of the resulting index. Each model stores a caller-supplied inclusive validity interval:

```text
minimumWavelengthNanometers <= wavelengthNanometers <= maximumWavelengthNanometers
```

Evaluation converts nanometers to micrometers before applying either equation.

For `Cauchy3`:

```text
n(wavelength) = A + B / wavelength^2 + C / wavelength^4
```

- `A` is dimensionless.
- `BUm2` has units of micrometer squared.
- `CUm4` has units of micrometer to the fourth power.

Negative `B` and `C` coefficients are permitted. A coefficient's sign alone does not determine whether the index is physical over the supplied interval.

For `Sellmeier3`:

```text
n(wavelength)^2 = 1
  + B1 * wavelength^2 / (wavelength^2 - C1)
  + B2 * wavelength^2 / (wavelength^2 - C2)
  + B3 * wavelength^2 / (wavelength^2 - C3)
```

- `B1`, `B2`, and `B3` are dimensionless.
- `C1Um2`, `C2Um2`, and `C3Um2` have units of micrometer squared.

Use coefficient sets only with the wavelength unit and validity range stated by their source. Caustikon does not infer a fit range from coefficient values.

For example, the SCHOTT coefficients used in the README follow the catalogue convention for index relative to air at room temperature. [SCHOTT TIE-29, section 2.3](https://media.schott.com/api/public/content/aaa572afd854434fb7b3faa4bc46103f?v=c0f4fa52) specifies the wavelength table and interpolation limits. A successful evaluation confirms the arithmetic and the caller-supplied interval, not the suitability of a coefficient set for other measurement conditions.

## Dispersion statuses

`EvaluateNanometers` always assigns its scalar `out` value. A batch call assigns one result and one status per input wavelength.

| `DispersionStatus` | Meaning | Result value |
| --- | --- | --- |
| `Success` | The wavelength is valid, inside the model range, and produces a positive finite index | Computed refractive index |
| `InvalidInput` | The wavelength is nonfinite or not greater than zero, or the model is an uninitialized default value | `double.NaN` |
| `OutsideModelRange` | The wavelength is outside the model's inclusive fit interval | `double.NaN` |
| `Singular` | An active, positive-resonance Sellmeier denominator rounds to zero | `double.NaN` |
| `NonPhysical` | The equation produces a nonfinite or nonpositive `n` or `n^2`, or an intermediate cannot be represented safely | `double.NaN` |

Out-of-range values are not extrapolated. The status distinguishes bad input, use outside a fitted interval, a Sellmeier pole, and a finite-input calculation with no physical real-index result.

Model constructors throw `ArgumentOutOfRangeException` when coefficients are nonfinite, when the minimum wavelength is not finite and greater than zero, or when the maximum is not finite and at least the minimum. `Sellmeier3` also rejects negative `C` coefficients and a validity interval that contains an active positive resonance pole, including either endpoint. A pole is located at `1000 * sqrt(Ci)` nanometers. Terms with `Bi = 0` are inactive and skipped, so their resonance values do not restrict the interval.

Finite inputs can still exceed the representable range of an intermediate dispersion calculation. Such evaluations return `NonPhysical` rather than reporting a successful nonfinite result. Zero terms are skipped, so constant models do not fail merely because an unused inverse-wavelength term would overflow.

For `Sellmeier3`, an active term with a positive resonance requires the squared wavelength in micrometers to retain normal `double` precision. If that square is zero or subnormal, evaluation returns `NonPhysical`. Inactive terms and zero-resonance constant terms do not impose this lower representational limit. Very large wavelengths use a scaled expression when squaring would overflow.

## Scalar and batch precision

Ray directions, refractive-index inputs to `Dielectric`, and reflectance outputs use `float`. Refraction and exact Fresnel calculations use `double` intermediates to cover the ratio range of positive finite `float` indices. Dispersion inputs and calculations use `double`. Batch and scalar overloads implement the same equations and status rules.

## Batch buffer rules

Batch methods accept spans and write to caller-owned output spans.

- Every span participating in a call must have the same length.
- A length mismatch or forbidden overlap raises `ArgumentException` before the first output write.
- Refraction permits exact in-place operation from `incidentUnits` to `refractedUnits`. The output must not overlap `normalUnits`, even exactly.
- Normal-reflectance and Schlick outputs may exactly overlap an input of the same element type. Dispersion may replace the wavelength span with refractive indices in place.
- Partial input/output overlap is rejected. Cross-type overlap is also rejected, including spans constructed over the same bytes with `MemoryMarshal.Cast`.
- A refraction or dispersion status span must not overlap any input or other output span.
- Fresnel output must not overlap any input span.
- Per-lane refraction indices are validated per lane and can produce `InvalidInput` beside successful lanes.
- Shared refraction indices and scalar arguments used for an entire batch are validated before output is written.
- Batch Fresnel, normal-reflectance, and Schlick overloads validate their input spans before writing results.

No batch method allocates an output array. Calls execute synchronously on the calling thread, with no hidden parallel work. Converting another collection to a span or materializing results remains the caller's choice.

## Constructor signatures

Coefficient arguments are interleaved with their units in the public names:

```csharp
public Cauchy3(
    double a,
    double bUm2,
    double cUm4,
    double minimumWavelengthNanometers,
    double maximumWavelengthNanometers)
```

```csharp
public Sellmeier3(
    double b1,
    double c1Um2,
    double b2,
    double c2Um2,
    double b3,
    double c3Um2,
    double minimumWavelengthNanometers,
    double maximumWavelengthNanometers)
```

Named arguments are recommended when coefficient sets are copied from a material catalog.
