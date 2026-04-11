# 📖 Documentazione e Guide — 3D-Ray

Benvenuto nella documentazione centrale di **3D-Ray**. Qui troverai tutto il materiale necessario per imparare a usare il motore, consultare le specifiche tecniche o approfondire il funzionamento interno del renderer.

La documentazione è organizzata in tre aree principali:

---

## 🎓 1. Tutorial (Learning Path)
Guide passo-passo progettate per portarti da zero alla creazione di scene fotorealistiche.

| # | Italiano 🇮🇹 | English 🇺🇸 |
|---|---|---|
| 01 | [Guida Rapida e Utilizzo](./tutorial/01-guida-rapida.md) | [Quick Start & Usage](./tutorial/01-quick-start.md) |
| 02 | [Costruire una Scena](./tutorial/02-costruire-una-scena.md) | [Building a Scene](./tutorial/02-building-a-scene.md) |
| 03 | [Tecniche Avanzate](./tutorial/03-tecniche-avanzate.md) | [Advanced Techniques](./tutorial/03-advanced-techniques.md) |
| 04 | [Catalogo dei Preset](./tutorial/04-catalogo-preset.md) | [Preset Catalog](./tutorial/04-preset-catalog.md) |

---

## 📜 2. Reference (Consultazione Rapida)
Riferimenti tecnici completi sulla sintassi YAML e i parametri del motore.

- **[Guida di Riferimento delle Scene (IT)](./reference/riferimento-scene.md)**: Sintassi completa, materiali, luci e geometrie in italiano.
- **[Scene Reference Guide (EN)](./reference/scene-reference.md)**: Full YAML syntax and engine parameters in English.

---

## 🧠 3. Technical Deep Dives (Architettura)
Documentazione approfondita sulla matematica e la logica dietro il motore. Dedicata a sviluppatori e curiosi di computer grafica.

- **[Pipeline di Rendering](./technical/rendering-pipeline.md)**: Il viaggio di un raggio, dall'YAML al pixel.
- **[Modello di Shading e Materiali](./technical/shading-model.md)**: PBR, Disney BSDF e Normal Mapping.
- **[Path Tracing e Illuminazione](./technical/path-tracing-and-lighting.md)**: NEE, Global Illumination e campionamento luci.
- **[Strutture di Accelerazione (BVH)](./technical/acceleration-structures.md)**: Come rendiamo le intersezioni veloci su milioni di poligoni.
- **[Geometria del Toro](./technical/quartic-solver-and-torus.md)**: Risoluzione analitica di equazioni quartiche.
- **[CSG — Boolean Operations](./technical/csg-boolean-operations.md)**: Logica booleana tra solidi 3D.

---

[🏠 Torna alla Root del Progetto](../README.md)
