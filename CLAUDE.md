# Apibim.Revit.Extensions — Developer Guide

## Project overview

Open-source library of Revit API extension methods, designed to support all Revit versions from 2019 to 2027.
Published as a NuGet package: `Apibim.Revit.Extensions`.

## Repository structure

```
Apibim.Revit.Extensions/
├── .github/workflows/
│   ├── ci.yml          ← Build + test on every push / PR
│   └── cd.yml          ← Pack + publish NuGet on tag v*.*.*
├── source/
│   └── Apibim.Revit.Extensions/
│       ├── Apibim.Revit.Extensions.csproj
│       └── Extensions/
│           ├── ElementExtensions.cs
│           ├── GeometryExtensions.cs
│           ├── PointExtensions.cs
│           ├── DocumentExtensions.cs
│           └── AsyncTasksExecutor.cs
├── tests/
│   └── Apibim.Revit.Extensions.Tests/
│       ├── Apibim.Revit.Extensions.Tests.csproj
│       └── Extensions/
│           ├── GeometryExtensionsTests.cs
│           ├── AsyncTasksExecutorTests.cs
│           └── PointExtensionsTests.cs
├── Build.props                  ← Multi-Revit-version build logic (user-provided)
├── Directory.Build.props        ← Imports Build.props; sets Nullable/ImplicitUsings/LangVersion
├── nuget.config
├── .gitignore
└── Apibim.Revit.Extensions.sln
```

## Build configurations

| Configuration | Revit | TargetFramework |
|---------------|-------|-----------------|
| R2019         | 2019  | net47           |
| R2020         | 2020  | net47           |
| R2021         | 2021  | net48           |
| R2022         | 2022  | net48           |
| R2023         | 2023  | net48           |
| R2024         | 2024  | net48           |
| R2025         | 2025  | net8.0-windows  |
| R2026         | 2026  | net8.0-windows  |
| R2027         | 2027  | net8.0-windows  |

Each configuration sets `REVIT_<year>` as a compile-time constant (e.g. `REVIT_2025`).

## Build commands

```bash
# Build for a specific Revit version
dotnet build source/Apibim.Revit.Extensions -c R2026

# Build all versions (PowerShell)
foreach ($c in @("R2021","R2022","R2023","R2024","R2025","R2026")) {
    dotnet build source/Apibim.Revit.Extensions -c $c
}

# Run tests (always with R2025 — only version that works headless)
dotnet test tests/Apibim.Revit.Extensions.Tests -c R2025

# Create NuGet package
dotnet pack source/Apibim.Revit.Extensions -c R2026 --output ./nupkgs
```

## NuGet package

- **PackageId:** `Apibim.Revit.Extensions`
- **Version scheme:** `{RevitYear}.0.{patch}` → e.g. `2026.0.1`
- **Trigger CD:** push a tag matching `v*.*.*` → `git tag v2026.0.1 && git push --tags`
- **Secret required:** `NUGET_API_KEY` in GitHub repository secrets

## Conditional compilation

Use `#if REVIT_2020 ... #else ... #endif` to branch on Revit API differences.
Example in `PointExtensions.cs`:

```csharp
#if REVIT_2020
    public static XYZ Recalculate(this XYZ point, DisplayUnitType unit) ...
#else
    public static XYZ Recalculate(this XYZ point, ForgeTypeId unit) ...
#endif
```

## Adding new extensions

1. Create `source/Apibim.Revit.Extensions/Extensions/MyExtensions.cs`
2. Use `namespace Apibim.Revit.Extensions;`
3. Mark class `public static`
4. Add unit tests in `tests/.../Extensions/MyExtensionsTests.cs`

## Notes

- `AsyncTasksExecutor` and `GeometryExtensions` tests run headless (no Revit process needed).
- `PointExtensions` tests require a live Revit process — marked `[Fact(Skip = ...)]`.
- `ElementExtensions` tests require `Document`/`UIDocument` — use Moq or test inside Revit.
- Revit API package: `Revit_All_Main_Versions_API_x64` from nuget.org, versioned by `$(RevitVersion).*`.
