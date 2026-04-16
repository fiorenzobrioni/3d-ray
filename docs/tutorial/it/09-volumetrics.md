# Capitolo 9: Mezzi partecipanti (Volumetrics)

L'aria reale non è perfettamente trasparente. La nebbia diffonde la luce, l'acqua assorbe le lunghezze d'onda del rosso, il fumo brilla quando viene attraversato da un raggio. 3D-Ray supporta un **mezzo partecipante globale omogeneo** che simula questi effetti.

---

## 9.1 Cosa sono i mezzi partecipanti?

Nel vuoto, la luce viaggia in linea retta all'infinito. In un mezzo partecipante (aria, acqua, fumo), accadono due cose:

- **Assorbimento (Absorption)** -- il mezzo "ingoia" i fotoni. La luce si affievolisce man mano che viaggia più lontano. L'assorbimento colorato crea atmosfere tinte (blu sott'acqua, foschia arancione al tramonto).

- **Scattering (Diffusione)** -- i fotoni cambiano direzione quando colpiscono le particelle nel mezzo. Questo è il motivo per cui la nebbia brilla quando i fari la attraversano e perché il cielo è blu.

La combinazione di assorbimento e scattering determina il comportamento della luce mentre attraversa il volume.

---

## 9.2 Configurazione del mezzo globale

Il mezzo è definito sotto `world: > medium:`:

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.06, 0.06, 0.06]
    phase: "hg"
    g: 0.85
```

| Parametro | Tipo      | Predefinito   | Descrizione                                     |
|-----------|-----------|---------------|-------------------------------------------------|
| `type`    | `string`  | --            | Al momento solo `"homogeneous"`                 |
| `sigma_a` | `[R,G,B]` | --            | Coefficiente di assorbimento per canale         |
| `sigma_s` | `[R,G,B]` | --            | Coefficiente di scattering per canale           |
| `phase`   | `string`  | `"isotropic"` | Tipo di funzione di fase (phase function)       |
| `g`       | `float`   | `0.0`         | Parametro di asimmetria (per la funzione `"hg"`)|

### sigma_a (Assorbimento)

Controlla quanto velocemente la luce viene assorbita. Le unità sono l'inverso delle unità world (1/unità). Valori più alti indicano un mezzo più denso e opaco.

- `[0.01, 0.01, 0.01]` -- assorbimento molto lieve (leggera foschia).
- `[0.1, 0.05, 0.01]` -- assorbimento colorato: il rosso viene assorbito più velocemente, il blu meno. Questo crea una tinta bluastra (come sott'acqua).

### sigma_s (Scattering)

Controlla quanta luce viene deviata dalle particelle. Valori più alti indicano una nebbia più densa con fasci di luce più visibili.

- `[0.02, 0.02, 0.02]` -- foschia sottile.
- `[0.1, 0.1, 0.1]` -- nebbia evidente.
- `[0.5, 0.5, 0.5]` -- nebbia spessa, impenetrabile.

Il coefficiente di estinzione totale è `sigma_t = sigma_a + sigma_s`. Questo determina l'opacità complessiva del mezzo (quanto velocemente la visibilità cala con la distanza).

---

## 9.3 Funzioni di Fase: Come la luce si diffonde

La funzione di fase determina la distribuzione angolare della luce diffusa.

### Isotropic (Isotropa - Predefinita)

```yaml
phase: "isotropic"
```

La luce si diffonde equamente in tutte le direzioni. Questo è il modello più semplice e funziona bene per foschia generica o fumo.

### Henyey-Greenstein

```yaml
phase: "hg"
g: 0.85
```

La funzione di fase Henyey-Greenstein (HG) permette una distorsione direzionale:

| Valore di `g` | Comportamento                                      |
|---------------|----------------------------------------------------|
| `0.0`         | Identico a isotropic                               |
| `0.3`         | Lieve scattering in avanti (foschia sottile)       |
| `0.7`         | Forte scattering in avanti (nebbia, nuvole)        |
| `0.85`        | Scattering molto concentrato in avanti (nebbia densa, caligine) |
| `-0.3`        | Scattering all'indietro (insolito, artistico)      |

Lo scattering in avanti (`g > 0`) significa che la luce tende a continuare all'incirca nella stessa direzione dopo aver colpito una particella. Questo è fisicamente accurato per la maggior parte dei mezzi reali (nebbia, polvere, aerosol) e crea bagliori luminosi intorno alle sorgenti luminose quando viste attraverso il mezzo.

Alias: `"hg"`, `"henyey_greenstein"`.

---

## 9.4 Ricette pratiche

### Nebbia leggera (Light Fog)

Una foschia sottile che ammorbidisce gli oggetti distanti e aggiunge atmosfera senza oscurare la scena.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.005, 0.005, 0.005]
    sigma_s: [0.04, 0.04, 0.04]
    phase: "hg"
    g: 0.8
```

### Caligine densa (Dense Mist)

La visibilità cala a poche unità. Le sorgenti luminose creano bagliori luminosi e drammatici.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.01, 0.01, 0.01]
    sigma_s: [0.15, 0.15, 0.15]
    phase: "hg"
    g: 0.85
```

### Sott'acqua (Underwater)

L'acqua assorbe la luce rossa più velocemente di quella blu. Più si guarda in profondità, più la scena diventa blu. Uno scattering moderato crea fasci di luce visibili dalla superficie.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.12, 0.06, 0.02]
    sigma_s: [0.02, 0.02, 0.02]
    phase: "hg"
    g: 0.6
```

### Foschia tinta (Golden Hour Atmosphere)

Foschia atmosferica calda che diffonde una luce giallo-oro, creando un effetto magico tipico dell'ora d'oro.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.002, 0.005, 0.015]
    sigma_s: [0.03, 0.025, 0.015]
    phase: "hg"
    g: 0.75
```

### Fumo denso (Thick Smoke)

Mezzo molto denso, quasi opaco, con forte scattering isotropo.

```yaml
world:
  medium:
    type: "homogeneous"
    sigma_a: [0.05, 0.05, 0.05]
    sigma_s: [0.4, 0.38, 0.35]
    phase: "isotropic"
```

---

## 9.5 Considerazioni sul rendering

Il rendering volumetrico è più impegnativo del rendering solo superficiale. Tieni a mente questi suggerimenti:

1. **Aumentare i campioni (samples).** Il mezzo aggiunge un'altra fonte di rumore (eventi di scattering casuali lungo ogni raggio). Usare almeno 64 SPP; 256+ per risultati puliti.

2. **Aumentare la profondità (depth).** Ogni evento di scattering conta come un rimbalzo. Con un mezzo denso, i raggi possono diffondersi più volte prima di raggiungere una luce. Usa `-d 12` (nebbia/fumo densi); la Russian Roulette termina automaticamente i path molto lunghi, quindi valori sopra `-d 16` raramente migliorano la qualità.

3. **Le luci spot creano i fasci di luce (God Rays).** Una luce spot che brilla attraverso la nebbia produce un cono di luce visibile. Questo è uno degli effetti più spettacolari possibili con i mezzi partecipanti.

4. **Le luci puntiformi brillano.** Nella nebbia, ogni luce puntiforme riceve un alone radiale morbido la cui dimensione dipende dalla densità del mezzo.

5. **Il mezzo è globale.** Colpisce ogni raggio nella scena, compresi i raggi d'ombra (le luci appaiono più deboli attraverso la nebbia). Non c'è modo di confinare il mezzo a un volume specifico -- riempie l'intero spazio del mondo.

6. **Iniziare da valori sottili, poi aumentare.** È più facile aggiungere nebbia che rimuoverla. Iniziare con valori di `sigma_s` molto bassi (0.01--0.03) e aumentare fino a ottenere l'effetto desiderato.

---

## 9.6 Esempio Completo: Cattedrale nella Nebbia

```yaml
# cathedral-fog.yaml
# Pilastri di pietra nella nebbia con una luce spot che crea un raggio visibile.

world:
  ambient_light: [0.005, 0.005, 0.008]
  background: [0.01, 0.01, 0.02]
  medium:
    type: "homogeneous"
    sigma_a: [0.008, 0.008, 0.008]
    sigma_s: [0.07, 0.07, 0.07]
    phase: "hg"
    g: 0.82

cameras:
  - name: "main"
    position: [0, 1.5, -6]
    look_at: [0, 2, 2]
    fov: 55

lights:
  # L'effetto principale: una luce spot che crea un fascio visibile nella nebbia
  - type: "spot"
    position: [0, 4.8, 4]
    direction: [0, -0.7, -0.3]
    color: [1.0, 0.92, 0.75]
    intensity: 120.0
    inner_angle: 10
    outer_angle: 22

  # Lieve riempimento per far sì che i pilastri non siano completamente neri
  - type: "point"
    position: [0, 4, -4]
    color: [0.5, 0.55, 0.7]
    intensity: 8.0

materials:
  - id: "floor"
    type: "disney"
    roughness: 0.7
    texture:
      type: "checker"
      scale: 0.5
      colors: [[0.25, 0.22, 0.2], [0.15, 0.13, 0.12]]

  - id: "stone_pillar"
    type: "disney"
    roughness: 0.6
    specular: 0.3
    texture:
      type: "marble"
      scale: 5.0
      noise_strength: 3.0
      colors: [[0.65, 0.6, 0.55], [0.4, 0.37, 0.33]]
      randomize_offset: true

  - id: "ceiling"
    type: "lambertian"
    color: [0.2, 0.18, 0.16]

entities:
  # Pavimento
  - type: "infinite_plane"
    point: [0, 0, 0]
    normal: [0, 1, 0]
    material: "floor"

  # Soffitto
  - type: "infinite_plane"
    point: [0, 5, 0]
    normal: [0, -1, 0]
    material: "ceiling"

  # Fila sinistra di pilastri
  - type: "cylinder"
    center: [-2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 1

  - type: "cylinder"
    center: [-2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 2

  - type: "cylinder"
    center: [-2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 3

  # Fila destra di pilastri
  - type: "cylinder"
    center: [2, 0, -2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 4

  - type: "cylinder"
    center: [2, 0, 2]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 5

  - type: "cylinder"
    center: [2, 0, 6]
    radius: 0.3
    height: 5.0
    material: "stone_pillar"
    seed: 6
```

Esegui il rendering con:

```
RayTracer -i cathedral-fog.yaml -w 1200 -H 800 -s 256 -d 12
```

La luce spot crea un drammatico fascio visibile che taglia la nebbia tra i pilastri. La funzione di fase HG concentrata in avanti (g=0.82) focalizza il bagliore attorno alla direzione del raggio, proprio come nella nebbia reale.

---

## Cosa si è imparato

- **sigma_a** controlla l'assorbimento (oscuramento della luce con la distanza).
- **sigma_s** controlla lo scattering (densità della nebbia, fasci di luce).
- La funzione di fase **isotropic** diffonde equamente; **Henyey-Greenstein** permette una preferenza direzionale (scattering in avanti per la nebbia).
- Il mezzo è globale e influenza tutti i raggi, comprese le ombre.
- Le scene volumetriche necessitano di più campioni e profondità rispetto alle scene solo superficiali.
- Le luci spot nella nebbia creano fasci di luce (god rays); le luci puntiformi creano aloni.

---

[Precedente: Constructive Solid Geometry (CSG)](./08-csg.md) | [Successivo: Librerie di asset e scene complete](./10-libraries-and-projects.md) | [Indice del Tutorial](./README.md)
