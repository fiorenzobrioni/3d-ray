# Tutorial 04: Catalogo dei Preset e Librerie

Il motore 3D-Ray include una vasta collezione di asset già pronti all'uso, situati nella cartella `scenes/libraries/`. Questo catalogo ti permetterà di creare scene professionali senza dover definire ogni singolo materiale o oggetto da zero.

---

## 1. Libreria Materiali
Troverai centinaia di materiali PBR basati sul modello **Disney BSDF**, suddivisi per categoria:

- **Metalli (`materials/metals.yaml`)**: Oro, Argento, Rame, Acciaio (in varianti lucide, satinate e spazzolate).
- **Vetri (`materials/glass.yaml`)**: Cristallo, Vetro colorato, Vetro smerigliato, Liquidi.
- **Naturali (`materials/organics.yaml`)**: Legni (Noce, Rovere, Mogano), Marmi venati, Pietre, Terreno.
- **Plastica e Vernici (`materials/plastics.yaml`)**: ABS nero, Vernice auto metallizzata, Gomma.

### Esempio d'uso:
```yaml
imports:
  - path: "libraries/materials/metals.yaml"
entities:
  - type: "sphere"
    material: "dis_oro_lucido"  # Prefisso 'dis_' per i materiali Disney
```

---

## 2. Libreria Oggetti (Templates)
Oltre 150 oggetti complessi costruiti con primitive e CSG. Sfoglia la cartella `libraries/objects/` per scoprire:

- **Arredamento (`furniture.yaml`)**: Tavoli, sedie, scaffali, lampade.
- **Gioielleria (`jewelry.yaml`)**: Anelli, gemme con tagli brillanti, diamanti.
- **Scienza (`laboratory.yaml`)**: Provette, beute, microscopi.
- **Scacchi (`chess.yaml`)**: Set completo Staunton con scacchiere.
- **Industria (`mechanical.yaml`)**: Ingranaggi, bulloni, pistoni.

### Esempio d'uso:
```yaml
imports:
  - path: "libraries/objects/chess.yaml"
entities:
  - type: "instance"
    template: "re_staunton"
    material: "dis_oro_lucido"  # Posso bagnare il re nell'oro!
```

---

## 3. Setup di Illuminazione
Non perdere tempo a posizionare ogni singola luce. Usa i set predefiniti in `libraries/lights/`:

- **Studio 3-Point**: Il setup classico per ritratti e prodotti (Key, Fill, Rim).
- **Global Illumination**: Set per esterni ed interni luminosi.
- **Starter Kits**: Scene complete (es. `starter-material-showroom.yaml`) che puoi usare come base per i tuoi test.

---

## 4. Come esplorare le librerie
Ogni cartella nella libreria contiene un file `README.md` dettagliato con l'elenco completo di tutti gli ID dei materiali e i nomi dei template disponibili.

> [!IMPORTANT]
> Ricorda che puoi sempre **sovrascrivere** un materiale della libreria definendolo con lo stesso `id` nel tuo file di scena locale.

---

[Torna all'Indice Tutorial](../tutorial/) | [Vai alla Guida di Riferimento](../../docs/reference/riferimento-scene.md)
