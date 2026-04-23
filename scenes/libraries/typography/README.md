# Libreria Tipografia — Alfabeto A–Z + Cifre 0–9

Raccolta di **36 glifi tipografici** (26 lettere maiuscole e 10 cifre) modellati
in 3D come mesh estruse con curve smooth, esportati in formato **OBJ** e
avvolti in **template YAML** istanziabili nelle scene 3D-Ray per comporre
parole e numeri.

---

## Contenuto

```
libraries/typography/
├── README.md                 # questo file
├── alphabet.yaml             # 36 template (glyph_A … glyph_Z, glyph_0 … glyph_9)
├── showcase.yaml             # scena di prova renderizzabile (griglia 6×6)
└── glyphs/                   # 36 file .obj
    ├── A.obj … Z.obj         # lettere maiuscole
    └── 0.obj … 9.obj         # cifre
```

---

## Perché OBJ + YAML e non solo YAML?

I glifi con curve smooth (O, C, G, S, 0, 8, 9, …) richiedono **100–500+
triangoli** per approssimare correttamente le curve. Esprimerli come entità
YAML sarebbe stato illeggibile (migliaia di righe per una sola lettera) e
lento da parsare.

La soluzione scelta usa il motore al meglio:

| Aspetto              | OBJ                                      | YAML inline          |
|----------------------|------------------------------------------|----------------------|
| Dimensione file      | Compatto (KB per glifo)                  | Molto verboso        |
| Smooth normals       | Sì, via direttive `vn`                   | Sì (smooth_triangle) |
| BVH interna          | **Sì** — via `Mesh.cs`                   | Solo via Group >4    |
| Istanziamento        | Stessa OBJ riusata per N istanze         | Ogni istanza ricrea  |
| Rigenerabilità       | Tool procedurale C#                      | Fatto a mano         |

L'OBJ loader del motore ([`src/RayTracer/Scene/ObjLoader.cs`](../../../src/RayTracer/Scene/ObjLoader.cs))
crea automaticamente `SmoothTriangle` quando trova le direttive `vn`
nel file (righe 213-236), costruisce una BVH interna per ogni mesh
([`src/RayTracer/Geometry/Mesh.cs`](../../../src/RayTracer/Geometry/Mesh.cs))
e accetta l'override del materiale da YAML.

Il wrapper `alphabet.yaml` aggiunge la sintassi ergonomica `type: instance,
template: glyph_X` in modo che la libreria si usi come le altre
(`libraries/objects/chess.yaml` e simili).

---

## Metriche uniformi

Tutti i glifi condividono lo stesso sistema di coordinate:

| Grandezza         | Valore | Significato                                      |
|-------------------|--------|--------------------------------------------------|
| Cap-height        | 1.0    | altezza delle maiuscole e delle cifre            |
| Baseline          | y = 0  | parte inferiore dei glifi                        |
| Bordo sinistro    | x = 0  | il glifo inizia a x=0                            |
| Faccia frontale   | z = 0  | (l'estrusione va verso −Z)                       |
| Profondità estr.  | 0.15   | spessore 3D uniforme                             |
| Larghezza tratto  | 0.15   | stem width (spessore delle aste)                 |
| Advance width     | varia  | larghezza proporzionale (0.25 "I" → 1.15 "W")    |
| Segmenti per arco | 32     | garantisce curve smooth anche in close-up        |

Stile: **geometric sans-serif** (famiglia Futura / Avenir) — tratti a
spessore costante, curve circolari pure, senza grazie.

---

## Uso

### Import minimo

```yaml
imports:
  - path: "libraries/typography/alphabet.yaml"

entities:
  - { type: "instance", template: "glyph_C", translate: [0.00, 0, 0] }
  - { type: "instance", template: "glyph_I", translate: [0.90, 0, 0] }
  - { type: "instance", template: "glyph_A", translate: [1.20, 0, 0] }
  - { type: "instance", template: "glyph_O", translate: [2.10, 0, 0] }
```

### Override materiale per istanza

Il template di default usa `font_primary` (bianco neutro). Ogni istanza può
sovrascrivere il materiale:

```yaml
entities:
  - type: "instance"
    template: "glyph_A"
    material: "dis_oro_lucido"     # dal libraries/materials/metals.yaml
    translate: [0, 0, 0]
```

Materiali "font" predefiniti in `alphabet.yaml`:

- `font_primary` — bianco neutro (default)
- `font_ink` — nero tipografico
- `font_gold` — oro lucido
- `font_copper` — rame satinato
- `font_chrome` — cromo specchio
- `font_neon` — rosa-magenta emissivo

### Trasformazioni

Scala, rotazione, traslazione funzionano come sugli altri template:

```yaml
  - type: "instance"
    template: "glyph_A"
    scale: 0.5                      # metà altezza
    rotate: [0, 0, 15]              # rotazione di 15° attorno a Z
    translate: [2, 1, -0.5]
```

### Composizione di parole

Ogni template riporta la propria `advance` (larghezza effettiva) come
commento. Per comporre una parola correttamente spaziata bisogna sommare
manualmente le advance degli `N−1` glifi precedenti più uno spacing di
kerning (0.02–0.05 tipicamente).

| Glifo | Advance | Glifo | Advance | Glifo | Advance | Glifo | Advance |
|:-----:|:-------:|:-----:|:-------:|:-----:|:-------:|:-----:|:-------:|
| A     | 0.85    | N     | 0.85    | 0     | 0.80    | 5     | 0.70    |
| B     | 0.65    | O     | 0.90    | 1     | 0.40    | 6     | 0.75    |
| C     | 0.80    | P     | 0.70    | 2     | 0.70    | 7     | 0.70    |
| D     | 0.75    | Q     | 1.00    | 3     | 0.70    | 8     | 0.70    |
| E     | 0.65    | R     | 0.75    | 4     | 0.80    | 9     | 0.75    |
| F     | 0.65    | S     | 0.70    |       |         |       |         |
| G     | 0.85    | T     | 0.75    |       |         |       |         |
| H     | 0.80    | U     | 0.80    |       |         |       |         |
| I     | 0.25    | V     | 0.85    |       |         |       |         |
| J     | 0.55    | W     | 1.15    |       |         |       |         |
| K     | 0.75    | X     | 0.80    |       |         |       |         |
| L     | 0.60    | Y     | 0.80    |       |         |       |         |
| M     | 1.00    | Z     | 0.75    |       |         |       |         |

---

## Orientamento e camera

L'estrusione parte da z=0 (faccia frontale) verso z=−0.15 (faccia posteriore).

Per via della convenzione della camera nel motore (`u = w × up` con
`w = lookFrom − lookAt`), **la posizione naturale della camera è sul lato
−Z dei glifi**, guardando verso +Z. In questo modo la X-world si mappa
correttamente su X-immagine e i glifi si leggono nel verso giusto.

Esempio:

```yaml
cameras:
  - name: "fronte"
    position: [0, 0.5, -5]
    look_at:  [0, 0.5, 0]
    fov: 45
```

La scena `showcase.yaml` mostra 4 camere di riferimento (griglia frontale,
close-up curve, 3/4 prospettica, riga delle cifre colorate).

---

## Rigenerazione dei glifi

I 36 file OBJ sono generati da un tool C# procedurale:

```
dotnet run --project src/Tools/TypographyGen
```

Il tool cammina dalla directory del binario fino alla repo root (cercando
`scenes/`) e scrive tutti i glifi in `scenes/libraries/typography/glyphs/`.
Il codice sorgente del tool è in
[`src/Tools/TypographyGen/Program.cs`](../../../src/Tools/TypographyGen/Program.cs).

Ogni glifo è costruito da primitive 3D estruse:

- `Rect` — rettangolo XY estruso (aste dritte, barre orizzontali)
- `Arc`  — settore anulare ellittico estruso con normali radiali smooth
  (curve: bowl di B/D/P, ring di O/C/G, hook di J, …)
- `Poly` — poligono convesso estruso con auto-orientazione CCW
  (parallelogrammi diagonali di A, K, M, N, V, W, X, Y, Z, 4, 7)

Le lettere con buchi (A, B, D, O, P, Q, R, 0, 4, 6, 8, 9) non richiedono
triangolazione con buchi: i tratti curvi sono sempre **anelli** (differenza
tra raggio esterno e interno), quindi il buco è implicito nella geometria.

---

## Render di prova

Dopo aver compilato il motore (`dotnet build src/RayTracer/RayTracer.csproj
-c Release`), il showcase si renderizza con:

```bash
# Smoke test (1-2 secondi)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/libraries/typography/showcase.yaml \
  -w 400 -H 300 -s 16 -d 4 -S 1 \
  -o renders/00-test-typography-showcase.png

# Preview (~10 secondi)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/libraries/typography/showcase.yaml \
  -w 800 -H 600 -s 128 -d 5 -S 2 \
  -c griglia \
  -o renders/typography-showcase-griglia.png

# Finale (alcuni minuti)
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/libraries/typography/showcase.yaml \
  -w 1920 -H 1440 -s 512 -d 6 -S 4 \
  -c griglia \
  -o renders/typography-showcase-final.png
```

---

## Compatibilità path

La libreria usa path mesh **relativi alla propria directory**
(`glyphs/A.obj` invece di `libraries/typography/glyphs/A.obj`). Questo
funziona grazie al meccanismo di fallback aggiunto in
[`SceneLoader.CreateMeshEntity`](../../../src/RayTracer/Scene/SceneLoader.cs):
se il path non si risolve rispetto alla directory della scena root, il
motore fa fallback alla directory del file YAML che ha dichiarato
l'entità (`EntityData.SourceDir`, popolato da `ProcessImports`).

Il risultato: `alphabet.yaml` può essere importato da qualsiasi scena,
ovunque si trovi nel filesystem, e i glifi OBJ si caricano correttamente.
