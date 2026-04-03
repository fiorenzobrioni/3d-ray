# 7. Sezione `lights`

Le luci esplicite della scena. Puoi combinare più tipi di luce per ottenere l'effetto desiderato.

## 7.1 Point Light (Puntiforme)
Luce omnidirezionale da un singolo punto.
```yaml
  - type: "point"
    position: [2, 5, -3]
    color: [1.0, 0.95, 0.85]
    intensity: 20.0
```

| Campo | Default | Descrizione |
|-------|---------|-------------|
| `position` | `[0, 10, 0]` | Posizione della sorgente nello spazio |
| `color` | `[1, 1, 1]` | Colore della luce |
| `intensity` | `1.0` | Intensità (attenuazione quadratica con la distanza). Valori tipici: 4–30. |

## 7.2 Directional Light (Sole)

```yaml
  - type: "directional"
    direction: [-0.5, -1.0, -0.3]
    color: [1.0, 0.98, 0.92]
    intensity: 0.8
```

| Campo | Default | Descrizione |
|-------|---------|-------------|
| `direction` | `[-1, -1, -1]` | Direzione **verso cui punta** la luce (non la sorgente). Viene normalizzata internamente. |
| `color` | `[1, 1, 1]` | Colore della luce |
| `intensity` | `1.0` | Intensità. Senza attenuazione con la distanza — valori tipici: 0.05–0.15. |

> **Alias:** Puoi usare anche `type: "sun"` come alias per `"directional"`.

> **💡 Tip: Allinea Directional Light e Sun Disk.** Se usi un gradient sky con sun disk, imposta la stessa `direction` sulla directional light per coerenza visiva: il sole visibile nel cielo e l'illuminazione diretta arrivano dalla stessa parte.

## 7.3 Spot Light (Faretto)
Luce conica con posizione e direzione. Ha un cono interno (piena intensità) e un cono esterno (sfumatura smooth). L'attenuazione angolare usa un'interpolazione quadratica tra i due coni.
```yaml
  - type: "spot"
    position: [0, 5, 0]
    direction: [0, -1, 0]    # Punta verso il basso
    color: [1.0, 0.9, 0.7]
    intensity: 40.0
    inner_angle: 15
    outer_angle: 30
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `position` | `[X, Y, Z]` | `[0, 10, 0]` | Posizione del faretto |
| `direction` | `[X, Y, Z]` | `[0, -1, 0]` | Direzione verso cui punta il cono |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore della luce |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 6–30. |
| `inner_angle` | float | `15` | Angolo del cono interno (luce piena), in gradi |
| `outer_angle` | float | `30` | Angolo del cono esterno (fade out), in gradi |

> **Alias:** Puoi usare anche `type: "spotlight"`.

## 7.4 Area Light (Emettitore Rettangolare)

Sorgente luminosa rettangolare che produce **ombre morbide** fisicamente corrette con gradiente di penombra. Definita da un angolo (`corner`) e due vettori che formano il rettangolo.

Il motore usa campionamento Monte Carlo: per ogni punto della scena vengono sparati `shadow_samples` raggi verso punti casuali sulla superficie della luce, e il risultato è la media. Più shadow samples = penombra più morbida e meno rumorosa.

```yaml
  - type: "area"
    corner: [-1.5, 4.99, -1.5]    # Un angolo del rettangolo
    u: [3.0, 0.0, 0.0]            # Primo lato (larghezza: 3 unità in X)
    v: [0.0, 0.0, 3.0]            # Secondo lato (profondità: 3 unità in Z)
    color: [1.0, 0.97, 0.9]
    intensity: 35.0
    shadow_samples: 16
```

| Campo | Tipo | Default | Descrizione |
|-------|------|---------|-------------|
| `corner` | `[X, Y, Z]` | — (**obbligatorio**) | Un angolo del rettangolo luminoso |
| `u` | `[X, Y, Z]` | — (**obbligatorio**) | Primo vettore lato del rettangolo |
| `v` | `[X, Y, Z]` | — (**obbligatorio**) | Secondo vettore lato del rettangolo |
| `color` | `[R, G, B]` | `[1, 1, 1]` | Colore emesso |
| `intensity` | float | `1.0` | Intensità. Valori tipici: 15–60. |
| `shadow_samples` | int | `16` | Raggi ombra per punto (default per-luce). Sovrascrivibile da CLI con `-S`. |

> **Alias:** Puoi usare anche `type: "area_light"`, `type: "rect"` o `type: "rect_light"`.

> **💡 Override da CLI:** Il parametro `--shadow-samples` (`-S`) da riga di comando sovrascrive il valore `shadow_samples` di **tutte** le area light nella scena. Questo permette di iterare sulla qualità senza modificare il file YAML.

> **⚠️ Costo computazionale:** Il `shadow_samples` ha un impatto diretto sul tempo di render. Con `-s 128` campioni pixel e `-S 16`, ogni pixel lancia `128 × 16 = 2048` raggi ombra per questa sola luce.

**Esempio: Pannello luminoso da soffitto**
```yaml
  - type: "area"
    corner: [-1.5, 4.99, -1.5]
    u: [3.0, 0.0, 0.0]
    v: [0.0, 0.0, 3.0]
    color: [1.0, 0.97, 0.9]
    intensity: 35.0
    shadow_samples: 16
```

---

## 7.5 — Calibrazione dell'Intensità

> 💡 **Nota sui valori tipici:** I range indicati nelle tabelle dei paragrafi 7.1–7.4 sono stati calibrati empiricamente su scene reali. Se l'immagine risulta sovraesposta o sottoesposta, scala **tutte** le intensità in modo uniforme mantenendo i rapporti tra le sorgenti.

### Valori di riferimento per tipo di luce

| Tipo luce | Range consigliato | Note |
|-----------|-------------------|------|
| `point` generica | 4 – 30 | Scala con il quadrato della distanza: raddoppiare la distanza richiede ×4 l'intensità |
| `spot` key light | 15 – 30 | Valori più alti per coni stretti (`inner_angle` < 15°) |
| `spot` fill / rim | 5 – 15 | Tipicamente 1/3 – 1/2 della key |
| `point` accent / bounce | 0.5 – 2 | Luci di dettaglio, quasi invisibili da sole |
| `directional` fill / multi-luce | 0.05 – 0.15 | Sorgente secondaria in scene con più luci |
| `directional` luce principale | 0.3 – 2.0 | Come unica luce outdoor (tramonto, luna): valori più alti compensano l'assenza di altre sorgenti |
| `area` pannello | 20 – 60 | Dipende dall'area del rettangolo e dalla distanza dalla scena |

### Workflow di calibrazione

1. Aggiungi le luci con i valori centrali del range.
2. Esegui un preview rapido (`-s 1 -w 400 -S 4`).
3. Se l'immagine è sovraesposta, **dimezza tutte le intensità** e ripeti.
4. Se è sottoesposta, **raddoppiale** e ripeti.
5. Quando l'esposizione globale è corretta, bilancia le singole sorgenti tra loro.
6. Tieni nota dei valori finali: potrai riusarli come punto di partenza per scene simili.

---

---

[← Torna all'indice](../02-tutorial-scene.md)
