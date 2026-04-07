# 5. Libreria Materiali PBR (Disney BSDF)

Preset copia-incolla per il materiale `type: "disney"`, organizzati per categoria.

> **Quando usare Disney:** Usa `disney` per gli oggetti protagonisti della scena dove servono effetti PBR avanzati (clearcoat, sheen, subsurface, vetro smerigliato). Per pavimenti, muri e superfici di sfondo, preferisci `lambertian` o `metal` — sono più veloci e producono meno rumore.

> **Eccezione: metalli Disney.** I materiali con `metallic: 1.0` (senza clearcoat né sheen) hanno un solo lobo attivo e producono **lo stesso rumore** di un `metal` classico. Usali liberamente su qualsiasi superficie, anche grandi.

> **Sample count:** Scene con molti materiali Disney richiedono circa 4× i campioni di scene con materiali classici per lo stesso livello di pulizia. Consiglio: 256 spp per draft Disney, 512+ per produzione. Vedi la [guida tecnica alla varianza Disney](../../technical/shading-model.md#2-varianza-e-convergenza-disney-bsdf-vs-materiali-classici) per i dettagli.

## Metalli

```yaml
# Oro
- id: "oro"
  type: "disney"
  color: [1.0, 0.71, 0.29]
  metallic: 1.0
  roughness: 0.15

# Rame
- id: "rame"
  type: "disney"
  color: [0.95, 0.64, 0.54]
  metallic: 1.0
  roughness: 0.25

# Cromo a specchio
- id: "cromo"
  type: "disney"
  color: [0.95, 0.93, 0.88]
  metallic: 1.0
  roughness: 0.02

# Acciaio satinato
- id: "acciaio_satinato"
  type: "disney"
  color: [0.58, 0.57, 0.55]
  metallic: 1.0
  roughness: 0.45

# Titanio anodizzato (blu)
- id: "titanio_blu"
  type: "disney"
  color: [0.25, 0.35, 0.65]
  metallic: 1.0
  roughness: 0.3

# Rame Ossidato (Patina verde)
- id: "rame_ossidato"
  type: "disney"
  color: [0.35, 0.55, 0.45]
  metallic: 0.6
  roughness: 0.7

# Ottone
- id: "ottone"
  type: "disney"
  color: [0.78, 0.57, 0.11]
  metallic: 1.0
  roughness: 0.25
```

## Plastiche e Dielettrici

```yaml
# Plastica opaca rossa
- id: "plastica_rossa"
  type: "disney"
  color: [0.8, 0.1, 0.1]
  roughness: 0.8
  metallic: 0.0

# Plastica lucida (tipo giocattolo)
- id: "plastica_lucida"
  type: "disney"
  color: [0.2, 0.6, 0.9]
  roughness: 0.15
  metallic: 0.0
  specular: 0.5

# Gomma nera
- id: "gomma"
  type: "disney"
  color: [0.05, 0.05, 0.05]
  roughness: 0.95
  specular: 0.1

# Ceramica (usata nelle scene showcase)
- id: "ceramica"
  type: "disney"
  color: [0.92, 0.88, 0.82]
  roughness: 0.3
  metallic: 0.0
  specular: 0.5
```

## Vernici e Finiture Speciali

```yaml
# Vernice auto rosso metallizzato
- id: "vernice_auto_rosso"
  type: "disney"
  color: [0.7, 0.05, 0.05]
  metallic: 0.0
  roughness: 0.3
  clearcoat: 1.0
  clearcoat_gloss: 0.95

# Lacca nera pianola
- id: "lacca_nera"
  type: "disney"
  color: [0.02, 0.02, 0.02]
  roughness: 0.05
  clearcoat: 1.0
  clearcoat_gloss: 1.0

# Vernice Auto Blu
- id: "vernice_auto_blu"
  type: "disney"
  color: [0.05, 0.15, 0.65]
  roughness: 0.3
  clearcoat: 1.0
  clearcoat_gloss: 0.95

# Legno Laccato (Metal + Wood texture)
- id: "legno_laccato"
  type: "metal"
  fuzz: 0.08
  texture:
    type: "wood"
    scale: 22.0
    colors: [[0.35, 0.05, 0.02], [0.15, 0.02, 0.01]]
```

## Tessuti e Materiali Organici

```yaml
# Velluto blu
- id: "velluto_blu"
  type: "disney"
  color: [0.05, 0.1, 0.5]
  roughness: 0.9
  sheen: 1.0
  sheen_tint: 0.8

# Pelle / cera (SSS)
- id: "pelle"
  type: "disney"
  color: [0.85, 0.6, 0.45]
  roughness: 0.6
  subsurface: 0.4
  specular: 0.2

# Cera / Candela (subsurface)
- id: "cera"
  type: "disney"
  color: [0.90, 0.82, 0.60]
  roughness: 0.7
  subsurface: 0.6
  specular: 0.15
```

## Vetri e Trasparenti

```yaml
# Vetro chiaro
- id: "vetro_chiaro"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5

# Vetro colorato (verde)
- id: "vetro_verde"
  type: "disney"
  color: [0.6, 1.0, 0.6]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5

# Vetro smerigliato
- id: "vetro_smerigliato"
  type: "disney"
  color: [0.95, 0.95, 1.0]
  roughness: 0.35
  spec_trans: 0.9
  ior: 1.5

# Diamante
- id: "diamante"
  type: "disney"
  color: [1.0, 1.0, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 2.42

# Vetro Ambra
- id: "vetro_ambra"
  type: "disney"
  color: [0.9, 0.6, 0.2]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.5

# Vetro Ottico
- id: "vetro_ottico"
  type: "disney"
  color: [0.97, 0.99, 1.0]
  roughness: 0.0
  spec_trans: 1.0
  ior: 1.62
```

---

---

[← Torna all'indice](../03-libreria-preset.md)
