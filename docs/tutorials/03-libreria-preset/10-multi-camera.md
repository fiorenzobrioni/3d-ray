# 9. Preset Multi-Camera

Configurazioni `cameras:` pronte per scene con più punti di vista. Copia la lista nel tuo YAML e usa `--camera <nome>` da CLI per selezionare.

## **Multi-Camera: Product Showcase (3 viste classiche)**
Ideale per presentare un singolo oggetto da più angolazioni.
```yaml
cameras:
  - name: "hero"
    position: [2.0, 3.0, -6.0]
    look_at: [0, 1, 0]
    fov: 40
    aperture: 0.05
    focal_dist: 7.0

  - name: "top"
    position: [0, 10, 0.01]
    look_at: [0, 0, 0]
    fov: 35
    aperture: 0.0
    focal_dist: 10.0

  - name: "macro"
    position: [0.5, 1.5, -3.0]
    look_at: [0, 1, 0]
    fov: 25
    aperture: 0.15
    focal_dist: 3.5
```

**Uso da CLI:**
```powershell
# Render da tutte e 3 le viste
dotnet run ... -- -i scene.yaml -c hero -o hero.png -s 64
dotnet run ... -- -i scene.yaml -c top -o top.png -s 64
dotnet run ... -- -i scene.yaml -c macro -o macro.png -s 64
```

## **Multi-Camera: Architetturale (4 viste interior)**
Per scene di interni con illuminazione area light o HDRI.
```yaml
cameras:
  - name: "wide"
    position: [5, 2.5, -8]
    look_at: [0, 1.5, 0]
    fov: 55

  - name: "detail"
    position: [1, 1.5, -3]
    look_at: [0, 1.2, 0]
    fov: 35
    aperture: 0.08
    focal_dist: 3.5

  - name: "corner"
    position: [-4, 2, -4]
    look_at: [0, 1, 2]
    fov: 45

  - name: "bird"
    position: [0, 8, 0.01]
    look_at: [0, 0, 0]
    fov: 50
```

## **Multi-Camera: Dutch Angle + Standard**
Quando vuoi sia l'inquadratura standard che una versione cinematografica.
```yaml
cameras:
  - name: "standard"
    position: [0, 2, -8]
    look_at: [0, 1, 0]
    fov: 50

  - name: "dutch"
    position: [2, 2.5, -7]
    look_at: [0, 1, 0]
    vup: [0.2, 1, 0]
    fov: 50
```

> **💡 Tip — Batch render:** Puoi scriptare il render da tutte le camere con un semplice loop:
> ```powershell
> # PowerShell
> foreach ($cam in "hero","top","macro") {
>     dotnet run ... -- -i scene.yaml -c $cam -o "output/${cam}.png" -s 64
> }
> ```
> ```bash
> # Bash
> for cam in hero top macro; do
>     dotnet run ... -- -i scene.yaml -c "$cam" -o "output/${cam}.png" -s 64
> done
> ```

> **Nota:** Con `lights: []` (lista vuota esplicita) ottieni solo illuminazione HDRI pura — il massimo realismo ma le ombre sono morbide. Aggiungendo una `directional` light ottieni ombre direzionali definite mantenendo la GI dell'HDRI.

---

[← Torna all'indice](../03-libreria-preset.md)
