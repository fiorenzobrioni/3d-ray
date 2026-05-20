# Capitolo 11: Superfici di Rivoluzione (Lathe)

Tutte le primitive incontrate finora si definiscono con una manciata
di numeri: un centro e un raggio, due vertici, magari un top radius
per il cono. È abbastanza per biliardi, architettura e pezzi degli
scacchi -- ma cede nel momento in cui si vuole renderizzare un
**vaso ceramico**, un **calice da vino**, una **colonna tornita** o
un **alfiere scacchistico dal corpo curvo**. Sono oggetti con un
profilo che varia con continuità lungo l'asse, e nessuna primitiva
a parametri fissi può descriverli.

La risposta classica è la **superficie di rivoluzione**: si prende
un profilo 2D disegnato nel piano `(r, y)` e lo si fa ruotare di
360° attorno all'asse Y. Il risultato è una superficie analitica
che può essere semplice come un cilindro o elaborata come un vaso
Ming -- tutto a partire da un breve elenco di punti.

3D-Ray la espone come primitiva `lathe`. A differenza di una mesh
tassellata, è una **superficie implicita** -- il ray tracer
interseca direttamente l'equazione matematica, senza poligoni di
mezzo. La silhouette rimane liscia a ogni zoom, e il file che si
scrive si misura in righe, non in megabyte.

Questo capitolo descrive le tre modalità di profilo (`linear`,
`catmull_rom`, `bezier`), le regole di design che tengono i profili
ben formati e il cost model necessario a scegliere la modalità
giusta per ogni oggetto.

---

## 11.1 Modello Mentale: il Profilo in 2D

Un lathe è definito da una **curva di profilo 2D** nel semipiano
destro `r >= 0, y ∈ ℝ`. Ogni punto del profilo è una coppia `[r, y]`:

- `r` è la distanza dall'asse Y (il raggio a quell'altezza)
- `y` è l'altezza lungo l'asse

Quando il renderer elabora il lathe, ruota matematicamente questa
curva attorno all'asse Y:

```
punto del profilo (r, y)  →  cerchio { (r·cos θ, y, r·sin θ)  |  θ ∈ [0, 2π) }
```

L'intera superficie 3D è l'unione di questi cerchi per ogni punto
del profilo.

**Regole del profilo** (il loader le fa rispettare e avvisa sulle
violazioni):

1. **`r >= 0`** -- raggi negativi non hanno senso (piegherebbero il
   profilo attraverso l'asse).
2. **`y` monotonicamente non decrescente** -- il profilo deve
   salire (o restare piatto) man mano che si elencano i punti. Un
   profilo che sale e poi scende produrrebbe una superficie
   auto-intersecante. Se i punti sono fuori ordine il loader li
   ordina per `y` ed emette un warning.
3. **Almeno 2 punti** -- un singolo punto non è un profilo.
4. **`r = 0` chiude quell'estremo** -- quando il primo o l'ultimo
   punto ha `r = 0`, il profilo *tocca l'asse* e il cap
   corrispondente è implicito (la superficie è già chiusa). Quando
   invece `r > 0` a un estremo, viene aggiunto automaticamente un
   disco di cap piatto.

Una volta interiorizzata questa immagine -- una curva nel piano
meridiano, fatta girare attorno all'asse Y -- le tre modalità di
profilo sono solo modi diversi di disegnare quella curva.

---

## 11.2 Profilo Linear -- La Colonna Tornita

La modalità `linear` tratta il profilo come una **polilinea**: i
punti consecutivi sono uniti da segmenti rettilinei, e ogni segmento
diventa un **tronco di cono** (frustum) dopo la rivoluzione.

```yaml
- name: "colonna"
  type: "lathe"
  profile_type: "linear"                # default -- può essere omesso
  material: "marmo"
  translate: [0, 0, 0]
  profile:
    - [0.30, 0.0]                       # base
    - [0.30, 0.1]                       # plinto
    - [0.25, 0.2]                       # collo della base
    - [0.28, 2.0]                       # fusto
    - [0.35, 2.1]                       # capitello
```

| Parametro       | Default  | Descrizione                                  |
|-----------------|----------|----------------------------------------------|
| `profile_type`  | `linear` | Modalità di interpolazione                   |
| `profile`       | --       | Lista di punti `[r, y]`, minimo 2            |
| `material`      | --       | Applicato uniformemente a tutta la superficie |
| `center` / `translate` / `rotate` / `scale` | -- | Trasformazioni standard |

**Quando usarla**

- Gambe di mobili torniti, balaustre, candelabri -- qualsiasi cosa
  in cui una transizione sfaccettata visibile *è* il look voluto.
- Profili con molti punti in cui ogni segmento deve restare
  geometricamente distinto.
- Massima velocità: l'intersezione raggio-frustum è un'equazione
  quadratica -- lo stesso costo di un cone.

**Come appare**

A ogni punto del profilo la normale cambia bruscamente: la luce
rimbalza diversamente sui due lati del vertice. È esattamente quello
che produce uno strumento da tornio reale quando si ferma a uno
spigolo. Per forme lisce tipo vaso, si usano le modalità spline
descritte sotto.

---

## 11.3 Profilo Catmull-Rom -- Il Vaso Ceramico

`catmull_rom` è la modalità da scegliere quando il profilo deve
avere un aspetto **liscio** ma si vuole comunque specificarlo
elencando i punti per cui la superficie deve passare. Usa
l'interpolazione **Catmull-Rom centripeta** (Yuksel et al. 2011),
con due proprietà importanti:

1. **Passa per ogni punto di controllo** -- ciò che si digita è ciò
   che si ottiene, vertice dopo vertice.
2. È **C¹ continua e priva di auto-intersezioni** anche quando due
   punti sono molto vicini -- la parametrizzazione centripeta
   elimina il classico "overshoot" della Catmull-Rom uniforme.

```yaml
- name: "vaso"
  type: "lathe"
  profile_type: "catmull_rom"           # alias: "catmull", "smooth"
  material: "porcellana"
  profile:
    - [0.00, 0.00]                      # base chiusa (sull'asse)
    - [0.30, 0.00]
    - [0.35, 0.10]
    - [0.55, 0.40]                      # pancia del vaso
    - [0.40, 0.80]                      # inizio del collo
    - [0.50, 0.95]                      # labbro svasato
    - [0.00, 0.95]                      # apertura chiusa (sull'asse)
```

Il loader converte ogni coppia di punti consecutivi in un **segmento
Bezier cubico**, calcolando i control point interni con la regola
della tangente centripeta. Internamente, le modalità Catmull-Rom e
Bezier condividono la stessa implementazione di segmento -- cambia
solo il setup.

**Quando usarla**

- Ceramiche, oggetti in vetro, ciotole di metallo, pinnacoli, urne.
- Profili in cui si preferisce descrivere la forma come "ecco i
  punti della silhouette" anziché "ecco le maniglie di controllo".
- Forme organiche, modellate a mano, dove mettere a punto le
  tangenti manualmente sarebbe scomodo.

**Vincoli**

- Servono almeno **4 punti** per definire le tangenti interne. Se
  se ne forniscono 2 o 3, il loader degrada silenziosamente la
  forma a `linear` e avvisa.
- Gli endpoint fantasma vengono riflessi attraverso il primo e
  l'ultimo vertice, così il comportamento delle tangenti agli
  estremi è naturale (la curva esce dall'endpoint nella direzione
  del segmento adiacente).

---

## 11.4 Profilo Bezier -- Controllo Totale

Quando si ha già una curva Bezier scritta a mano -- da un editor
vettoriale, un export CAD o una formula pubblicata -- si usa la
modalità `bezier` e si forniscono esplicitamente i quattro control
point cubici per ogni segmento.

```yaml
- name: "ciotola"
  type: "lathe"
  profile_type: "bezier"
  material: "ceramica_smaltata"
  profile:                              # estremi di ogni segmento (N punti = N-1 segmenti)
    - [0.0, 0.0]
    - [0.5, 0.3]
    - [0.5, 0.6]
  profile_bezier_controls:              # esattamente 4 × (N - 1) punti, concatenati
    - [0.0, 0.0]                        # segmento 1, P0
    - [0.3, 0.0]                        #             P1
    - [0.5, 0.1]                        #             P2
    - [0.5, 0.3]                        #             P3  (== segmento 2, P0)
    - [0.5, 0.3]                        # segmento 2, P0
    - [0.5, 0.45]                       #             P1
    - [0.5, 0.5]                        #             P2
    - [0.5, 0.6]                        #             P3
```

**Regole**

- `profile` elenca l'**endpoint di ciascun segmento** -- N punti
  definiscono N-1 segmenti.
- `profile_bezier_controls` elenca **quattro control point per
  segmento**, concatenati. Per N endpoint si hanno quindi
  `4 × (N - 1)` voci. Un numero non coerente fa sì che il loader
  scarti l'entità e sostituisca il materiale con un Lambertian
  grigio, così l'errore è immediatamente visibile.
- Per ottenere **continuità C¹** tra i segmenti occorre che
  `P3(segmento k) == P0(segmento k+1)` *e*
  `P3(k) - P2(k) == P1(k+1) - P0(k+1)` (la tangente uscente
  eguaglia quella entrante per modulo e direzione). Rompere questa
  regola produce uno spigolo netto volutamente -- utile per labbri
  marcati, modanature e profili a spalla.

**Quando usarla**

- Si ha già un dataset Bezier (SVG, Illustrator, uno strumento
  CAD).
- Si vogliono cuspidi deliberate o tangenti rotte che la
  Catmull-Rom appianerebbe.
- Si riproduce una forma pubblicata definita in termini di control
  point Bezier (tipografia, tassonomie classiche di vasi, ecc.).

---

## 11.5 Cap e Profili Chiusi

Ogni lathe ha al massimo due **dischi di cap** -- cerchi piatti che
chiudono sopra e sotto:

- Il cap inferiore è aggiunto a `y = y_primo` quando `r_primo > 0`.
- Il cap superiore è aggiunto a `y = y_ultimo` quando
  `r_ultimo > 0`.
- Quando `r = 0` a un estremo, il profilo già tocca l'asse, la
  superficie è geometricamente chiusa e nessun cap viene disegnato.

Ciò offre due idiomi per le forme chiuse:

```yaml
# Chiusa tramite cap (fondo piatto, top piatto)
profile:
  - [0.5, 0.0]
  - [0.6, 0.5]
  - [0.5, 1.0]

# Chiusa tramite l'asse (arrotondata come un limone)
profile:
  - [0.00, 0.0]
  - [0.35, 0.2]
  - [0.50, 0.5]
  - [0.35, 0.8]
  - [0.00, 1.0]
```

I due profili sopra producono solidi molto diversi: il primo ha due
facce discoidali visibili, il secondo nessuna.

---

## 11.6 Una Scena Completa: Vetrina delle Tre Modalità

La repo include una scena di riferimento in
`scenes/showcases/primitive-lathe.yaml` che mette le tre modalità
sullo stesso palcoscenico. Ecco una versione distillata da copiare
in un nuovo file:

```yaml
camera:
  position: [0, 3.5, -9]
  look_at: [0, 1.2, 0]
  fov: 42

world:
  sky:
    type: "gradient"
    zenith_color: [0.04, 0.05, 0.10]
    horizon_color: [0.12, 0.12, 0.18]

materials:
  - id: "pavimento"
    type: "disney"
    roughness: 0.85
    texture:
      type: "checker"
      colors: [[0.10, 0.10, 0.11], [0.22, 0.22, 0.23]]
      scale: 0.7
  - id: "porcellana"
    type: "disney"
    color: [0.94, 0.92, 0.88]
    roughness: 0.25
    specular: 0.6
  - id: "marmo"
    type: "disney"
    color: [0.88, 0.85, 0.80]
    roughness: 0.4
  - id: "luce"
    type: "emissive"
    emission: [6.0, 4.0, 1.8]

entities:
  # Vaso liscio -- Catmull-Rom
  - name: "vaso"
    type: "lathe"
    profile_type: "catmull_rom"
    material: "porcellana"
    translate: [-2.2, 0, 0]
    profile:
      - [0.00, 0.00]
      - [0.30, 0.00]
      - [0.35, 0.10]
      - [0.55, 0.40]
      - [0.40, 0.80]
      - [0.50, 0.95]
      - [0.00, 0.95]

  # Colonna sfaccettata -- Linear
  - name: "colonna"
    type: "lathe"
    profile_type: "linear"
    material: "marmo"
    profile:
      - [0.30, 0.0]
      - [0.30, 0.1]
      - [0.25, 0.2]
      - [0.28, 2.0]
      - [0.35, 2.1]

  # Ciotola emissiva -- Bezier (illumina la scena via NEE)
  - name: "ciotola"
    type: "lathe"
    profile_type: "bezier"
    material: "luce"
    translate: [2.2, 0.5, 0]
    profile:
      - [0.0, 0.0]
      - [0.5, 0.3]
      - [0.5, 0.6]
    profile_bezier_controls:
      - [0.0, 0.0]
      - [0.3, 0.0]
      - [0.5, 0.1]
      - [0.5, 0.3]
      - [0.5, 0.3]
      - [0.5, 0.45]
      - [0.5, 0.5]
      - [0.5, 0.6]
```

Renderizzala con:

```
dotnet run --project src/RayTracer -c Release -- \
  -i scenes/showcases/primitive-lathe.yaml -o renders/lathe.png \
  -w 1280 -H 720 -s 256 -d 8
```

Dovresti vedere: un vaso ceramico liscio a sinistra (nessuno
sfaccettamento a qualsiasi zoom), una colonna di marmo sfaccettata
al centro (ogni vertice del profilo produce un anello di
discontinuità nell'ombreggiatura) e una ciotola Bezier che brilla a
destra e illumina il pavimento senza bisogno di luci aggiuntive --
il lathe stesso è la sorgente luminosa.

---

## 11.7 Mappatura UV e Texture

I lathe seguono le stesse convenzioni UV di `cylinder` e `cone`,
quindi i materiali progettati per quelle primitive si trasferiscono
senza modifiche:

- **U** è l'**angolo azimutale** attorno all'asse Y, incapsulato in
  `[0, 1)` -- tileabile in modo seamless.
- **V** è la **lunghezza d'arco cumulativa** lungo il profilo,
  normalizzata in `[0, 1]`. Per un profilo lineare è piecewise
  lineare in `y`. Per uno spline la lunghezza d'arco è precalcolata
  con quadratura di Gauss-Legendre a 8 punti per segmento, che
  raggiunge errore relativo sotto `1e-6` per qualsiasi cubica.

In pratica: una texture checker su un vaso si avvolge in modo
uniforme attorno alla pancia, e una texture a strisce di legno su
una colonna corre lungo l'altezza con le strisce più ravvicinate
dove il raggio si riduce (fedele a come un nastro seguirebbe la
superficie).

---

## 11.8 Lathe come Sorgenti Luminose

Se il materiale assegnato a un lathe è `emissive`, la primitiva
entra automaticamente nel pool di **next-event estimation (NEE)**
-- lo stesso meccanismo usato dalle luci area e sphere. Nessuna
configurazione aggiuntiva serve.

Internamente il sampler percorre una CDF pesata per area
attraverso tutti i segmenti e i cap, così ogni punto della
superficie del lathe viene scelto con densità proporzionale al suo
contributo all'energia emessa totale. Il risultato è
un'**illuminazione diretta senza rumore** da parte di oggetti
curvi ed emissivi: insegne al neon, ceramiche luminose, gusci di
lampade fluorescenti. Il Capitolo 6 approfondisce l'interazione tra
NEE e gli altri tipi di luce.

---

## 11.9 Come Funziona l'Intersezione (e Perché Importa)

Non è necessario capire questa sezione per usare la feature, ma il
cost model che illustra aiuta a dimensionare le scene.

Per un profilo **linear** ogni segmento è un frustum, e
l'intersezione raggio-frustum si riduce a un'**equazione
quadratica** -- identica al `cone`. Economica, esatta, ed è in
computer graphics dagli anni '70.

Per un profilo **spline** (`catmull_rom` o `bezier`) ogni segmento
è una curva cubica nel piano meridiano. Facendo ruotare una cubica
attorno a Y e intersecandola con una retta si ottiene un
**polinomio di grado 6** nel parametro `u` della curva. Non esiste
una soluzione in forma chiusa oltre il grado 4, perciò 3D-Ray
risolve numericamente con l'approccio della `lathe` di PovRay e
della `Curve` di PBRT (usata per i capelli):

1. **Costruire una Sturm chain** dal polinomio di grado 6 e dalle
   sue derivate.
2. **Isolare ogni radice reale** in `[0, 1]` contando i cambi di
   segno nella chain (il teorema di Sturm ne garantisce il
   conteggio).
3. **Raffinare** ogni radice isolata con **Newton-Raphson**, con
   fallback a bisezione se Newton esce dal bracket.
4. **Ricostruire** `t`, il parametro del raggio, dall'`u` accettato.

Il solver è implementato in `Core/SturmSolver.cs` come root finder
polinomiale general-purpose, riutilizzabile per altre superfici
implicite.

**Cost model**

- Segmento `linear`: circa lo stesso di un `cone` -- quadratica
  comparabile.
- Segmento spline: all'incirca **10× un hit di cone**. Ancora veloce
  in termini assoluti (poche centinaia di nanosecondi), ma si
  somma tra tanti campioni per pixel.

Un AABB per-segmento viene precalcolato dai control point più gli
zeri delle derivate di `Y(u)` e `R(u)`, così i segmenti fuori dal
raggio corrente sono scartati quasi gratis -- il costo 10× si paga
solo per i segmenti effettivamente colpiti.

---

## 11.10 Scegliere la Modalità Giusta

| Obiettivo                                         | Usa             |
|---------------------------------------------------|-----------------|
| Massima velocità, look sfaccettato accettabile    | `linear`        |
| Silhouette liscia, autore per punti silhouette    | `catmull_rom`   |
| Silhouette liscia + spigoli deliberati            | `bezier`        |
| Convertire un percorso SVG/Illustrator            | `bezier`        |
| Controllo fine delle tangenti                     | `bezier`        |
| Riprodurre una gamba di sedia tornita, torre scacchi, balaustra | `linear` |
| Riprodurre un vaso ceramico, un calice, un boccale | `catmull_rom`  |

Due regole pratiche in caso di dubbio:

1. Se l'oggetto si modellerebbe su un **tornio fisico** (una
   passata con uno strumento rettilineo), usa `linear`.
2. Se lo si modellerebbe **gettando argilla su un tornio da
   vasaio** e stirando la forma con continuità, usa `catmull_rom`.

---

## 11.11 Troubleshooting

**La silhouette è frastagliata anche con `catmull_rom`.**
Controlla il numero di punti del profilo -- Catmull-Rom ne richiede
almeno 4. Con 2 o 3 il loader passa silenziosamente a `linear`.
Leggi i warning del loader stampati dopo "Loading scene... done"
per conferma.

**Il profilo si auto-interseca o si renderizza al rovescio.**
I valori di `y` non monotoni vengono ordinati dal loader, il che
può rendere le forme "a ritorno" del tutto diverse da quelle
attese. I profili lathe devono essere **funzioni a singolo valore
di y** nel piano `(r, y)`. Se serve davvero una silhouette
auto-intersecante (un toroide, un otto), modellala con CSG invece.

**Una ciotola Bezier mostra una piega visibile al confine tra
segmenti.** I segmenti adiacenti rompono la continuità delle
tangenti. Per una transizione liscia, allinea gli ultimi due
control del segmento *k* con i primi due del segmento *k+1* come
descritto in 11.4. Per uno spigolo deliberato, lasciali
disallineati.

**Il mio lathe emissivo non emette luce apprezzabile.** La potenza
emessa totale scala con la **superficie**, non con l'aspetto del
profilo. Un lathe alto e sottile con `r` vicino a zero ha poca
area -- aumenta `emission` o allarga il profilo.

**I render sono 5-10× più lenti del previsto.** I segmenti spline
costano ~10× un hit di cone. Se il profilo ha molti segmenti o si
usano molti lathe spline, valuta di (a) passare a `linear` per gli
oggetti piccoli in inquadratura, (b) ridurre il numero di punti
del profilo (oltre i 10-12 punti si percepisce raramente), (c)
usare la qualità Preview mentre si itera sulla composizione.

---

## Cosa hai imparato

- I lathe fanno ruotare un profilo 2D `(r, y)` attorno all'asse Y
  per produrre una superficie di rivoluzione analitica, senza
  tassellazione.
- Tre modalità coprono l'intero spazio di progetto: `linear`
  (sfaccettata, veloce), `catmull_rom` (liscia, passa per ogni
  punto), `bezier` (control handle espliciti).
- I cap vengono aggiunti automaticamente quando il profilo non
  tocca l'asse agli estremi; `r = 0` chiude matematicamente
  quell'estremo.
- UV segue le convenzioni di cylinder/cone: U è l'azimut, V è la
  lunghezza d'arco cumulativa normalizzata -- le texture si
  avvolgono naturalmente.
- I lathe emissivi entrano automaticamente nel pool NEE e vengono
  campionati con densità pesata per area.
- Le intersezioni spline sono risolte con un ibrido Sturm chain +
  Newton (~10× il costo di un hit di cone). Linear è quadratica,
  allo stesso costo di un cone.
- Si sceglie la modalità in base a come si *pensa* l'oggetto:
  lavorato a macchina (linear) contro scolpito (spline).

---

[Precedente: Librerie di asset e scene complete](./10-libraries-and-projects.md) | [Indice del Tutorial](./README.md) | [Successivo: Profili 2D estrusi (Extrusion)](./12-extrusion-2d-profiles.md)
