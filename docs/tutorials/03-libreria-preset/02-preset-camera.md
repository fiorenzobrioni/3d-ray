# 2. Preset Camera

## **Preset: Studio Classico**
Vista frontale, leggermente rialzata, adatta per product rendering.
```yaml
camera:
  position: [0.0, 2.0, -8.0]
  look_at: [0.0, 1.0, 0.0]
  fov: 40.0
```

## **Preset: Close-Up (Primo Piano)**
Ottimale per dettagli di materiale e texture.
```yaml
camera:
  position: [0.0, 1.5, -3.5]
  look_at: [0.0, 1.0, 0.0]
  fov: 35.0
```

## **Preset: Wide Angle (Architettura)**
Cattura più spazio per scene ampie.
```yaml
camera:
  position: [0.0, 1.5, -7.0]
  look_at: [0.0, 1.5, 0.0]
  fov: 45.0
```

## **Preset: Overhead (Vista Zenitale)**
Perfetto per scacchiere, tavoli o planimetrie.
```yaml
camera:
  position: [0.0, 10.0, 0.01] # Leggero offset in Z per evitare gimbal lock
  look_at: [0.0, 0.0, 0.0]
  fov: 35.0
```

## **Preset: Dutch Angle (Drammatico)**
Inclinazione della camera per un effetto dinamico.
```yaml
camera:
  position: [3, 2, -5]
  look_at: [0, 1, 0]
  vup: [0.3, 1, 0] # Inclina la camera a sinistra
  fov: 50
```

---

---

[← Torna all'indice](../03-libreria-preset.md)
