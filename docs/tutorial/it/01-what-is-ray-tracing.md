# Capitolo 1: Che cos'è il ray tracing?

Prima di scrivere una singola riga di descrizione della scena, è utile capire
*cosa fa il motore* quando trasforma il file YAML in un'immagine.
Questo capitolo introduce le idee fondamentali -- nessun codice ancora, solo il
modello mentale su cui si farà affidamento per il resto del tutorial.

---

## 1.1 Luce, superfici e fotocamera

Nel mondo fisico, una sorgente luminosa emette fotoni. Questi fotoni viaggiano
in linea retta finché non colpiscono una superficie (o si disperdono in un mezzo 
volumetrico come la nebbia), dove possono essere assorbiti, riflessi, rifratti o re-emessi. 
Una piccola frazione di essi entra alla fine in una fotocamera (o nell'occhio), 
e il pattern che formano diventa un'immagine.

Simulare questo processo in avanti -- dalla sorgente luminosa alla fotocamera --
è estremamente inefficiente. La maggior parte dei fotoni non raggiunge mai la
fotocamera. Il ray tracing inverte il processo: lancia raggi **dalla fotocamera
nella scena** e ne traccia il percorso a ritroso verso la luce. Questo si chiama
*backward ray tracing*, ed è la base di praticamente ogni renderer fotorealistico,
incluso 3D-Ray.

La fotocamera si trova in un punto dello spazio, puntata verso un bersaglio. Per
ogni pixel dell'immagine di output, il motore lancia uno o più raggi attraverso
quel pixel nella scena. Ogni raggio colpisce un oggetto oppure si perde nel cielo.

---

## 1.2 Cosa succede quando un raggio colpisce una superficie

Quando un raggio colpisce una superficie, il risultato dipende dal **materiale**:

- Le superfici **diffuse (Lambertian)** disperdono il raggio entrante in una direzione
  casuale pesata dalla normale alla superficie. Si pensi al gesso, all'intonaco o
  alla vernice opaca.

- Le superfici **metalliche** riflettono il raggio secondo le leggi della riflessione,
  con una certa dispersione casuale (controllata da un parametro "fuzz" o "roughness").
  Si pensi all'acciaio spazzolato o all'oro lucidato.

- Le superfici **dielettriche (vetro)** riflettono *e* rifrangono il raggio. Una
  frazione della luce rimbalza sulla superficie; il resto si piega entrando o uscendo
  dal materiale. Il rapporto dipende dall'angolo di incidenza (l'*effetto Fresnel*)
  e dall'indice di rifrazione del materiale.

- Le superfici **emissive** aggiungono la propria luce al raggio. Non si limitano a
  riflettere la luce in arrivo -- emettono luce propria.

- Le superfici **Disney/PBR** combinano tutto quanto sopra in un unico modello
  fisicamente ispirato con parametri come metallic, roughness, clearcoat e
  subsurface scattering.

Dopo che il materiale decide cosa accade, viene generato un nuovo raggio (riflesso,
rifratto o disperso) e il processo si ripete. Il raggio continua a rimbalzare finché
non colpisce una superficie emissiva, raggiunge una sorgente luminosa o esaurisce il
numero massimo di rimbalzi.

---

## 1.3 Path tracing e integrazione Monte Carlo

Un singolo percorso di raggio -- fotocamera, superficie, superficie, luce --
cattura solo una delle possibili traiettorie della luce. Per produrre un'immagine
uniforme e priva di rumore è necessario mediare molti percorsi. Questa è
**l'integrazione Monte Carlo**: il motore lancia molti raggi casuali per pixel,
ciascuno seguendo un percorso leggermente diverso, e ne fa la media.

Per rendere l'intero processo infinitamente più efficiente, 3D-Ray utilizza una tecnica chiave chiamata **Next Event Estimation (NEE)**. Invece di affidarsi al puro caso sperando che un raggio rimbalzante colpisca "alla cieca" una sorgente di luce (il che causerebbe molto rumore), ad ogni intersezione 3D-Ray lancia attivamente e deterministicamente "raggi d'ombra" (shadow rays) direttamente verso le sorgenti luminose conosciute. Questo separa di fatto l'illuminazione diretta dai rimbalzi puramente stocastici e indiretti, accelerando drasticamente la convergenza e la pulizia del render finale.

Il parametro chiave è il **numero di campioni per pixel** (SPP). Con 1 campione
l'immagine è estremamente rumorosa -- ogni pixel è essenzialmente un'unica stima
casuale. Con 16 campioni l'immagine diventa riconoscibile. Con 256 o più si avvicina
alla qualità fotografica.

> **Nota tecnica:** 3D-Ray usa il *campionamento stratificato (jittered)*. Ogni
> pixel è diviso in una griglia di sotto-celle (ad esempio, 4x4 = 16 celle per 16
> campioni). Un raggio viene inviato attraverso un punto casuale all'interno di
> ciascuna cella. Questo produce una distribuzione più uniforme rispetto al
> campionamento puramente casuale e converge più velocemente. Con il sampler
> **Sobol** predefinito il motore usa il conteggio esatto richiesto. Con il
> sampler legacy `--sampler prng`, arrotonda per eccesso al quadrato perfetto
> più vicino (es. richiedere 20 campioni fornisce 25 = 5×5). Vedi
> [Profili di Rendering §3](../../reference/profili-di-rendering.md).

---

## 1.4 Profondità del raggio: quanti rimbalzi?

Ogni volta che un raggio colpisce una superficie e genera un nuovo raggio, conta
come un **rimbalzo**. Il parametro `depth` imposta il numero massimo di rimbalzi
per percorso.

- **Le scene diffuse** raramente necessitano di più di 5-10 rimbalzi perché ogni
  rimbalzo assorbe la maggior parte della luce.
- **Gli oggetti di vetro** sono costosi: ogni superficie che il raggio entra ed esce
  richiede due rimbalzi. Una sfera di vetro all'interno di un box di vetro può
  facilmente richiedere 20 o più rimbalzi.
- **Le scene d'interni** illuminate solo da superfici emissive (come una Cornell
  Box) traggono vantaggio da una profondità maggiore perché la luce deve rimbalzare
  molte volte per illuminare la stanza.

La profondità predefinita in 3D-Ray è **8**, più che sufficiente per la
stragrande maggioranza delle scene grazie alla Russian Roulette (sezione 1.5).
Alzala a 16–20 solo quando renderizzi vetri impilati (liquidi nei bicchieri,
dielettrici annidati). Consulta [Profili di Rendering](../../reference/profili-di-rendering.md)
per la guida completa.

---

## 1.5 Russian Roulette: sapere quando fermarsi

Non tutti i percorsi sono ugualmente utili. Un raggio che ha rimbalzato molte volte
e trasporta pochissima energia difficilmente contribuirà in modo significativo al
colore finale del pixel. Invece di tracciare sempre fino alla profondità massima, il
motore usa la **Russian Roulette**: ad ogni rimbalzo oltre un numero minimo, c'è
una probabilità che il percorso venga terminato anticipatamente. I percorsi sopravvissuti
vengono pesati al rialzo per compensare, così il risultato rimane non polarizzato.

Questo è completamente automatico -- non è necessario configurarlo. Significa
semplicemente che il motore concentra le sue risorse dove conta di più.

---

## 1.6 BVH: trovare gli oggetti rapidamente

Una scena può contenere migliaia o milioni di triangoli. Testare ogni raggio contro
ogni oggetto sarebbe impraticabilmente lento. 3D-Ray costruisce una **Bounding Volume
Hierarchy** (BVH) -- un albero di bounding box annidate che consente al motore di
saltare vaste regioni della scena che un raggio non può colpire. Con una BVH, il
costo per trovare l'intersezione più vicina cresce logaritmicamente con il numero di
oggetti, rendendo trattabili anche scene complesse.

La costruzione della BVH è automatica. Non è mai necessario configurarla.

---

## 1.7 Il flusso di lavoro di rendering iterativo

Le immagini di alta qualità richiedono tempo. Non si dovrebbe mai passare
direttamente a un render di produzione. Si usa invece un flusso di lavoro in tre fasi:

| Fase         | Risoluzione | Campioni | Profondità | Campioni ombra | Scopo                              |
|--------------|-------------|----------|------------|----------------|------------------------------------|
| **Anteprima**| 400x225     | 1        | 4          | 1              | Verificare composizione e inquadratura |
| **Bozza**    | 800x450     | 16       | 6          | 1              | Verificare materiali e illuminazione |
| **Finale**   | 1920x1080   | 256      | 8          | 4              | Output di qualità di produzione    |

> I valori di profondità e campioni-ombra corrispondono ai profili canonici in
> [Profili di Rendering](../../reference/profili-di-rendering.md), che riporta
> anche i conteggi campioni raccomandati per ciascuna fase.

Un render di anteprima richiede pochi secondi e dice immediatamente se la fotocamera
è puntata nella direzione giusta e se gli oggetti sono più o meno dove si vogliono.
Una bozza richiede alcuni minuti e permette di valutare colori, materiali e
illuminazione. Solo quando tutto sembra giusto si passa a un render finale che può
richiedere un'ora o più.

I flag CLI che controllano queste impostazioni sono:

| Flag | Forma estesa       | Predefinito | Funzione                                   |
|------|--------------------|-------------|---------------------------------------------|
| `-w` | `--width`          | 1200        | Larghezza immagine in pixel                 |
| `-H` | `--height`         | 800         | Altezza immagine in pixel                   |
| `-s` | `--samples`        | 16          | Campioni per pixel                          |
| `-d` | `--depth`          | 8           | Rimbalzi massimi del raggio                 |
| `-S` | `--shadow-samples` | *(per luce)*| Sovrascrive il conteggio dei campioni d'ombra per tutte le luci area/sphere |
| `-C` | `--clamp`          | 100         | Firefly clamp (radianza per-campione)       |

Il riferimento CLI completo è nel Capitolo 10. Per ora è sufficiente ricordare il
pattern anteprima/bozza/finale -- farà risparmiare ore di lavoro.

---

## 1.8 Anatomia di un file di scena 3D-Ray

Ogni scena è descritta in un singolo file YAML (o un file principale che ne importa
altri). Al livello superiore contiene fino a sette sezioni:

```
world:        Impostazioni globali -- luce ambientale, sfondo, cielo, nebbia
cameras:      Una o più definizioni di fotocamera
materials:    Definizioni dei materiali con nome
entities:     Gli oggetti nella scena
lights:       Sorgenti luminose esplicite
templates:    Blueprint di oggetti riutilizzabili (non renderizzati direttamente)
imports:      Percorsi verso file YAML esterni da unire
```

Ogni sezione verrà appresa in dettaglio nei capitoli successivi. Per ora, la cosa
importante è la struttura generale: una scena è un *world* visto attraverso una
*camera*, popolato da *entities* con *materials*, illuminato da *lights*,
opzionalmente organizzato con *templates* e *imports*.

---

## 1.9 Il sistema di coordinate

3D-Ray usa un sistema di coordinate **destrorso, Y-up**:

- **X** punta a destra.
- **Y** punta in su.
- **Z** punta verso la fotocamera (fuori dallo schermo in una vista predefinita).

Quando si posiziona una sfera a `[0, 1, 0]`, si trova un'unità sopra l'origine.
Un pavimento è tipicamente un piano infinito a `y = 0`. Z positivo è "davanti" alla
posizione predefinita della fotocamera.

I colori sono specificati come triplette `[R, G, B]` nell'intervallo **0.0 - 1.0**
(non 0 - 255). `[1, 0, 0]` è rosso puro; `[0.5, 0.5, 0.5]` è grigio medio.

---

## Riepilogo

| Concetto              | Significato                                              |
|-----------------------|----------------------------------------------------------|
| Backward ray tracing  | I raggi vanno dalla fotocamera nella scena, non dalle luci |
| Path tracing          | Segue ogni raggio attraverso molteplici rimbalzi         |
| Next Event Estimation | Raggi d'ombra espliciti verso le luci per ridurre il rumore |
| Campionamento Monte Carlo | Media molti percorsi casuali per pixel per ridurre il rumore |
| Campioni per pixel    | Più campioni = meno rumore = render più lento            |
| Profondità del raggio | Numero massimo di rimbalzi su superficie per percorso    |
| Russian Roulette      | Terminazione anticipata probabilistica dei percorsi fiochi |
| BVH                   | Struttura di accelerazione per test raggio-oggetto rapidi |
| Campionamento stratificato | Divide il pixel in sotto-celle per una copertura più uniforme |

Con queste basi, si è pronti a scrivere la prima scena.

---

[Successivo: La prima scena](./02-first-scene.md) | [Indice del Tutorial](./README.md)
