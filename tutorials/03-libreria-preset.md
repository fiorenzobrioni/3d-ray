# Libreria di Preset e Asset Pronti all'Uso

Una collezione di configurazioni pronte da copiare e incollare nel tuo file YAML per creare scene professionali.

---

## Indice
1. [Come Usare i Preset](#1-come-usare-i-preset)
2. [Preset Camera](#2-preset-camera)
3. [Sistemi di Illuminazione](#3-sistemi-di-illuminazione)
4. [Catalogo Materiali Professionale](#4-catalogo-materiali-professionale)
5. [Oggetti e Primitive Base](#5-oggetti-e-primitive-base)
6. [Scene Base Complete (Stage Starter)](#6-scene-base-complete-stage-starter)

---

## 1. Come Usare i Preset

Ogni preset è un frammento YAML pronto all'uso. Puoi:
- **Copiarlo** direttamente nel tuo file di scena.
- **Combinare** più preset per costruire scene complete.
- **Modificare** i valori per adattarli alle tue esigenze.

---

## 2. Preset Camera

### **Preset: Studio Classico**
Vista frontale, leggermente rialzata, adatta per product rendering.
```yaml
camera:
  position: [0.0, 2.0, -8.0]
  look_at: [0.0, 1.0, 0.0]
  fov: 40.0
```

### **Preset: Close-Up (Primo Piano)**
Ottimale per dettagli di materiale e texture.
```yaml
camera:
  position: [0.0, 1.5, -3.5]
  look_at: [0.0, 1.0, 0.0]
  fov: 35.0
```

### **Preset: Wide Angle (Architettura)**
Cattura più spazio per scene ampie.
```yaml
camera:
  position: [0.0, 1.5, -7.0]
  look_at: [0.0, 1.5, 0.0]
  fov: 45.0
```

### **Preset: Overhead (Vista Zenitale)**
Perfetto per scacchiere, tavoli o planimetrie.
```yaml
camera:
  position: [0.0, 10.0, 0.01] # Leggero offset in Z per evitare gimbal lock
  look_at: [0.0, 0.0, 0.0]
  fov: 35.0
```

### **Preset: Dutch Angle (Drammatico)**
Inclinazione della camera per un effetto dinamico.
```yaml
camera:
  position: [3, 2, -5]
  look_at: [0, 1, 0]
  vup: [0.3, 1, 0] # Inclina la camera a sinistra
  fov: 50
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

### **Preset: Area Light Studio (Soft Shadows)**
Un pannello luminoso da soffitto per ombre morbide con penombra realistica. Ideale per product design.
```yaml
lights:
  - type: "area"
    corner: [-2.0, 4.9, -2.0]
    u: [4.0, 0.0, 0.0]
    v: [0.0, 0.0, 4.0]
    color: [1.0, 0.97, 0.92]
    intensity: 40.0
    shadow_samples: 16
  - type: "point"       # Fill di riempimento laterale
    position: [-6, 2, -3]
    color: [0.8, 0.85, 1.0]
    intensity: 4
```

### **Preset: Studio Spot (Mirror Polish)**
Luce Spot concentrata per creare riflessi spettacolari su metallo o vetro.
```yaml
lights:
  - type: "spot"
    position: [0, 10, -5]
    direction: [0, -1, 1]
    intensity: 200
    inner_angle: 10
    outer_angle: 25
    color: [1, 1, 1]
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

### **Preset: Moonlight (Luce Lunare)**
Atmosfera notturna fredda con ombre molto allungate.
```yaml
lights:
  - type: "directional"
    direction: [0.2, -1, 0.1]
    color: [0.7, 0.7, 1.0]
    intensity: 0.4
```

---

## 4. Catalogo Materiali Professionale

Una collezione di materiali che sfrutta le texture procedurali e la randomizzazione.

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

### **Marmo: Nero Marquinia**
Sfondo nero intenso con venature bianche spettacolari.
```yaml
  - id: "marmo_nero"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 12.0
      noise_strength: 10.0
      colors: [[0.05, 0.05, 0.05], [0.7, 0.7, 0.7]]
      randomize_offset: true
```

### **Legno: Noce**
```yaml
  - id: "legno_noce"
    type: "lambertian"
    texture:
      type: "wood"
      scale: 3.0
      noise_strength: 2.0
      colors: [[0.45, 0.28, 0.15], [0.30, 0.18, 0.08]]
      randomize_rotation: true
```

### **Metallo: Oro Lucido**
```yaml
  - id: "oro"
    type: "metal"
    color: [0.85, 0.65, 0.1]
    fuzz: 0.02
```

### **Metallo: Acciaio Satinato**
```yaml
  - id: "acciaio"
    type: "metal"
    color: [0.7, 0.7, 0.75]
    fuzz: 0.15
```

### **Vetro: Cristallo**
```yaml
  - id: "cristallo"
    type: "dielectric"
    refraction_index: 1.8
    color: [1.0, 1.0, 1.0]
```

### **Vetro: Fumé**
```yaml
  - id: "vetro_fume"
    type: "dielectric"
    refraction_index: 1.5
    color: [0.7, 0.7, 0.7]
```

### **Pavimento: Scacchiera Classica**
```yaml
  - id: "scacchiera"
    type: "lambertian"
    texture:
      type: "checker"
      scale: 2.0
      colors: [[0.05, 0.05, 0.05], [0.95, 0.95, 0.95]]
```

---

## 5. Oggetti e Primitive Base

Strutture di supporto pronte all'uso.

> **Nota:** Il box usa il **centro** come riferimento per il `translate`. Per posizionare la base a Y=0, trasloca di `altezza / 2` in Y.

### **Piedistallo Moderno**
Un semplice blocco (altezza 0.8) su cui esporre un oggetto.
```yaml
  - name: "piedistallo"
    type: "box"
    scale: [2.0, 0.8, 2.0]
    translate: [0.0, 0.4, 0.0]   # Metà altezza in Y → base a Y=0
    material: "marmo_base"
```

### **Base Espositiva Circolare**
Un cilindro basso e largo per presentare prodotti.
```yaml
  - name: "base_expo"
    type: "cylinder"
    center: [0.0, 0.0, 0.0]   # Centro della base inferiore
    radius: 3.0
    height: 0.2
    material: "metallo_scuro"
```

### **Parete con Cornice**
Una parete (quad) con una cornice applicata.
```yaml
  - name: "parete"
    type: "quad"
    q: [-5, 0, 5]
    u: [10, 0, 0]
    v: [0, 8, 0]
    material: "muro_bianco"
  - name: "cornice"
    type: "box"
    scale: [4.0, 3.0, 0.1]
    translate: [0.0, 4.0, 4.9]   # Leggermente davanti alla parete
    material: "legno_noce"
```

### **Teca di Vetro**
Un box trasparente protettivo.
```yaml
  - name: "teca"
    type: "box"
    scale: [4, 4, 4]
    translate: [0, 2, 0]
    material: "vetro_fume"
```

### **Colonnato (Due Colonne + Trave)**
Struttura architettonica classica.
```yaml
  - { name: "col_sx", type: "cylinder", center: [-3, 0, 0], radius: 0.35, height: 4.0, material: "marmo_carrara" }
  - { name: "col_dx", type: "cylinder", center: [3, 0, 0], radius: 0.35, height: 4.0, material: "marmo_carrara" }
  - name: "trave"
    type: "box"
    scale: [7.5, 0.5, 0.9]
    translate: [0.0, 4.25, 0.0]
    material: "marmo_carrara"
```

---

## 6. Scene Base Complete (Stage Starter)

Questi "modelli pronti" includono tutto il necessario bilanciato per iniziare.

### **Stage A: Studio Fotografico Professionale**
Un set pulito con illuminazione area light e fill laterale.
```yaml
world:
  ambient_light: [0.08, 0.08, 0.08]
  background: [0.9, 0.9, 0.9]
  ground: { type: "infinite_plane", material: "studio_floor", y: 0 }

camera:
  position: [0, 2, -10]
  look_at: [0, 1, 0]
  fov: 40

materials:
  - id: "studio_floor"
    type: "lambertian"
    texture: { type: "checker", scale: 4.0, colors: [[0.8, 0.8, 0.8], [0.85, 0.85, 0.85]] }
  - id: "piedistallo_mat"
    type: "lambertian"
    color: [0.9, 0.9, 0.9]

lights:
  - type: "area"
    corner: [-2.5, 4.9, -2.5]
    u: [5.0, 0.0, 0.0]
    v: [0.0, 0.0, 5.0]
    color: [1.0, 0.97, 0.92]
    intensity: 35.0
    shadow_samples: 16
  - type: "point"
    position: [-8, 4, -5]
    color: [0.85, 0.85, 1.0]
    intensity: 20

entities:
  - name: "pedestal"
    type: "box"
    scale: [2.0, 0.2, 2.0]
    translate: [0.0, 0.1, 0.0]
    material: "piedistallo_mat"
```

### **Stage B: Tramonto (Esterno Drammatico)**
Un orizzonte vasto con luce calda diagonale e suolo metallico riflettente.
```yaml
world:
  ambient_light: [0.05, 0.05, 0.1]
  background: [0.8, 0.4, 0.2]
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
  - type: "directional"
    direction: [-1, -0.2, -1]
    color: [1, 0.4, 0.1]
    intensity: 2
```

### **Stage C: Neon Cyber-Point (Creativo)**
Un set ad alto contrasto con luci magenta e ciano, stile sci-fi.
```yaml
world:
  ambient_light: [0.01, 0, 0.02]
  background: [0.0, 0.0, 0.0]

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
  - type: "point"
    position: [-4, 3, 0]
    color: [1, 0, 1]
    intensity: 150
  - type: "point"
    position: [4, 3, 0]
    color: [0, 1, 1]
    intensity: 150
```

### **Stage D: Galleria d'Arte (Area Light + Arch)**
Interno architettonico con illuminazione da soffitto morbida, pavimento in marmo e colonnato.
```yaml
world:
  ambient_light: [0.03, 0.03, 0.04]
  background: [0.0, 0.0, 0.0]
  ground: { type: "infinite_plane", material: "pavimento_marmo", y: 0 }

camera:
  position: [0, 2.5, -12]
  look_at: [0, 2, 0]
  fov: 45

materials:
  - id: "pavimento_marmo"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 6.0
      noise_strength: 8.0
      colors: [[0.9, 0.88, 0.85], [0.4, 0.35, 0.3]]
  - id: "colonna_marmo"
    type: "lambertian"
    texture:
      type: "marble"
      scale: 10.0
      randomize_rotation: true
      colors: [[0.95, 0.95, 0.95], [0.6, 0.6, 0.6]]
  - id: "esposto"
    type: "dielectric"
    refraction_index: 1.7
    color: [0.85, 0.7, 0.2]

lights:
  - type: "area"
    corner: [-3.0, 5.9, -3.0]
    u: [6.0, 0.0, 0.0]
    v: [0.0, 0.0, 6.0]
    color: [1.0, 0.96, 0.88]
    intensity: 45.0
    shadow_samples: 16
  - type: "spot"
    position: [0, 5.5, 0]
    direction: [0, -1, 0]
    color: [1.0, 0.98, 0.9]
    intensity: 20
    inner_angle: 15
    outer_angle: 30

entities:
  - { name: "col_sx", type: "cylinder", center: [-3.5, 0, 1], radius: 0.3, height: 5.5, material: "colonna_marmo" }
  - { name: "col_dx", type: "cylinder", center: [3.5, 0, 1], radius: 0.3, height: 5.5, material: "colonna_marmo" }
  - { name: "oggetto_esposto", type: "sphere", center: [0, 1.5, 0], radius: 1.2, material: "esposto" }
```

---

> **💡 Consigli d'uso:**
> - Usa `randomize_offset: true` e `randomize_rotation: true` nelle texture procedurali per far apparire ogni oggetto unico anche con lo stesso materiale.
> - Per gli Stage che usano area light, usa `-S 4 -s 1` da CLI per il draft, poi `-S 16 -s 128` per il render finale — non serve modificare il YAML!
> - I seed fissi negli oggetti garantiscono che le venature siano identiche tra render successivi — utile per iterare sull'illuminazione senza cambiare l'aspetto dei materiali.
