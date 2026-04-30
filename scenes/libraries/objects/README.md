# 📦 Libreria Oggetti — Template Riutilizzabili

Raccolta di **~150 template** e **~220 materiali** dedicati, organizzati in 11 librerie tematiche. Ogni file contiene oggetti composti professionali costruiti con primitive, **lathe (superficie di rivoluzione)**, CSG, torus, sfere scalate (ellissoidi) e gruppi annidati. Oltre 20 template sfruttano la primitiva `lathe` per corpi torniti di livello professionale — vedi la sezione [Profili di rivoluzione (lathe)](#profili-di-rivoluzione-lathe) più avanti.

---

## Struttura

```
scenes/libraries/objects/
├── furniture.yaml            11 template — Arredamento da interni
├── decorative-objects.yaml   12 template — Oggetti decorativi e da esposizione
├── tableware.yaml            11 template — Stoviglie e tavola
├── architecture.yaml         15 template — Elementi architettonici classici
├── mechanical.yaml           14 template — Meccanica e industria
├── jewelry.yaml              14 template — Gioielleria
├── lighting.yaml             15 template — Apparecchi di illuminazione
├── laboratory.yaml           14 template — Attrezzatura da laboratorio
├── musical.yaml              14 template — Strumenti musicali
├── outdoor.yaml              15 template — Arredo esterno e giardino
└── nature.yaml               15 template — Piante, fiori e natura
```

---

## Come Usare

### 1. Importare una libreria

Nella sezione `imports:` della scena, aggiungere il percorso relativo:

```yaml
imports:
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/lighting.yaml"
```

Questo importa sia i **materiali** (con prefisso dedicato) che i **template** della libreria.

### 2. Istanziare un oggetto

Nella sezione `entities:`, usare `type: "instance"` con il nome del template:

```yaml
entities:
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  - type: "instance"
    template: "lampada_tavolo"
    translate: [0.50, 0.78, 0]       # Sopra il tavolo
```

### 3. Sovrascrivere il materiale

Ogni template ha un materiale di default. Per cambiarlo, usare il campo `material`:

```yaml
  - type: "instance"
    template: "sedia_classica"
    material: "frn_mogano"            # Mogano invece di ciliegio
    translate: [0.60, 0, -0.40]
```

> **Nota:** l'override del materiale si applica come fallback globale. I figli con materiale esplicito (es. la lampadina emissiva in una lampada) mantengono il proprio materiale — non vengono sovrascritti.

### 4. Trasformare le istanze

Scale, rotazione e traslazione si applicano normalmente:

```yaml
  - type: "instance"
    template: "scaffale"
    translate: [-2, 0, 3]
    rotate: [0, 90, 0]               # Ruotato di 90° attorno a Y
    scale: 1.2                        # Ingrandito del 20%
```

Scale asimmetrico funziona: `scale: [1.0, 0.8, 1.0]` schiaccia in altezza.

> **⚠️ Best practice trasformazioni su primitive**
>
> Su primitive che hanno un parametro `center:` (sphere, cylinder, cone,
> capsule, torus), **non combinare `center:` con `rotate:` o `scale:`**.
> Lo scale e la rotate vengono sempre applicati attorno all'**origine
> globale**, non attorno al `center:` della primitiva. Combinandoli si
> ottiene un riposizionamento inatteso del primitivo.
>
> Pattern corretto: lascia `center` a default `[0, 0, 0]` (omettilo) e usa
> `translate:` per il posizionamento finale. ComputeTransformMatrix applica
> sempre l'ordine `scale → rotate → translate`, quindi posizionare via
> `translate` dopo `rotate` produce sempre il risultato atteso.
>
> ```yaml
> # ❌ Sbagliato — il rotate ruota la sfera attorno all'origine globale
> - type: "sphere"
>   center: [0, 1.5, 0]
>   radius: 0.3
>   rotate: [0, 0, 90]
>
> # ✅ Corretto — la sfera è in (0,0,0), si scala/ruota localmente, poi posiziona
> - type: "sphere"
>   radius: 0.3
>   rotate: [0, 0, 90]
>   translate: [0, 1.5, 0]
> ```
>
> `box` e `mesh` non hanno parametro `center:` e usano nativamente `translate:`,
> quindi non sono soggetti a questo problema. Anche le istanze di `template`
> usano direttamente `translate:`/`rotate:`/`scale:` ed è il pattern corretto.

### 5. Combinare più librerie

Import multipli si fondono automaticamente. I materiali e template locali della scena sovrascrivono quelli importati con lo stesso `id`/`name`:

```yaml
imports:
  - path: "libraries/metals.yaml"              # Materiali metalli
  - path: "libraries/objects/furniture.yaml"   # Mobili
  - path: "libraries/objects/lighting.yaml"    # Lampade
  - path: "libraries/objects/tableware.yaml"   # Stoviglie
  - path: "libraries/objects/nature.yaml"      # Piante

materials:
  # Override locale: rendi il paralume rosso invece che panna
  - id: "frn_paralume"
    type: "disney"
    color: [0.65, 0.15, 0.10]
    roughness: 0.90

entities:
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  - type: "instance"
    template: "lampada_tavolo"
    translate: [0.40, 0.78, 0.20]

  - type: "instance"
    template: "bicchiere_vino"
    translate: [-0.25, 0.80, -0.15]

  - type: "instance"
    template: "rosa"
    translate: [0, 0.80, 0]

  - type: "instance"
    template: "bonsai"
    translate: [-1.5, 0, 1.0]
```

---

## Catalogo Completo

### 🪑 furniture.yaml — Arredamento

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `tavolo_classico` | Tavolo da pranzo con gambe tornite e fascia sottopiano | 1.40 × 0.76 × 0.80 m |
| `sedia_classica` | Sedia con schienale a stecche e gambe coniche | 0.44 × 0.90 × 0.44 m |
| `tavolino_caffe` | Tavolino rotondo con colonna tornita | Ø 0.80 × 0.45 m |
| `lampada_tavolo` | Lampada con paralume conico cavo (CSG) e lampadina emissiva | Ø 0.28 × 0.48 m |
| `lampada_terra` | Lampada da terra alta con paralume e lampadina emissiva | Ø 0.38 × 1.65 m |
| `scaffale` | Libreria a 5 ripiani con bordi arrotondati | 0.80 × 1.80 × 0.30 m |
| `sgabello_bar` | Sgabello con seduta imbottita e poggiapiedi ad anello | Ø 0.36 × 0.75 m |
| `comodino` | Comodino con cassetto e pomello in ottone | 0.45 × 0.55 × 0.40 m |
| `candelabro` *(lathe)* | Candeliere a 3 bracci con stelo tornito (lathe) e fiamme emissive | Ø 0.40 × 0.50 m |
| `vaso_decorativo` | Vaso classico a profilo sagomato, cavo (CSG) | Ø 0.24 × 0.35 m |
| `candelabro_tornito` *(lathe)* | Candelabro barocco a braccio singolo, integralmente tornito (singolo lathe) | Ø 0.18 × 0.58 m |

Prefisso materiali: `frn_`

### 🏺 decorative-objects.yaml — Oggetti Decorativi

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `obelisco` | Obelisco egizio con pyramidion dorato (CSG) | 0.14 × 0.65 m |
| `sfera_piedistallo` *(lathe)* | Sfera su piedistallo tornito (singolo lathe Catmull-Rom) | Ø 0.26 × 0.50 m |
| `clessidra` | Clessidra con bulbi in vetro (CSG profonda) e telaio | 0.14 × 0.32 m |
| `globo` | Globo con anello meridiano inclinato 23.5° | Ø 0.30 × 0.42 m |
| `coppa_trofeo` | Coppa con manici ad anello (mezzo torus CSG) | Ø 0.22 × 0.40 m |
| `uovo_ornamentale` | Uovo Fabergé su treppiede con gemma | Ø 0.12 × 0.22 m |
| `sfera_armillare` | Sfera armillare con 4 anelli orbitali | Ø 0.34 × 0.50 m |
| `fermacarte` | Cupola di cristallo emisferica (CSG) con glow | Ø 0.10 × 0.09 m |
| `piramide_cristallo` | Piramide trasparente (CSG cono ∩ box) con glow | 0.12 × 0.18 m |
| `colonnina` *(lathe)* | Colonnina con fusto lathe Linear ad entasi + 8 scanalature CSG | Ø 0.18 × 0.55 m |
| `vaso_ming` *(lathe)* | Vaso cinese classico — piede, pancia bombata, collo alto (lathe) | Ø 0.22 × 0.42 m |
| `anfora_greca` *(lathe)* | Anfora greca con corpo lathe e 2 manici mezzo-torus CSG | Ø 0.22 × 0.38 m |

Prefisso materiali: `dec_`

### 🍷 tableware.yaml — Stoviglie e Tavola

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `bicchiere_vino` *(lathe)* | Calice con coppa ovoidale, singolo lathe Catmull-Rom | Ø 0.08 × 0.22 m |
| `calice_cristallo` *(lathe)* | Calice con doppio nodo e bordo dorato, corpo lathe | Ø 0.09 × 0.20 m |
| `tumbler` | Bicchiere basso da whisky con fondo spesso | Ø 0.08 × 0.10 m |
| `tazza_caffe` | Tazza con manico e piattino, bordo dorato | Ø 0.12 × 0.10 m |
| `piatto_piano` | Piatto con tesa, cavetto e specchio concavo (CSG) | Ø 0.26 × 0.025 m |
| `bottiglia_vino` *(lathe)* | Bordolese — corpo/spalla/collo/labbro in un solo lathe + punt CSG | Ø 0.08 × 0.32 m |
| `decanter` *(lathe)* | Decanter in cristallo con pancia bombata e collo alto svasato | Ø 0.18 × 0.28 m |
| `caraffa` | Caraffa con corpo bombato e manico (mezzo torus) | Ø 0.14 × 0.28 m |
| `teiera` | Teiera con beccuccio articolato e coperchio a cupola | Ø 0.22 × 0.16 m |
| `zuccheriera` | Ciotola con coperchio, 2 manici e pomello | Ø 0.12 × 0.12 m |
| `cucchiaio` | Cucchiaio con paletta concava (CSG) | 0.04 × 0.005 × 0.19 m |

Prefisso materiali: `tbw_`

### 🏛️ architecture.yaml — Elementi Architettonici

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `colonna_dorica` | Fusto scanalato (CSG 12 cilindri), echino, abaco | Ø 0.46 × 3.00 m |
| `colonna_ionica` | Base attica, fusto scanalato, volute a spirale | Ø 0.44 × 3.20 m |
| `colonna_liscia` *(lathe)* | Colonna toscana con fusto lathe Linear ad entasi vitruviana | Ø 0.40 × 3.00 m |
| `arco_tutto_sesto` | Arco semicircolare con pilastri e chiave di volta | 1.40 × 2.80 × 0.40 m |
| `arco_gotico` | Arco ogivale (CSG 2 cilindri intersecati) | 1.30 × 3.20 × 0.40 m |
| `scalinata` | 7 gradini con proporzioni reali (17 × 30 cm) | 1.40 × 1.19 × 2.10 m |
| `balaustro` *(lathe)* | Balaustro singolo — plinto, ventre, collarini, capitello in un solo lathe | Ø 0.10 × 0.70 m |
| `balaustra` | Sezione con 5 balaustri, zoccolatura e corrimano | 1.20 × 0.90 × 0.14 m |
| `pilastro` | Pilastro rettangolare a ridosso di muro | 0.30 × 3.00 × 0.08 m |
| `frontone` | Timpano triangolare (CSG) su trabeazione | 2.40 × 0.70 × 0.40 m |
| `cupola` | Cupola emisferica con tamburo, lanterna e croce | Ø 2.00 × 2.40 m |
| `nicchia` | Nicchia murale con volta a semicilindro (CSG) | 0.60 × 1.60 × 0.35 m |
| `portale_classico` | Portale con trabeazione e 15 dentelli | 1.80 × 2.80 × 0.40 m |
| `cornicione` | Sezione modulare con dentelli e gocciolatoio | 1.00 × 0.30 × 0.35 m |
| `pinnacolo` *(lathe)* | Pinnacolo architettonico tornito con cuspide terminale | Ø 0.18 × 0.80 m |

Prefisso materiali: `arc_`

### ⚙️ mechanical.yaml — Meccanica e Industria

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `ingranaggio` | Ruota dentata a 8 denti con foro e chiavetta (CSG) | Ø 0.12 × 0.025 m |
| `dado_esagonale` | Dado M10 esagonale (CSG 3 box) con svasature | Ø 0.019 × 0.008 m |
| `bullone` | Bullone a testa esagonale M10×40 con filetto | Ø 0.019 × 0.045 m |
| `vite_brugola` | Vite con incavo esagonale (CSG) in ossido nero | Ø 0.016 × 0.035 m |
| `cuscinetto` | Cuscinetto a sfere con 8 sfere e piste (CSG) | Ø 0.06 × 0.016 m |
| `pistone` | Pistone con sedi fasce e spinotto (CSG) | Ø 0.08 × 0.07 m |
| `valvola` | Valvola a saracinesca con volantino rosso | 0.18 × 0.30 m |
| `flangia` | Flangia cieca con 6 fori passanti (CSG) | Ø 0.16 × 0.022 m |
| `puleggia` | Puleggia con gola trapezoidale e chiavetta (CSG) | Ø 0.12 × 0.030 m |
| `molla` | Molla elicoidale a 6 spire (torus impilati) | Ø 0.04 × 0.10 m |
| `biella` | Biella con testa, fusto a I e piede (CSG) | 0.02 × 0.16 m |
| `manometro` | Manometro con vetro emisferico (CSG) e lancetta | Ø 0.10 × 0.08 m |
| `cilindro_idraulico` | Cilindro a doppio effetto con stelo cromato | Ø 0.06 × 0.32 m |
| `giunto_flangiato` | Giunto rigido a flange con 4 bulloni | Ø 0.10 × 0.06 m |

Prefisso materiali: `mec_`

### 💎 jewelry.yaml — Gioielleria

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `gemma_brillante` | Taglio brillante rotondo (CSG 2 coni) | Ø 0.030 × 0.020 m |
| `gemma_cabochon` | Taglio cabochon a cupola liscia (CSG) | Ø 0.025 × 0.014 m |
| `anello_solitario` | Anello con brillante in castone a 6 griffe | Ø 0.09 × 0.055 m |
| `anello_fascia` | Fede nuziale con bordi milgrain | Ø 0.09 × 0.03 m |
| `anello_trilogy` | Anello con 3 gemme zaffiro in fila | Ø 0.09 × 0.05 m |
| `anello_sigillo` | Anello con piatto ovale in onice | Ø 0.09 × 0.04 m |
| `pendente_goccia` | Pendente con gemma ametista a goccia | 0.025 × 0.065 m |
| `pendente_croce` | Croce latina con rubino centrale | 0.035 × 0.070 m |
| `orecchino_cerchio` | Creola in oro con cerniera | Ø 0.04 × 0.003 m |
| `orecchino_goccia` | Orecchino pendente con topazio | 0.02 × 0.065 m |
| `bracciale_rigido` | Bangle con 3 gemme smeraldo cabochon | Ø 0.070 × 0.012 m |
| `collana_perle` | Filo di 15 perle graduate con chiusura | 0.30 × 0.04 m |
| `tiara` | Diadema con 5 punte e diamanti | 0.14 × 0.06 m |
| `portagioie` | Cofanetto aperto con cuscino in velluto | 0.08 × 0.05 × 0.06 m |

Prefisso materiali: `jwl_` — Scala ~5×: `scale: 0.2` per dimensioni reali.

### 💡 lighting.yaml — Illuminazione

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `lampadario_classico` | 6 bracci con candele e gocce di cristallo | Ø 0.60 × 0.55 m |
| `sospensione_sfera` | Globo opalino pendente | Ø 0.30 × 0.50 m |
| `sospensione_industriale` | Paralume a campana metallica | Ø 0.35 × 0.45 m |
| `sospensione_tiffany` | Cupola a vetri colorati in 3 fasce (CSG) | Ø 0.40 × 0.45 m |
| `applique_classica` | Applique con braccio curvo e paralume (CSG) | 0.16 × 0.30 × 0.22 m |
| `applique_moderna` | Up/down LED rettangolare | 0.08 × 0.18 × 0.10 m |
| `plafoniera` *(lathe)* | Cupola opalina a diffusore, lathe Catmull-Rom | Ø 0.35 × 0.12 m |
| `faretto` | Faretto orientabile su giunto sferico | Ø 0.10 × 0.18 m |
| `lampione_classico` | Lampione vittoriano a 3 lanterne | Ø 0.60 × 3.50 m |
| `lampione_moderno` | Palo conico con pannello LED | 0.40 × 4.50 m |
| `lanterna` | Lanterna pensile con pannelli vetro ambrato | Ø 0.22 × 0.45 m |
| `neon_anello` | Anello LED sospeso con 3 cavi | Ø 0.50 × 0.03 m |
| `neon_tubo` | Tubo fluorescente con riflettore (CSG) | 0.04 × 0.06 × 1.20 m |
| `torcia_medievale` | Torcia da muro con fiamma doppia emissiva | 0.12 × 0.45 × 0.14 m |
| `paralume_svasato` *(lathe)* | Paralume troncoconico cavo (CSG di due lathe) con emissione interna | Ø 0.30 × 0.22 m |

Prefisso materiali: `lit_` — Tutti emettono luce reale (NEE).

**Orientamento pendenti:** Y=0 = soffitto, l'apparecchio pende verso Y negativo.
**Orientamento applique:** Z=0 = muro, sporge verso Z negativo.

### 🔬 laboratory.yaml — Laboratorio

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `provetta` | Tubo con fondo emisferico cavo (CSG) | Ø 0.018 × 0.15 m |
| `beuta_erlenmeyer` *(lathe)* | Beuta conica — profilo lathe Linear con collo e labbro | Ø 0.10 × 0.18 m |
| `pallone_distillazione` *(lathe)* | Pallone sferico — bulbo + collo in un solo lathe + raccordo CSG | Ø 0.12 × 0.22 m |
| `matraccio` | Matraccio tarato con linea di taratura | Ø 0.10 × 0.26 m |
| `becher` | Becher con beccuccio e graduazioni | Ø 0.08 × 0.11 m |
| `cilindro_graduato` | Cilindro alto con base esagonale (CSG) | Ø 0.04 × 0.30 m |
| `imbuto` *(lathe)* | Imbuto conico con gambo — profilo lathe Linear solido | Ø 0.10 × 0.16 m |
| `mortaio_pestello` | Mortaio emisferico con pestello inclinato | Ø 0.12 × 0.10 m |
| `bunsen` | Becco Bunsen con fiamma blu doppia emissiva | Ø 0.05 × 0.20 m |
| `portaprovette` | Rack in legno per 6 provette (CSG fori) | 0.22 × 0.10 × 0.06 m |
| `microscopio` | Microscopio con 3 obiettivi e manopole | 0.15 × 0.35 × 0.10 m |
| `condensatore` | Condensatore a riflusso tubo-in-tubo | Ø 0.04 × 0.30 m |
| `storta` | Storta alchemica con collo curvo | 0.12 × 0.18 × 0.12 m |
| `bilancia_analitica` | Bilancia a bracci con 2 piatti sospesi | 0.30 × 0.30 × 0.08 m |

Prefisso materiali: `lab_` — Vetro borosilicato (Pyrex) IOR 1.47.

### 🎵 musical.yaml — Strumenti Musicali

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `campana` *(lathe)* | Campana in bronzo con profilo acustico lathe Catmull-Rom + batacchio | Ø 0.30 × 0.30 m |
| `tamburo_rullante` | Rullante con 8 tiranti e chiavette | Ø 0.36 × 0.18 m |
| `timpano` *(lathe)* | Timpano con caldaia in rame lathe Catmull-Rom + pelle | Ø 0.65 × 0.50 m |
| `diapason` | Diapason in acciaio con 2 rebbi | 0.025 × 0.12 m |
| `metronomo` | Metronomo a piramide tronca (CSG) con pendolo | 0.12 × 0.22 × 0.11 m |
| `gong` | Gong su telaio in ebano con boss | Ø 0.60 × 0.80 m |
| `piatto_batteria` | Piatto convesso su asta con treppiede | Ø 0.40 × 0.90 m |
| `xilofono` | Xilofono a 8 lame con mazzuole | 0.60 × 0.20 × 0.12 m |
| `violino` | Violino con riccio, ponte, 4 corde e mentoniera | 0.14 × 0.06 × 0.60 m |
| `chitarra_acustica` | Chitarra con buca, rosetta, 6 corde e meccaniche | 0.38 × 0.10 × 1.00 m |
| `tromba` | Tromba con campana cava (CSG) e 3 pistoni | 0.14 × 0.14 × 0.50 m |
| `grammofono` | Grammofono vintage con tromba conica (CSG) | 0.40 × 0.35 × 0.40 m |
| `maracas` | Coppia di maracas con corpo sferico laccato | Ø 0.07 × 0.25 m cad. |
| `triangolo` | Triangolo in acciaio con battente | 0.18 × 0.20 m |

Prefisso materiali: `mus_`

### 🌳 outdoor.yaml — Arredo Esterno

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `panchina` | Panchina con doghe in teak e ghisa (arco CSG) | 1.50 × 0.80 × 0.60 m |
| `tavolo_picnic` | Tavolo con panche integrate e cavalletti ad A | 1.60 × 0.75 × 0.75 m |
| `fontana` | Fontana a 3 vasche con zampillo d'acqua | Ø 1.20 × 1.50 m |
| `fioriera` *(lathe)* | Vaso terracotta cavo — CSG di lathe esterno e interno | Ø 0.40 × 0.38 m |
| `staccionata` | Sezione con 5 picchetti e punte coniche | 1.20 × 1.00 × 0.08 m |
| `pergolato` | Sezione con 2 colonne e 4 travetti | 2.00 × 2.50 × 0.30 m |
| `pozzo` | Pozzo medievale con tetto, argano e secchio (CSG) | Ø 1.00 × 2.20 m |
| `idrante` | Idrante antincendio con 2 attacchi in ottone | Ø 0.20 × 0.70 m |
| `cestino` | Cestino cilindrico cavo (CSG) con coperchio a cupola | Ø 0.40 × 0.90 m |
| `cassetta_postale` | Cassetta USA su palo con bandierina rossa | 0.20 × 1.20 × 0.22 m |
| `birdbath` | Bagno per uccelli con colonna tornita e vasca (CSG) | Ø 0.50 × 0.75 m |
| `meridiana` | Meridiana su piedistallo con gnomone in ottone | Ø 0.30 × 0.80 m |
| `gazebo` | Gazebo esagonale con 6 colonne e tetto a padiglione | Ø 3.00 × 3.20 m |
| `barbecue` | Barbecue sferico su treppiede (CSG emisfere cave) | Ø 0.55 × 0.90 m |
| `vaso_giardino_classico` *(lathe)* | Grande vaso a imboccatura larga — lathe esterno/interno CSG | Ø 0.55 × 0.70 m |

Prefisso materiali: `out_`

> **♟️ Chess set rimosso.** La libreria `chess.yaml` (set Staunton + 3
> scacchiere + 2 scatole porta-pezzi) e lo starter `starter-chess-set.yaml`
> sono stati rimossi in attesa di una rifattorizzazione futura più curata
> (cavallo, dettagli ornamentali). Il pattern di costruzione lathe +
> CSG resta documentato in `decorative-objects.yaml` (vasi, anfore) e
> `tableware.yaml` (calici, bottiglie).

### 🌿 nature.yaml — Piante, Fiori e Natura

| Template | Descrizione | Dimensioni |
|----------|-------------|------------|
| `albero_latifoglia` | Albero deciduo con chioma a nuvola (sfere) | Ø 2.50 × 3.50 m |
| `abete` | Abete conico con 5 palchi di rami | Ø 1.40 × 3.20 m |
| `palma` | Palma con tronco segmentato e 8 fronde | Ø 2.00 × 3.80 m |
| `cactus_saguaro` | Saguaro con 2 bracci e costolature | Ø 0.80 × 2.20 m |
| `bonsai` | Bonsai in vaso ceramica con tronco contorto | Ø 0.30 × 0.35 m |
| `fungo_porcino` | Porcino con gambo bulboso e cappello (CSG) | Ø 0.12 × 0.12 m |
| `fungo_amanita` | Amanita muscaria con macchie bianche | Ø 0.10 × 0.14 m |
| `rosa` | Rosa aperta con 12 petali stratificati | Ø 0.10 × 0.35 m |
| `tulipano` | 3 tulipani colorati in vaso | Ø 0.12 × 0.38 m |
| `girasole` | Girasole con disco e 12 petali a corona | Ø 0.22 × 0.90 m |
| `ninfea` | Ninfea con foglia galleggiante e fiore bianco | Ø 0.30 × 0.08 m |
| `pianta_grassa` | Echeveria con rosetta a spirale in vaso | Ø 0.14 × 0.18 m |
| `masso` | Formazione di 5 pietre sovrapposte | Ø 0.80 × 0.55 m |
| `tronco` | Ceppo con radici esposte e anelli di crescita | Ø 0.40 × 0.35 m |
| `pietre_zen` | Cairn di 5 pietre lisce impilate | Ø 0.15 × 0.25 m |

Prefisso materiali: `nat_` — Fogliame con `subsurface` per traslucenza.

---

## Convenzioni Generali

### Origine e Orientamento

Tutti i template hanno la **base a Y=0** e sono centrati in XZ. Per posizionarli serve solo `translate: [x, 0, z]` — la base poggia automaticamente sul pavimento.

**Eccezioni:**
- **Lampade a sospensione** (lighting.yaml): Y=0 = soffitto, pendono verso Y negativo.
- **Applique**: Z=0 = muro, sporgono verso Z negativo.

### Prefissi Materiali

Ogni libreria usa un prefisso unico per evitare collisioni:

| Prefisso | Libreria |
|----------|----------|
| `frn_` | furniture |
| `dec_` | decorative-objects |
| `tbw_` | tableware |
| `arc_` | architecture |
| `mec_` | mechanical |
| `jwl_` | jewelry |
| `lit_` | lighting |
| `lab_` | laboratory |
| `mus_` | musical |
| `out_` | outdoor |
| `nat_` | nature |

### Override Materiali

Per personalizzare un materiale importato, ridefiniscilo nella scena con lo stesso `id`. La definizione locale vince sempre su quella importata.

### Seed e Variazione

I materiali con `randomize_offset: true` (cortecce, pietre, marmi) generano automaticamente una variazione diversa per ogni istanza grazie al seed deterministico. Non serve fare nulla: due alberi affiancati avranno corteccia diversa.

---

## Profili di Rivoluzione (lathe)

Oltre 20 template di queste librerie sfruttano la primitiva `lathe` (superficie di rivoluzione) — marcati con *(lathe)* nelle tabelle sopra. Il motivo: i corpi assi-simmetrici (calici, bottiglie, vasi, colonne tornite, balaustri, vetreria da laboratorio, campane, paralumi) sono intrinsecamente generati per rivoluzione di un profilo 2D attorno all'asse Y. Una singola primitiva `lathe` con profilo Catmull-Rom produce:

- **silhouette C¹ continua** — nessuna discontinuità di normale al raccordo fra segmenti, impossibile da ottenere impilando sfere/coni/torus;
- **meno primitive** — un balaustro classico passa da 9 primitive (cylinder/sphere/cone/torus/box) a **un solo** lathe;
- **UV coerenti** — U lungo l'angolo azimutale, V lungo la lunghezza d'arco del profilo, utile per texture decal;
- **editing rapido** — modificare la forma significa cambiare pochi punti `[r, y]` nel profilo, non ricalcolare CSG annidati.

### Tre strategie implementate nelle librerie

1. **Lathe solido per vetro trasparente.** Calici, bottiglie e vetreria da laboratorio (Pyrex IOR 1.47, cristallo IOR 1.62) sono un singolo `lathe` pieno: la rifrazione del materiale crea naturalmente l'effetto ottico della parete sottile, senza bisogno di CSG cavo. Esempi: `bicchiere_vino`, `bottiglia_vino`, `decanter`, `beuta_erlenmeyer`, `pallone_distillazione`, `imbuto`.

2. **Lathe solido per opachi non cavi.** Pezzi torniti in legno, pietra, bronzo o porcellana dove non serve vedere una cavità interna: il lathe solido sostituisce una catena di primitive. Esempi: `balaustro`, `colonna_liscia`, `pinnacolo`, `sfera_piedistallo`, `vaso_ming`, `anfora_greca` (corpo), `campana`, `timpano` (caldaia), stelo di `candelabro`, `candelabro_tornito`.

3. **CSG di due lathe per gusci a parete sottile opachi.** Quando il materiale è opaco (terracotta, tessuto) e la cavità interna deve essere visibile, due lathe in `csg: subtraction` generano un guscio di spessore costante. Esempi: `fioriera`, `vaso_giardino_classico`, `paralume_svasato`.

### Schema YAML di base

```yaml
- type: "lathe"
  material: "..."
  profile_type: "catmull_rom"   # oppure "linear" | "bezier"
  profile:
    - [0.000, 0.000]   # [r, y] — minimo 2 punti (4 per catmull_rom)
    - [0.050, 0.000]
    - [0.040, 0.100]
    - [0.000, 0.120]
```

Regole chiave:
- **r ≥ 0**, asse di rivoluzione sempre Y.
- **y monotona non-decrescente** (il loader auto-ordina e avvisa se non lo è).
- **catmull_rom** richiede ≥ 4 punti (downgrade a linear con warning).
- Se `r_first > 0` viene aggiunto un **tappo inferiore**; se `r_last > 0` un tappo superiore. Per chiudere dolcemente sull'asse, termina con `[0, y]`.

Per il riferimento completo (inclusi Bezier e costi di intersezione), vedi [Capitolo 11: Superfici di rivoluzione](../../../docs/tutorial/it/11-lathe-surface-of-revolution.md).

---

## Esempi di Scene

### Salotto

```yaml
imports:
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/lighting.yaml"
  - path: "libraries/objects/decorative-objects.yaml"
  - path: "libraries/objects/nature.yaml"

entities:
  - type: "instance"
    template: "tavolo_classico"
    translate: [0, 0, 0]

  - type: "instance"
    template: "sedia_classica"
    translate: [0, 0, -0.70]
    rotate: [0, 180, 0]

  - type: "instance"
    template: "lampada_terra"
    translate: [-1.5, 0, 0.8]

  - type: "instance"
    template: "scaffale"
    translate: [2, 0, 1.5]
    rotate: [0, -90, 0]

  - type: "instance"
    template: "candelabro"
    translate: [0, 0.78, 0]

  - type: "instance"
    template: "bonsai"
    translate: [0.50, 0.78, 0.25]
```

### Scena all'aperto

```yaml
imports:
  - path: "libraries/objects/outdoor.yaml"
  - path: "libraries/objects/nature.yaml"

entities:
  - type: "instance"
    template: "fontana"
    translate: [0, 0, 0]

  - type: "instance"
    template: "panchina"
    translate: [3, 0, 0]
    rotate: [0, -90, 0]

  - type: "instance"
    template: "albero_latifoglia"
    translate: [-4, 0, 2]

  - type: "instance"
    template: "albero_latifoglia"
    translate: [5, 0, -3]
    scale: 0.8
    rotate: [0, 120, 0]

  - type: "instance"
    template: "lampione_classico"
    translate: [2, 0, -4]

  - type: "instance"
    template: "staccionata"
    translate: [-3, 0, -5]

  - type: "instance"
    template: "staccionata"
    translate: [-1.8, 0, -5]
```

### Laboratorio dell'alchimista

```yaml
imports:
  - path: "libraries/objects/furniture.yaml"
  - path: "libraries/objects/laboratory.yaml"
  - path: "libraries/objects/lighting.yaml"

entities:
  - type: "instance"
    template: "tavolo_classico"
    material: "frn_mogano"
    translate: [0, 0, 0]

  - type: "instance"
    template: "beuta_erlenmeyer"
    translate: [-0.30, 0.80, 0]

  - type: "instance"
    template: "pallone_distillazione"
    translate: [0, 0.80, 0]

  - type: "instance"
    template: "bunsen"
    translate: [0.30, 0.80, 0]

  - type: "instance"
    template: "storta"
    translate: [-0.50, 0.80, 0.15]

  - type: "instance"
    template: "bilancia_analitica"
    translate: [0.50, 0.80, -0.10]

  - type: "instance"
    template: "torcia_medievale"
    translate: [-2, 1.8, 0]

  - type: "instance"
    template: "torcia_medievale"
    translate: [2, 1.8, 0]
```
