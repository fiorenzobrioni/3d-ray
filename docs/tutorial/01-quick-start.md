# Tutorial 01: Quick Start and Usage

Welcome to the 3D-Ray engine! This guide will help you set up your environment, build the project, and launch your first render in minutes.

---

## 1. Prerequisites
To run the renderer, you need:
- **.NET 10 SDK** (or higher).
- A multi-core CPU (the engine scales linearly with core count).

## 2. Compilation
Open a terminal in the project folder and run:
```powershell
dotnet build src/RayTracer/RayTracer.csproj -c Release
```

## 3. Your First Render
The easiest way to start is by rendering one of the included sample scenes:

```powershell
# Run a quick draft render of the Newton's Cradle scene
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i scenes/pendolo-newton.yaml -s 16 -w 800 -H 450
```

The resulting image will be automatically saved in the `output/` folder.

---

## 4. CLI Parameters Guide
You can customize every render via the command line:

| Parameter | Alias | Default | Description |
|-----------|-------|---------|-------------|
| `--input` | `-i` | — | **Required.** Path to the scene YAML file. |
| `--output`| `-o` | `output/` | Image output path (PNG, JPG, or BMP). |
| `--width` | `-w` | `1200` | Image width in pixels. |
| `--height`| `-H` | `800` | Image height in pixels. |
| `--samples`| `-s` | `16` | Samples per pixel (quality/noise). |
| `--depth` | `-d` | `50` | Maximum light bounces. |
| `--shadow-samples`| `-S` | *(YAML)* | Shadow quality for Area Lights. |
| `--camera`| `-c` | `0` | Select a specific camera by name or index. |

> [!TIP]
> The number of samples (`-s`) is always rounded up to the nearest perfect square (e.g., `-s 20` becomes `25`).

---

## 5. Rendering Strategy (Workflow)
Don't jump straight into high-quality renders! Use this iterative approach:

1. **Preview (Seconds)**: `-w 400 -s 1 -d 5` -> Check composition and lighting.
2. **Draft (Minutes)**: `-w 800 -s 16 -d 20` -> Check materials and colors.
3. **Final (Hours)**: `-w 1920 -s 256 -d 50` -> Clean, noise-free final render.

---

## 6. Common Troubleshooting
- **Black Image**: Ensure there are lights in the scene or that the camera isn't "inside" an object.
- **Too Much Noise**: Increase samples (`-s`) or shadow samples (`-S`).
- **Very Slow**: Reduce resolution or samples during testing. Always compile in `-c Release` mode.

---

[Go to Tutorial 02: Building a Scene](./02-building-a-scene.md) | [Back to README](../../README.md)
