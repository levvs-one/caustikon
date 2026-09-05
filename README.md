# Caustikon

Allocation-free geometric optics primitives for .NET.

Caustikon handles what happens when a ray meets a dielectric boundary: its transmitted direction, reflected power, and the change in refractive index with wavelength. It provides vector refraction, exact Fresnel power reflectance, Schlick's approximation, and three-term Cauchy and Sellmeier dispersion models.

Scalar and span APIs use caller-owned storage. The library targets .NET 8 and .NET 10, with no external runtime dependencies. The API is pre-1.0 and may change between releases.

![Six wavelengths refracted through an N-BK7 prism](examples/Prism/prism.svg)

[The prism example](examples/Prism) calculates the ray paths and prints the numerical results. The spectral spread is to scale; colors identify wavelengths, not transmitted power.

## Use from source

Caustikon is not published to NuGet.org. Clone this repository and add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="../caustikon/src/Caustikon/Caustikon.csproj" />
</ItemGroup>
```

The repository selects .NET SDK 10.0.400 and permits later patches in the same 10.0.4xx feature band. Running the tests for both targets also requires the .NET 8 runtime; a second SDK is not necessary.

## Refract a ray

```csharp
using System.Numerics;
using Caustikon;

Vector3 incident = Vector3.Normalize(new Vector3(0.5f, -0.8660254f, 0f));
Vector3 normal = Vector3.UnitY;

RefractionKind kind = Dielectric.RefractUnit(
    incident,
    normal,
    nIncident: 1f,
    nTransmitted: 1.5f,
    out Vector3 transmitted);

if (kind is RefractionKind.Refracted or RefractionKind.CriticalAngle)
{
    float cosIncident = -Vector3.Dot(incident, normal);
    FresnelPower power = Dielectric.Fresnel(cosIncident, 1f, 1.5f);
    Console.WriteLine($"T = {transmitted}, R = {power.Unpolarized}");
}
```

The direction and normal convention is strict:

- `incident` points along ray travel, toward the interface.
- `normal` points back into the incident medium.
- Their dot product must be nonpositive. A positive dot product is invalid.
- `transmitted` points away from the interface into the transmitted medium.
- `nIncident` and `nTransmitted` are positive phase refractive indices measured under comparable conditions.

`RefractUnit` returns a status instead of hiding the critical-angle boundary. `Refracted` and `CriticalAngle` return a unit direction. `TotalInternalReflection` and `InvalidInput` return `Vector3.Zero`.

## Evaluate a dispersion model

The public wavelength unit is the nanometer. Use the wavelength convention of the coefficient source, including its air or vacuum reference. Coefficient names state the micrometer powers used by the equations.

```csharp
using Caustikon;

var glass = new Sellmeier3(
    b1: 1.03961212,
    c1Um2: 0.006000699,
    b2: 0.231792344,
    c2Um2: 0.0200179144,
    b3: 1.01046945,
    c3Um2: 103.560653,
    minimumWavelengthNanometers: 365d,
    maximumWavelengthNanometers: 2325.4d);

DispersionStatus status = glass.EvaluateNanometers(
    wavelengthNanometers: 587.6d,
    out double refractiveIndex);

if (status is DispersionStatus.Success)
{
    Console.WriteLine(refractiveIndex);
}
```

The coefficients come from the [SCHOTT N-BK7 datasheet](https://media.schott.com/api/public/content/41e799d0bf874807a0bb8e702fbb75b5?v=54856406). At 587.6 nm, the result is approximately `1.51679844`, consistent with the datasheet's rounded `1.51680`. SCHOTT's catalogue relation gives refractive index relative to air at room temperature; see [TIE-29, section 2.3](https://media.schott.com/api/public/content/aaa572afd854434fb7b3faa4bc46103f?v=c0f4fa52) for its conventions.

The example chooses a 365-2325.4 nm interval within the tabulated values. This is a caller-selected range. Every model carries an inclusive wavelength interval and reports `OutsideModelRange` beyond it.

## API map

| Area | Scalar API | Batch API |
| --- | --- | --- |
| Refraction | `Dielectric.RefractUnit` | Shared or per-lane refractive indices |
| Exact dielectric reflectance | `Dielectric.Fresnel` | Shared or per-lane refractive indices |
| Normal-incidence reflectance | `Dielectric.NormalReflectance` | Per-lane refractive indices |
| Schlick reflectance | `Dielectric.Schlick` | Shared `R0` or shared refractive indices |
| Cauchy dispersion | `Cauchy3.EvaluateNanometers` | Wavelength, result, and status spans |
| Sellmeier dispersion | `Sellmeier3.EvaluateNanometers` | Wavelength, result, and status spans |

Batch overloads write into spans supplied by the caller. Span lengths must match. Permitted in-place operations and overlap restrictions are specified in [the buffer contract](docs/conventions.md#batch-buffer-rules).

## Numerical contracts

- `FresnelPower.S` and `P` store power reflectances, not field amplitudes. `Unpolarized` computes their arithmetic mean.
- `Dielectric.Fresnel` takes `cosIncident` in `[0, 1]`. It returns unit reflectance at and beyond the critical boundary.
- `Dielectric.Schlick(cosIncident, normalReflectance)` evaluates the approximation only. It cannot infer total internal reflection from `R0` and the cosine.
- `Cauchy3` evaluates `n = A + B / wavelength^2 + C / wavelength^4` with wavelength in micrometers.
- `Sellmeier3` evaluates `n^2 = 1 + sum(Bi * wavelength^2 / (wavelength^2 - Ci))` with wavelength in micrometers.
- A non-successful dispersion evaluation writes `double.NaN`.
- Constructors reject nonfinite coefficients and invalid wavelength intervals. `Sellmeier3` also rejects negative resonance coefficients and intervals containing an active positive resonance pole. A term with `Bi = 0` is inactive.

The full status and boundary rules are in [docs/conventions.md](docs/conventions.md).

## Measured cost

The refraction benchmark reported zero managed allocations in all 16 scalar and span cases. On an Intel i5-4670 with .NET 10, a million air-to-glass interactions took 27.70 ms in a caller-written scalar loop and 31.64 ms through the span API. These are single-machine measurements, with inputs and buffers prepared before timing. [The full report](docs/performance.md) records the method, uncertainty, limitations, and reproduction command.

## Scope

Caustikon models homogeneous, isotropic, nonabsorbing dielectric media and phase refractive index. It does not model absorption, complex refractive index, birefringence, polarization state propagation, thin-film interference, diffraction, surface roughness, lens geometry, ray-scene intersection, or rendering. It is intended to be embedded in systems that own those concerns.

## Build and test

```powershell
dotnet restore Caustikon.sln --locked-mode
dotnet build Caustikon.sln -c Release --no-restore
dotnet test --project tests/Caustikon.Tests/Caustikon.Tests.csproj -c Release -f net8.0 --no-build
dotnet test --project tests/Caustikon.Tests/Caustikon.Tests.csproj -c Release -f net10.0 --no-build
dotnet pack src/Caustikon/Caustikon.csproj -c Release --no-build --output artifacts/package
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the acceptance rules used for numerical changes and benchmarks.

## References

- W. Sellmeier, [Zur Erklarung der abnormen Farbenfolge im Spectrum einiger Substanzen](https://onlinelibrary.wiley.com/doi/10.1002/andp.18722231105), 1872 - the original dispersion relation.
- Christophe Schlick, [An Inexpensive BRDF Model for Physically-based Rendering](https://onlinelibrary.wiley.com/doi/10.1111/1467-8659.1330233), 1994 - the reflectance approximation used by `Dielectric.Schlick`.
- Matt Pharr, Wenzel Jakob, and Greg Humphreys, [Physically Based Rendering, fourth edition - Specular Reflection and Transmission](https://www.pbr-book.org/4ed/Reflection_Models/Specular_Reflection_and_Transmission) - equations and a reference implementation for dielectric reflection and refraction.
- SCHOTT, [N-BK7 optical glass datasheet](https://media.schott.com/api/public/content/41e799d0bf874807a0bb8e702fbb75b5?v=54856406) - the dispersion coefficients and tabulated refractive indices used in the example and regression tests.

## License

[MIT](LICENSE) - Copyright (c) 2026 levvs-one.
