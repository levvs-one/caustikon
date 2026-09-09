# Caustikon.Glasses

Optical glass catalog for [Caustikon](https://github.com/levvs-one/caustikon): 1646 manufacturer glasses and nine liquids
from SCHOTT, OHARA, HOYA, CDGM, HIKARI, SUMITA and others, each with its dispersion model,
tabulated absorption, temperature coefficients where the manufacturer publishes them, and a
record of where every number came from.

```csharp
using Caustikon;
using Caustikon.Glasses;

// Hot path: the concrete model, a value type, no allocation.
Sellmeier bk7 = Schott.NBK7;
bk7.EvaluateNanometers(587.5618, out double nd);          // 1.51680

// Convenience path: name lookup, one allocation per glass resolved.
Glass glass = GlassCatalog.Find("ohara", "S-BSL7")!;
double abbe = glass.AbbeD;                                 // 64.1
glass.Extinction!.InternalTransmittance(400, 10, out double t);   // τi of a 10 mm path
TransmittedColour tint = GlassColour.Transmitted(glass, 25).Value;
string hex = tint.Hex;                                     // what a 25 mm slab does to D65
```

A caller's own glass has the same standing as a catalogued one:

```csharp
Sellmeier melt = new(0, [1.0396, 0.2318, 1.0105], [0.0060, 0.0200, 103.56], 300, 2500);
Glass mine = Glass.Define("melt 42", in melt, "in-house measurement, 2026-09", new DateOnly(2026, 9, 5));
```

## What every entry carries

| Member | Meaning | Source |
| --- | --- | --- |
| `Model` | `Sellmeier`, `Polynomial` or `Cauchy` over the fitted wavelength interval | manufacturer catalog |
| `CatalogIndexD`, `CatalogAbbeD` | `n_d` and `ν_d` as printed, independent of the fit | manufacturer catalog |
| `Extinction` | `k(λ)` table → absorption coefficient → internal transmittance for any path length | manufacturer τi tables |
| `Thermal` | SCHOTT-form `dn/dT` coefficients → absolute index shift with temperature | manufacturer catalog |
| `Provenance` | database, commit, citation, file path, retrieval date, import notes | this package |

Every catalogued glass is checked in CI at the exact d, F and C lines against the printed
`n_d` and `ν_d`; the bound is five units in the fifth decimal of index, plus the rounding of
the printed value. `data/glasses/manifest.json` in the repository records the deviation of
each fit from its catalog.

## Data

The entries are transcribed from the RefractiveIndex.INFO database, released under CC0 1.0.
Colour uses the CIE 1931 2° observer and illuminant D65. Licences and citations are in
[DATA-LICENSE.md](https://github.com/levvs-one/caustikon/blob/main/DATA-LICENSE.md).
