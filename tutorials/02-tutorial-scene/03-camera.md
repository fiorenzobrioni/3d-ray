# 3. Sezione `camera`

Controlla il punto di vista, l'inquadratura e l'effetto profondità di campo.

```yaml
camera:
  position: [0, 2, -8]       # Posizione della fotocamera
  look_at: [0, 0, 0]         # Punto verso cui guarda
  vup: [0, 1, 0]             # Vettore "alto" (roll della camera)
  fov: 60                     # Campo visivo verticale (gradi)
  aperture: 0.1               # Apertura lente (0 = tutto a fuoco)
  focal_dist: 8.0             # Distanza di messa a fuoco
```

## Parametri

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 1, -5]` | Dove si trova la camera |
| `look_at` | `[X, Y, Z]` | `[0, 0, 0]` | Punto di mira |
| `vup` | `[X, Y, Z]` | `[0, 1, 0]` | Vettore verso l'alto. Cambialo per inclinare la camera (Dutch angle). |
| `fov` | float | `60` | Campo visivo verticale in gradi. 30°=teleobiettivo, 60°=standard, 90°=grandangolo. |
| `aperture` | float | `0.0` | Diametro dell'apertura della lente. 0.0 = tutto a fuoco (pinhole). Valori > 0 producono depth of field. |
| `focal_dist` | float | `1.0` | Distanza dal piano di fuoco (in unità di scena). |

> **⚠️ Importante — Depth of Field:** Il valore di default `focal_dist: 1.0` è valido solo se `aperture: 0` (tutto a fuoco). Appena `aperture > 0`, il piano di fuoco si trova a 1 unità dalla camera — tipicamente dentro o vicinissimo agli oggetti, producendo bokeh estremo non intenzionale. **Misura la distanza camera→soggetto** e usala come `focal_dist`. Esempio: camera in `[0, 2, -8]`, soggetto in `[0, 1, 0]` → distanza ≈ `8.1` → `focal_dist: 8.1`.

---

## 3.1 Multi-Camera (`cameras:` list)

Oltre alla sintassi singola `camera:`, il motore supporta una lista di camere nominate con `cameras:`. Questo permette di definire più punti di vista nella stessa scena e selezionarne uno da CLI con `--camera <nome|indice>`.

**Sintassi:**
```yaml
cameras:
  - name: "main"
    position: [0, 5, -8]
    look_at: [0, 0, 0]
    fov: 45
    aperture: 0.1
    focal_dist: 12

  - name: "top"
    position: [0, 12, 0.01]
    look_at: [0, 0, 0]
    fov: 35
    aperture: 0.0
    focal_dist: 12

  - name: "closeup"
    position: [1.5, 1.2, -4]
    look_at: [0, 0.8, 0]
    fov: 25
    aperture: 0.2
    focal_dist: 4.25
```

Ogni camera nella lista accetta gli stessi parametri della sintassi singola (`position`, `look_at`, `vup`, `fov`, `aperture`, `focal_dist`), più un campo opzionale `name` per identificarla.

**Selezione da CLI:**
```powershell
# Elenca le camere disponibili
dotnet run ... -- -i scenes/chess.yaml --list-cameras

# Seleziona per nome (case-insensitive)
dotnet run ... -- -i scenes/chess.yaml -c top -o top.png

# Seleziona per indice (0-based)
dotnet run ... -- -i scenes/chess.yaml -c 2 -o closeup.png
```

**Regole di precedenza:**
- Se il YAML contiene sia `camera:` che `cameras:`, la lista `cameras:` ha la precedenza.
- Se non si specifica `-c` e la lista ha più di una camera, viene usata la prima con un warning.
- Se il nome o indice specificato non corrisponde a nessuna camera, viene usata la prima con un warning.

> **💡 Tip:** Per scene complesse con molte angolazioni (come la scacchiera), definisci tutte le camere nella lista e usa `--list-cameras` per ricordare quali sono disponibili. Puoi poi fare batch render da script iterando su ciascuna camera.

> **Retrocompatibilità:** La sintassi legacy `camera:` (singola) continua a funzionare normalmente. La migrazione a `cameras:` è opzionale.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
