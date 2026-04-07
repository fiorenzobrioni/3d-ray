# 7. Preset Sky (Cielo Procedurale e HDRI)

Configurazioni pronte per la sezione `sky:` dentro `world:`. Ogni preset include zenith, orizzonte, terreno e opzionalmente un sun disk. Copia il blocco `sky:` nel tuo `world:`.

> **Nota:** Il gradient sky agisce come sorgente di illuminazione globale — i raggi che escono dalla scena campionano il gradiente. Questo produce GI naturale: ombre azzurre, riflessi caldi all'orizzonte, color bleeding realistico. Per l'illuminazione diretta (ombre sugli oggetti), aggiungi una `directional` light con la stessa `direction` del sun disk.

## **Sky: Mezzogiorno (Clear Day)**
Cielo pulito con sole alto, luce neutra-fredda.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.10, 0.30, 0.80]
    horizon_color: [0.65, 0.80, 1.00]
    ground_color:  [0.30, 0.28, 0.22]
    sun:
      direction: [-0.2, -1.0, -0.3]
      color: [1.0, 0.98, 0.92]
      intensity: 15.0
      size: 2.0
      falloff: 48.0
```

## **Sky: Golden Hour (Ora d'Oro)**
Sole basso, luce calda dorata. L'orizzonte è arancio, lo zenit resta blu.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.15, 0.25, 0.55]
    horizon_color: [0.85, 0.55, 0.25]
    ground_color:  [0.20, 0.15, 0.10]
    sun:
      direction: [-0.8, -0.25, -0.5]
      color: [1.0, 0.85, 0.5]
      intensity: 20.0
      size: 4.0
      falloff: 24.0
```

## **Sky: Tramonto Drammatico**
Rosso fuoco all'orizzonte, viola allo zenit. Sole grande e glow ampio.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.08, 0.05, 0.20]
    horizon_color: [0.95, 0.30, 0.05]
    ground_color:  [0.10, 0.05, 0.02]
    sun:
      direction: [-1.0, -0.08, -0.2]
      color: [1.0, 0.4, 0.05]
      intensity: 30.0
      size: 6.0
      falloff: 12.0
```

## **Sky: Cielo Nuvoloso (Overcast)**
Grigio uniforme, nessun sole visibile. Luce morbida e piatta.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.55, 0.58, 0.62]
    horizon_color: [0.70, 0.72, 0.75]
    ground_color:  [0.35, 0.33, 0.30]
```

## **Sky: Notte Serena**
Cielo scuro senza sole. Ideale per scene illuminate da emissivi o luci artificiali.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.01, 0.01, 0.04]
    horizon_color: [0.04, 0.04, 0.08]
    ground_color:  [0.01, 0.01, 0.02]
```

## **Sky: Alba (Sunrise)**
Toni freddi allo zenit con fascia rosa-arancio all'orizzonte.
```yaml
  sky:
    type: "gradient"
    zenith_color:  [0.12, 0.18, 0.45]
    horizon_color: [0.90, 0.55, 0.40]
    ground_color:  [0.15, 0.12, 0.10]
    sun:
      direction: [1.0, -0.15, -0.3]
      color: [1.0, 0.65, 0.35]
      intensity: 18.0
      size: 5.0
      falloff: 16.0
```

> **💡 Tip:** Per un risultato completo, abbina ogni sky preset a una `directional` light con la stessa `direction` e colore simile al sole. Il sun disk fornisce solo la componente visuale nel cielo; la directional light fornisce ombre e highlight sugli oggetti.

## Preset HDRI (Environment Map)

Per usare un HDRI, scarica un file `.hdr` equirectangolare da [Poly Haven](https://polyhaven.com/hdris) e posizionalo in una cartella `hdri/` accanto al YAML. L'HDRI fornisce illuminazione realistica catturata da fotografie reali — il massimo livello di qualità per riflessi metallici e rifrazioni.

**HDRI: Studio Fotografico**
Illuminazione controllata da studio, ideale per product rendering e material showcase.
```yaml
  sky:
    type: "hdri"
    path: "hdri/studio_small_09_4k.hdr"
    intensity: 1.0
    rotation: 0
```

**HDRI: Esterno (Parco / Natura)**
Luce naturale con cielo aperto, per scene outdoor con vegetazione.
```yaml
  sky:
    type: "hdri"
    path: "hdri/meadow_4k.hdr"
    intensity: 1.2
    rotation: 45
```

**HDRI: Interno Architettonico**
Illuminazione da interni con finestre, per scene architettoniche.
```yaml
  sky:
    type: "hdri"
    path: "hdri/entrance_hall_4k.hdr"
    intensity: 0.8
    rotation: 180
```

> **💡 Tip:** Usa `rotation` per allineare la sorgente luminosa principale dell'HDRI (sole, finestra) con la direzione desiderata nella scena. Usa `intensity` per regolare l'esposizione senza modificare il file `.hdr`. Con HDRI, usa `lights: []` per luce solo dall'environment map, oppure aggiungi luci esplicite per ombre direzionali extra.

---

---

[← Torna all'indice](../03-libreria-preset.md)
