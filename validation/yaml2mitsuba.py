"""Converts the subset of the 3D-Ray YAML scene format used by the validation
scenes into a Mitsuba 3 scene dictionary (PIANO-REVIEW.md, Phase 0).

The mapping is deliberately strict: anything the converter cannot translate
exactly raises UnsupportedFeature instead of being approximated, so a
difference in the comparison is always the engine's, never the converter's.
Documented approximations (listed in APPROXIMATIONS) are the exception and are
reported with the results.

Conventions matched:
  camera     vertical fov, look-at with vup, thin lens radius = aperture / 2
  pixels     box reconstruction filter (3D-Ray jitters uniformly in the pixel)
  depth      3D-Ray "-d D" lights paths up to D + 1 segments: Mitsuba max_depth = D + 1
  quad       q (corner), u, v; normal = normalize(cross(u, v)); emits on the front side only
  emissive   radiance = color * intensity, front side only, does not reflect
  sphere light / area light   radiance = color * intensity (visible proxy)
  point light                 radiant intensity = color * intensity (W/sr)
  flat sky                    constant environment of radiance = color
"""

from __future__ import annotations

import math

import yaml


class UnsupportedFeature(Exception):
    pass


# Model differences the converter cannot remove; reported next to the numbers.
APPROXIMATIONS = {
    "disney": "Disney BSDF mapped to Mitsuba 'principled' (Disney 2015 base model; "
              "3D-Ray adds Kulla-Conty multiscatter and Charlie sheen, so a gap is expected).",
}


def _vec(v, default=None):
    if v is None:
        if default is None:
            raise UnsupportedFeature("missing vector")
        return list(default)
    return [float(x) for x in v]


# The engine's default for a material without 'color' (SceneLoader): grey 0.5,
# for every material type, glass and emitters included.
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
    cam = cams[camera_index] if cams else scene.get("camera")
    if cam is None:
        raise UnsupportedFeature("scene has no camera")
    origin = _vec(cam.get("position"))
    target = _vec(cam.get("look_at"))
    up = _vec(cam.get("vup"), (0, 1, 0))
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
        aperture = focal / float(cam["f_stop"]) / 1000.0 / float(cam.get("meters_per_unit", 1.0))
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
        sensor["focus_distance"] = float(cam.get("focal_dist", 1.0))
    return sensor


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
        if any(abs(c - 1.0) > 1e-6 for c in _vec(color, ENGINE_DEFAULT_COLOR)):
            raise UnsupportedFeature("tinted dielectric")
        ior = float(m.get("refraction_index", 1.5))
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
        # `roughness` is the loader's alias of `fuzz` for a metal.
        fuzz = float(m.get("fuzz", m.get("roughness", 0.0)))
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
            if any(abs(c - 1.0) > 1e-6 for c in _vec(color, ENGINE_DEFAULT_COLOR)) or m.get("edge_color"):
                raise UnsupportedFeature("coloured F82 Metal (no exact Mitsuba twin)")
            fres = {"material": "none"}
        if fuzz <= 0:
            return {"type": "conductor", **fres}
        return {"type": "roughconductor", **fres, "distribution": "ggx",
                "alpha": max(fuzz * fuzz, 1e-4), "sample_visible": True}   # the engine's floor
    if t in ("disney", "disney_bsdf", "pbr"):
        used.add("disney")
        for key in ("subsurface", "diff_trans", "thin_film_thickness", "transmission_color",
                    "transmission_depth", "coat_roughness", "thin_walled"):
            if m.get(key):
                raise UnsupportedFeature(f"disney parameter '{key}'")
        b = {
            "type": "principled",
            "base_color": _rgb(color),
            "metallic": float(m.get("metallic", 0.0)),
            "roughness": float(m.get("roughness", 0.5)),
            "specular": float(m.get("specular", 0.5)),
            "spec_tint": float(m.get("specular_tint", 0.0)),
            "sheen": float(m.get("sheen", 0.0)),
            "sheen_tint": float(m.get("sheen_tint", 0.5)),
            "clearcoat": float(m.get("clearcoat", 0.0)),
            "clearcoat_gloss": float(m.get("clearcoat_gloss", 1.0)),
            "spec_trans": float(m.get("spec_trans") or 0.0),
            "anisotropic": float(m.get("anisotropic", 0.0)),
        }
        # Mitsuba derives eta from 'specular' unless eta is given, and refuses
        # both: transmissive materials pass the IOR, opaque ones the specular.
        if b["spec_trans"] > 0:
            del b["specular"]
            b["eta"] = float(m.get("ior", 1.5))
        return b
    if t == "emissive":
        return None
    raise UnsupportedFeature(f"material type '{t}'")


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
    if world.get("medium"):
        raise UnsupportedFeature("world medium")
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
                    "interior_medium", "visibility"):
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
            radiance = [c * float(m.get("intensity", 1.0)) for c in _vec(m.get("color"), ENGINE_DEFAULT_COLOR)]
            shape["emitter"] = {"type": "area", "radiance": {"type": "rgb", "value": radiance}}
        else:
            shape["bsdf"] = bsdf
        d[f"entity_{i}"] = shape

    # Mitsuba's 'path' integrator lets shadow rays stop at pass-through (null)
    # surfaces; 'volpath' crosses them, as 3D-Ray does for an IOR-1 shell.
    if any(isinstance(v, dict) and v.get("bsdf", {}).get("type") == "null" for v in d.values()):
        d["integrator"]["type"] = "volpath"

    for i, l in enumerate(scene.get("lights") or []):
        t = (l.get("type") or "").lower()
        color = _vec(l.get("color"), (1, 1, 1))
        k = float(l.get("intensity", 1.0))
        value = [c * k for c in color]
        if l.get("soft_radius"):
            raise UnsupportedFeature("soft_radius")
        if t == "point":
            d[f"light_{i}"] = {"type": "point", "position": _vec(l.get("position")),
                               "intensity": {"type": "rgb", "value": value}}
        elif t in ("sphere", "sphere_light", "ball", "ball_light"):
            d[f"light_{i}"] = {
                "type": "sphere", "center": _vec(l.get("position")), "radius": float(l.get("radius", 0.5)),
                "bsdf": {"type": "diffuse", "reflectance": {"type": "rgb", "value": [0, 0, 0]}},
                "emitter": {"type": "area", "radiance": {"type": "rgb", "value": value}},
            }
        elif t in ("area", "area_light", "rect", "rect_light"):
            d[f"light_{i}"] = {
                "type": "rectangle",
                "to_world": _rect_to_world(mi, _vec(l.get("corner")), _vec(l.get("u")), _vec(l.get("v"))),
                "bsdf": {"type": "diffuse", "reflectance": {"type": "rgb", "value": [0, 0, 0]}},
                "emitter": {"type": "area", "radiance": {"type": "rgb", "value": value}},
            }
        else:
            raise UnsupportedFeature(f"light type '{t}'")

    return d, used
