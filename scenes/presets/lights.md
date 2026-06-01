# Luci — preset (copia-incolla)

Configurazioni di illuminazione pronte per studio fotografico, esterni, notte/neon e
scene con caustiche attive. Tutti i blocchi sono pronti da incollare nel `lights:`
della tua scena. Per il flusso d'uso vedi [`README.md`](README.md); schema completo in
[`../../docs/reference/scene-reference.md`](../../docs/reference/scene-reference.md).

> **Regola d'oro per le luci `area`.** Il motore richiede `corner`, `u` e `v`
> (i tre vettori che definiscono il rettangolo): **non** `position`/`width`/`height`.
> Un'area senza `corner` viene ignorata con warning. Qualsiasi geometria con materiale
> `emissive` entra automaticamente in NEE (vedi Sezione D). Le **caustiche** (Sezione E)
> sono guidate da luci `area`/geometriche, `sphere`, e `point`/`spot` (queste modellate
> come piccolo bulbo sferico finito di raggio `soft_radius`); solo `directional`/sole e
> il cielo/HDRI non le producono.

---

## Schema rilevante

```yaml
lights:
  - type: "area"               # rettangolo soft: key/fill/soffitto
    corner: [-1, 5, -1]        # un angolo del rettangolo (world-space)
    u: [2, 0, 0]               # vettore larghezza
    v: [0, 0, 2]               # vettore altezza
    color: [1.0, 0.96, 0.90]   # tinta (linear sRGB)
    intensity: 45.0            # 1–15 fill · 20–60 key · 70–120 caustiche
    shadow_samples: 9          # 4 preview · 9 standard · 16 penombre fini

  - type: "point"              # sorgente puntiforme onnidirezionale
    position: [3, 4, 2]
    color: [1, 1, 1]
    intensity: 30.0

  - type: "spot"               # cono orientabile con penombra
    position: [0, 6, 0]
    direction: [0, -1, 0]      # vettore di puntamento (normalizzato)
    cone_angle_deg: 30         # apertura totale del cono
    penumbra_deg: 8            # ampiezza della zona di transizione
    color: [1, 0.95, 0.85]
    intensity: 50.0

  - type: "directional"        # sole/luna: raggi paralleli da una direzione
    direction: [0.3, -0.8, 0.4]  # vettore verso cui la luce punta
    color: [1.0, 0.95, 0.85]
    intensity: 6.0
    angular_radius_deg: 0.53   # disco solare → penombre morbide

  - type: "portal"             # finestra verso il cielo (solo interni)
    anchor: [3.0, 1.2, -2.5]  # un angolo della finestra
    u: [0.0, 0.0, 2.5]        # bordo U (cross(u,v) punta verso il cielo)
    v: [0.0, 1.2, 0.0]        # bordo V
    shadow_samples: 8
```

---

# Sezione A — Studio

Setup fotografici in studio chiuso. Abbinare a `world.md` B1 (ciclorama bianco),
B2 (studio nero) o B4 (neon/cyberpunk). Render consigliato: `-s 256 -d 6`.

## A1. 3-point standard (key / fill / rim)

```yaml
lights:
  - type: "area"   # KEY — diagonale sinistra, calda
    corner: [-4.0, 4.0, -2.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 0.0, 2.0]
    color: [1.0, 0.96, 0.90]
    intensity: 45.0
    shadow_samples: 9
  - type: "area"   # FILL — destra, fredda, più debole
    corner: [3.0, 3.0, -1.5]
    u: [1.2, 0.0, 0.0]
    v: [0.0, 0.0, 1.2]
    color: [0.72, 0.82, 1.0]
    intensity: 15.0
    shadow_samples: 9
  - type: "area"   # RIM — dietro e in alto, bianca
    corner: [-1.0, 3.5, 4.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 1.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 30.0
    shadow_samples: 9
```

Il setup universale per ritratti e still life: key calda a 45° che modella il
volume, fill fredda che riempie le ombre senza cancellarle, rim bianca che stacca il
soggetto dallo sfondo. La temperatura contrapposta key/fill amplifica la profondità
percepita.

## A2. High-key (chiave alta)

```yaml
lights:
  - type: "area"   # soft box frontale grande
    corner: [-5.0, 5.0, -3.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 1.0, 1.0]
    intensity: 60.0
    shadow_samples: 12
  - type: "area"   # pannello schiarente dal basso
    corner: [-5.0, 1.0, 4.0]
    u: [10.0, 0.0, 0.0]
    v: [0.0, 4.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 25.0
    shadow_samples: 9
```

Due soft box grandi quasi eliminano le ombre: look catalogo, e-commerce, beauty.
Abbinare a `world.md` B1 (ciclorama bianco) per fondo puro. `shadow_samples: 12`
garantisce uniformità sui bordi delle penombre.

## A3. Drammatico (low-key)

```yaml
lights:
  - type: "spot"
    position: [-3.0, 6.0, 2.0]
    direction: [0.4, -1.0, -0.3]
    cone_angle_deg: 24
    penumbra_deg: 6
    color: [1.0, 0.97, 0.92]
    intensity: 80.0
    shadow_samples: 12
```

Un solo spot dall'alto-laterale: massimo contrasto, ombre dure, atmosfera teatrale.
Abbinare a `world.md` B2 (studio nero). Funziona bene su oggetti con forte
tridimensionalità — sculture, gioielli, flaconi.

## A4. Prodotto (product / packshot)

```yaml
lights:
  - type: "area"   # soft box sinistro
    corner: [-4.0, 3.0, 1.0]
    u: [1.5, 0.0, 0.0]
    v: [0.0, 2.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 40.0
    shadow_samples: 12
  - type: "area"   # soft box destro (simmetrico)
    corner: [2.5, 3.0, 1.0]
    u: [1.5, 0.0, 0.0]
    v: [0.0, 2.0, 0.0]
    color: [1.0, 1.0, 1.0]
    intensity: 40.0
    shadow_samples: 12
  - type: "area"   # rim in alto per il contorno
    corner: [-1.0, 4.0, 4.0]
    u: [2.0, 0.0, 0.0]
    v: [0.0, 1.0, 0.0]
    color: [0.9, 0.95, 1.0]
    intensity: 25.0
    shadow_samples: 9
```

Due soft box laterali simmetrici danno la classica illuminazione packshot "ali di
farfalla", con il rim in alto che separa il profilo. Per superfici riflessive o
vetro usa `-C 25` per spegnere i fireflies dalle riflessioni intense.

---

# Sezione B — Esterni

Da abbinare a `world.md` A1–A9 e ai corrispondenti preset `sky.md`. I preset
direzionali qui funzionano senza cielo fisico; se usi già un cielo analitico con
`sun:`, il PhysicalSun viene auto-registrato dal loader e non è necessaria
una luce `directional` separata.

## B1. Golden hour

```yaml
lights:
  - type: "directional"
    direction: [0.85, -0.25, 0.45]   # sole basso, quasi all'orizzonte
    color: [1.0, 0.72, 0.42]         # arancio caldo
    intensity: 5.0
    angular_radius_deg: 0.53
```

Sole radente con tinta calda: ombre lunghe e morbide, luce scolpente. `angular_radius_deg:
0.53` (disco reale) ammorbidisce i bordi delle ombre senza sfumarli eccessivamente.
Abbinare a `world.md` A2/A8 e `-S 6` per le penombre.

## B2. Mezzogiorno

```yaml
lights:
  - type: "directional"
    direction: [0.2, -0.95, 0.1]     # quasi zenitale
    color: [1.0, 0.98, 0.92]         # bianco neutro, lieve caldo
    intensity: 8.0
    angular_radius_deg: 0.53
```

Sole alto: ombre corte e dure, luce piatta e neutra. L'intensità `8.0` compensa
l'angolo sfavorevole per le superfici verticali. Abbinare a `world.md` A1.

## B3. Coperto (overcast)

```yaml
lights:
  - type: "area"
    corner: [-15.0, 18.0, -15.0]
    u: [30.0, 0.0, 0.0]
    v: [0.0, 0.0, 30.0]
    color: [0.92, 0.94, 1.0]         # bianco-blu, luce di cielo nuvoloso
    intensity: 4.0
    shadow_samples: 16
```

Una grande area dall'alto simula il cielo coperto: luce diffusa, ombre quasi
assenti, temperatura leggermente fredda. `shadow_samples: 16` è necessario per
evitare banding nelle zone d'ombra su grandi superfici. Abbinare a `world.md` A5.

---

# Sezione C — Notte e neon

## C1. Luce di luna

```yaml
lights:
  - type: "directional"
    direction: [0.4, -0.7, 0.3]
    color: [0.55, 0.65, 0.95]   # fredda, bluastra
    intensity: 0.8
    angular_radius_deg: 0.5
```

Direzionale fredda a intensità molto bassa: ombre morbide e bluastre tipiche della
luce lunare. Abbinare a `world.md` A6 (cielo notturno con gradiente). Un punto
`point` caldo a intensità 2–5 simula una lanterna o lampione in scena.

## C2. Neon / cyberpunk

```yaml
lights:
  - type: "area"   # pannello magenta sinistra
    corner: [-4.0, 1.0, -2.0]
    u: [0.1, 3.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.1, 0.55]
    intensity: 35.0
    shadow_samples: 9
  - type: "area"   # pannello cyan destra
    corner: [4.0, 1.0, -2.0]
    u: [0.1, 3.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [0.1, 0.7, 1.0]
    intensity: 35.0
    shadow_samples: 9
```

Due pannelli verticali con tinte sature contrapposte (magenta vs cyan): ogni
superficie riflessiva cattura entrambi i colori creando il tipico split-toning
cyberpunk. Abbinare a `world.md` B4 (pavimento metallico viola-nero). Usa `-C 25`
con intensità alte per spegnere i fireflies sulle riflessioni sature.

---

# Sezione D — Luci geometriche (emissive)

Una geometria con materiale `emissive` emette luce ed entra automaticamente nel
campionamento diretto (NEE). A differenza di `lights:`, è **visibile in camera** e
contribuisce al look dell'ambiente; utile per pannelli a soffitto, strisce LED,
tubi neon visibili in scena. Il blocco `materials:` può stare nella stessa scena
o in un file importato.

## D1. Pannello soffitto emissivo

```yaml
materials:
  - id: "emi_pannello"
    type: "emissive"
    color: [1.0, 0.95, 0.85]   # bianco caldo (≈ 3000 K)
    intensity: 12.0             # 8–15 interni normali · 20–40 ambienti scuri

entities:
  - type: "quad"
    corner: [-2, 5, -2]
    u: [4, 0, 0]
    v: [0, 0, 4]
    material: "emi_pannello"
```

Pannello luce da soffitto: luce diffusa e morbida come un softbox ma fisicamente
presente nella scena (visibile nelle riflessioni specchianti). La posizione e il
`corner`/`u`/`v` si scalano in proporzione all'ambiente.

## D2. Tubo neon emissivo

```yaml
materials:
  - id: "emi_neon_rosa"
    type: "emissive"
    color: [1.0, 0.15, 0.5]   # magenta neon
    intensity: 30.0

entities:
  - type: "cylinder"
    center: [0, 2.5, -3]
    radius: 0.04               # sottile → ombra morbida con penombra tipica
    height: 3.0
    material: "emi_neon_rosa"
```

Tubo cilindrico emissivo: simula un tubo neon o una striscia LED verticale. Il
raggio piccolo (`0.04`) concentra l'emissione. Per strisce orizzontali o inclinate
avvolgi il cilindro in un `Transform`. Cambia `color` per altri colori neon
(`[0.1, 0.7, 1.0]` cyan, `[0.2, 1.0, 0.4]` verde).

---

# Sezione E — Luci ottimizzate per caustiche

Le caustiche focalizzate richiedono **un solo opt-in**: il flag CLI `--caustics on`
(di default sui preset `final`/`ultra`). Con quello attivo il motore
**auto-classifica** le entità in caster (geometria curva + materiale
speculare/trasmissivo) e riceventi (tutto il resto, incluso il `ground`); i flag
YAML `caustic_caster`/`caustic_receiver` restano come **override opzionali a 3 stati**
(assente = auto, `true` = forza, `false` = escludi). Guidano MNEE e SMS le luci
**`area`/geometriche**, **`sphere`** e **`point`/`spot`** (queste modellate come piccolo
bulbo sferico finito di raggio `soft_radius`); `directional`/sole e cielo/HDRI **non**
producono caustiche focalizzate. Per la guida completa su caster, receiver e materiali
vedi [`caustics.md`](caustics.md).

La nitidezza della caustica dipende dalla **dimensione** della sorgente:
piccola e intensa → spot netto (MNEE); grande → alone morbido (SMS). Per `point`/`spot`
la dimensione è il `soft_radius` del bulbo (più piccolo = più netto e rumoroso; alza
`--mnee-samples` per ripulire).

## E1. Area piccola per caustica netta — vetro liscio / specchio (MNEE)

```yaml
lights:
  - type: "area"
    corner: [-0.4, 6.0, -0.4]   # piccola e alta → raggi quasi paralleli
    u: [0.8, 0.0, 0.0]
    v: [0.0, 0.0, 0.8]
    color: [1.0, 0.97, 0.92]
    intensity: 80.0              # alta: l'energia concentrata nel fuoco compensa
    shadow_samples: 6
```

L'area di riferimento per caustiche MNEE (vetro `dielectric` liscio, metallo a
specchio con `fuzz: 0`): dimensione `0.8×0.8` e alta intensità massimizzano la
nitidezza dello spot. Posizione elevata (`y = 6.0`) assicura un angolo d'incidenza
favorevole su caster sferici a `y ≈ 1`. Abbinare a `world.md` B2 (studio nero) per
leggere la caustica senza disturbi ambientali.

## E2. Area media per caustica frosted — vetro smerigliato / metallo satinato (SMS)

```yaml
lights:
  - type: "area"
    corner: [-1.0, 5.5, -1.0]   # più grande → alone più morbido
    u: [2.0, 0.0, 0.0]
    v: [0.0, 0.0, 2.0]
    color: [1.0, 0.97, 0.92]
    intensity: 55.0              # ridotta: area maggiore, densità inferiore
    shadow_samples: 9
```

Per vetro Disney con `roughness > 0.04` e metalli con `fuzz > 0`: la luce più
grande (`2.0×2.0`) allarga il fuoco SMS in un alone soffuso. Alza
`--sms-samples 8–16` per ridurre il rumore tipico del frosted; usa `-C 25` se il
vetro genera spike. Intensità più bassa del preset E1 perché l'energia è distribuita
su un'area maggiore.

## E3. Sole simulato per caustiche outdoor

```yaml
lights:
  - type: "area"
    corner: [-0.25, 20.0, -0.25]   # molto lontana e piccola → raggi quasi paralleli
    u: [0.5, 0.0, 0.0]
    v: [0.0, 0.0, 0.5]
    color: [1.0, 0.97, 0.90]       # bianco caldo (sole di mezzogiorno)
    intensity: 350.0               # distanza alta → compensa con intensità
    shadow_samples: 6
```

Una `directional`/sole non guida MNEE/SMS (sorgente all'infinito, senza area da
campionare), quindi per caustiche outdoor si simula il sole con un'area molto piccola
e lontana (`y = 20`): i raggi convergono quasi parallelamente su un caster sferico a
quota zero, producendo uno spot netto simile a quello solare. L'intensità alta (`350`)
compensa il quadrato della distanza. Per una caustica calda di tramonto tinta con
`color: [1.0, 0.62, 0.28]` e abbassa `y` a `10–12` per allargare leggermente il fuoco.
In alternativa una `sphere` lontana (E4) o uno `spot` stretto (E5) ottengono lo stesso
effetto castando direttamente.

## E4. Sphere light per caustica netta (MNEE)

```yaml
lights:
  - type: "sphere"
    position: [0, 6.0, 0]       # alta → angolo d'incidenza favorevole
    radius: 0.5                  # piccola → fuoco netto (il raggio È l'area emittente)
    color: [1.0, 0.97, 0.92]
    intensity: 70.0
    shadow_samples: 6
```

La `sphere` è un emettitore d'area esatto: casta caustiche come una `area` ma con
geometria sferica (campionamento d'area su `4πR²`). Il `radius` controlla la nitidezza
— piccolo = spot netto. Intensità alta per concentrare l'energia nel fuoco. Ideale
quando vuoi anche un riflesso circolare pulito del proxy luminoso sul caster.

## E5. Point / Spot per caustica da bulbo (MNEE)

```yaml
lights:
  # Point: bulbo onnidirezionale. Il soft_radius dimensiona il bulbo caustico.
  - type: "point"
    position: [0, 6.0, 0]
    color: [1.0, 0.95, 0.88]
    intensity: 140.0            # I/d²: alza per compensare la distanza
    soft_radius: 0.12           # piccolo = fuoco più netto ma più rumoroso

  # Spot: come point, ma la falloff di cono modula anche la caustica.
  - type: "spot"
    position: [0, 6.0, 0]
    direction: [0, -1, 0]
    color: [1.0, 0.95, 0.88]
    intensity: 160.0
    inner_angle: 20
    outer_angle: 40
    soft_radius: 0.12
```

`point`/`spot` castano via **bulbo virtuale finito** di raggio `soft_radius` (default
`0.05` se omesso): più piccolo = caustica più netta ma rumorosa. Il bulbo finito è più
rumoroso di una luce d'area, quindi aggiungi `--mnee-samples 4–8` per ripulire il fuoco.
Lo `spot` applica la sua falloff di cono alla luce focalizzata — utile per isolare una
singola caustica senza illuminare il resto della scena.

---

## Matrice decisionale

| Caso d'uso | Preset | Note chiave |
|------------|--------|-------------|
| Ritratto / still life neutro | A1 3-point | `world.md` B1/B2 · `-s 256 -d 6` |
| Catalogo luminoso, e-commerce | A2 high-key | `world.md` B1 · `-s 256` |
| Cinematografico / teatrale | A3 drammatico | `world.md` B2 · `-S 12` |
| Packshot prodotto, vetro | A4 product | `world.md` B4 · `-C 25 -S 12` |
| Paesaggio tramonto / alba | B1 golden hour | `world.md` A2/A8 · `-S 6` |
| Paesaggio diurno | B2 mezzogiorno | `world.md` A1 · `-S 4` |
| Cielo coperto, ombre morbide | B3 overcast | `world.md` A5 · `-S 16` |
| Notte lunare | C1 luna | `world.md` A6 · `-d 8` |
| Insegne neon, cyberpunk | C2 neon | `world.md` B4 · `-C 25` |
| Pannello soffitto visibile | D1 emissiva quad | geometria + NEE automatico |
| Tubo neon / LED visibile | D2 emissiva cylinder | raggio piccolo → penombra morbida |
| Caustica netta (vetro / specchio) | E1 area piccola | `--caustics on` · studio nero |
| Caustica frosted (smerigliato) | E2 area media | `--caustics on --sms-samples 8` |
| Caustica outdoor (sole simulato) | E3 area lontana | `--caustics on` · alta intensità |
| Caustica netta da sfera | E4 sphere light | `--caustics on` · `radius` piccolo |
| Caustica da bulbo point/spot | E5 point/spot | `--caustics on --mnee-samples 4` · `soft_radius` |

## CLI tips

```bash
# Studio con penombre morbide: alza i campioni d'ombra
dotnet run --project src/RayTracer -- -i scena.yaml -S 12

# Neon / emissive ad alta intensità: clampa i fireflies
dotnet run --project src/RayTracer -- -i scena.yaml -C 25

# Caustiche MNEE (vetro liscio): qualità standard
dotnet run --project src/RayTracer -- -i scena.yaml -q final --caustics on

# Caustiche frosted (SMS): più campioni SMS per pulire il rumore
dotnet run --project src/RayTracer -- -i scena.yaml -q final --caustics on --sms-samples 16 -C 25

# Caustiche da point/spot (bulbo finito): più campioni emettitore per pulire il rumore
dotnet run --project src/RayTracer -- -i scena.yaml -q final --caustics on --mnee-samples 8

# Preview rapida con caustiche attive
dotnet run --project src/RayTracer -- -i scena.yaml -q medium-small --caustics on --sms-samples 4
```
