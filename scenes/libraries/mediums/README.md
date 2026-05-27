# Libreria Medium — 3D-Ray

Raccolta di medium volumetrici per il motore 3D-Ray: **5 file YAML tematici**
con **~135 medium** pronti all'uso, calibrati su dati ottici della letteratura
scientifica (Jensen 2001, Narasimhan 2006, Frisvad 2007).

I medium descrivono il **trasporto volumetrico interno** di un oggetto 3D:
scattering, assorbimento e lunghezza del cammino libero medio. Completano i
materiali di superficie (BSDF) per produrre Subsurface Scattering (SSS)
fotorealistico tramite Random Walk volumetrico (SSS Phase 3 del motore).

## Differenza tra `material` e `medium`

| Concetto | Descrive | Parametri chiave | File libreria |
|----------|----------|------------------|---------------|
| **material** | Superficie (BSDF) | albedo, roughness, metallic, spec_trans, clearcoat… | `libraries/materials/` |
| **medium** | Volume interno | σ_a, σ_s, phase function, g | `libraries/mediums/` |

La distinzione pratica è semplice:

- Il **material** controlla cosa succede quando un raggio **tocca** la superficie
  (riflessione, rifrazione, diffusione speculare).
- Il **medium** controlla cosa succede quando la luce **entra dentro** l'oggetto
  (quanto viene assorbita, quanto diffusa, in che direzione).

Un'entity può avere entrambi contemporaneamente — e spesso è proprio questa
combinazione a produrre i risultati più fotorealistici:

```yaml
- name: "scultura_marmo"
  type: sphere
  material: "dis_carrara_lucido"        # dalla libreria materials/stones.yaml
  interior_medium: "med_marmo_carrara"  # dalla libreria mediums/stones.yaml
```

### Medium vs `subsurface` Disney BSDF

Il parametro `subsurface` del materiale Disney è un'**approssimazione
analitica** del SSS (modello dipolo di Jensen): economica, ma limitata.
Non cattura effetti di colore profondi, ombre portate interne, o trasparenza
bidirezionale.

Il **medium volumetrico** fa un vero **Random Walk** nel volume: la luce entra,
rimbalza tra le particelle secondo σ_s e la phase function, viene assorbita
secondo σ_a per ogni canale RGB, e alla fine esce (o viene estinta). Il
risultato è fisicamente corretto per qualsiasi spessore di materiale, per luce
in trasmissione (controluce), e per oggetti cavi.

**Quando usare cosa:**

- `subsurface` Disney → render draft veloci, skin semplice, scene non in
  primo piano. Converge rapidamente.
- `interior_medium` volumetrico → close-up fotorealistici, controluce su
  alabastro/cera/ghiaccio, latte, pelle in primo piano. Richiede sample
  count più alto (vedi sezione 7).

## Come funzionano i medium in 3D-Ray

- **σ_a** (assorbimento) e **σ_s** (scattering) sono vettori RGB con un valore
  per canale: `[R ~700nm, G ~550nm, B ~450nm]`. Unità: per world-unit.
- Alto σ_a su un canale significa che quel colore viene **assorbito** (non
  percepito). Es.: σ_a blu alto → oggetto appare giallo/ambrato.
- Alto σ_s + basso σ_a → materiale bianco/lattiginoso/traslucente (alabastro,
  latte, neve fresca).
- Il motore esegue un **Random Walk volumetrico**: campiona la distanza al
  prossimo evento di scattering, aggiorna la direzione secondo la phase
  function, accumula l'assorbimento Beer-Lambert lungo il percorso.
- **Il material deve essere rifrattivo** per far entrare la luce nel medium:
  usa `dis_*` con `spec_trans > 0` (Disney), oppure `type: dielectric`, oppure
  Disney con `flatness > 0` (superfici ceroses). Un Lambertian puro non fa
  entrare la luce e il medium non è visibile.
- **Phase function:** controlla la direzione preferita dello scattering dopo
  ogni evento. Vedi sezione 8 per quale scegliere.

## Calibrazione della scala — sezione critica

Questa è la considerazione più importante per l'uso corretto dei medium.

**σ_a e σ_s sono in "per world-unit"**: il loro effetto visivo dipende
direttamente dalla dimensione dell'oggetto in world-unit. Un oggetto grande
assorbe e diffonde di più (percorso più lungo); uno piccolo, di meno.

### Regola pratica

Le librerie `stones`, `organics`, e `ice-snow` sono calibrate per oggetti
**tipici** con raggio 0.1–0.5 wu (1 wu ≈ 10–30 cm nelle scene 3D-Ray standard).
La libreria `atmospherics` è calibrata per volumi di scala metrica/paesaggio
(1 wu ≈ 1–5 m).

Se l'oggetto è più piccolo o più grande del range calibrato, scala σ_a e σ_s
proporzionalmente:

> **σ_scala = σ_libreria × (scala_riferimento / scala_oggetto)**

Esempio: un med_marmo_carrara è calibrato per raggio ≈ 0.3 wu. Se il tuo
oggetto ha raggio 0.03 wu (10× più piccolo), moltiplica σ per 10.

### Tabella esempi pratici

| Tipo oggetto | Dimensione | Fattore σ | Libreria |
|---|---|---|---|
| Sfera di marmo protagonista | radius 0.3 wu | σ × 1 (as-is) | `stones` |
| Sfera di marmo in miniatura | radius 0.03 wu | σ × 10 | `stones` |
| Scultura grande | radius 1.5 wu | σ × 0.2 | `stones` |
| Blocco di ghiaccio standard | 1×1×1 wu | σ × 1 (as-is) | `ice-snow` |
| Blocco di ghiaccio grande | 2×2×2 wu | σ × 0.5 | `ice-snow` |
| Candela di cera | radius 0.05 wu | σ × 4 | `organics` |
| Nebbia su paesaggio | volume 50 wu | usare as-is | `atmospherics` |
| Nuvola isolata | volume 5 wu | σ × 0.3 | `atmospherics` |

### Come scalare inline nella scena

Non è necessario modificare la libreria. Puoi definire un medium inline con σ
scalati, oppure scrivere un medium derivato direttamente nella scena:

```yaml
mediums:
  - id: "med_marmo_carrara_mini"    # versione per oggetto piccolo (radius 0.03)
    type: homogeneous
    sigma_a: [0.021, 0.041, 0.071]  # σ_a × 10
    sigma_s: [21.9,  26.2,  30.0 ]  # σ_s × 10
    phase:   isotropic
```

## Come usare nelle scene

Importa una o più librerie medium nella sezione `imports:` della scena,
insieme alle librerie material corrispondenti. I path sono relativi alla
directory della scena (tipicamente `scenes/`).

### Pattern 1 — Import semplice

```yaml
imports:
  - { path: "libraries/materials/stones.yaml" }
  - { path: "libraries/mediums/stones.yaml" }

entities:
  - name: scultura
    type: sphere
    center: [0, 1, 0]
    radius: 0.35
    material: dis_carrara_lucido
    interior_medium: med_marmo_carrara
```

### Pattern 2 — Material e medium diversi

La stessa superficie può contenere un volume di sostanza diversa (es. un vaso
di alabastro riempito d'acqua colorata):

```yaml
imports:
  - { path: "libraries/materials/stones.yaml" }
  - { path: "libraries/materials/liquids.yaml" }
  - { path: "libraries/mediums/stones.yaml" }
  - { path: "libraries/mediums/liquids.yaml" }

entities:
  # Vaso di alabastro pieno d'aria — SSS alabastro puro
  - name: vaso_alabastro
    type: sphere
    material: dis_alabastro_bianco
    interior_medium: med_alabastro_bianco

  # Sfera di vetro riempita d'acqua colorata
  - name: sfera_vetro_acqua
    type: sphere
    material: dis_acqua_piscina        # superficie rifrattiva acquosa
    interior_medium: med_acqua_pulita  # volume SSS acqua limpida
```

### Pattern 3 — Nebbia come globalMedium

Il `globalMedium` (nebbia su tutto il mondo) non usa la sezione `mediums:` — va
definito inline sotto `world:`. I medium della libreria `atmospherics` possono
servire come riferimento per i valori σ:

```yaml
world:
  background: [0.6, 0.7, 0.8]
  medium:
    type: height_fog
    sigma_a: [0.010, 0.011, 0.012]
    sigma_s: [0.035, 0.038, 0.040]
    y0: 0
    scale_height: 5
    phase: hg
    g: 0.5

# I valori sopra corrispondono a una nebbia da valle leggera —
# vedi atmospherics.yaml per tutti i preset di riferimento.
```

### Pattern 4 — Entity con medium bounded (SSS volumetrico completo)

```yaml
imports:
  - { path: "libraries/materials/organics.yaml" }
  - { path: "libraries/mediums/organics.yaml" }

entities:
  - name: candela_cera
    type: cylinder
    center: [0, 0, 0]
    radius: 0.05
    height: 0.3
    material: dis_cera_api         # Disney con spec_trans + flatness: superficie cerosa
    interior_medium: med_cera_api  # Random Walk volumetrico: la luce entra e diffonde
    # Effetto: la fiamma retrostante illumina il cilindro dall'interno con il
    # tipico arancio caldo della cera trasparente — impossibile con subsurface Disney.
```

La luce entra dalla superficie (grazie a `spec_trans` o `flatness` > 0),
percorre un cammino casuale nel volume dove ogni evento di scattering ridistribuisce
la direzione (phase function) e ogni tratto assorbe secondo σ_a. Il percepito è
un volume luminoso, caldo, con variazioni di densità cromatica proporzionali allo
spessore attraversato.

### Pattern 5 — Sfera di latte

```yaml
imports:
  - { path: "libraries/materials/liquids.yaml" }
  - { path: "libraries/mediums/liquids.yaml" }

entities:
  - name: tazza_latte
    type: sphere
    center: [0, 0.5, 0]
    radius: 0.3
    material: dis_latte_intero         # superficie lattiginosa (spec_trans basso)
    interior_medium: med_latte_intero  # volume SSS denso — σ_s altissimo
    # Il latte è uno dei materiali più scatteranti in natura (σ_s >> σ_a).
    # Risultato: bianco opalescente con luce trasmessa quasi nulla.
```

## I file della libreria

| File | N° medium | Categorie principali |
|------|----------:|---------------------|
| `stones.yaml` | 27 | Marmi bianchi e colorati, alabastro, onice, pietre preziose, travertino |
| `liquids.yaml` | ~35 | Acque, latticini, vini e alcolici, sciroppi, oli, succhi, sangue |
| `organics.yaml` | ~30 | Pelle, cere, resine, ambra, polpe vegetali, tessuti animali, cioccolato |
| `atmospherics.yaml` | ~25 | Nebbie, fumi, vapore, nuvole, polvere cosmica, aria |
| `ice-snow.yaml` | ~20 | Neve (fresca, compatta, sporca), ghiaccio (blu, chiaro, torbido), brina |
| **Totale** | **~137** | |

### `stones.yaml`

Marmi bianchi (Carrara, Statuario, Calacatta, Thassos), marmi colorati (Rosso
Verona, Rosa Portogallo, Giallo Siena, Verde Guatemala, Blu Sodalite, Nero
Marquinia, Port Laurent), alabastro gessoso (bianco, rosa), onice calcite
(miele, verde, nero), pietre preziose e semipreziose (giada verde/bianca,
quarzo rosa/fumé, ametista, citrino, opale bianco, calcedonio), travertino
romano, pietra leccese, alabastro egiziano. Valori σ calibrati da Jensen et
al. 2001 come riferimento primario; altri scalati coerentemente.

### `liquids.yaml`

Acque (piscina, mare, torrente, torbida, termale), latticini (intero, scremato,
panna, condensato), sangue (arterioso, venoso, secco), oli (oliva, semi,
motore), alcolici (vino rosso/bianco/rosé, birra, whisky, rum, vodka),
sciroppi (miele, acero, caramello, melassa), succhi di frutta, bevande calde,
refrigeranti. σ_s molto alti per i latticini (milkfat globules), bassi per i
liquidi trasparenti. Dati da Narasimhan et al. 2006.

### `organics.yaml`

Pelle umana (chiara, media, scura, abbronzata), cere (api, paraffina, soia,
candela), ambra (chiara, scura, rossa), resine vegetali, polpe di frutta,
cioccolato (fondente, latte, bianco), formaggio, grasso corporeo, midollo
osseo. Phase function HG con g=0.7–0.9 per pelle e tessuti biologici (forward
scattering pronunciato). Dati da Jensen 2001 + Tuchin 2007.

### `atmospherics.yaml`

Nebbia da valle (leggera, densa), nebbia marina, foschia, fumo (grigio,
bianco, nero), vapore acqueo, nuvole cumulus/stratus, polvere desertica, polvere
cosmica, aria (chiara, inquinata, a quota). σ calibrati per scala metrica (1 wu
≈ 1–5 m). Phase double_hg per nuvole, HG g>0.5 per fumi, Rayleigh per aria.

### `ice-snow.yaml`

Neve fresca (altissimo σ_s, isotropic), neve compatta, neve bagnata, neve
sporca, ghiaccio blu glaciale, ghiaccio chiaro, ghiaccio torbido, ghiaccio
brina (surface-coat). σ per ghiaccio da Warren & Brandt 2008; per neve da
Wiscombe & Warren 1980, scalati a world-unit 3D-Ray.

## Combinazioni raccomandate material + medium

| Material | File mat. | Medium | File med. | Effetto |
|---|---|---|---|---|
| `dis_carrara_lucido` | `stones` | `med_marmo_carrara` | `stones` | SSS marmo bianco classico, glow lattiginoso caldo |
| `dis_alabastro_bianco` | `stones` | `med_alabastro_bianco` | `stones` | Forte retroilluminazione traslucente, lampade/vasi |
| `dis_onice_miele` | `stones` | `med_onice_miele` | `stones` | Ambrato dorato caldo, retroilluminazione teatrale |
| `dis_quarzo_rosa` | `minerals-gems` | `med_quarzo_rosa` | `stones` | Rosa chiarissimo cristallino, quasi vetro |
| `dis_ametista_grezza` | `minerals-gems` | `med_ametista` | `stones` | Viola/lavanda semitrasparente |
| `dis_ghiaccio_blu` | `glasses` | `med_ghiaccio_blu_glaciale` | `ice-snow` | Ghiacciaio, iceberg, blocchi artici |
| `dis_neve_compatta` | `glasses` | `med_neve_fresca` | `ice-snow` | Neve retroilluminata con glow bluastro |
| `dis_acqua_piscina` | `liquids` | `med_acqua_pulita` | `liquids` | Acquario, piscina, vasche d'acqua limpida |
| `dis_latte_intero` | `liquids` | `med_latte_intero` | `liquids` | Latte/latticini, colori pastello lattiginosi |
| `dis_cera_api` | `organics` | `med_cera_api` | `organics` | Candela retroilluminata, scultura in cera |
| `dis_candela_avorio` | `organics` | `med_cera_paraffina` | `organics` | Candela bianca con glow arancio dalla fiamma |
| `dis_sapone_bianco` | `organics` | `med_sapone_bianco` | `organics` | Sapone traslucido, pelle SSS leggero |
| `dis_pelle_chiara` | `organics` | `med_pelle_chiara` | `organics` | Skin SSS fotorealistico per close-up |
| `dis_cioccolato_fondente` | `foods` | `med_cioccolato_fondente` | `organics` | Chocolate SSS scuro, glassa colata |
| `dis_ambra_chiara` | `organics` | `med_ambra_chiara` | `organics` | Ambra dorata traslucida con inclusioni |
| `dis_opale_bianco` | `minerals-gems` | `med_opale_bianco` | `stones` | Lattiginoso opalescente + iridescenza thin_film |
| `dis_calcite_islandese` | `minerals-gems` | `med_calcedonio` | `stones` | Cristallo chiaro con diffusione azzurrata |

## Sample count consigliati per SSS

Il Random Walk volumetrico converge più lentamente del BSDF puro: ogni campione
deve completare il percorso nel volume prima di contribuire al pixel.

| Scenario | Preview | Draft | Produzione |
|---|---:|---:|---:|
| Marmo / pietra chiara (σ_s alto, σ_a basso) | 64 spp | 128 spp | 512 spp |
| Onice / alabastro (traslucenza massima) | 128 spp | 256 spp | 1024 spp |
| Pelle umana close-up | 128 spp | 256 spp | 1024 spp |
| Cera candela retroilluminata | 64 spp | 128 spp | 512 spp |
| Ghiaccio (σ_s medio, trasparenza alta) | 64 spp | 128 spp | 512 spp |
| Neve (σ_s altissimo, cammino breve) | 32 spp | 64 spp | 256 spp |
| Latte / latticini (SSS opaco denso) | 32 spp | 64 spp | 256 spp |
| Nebbia / fumo leggero | 32 spp | 64 spp | 256 spp |
| Nebbia densa / nuvole | 64 spp | 128 spp | 512 spp |
| Cioccolato scuro (alta assorbenza) | 64 spp | 128 spp | 512 spp |

Vedi `docs/reference/rendering-profiles.md` per le scorciatoie
`-q draft-tiny / draft / medium / final / ultra`. Per scene con SSS volumetrico
usare `-d 6` o superiore (profondità di rimbalzo) e `-C 50` per limitare i
firefly da percorsi ad alta varianza.

## Note sulla phase function

La phase function controlla la **distribuzione angolare** dello scattering dopo
ogni evento nel volume. Scegliere quella giusta impatta fortemente l'aspetto
del materiale.

### `isotropic`

Scattering uniforme in tutte le direzioni. Ottimo per:

- Pietre (marmo, travertino, calcare) — cristalli calcitici scatterano
  isotropicamente a scale macroscopiche
- Cera e candele
- Neve fresca
- Latte e latticini
- Qualsiasi materiale dove non si conosce g

### `hg` (Henyey-Greenstein)

Un singolo lobo con parametro `g ∈ [-1, 1]`:

- `g = 0` → equivalente a isotropic
- `g > 0` → forward scattering (la luce preferisce continuare nella direzione
  originale) → pelle, tessuti biologici, nebbia, fumo leggero
- `g < 0` → backward scattering (retroriflettenza) → neve polverosa,
  retro-diffusione su substrati rugosi

Valori tipici per materiali biologici: `g = 0.7–0.9`. Per nebbia: `g = 0.5–0.7`.
Per alabastro e pietre gessose: `g = 0.1` (quasi isotropico con leggera
forward-scatter).

### `double_hg`

Due lobi HG (forward + backward) combinati con peso `f`:

- Nuvole (cumulus/stratus): forward forte `g1 = 0.85`, backward lieve
  `g2 = -0.3`, `f = 0.9`
- Fumi densi: forward `g1 = 0.7`, backward `g2 = -0.15`, `f = 0.8`

Cattura la gloria e il rainbow da gocce sferiche; più costoso di HG singolo.

### `rayleigh`

Scattering proporzionale a λ⁻⁴: diffonde molto di più il blu che il rosso.
Produce il cielo azzurro e i tramonti arancioni. Usare per:

- Aria pulita e atmosfera (quota alta)
- Gas puri a bassa densità
- Aria inquinata a pressione standard

Non usare per nebbia/pioggia/gocce (usare HG o double_hg che modellano
particelle Mie).
