# Caustikon full build — working plan

Companion to `docs/superpowers/specs/2026-09-05-glass-catalog-design.md`. Owner
delegated stages 1–3 and the site for autonomous execution on 2026-09-05. This
file is the resume point: an interrupted session continues from the first
unchecked item. Check items only after `dotnet build` and `dotnet test` pass.

Sources on disk (not in the repository):
- RefractiveIndex.INFO database, CC0: `C:\Users\dev5\Downloads\rii-db` (commit c5c2f18)
- CIE 1931 2° CMF (1 nm) and D65 (5 nm), from colour-science (BSD-3) data tables:
  `C:\Users\dev5\Downloads\caustikon-sources\cie1931_2deg_1nm.csv`, `cie_d65_5nm.csv`

Data facts that shaped the design (surveyed 2026-09-05):
- 1784 vendor entries; formula forms present: Sellmeier-2 (660), Polynomial (972),
  Sellmeier-1 (9, convertible to Sellmeier-2 by squaring C), Cauchy (5);
  tabulated n / nk (138) are skipped and listed in the manifest.
- 1648 entries carry catalog nd and Vd; 1662 carry tabulated k; 1335 carry Schott
  thermal-dispersion "formula A" coefficients.
- Coefficient counts reach 15 (7 terms); capacity is 8 terms.

## Stage 1 — core

- [ ] `IDispersionModel` interface; `Sellmeier3` and `Cauchy3` implement it
- [ ] `Sellmeier` (offset + up to 8 terms, C in µm²), `Polynomial` (n²), `Cauchy` (n),
      all `readonly struct`, InlineArray storage, validating constructors, same statuses
- [ ] `Dispersion.EvaluateNanometers<T>` generic batch helper with the buffer contract
- [ ] Tests: general Sellmeier reproduces `Sellmeier3` on N-BK7 bit-for-bit;
      Polynomial against a CDGM glass nd/Vd; Cauchy against `Cauchy3`; guards

## Stage 1 — Caustikon.Glasses

- [ ] Generator `tools/Caustikon.Glasses.Generator` (YamlDotNet) reading the RII tree,
      writing `data/glasses/*.json` (normalized rows + provenance) and
      `src/Caustikon.Glasses/Generated/*.g.cs`
- [ ] `Glass`, `GlassProvenance`, `DispersionFormula`, `GlassCatalog`, vendor classes
- [ ] `DATA-LICENSE.md`
- [ ] Tests: every glass with catalog nd/Vd reproduces them at the exact d/F/C lines
      (tolerance measured from the data and recorded); provenance completeness

## Stage 2 — thickness, temperature, colour

- [ ] `TabulatedExtinction` → internal transmittance at thickness (Beer–Lambert),
      verified against SCHOTT N-BK7 τi(10 mm)
- [ ] `ThermalDispersion` (SCHOTT formula A), absolute Δn, reference 20 °C
- [ ] CIE 1931 2° + D65 embedded; `TransmittedColour` → XYZ, linear sRGB, sRGB;
      test: empty path reproduces D65 white
- [ ] README Scope amended to say what stage 2 added and what it still excludes

## Stage 3 — site and delivery

- [ ] `site/Caustikon.Site` Blazor WebAssembly: home, catalog with search, glass page
      (n(λ), τi(λ, d), colour, thermal), playground (glass × angle × thickness),
      formulas page; one typeface, SVG charts drawn from the library, no template CSS
- [ ] `.github/workflows/pages.yml` (build + deploy site), `release.yml` (pack + push
      NuGet on tag with `NUGET_API_KEY`); verify.yml extended to Glasses tests
- [ ] README rewritten around NuGet install and a five-line quickstart
- [ ] CHANGELOG entries

## Owner-only (cannot be done from this machine)

- Rename repository `sidelight` → `caustikon`
- Settings → Pages → Source: GitHub Actions
- Add `NUGET_API_KEY` secret; push a `v*` tag to publish
