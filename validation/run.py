#!/usr/bin/env python3
"""Validation harness for the engine review (PIANO-REVIEW.md, Phase 0).

For every scene in validation/scenes/ (or the ones named on the command line)
and every run in its `validation:` block it renders:

  mitsuba.pfm            Mitsuba 3 'path' integrator, the reference (cached)
  3dray-reference.*.pfm  3D-Ray with --reference (the unbiased estimator)
  3dray-default.*.pfm    3D-Ray as users run it (clamps, transparent shadows,
                         adaptive RR) but without the denoiser

and compares each 3D-Ray image with the reference (or with the scene's
`analytic_mean` when it has one): mean ratio, relMSE, SMAPE, share of bad
pixels, HDR-FLIP, plus false-colour difference maps. Results go to
validation/out/report.md and validation/out/report.json.

  python3 validation/run.py                       # every scene
  python3 validation/run.py cornell-lambert       # one scene
  python3 validation/run.py --convergence cornell-lambert:d32

--convergence renders the reference mode at growing spp and K independent
seeds, and fits the error slope: an unbiased estimator keeps falling as 1/N;
a plateau above the reference's own noise is bias.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import yaml

ROOT = Path(__file__).resolve().parent.parent
VALIDATION = ROOT / "validation"
SCENES = VALIDATION / "scenes"
OUT = VALIDATION / "out"
RAYTRACER = ROOT / "src" / "RayTracer" / "RayTracer.csproj"
IMAGECOMPARE = ROOT / "src" / "Tools" / "ImageCompare" / "ImageCompare.csproj"

sys.path.insert(0, str(VALIDATION))
import yaml2mitsuba  # noqa: E402


# ── I/O ──────────────────────────────────────────────────────────────────────

def write_pfm(path: Path, img: np.ndarray) -> None:
    """img: H x W x 3 float32, row 0 = top. PFM stores rows bottom to top."""
    h, w, _ = img.shape
    with open(path, "wb") as f:
        f.write(f"PF\n{w} {h}\n-1.0\n".encode("ascii"))
        f.write(np.ascontiguousarray(np.flipud(img), dtype="<f4").tobytes())


def read_pfm(path: Path) -> np.ndarray:
    with open(path, "rb") as f:
        magic = f.readline().strip()
        channels = 3 if magic == b"PF" else 1
        w, h = map(int, f.readline().split())
        scale = float(f.readline())
        dtype = "<f4" if scale < 0 else ">f4"
        data = np.frombuffer(f.read(), dtype=dtype, count=w * h * channels)
    img = data.reshape(h, w, channels)
    if channels == 1:
        img = np.repeat(img, 3, axis=2)
    return np.flipud(img).astype(np.float32)


def luminance(img: np.ndarray) -> np.ndarray:
    return 0.2126 * img[..., 0] + 0.7152 * img[..., 1] + 0.0722 * img[..., 2]


# ── Renderers ────────────────────────────────────────────────────────────────

def build() -> None:
    for proj in (RAYTRACER, IMAGECOMPARE):
        subprocess.run(["dotnet", "build", str(proj), "-c", "Release", "-v", "q", "-nologo"],
                       check=True, stdout=subprocess.DEVNULL)


def render_mitsuba(scene: Path, cfg: dict, run: dict, out: Path, variant: str) -> float:
    import mitsuba as mi
    mi.set_variant(variant)
    d, _ = yaml2mitsuba.convert(str(scene), cfg["width"], cfg["height"],
                                cfg["reference_spp"], run["depth"])
    t0 = time.time()
    img = np.array(mi.render(mi.load_dict(d), spp=cfg["reference_spp"]), dtype=np.float32)
    write_pfm(out, img[..., :3])
    return time.time() - t0


def render_3dray(scene: Path, cfg: dict, run: dict, out_png: Path, reference: bool,
                 spp: int | None = None, seed: int = 0, extra: list[str] | None = None) -> float:
    cmd = ["dotnet", "run", "--project", str(RAYTRACER), "-c", "Release", "--no-build", "--",
           "-i", str(scene), "-o", str(out_png),
           "-w", str(cfg["width"]), "-H", str(cfg["height"]),
           "-s", str(spp or cfg["spp"]), "-d", str(run["depth"]),
           "--aov", "beauty", "--aov-format", "pfm"]
    if reference:
        cmd.append("--reference")
    else:
        cmd += ["--denoiser", "none"]
    if seed:
        cmd += ["--seed", str(seed)]
    cmd += extra or []
    t0 = time.time()
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0 or "Error" in res.stdout:
        raise RuntimeError(f"3D-Ray failed on {scene.name}:\n{res.stdout}\n{res.stderr}")
    for line in res.stdout.splitlines():
        if "NaN/Inf" in line:
            print("   ", line.strip())
    return time.time() - t0


def beauty_of(png: Path) -> Path:
    return png.with_name(png.stem + ".beauty.pfm")


# ── Metrics ──────────────────────────────────────────────────────────────────

def image_compare(test: Path, ref: Path, diff: Path) -> dict:
    res = subprocess.run(
        ["dotnet", "run", "--project", str(IMAGECOMPARE), "-c", "Release", "--no-build", "--",
         str(test), str(ref), "--json", "--diff", str(diff)],
        capture_output=True, text=True, check=True)
    return json.loads(res.stdout.strip().splitlines()[-1])


def flip(test: np.ndarray, ref: np.ndarray) -> float | None:
    try:
        import flip_evaluator
        _, mean, _ = flip_evaluator.evaluate(np.maximum(ref, 0), np.maximum(test, 0), "HDR",
                                             applyMagma=False)
        return float(mean)
    except Exception as exc:  # FLIP is optional
        print("    FLIP unavailable:", exc)
        return None


def compare(test_path: Path, ref_path: Path | None, analytic: float | None, diff: Path) -> dict:
    test = read_pfm(test_path)
    if analytic is not None:
        lum = luminance(test)
        return {"MeanTest": float(lum.mean()), "MeanReference": analytic,
                "MeanRatio": float(lum.mean() / analytic),
                "MaxPixelRelError": float(np.max(np.abs(lum - analytic)) / analytic),
                "RelMse": float(np.mean((test - analytic) ** 2 / (analytic ** 2 + 0.01)))}
    m = image_compare(test_path, ref_path, diff)
    m["Flip"] = flip(test, read_pfm(ref_path))
    return m


# ── Driver ───────────────────────────────────────────────────────────────────

def load_cfg(scene: Path) -> dict:
    with open(scene, encoding="utf-8") as f:
        cfg = (yaml.safe_load(f) or {}).get("validation")
    if not cfg:
        raise ValueError(f"{scene.name}: no 'validation:' block")
    cfg.setdefault("runs", [{"name": f"d{cfg.get('depth', 16)}", "depth": cfg.get("depth", 16)}])
    return cfg


def validate(scene: Path, args) -> list[dict]:
    cfg = load_cfg(scene)
    rows = []
    for run in cfg["runs"]:
        out = OUT / scene.stem / run["name"]
        out.mkdir(parents=True, exist_ok=True)
        print(f"== {scene.stem} [{run['name']}]")
        analytic = cfg.get("analytic_mean")

        ref = out / "mitsuba.pfm"
        mitsuba_note = None
        if analytic is None or args.mitsuba_for_analytic:
            if args.force_reference or not ref.exists():
                try:
                    t = render_mitsuba(scene, cfg, run, ref, args.variant)
                    print(f"   mitsuba: {t:.1f}s")
                except yaml2mitsuba.UnsupportedFeature as exc:
                    mitsuba_note = f"not convertible: {exc}"
                    print("   mitsuba:", mitsuba_note)

        if mitsuba_note is None and analytic is None:
            import mitsuba as mi
            mi.set_variant(args.variant)
            _, used = yaml2mitsuba.convert(str(scene), cfg["width"], cfg["height"], 1, run["depth"])
            if used:
                mitsuba_note = "approx: " + ", ".join(sorted(used))
        row = {"scene": scene.stem, "run": run["name"], "depth": run["depth"], "spp": cfg["spp"],
               "reference_spp": cfg["reference_spp"], "analytic": analytic, "note": mitsuba_note}
        # Extra modes: the default mode plus the listed flags, e.g.
        #   extra_modes: { transparent: ["--transparent-shadows", "on"] }
        modes = [("reference", True, []), ("default", False, [])]
        modes += [(name, False, list(flags)) for name, flags in (cfg.get("extra_modes") or {}).items()]
        for mode, reference, flags in modes:
            if mode != "reference" and args.skip_default:
                continue
            png = out / f"3dray-{mode}.png"
            t = render_3dray(scene, cfg, run, png, reference, extra=flags)
            target = None if analytic is not None else (ref if ref.exists() else None)
            if target is None and analytic is None:
                continue
            m = compare(beauty_of(png), target, analytic, out / f"diff-{mode}.png")
            m["time"] = round(t, 1)
            row[mode] = m
            print(f"   3dray {mode:9s}: ratio={m['MeanRatio']:.4f} relMSE={m['RelMse']:.3e}"
                  + (f" FLIP={m['Flip']:.4f}" if m.get("Flip") is not None else "") + f" ({t:.1f}s)")
        rows.append(row)
    return rows


def convergence(spec: str, args) -> dict:
    name, _, run_name = spec.partition(":")
    scene = SCENES / f"{name}.yaml"
    cfg = load_cfg(scene)
    run = next(r for r in cfg["runs"] if not run_name or r["name"] == run_name)
    out = OUT / scene.stem / run["name"]
    out.mkdir(parents=True, exist_ok=True)
    ref = out / "mitsuba.pfm"
    analytic = cfg.get("analytic_mean")
    if analytic is None and not ref.exists():
        render_mitsuba(scene, cfg, run, ref, args.variant)
    ref_img = None if analytic is not None else read_pfm(ref)

    def rel_mse(img):
        r = np.full_like(img, analytic) if ref_img is None else ref_img
        return float(np.mean((img - r) ** 2 / (r ** 2 + 0.01)))

    result = {"scene": name, "run": run["name"], "spp": [], "spp_relmse": [], "k": [], "k_relmse": []}
    print(f"== convergence {name} [{run['name']}]")
    for spp in args.spp_ladder:
        png = out / f"conv-spp{spp}.png"
        render_3dray(scene, cfg, run, png, True, spp=spp)
        e = rel_mse(read_pfm(beauty_of(png)))
        result["spp"].append(spp); result["spp_relmse"].append(e)
        print(f"   spp={spp:6d} relMSE={e:.4e}")

    acc = None
    for k in range(1, args.k + 1):
        png = out / f"conv-seed{k}.png"
        render_3dray(scene, cfg, run, png, True, spp=args.k_spp, seed=k)
        img = read_pfm(beauty_of(png))
        acc = img if acc is None else acc + img
        e = rel_mse(acc / k)
        result["k"].append(k); result["k_relmse"].append(e)
        print(f"   K={k:3d} x {args.k_spp} spp  relMSE={e:.4e}")

    def slope(xs, ys):
        lx, ly = np.log(xs), np.log(ys)
        return float(np.polyfit(lx, ly, 1)[0])

    result["spp_slope"] = slope(result["spp"], result["spp_relmse"])
    result["k_slope"] = slope(result["k"], result["k_relmse"])
    # relMSE(K) ≈ a / K + b: b is the error that averaging cannot remove,
    # i.e. bias² (plus the reference's own noise).
    A = np.vstack([1.0 / np.array(result["k"]), np.ones(len(result["k"]))]).T
    a, b = np.linalg.lstsq(A, np.array(result["k_relmse"]), rcond=None)[0]
    result["k_fit_a"], result["k_fit_floor"] = float(a), float(b)
    print(f"   slope vs spp = {result['spp_slope']:.2f} (unbiased: <= -1), "
          f"slope vs K = {result['k_slope']:.2f} (unbiased: -1), floor = {b:.3e}")
    return result


def write_report(rows: list[dict], conv: list[dict]) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / "report.json", "w", encoding="utf-8") as f:
        json.dump({"rows": rows, "convergence": conv}, f, indent=2)
    lines = ["# Validation report", "",
             "| scene | run | mode | mean ratio | relMSE | FLIP | bad px | note |",
             "|---|---|---|---|---|---|---|---|"]
    for r in rows:
        modes = ["reference", "default"] + [k for k in r if k not in
                 ("reference", "default", "scene", "run", "depth", "spp", "reference_spp", "analytic", "note")]
        for mode in modes:
            m = r.get(mode)
            if not m:
                continue
            flip_v = m.get("Flip")
            bad = m.get("BadPixelFraction")
            lines.append(
                f"| {r['scene']} | {r['run']} | {mode} | {m['MeanRatio']:.4f} | {m['RelMse']:.3e} | "
                f"{'' if flip_v is None else f'{flip_v:.4f}'} | {'' if bad is None else f'{bad:.1%}'} | "
                f"{('analytic ' + str(r['analytic'])) if r['analytic'] is not None else (r['note'] or '')} |")
    for c in conv:
        lines += ["", f"## Convergence {c['scene']} [{c['run']}]", "",
                  f"slope vs spp {c['spp_slope']:.2f}, slope vs K {c['k_slope']:.2f}, "
                  f"floor {c['k_fit_floor']:.3e}"]
    (OUT / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\nReport: {OUT / 'report.md'}")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("scenes", nargs="*", help="scene stems (default: all)")
    p.add_argument("--force-reference", action="store_true", help="re-render the Mitsuba references")
    p.add_argument("--skip-default", action="store_true", help="skip the default-mode 3D-Ray render")
    p.add_argument("--mitsuba-for-analytic", action="store_true",
                   help="also render Mitsuba for scenes with an analytic answer")
    p.add_argument("--variant", default="llvm_ad_rgb", help="Mitsuba variant (default llvm_ad_rgb)")
    p.add_argument("--convergence", action="append", default=[], metavar="SCENE[:RUN]")
    p.add_argument("--spp-ladder", type=int, nargs="+", default=[16, 64, 256, 1024])
    p.add_argument("--k", type=int, default=8, help="independent seeds for the averaging test")
    p.add_argument("--k-spp", type=int, default=64)
    args = p.parse_args()

    build()
    rows = []
    if not args.convergence or args.scenes:
        names = args.scenes or sorted(s.stem for s in SCENES.glob("*.yaml"))
        for n in names:
            rows += validate(SCENES / f"{n}.yaml", args)
    conv = [convergence(c, args) for c in args.convergence]
    # A partial run keeps the other half of the previous report.
    previous = OUT / "report.json"
    if previous.exists():
        old = json.loads(previous.read_text(encoding="utf-8"))
        if not rows:
            rows = old.get("rows", [])
        if not conv:
            conv = old.get("convergence", [])
    write_report(rows, conv)


if __name__ == "__main__":
    main()
