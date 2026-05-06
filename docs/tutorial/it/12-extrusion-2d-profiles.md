# Capitolo 12: Profili 2D Estrusi (Extrusion)

Il capitolo precedente ha introdotto il `lathe`: prendi un profilo 2D
e lo fai ruotare attorno a un asse. Quello copre tutto ciò che
metteresti su un tornio da vasaio — vasi, calici, colonne — ma una
famiglia enorme di oggetti del mondo reale è costruita nel modo
*opposto*: hanno una sezione costante che corre dritta lungo un asse,
come tagliabiscotti spinti dentro un panetto di pasta.

Pensaci un attimo. Una **trave a I in acciaio** ha la stessa forma a
`I` lungo tutta la sua lunghezza. Un **ingranaggio** ha la stessa
silhouette dentata in cima come in basso. Una **matita esagonale**,
una **cella esagonale del nido d'ape**, un **biscotto**, uno **scudo
araldico**, la **lettera A** in un logo 3D, un **medaglione tondo**,
un **pilastro a L** in cemento armato, una **cornice di finestra**,
una **rondella con foro quadrato** — niente di tutto questo si può
ottenere al tornio, ma ognuno di questi oggetti si descrive disegnando
la sezione una sola volta e dicendo "ora estendila per mezzo metro".

È esattamente ciò che fa la primitiva `extrusion` di 3D-Ray.

Si scrive un loop chiuso 2D nel piano XZ e si dice al motore quanto
deve essere alto. Ne esce un prisma con pareti laterali e tappi alle
estremità, pronto da renderizzare. Niente mesh esterne, niente
triangolazione manuale, niente trucchi booleani. E — a differenza di
un vero tagliabiscotti — la sezione può essere **concava**: stelle,
ingranaggi, croci, lettere, loghi, qualunque cosa tu sappia disegnare
senza incrociare le linee, il triangolatore automatico ear-clipping
del motore la gestisce per te.

Questo capitolo passa in rassegna le tre modalità di profilo
(`linear`, `catmull_rom`, `bezier`), i modificatori opzionali `twist`
e `taper` che trasformano un noioso prisma dritto in una colonna
scolpita, le regole sui tappi (caps), e una scena showcase completa
da copiare e modificare subito.

---

## 12.1 Modello Mentale: un Profilo nel Piano XZ

Una extrusion è definita da un **profilo 2D chiuso** nel piano XZ.
Ogni punto del profilo è una coppia `[x, z]` — il loop che disegni in
pianta, guardando dall'alto verso il basso lungo l'asse +Y.

Il motore prende quella sagoma piatta e la **estende lungo +Y** da
`y = 0` a `y = height`. Visivamente:

```
punto del profilo (x, z)  →  segmento verticale { (x, y, z)  |  y ∈ [0, height] }
```

L'intera superficie 3D è l'unione di:
- le **pareti laterali** — una striscia verticale per ogni edge del profilo,
- il **tappo inferiore** in `y = 0`,
- il **tappo superiore** in `y = height`.

**Regole del profilo** (il loader le fa rispettare e avvisa sulle
violazioni):

1. **Almeno 3 punti** — il profilo è un poligono, quindi serve almeno
   un triangolo.
2. **Il loop è implicitamente chiuso** — *non* ripetere il primo punto
   alla fine. Se lo fai, il loader scarta silenziosamente il
   duplicato.
3. **Niente auto-intersezioni** — gli edge del poligono non devono
   incrociarsi tra loro. Forme a otto produrrebbero tappi indefiniti.
4. **L'orientamento è auto-corretto** — il senso antiorario (CCW) è
   la convenzione canonica (interno alla sinistra di ogni edge). Se
   scrivi un profilo orario, il loader lo inverte silenziosamente per
   far sì che le normali delle pareti puntino comunque all'esterno.
   Non te ne devi preoccupare.
5. **I profili concavi vanno bene** — stelle, forme a L, ingranaggi
   funzionano tutti. Il triangolatore dei tappi (ear-clipping)
   gestisce robustamente i poligoni concavi semplici.

Una volta interiorizzata l'immagine — un contorno 2D spinto verso
l'alto — le tre modalità di profilo sono solo modi diversi di
disegnare quel contorno.

---

## 12.2 Profilo Linear — Il Prisma Sfaccettato

La modalità `linear` tratta il profilo come una **polilinea**: i punti
consecutivi sono uniti da edge rettilinei, e ogni edge diventa una
parete verticale piatta dopo l'estrusione. Le normali ai vertici sono
discontinue, quindi la silhouette ha ridge nette — esattamente il
look di un profilato di alluminio o di una sagoma in acrilico
tagliata a CNC.

```yaml
- name: "stella"
  type: "extrusion"
  profile_type: "linear"                # default — può essere omesso
  height: 0.4
  caps: "both"
  material: "oro"
  profile:                              # stella a 5 punte, 10 vertici
    - [ 0.55,  0.00]
    - [ 0.18,  0.13]
    - [ 0.17,  0.52]
    - [-0.07,  0.21]
    - [-0.44,  0.32]
    - [-0.22,  0.00]
    - [-0.44, -0.32]
    - [-0.07, -0.21]
    - [ 0.17, -0.52]
    - [ 0.18, -0.13]
```

| Parametro        | Default  | Descrizione                                                          |
|------------------|----------|----------------------------------------------------------------------|
| `profile_type`   | `linear` | Modalità di interpolazione                                           |
| `profile`        | --       | Loop chiuso di punti `[x, z]`, almeno 3                              |
| `height`         | `1`      | Lunghezza dell'estrusione lungo +Y                                   |
| `caps`           | `both`   | `both` / `start` / `end` / `none`                                    |
| `twist_degrees`  | `0`      | Rotazione totale del profilo superiore attorno a Y, in gradi         |
| `taper`          | `1`      | Scala XZ uniforme del profilo superiore (1 = dritto, < 1 si stringe) |
| `curve_samples`  | `16`     | Risoluzione della polilinea per i modi curvi (ignorato da `linear`)  |
| `material`       | --       | Applicato uniformemente a pareti e tappi                             |
| `center` / `translate` / `rotate` / `scale` | -- | Trasformazioni standard delle primitive             |

**Quando usarlo**

- Profilati industriali / architettonici (travi a I, profili a U,
  a T, a L, cornici, battiscopa).
- Forme araldiche: stelle, croci, scudi, vessilli.
- Ingranaggi, ruote dentate, denti d'arresto — la silhouette dentata
  *è* il punto di tutto e vuoi che ogni dente sia netto.
- Estrusioni di logo, tipografia 3D (la maggior parte delle lettere
  sono poligoni concavi-ma-semplici — non serve nessuna gestione
  speciale).
- Ogni volta che un oggetto del mondo reale verrebbe **lavorato a
  macchina o tagliato** invece che modellato.

**Che aspetto ha**

Spigoli netti a ogni vertice del profilo, identico a ciò che produce
una fresa CNC o un tagliabiscotti. Se vuoi una silhouette liscia, usa
una delle modalità spline qui sotto.

---

## 12.3 Profilo Catmull-Rom — La Colonna Scolpita

`catmull_rom` è la modalità che vuoi quando il profilo deve apparire
**liscio** e preferisci scriverlo elencando i punti per cui la curva
deve passare. Usa la stessa interpolazione Catmull-Rom centripetale
del lathe — la curva passa per ogni control point, è C¹ continua e
non sbanda mai.

```yaml
- name: "colonna_scanalata"
  type: "extrusion"
  profile_type: "catmull_rom"           # alias: "catmull", "smooth"
  height: 3.4
  twist_degrees: 60                     # leggera spirale lungo la colonna
  taper: 0.88                           # si restringe leggermente in alto
  curve_samples: 24                     # silhouette più liscia
  caps: "both"
  material: "marmo"
  profile:                              # sezione a 16 lobi
    - [ 0.55,  0.000]
    - [ 0.43,  0.180]
    - [ 0.39,  0.390]
    - [ 0.30,  0.300]
    - [ 0.000, 0.55]
    - [-0.300, 0.300]
    - [-0.39,  0.390]
    - [-0.43,  0.180]
    - [-0.55,  0.000]
    - [-0.43, -0.180]
    - [-0.39, -0.390]
    - [-0.300,-0.300]
    - [ 0.000,-0.55]
    - [ 0.300,-0.300]
    - [ 0.39, -0.390]
    - [ 0.43, -0.180]
```

Il loader densifica ogni edge di input in `curve_samples` piccoli
segmenti rettilinei prima di triangolare, ed emette pareti laterali
**con shading liscio (smooth-shaded)** in modo che la silhouette
appaia come una curva continua a qualsiasi zoom.

**Quando usarlo**

- Colonne scolpite, balaustre decorative, modanature ornamentali.
- Forme organiche morbide (la sezione di una foglia, un'arachide, un
  fagiolo, una macchia di erba a forma di nuvola).
- Qualunque sezione che disegneresti a mano libera cliccando punti
  lungo la silhouette.

**Vincoli**

- Servono almeno **3 punti** (con 3 punti ottieni essenzialmente un
  triangolo liscio — raro ma valido).
- `curve_samples` ha default 16 per segmento di input. Portalo a 24
  o 32 per primi piani da hero shot; abbassalo a 8 per oggetti
  distanti o affollati così da tenere la BVH compatta.

---

## 12.4 Profilo Bezier — Controllo Totale

Quando hai già una curva Bezier scritta a mano — esportata da
Illustrator, da un pacchetto CAD, da un file SVG, o da una specifica
pubblicata — usa la modalità `bezier` e fornisci esplicitamente i
quattro control point cubici per ogni segmento. Il profilo è
interpretato come **loop chiuso**: l'ultimo segmento richiude il loop
sul primo vertice.

```yaml
- name: "scudo"
  type: "extrusion"
  profile_type: "bezier"
  height: 0.3
  caps: "both"
  material: "vetro_smeraldo"
  profile:                              # endpoint di ogni segmento (N segmenti, chiuso)
    - [ 0.6,  0.0]
    - [ 0.0,  0.7]
    - [-0.6,  0.0]
    - [ 0.0, -0.5]
  profile_bezier_controls:              # esattamente 4 × N punti, concatenati
    # Segmento 1: destra → alto
    - [ 0.60,  0.00]                    # P0 (== profile[0])
    - [ 0.60,  0.42]                    # P1
    - [ 0.36,  0.70]                    # P2
    - [ 0.00,  0.70]                    # P3 (== profile[1])
    # Segmento 2: alto → sinistra
    - [ 0.00,  0.70]
    - [-0.36,  0.70]
    - [-0.60,  0.42]
    - [-0.60,  0.00]
    # Segmento 3: sinistra → basso
    - [-0.60,  0.00]
    - [-0.60, -0.30]
    - [-0.32, -0.50]
    - [ 0.00, -0.50]
    # Segmento 4: basso → destra (chiude il loop)
    - [ 0.00, -0.50]
    - [ 0.32, -0.50]
    - [ 0.60, -0.30]
    - [ 0.60,  0.00]
```

**Regole**

- `profile` elenca gli **endpoint di ogni segmento** — N punti
  definiscono N segmenti. Non esiste un "profilo aperto" in modalità
  `bezier`: l'ultimo segmento chiude sempre il loop su `profile[0]`.
- `profile_bezier_controls` elenca **quattro control point per
  segmento**, concatenati. Per N endpoint sono `4 × N` voci (nota:
  differisce dal lathe che ne richiede `4 × (N-1)`, perché qui il
  loop dell'estrusione è chiuso). Un conteggio sbagliato fa
  rifiutare l'entità con un warning differito.
- Per la **continuità C¹** tra segmenti, assicurati che
  `P3(segmento k) == P0(segmento k+1)` *e* che la tangente uscente
  combaci con quella entrante (`P3(k) - P2(k) == P1(k+1) - P0(k+1)`).
  Rompere questa regola produce uno **spigolo netto intenzionale** —
  perfetto per punte affilate, faccette di gemma o emblemi spigolosi.

**Quando usarlo**

- Hai già control data Bezier (SVG, Illustrator, CAD).
- Vuoi riprodurre una forma pubblicata definita da control point
  Bezier (tipografia, ornamenti classici, marchi aziendali).
- Vuoi controllo esplicito su direzione e modulo delle tangenti — per
  esempio per dare a un cuore esattamente la curvatura giusta in alto
  e la cuspide giusta in basso.

---

## 12.5 Tappi: Gusci Aperti, Vasche e Prismi Pieni

Il parametro `caps` sceglie quali estremità dell'extrusion vengono
chiuse da un tappo triangolato:

| Valore  | Tappo basso | Tappo alto | Caso d'uso                                          |
|---------|-------------|------------|-----------------------------------------------------|
| `both`  | sì          | sì         | Default. Prisma pieno, nessun foro.                 |
| `start` | sì          | no         | Una vasca, un bacile, il corpo di una tazza senza coperchio. |
| `end`   | no          | sì         | Un tetto, un coperchio, la falda di un cappello visto dal basso. |
| `none`  | no          | no         | Tubo puro — utile per fasce, tagliabiscotti, operandi CSG cavi. |

Una extrusion con tappi è un solido chiuso adatto come operando CSG
(puoi `subtract` un cilindro per perforare un foro, ad esempio). Una
extrusion `caps: "none"` è un guscio one-sided: i raggi attraversano
le estremità mancanti e il motore vede l'interno delle pareti. È
esattamente ciò che vuoi per una cornice di finestra o un tubo cavo,
ma squalifica l'extrusion dall'uso CSG finché non la chiudi.

---

## 12.6 Twist e Taper: Da Prismi a Colonne Scolpite

Una extrusion dritta sembra un profilato di alluminio. Aggiungi
`twist` o `taper`, e improvvisamente può scolpire forme che in
Blender richiederebbero uno stack di modificatori multi-step o in
Houdini una catena di `polyextrude`.

### Twist

`twist_degrees` ruota il profilo superiore attorno all'asse Y per
l'angolo specificato, con la rotazione che interpola linearmente
lungo l'altezza.

- Un **profilo quadrato + twist 45°** trasforma un prisma in un
  pilastro a spirale rastremato.
- Un **profilo a ingranaggio + twist 360°** trasforma un ingranaggio
  in una punta da trapano elicoidale.
- Un **profilo scanalato + twist 60°** trasforma una colonna nel
  tipo di elemento architettonico a cavatappi che si vede nelle
  opere di Antoni Gaudí.

Il twist si compone con la triangolazione dei tappi in modo
trasparente: il tappo inferiore è triangolato per il profilo non
ruotato, e quello superiore per la copia ruotata. Entrambi
appaiono corretti.

### Taper

`taper` è una scala XZ uniforme applicata al profilo superiore
relativamente a quello inferiore:

- `taper: 1.0` — prisma dritto (default).
- `taper: 0.0` — punto degenere in cima (vera piramide / pinnacolo).
  In pratica tienilo leggermente sopra zero (`0.01`) per evitare
  triangoli con area numericamente nulla.
- `taper: 0.5` — la cima è metà della base (un tronco di prisma).
- `taper: 1.5` — la cima si allarga del 50% rispetto alla base
  (piramide rovesciata, utile per gonne svasate e cappelli da
  fungo).

### Combinare i due

Twist + taper insieme producono l'intera famiglia delle **colonne
architettoniche** (salomoniche, gaudiane, art déco), delle
**balaustre decorative**, delle **maniglie torniche**, delle
**torri di gelato soft-serve** e dei **finiali classici** — tutto da
una singola primitiva, senza bisogno di stack di modificatori.

```yaml
# Colonna salomonica: base quadrata, elica 180° lungo l'altezza, si stringe del 20% in cima
- name: "salomonica"
  type: "extrusion"
  profile_type: "linear"
  height: 4.0
  twist_degrees: 180
  taper: 0.80
  caps: "both"
  material: "marmo"
  profile:
    - [ 0.5,  0.5]
    - [ 0.5, -0.5]
    - [-0.5, -0.5]
    - [-0.5,  0.5]
```

---

## 12.7 Una Scena Completa: Showcase delle Tre Modalità

Il repository include una scena di riferimento in
`scenes/showcases/extrusion-showcase.yaml` che mette tutte e tre le
modalità sullo stesso palco. Ecco una versione distillata da
incollare in un nuovo file:

```yaml
camera:
  position: [0, 2.0, -5.6]
  look_at: [0, 1.4, 0]
  fov: 42

world:
  sky:
    type: "gradient"
    zenith_color: [0.06, 0.08, 0.14]
    horizon_color: [0.20, 0.18, 0.22]
  ground:
    type: "plane"
    material: "pavimento"
    y: 0

materials:
  - id: "pavimento"
    type: "disney"
    texture:
      type: "checker"
      colors: [[0.92, 0.90, 0.85], [0.10, 0.10, 0.12]]
      scale: 1.5
    roughness: 0.18
  - id: "oro"
    type: "disney"
    color: [1.00, 0.78, 0.34]
    metallic: 1.0
    roughness: 0.22
  - id: "marmo"
    type: "disney"
    color: [0.94, 0.93, 0.90]
    roughness: 0.15
    clearcoat: 0.8
  - id: "smeraldo"
    type: "disney"
    color: [0.22, 0.78, 0.42]
    roughness: 0.05
    spec_trans: 1.0
    ior: 1.55

lights:
  - type: "directional"
    direction: [0.45, -0.7, 0.55]
    color: [1.0, 0.92, 0.78]
    intensity: 2.6
    angular_radius_deg: 0.55
  - type: "point"
    position: [-4.5, 3.5, -3.0]
    color: [1.0, 0.74, 0.50]
    intensity: 22

entities:
  # Hero 1 — stella d'oro (linear concavo)
  - name: "stella"
    type: "extrusion"
    profile_type: "linear"
    height: 0.18
    caps: "both"
    material: "oro"
    rotate: [90, 0, 0]
    translate: [-2.6, 1.55, 0]
    profile:
      - [ 0.55,  0.00]
      - [ 0.18,  0.13]
      - [ 0.17,  0.52]
      - [-0.07,  0.21]
      - [-0.44,  0.32]
      - [-0.22,  0.00]
      - [-0.44, -0.32]
      - [-0.07, -0.21]
      - [ 0.17, -0.52]
      - [ 0.18, -0.13]

  # Hero 2 — colonna marmorea scanalata twistata (catmull_rom + twist + taper)
  - name: "colonna"
    type: "extrusion"
    profile_type: "catmull_rom"
    height: 3.4
    twist_degrees: 60
    taper: 0.88
    curve_samples: 24
    caps: "both"
    material: "marmo"
    translate: [0, 0, 0]
    profile:
      - [ 0.55,  0.000]
      - [ 0.000, 0.55]
      - [-0.55,  0.000]
      - [ 0.000,-0.55]

  # Hero 3 — scudo di smeraldo Bezier
  - name: "scudo"
    type: "extrusion"
    profile_type: "bezier"
    height: 0.25
    caps: "both"
    material: "smeraldo"
    rotate: [90, 0, 0]
    translate: [2.6, 1.55, 0]
    profile:
      - [ 0.6,  0.0]
      - [ 0.0,  0.7]
      - [-0.6,  0.0]
      - [ 0.0, -0.5]
    profile_bezier_controls:
      - [ 0.60,  0.00]
      - [ 0.60,  0.42]
      - [ 0.36,  0.70]
      - [ 0.00,  0.70]
      - [ 0.00,  0.70]
      - [-0.36,  0.70]
      - [-0.60,  0.42]
      - [-0.60,  0.00]
      - [-0.60,  0.00]
      - [-0.60, -0.30]
      - [-0.32, -0.50]
      - [ 0.00, -0.50]
      - [ 0.00, -0.50]
      - [ 0.32, -0.50]
      - [ 0.60, -0.30]
      - [ 0.60,  0.00]
```

Renderizza:

```
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/extrusion-showcase.yaml -o renders/extrusion.png \
  -w 1280 -H 720 -s 256 -d 8
```

Dovresti vedere: una stella d'oro sfaccettata a sinistra (ogni
rientranza concava netta, nessun foro nel tappo), una colonna di
marmo scanalata e twistata al centro (la silhouette segue
visibilmente la spirale e si restringe verso l'alto) e uno scudo di
vetro smeraldo a destra (silhouette Bezier arrotondata, rifrazione
che colora ciò che sta dietro).

---

## 12.8 UV Mapping e Texture

Le extrusion emettono coordinate UV che combaciano con il modo in cui
un designer disporrebbe una texture wraparound su un vero oggetto
prismatico:

- **U** è la **lunghezza d'arco lungo il profilo**, normalizzata in
  `[0, 1]`. Una texture avvolta sulle pareti laterali si tassella
  senza cuciture: `u = 0` è il primo vertice del profilo, `u = 1` è
  di nuovo lo stesso vertice dopo un giro completo del loop.
- **V** è l'**altezza lungo l'asse di estrusione**, normalizzata in
  `[0, 1]`. `v = 0` è il tappo inferiore, `v = 1` è il tappo
  superiore.
- **I tappi** usano UV baricentrici dalla triangolazione — vanno
  bene per un colore uniforme o un pattern piatto, meno ideali per
  un logo registrato con precisione. Se vuoi un logo sul tappo,
  proiettalo con un `Quad` posizionato leggermente sopra la
  superficie del tappo.

Due conseguenze pratiche:

- Una texture procedurale **noise** o **marble** si avvolge senza
  alcuna cucitura visibile, indipendentemente dal numero di vertici
  del profilo.
- Una texture **wood** a strisce su una colonna scorre parallela
  all'asse di estrusione, con densità delle strisce inversamente
  proporzionale alla larghezza locale del profilo — esattamente il
  modo in cui la venatura si legge su un vero pilastro di legno.

---

## 12.9 Le Extrusion come Sorgenti di Luce

Se il materiale assegnato a una extrusion è `emissive`, la primitiva
entra automaticamente nel pool della **next-event estimation (NEE)**
— lo stesso meccanismo usato dal lathe e dalle area light. Non
serve nessuna configurazione extra.

Internamente il sampler percorre una **CDF pesata per area** su
tutte le pareti e i triangoli dei tappi, per cui ogni punto sulla
extrusion è scelto con densità proporzionale al suo contributo alla
potenza emessa totale. Il risultato è **illuminazione diretta priva
di rumore** da forme emissive concave:

- Un'**insegna al neon a forma di stella** che brilla contro un
  muro di mattoni.
- Una **lettera "A"** che brilla come parte di un logo 3D.
- Un pannello emissivo a forma di **ingranaggio** dentro una sala di
  controllo steampunk.
- Un'**insegna a forma di croce** sopra una porta di ospedale.

Tutte queste sarebbero faticose da scrivere come area light
classiche (rettangoli sovrapposti, poi composti) e banali come una
singola extrusion emissiva.

---

## 12.10 Come Viene Costruita la Geometria, Internamente

Non serve capire questa sezione per usare la feature, ma il cost
model che spiega ti aiuterà a tenere le scene veloci.

Quando crei una extrusion, il loader:

1. **Tassella il profilo** in una polilinea 2D fine:
   - Per `linear` usa i tuoi punti così come sono.
   - Per `catmull_rom` e `bezier` campiona ogni segmento di input
     in `curve_samples` piccoli pezzi rettilinei.
2. **Auto-corregge l'orientamento** in modo che il loop sia
   antiorario (interno a sinistra di ogni edge). Questo garantisce
   che le normali esterne delle pareti puntino fuori dall'interno.
3. **Costruisce le pareti laterali** come una striscia di triangoli
   tra un anello inferiore (in `y = 0`) e un anello superiore (in
   `y = height`). Per i profili curvi il motore emette triangoli
   **smooth-shaded**, in modo che triangoli adiacenti condividano
   normali medie sull'edge comune e la silhouette appaia come una
   curva continua a qualsiasi zoom. Per `linear` emette triangoli
   **flat-shaded** per tenere i ridge netti.
4. **Triangola i tappi** con il classico algoritmo
   **ear-clipping**. Ogni tappo da `n` vertici diventa `n - 2`
   triangoli. I poligoni concavi-ma-semplici sono gestiti
   correttamente senza decomposizione manuale.
5. **Avvolge ogni triangolo in una BVH interna** — esattamente come
   una mesh OBJ. La BVH di scena esterna vede l'intera extrusion
   come una singola foglia con un AABB unico, e la BVH interna
   gestisce la traversata veloce delle centinaia o migliaia di
   triangoli che un profilo complesso può produrre.

**Cost model**

- Un profilo `linear` di N vertici produce `2N` triangoli di parete
  più al massimo `2 × (N - 2)` triangoli di tappo. Una stella a 5
  punte (10 vertici) sono ~36 triangoli totali — paragonabile a un
  box low-poly.
- Un profilo `catmull_rom` o `bezier` di N vertici con
  `curve_samples = 16` produce ~`32N` triangoli di parete più
  `2 × (16N - 2)` triangoli di tappo. Una colonna scanalata da 16
  vertici diventa ~1100 triangoli — comunque una singola foglia
  BVH per la scena esterna, e la traversata interna resta O(log).

In pratica: usa pure le extrusion liberamente anche per primi piani
da hero shot. L'unico knob che vale la pena toccare quando ne hai
molte è `curve_samples` — abbassalo a 8 per oggetti distanti,
alzalo a 24-32 per i soggetti principali.

---

## 12.11 Scegliere la Modalità e i Modificatori Giusti

| Cosa vuoi                                                          | Usa                                  |
|--------------------------------------------------------------------|--------------------------------------|
| Profilato industriale (trave a I, canale a U, angolare a L)        | `linear`                             |
| Stelle, croci, segni più, loghi                                    | `linear` (i concavi vanno bene)      |
| Ingranaggi, ruote dentate, denti d'arresto                         | `linear`                             |
| Tipografia 3D (lettere come logo)                                  | `linear`                             |
| Colonne scolpite, balaustre, modanature decorative                 | `catmull_rom` + `twist` + `taper`    |
| Sezioni organiche morbide (foglie, fagioli)                        | `catmull_rom`                        |
| Riprodurre un path SVG/Illustrator o una curva CAD                 | `bezier`                             |
| Cuore, lacrima, cuspidi/tangenti spezzate intenzionali             | `bezier`                             |
| Forme da tagliabiscotti / stencil                                  | `linear`, `caps: "none"`             |
| Punta da trapano elicoidale                                        | `linear`, `twist_degrees: 360+`      |
| Piramidi, obelischi, finiali                                       | qualsiasi modo, `taper: 0.0`–`0.3`   |

Due regole pratiche nel dubbio:

1. Se la sezione ha **angoli netti che devono restare spigolosi**,
   usa `linear`.
2. Se descriveresti il profilo dicendo "disegna una curva liscia
   passante per questi punti", usa `catmull_rom`. Ricorri a
   `bezier` solo quando ti serve controllo esplicito sulle
   tangenti o stai importando una curva esistente da un altro
   tool.

---

## 12.12 Risoluzione dei Problemi

**Il mio profilo concavo si renderizza con un buco nel tappo.**
Verifica due volte che il poligono sia **semplice** (nessuna
auto-intersezione). Il triangolatore ear-clipping gestisce qualsiasi
poligono concavo finché i suoi edge non si incrociano. Se un edge a
metà loop ne attraversa un altro, il tappo avrà buchi spuri.
Visualizza prima il profilo in 2D — uno strumento come GeoGebra o
anche un veloce plot in `matplotlib` rende il problema evidente.

**La silhouette è seghettata anche se ho usato `catmull_rom`.**
Alza `curve_samples` dal default 16 a 24 o 32. Il default produce
~5° di arco per campione su una colonna tipica, che appare liscio
nei piani medi ma si legge come sfaccettato nei primi piani stretti.

**La mia colonna twistata mostra sfaccettature visibili che si
avvolgono.**
Stesso rimedio: alza `curve_samples`. Il twist rende visibile la
triangolazione delle pareti perché ogni anello di triangoli è
ruotato rispetto al successivo. Una colonna a `twist_degrees: 360`
con `curve_samples: 8` apparirà chiaramente sfaccettata; la stessa
colonna con `curve_samples: 24` apparirà liscia.

**Il mio scudo Bezier ha una piega su un confine di segmento.**
Hai rotto la continuità C¹ tra due segmenti adiacenti. O allinei
le tangenti come descritto in 12.4, o accetti la piega come
caratteristica di design intenzionale (perfetta per punte di scudo
e faccette di gemma).

**La mia extrusion emissiva non illumina la scena tanto quanto
mi aspettavo.**
La potenza emessa è proporzionale all'**area di superficie totale**
dell'extrusion (pareti + tappi). Una extrusion sottile o stretta
ha poca area. Allarga il profilo, aumenta `height` o alza il valore
di `emission` sul materiale.

**Il tappo inferiore manca su un oggetto alto e sottile.**
Assicurati che `caps` sia `both` (default) o `start`. Controlla i
warning del loader stampati dopo "Loading scene... done" per vedere
se il profilo è stato rifiutato per essere degenere (punti
collineari, area quasi nulla) — in quel caso il triangolatore
abbandona graziosamente e vengono emesse solo le pareti.

**Il primo triangolo del tappo appare fuori posto quando uso una
rotazione.**
Ricorda che l'ordine delle trasformazioni è
`scale → rotate → translate` attorno all'origine **globale**. Se usi
`center:` insieme a `rotate:` ruoti attorno all'origine del mondo
invece che al centro dell'extrusion. O ometti `center:` e usi
`translate:` per il posizionamento, o avvolgi l'extrusion in un
`group` la cui trasformazione ti dà il pivot locale che vuoi. (Vedi
capitolo 5 per la discussione completa sull'ordine delle
trasformazioni.)

---

## Cosa Hai Imparato

- `extrusion` (alias `prism` o `linear_extrude`) estrude un profilo
  2D chiuso nel piano XZ lungo l'asse +Y locale, producendo un
  prisma con pareti laterali e tappi opzionali alle estremità.
- Tre modalità di profilo speculari al lathe: `linear` per ridge
  netti e forme industriali / araldiche sfaccettate, `catmull_rom`
  per silhouette lisce scritte per punti, `bezier` per controllo
  totale su direzione e modulo delle tangenti.
- **I profili concavi funzionano** out-of-the-box grazie ai tappi
  ear-clipped — stelle, ingranaggi, lettere, forme a L sono tutti
  job da una singola primitiva.
- `caps: both | start | end | none` sceglie quali estremità
  chiudere, abilitando prismi pieni, vasche, cappelli e tubi puri.
- `twist_degrees` e `taper` trasformano prismi dritti in colonne
  scolpite, punte da trapano elicoidali, finiali e piramidi —
  senza bisogno di stack di modificatori o catene CSG.
- Le UV sono `(arc length, height)`, perfette per texture
  wraparound che corrono lungo l'asse di estrusione con densità di
  strisce legata alla larghezza locale del profilo.
- Le extrusion emissive entrano automaticamente nel pool NEE con
  campionamento pesato per area — stelle al neon, lettere
  luminose e pannelli a forma di stella "funzionano e basta" come
  sorgenti di luce.
- Internamente ogni extrusion è una lista di triangoli dentro la
  propria BVH, quindi la BVH di scena esterna vede una sola foglia
  per extrusion indipendentemente dalla complessità del profilo.

---

[Precedente: Superfici di Rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md) | [Indice del Tutorial](./README.md)
