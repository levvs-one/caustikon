# Contributing to Caustikon

Caustikon keeps a small production surface on purpose. A change should make the optics more correct, the contract clearer, or measured execution less expensive. New abstractions need a current use case.

## Prerequisites

- .NET SDK 10.0.400 or a later patch in the 10.0.4xx feature band, as selected by `global.json`
- .NET 8 runtime, to execute the `net8.0` tests
- Git

The .NET 10 SDK builds both `net8.0` and `net10.0` targets. The production project uses only the .NET Base Class Library. Test and benchmark dependencies stay outside `src/Caustikon`.

## Build and test

From the repository root:

```powershell
dotnet restore Caustikon.sln --locked-mode
dotnet build Caustikon.sln --configuration Release --no-restore
dotnet test --project tests/Caustikon.Tests/Caustikon.Tests.csproj --configuration Release --framework net8.0 --no-build
dotnet test --project tests/Caustikon.Tests/Caustikon.Tests.csproj --configuration Release --framework net10.0 --no-build
dotnet pack src/Caustikon/Caustikon.csproj --configuration Release --no-build --output artifacts/package
```

`global.json` selects Microsoft.Testing.Platform for `dotnet test`. Keep the `--project` option: the older positional project syntax belongs to the VSTest runner and is not used here. CI runs both targets on Windows and Linux, builds the benchmark project, and packages the library.

Dependency versions are committed in `packages.lock.json` files. After an intentional test or benchmark dependency change, run `dotnet restore Caustikon.sln --force-evaluate`, review the lockfile changes, and repeat the locked restore before committing.

## Make a focused change

1. Read [the conventions](docs/conventions.md) before changing formulas or public signatures.
2. Add tests for the physical case, its boundary, invalid inputs, and the status returned to the caller.
3. Keep production code free of package dependencies.
4. Run the Release build, the full test suite, and package creation.
5. Update `CHANGELOG.md` when a public contract or observable numerical result changes.

## Numerical changes

A numerical change needs evidence that is easy to review:

- State the equation and the convention it assumes.
- Link the primary paper, standard, dataset, or reference implementation used for comparison.
- Include values on both sides of each branch boundary.
- Cover normal incidence, grazing incidence, the critical angle, total internal reflection, and non-finite inputs when they apply.
- Use tolerances derived from the calculation and data precision. Do not widen a tolerance only to make a failing test pass.

Cross-checks against another implementation are useful, but generated expected values must be committed as explicit test cases with their source and units recorded.

## Performance changes

Do not describe a change as faster without a BenchmarkDotNet result from the same machine, runtime, build configuration, and input set. Report time and allocation data. Keep correctness tests separate from benchmarks.

Run benchmarks from a Release build on an otherwise idle machine:

```powershell
dotnet run --project benchmarks/Caustikon.Benchmarks/Caustikon.Benchmarks.csproj --configuration Release --no-build -- --filter '*'
```

Keep the full BenchmarkDotNet environment header and identify the source commit when reporting results. A span API is not a claim of explicit SIMD or parallel execution.

An optimization is not acceptable if it changes documented statuses, weakens input validation, or makes behavior near a branch boundary depend on batch layout.

## Public API

- Prefer explicit status values for expected physical outcomes.
- Do not use exceptions to report total internal reflection or an out-of-range dispersion model.
- Do not normalize, clamp, reverse, or substitute caller input unless the API contract says so.
- Add public types only when they carry domain meaning that cannot be expressed by the existing surface.
- Keep XML documentation exact about direction, units, valid ranges, and ownership of output buffers.

Breaking changes require a clear reason in the pull request and an entry in `CHANGELOG.md`. Before 1.0, compatibility is not guaranteed, but silent contract changes are still rejected.

## Documentation

Examples must compile against the current public API. Use ASCII punctuation, including the hyphen `-`. Do not publish benchmark numbers that cannot be reproduced from the benchmark project.

## License

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
