# Preset — catalogo copia-incolla per 3D-Ray

Questa cartella è un **catalogo di preset**: blocchi YAML pronti da **copiare e
incollare** dentro le tue scene. Apri il catalogo della famiglia che ti serve,
copia il blocco, incollalo nella tua scena e ritocca i parametri che vuoi. Ogni
preset è validato contro lo schema del motore e renderizza senza warning.

## Come si usa

1. Apri il catalogo della famiglia (tabella sotto).
2. Copia il blocco `materials:` / `lights:` / `mediums:` del preset scelto.
3. Incollalo nella tua scena (le voci con lo stesso `id` si fondono nel blocco
   locale della scena).
4. Referenzia l'`id` dalle entità (`material: "..."`) e ritocca colore/scala.

```yaml
# nella tua scena
materials:
  - id: "carrara_lucido"      # ← incollato da presets/materials-stone.md
    type: "disney"
    # ...
entities:
  - type: "sphere"
    center: [0, 1, 0]
    radius: 1
    material: "carrara_lucido"
```

## Cataloghi

| Catalogo | Contenuto |
|----------|-----------|
| [`world.md`](world.md) | Cielo + terreno abbinati (ambienti naturali e studi) + medium |
| [`sky.md`](sky.md) | Modelli di cielo in isolamento (flat, gradient, Preetham, Nishita, Hosek, HDRI) |
| [`materials-stone.md`](materials-stone.md) | Marmi, graniti, travertino, onice, alabastro, mattone, cemento |
| [`materials-metal.md`](materials-metal.md) | Metalli grezzi e lucidati, vernici industriali |
| [`materials-wood.md`](materials-wood.md) | Legni grezzi, laccati, verniciati |
| [`materials-glass.md`](materials-glass.md) | Vetri, gemme/minerali, liquidi (famiglia trasmissiva) |
| [`materials-organic.md`](materials-organic.md) | Tessuti, pelli, cibi, organici |
| [`materials-synthetic.md`](materials-synthetic.md) | Plastiche, sintetici, vernici, ceramiche |
| [`materials-ground.md`](materials-ground.md) | Materiali per il terreno (si abbinano a `world.md`) |
| [`materials-weathering.md`](materials-weathering.md) | Invecchiamento e ricette `mix` |
| [`lights.md`](lights.md) | Set di luci pronti (studio, esterni, notte/neon, emissive, caustiche) |
| [`mediums.md`](mediums.md) | Atmosfere, nebbie, ghiaccio/neve, SSS, liquidi volumetrici |
| [`terrains.md`](terrains.md) | Ricetta heightfield + strati altimetrici/pendenza |
| [`caustics.md`](caustics.md) | Caster di caustiche pronti (vetro/cristallo/frosted + metallo lucido/spazzolato) + receiver, luce e mini-scena |

## Convenzioni

- Gli `id` dei preset sono pensati per essere **rinominati** liberamente nella tua
  scena. Quelli proposti sono brevi e descrittivi.
- I materiali usano il **Disney BSDF** (`type: disney`) salvo dove un tipo
  classico è la scelta corretta.
- Le risorse binarie (texture immagine, font, heightmap) vivono in
  [`../assets/`](../assets/) e si referenziano per path relativo dalla scena.
- Schema completo dei materiali: [`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).
