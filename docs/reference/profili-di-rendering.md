# Guida ai Profili di Rendering

Questo documento è il riferimento pratico per regolare i tre parametri di qualità di 3D-Ray — `-s` (campioni per pixel), `-d` (profondità massima dei rimbalzi), `-S` (campioni d'ombra) — più il firefly clamp `-C`. Definisce tre profili canonici (**Preview**, **Standard**, **Final**), spiega come ciascun parametro si mappa sull'architettura interna del motore ed elenca i tip per non sprecare tempo di render.

---

### 1. **I TRE PROFILI CANONICI**

| Profilo | `-s` | griglia | `-d` | `-S` | Uso tipico |
|---|---|---|---|---|---|
| **Preview** (Bozza) | `64` | 8×8 | `4` | `1` | Composizione scena, posizionamento camere, colori materiali. Veloce e granuloso. |
| **Standard** (Medio) | `256` | 16×16 | `6` | `1` (o `4`) | CI/CD, render di review, anteprime nei log. Pulito con grana filmica. |
| **Final** (Vetrina) | `1024` | 32×32 | `8` | `4` | Portfolio, copertina del README, render promozionali. Croccante e pubblicabile. |

Tutti i valori della tabella sono **quadrati perfetti** di proposito (vedi sezioni 3 e 5).

**Comandi pronti da copiare:**
```bash
# Preview — da pochi secondi a un minuto su una scena tipica
RayTracer -i my-scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard — adatto a CI/CD, ottimo per anteprime in linea nel README
RayTracer -i my-scene.yaml -w 800 -H 450 -s 256 -d 6

# Final — qualità portfolio / copertina del README
RayTracer -i my-scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4
```

---

### 2. **VALORI DI DEFAULT**

| Parametro | Default | Origine |
|---|---|---|
| `-s` / `--samples` | `16` (griglia 4×4) | `Program.cs` |
| `-d` / `--depth` | `8` | `Program.cs` |
| `-S` / `--shadow-samples` | non impostato → valore YAML per-luce | `Program.cs` |
| `-C` / `--clamp` | `100` (firefly clamp) | `Renderer.DefaultMaxSampleRadiance` |
| `--sampler` | `sobol` (Owen scramble) | `Program.cs` / `Sampler.SetKind` |

> Il sampler default `sobol` (Burley 2020, Owen scrambling hash-based sulla tabella Joe-Kuo) converge più rapidamente del PRNG thread-local su pixel jitter, lens sampling e primi bounce. Passa `--sampler prng` per tornare al vecchio comportamento — utile quando confronti con render storici o debugghi regressioni stocastiche.

I default sono pensati per **iterazione rapida**, non per qualità finale. Considera il profilo Preview come il minimo per un render "presentabile"; usa Standard o Final quando devi pubblicare.

---

### 3. **COMPRENDERE `-s` (CAMPIONI PER PIXEL)**

Il motore esegue **stratified sampling** su una griglia √N × √N per pixel. Passare `-s 16` produce una griglia 4×4 (16 campioni); `-s 256` una griglia 16×16 (256 campioni). Ogni cella produce un campione jittered.

**I quadrati perfetti sono "gratis".** Se passi un valore non quadrato il motore arrotonda √N per eccesso, quindi `-s 100` diventa 10×10 = 100 (esatto), ma `-s 15` diventa silenziosamente 4×4 = 16. Per controllare il costo con precisione, preferisci: `1, 4, 9, 16, 25, 36, 49, 64, 100, 144, 196, 256, 400, 576, 784, 1024, 1600`.

**Costo:** approssimativamente lineare — raddoppiando `-s` raddoppi all'incirca il tempo di render.

---

### 4. **COMPRENDERE `-d` (PROFONDITÀ MASSIMA DEI RIMBALZI)**

`-d` limita il numero di rimbalzi indiretti che un raggio può effettuare. Nel path tracing i primi 4–6 rimbalzi indiretti contribuiscono per circa il 99% all'illuminazione realistica nella maggior parte delle scene.

**Perché il default è 8 (non 50):** il renderer usa **Russian Roulette adattiva** (`Renderer.cs`). Per scene con illuminazione normale RR si attiva al 4° rimbalzo terminando stocasticamente i path a basso contributo; per scene indirect-dominant (solo emissive, luci deboli) si attiva all'8° con una soglia di sopravvivenza più alta. Alzare `-d` oltre questo punto raramente cambia l'immagine ma costa sempre tempo.

**Quando alzare `-d` sopra 8:**
- **Dielettrici impilati** — liquidi nei bicchieri, file di bottiglie di vino, sfere di vetro annidate. Ogni interfaccia di entrata/uscita consuma un rimbalzo, quindi 10 interfacce di vetro richiedono `-d 16–20` o il vetro più interno diventa inaspettatamente nero.
- **Mezzi partecipanti densi con geometria complessa** dietro al volume.

Per tutto il resto (oggetti opachi, singole lastre di vetro, metalli, interni normali) **`-d 4–8` è abbondante**.

---

### 5. **COMPRENDERE `-S` (CAMPIONI D'OMBRA)**

`-S` forza un override globale del numero di raggi d'ombra usati da ogni luce ad area (`AreaLight`, `SphereLight`, `GeometryLight`). Ogni luce costruisce una griglia stratificata √N × √N sulla propria superficie e lancia un raggio d'ombra per cella.

**Attenzione — costo moltiplicativo.** A ogni intersezione di superficie il motore lancia `S` raggi d'ombra **per ogni luce ad area, per ogni campione pixel, per ogni rimbalzo**. Con `-s 256`, `-S 4`, due luci ad area e 6 rimbalzi parli di circa `256 × 4 × 2 × 6 ≈ 12.000` raggi d'ombra per pixel. Alzare `-S` è il modo più rapido per distruggere i tempi di render.

**Regola empirica:**
- Default / Preview / Standard → `-S 1`.
- Alza `-S` a `4` (2×2) o `9` (3×3) solo quando il render generale è già pulito ma le penombre morbide a terra restano l'unica fonte di rumore.
- Usa quadrati perfetti (`1, 4, 9, 16`) — stessa stratificazione √N×√N di `-s`.

I campioni pixel (`-s`) e i campioni d'ombra (`-S`) riducono entrambi il rumore d'ombra. Spendi prima il budget su `-s`; ricorri a `-S` solo quando hai verificato che il collo di bottiglia siano le ombre (non la GI).

---

### 6. **FIREFLY CLAMP (`-C` / `--clamp`)**

`MaxSampleRadiance` (esposto come `-C`) è il limite massimo per la radianza per-campione **prima del tone mapping**. Cattura gli outlier rari prodotti da caustiche speculari, compensazione delle Disney lobe e boost della Russian Roulette — i pixel che altrimenti apparirebbero come puntini bianchi luminosi ("fireflies") nel render.

**Default:** `100`. Sufficientemente alto da preservare i highlight degli emissive, sufficientemente basso da uccidere gli spike numerici.

**Quando abbassare `-C`:**
- Nebbia densa / mezzi omogenei spessi + `-d` forzato alto.
- Scene con molti piccoli emissive luminosi visti attraverso il vetro.
- Prova `-C 25` (aggressivo) o `-C 15` (pesante). Perdi un po' di gamma dinamica HDR sui highlight più caldi ma guadagni ombre più pulite e penombre più morbide ai rimbalzi estremi.

**Quando alzare `-C`:**
- HDRI con sole molto intenso dove il disco solare appare meno luminoso del previsto.
- Prova `-C 500` o disattiva il clamp di fatto con un valore molto alto. Il rischio sono i fireflies.

Il clamp usa **scaling con preservazione della luminanza**, quindi non altera la tinta sui highlight luminosi — solo la luminosità.

---

### 7. **TIP PRATICI**

- **Inizia ogni scena in Preview.** Itera composizione e materiali con `-s 64 -d 4 -S 1` finché l'immagine non si legge correttamente. Solo allora promuovi a Standard o Final.
- **Spendi il budget su `-s` prima di `-S` o `-d`.** I campioni pixel attaccano ogni fonte di rumore contemporaneamente (GI, ombre, speculare); gli altri due agiscono su problemi specifici.
- **`-d 4` è il sweet spot per Preview** perché la Russian Roulette in scene normali si attiva esattamente al 4° rimbalzo — oltre, ti affidi comunque a RR.
- **Non combinare `-s` alto con `-S` alto senza motivo.** `-s 1024 -S 16` è quasi sempre uno scambio cattivo. `-s 1024 -S 4` di solito eguaglia visivamente a ¼ del costo.
- **Le scene cariche di vetro sono l'unico motivo legittimo per superare `-d 8`.** Alza `-d` a `16` o `20` solo quando vedi interni inaspettatamente neri nei dielettrici impilati.
- **Riproducibilità in CI.** I valori `-s`/`-d`/`-S` nei `.github/workflows/*.yml` dovrebbero essere un profilo nominato specifico (tipicamente Preview o uno Standard ridotto) — non valori ad-hoc.

---

### 8. **TIP SPECIFICI PER MEZZI PARTECIPANTI (NEBBIA / FUMO)**

3D-Ray supporta quattro tipi di medium globali — `homogeneous`, `height_fog`,
`procedural`, `grid` — più le phase function `isotropic`, `hg`, `rayleigh`,
`double_hg`, `schlick`. Ogni tipo ha un costo/rumore molto diverso.

- **Homogeneous e height_fog sono "gratis"** rispetto al rendering normale: la
  trasmittanza ha forma chiusa, nessun delta tracking. Un Preview è già
  usabile; uno Standard è quasi sempre sufficiente.
- **Procedural (Perlin fBm) e grid usano delta tracking** (Woodcock) + ratio
  tracking: più rumorosi per costruzione, soprattutto nelle zone dense.
  - Preview mostra grana evidente — va bene per la composizione.
  - Per immagini pubblicabili punta a `-s 576` (24×24) o `-s 1024`.
  - Se vedi rumore concentrato nel cono luminoso, aumenta `-s` (non `-S`).
- **Firefly clamp con nebbia densa.** Mezzi con `sigma_s` alto e `-d 8+`
  producono talvolta spike luminosi rari. Abbassa `-C` a `25` o `15` senza
  timore: perdi poco dinamica, guadagni molto pulito.
- **Non alzare `-d` per la nebbia.** Il path volumetrico è già gestito
  correttamente a `-d 6–8`. Più rimbalzi nella nebbia = più costo, non più
  realismo (la Russian Roulette termina comunque i cammini).
- **Phase function con g → 1 (es. HG g=0.95)** rende god-ray più stretti e
  drammatici ma **aumenta la varianza**: se vedi coni "rumorosi", abbassa `g`
  a 0.7-0.85 oppure passa a `double_hg` con pesi più equilibrati.
- **Rayleigh** è economica (closed-form) e utile per cieli/atmosfera.
  `double_hg` e `schlick` costano quanto HG standard.
- **Grid medium: attenzione alla risoluzione.** Griglie inline-YAML fino a 8³
  sono ok; sopra passa al formato binario `.vol` (campo `file:` invece di
  `data:`). La risoluzione non incide sul costo di rendering — solo sul
  tempo di parsing e sull'uso di memoria.

**Profilo consigliato per showcase volumetric:**
```bash
# Preview volumetric (controllo composizione, ~30-60 s)
RayTracer -i scene.yaml -w 400 -H 225 -s 64 -d 4 -S 1

# Standard volumetric (review; delta-tracking ancora un po' rumoroso)
RayTracer -i scene.yaml -w 800 -H 450 -s 400 -d 6 -S 1

# Final volumetric (pulizia pubblicabile)
RayTracer -i scene.yaml -w 1920 -H 1080 -s 1024 -d 8 -S 4 -C 50
```

---

### 9. **DOCUMENTAZIONE CORRELATA**

- [`docs/technical/path-tracing-and-lighting.md`](../technical/path-tracing-and-lighting.md) — interni del path tracer, NEE e Russian Roulette.
- [`docs/technical/rendering-pipeline.md`](../technical/rendering-pipeline.md) — panoramica della pipeline.
- [`docs/reference/riferimento-scene.md`](./riferimento-scene.md) — schema YAML delle scene.
