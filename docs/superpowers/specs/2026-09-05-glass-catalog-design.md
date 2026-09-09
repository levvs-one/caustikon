# Glass catalog and user-defined glass — design

Date: 2026-09-05
Status: proposed
Stage: 1 of 3

## Goal

Let a caller name a glass instead of transcribing coefficients from a vendor
datasheet, and let a caller define a glass of their own with the same standing as
a catalogued one. Today `examples/Prism/Program.cs` carries N-BK7 as six literals
in a constructor call. That is the only glass the project knows, and it exists as
example code rather than as a supported artifact.

Success: a caller writes `Schott.NBk7` or supplies their own coefficients, and in
both cases gets a model whose numbers are traceable to a published source and are
checked in CI against values that source publishes independently.

## Naming

The product is **Caustikon**. The repository is to be renamed from `sidelight` to
`caustikon` so that the repository, the solution, the assembly, the NuGet package
and the namespace agree. Every other identifier in the project already says
Caustikon; the repository name is the only outlier, and it is the cheapest of them
to change. GitHub redirects the old URL, so no external link breaks.

This design assumes that rename. Documentation written against it uses
`github.com/levvs-one/caustikon`.

## Stage 1 scope

A new project `src/Caustikon.Glasses`, published as its own NuGet package,
depending on `Caustikon`. The core package gains one interface, any dispersion
model structs the selected glasses turn out to need, and no data.

Stage 1 delivers:

1. A dispersion-model abstraction in the core, so heterogeneous models can be used
   generically without boxing.
2. A `Glass` record binding a model to its identity and provenance.
3. A catalog of glasses from three vendors, covering at least two distinct
   dispersion formula forms.
4. First-class support for user-defined glass: the same `Glass` type, constructed
   by the caller, with provenance the caller supplies.
5. Verification of every catalogued glass against vendor-published indices.

## Architecture

### Model abstraction (core package)

```csharp
public interface IDispersionModel
{
    DispersionStatus EvaluateNanometers(double wavelengthNanometers, out double refractiveIndex);
    double MinimumWavelengthNanometers { get; }
    double MaximumWavelengthNanometers { get; }
}
```

`Sellmeier3` and `Cauchy3` implement it. Their existing members already match
these signatures, so this is additive and breaks no caller.

Consumers that care about speed take a generic parameter constrained as
`where T : struct, IDispersionModel`. The JIT specialises per model type, so there
is no boxing and no allocation. This is what preserves the library's existing
promise, measured and recorded in `docs/performance.md`, of zero managed
allocations on the refraction path.

Rejected: a curiously-recurring generic parameter (`IDispersionModel<TSelf>`) and
static abstract interface members. Neither buys anything here, and both make every
signature that mentions a model harder to read.

### Two access layers, with the cost stated

A catalog is heterogeneous: different glasses use different formula forms, so
their models are different value types. A single collection of them cannot stay
generic. Rather than hide that, the design splits it:

**Typed constants — the hot path.** `Schott.NBk7` is a static property returning a
concrete `Sellmeier3`. The type is known at compile time, the value is a struct,
nothing allocates. Code that traces a million rays uses this layer.

**Runtime lookup — the convenience path.** `GlassCatalog.Find(vendor, name)`
returns a `Glass` whose model is held behind the interface, which boxes. Code that
resolves a glass from user input or a config file uses this layer.

The boxing on the lookup layer will be stated in its XML documentation and in the
README, not left for a caller to discover with a profiler. One allocation per
glass resolved, none per ray traced.

### The `Glass` record

```csharp
public sealed record Glass(
    string Vendor,
    string Name,
    IDispersionModel Model,
    GlassProvenance Provenance);
```

`GlassProvenance` records where the numbers came from: source (vendor datasheet or
RefractiveIndex.INFO entry), the catalog version or database path, the retrieval
date, and the formula form. A catalogued glass and a user-defined glass are the
same type; the difference is only what the provenance says. A caller who invents
coefficients records that they invented them.

### User-defined glass

Already almost supported: `Sellmeier3` and `Cauchy3` have public validating
constructors. Stage 1 adds the wrapper that gives a custom model the same
standing as a catalogued one, and a provenance value meaning "supplied by the
caller". No parser, no file format, no registry mutation. A caller who wants to
load glass definitions from a file writes ten lines against the public
constructors; the library does not own that decision.

## Data and provenance

Source: the RefractiveIndex.INFO database, released under **CC0 1.0 Universal**
(verified 2026-09-05 at https://refractiveindex.info/about and in
polyanskiy/refractiveindex.info-database). Use, modification and redistribution are
unrestricted, including commercially, with no permission required. Attribution is
not legally required; the project will attribute anyway, because a physical
constant without a citation is not usable evidence.

Vendor datasheets remain the authority for the verification values, which is why
they are cited separately from the coefficients.

`data/` in the repository holds the extracted rows plus one provenance record per
glass. Catalog code is **generated by a script and committed**, not generated
during the build:

- diffs to physical constants show up in review, where they belong;
- the build needs no network and no extra tool;
- reproduction matches the pattern already used for snapshots in
  `llms-txt-snapshots` and for the recorded benchmark CSV in this repository.

`DATA-LICENSE.md` covers the data separately from the MIT licence on the code.

## Glass selection

Three vendors: SCHOTT, Ohara, Hoya. Two to three glasses each.

The binding constraint is **coverage of formula forms, not popularity**: the
selection must exercise at least two distinct dispersion formula forms, because
proving the abstraction survives heterogeneity is the entire purpose of a thin
vertical slice. The concrete list is fixed during import by reading the formula
form recorded in each candidate's database entry, under this rule:

1. Include N-BK7 (SCHOTT) unconditionally — it is the existing regression anchor.
2. Add glasses until at least two distinct formula forms are represented.
3. Prefer glasses whose vendor publishes n_d, n_F and n_C, since a glass that
   cannot be verified independently cannot be catalogued.

If a needed formula form is not yet implemented in the core, implementing it is in
scope for stage 1; that is the discovery the slice exists to force.

## Verification

Two test classes, both in CI:

**Numerical.** Every catalogued glass is checked at the Fraunhofer d, F and C
lines against the indices its vendor publishes, using exact line wavelengths
(587.5618, 486.1327, 656.2725 nm), with the Abbe number checked as well. The Abbe
number is the sharp test: it divides by the difference of two nearby indices, so an
error in the fourth digit of a coefficient shows up there and nowhere else.

This deliberately fixes a weakness in the current tests. `Sellmeier3Tests` checks
365, 587.6 and 2325.4 nm against values produced by the same formula, which detects
regressions but cannot detect a wrong coefficient. Only the d-line test is anchored
outside the implementation. Catalogued glasses will be anchored outside it in full.

**Provenance.** Every catalog entry has every provenance field populated. A glass
whose origin is unrecorded fails the build.

## Non-goals for stage 1

Tabulated nk data and interpolation, absorption, complex refractive index,
temperature and pressure dependence, automatic synchronisation with the upstream
database, and colour. Each either contradicts the Scope section of the README or
deserves its own spec.

## Roadmap

**Stage 2 — colour.** Real glass colour needs three things the project does not
have: internal transmittance per glass, a source spectrum, and the CIE colour
matching functions with a white point. It also requires the core to model
absorption, which the README's Scope section currently disowns, so stage 2 begins
by amending that section rather than quietly contradicting it. Output: the
perceived tint of a named glass at a named thickness.

**Stage 3 — interactive visualiser.** An application over the library where glass,
geometry, angle and thickness are adjustable and the result is drawn live. Colour
there is the renderer's job, not the library's. This is a separate project in the
same repository, and it is the first thing here that has a user interface.

Stages 2 and 3 get their own specs. Nothing in stage 1 is designed to be thrown
away when they arrive: the `Glass` record gains fields, and the model abstraction
gains implementations.

## Decisions already settled

| Question | Decision |
|---|---|
| Separate repository? | No. Everything stays in the Caustikon repository. |
| Where does catalog data live? | A separate `Caustikon.Glasses` package; the core gains no vendor data. |
| Catalog generated at build time? | No. Generated by script and committed. |
| Data licence | CC0 1.0, verified. Attribution given regardless. |
| Repository name | Rename `sidelight` to `caustikon`. |
