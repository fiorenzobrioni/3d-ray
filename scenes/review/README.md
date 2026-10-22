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
