# Libreria Font — 3D-Ray

Font 3D per la primitiva `extrusion` del motore: ogni carattere è un
template con profilo poligonale estruso lungo Y. I file sono generati
dallo strumento `FontGen` incluso nel repository.

## Come usare

I font si importano come qualsiasi libreria nella sezione `imports:`.
Ogni carattere è un template istanziabile con `type: "instance"`.

```yaml
imports:
  - path: "libraries/fonts/font-open-sans.yaml"

materials:
  - id: "font_material"
    type: "disney"
    color: [0.9, 0.9, 0.9]
    roughness: 0.15
    metallic: 0.0

entities:
  - type: "instance"
    template: "lettera_H_maiusc_open-sans"
    translate: [0, 0, 0]

  - type: "instance"
    template: "lettera_i_minusc_open-sans"
    translate: [0.35, 0, 0]
```

Il materiale `font_material` è il materiale di default per tutti i
caratteri — ridefiniscilo nella scena per cambiare aspetto all'intera
font.

## Convenzioni dei template

| Campo | Valore |
|-------|--------|
| Naming | `lettera_<char>_<maiusc\|minusc>_<font>` per lettere, `numero_<digit>_<font>` per cifre |
| Asse estruso | Y (l'altezza del carattere si estende lungo Y) |
| Origine | centrato in XZ, base a Y=0 |
| Altezza default | 0.15 unità (modificabile con `scale`) |
| Materiale | `font_material` (da ridefinire nella scena) |

```yaml
# Esempio: lettera H alta 0.30 in acciaio spazzolato
- type: "instance"
  template: "lettera_H_maiusc_open-sans"
  material: "dis_acciaio_spazzolato"
  scale: 2.0          # 2× → altezza 0.30
  translate: [0, 0, 0]
```

## I file della libreria

| File | Font | Caratteri |
|------|------|-----------|
| `font-open-sans.yaml` | Open Sans (Regular) | A–Z, a–z, 0–9 |

## Rigenerare o aggiungere font

Usa lo strumento `FontGen` incluso nel repository:

```bash
# Lista i font disponibili nel sistema
dotnet run --project src/Tools/FontGen/FontGen.csproj -- --list-fonts

# Genera un nuovo font (altezza default 0.15, A–Z a–z 0–9)
dotnet run --project src/Tools/FontGen/FontGen.csproj -- \
  --font "Roboto Slab" \
  --height 0.20

# Genera solo alcune lettere
dotnet run --project src/Tools/FontGen/FontGen.csproj -- \
  --font "Google Sans" --chars "ABC123"
```

L'output viene scritto in `scenes/libraries/fonts/`. I file TTF sorgente
si trovano in `scenes/libraries/fonts/ttf/`.

## Font TTF inclusi

| Cartella | Font | Licenza |
|----------|------|---------|
| `ttf/open-sans/` | Open Sans Regular + Bold | OFL |
| `ttf/google-sans/` | Google Sans Regular + Bold | OFL |
| `ttf/google-sans-flex/` | Google Sans Flex (variable) | OFL |
| `ttf/coiny/` | Coiny Regular | OFL |
| `ttf/roboto-slab/` | Roboto Slab Bold | OFL |

Tutti i font sono distribuiti con licenza Open Font License (OFL),
compatibile con l'uso in scenari di rendering.
