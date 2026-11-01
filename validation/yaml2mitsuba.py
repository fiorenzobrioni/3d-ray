"""Converts the subset of the 3D-Ray YAML scene format used by the validation
scenes into a Mitsuba 3 scene dictionary (PIANO-REVIEW.md, Phase 0).

The mapping is deliberately strict: anything the converter cannot translate
exactly raises UnsupportedFeature instead of being approximated, so a
difference in the comparison is always the engine's, never the converter's.
Documented approximations (listed in APPROXIMATIONS) are the exception and are
reported with the results.

Conventions matched:
  format     scene format 2 (PIANO-REVIEW.md Phase 10)
  camera     vertical fov, look-at with up, thin lens radius = aperture / 2
  pixels     Gaussian reconstruction filter, sigma 0.5 px (the engine's default)
  depth      3D-Ray "-d D" lights paths up to D + 1 segments: Mitsuba max_depth = D + 1
  quad       q (corner), u, v; normal = normalize(cross(u, v)); emits on the front side only
  emissive   radiance = tint(color) * luminance, front side only, does not reflect
  sphere light / area light   radiance = tint(color) * luminance (visible proxy)
  point light                 intensity = tint(color) * intensity / metres-per-unit^2
                              (tint = the colour over its luminance)
  flat sky                    constant environment of radiance = color
  media (Phase 8)             homogeneous and grid, global (the sensor's medium) or
                              bound to an entity (interior); integrator volpath.
                              Grid: 3D-Ray's voxels sit on the box's corners, Mitsuba's
                              at cell centres, so the box grows by half a voxel (the
                              border voxels must be zero, or the clamp differs).
"""

from __future__ import annotations

import math

import yaml


class UnsupportedFeature(Exception):
    pass


# Model differences the converter cannot remove; reported next to the numbers.
APPROXIMATIONS = {
    "openpbr": "OpenPBR mapped to Mitsuba 'principled' through the Disney parameters (different "
               "layering, Kulla-Conty multiscatter, Zeltner fuzz: a gap is expected).",
}


def _vec(v, default=None):
    if v is None:
        if default is None:
            raise UnsupportedFeature("missing vector")
        return list(default)
    return [float(x) for x in v]


# The engine's default colour of a Lambertian (format 2: glass, metal and
# emitters default to white).
ENGINE_DEFAULT_COLOR = (0.5, 0.5, 0.5)


def _rgb(c, default=ENGINE_DEFAULT_COLOR):
    v = _vec(c, default)
    return {"type": "rgb", "value": v}


def _cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _norm(a):
    l = math.sqrt(sum(x * x for x in a))
    return [x / l for x in a]


def _rect_to_world(mi, corner, u, v):
    """Matrix mapping Mitsuba's rectangle [-1,1]^2 (normal +z) onto the
    parallelogram corner + s*u + t*v, normal along cross(u, v)."""
    n = _norm(_cross(u, v))
    c = [corner[i] + 0.5 * u[i] + 0.5 * v[i] for i in range(3)]
    m = [[0.5 * u[r], 0.5 * v[r], n[r], c[r]] for r in range(3)] + [[0, 0, 0, 1]]
    return mi.ScalarTransform4f(m)


def _camera(mi, scene, width, height, spp, camera_index=0):
    cams = scene.get("cameras")
    cam = cams[camera_index] if cams else None
    if cam is None:
        raise UnsupportedFeature("scene has no camera")
    origin = _vec(cam.get("position"))
    target = _vec(cam.get("look_at"))
    up = _vec(cam.get("up"), (0, 1, 0))
    aperture = float(cam.get("aperture", 0.0))
    fov = float(cam.get("fov", 60.0))
    # Physical camera keys, converted as SceneLoader.ApplyPhysicalCamera does.
    if cam.get("aperture_blades", 0) or float(cam.get("aperture_ratio", 1.0)) != 1.0:
        raise UnsupportedFeature("bladed or anamorphic aperture")
    sensor_height = float(cam.get("sensor_width", 36.0)) * height / width
    if cam.get("focal_length"):
        fov = math.degrees(2.0 * math.atan(sensor_height / (2.0 * float(cam["focal_length"]))))
    if cam.get("f_stop"):
        focal = float(cam.get("focal_length") or sensor_height / (2.0 * math.tan(math.radians(fov) / 2.0)))
        aperture = focal / float(cam["f_stop"]) / 1000.0 / _metres_per_unit(scene)
    sensor = {
        "type": "perspective" if aperture <= 0 else "thinlens",
        "fov_axis": "y",
        "fov": fov,
        "to_world": mi.ScalarTransform4f().look_at(origin=origin, target=target, up=up),
        "film": {
            "type": "hdrfilm", "width": width, "height": height,
            # Same filter as the engine's default (--filter gaussian): sigma
            # 0.5 px, truncated at 4 sigma and shifted to zero there.
            "rfilter": {"type": "gaussian", "stddev": 0.5}, "pixel_format": "rgb",
        },
        "sampler": {"type": "independent", "sample_count": spp},
    }
    if aperture > 0:
        sensor["aperture_radius"] = aperture / 2.0
        sensor["focus_distance"] = float(cam.get("focus_distance", 1.0))
    return sensor


def _metres_per_unit(scene):
    length = (scene.get("units") or {}).get("length", 1.0)
    names = {"m": 1.0, "cm": 0.01, "mm": 0.001, "km": 1000.0, "in": 0.0254, "ft": 0.3048}
    return names[length] if isinstance(length, str) else float(length)


def _luminance(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def _tint(c):
    # Format 2: a light's colour is a tint of luminance 1.
    lum = _luminance(c)
    return [x / lum for x in c] if lum > 0 else [1.0, 1.0, 1.0]


def _two_sided(b):
    # 3D-Ray's opaque surfaces shade both faces; Mitsuba's are one-sided.
    return {"type": "twosided", "bsdf": b}


def _bsdf(m, used):
    b = _bsdf_one_sided(m, used)
    if b is None or b["type"] in ("dielectric", "roughdielectric", "null"):
        return b
    if b["type"] == "principled" and b["spec_trans"] > 0:
        return b
    return _two_sided(b)


def _bsdf_one_sided(m, used):
    t = (m.get("type") or "").lower()
    color = m.get("color")
    if t == "lambertian":
        return {"type": "diffuse", "reflectance": _rgb(color)}
    if t == "dielectric":
        if any(abs(c - 1.0) > 1e-6 for c in _vec(color, (1.0, 1.0, 1.0))):
            raise UnsupportedFeature("tinted dielectric")
        ior = float(m.get("ior", 1.5))
        if abs(ior - 1.0) < 1e-6:
            # Index-matched: no reflection, no bending. Mitsuba's pass-through BSDF.
            return {"type": "null"}
        rough = float(m.get("roughness") or 0.0)
        if rough > 0:
            # Walter 2007 with GGX and height-correlated Smith, as the engine.
            return {"type": "roughdielectric", "int_ior": ior, "ext_ior": 1.0, "distribution": "ggx",
                    "alpha": max(rough * rough, 1e-4), "sample_visible": True}
        return {"type": "dielectric", "int_ior": ior, "ext_ior": 1.0}
    if t == "metal":
        # 3D-Ray Metal: GGX with height-correlated Smith (as Mitsuba's), alpha
        # = fuzz^2, Fresnel from a measured preset / eta + k (exact twin of a
        # Mitsuba conductor) or the F82 model from color (exact twin only for
        # white, where F = 1). Mitsuba has no multiple-scattering lobe, so a
        # rough metal must set multiple_scattering: false to be compared.
        fuzz = float(m.get("roughness") or 0.0)
        if fuzz > 0 and m.get("multiple_scattering", True):
            raise UnsupportedFeature("rough Metal with multiple scattering (Mitsuba has none)")
        presets = {"gold": "Au", "silver": "Ag", "copper": "Cu", "aluminium": "Al", "chromium": "Cr",
                   "tungsten": "W", "nickel": "Ni_palik", "iridium": "Ir", "molybdenum": "Mo",
                   "rhodium": "Rh", "tantalum": "Ta", "titanium_nitride": "TiN", "vanadium": "V_palik",
                   "beryllium": "Be", "mercury": "Hg"}
        if m.get("preset"):
            fres = {"material": presets[m["preset"].lower()]}
        elif m.get("eta") is not None:
            fres = {"eta": _rgb(m["eta"]), "k": _rgb(m.get("k", [0, 0, 0]))}
        else:
            if any(abs(c - 1.0) > 1e-6 for c in _vec(color, (1.0, 1.0, 1.0))) or m.get("edge_color"):
                raise UnsupportedFeature("coloured F82 Metal (no exact Mitsuba twin)")
            fres = {"material": "none"}
        if fuzz <= 0:
            return {"type": "conductor", **fres}
        return {"type": "roughconductor", **fres, "distribution": "ggx",
                "alpha": max(fuzz * fuzz, 1e-4), "sample_visible": True}   # the engine's floor
    if t == "openpbr":
        # Compared with Mitsuba's principled BSDF through the inverse of the
        # Disney mapping (RayTracer.Materials.DisneyMaterial), for the inputs
        # both models share.
        used.add("openpbr")
        for key in ("subsurface_weight", "thin_film_weight", "transmission_depth", "geometry_thin_walled",
                    "base_diffuse_roughness", "coat_color", "specular_color", "fuzz_color", "emission_luminance"):
            if m.get(key):
                raise UnsupportedFeature(f"openpbr input '{key}'")
        tw = float(m.get("transmission_weight", 0.0))
        coat_r = float(m.get("coat_roughness", 0.0))
        b = {
            "type": "principled",
            "base_color": _rgb(m.get("base_color", [0.8, 0.8, 0.8])),
            "metallic": float(m.get("base_metalness", 0.0)),
            "roughness": float(m.get("specular_roughness", 0.3)),
            "specular": float(m.get("specular_weight", 1.0)) / 2.0,
            "spec_tint": 0.0,
            "sheen": float(m.get("fuzz_weight", 0.0)),
            "sheen_tint": 0.0,
            "clearcoat": float(m.get("coat_weight", 0.0)),
            # coat_roughness = sqrt(lerp(0.1, 0.001, gloss)).
            "clearcoat_gloss": min(1.0, max(0.0, (0.1 - coat_r * coat_r) / 0.099)),
            "spec_trans": tw,
            "anisotropic": float(m.get("specular_roughness_anisotropy", 0.0)) / 0.9,
        }
        if b["clearcoat"] > 0 and abs(float(m.get("coat_ior", 1.6)) - 1.5) > 1e-6:
            raise UnsupportedFeature("coat_ior other than 1.5 (Mitsuba's clearcoat)")
        if b["sheen"] > 0 and abs(float(m.get("fuzz_roughness", 0.5)) - 0.3) > 1e-6:
            raise UnsupportedFeature("fuzz_roughness other than 0.3")
        # Mitsuba derives eta from 'specular' unless eta is given, and refuses
        # both: transmissive materials pass the IOR, opaque ones the specular.
        if tw > 0:
            del b["specular"]
            b["eta"] = float(m.get("specular_ior", 1.5))
        return b
    if t == "emissive":
        return None
    raise UnsupportedFeature(f"material type '{t}'")


def _phase(md):
    p = (md.get("phase") or ("hg" if md.get("g") is not None else "isotropic")).lower()
    if p == "isotropic":
        return {"type": "isotropic"}
    if p in ("hg", "henyey_greenstein"):
        return {"type": "hg", "g": float(md.get("g", 0.0))}
    if p == "rayleigh":
        return {"type": "rayleigh"}
    raise UnsupportedFeature(f"phase function '{p}'")


def _medium(md):
    """A 3D-Ray medium as a Mitsuba one (homogeneous or grid)."""
    import mitsuba as mi
    import numpy as np

    t = (md.get("type") or "homogeneous").lower()
    sa = np.array(_vec(md.get("sigma_a"), (0, 0, 0)), dtype=np.float64)
    ss = np.array(_vec(md.get("sigma_s"), (0, 0, 0)), dtype=np.float64)
    st = sa + ss
    albedo = [float(ss[c] / st[c]) if st[c] > 0 else 0.0 for c in range(3)]
    if t == "homogeneous":
        # An RGB spectrum must lie in [0, 1]: the extinction goes through 'scale'.
        scale = float(max(st.max(), 1e-12))
        return {"type": "homogeneous", "sigma_t": {"type": "rgb", "value": (st / scale).tolist()}, "scale": scale,
                "albedo": {"type": "rgb", "value": albedo}, "phase": _phase(md), "sample_emitters": True}
    if t == "grid":
        if md.get("file") or (md.get("interpolation") or "trilinear").lower() != "trilinear":
            raise UnsupportedFeature("grid from a file or with a tricubic filter")
        nx, ny, nz = int(md["nx"]), int(md["ny"]), int(md["nz"])
        d = np.clip(np.array(md["data"], dtype=np.float32).reshape(nz, ny, nx), 0.0, 1.0)
        if d[0].any() or d[-1].any() or d[:, 0].any() or d[:, -1].any() or d[:, :, 0].any() or d[:, :, -1].any():
            raise UnsupportedFeature("grid with density on its border voxels")
        grid = np.stack([d * float(st[c]) for c in range(3)], axis=-1)
        lo = np.array(_vec(md["bounds_min"]), dtype=np.float64)
        hi = np.array(_vec(md["bounds_max"]), dtype=np.float64)
        half = (hi - lo) / (np.array([nx, ny, nz]) - 1) * 0.5
        lo, hi = lo - half, hi + half
        to_world = mi.ScalarTransform4f().translate(lo.tolist()).scale((hi - lo).tolist())
        return {"type": "heterogeneous", "phase": _phase(md), "sample_emitters": True, "scale": 1.0,
                "albedo": {"type": "rgb", "value": albedo},
                "sigma_t": {"type": "gridvolume", "grid": mi.VolumeGrid(grid), "to_world": to_world,
                            "filter_type": "trilinear"}}
    raise UnsupportedFeature(f"medium type '{t}'")


def convert(path: str, width: int, height: int, spp: int, depth: int, camera_index: int = 0):
    """Returns (mitsuba scene dict, set of approximation keys used)."""
    import mitsuba as mi

    with open(path, encoding="utf-8") as f:
        scene = yaml.safe_load(f)
    if scene.get("imports") or scene.get("templates"):
        raise UnsupportedFeature("imports/templates")

    used: set[str] = set()
    d = {
        "type": "scene",
        "integrator": {"type": "path", "max_depth": depth + 1, "rr_depth": 5},
        "sensor": _camera(mi, scene, width, height, spp, camera_index),
    }

    world = scene.get("world") or {}
    media = {m["id"].lower(): m for m in scene.get("mediums") or []}
    has_media = bool(world.get("medium")) or bool(media)
    if world.get("medium"):
        d["sensor"]["medium"] = _medium(world["medium"])
    sky = world.get("sky") or {"type": "flat", "color": [0, 0, 0]}
    if (sky.get("type") or "flat").lower() != "flat":
        raise UnsupportedFeature(f"sky type '{sky.get('type')}'")
    sky_color = _vec(sky.get("color"), (0, 0, 0))
    if any(c > 0 for c in sky_color):
        d["env"] = {"type": "constant", "radiance": {"type": "rgb", "value": sky_color}}

    materials = {m["id"]: m for m in scene.get("materials") or []}

    for i, e in enumerate(scene.get("entities") or []):
        t = (e.get("type") or "").lower()
        for key in ("transform", "rotate", "scale", "translate", "children", "visible_to_camera",
                    "exterior_medium", "visibility"):
            if key in e:
                raise UnsupportedFeature(f"entity key '{key}'")
        m = materials.get(e.get("material"))
        if m is None:
            raise UnsupportedFeature(f"entity {i}: missing material '{e.get('material')}'")
        if t == "sphere":
            shape = {"type": "sphere", "center": _vec(e.get("center"), (0, 0, 0)),
                     "radius": float(e.get("radius", 1.0))}
        elif t == "quad":
            shape = {"type": "rectangle",
                     "to_world": _rect_to_world(mi, _vec(e["q"]), _vec(e["u"]), _vec(e["v"]))}
        else:
            raise UnsupportedFeature(f"entity type '{t}'")

        bsdf = _bsdf(m, used)
        if bsdf is None:  # emissive
            shape["bsdf"] = {"type": "diffuse", "reflectance": {"type": "rgb", "value": [0, 0, 0]}}
            radiance = [c * float(m.get("luminance", 1.0)) for c in _tint(_vec(m.get("color"), (1.0, 1.0, 1.0)))]
            shape["emitter"] = {"type": "area", "radiance": {"type": "rgb", "value": radiance}}
        else:
            shape["bsdf"] = bsdf
        if e.get("interior_medium"):
            shape["interior"] = _medium(media[e["interior_medium"].lower()])
        d[f"entity_{i}"] = shape

    # Mitsuba's 'path' integrator lets shadow rays stop at pass-through (null)
    # surfaces; 'volpath' crosses them, as 3D-Ray does for an IOR-1 shell.
    if has_media or any(isinstance(v, dict) and v.get("bsdf", {}).get("type") == "null" for v in d.values()):
        d["integrator"]["type"] = "volpath"

    for i, l in enumerate(scene.get("lights") or []):
        t = (l.get("type") or "").lower()
        color = _tint(_vec(l.get("color"), (1, 1, 1)))
        mpu = _metres_per_unit(scene)
        if l.get("power") is not None or l.get("temperature") is not None or l.get("exposure"):
            raise UnsupportedFeature("light power, temperature or exposure")
        # Candela carries metres; luminance does not.
        k = float(l.get("intensity", 1.0)) / (mpu * mpu) if t in ("point", "spot") else float(l.get("luminance", 1.0))
        value = [c * k for c in color]
        if l.get("soft_radius"):
            raise UnsupportedFeature("soft_radius")
        if t == "point":
            d[f"light_{i}"] = {"type": "point", "position": _vec(l.get("position")),
                               "intensity": {"type": "rgb", "value": value}}
        elif t == "sphere":
            d[f"light_{i}"] = {
                "type": "sphere", "center": _vec(l.get("position")), "radius": float(l.get("radius", 0.5)),
                "bsdf": {"type": "diffuse", "reflectance": {"type": "rgb", "value": [0, 0, 0]}},
                "emitter": {"type": "area", "radiance": {"type": "rgb", "value": value}},
            }
        elif t == "area":
            d[f"light_{i}"] = {
                "type": "rectangle",
                "to_world": _rect_to_world(mi, _vec(l.get("corner")), _vec(l.get("u")), _vec(l.get("v"))),
                "bsdf": {"type": "diffuse", "reflectance": {"type": "rgb", "value": [0, 0, 0]}},
                "emitter": {"type": "area", "radiance": {"type": "rgb", "value": value}},
            }
        else:
            raise UnsupportedFeature(f"light type '{t}'")

    return d, used
