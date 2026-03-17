# Tutorial: Libreria di Preset e Asset YAML

Questa guida raccoglie una collezione di configurazioni "preconfezionate" per facilitare la creazione di scene complesse. Puoi copiare e incollare questi blocchi direttamente nei tuoi file `.yaml`.

---

## Indice
1. [Mondi ed Ambienti](#1-mondi-ed-ambienti)
2. [Configurazioni Camera](#2-configurazioni-camera)
3. [Sistemi di Illuminazione](#3-sistemi-di-illuminazione)
4. [Oggetti e Primitive Base](#4-oggetti-e-primitive-base)
5. [Catalogo Materiali Professionale](#5-catalogo-materiali-professionale)
6. [Scene Base Complete (Stage Starter)](#6-scene-base-complete-stage-starter)

---

## 1. Mondi ed Ambienti

Gli ambienti definiscono l'atmosfera globale e il colore del cielo.

### **Preset: Studio Fotografico (High-Key)**
Un ambiente pulito, ideale per isolare un oggetto e metterne in risalto i colori.
```yaml
world:
  ambient_light: [0.2, 0.2, 0.2]
  background: [0.95, 0.95, 0.95] # Bianco quasi puro
  ground:
    type: "infinite_plane"
    material: "studio_floor"
    y: 0.0
```

### **Preset: Blue Hour (Tramonto)**
Atmosfera soffusa e fredda con un tono bluastro.
```yaml
world:
  ambient_light: [0.05, 0.05, 0.15]
  background: [0.1, 0.2, 0.4] # Cielo blu profondo
```

### **Preset: Spazio Profondo (The Void)**
Nero assoluto, perfetto per scene drammatiche dove solo le luci contano.
```yaml
world:
  ambient_light: [0.0, 0.0, 0.0]
  background: [0.0, 0.0, 0.0]

### **Preset: Deep Fog (Nebbia Fitta)**
Visibilità ridotta e atmosfera misteriosa con colori desaturati.
```yaml
world:
  ambient_light: [0.4, 0.4, 0.4]
  background: [0.35, 0.35, 0.35]
```
```

### **Preset: Overcast (Cielo Coperto)**
Luce piatta e diffusa, ideale per esterni senza ombre nette.
```yaml
world:
  ambient_light: [0.3, 0.3, 0.35]
  background: [0.7, 0.7, 0.75] # Grigio tenue
```

### **Preset: Neon Night (Cyberpunk)**
Atmosfera urbana notturna con una leggera tinta neon di base.
```yaml
world:
  ambient_light: [0.02, 0.01, 0.05]
  background: [0.01, 0.0, 0.02]
```

---

## 2. Configurazioni Camera

Profili ottici per diversi tipi di inquadratura artistica.

### **Preset: Macro (Dettaglio Estremo)**
Ideale per piccoli oggetti. Crea uno sfondo molto sfocato (bokeh).
```yaml
camera:
  position: [0.0, 0.5, -2.5]
  look_at: [0.0, 0.4, 0.0]
  fov: 30.0
  aperture: 0.25          # Sfocatura pronunciata
  focal_dist: 2.5
```

### **Preset: Grandangolo (Panoramica)**
Per mostrare intere stanze o paesaggi.
```yaml
camera:
  position: [5.0, 5.0, -10.0]
  look_at: [0.0, 0.0, 0.0]
  fov: 75.0               # Campo visivo ampio
```

### **Preset: Ritratto Classico**
Prospettiva naturale senza distorsioni.
```yaml
camera:
  position: [0.0, 1.5, -7.0]
  look_at: [0.0, 1.5, 0.0]
  fov: 45.0
```

### **Preset: Hero Shot (Dal Basso)**
Inquadratura dal basso verso l'alto per dare importanza al soggetto.
```yaml
camera:
  position: [0.0, 0.2, -4.0]
  look_at: [0.0, 1.2, 0.0]
  fov: 50.0
```

### **Preset: Overhead (Vista Zenitale)**
Perfetto per scacchiere, tavoli o planimetrie.
```yaml
camera:
  position: [0.0, 10.0, 0.01] # Leggero offset in Z per evitare gimbal lock
  look_at: [0.0, 0.0, 0.0]
  fov: 35.0

### **Preset: Ultra Wide (Fisheye)**
Prospettiva distorta e immersiva, eccellente per catturare l'intero volume di una stanza.
```yaml
camera:
  fov: 110.0
```
```

---

## 3. Sistemi di Illuminazione

Combinazioni di luci per scolpire la forma degli oggetti.

### **Preset: Three-Point Lighting (Standard)**
Il setup classico del cinema: Key Light (principale), Fill Light (riempimento) e Back Light (contorno).
```yaml
lights:
  - type: "point"      # Key Light
    position: [5, 5, -5]
    intensity: 100
  - type: "point"      # Fill Light (ombre più morbide)
    position: [-5, 2, -2]
    intensity: 30
    color: [0.8, 0.8, 1.0] 
  - type: "point"      # Back Light (distacca dallo sfondo)
    position: [0, 8, 5]
    intensity: 60
```

### **Preset: Luce Drammatica (Chiaroscuro)**
Forte contrasto da un lato solo.
```yaml
lights:
  - type: "point"
    position: [-8, 4, -4]
    color: [1.0, 0.9, 0.8]
    intensity: 200
```

### **Preset: Warm & Cool Contrast**
Combinazione di luce calda (arancio) e fredda (blu) sui lati opposti del soggetto.
```yaml
lights:
  - type: "point"
    position: [-5, 3, -2]
    color: [1.0, 0.4, 0.1] # Arancio
    intensity: 80
  - type: "point"
    position: [5, 3, -2]
    color: [0.1, 0.4, 1.0] # Blu
    intensity: 80
```

### **Preset: Rim Lighting (Silhouette)**
Luce posizionata dietro l'oggetto per evidenziarne i bordi.
```yaml
lights:
  - type: "point"
    position: [0, 5, 8]
    color: [1.0, 1.0, 1.0]
    intensity: 150

### **Preset: Moonlight (Luce Lunare)**
Atmosfera notturna fredda con ombre molto allungate.
```yaml
lights:
  - type: "directional"
    direction: [0.2, -1, 0.1]
    color: [0.7, 0.7, 1.0]
    intensity: 0.4
```
```

---

## 4. Oggetti e Primitive Base

Strutture di supporto per le tue composizioni.

### **Piedistallo Moderno**
Un semplice blocco su cui esporre una sfera o un altro oggetto.
```yaml
  - name: "piedistallo"
    type: "box"
    min: [-1.0, 0.0, -1.0]
    max: [1.0, 0.8, 1.0]
    material: "marmo_base"
```

### **Teca di Vetro**
Un box trasparente per simulare contenitori o protezioni.
```yaml
  - name: "teca"
    type: "box"
    min: [-2.0, 0.0, -2.0]
    max: [2.0, 4.0, 2.0]
    material: "vetro_standard"
```

### **Base Espositiva Circolare**
Un cilindro basso e largo per presentare prodotti o opere d'arte.
```yaml
  - name: "base_expo"
    type: "cylinder"
    center: [0.0, 0.0, 0.0]
    radius: 3.0
    height: 0.2
    material: "metallo_scuro"
```

### **Architrave / Cornice Doppia**
Due pilastri e una traversa per creare profondità architettonica.
```yaml
  - { name: "pilastro_1", type: "cylinder", center: [-3, 0, 2], radius: 0.4, height: 5, material: "marmo" }
  - { name: "parete_2", type: "cylinder", center: [3, 0, 2], radius: 0.4, height: 5, material: "marmo" }
  - { name: "trave", type: "box", min: [-4, 5, 1.5], max: [4, 5.8, 2.5], material: "marmo" }

### **Cornice per Quadro**
Un box sottile con uno scalino interno per presentare opere o texture sulla parete.
```yaml
  - { name: "cornice_base", type: "box", min: [-2, 1, 4.9], max: [2, 4, 5.0], material: "legno_noce" }
  - { name: "tela", type: "box", min: [-1.8, 1.2, 4.95], max: [1.8, 3.8, 5.0], material: "marmo_carrara" }
```
```

---

## 5. Catalogo Materiali Professionale

Una collezione di materiali che sfrutta le nuove texture procedurali e la randomizzazione.

### **Marmo: Carrara White**
Bianco con venature grigie sottili.
```yaml
  - id: "marmo_carrara"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 15.0
      noise_strength: 12.0
      colors: [[0.95, 0.95, 0.95], [0.5, 0.5, 0.5]]
      randomize_offset: true
```

### **Legno: Noce Scuro (Walnut)**
Elegante, scuro, perfetto per mobili o tavoli.
```yaml
  - id: "legno_noce"
    type: "lambertian"
    texture:
      type: "wood"
      scale: 20.0
      noise_strength: 3.0
      colors: [[0.25, 0.15, 0.1], [0.15, 0.1, 0.05]]
      rotation: [0, 0, 90] # Venature lungo l'asse X
      randomize_offset: true
```

### **Metallo: Oro Satinato**
Riflesso caldo e morbido.
```yaml
  - id: "oro_satinato"
    type: "metal"
    color: [0.85, 0.65, 0.2]
    fuzz: 0.15
```

### **Terreno: Scacchiera Classica**
Il classico pavimento da test.
```yaml
  - id: "pavimento_test"
    type: "lambertian"
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.1, 0.1, 0.1], [0.2, 0.2, 0.2]]
```

### **Marmo: Nero Marquinia**
Sfondo nero intenso con venature bianche spettacolari.
```yaml
  - id: "marmo_nero"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 18.0
      colors: [[0.05, 0.05, 0.05], [0.9, 0.9, 0.9]]
      randomize_rotation: true
```

### **Legno: Ebano (Ebony)**
Legno quasi nero, molto denso e lucido.
```yaml
  - id: "ebano"
    type: "metal"
    fuzz: 0.2
    texture:
      type: "wood"
      scale: 40.0 # Venature molto fitte
      colors: [[0.1, 0.1, 0.1], [0.02, 0.02, 0.02]]
      randomize_offset: true
```

### **Pietra Grezza / Cemento**
Effetto granuloso creato con il Noise.
```yaml
  - id: "cemento"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 50.0
      colors: [[0.5, 0.5, 0.53], [0.4, 0.4, 0.4]]
```

### **Metallo: Acciaio Spazzolato**
Finitura industriale con riflessi molto sfumati.
```yaml
  - id: "acciaio_spazzolato"
    type: "metal"
    color: [0.75, 0.75, 0.78]
    fuzz: 0.4
```

### **Sfondo: Gradiente Soft Studio**
Texture noise molto larga per creare variazioni di colore impercettibili sul muro di sfondo.
```yaml
  - id: "studio_wall"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 0.5 # Scala molto piccola = pattern molto grande
      colors: [[0.8, 0.8, 0.8], [0.7, 0.7, 0.7]]

### **Metallo: Oro Rosso (Rose Gold)**
Riflesso caldo e lussuoso, ideale per finiture di pregio.
```yaml
  - id: "oro_rosso"
    type: "metal"
    color: [0.95, 0.6, 0.5]
    fuzz: 0.02
```

### **Pietra di Lava**
Texture scura e irregolare con bagliori rossastri nelle venature.
```yaml
  - id: "pietra_lava"
    type: "lambertian"
    texture:
      type: "noise"
      scale: 15.0
      colors: [[0.1, 0.1, 0.1], [0.15, 0.05, 0.0]]
```

### **Vetro: Rubino Profondo**
Vetro rosso intenso, ideale per gioielli o decorazioni di lusso.
```yaml
  - id: "vetro_rubino"
    type: "dielectric"
    refraction_index: 1.6
    color: [0.8, 0.05, 0.05]
```

### **Vetro: Smeraldo**
Tonalità verde brillante con alta rifrazione.
```yaml
  - id: "vetro_smeraldo"
    type: "dielectric"
    refraction_index: 1.58
    color: [0.1, 0.7, 0.2]
```

### **Vetro: Ambra Antica**
Un tono caldo e dorato, perfetto per simulare resine o vetri d'epoca.
```yaml
  - id: "vetro_ambra"
    type: "dielectric"
    refraction_index: 1.55
    color: [0.9, 0.6, 0.1]
```

### **Vetro: Cobalto (Blu Reale)**
Blu profondo e saturo.
```yaml
  - id: "vetro_cobalto"
    type: "dielectric"
    refraction_index: 1.52
    color: [0.05, 0.1, 0.8]
```

### **Vetro: Fumé Professionale**
Grigio neutro che riduce la luminosità del raggio senza distorcere i colori.
```yaml
  - id: "vetro_fume"
    type: "dielectric"
    refraction_index: 1.5
    color: [0.3, 0.3, 0.32]
```

---

## 6. Scene Base Complete (Stage Starter)

Questi "modelli pronti" includono Mondo, Camera, Luci e Oggetti bilanciati. Copiali interamente per iniziare a renderizzare immediatamente.

### **Stage A: Studio Fotografico Professionale**
Un set pulito con illuminazione a 3 punti, ideale per cataloghi prodotti.
```yaml
world:
  ambient_light: [0.15, 0.15, 0.15]
  background: [0.9, 0.9, 0.9]
  ground: { type: "infinite_plane", material: "studio_floor", y: 0 }
camera:
  position: [0, 2, -10]
  look_at: [0, 1, 0]
  fov: 40
materials:
  - id: "studio_floor"
    type: "lambertian"
    texture: { type: "checker", scale: 4, colors: [[0.8, 0.8, 0.8], [0.85, 0.85, 0.85]] }
lights:
  - { type: "point", position: [10, 10, -10], intensity: 150 } 
  - { type: "point", position: [-10, 5, -5], intensity: 50, color: [0.9, 0.9, 1] }
entities:
  - { name: "pedestal", type: "box", min: [-1, 0, -1], max: [1, 0.2, 1], material: "lambertian" }
```

### **Stage B: Tramonto su Piano Infinito (Esterno)**
Un orizzonte vasto con luce calda diagonale, perfetto per silhouette architettoniche.
```yaml
world:
  ambient_light: [0.05, 0.05, 0.1]
  background: [1.0, 0.5, 0.3]
  ground: { type: "infinite_plane", material: "ocean_metal", y: 0 }
camera:
  position: [0, 1, -15]
  look_at: [0, 1.5, 0]
  fov: 35
materials:
  - id: "ocean_metal"
    type: "metal"
    color: [0.1, 0.2, 0.4]
    fuzz: 0.1
lights:
  - { type: "directional", direction: [-1, -0.2, -1], color: [1, 0.4, 0.1], intensity: 2 }
```

### **Stage C: Galleria d'Arte Moderna (Interno)**
Una stanza con pavimento in marmo e illuminazione a pioggia (zenitale).
```yaml
world:
  ambient_light: [0.02, 0.02, 0.02]
  background: [0.5, 0.7, 1.0]
camera:
  position: [8, 3, -12]
  look_at: [0, 2, 0]
materials:
  - id: "pavimento_marmo"
    type: "lambertian"
    texture: { type: "marble", scale: 5, colors: [[0.8, 0.8, 0.8], [0.9, 0.9, 0.9]] }
lights:
  - { type: "point", position: [0, 10, 0], intensity: 200 }
entities:
  - { name: "pavimento", type: "box", min: [-10, -0.1, -10], max: [10, 0, 10], material: "pavimento_marmo" }
  - { name: "parete_dietro", type: "box", min: [-10, 0, 10], max: [10, 10, 11], material: "lambertian" }
```

### **Stage D: Neon Cyber-Point (Creativo)**
Un set ad alto contrasto con luci magenta e ciano, stile sci-fi.
```yaml
world:
  ambient_light: [0.01, 0, 0.02]
  background: [0, 0, 0]
camera:
  position: [0, 2, -10]
  look_at: [0, 1.5, 0]
  fov: 60
materials:
  - id: "metal_neon"
    type: "metal"
    color: [0.1, 0.1, 0.2]
    fuzz: 0.05
lights:
  - { type: "point", position: [-4, 3, 0], color: [1, 0, 1], intensity: 150 }
  - { type: "point", position: [4, 3, 0], color: [0, 1, 1], intensity: 150 }
```
```

---

> **💡 Consiglio**: Quando usi i materiali che hanno `randomize_offset: true`, ricorda che ogni oggetto che indossa quel materiale apparirà diverso dagli altri, creando una scena molto più naturale e meno "digitale".
