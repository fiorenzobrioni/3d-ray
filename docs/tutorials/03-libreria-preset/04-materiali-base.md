# 4. Catalogo Materiali Professionale

Una collezione di materiali che sfrutta le texture procedurali e la randomizzazione.

## **Marmo: Carrara White**
Bianco con venature grigie sottili.
```yaml
  - id: "marmo_carrara"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 12.0
      colors: [[0.95, 0.95, 0.95], [0.5, 0.5, 0.5]]
      randomize_offset: true
```

## **Marmo: Nero Marquinia**
Sfondo nero intenso con venature bianche spettacolari.
```yaml
  - id: "marmo_nero"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 10.0
      colors: [[0.05, 0.05, 0.05], [0.7, 0.7, 0.7]]
      randomize_offset: true
```

## **Legno: Noce**
```yaml
  - id: "legno_noce"
    type: "lambertian"
    texture:
      type: "wood"
      scale: 3.0
      noise_strength: 2.0
      colors: [[0.45, 0.28, 0.15], [0.30, 0.18, 0.08]]
      randomize_rotation: true
```

## **Metallo: Oro Lucido**
```yaml
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.1]
    fuzz: 0.02
```

## **Metallo: Acciaio Satinato**
```yaml
  - id: "acciaio"
    type: "metal"
    color: [0.7, 0.7, 0.75]
    fuzz: 0.15
```

## **Vetro: Cristallo**
```yaml
  - id: "cristallo"
    type: "dielectric"
    refraction_index: 1.8
    color: [1.0, 1.0, 1.0]
```

## **Vetro: Fumé**
```yaml
  - id: "vetro_fume"
    type: "dielectric"
    refraction_index: 1.5
    color: [0.7, 0.7, 0.7]
```

## **Pavimento: Scacchiera Classica**
```yaml
  - id: "scacchiera"
    type: "lambertian"
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.05, 0.05, 0.05], [0.95, 0.95, 0.95]]
```

## **Emissivo: Neon Magenta**
Glow vivace rosa-magenta, ideale per ambientazioni cyberpunk e sci-fi.
```yaml
  - id: "neon_magenta"
    type: "emissive"
    color: [1.0, 0.05, 0.6]
    intensity: 8.0
```

## **Emissivo: Neon Ciano**
Complemento freddo al magenta per effetti bicolore.
```yaml
  - id: "neon_ciano"
    type: "emissive"
    color: [0.05, 0.85, 1.0]
    intensity: 8.0
```

## **Emissivo: LED Bianco Caldo**
Pannello luminoso con temperatura colore simile a una lampadina tungsteno.
```yaml
  - id: "led_caldo"
    type: "emissive"
    color: [1.0, 0.85, 0.6]
    intensity: 12.0
```

## **Emissivo: LED Bianco Freddo**
Pannello luminoso con temperatura colore daylight.
```yaml
  - id: "led_freddo"
    type: "emissive"
    color: [0.9, 0.95, 1.0]
    intensity: 12.0
```

## **Emissivo: Lava (con Texture)**
Superficie incandescente con pattern non uniforme via texture marble.
```yaml
  - id: "lava"
    type: "emissive"
    intensity: 20.0
    texture:
      type: "marble"
      scale: 3.0
      noise_strength: 6.0
      colors: [[1.0, 0.3, 0.0], [1.0, 0.8, 0.0]]
```

## **Emissivo: Verde Acido**
LED indicatore o effetto matrice.
```yaml
  - id: "led_verde"
    type: "emissive"
    color: [0.1, 1.0, 0.3]
    intensity: 4.0
```

## **Emissivo: Ambra / Fiamma**
Glow caldo per candele, torce o lampade.
```yaml
  - id: "glow_ambra"
    type: "emissive"
    color: [1.0, 0.65, 0.1]
    intensity: 5.0
```

> **💡 Note sull'uso dei materiali emissivi:**
> - I materiali emissivi **non necessitano** di luci esplicite nella sezione `lights:` — sono essi stessi sorgenti di luce.
> - L'illuminazione indiretta (color bleeding) funziona tramite i rimbalzi del path tracer: un neon magenta vicino a una parete bianca la colorerà di rosa.
> - Per scene illuminate **solo** da emissivi, usa campioni alti (`-s 128+`) e profondità adeguata (`-d 10+`).
> - Un emissivo su un `quad` piatto è un'alternativa visibile all'`area` light: puoi vederlo nella scena e nei riflessi.

---

## **Materiali Base Solidi: Bianco Puro**
```yaml
  - id: "bianco"
    type: "lambertian"
    color: [0.92, 0.92, 0.92]
```

## **Materiali Base Solidi: Rosso Base**
```yaml
  - id: "rosso"
    type: "lambertian"
    color: [0.75, 0.15, 0.10]
```

## **Materiali Base Solidi: Grigio Medio**
```yaml
  - id: "grigio"
    type: "lambertian"
    color: [0.5, 0.5, 0.5]
```

## **Materiali Base Solidi: Nero Opaco**
```yaml
  - id: "nero"
    type: "lambertian"
    color: [0.03, 0.03, 0.03]
```

## **Superfici Architettoniche: Muro Bianco**
```yaml
  - id: "muro_bianco"
    type: "lambertian"
    color: [0.88, 0.86, 0.82]
```

## **Superfici Architettoniche: Pavimento Generico (Scacchiera Discreta)**
```yaml
  - id: "pavimento"
    type: "lambertian"
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.15, 0.15, 0.15], [0.25, 0.25, 0.25]]
```

## **Superfici Architettoniche: Cemento**
```yaml
  - id: "cemento"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 8.0
      colors: [[0.60, 0.60, 0.60], [0.50, 0.50, 0.50]]
```

## **Superfici Architettoniche: Mattoni**
```yaml
  - id: "mattoni"
    type: "lambertian"
    color: [0.55, 0.25, 0.18]
```

## **Superfici Architettoniche: Pietra**
```yaml
  - id: "pietra"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 6.0
      colors: [[0.55, 0.52, 0.48], [0.42, 0.40, 0.36]]
```

## **Metalli Aggiuntivi: Metallo Scuro**
```yaml
  - id: "metallo_scuro"
    type: "metal"
    color: [0.12, 0.12, 0.15]
    fuzz: 0.08
```

## **Metalli Aggiuntivi: Metallo Ruggine (Disney)**
```yaml
  - id: "metallo_ruggine"
    type: "disney"
    color: [0.55, 0.28, 0.15]
    metallic: 0.7
    roughness: 0.8
```

## **Pietre e Ceramiche: Marmo Base (Generico)**
```yaml
  - id: "marmo_base"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 8.0
      colors: [[0.90, 0.88, 0.85], [0.50, 0.48, 0.45]]
      randomize_offset: true
```

## **Pietre e Ceramiche: Marmo Bianco (Calacatta Style)**
```yaml
  - id: "marmo_bianco"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 10.0
      colors: [[0.96, 0.96, 0.96], [0.55, 0.55, 0.55]]
      randomize_offset: true
```

## **Pietre e Ceramiche: Ceramica Bianca**
```yaml
  - id: "ceramica_bianca"
    type: "disney"
    color: [0.95, 0.93, 0.90]
    roughness: 0.2
    specular: 0.5
```

## **Pietre e Ceramiche: Avorio**
```yaml
  - id: "avorio"
    type: "lambertian"
    color: [0.96, 0.93, 0.82]
```

## **Gomme: Gomma Nera**
```yaml
  - id: "gomma_nera"
    type: "disney"
    color: [0.05, 0.05, 0.05]
    roughness: 0.95
    specular: 0.1
```

## **Gomme: Gomma Rossa**
```yaml
  - id: "gomma_rossa"
    type: "disney"
    color: [0.7, 0.08, 0.05]
    roughness: 0.9
    specular: 0.08
```

## **Plastiche Aggiuntive: Plastica Bianca**
```yaml
  - id: "plastica_bianca"
    type: "disney"
    color: [0.92, 0.92, 0.92]
    roughness: 0.6
    metallic: 0.0
```

## **Plastiche Aggiuntive: Plastica Arancione**
```yaml
  - id: "plastica_arancio"
    type: "disney"
    color: [0.9, 0.45, 0.05]
    roughness: 0.5
    metallic: 0.0
```

## **Emissivi Aggiuntivi: Neon Rosa**
```yaml
  - id: "neon_rosa"
    type: "emissive"
    color: [1.0, 0.2, 0.6]
    intensity: 12.0
```

## **Emissivi Aggiuntivi: Neon Bianco (Daylight)**
```yaml
  - id: "neon_bianco"
    type: "emissive"
    color: [1.0, 0.98, 0.95]
    intensity: 15.0
```

## **Emissivi Aggiuntivi: Neon Blu**
```yaml
  - id: "neon_blu"
    type: "emissive"
    color: [0.2, 0.4, 1.0]
    intensity: 8.0
```

## **Emissivi Aggiuntivi: Luce Calda (Emissive per scene CSG)**
```yaml
  - id: "luce_calda"
    type: "emissive"
    color: [1.0, 0.85, 0.6]
    intensity: 15.0
```

---

[← Torna all'indice](../03-libreria-preset.md)
