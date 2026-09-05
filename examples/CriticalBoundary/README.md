# A spectrum at the critical boundary

Six wavelengths reach the same flat N-BK7/air boundary from inside the glass, each at 41 degrees to the normal. Dispersion gives each wavelength a different critical angle. Some wavelengths leave the glass; others undergo total internal reflection. The example calculates both outcomes through the batch API, without a scene, renderer or artificial invalid samples.

## Run

From the repository root, using the SDK selected by `global.json`:

```powershell
dotnet run --project examples/CriticalBoundary -c Release
```

The program writes a console table and creates no output files. `--help` or `-h` prints a short description; no angle or material arguments are accepted. Unexpected arguments, failed calculations or failed cross-checks return a nonzero exit code. The final `Checked ...` line is emitted only after all six rows pass the checks.

## Read the result

- `lambda_nm` is wavelength in nanometers, using the coefficient source's convention.
- `n_used` is the glass index after conversion to `float`, exactly as passed to `Dielectric`. The dispersion calculation itself uses `double`.
- `critical_deg` is `asin(1 / n_used)` in degrees, measured from the interface normal.
- `kind` is the actual `RefractionKind`. No status is substituted for display.
- `transmitted_deg` is the outgoing direction's angle to the normal into air. `-` means no transmitted ray exists under total internal reflection; the zero output vector is not interpreted as an angle.
- `R_unpolarized` is the fraction of initially unpolarized incident power reflected by this one interface, not a field amplitude or a percentage. Total internal reflection gives 1. For a lossless interface, the transmitted power fraction is `1 - R_unpolarized`.

The expected physical split is total internal reflection at 404.7 and 435.8 nm, and refraction at 486.1, 546.1, 587.6 and 656.3 nm. These samples are not chosen to trigger the numerical `CriticalAngle` snap. That status remains a separate boundary case in the library's tests.

## Follow the calculation

1. `Sellmeier3.EvaluateNanometers` fills caller-owned index and status arrays. Every status is checked before an index is converted or passed downstream. This fixed, in-range spectrum treats a dispersion failure as a failed example, not as usable `NaN` input.
2. The per-lane `Dielectric.RefractUnit` overload uses one common incident direction and normal, but a different glass index in each position. `Dielectric.Fresnel` evaluates the matching per-lane reflectances. All buffers have equal lengths and separate storage. The normal is `Vector3.UnitY`, pointing back into the glass; incident and transmitted rays travel toward negative Y.
3. Each batch result is compared with the corresponding scalar call. An additional scalar Snell check verifies the transmitted X/Y components or the absence of a real transmitted direction. Its component tolerance is two binary32 spacings above 1, approximately `2.38e-7`; it does not change the library's critical-angle classification. The TIR branch also requires a zero transmitted vector and unit S/P reflectance.

The cosine supplied to Fresnel is derived from the normalized incident direction. The program preserves the input arrays, so the scalar checks use the same inputs as the batch calls. Caller-owned arrays and console formatting allocate memory; this example is not an allocation benchmark or a speed comparison.

## Material and limits

The coefficients and catalog wavelengths come from the [official SCHOTT N-BK7 datasheet](https://media.schott.com/api/public/content/41e799d0bf874807a0bb8e702fbb75b5?v=54856406), dated 01-Dec-2023: `B = [1.039612120, 0.231792344, 1.010469450]`, `C = [0.006000699, 0.0200179144, 103.56065300]` in square micrometers. The example uses a caller-selected validity interval of 365-2325.4 nm. SCHOTT's index is relative to air, so the transmitted medium has index 1 in the same convention.

This is one ideal, flat, uncoated interface between homogeneous, isotropic, nonabsorbing media. It does not model propagation distance, bulk absorption, temperature corrections, surface roughness, evanescent coupling to another nearby interface or a finite-width beam. It does not trace the reflected ray. See [the public conventions](../../docs/conventions.md) for model ranges, vector orientation, boundary classification and buffer ownership.
