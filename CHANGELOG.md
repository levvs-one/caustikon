# Changelog

All notable changes to Caustikon are recorded here.

## 0.1.0 - 2026-09-05

### Added

- Scalar vector refraction with explicit `Refracted`, `CriticalAngle`, `TotalInternalReflection`, and `InvalidInput` outcomes.
- Scalar exact dielectric Fresnel power reflectance for s, p, and unpolarized light.
- Normal-incidence and Schlick reflectance helpers.
- Span overloads for refraction and reflectance with shared or per-lane refractive indices where applicable.
- Three-term Cauchy and Sellmeier dispersion models with explicit coefficient units and inclusive wavelength ranges.
- Per-wavelength dispersion statuses that distinguish invalid input, use outside the fitted range, a Sellmeier singularity, and a nonphysical result.
- Targets for .NET 8 and .NET 10 with no production package dependencies.
- Numerical regression tests, a BenchmarkDotNet suite, public conventions, contribution rules, and cross-platform verification.
- A reproducible N-BK7 prism example with calculated ray geometry, angular dispersion, and first-pass polarized power.
- A recorded 16-case refraction benchmark with zero managed allocations in every measured case.
