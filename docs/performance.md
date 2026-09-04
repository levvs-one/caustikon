# Refraction measurements

Measured on 2026-09-05 with BenchmarkDotNet 0.15.8. All 16 cases reported **0 B allocated per operation** after setup. Inputs, outputs, and status arrays were allocated before measurement.

An operation processes the entire `Count` batch. The scalar baseline is a caller-written loop that stores the same directions and statuses as the span overload. Ordinary refraction and total internal reflection have separate baselines.

| Scenario | Rays per operation | Scalar loop mean | Span mean |
| --- | ---: | ---: | ---: |
| Air to glass | 1 | 28.39 ns | 41.51 ns |
| Air to glass | 16 | 425.44 ns | 504.37 ns |
| Air to glass | 1,024 | 27.84 us | 31.69 us |
| Air to glass | 1,000,000 | 27.70 ms | 31.64 ms |
| Total internal reflection | 1 | 20.86 ns | 31.53 ns |
| Total internal reflection | 16 | 317.14 ns | 373.61 ns |
| Total internal reflection | 1,024 | 20.43 us | 23.57 us |
| Total internal reflection | 1,000,000 | 19.96 ms | 22.67 ms |

For the million-ray refraction case, BenchmarkDotNet's 99.9% confidence-interval half-width was 0.73 ms for the scalar loop and 0.60 ms for the span call. The span API was about 14% slower at this size in this run. It provides checked buffer handling and caller-owned storage; it executes a scalar loop without internal parallelism. No separate wide-SIMD implementation is included.

The complete exported measurements, including error, standard deviation, allocation, and job settings, are in [benchmarks-2026-09-05.csv](benchmarks-2026-09-05.csv). The file uses a semicolon delimiter.

## Machine and method

- Intel Core i5-4670, 3.40 GHz, Haswell, 4 physical cores
- Windows 11, build 10.0.26200.9168
- .NET SDK 10.0.400, runtime 10.0.11, x64 RyuJIT
- Concurrent workstation GC; Release build
- One process launch, 5 warmup iterations, 15 measurement iterations, 250 ms target iteration time
- BenchmarkDotNet selected the high-performance power plan during measurement and restored the balanced plan afterward

The inputs are deterministic. Air-to-glass rays use incidence angles from 0.05 to 1.10 radians and indices 1 to 1.5. The TIR case uses 0.80 to 1.20 radians and indices 1.5 to 1. Every generated input is checked during setup. Azimuth varies across each batch.

This is a steady-state microbenchmark on one shared desktop. It does not measure cold start, a complete renderer, cache behavior in a host application, other processors, or other runtimes. Compare changes using the same machine and configuration.

## Reproduce

From the repository root:

```powershell
dotnet run --project benchmarks/Caustikon.Benchmarks -c Release -- --filter "*" --launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 250
```

The measured source files have these SHA-256 hashes before Git line-ending normalization:

```text
src/Caustikon/Dielectric.cs
EC19AE94AC1E669D928F000CAAE70774AE5705623F1D968BDAA5CD60D7651CA5

benchmarks/Caustikon.Benchmarks/DielectricBenchmarks.cs
2891EDFBED3C3C6DF114DB1A85A869CEBAB8D07AD019FC468E9D0D2A96752EE3
```
