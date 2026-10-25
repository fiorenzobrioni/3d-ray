# scenes/review - scene di verifica e di riferimento

Scene costruite durante la revisione del motore (`PIANO-REVIEW.md`) per controllare un risultato nel renderer completo, non solo nei test unitari. Restano nel repo come riferimento e come esempi: ognuna dice nell'intestazione cosa verifica, come renderizzarla e cosa ci si deve aspettare.

Le scene confrontate con Mitsuba stanno invece in `validation/scenes/`; gli showcase delle funzionalità in `scenes/showcases/`. Le immagini di confronto prodotte durante la revisione sono in `images/`.

| Scena | Fase | Cosa verifica | Risultato atteso |
|---|---|---|---|
| `white-furnace-classic.yaml` | 5 | Fornace bianca di `lambertian`, `metal` (ruvido e specchio), `dielectric` ruvido | Ogni sfera vale 1 nel buffer beauty (il vetro ruvido 0.98: scattering singolo dell'interfaccia) |
| `openpbr-white-furnace.yaml` | 5 | Fornace bianca del materiale `openpbr` in tutte le configurazioni della specifica | Ogni sfera vale 1 (vetro ruvido 0.98) |
| `disney-to-openpbr.yaml` | 5 | Il tipo `disney` prima e dopo la traduzione su OpenPBR: plastica liscia, velluto, lacca, vetro colorato, metallo spazzolato, foglia | Confronto in `images/disney-to-openpbr.jpg`: plastica senza firefly, velluto senza energia in più, lacca col riflesso pieno |
| `texture-frames.yaml` | 4 | Frame tangente di normal map e bump: tre cubi con la stessa normal map ruotati di 0°, 90°, 45°; tre sfere con lo stesso bump procedurale, ferma, ruotata, scalata 2× | `images/texture-frames.jpg`: fughe incavate su ogni faccia di ogni cubo; le tre sfere con lo stesso rilievo (la prima e la terza identiche) |
| `light-units.yaml` | 6 | Unità fotometriche: lampadina in lumen, spot in candele, spot con profilo IES, pannello in nit, sopra un pavimento grigio; camera in EV100 | Luminanza del pavimento sotto ogni luce uguale al calcolo a mano nell'intestazione (2.596, 8.868, 17.892, 19.793 nit) entro lo 0.1%; `images/light-units.jpg` |
| `daylight-units.yaml` | 6 | Cielo Hosek-Wilkie in nit con il sole coerente, esposizione EV100 15 ("sunny 16") | Pavimento grigio 18% al sole uguale a 0.18 × E / π, con E l'illuminanza che il loader stampa (4290 contro 4308 nit); ombre piene; `images/daylight-units.jpg`, confronto dei tre modelli in `images/daylight-sky-models.jpg`, prima/dopo delle scene con cielo fisico in `images/sky-before-after.jpg` |
| `path-depths.yaml` | 7 | Profondità per tipo di rimbalzo: tre gusci di vetro concentrici attorno a un nocciolo rosso (6 trasmissioni più un rimbalzo diffuso), otto pannelli con cut-out a scacchi uno dietro l'altro | Con `-q draft-small` nocciolo rosso e tutti i pannelli visibili dai buchi; con `--depth-transmission 4` nocciolo nero, con `--depth-transparent 4` buchi neri; `images/path-depths.jpg` (prima, con la profondità 4 per tutto: nocciolo nero) |
| `guiding-room.yaml` | 7b | Path guiding: stanza chiusa illuminata dal sole attraverso una finestra, quasi tutta la luce di rimbalzo dalla macchia di sole | Con `--guiding on` stessa immagine (media entro il rumore) e circa il 20% di errore in meno a parità di campioni (240 × 135, 256 spp), a circa 1.4 volte il tempo per campione; `images/guiding-room.jpg` (sopra senza, sotto con guiding, stesso numero di campioni) |
| `vcm-pool.yaml` | 8b | Integratori bidirezionali: fondo di una piscina sotto una luce piccola, visto attraverso l'acqua mossa (cammini luce → acqua → fondo → acqua → camera) | A parità di tempo (480 × 270, circa 40 s) path tracing e `--integrator bdpt` mostrano solo firefly sparsi, `--integrator vcm` la rete di caustiche sulle piastrelle; `images/vcm-pool.jpg` |
| `subdivision-creases.yaml` | 2 | Pieghe di Catmull-Clark: lo stesso cubo suddiviso quattro volte con tutti gli spigoli in piega, nitidezza 0, 1, 2 e infinita, vertici sulla superficie limite | Da sinistra a destra spigoli sempre più vivi; l'ultimo è un cubo con facce piatte e spigoli netti nell'ombreggiatura; `images/subdivision-creases.jpg` |
| `denoiser-bokeh.yaml` | 9 | Denoiser su luci piccole e sfocate (bokeh a f/1.2): `--denoiser none`, `nfor`, `oidn` a 16 campioni | Con `oidn` dischi rotondi e puliti come nel riferimento a 1024 campioni; con `nfor` dischi squadrati e sfumati (§3.33, NFOR resta il ripiego senza librerie native); `images/denoiser-bokeh.jpg` (sopra none e nfor, sotto oidn e il riferimento) |

## Come leggere una fornace bianca

Cielo bianco uniforme di radianza 1, nessuna luce (`lights: []`: senza questa riga il loader aggiunge due luci di default), molti rimbalzi:

```
dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \
  -i scenes/review/openpbr-white-furnace.yaml -o renders/furnace.png \
  -s 256 -d 32 --view standard --denoiser none -w 640 -H 360 --aov beauty
```

Il file `renders/furnace.beauty.pfm` contiene la radianza lineare: lo sfondo vale 1 e una sfera che non assorbe deve valere 1 a sua volta. Più scura significa energia persa, più chiara energia creata.

Misure con il codice di chiusura della Fase 5 (256 campioni, 640x360):

| Sfera | Valore |
|---|---|
| lambert, metal, mirror | 1.0001, 1.0007, 1.0000 |
| openpbr diffuse, metal, coat, fuzz, film, aniso | 1.001, 1.000, 0.998, 0.999, 0.998, 0.999 |
| openpbr thin_sss, opacity, everything | 0.999, 1.000, 0.999 |
| vetro liscio, vetro ruvido (r 0.3) | 1.000, 0.984 |
