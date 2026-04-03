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

---

[← Torna all'indice](../03-libreria-preset.md)
