# Changelog

## Unreleased

- `Caustikon.Glasses` gains a `Liquids` vendor: water (Daimon and Masumura 2007 fit, Hale and Querry 1973 absorption), ethanol, methanol, acetone, glycerol, ethylene glycol, benzene, toluene and carbon disulfide, from the database's material measurements. Their catalog `nd` and `Vd` are the fit's own values and the provenance says so.

All notable changes to Caustikon are recorded here.

## Unreleased

### Added

- `IDispersionModel`, implemented by every model, so consumers can stay generic over value-type models without boxing.
- `Sellmeier` (offset plus up to eight resonance terms, negative resonances accepted), `Polynomial` (`n²` power series) and `Cauchy` (`n` power series) with arbitrary exponents, the three closed forms manufacturer catalogs use.
- `Dispersion.EvaluateNanometers<T>`, the batch buffer contract for any model, and `Dispersion.ClassifyWavelength`.
- Package `Caustikon.Glasses`: 1646 catalogued glasses from nine manufacturers, generated from the RefractiveIndex.INFO database (CC0 1.0) with a provenance record per glass; typed vendor fields for the hot path and `GlassCatalog.Find` for name lookup; `Glass.Define` for caller-defined glass.
- Tabulated extinction with absorption and internal transmittance at any path length; SCHOTT-form temperature coefficients; transmitted colour under D65 in CIE XYZ and sRGB.
- Every catalogued glass is verified in CI against its printed `nd` and `Vd`; N-BK7 is pinned to its SCHOTT datasheet for internal transmittance and dn/dT.

### Changed

- Repository renamed from `sidelight` to `caustikon` to match the product, package and namespace.

- Include XML API documentation in both package targets.
- Add a runnable spectral batch example across the glass-to-air critical boundary.
- Document a complete source-reference quickstart and explicit failure handling.
- Simplify the prism drawing and verify its generated geometry in CI.

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
