# 1. Struttura del File

Ogni file di scena è un documento YAML con 5 sezioni principali:

```yaml
imports:    # (opzionale) File YAML esterni da importare
templates:  # (opzionale) Oggetti composti riutilizzabili (blueprint)
world:      # Ambiente globale (cielo, terreno, luce ambiente)
camera:     # Punto di vista e ottica
materials:  # Definizione dei materiali (colori, texture, proprietà fisiche)
entities:   # Oggetti 3D nella scena (primitive, gruppi, istanze, CSG, mesh)
lights:     # Sorgenti di luce
```

> **Nota:** I colori sono sempre espressi come `[R, G, B]` con valori da `0.0` a `1.0`. Le coordinate usano il sistema: **X** = destra, **Y** = alto, **Z** = verso la camera (negativo = lontano dalla camera).

> **Nota:** Le sezioni `imports:` e `templates:` sono opzionali. Se presenti, `imports` deve essere la prima sezione del file. Vedi la [sezione 11](11-groups-and-imports.md) per i dettagli.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
