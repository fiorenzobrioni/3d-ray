# HeightField Procedurale — Terrain senza triangoli

Implementazione di un nuovo primitivo `HeightField` nel ray tracer che definisce la superficie del terreno come `y = f(x, z)` dove `f` è una `NoiseTexture` (Musgrave, fBm, ridged, ecc.), intersecato via ray marching. **Zero triangoli, dettaglio infinito.**

## User Review Required

> [!IMPORTANT]
> **Sintassi YAML**: la proposta sotto usa `height_texture` come blocco `TextureData` inline. Vuoi un nome diverso per questo campo?

> [!IMPORTANT]
> **Materiale per strato**: nella Fase 1 il HeightField usa un singolo materiale. Vuoi che già dalla Fase 1 supporti un color ramp basato su altitudine e pendenza (come StratumClassifier di TerrainGen), oppure lo aggiungiamo in una fase successiva?

## Open Questions

1. **Dimensioni tipiche**: i tuoi terrain TerrainGen usano `Size = 100` con `heightScale = Size * 0.25 = 25`. Vuoi gli stessi default per il HeightField (`bounds: [-50, -50, 50, 50]`, `max_height: 25`)?

2. **Acqua**: vuoi che il HeightField supporti un piano d'acqua integrato (un `sea_level` che genera automaticamente una superficie riflettente), oppure lo aggiungi come entità separata nella scena?

3. **Performance vs qualità**: il ray marching con Musgrave a 8 ottave sarà ~5-10× più lento per raggio rispetto a un BVH di triangoli. È accettabile per questo uso? (Possiamo mitigare con LOD automatico via `ComputeMaxOctaves`).

---

## Proposed Changes

### Primitivo HeightField (Core)

#### [NEW] [HeightField.cs](file:///c:/Fiorenzo/GitHub-Personale/3d-ray/src/RayTracer/Geometry/HeightField.cs)

Nuovo `IHittable` — ~250-300 righe. Struttura:

```csharp
public class HeightField : IHittable
{
    // ── Parametri ─────────────────────────────────────────────
    float XMin, ZMin, XMax, ZMax;   // estensione planare
    float MaxHeight;                 // altezza massima (per AABB)
    ITexture HeightTexture;          // NoiseTexture con Musgrave
    float HeightScale;               // moltiplicatore altezza
    IMaterial Material;

    // ── Ray marching ──────────────────────────────────────────
    int MaxSteps = 128;              // limite iterazioni
    float Epsilon = 1e-4f;           // soglia convergenza
    
    // ── IHittable ─────────────────────────────────────────────
    bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec);
    AABB BoundingBox();
    int Seed { get; set; }
}
```

**Algoritmo di intersezione** — 3 fasi:

1. **AABB slab test**: calcola `tEnter`/`tExit` per il box `[XMin, 0, ZMin] → [XMax, MaxHeight, ZMax]`
2. **Ray marching con step fisso**: passo lungo il raggio con step adattivo; ad ogni passo valuta `h = HeightScale * luminance(HeightTexture.Value(u, v, p))` e confronta con `p.Y`. Quando `p.Y < h` (il raggio è sceso sotto la superficie), si è trovato un intervallo di bracketing
3. **Bisection refinement**: 6-8 passi di bisezione nell'intervallo `[t_prev, t_curr]` per convergere al punto esatto

**Normale analitica** — finite-difference sul gradiente:
```csharp
float delta = 0.001f;
float hL = SampleHeight(x - delta, z);
float hR = SampleHeight(x + delta, z);
float hD = SampleHeight(x, z - delta);
float hU = SampleHeight(x, z + delta);
Vector3 normal = Vector3.Normalize(new Vector3(hL - hR, 2 * delta, hD - hU));
```

**UV mapping** — planare normalizzato:
```csharp
rec.U = (hitX - XMin) / (XMax - XMin);
rec.V = (hitZ - ZMin) / (ZMax - ZMin);
```

**TBN basis** — per normal/bump mapping:
```csharp
rec.Tangent = Vector3.Normalize(new Vector3(1, (hR - hL) / (2*delta), 0));
rec.Bitangent = Vector3.Cross(normal, rec.Tangent);
```

---

### Integrazione YAML — SceneData

#### [MODIFY] [SceneData.cs](file:///c:/Fiorenzo/GitHub-Personale/3d-ray/src/RayTracer/Scene/SceneData.cs)

Aggiungere campi a `EntityData` (dopo la sezione `// Plane`, ~riga 944):

```csharp
// ── HeightField (procedural terrain) ─────────────────────────────────────
[YamlMember(Alias = "bounds")]
public List<float>? Bounds { get; set; }        // [xmin, zmin, xmax, zmax]

[YamlMember(Alias = "max_height")]
public float MaxHeight { get; set; } = 25f;

[YamlMember(Alias = "height_scale")]
public float HeightScale { get; set; } = 1f;

[YamlMember(Alias = "height_texture")]
public TextureData? HeightTexture { get; set; }

[YamlMember(Alias = "max_steps")]
public int MaxSteps { get; set; } = 128;
```

---

### Integrazione YAML — SceneLoader

#### [MODIFY] [SceneLoader.cs](file:///c:/Fiorenzo/GitHub-Personale/3d-ray/src/RayTracer/Scene/SceneLoader.cs)

**1. Aggiungere case nel dispatch `CreateEntity`** (~riga 1526, dopo `"plane"`):

```csharp
"heightfield" or "height_field" or "terrain"
    => CreateHeightFieldEntity(e, mat),
```

**2. Nuovo metodo `CreateHeightFieldEntity`** (~30 righe):
- Parsing di `bounds` → `xmin, zmin, xmax, zmax`  
- Parsing di `height_texture` con `CreateTexture()` (riutilizza tutta l'infrastruttura NoiseTexture/Musgrave)
- Validazione: texture obbligatoria, bounds validi
- Costruzione `HeightField` e wrap in `Transform` se `center` è specificato

---

### Scena di test

#### [NEW] [heightfield-test.yaml](file:///c:/Fiorenzo/GitHub-Personale/3d-ray/scenes/heightfield-test.yaml)

Scena minimale per validare il primitivo:

```yaml
world:
  sky:
    type: "gradient"
    zenith_color:  [0.04, 0.06, 0.11]
    horizon_color: [0.28, 0.26, 0.32]
    ground_color:  [0.02, 0.02, 0.03]
  sun:
    direction: [-0.5, -1, -0.3]
    color: [1.0, 0.95, 0.85]
    intensity: 2.0

cameras:
  - name: "overview"
    type: "perspective"
    position: [0, 40, -60]
    look_at: [0, 5, 0]
    fov: 45
    up: [0, 1, 0]

materials:
  terrain_rock:
    type: "disney"
    base_color: [0.45, 0.38, 0.32]
    roughness: 0.9

objects:
  - name: "terrain"
    type: "heightfield"
    bounds: [-50, -50, 50, 50]
    max_height: 25
    height_scale: 25
    height_texture:
      type: "noise"
      noise_type: "hetero_terrain"
      scale: 0.03
      octaves: 8
      lacunarity: 2.3
      fractal_increment: 0.25
      fractal_offset: 0.7
      distortion: 0.3
    material: "terrain_rock"
```

---

## Riepilogo file

| Azione | File | Stima righe |
|--------|------|-------------|
| **NEW** | `src/RayTracer/Geometry/HeightField.cs` | ~280 |
| **MODIFY** | `src/RayTracer/Scene/SceneData.cs` | +15 |
| **MODIFY** | `src/RayTracer/Scene/SceneLoader.cs` | +40 |
| **NEW** | `scenes/heightfield-test.yaml` | ~45 |

Totale: **~380 righe nuove**, 2 file modificati, 2 file nuovi.

---

## Verification Plan

### Automated Tests

1. **Build**: `dotnet build src/RayTracer/RayTracer.csproj -c Release` — deve compilare senza errori
2. **Render test**: 
```bash
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/heightfield-test.yaml -o renders/heightfield-test.png \
  -w 1280 -H 720 -s 16 -d 4
```
3. **Verifica visiva**: il render deve mostrare un terreno montagnoso procedurale con:
   - Silhouette visibile (il ray marching trova intersezioni corrette)
   - Ombre proprie (le normali sono corrette)
   - Nessun artefatto (banding, buchi, Z-fighting)

### Manual Verification

- Confronto visivo con un terrain equivalente generato da TerrainGen
- Test con diversi `noise_type`: `hetero_terrain`, `hybrid_multifractal`, `ridged`, `fbm`
- Test zoom-in: verificare che il dettaglio sia effettivamente "infinito" (non pixelato come un mesh a risoluzione fissa)
