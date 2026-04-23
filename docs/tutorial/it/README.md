# Tutorial di 3D-Ray

Una guida completa alla creazione di immagini fotorealistiche con il motore 3D-Ray, dai principi fondamentali alle scene complesse.

---

## Capitoli

### [01 -- Che cos'è il ray tracing?](./01-what-is-ray-tracing.md)
Come interagiscono luce, fotocamera e superfici. La teoria alla base del path tracing,
del campionamento Monte Carlo e del flusso di lavoro di rendering iterativo. Nessun
codice ancora -- solo il modello mentale necessario prima di scrivere la prima scena.

### [02 -- La prima scena](./02-first-scene.md)
Una guida pratica che costruisce una scena completa da zero: world, fotocamera,
materiali, oggetti e luci. Al termine si renderizzerà un gruppo di tre sfere su un
pavimento, comprendendo ogni riga del file YAML.

### [03 -- I materiali in dettaglio](./03-materials.md)
Tutti e sei i tipi di materiale spiegati parametro per parametro: Lambertian, Metal,
Dielectric (vetro), Emissive, Disney/PBR e Mix. Incluse le texture procedurali
(checker, noise, marble, wood), le texture da immagine e le normal map.

### [04 -- Tutte le forme](./04-geometric-primitives.md)
Ogni primitiva geometrica supportata dal motore -- da sfere e box a tori, capsule,
annuli, quad e mesh OBJ -- con la sintassi YAML esatta, i valori predefiniti e una
scena galleria che le mostra tutte.

### [05 -- Trasformazioni, gruppi e organizzazione della scena](./05-transforms-and-groups.md)
Spostare, ruotare e scalare gli oggetti. Comporre gerarchie con i gruppi. Definire
template riutilizzabili e istanziarli. Suddividere le scene in più file con il sistema
di import.

### [06 -- Illuminazione avanzata](./06-lighting.md)
Luci point, directional, spot, area e sphere. Superfici emissive che brillano. Ombre
morbide, controllo dei campioni d'ombra e configurazioni di illuminazione pratiche
(studio a tre punti, chiaroscuro drammatico, luce solare esterna).

### [07 -- Cielo, ambiente ed effetti fotocamera](./07-sky-environment-camera.md)
Modalità cielo flat, gradient e HDRI. Il disco solare. Profondità di campo con
apertura e distanza focale. Più fotocamere con nome e come selezionarle.

### [08 -- Constructive Solid Geometry (CSG)](./08-csg.md)
Unione, intersezione e sottrazione -- le tre operazioni booleane che permettono di
scolpire forme complesse a partire da primitive semplici. Operazioni annidate,
materiali per figlio e ricette di modellazione pratiche.

### [09 -- Mezzi partecipanti (Volumetrics)](./09-volumetrics.md)
Nebbia, foschia, effetto sott'acqua, nubi, fumo localizzato. Quattro tipi di
mezzo (homogeneous, height fog, procedurale Perlin fBm, grid 3D) e cinque
phase function (isotropic, HG, Rayleigh, double-HG, Schlick).

### [10 -- Librerie di asset e scene complete](./10-libraries-and-projects.md)
L'ecosistema di librerie incluso: 800+ materiali, 154+ template di oggetti,
14 preset di illuminazione e 18 scene starter kit. Riferimento CLI, flusso di
lavoro del progetto e guida alla risoluzione dei problemi.

### [11 -- Superfici di rivoluzione (Lathe)](./11-lathe-surface-of-revolution.md)
La primitiva `lathe`: fai ruotare un profilo 2D attorno all'asse Y per
ottenere una superficie di rivoluzione analitica. Tre modalità di
interpolazione (linear, Catmull-Rom centripeta, Bezier esplicita), cap
automatici, NEE per i lathe emissivi e il solver Sturm-chain dietro le
quinte.

---

> Ogni capitolo si basa sul precedente. Chi è alle prime armi con il ray tracing
> dovrebbe iniziare dal Capitolo 1 e procedere in ordine. Chi conosce già le basi
> può passare direttamente all'argomento di interesse.
