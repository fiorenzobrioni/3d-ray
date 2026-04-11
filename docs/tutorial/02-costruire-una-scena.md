# Tutorial 02: Costruire una Scena (Hands-On)

In questo tutorial passeremo dalla teoria alla pratica. Costruiremo insieme una scena classica: una **sfera metallica rossa** appoggiata su un **pavimento di marmo**, il tutto illuminato da luci da studio.

Al termine, avrai un file YAML completo che potrai renderizzare per vedere il risultato.

---

## 1. Lo Sceletro del File
Ogni scena inizia con la definizione del mondo e della camera. Crea un nuovo file chiamato `natura-morta.yaml`.

```yaml
world:
  ambient_light: [0.05, 0.05, 0.06]  # Luce globale soffusa
  background: [0.1, 0.1, 0.12]       # Colore dello sfondo (quasi nero)

cameras:
  - name: "main"
    position: [0, 2, -5]             # Posizionata in alto e indietro
    look_at: [0, 1, 0]                # Punta verso il centro della scena
    fov: 40                          # Campo visivo (zoom)
```

## 2. Definire i Materiali
Prima di creare gli oggetti, dobbiamo definire come appariranno. Useremo il materiale **Disney (PBR)** per il massimo realismo.

```yaml
materials:
  - id: "marmo_pavimento"
    type: "disney"
    color: [0.9, 0.9, 0.9]
    roughness: 0.1                   # Molto liscio/riflettente
    specular: 0.8
    texture:                         # Aggiungiamo venature procedurali
      type: "marble"
      scale: 15.0

  - id: "metallo_rosso"
    type: "disney"
    color: [0.8, 0.1, 0.1]
    metallic: 1.0                    # Comportamento metallico
    roughness: 0.15                  # Un po' di opacità nei riflessi
```

## 3. Aggiungere gli Oggetti (Entities)
Ora posizioniamo il pavimento e la nostra sfera "protagonista".

```yaml
entities:
  # Pavimento (un piano infinito)
  - name: "pavimento"
    type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "marmo_pavimento"

  # Sfera
  - name: "sfera_test"
    type: "sphere"
    center: [0, 1, 0]                # Appoggiata sul pavimento (Y=1 se raggio=1)
    radius: 1.0
    material: "metallo_rosso"
```

## 4. Illuminazione
Senza luci, la scena sarà nera. Aggiungiamo una luce principale (Key Light) e una di riempimento (Fill Light).

```yaml
lights:
  - type: "area"                     # Luce rettangolare per ombre morbide
    corner: [-2, 4, -2]
    u: [4, 0, 0]
    v: [0, 0, 4]
    color: [1, 1, 1]
    intensity: 15.0
    shadow_samples: 16

  - type: "point"                    # Una piccola luce per schiarire le ombre
    position: [3, 2, -2]
    color: [0.8, 0.8, 1.0]           # Leggermente azzurrina
    intensity: 3.0
```

---

## 5. Il Render Finale
Salva il file e lancia il comando dal terminale:

```powershell
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- -i natura-morta.yaml -o output/test.png -s 64
```

### Cosa abbiamo imparato?
- Gli oggetti sono definiti nella sezione `entities`.
- L'aspetto visivo è controllato dai `materials`.
- Il materiale **Disney** è il più versatile per ottenere risultati fotorealistici.
- Le **Area Light** producono ombre molto più belle rispetto alle Point Light, ma richiedono più campioni (`-s`).

---

> [!TIP]
> Per una lista completa di tutte le geometrie e i materiali disponibili, consulta la [Guida di Riferimento delle Scene](../../docs/reference/riferimento-scene.md).

[Vai al Tutorial 03: Tecniche Avanzate](./03-tecniche-avanzate.md) | [Indice Tutorial](../tutorial/)
