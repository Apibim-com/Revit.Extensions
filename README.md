<div align="center">
  <img src="Revit.Extensions/icon-large.png" alt="Revit.Extensions" />

# Revit.Extensions

[![CI](https://github.com/apibim/Revit.Extensions/actions/workflows/ci.yml/badge.svg)](https://github.com/apibim/Revit.Extensions/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Revit.Extensions?color=blue)](https://www.nuget.org/packages/Revit.Extensions)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Revit.Extensions)](https://www.nuget.org/packages/Revit.Extensions)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

</div>

A collection of extension methods for the **Autodesk Revit API**, designed to reduce boilerplate and make Revit add-in development more expressive. Supports **Revit 2020 – 2027**.

---

## Installation

```
dotnet add package Revit.Extensions
```

Or search for **`Revit.Extensions`** in the NuGet Package Manager inside Visual Studio.

---

## Revit version support

| Revit | Target framework | NuGet version |
| ----- | ---------------- | ------------- |
| 2020  | net47            | 2020.0.\*     |
| 2021  | net48            | 2021.0.\*     |
| 2022  | net48            | 2022.0.\*     |
| 2023  | net48            | 2023.0.\*     |
| 2024  | net48            | 2024.0.\*     |
| 2025  | net8.0-windows   | 2025.0.\*     |
| 2026  | net8.0-windows   | 2026.0.\*     |
| 2027  | net8.0-windows   | 2027.0.\*     |

The package automatically targets the correct framework — no manual configuration needed.

---

## API reference

### `GeometryExtensions`

Helpers for working with Revit geometry types (`XYZ`, `Outline`, `BoundingBoxXYZ`).

```csharp
using Revit.Extensions;

// Convert XYZ ↔ value tuple
XYZ point = new XYZ(1.0, 2.0, 3.0);
(double X, double Y, double Z) vec = point.ToVector();   // (1.0, 2.0, 3.0)
XYZ restored = vec.ToXYZ();                              // XYZ(1.0, 2.0, 3.0)

// Expand an Outline uniformly (default: 10 ft)
Outline outline = new Outline(new XYZ(0, 0, 0), new XYZ(5, 5, 5));
Outline expanded = outline.Extend(size: 2);   // min −2, max +2 in every axis

// Transform a BoundingBoxXYZ from local to world space
BoundingBoxXYZ bbox = element.get_BoundingBox(view);
BoundingBoxXYZ? worldBbox = bbox.TransformBoundingBox();
```

---

### `PointExtensions`

Unit conversion for `XYZ` coordinates using Revit's `UnitUtils`.

```csharp
using Autodesk.Revit.DB;
using Revit.Extensions;

// Revit stores coordinates in feet internally.
// Convert a point to meters (Revit 2021+):
XYZ pointInFeet = new XYZ(3.28084, 0, 0);
XYZ pointInMeters = pointInFeet.Recalculate(UnitTypeId.Meters);
// → XYZ(1.0, 0, 0)
```

> **Revit 2020** uses the legacy `DisplayUnitType` enum — the overload is selected automatically via conditional compilation.

---

### `ElementExtensions`

Helpers for collecting elements and grids from the active document.

```csharp
using Revit.Extensions;

// Collect selected elements, or fall back to all FamilyInstances + HostObjects in the active view
IList<Element> elements = ElementExtensions.GetAllElementOrSelected(uiDocument);

// Get all grids from the level closest to elevation 0
IList<Grid> grids = ElementExtensions.GetAllGridFromFirstLevel(document, out string? levelName);
if (levelName is not null)
    TaskDialog.Show("Grids", $"Found {grids.Count} grids on level '{levelName}'.");
```

---

### `AsyncTasksExecutor`

Run async code synchronously inside the Revit event loop, where `async/await` on the main thread is unsupported.

```csharp
using Revit.Extensions;

// Execute a Task<T> synchronously
string result = AsyncTasksExecutor.Execute(async () =>
{
    await Task.Delay(100);
    return "done";
});

// Execute a ValueTask<T> synchronously
int value = AsyncTasksExecutor.Execute(async () =>
{
    await Task.Yield();
    return 42;
});
```

---

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-extension`
3. Add your extension in `Revit.Extensions/Extensions/` with a corresponding test in `tests/Extensions/`
4. Open a pull request against `main`

---

## License

MIT © [Apibim SpA](https://apibim.com)
