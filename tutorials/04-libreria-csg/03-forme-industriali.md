# 3. Forme Industriali e Meccaniche

## Dado Esagonale (Approssimato)

Un cilindro intersecato con tre box ruotati di 60° ciascuno produce l'impronta esagonale:

```yaml
- name: "dado_esagonale"
  type: "csg"
  operation: "intersection"
  material: "acciaio_satinato"
  left:
    type: "csg"
    operation: "intersection"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 1.0
      height: 0.7
    right:
      type: "csg"
      operation: "intersection"
      left:
        type: "box"
        scale: [2.0, 1.0, 1.15]
        translate: [0, 0.35, 0]
      right:
        type: "csg"
        operation: "intersection"
        left:
          type: "box"
          scale: [2.0, 1.0, 1.15]
          rotate: [0, 60, 0]
          translate: [0, 0.35, 0]
        right:
          type: "box"
          scale: [2.0, 1.0, 1.15]
          rotate: [0, -60, 0]
          translate: [0, 0.35, 0]
  right:
    # Foro centrale per il bullone
    type: "cylinder"
    center: [0, -0.1, 0]
    radius: 0.35
    height: 1.0
```

## Bullone

Testa cilindrica (dado semplificato) con gambo:

```yaml
- name: "bullone"
  type: "csg"
  operation: "union"
  material: "acciaio_satinato"
  left:
    # Testa del bullone
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.55
    height: 0.35
  right:
    # Gambo
    type: "cylinder"
    center: [0, -1.5, 0]
    radius: 0.22
    height: 1.5
```

## Anello / Toro Approssimato

Un cilindro grande con un cilindro più piccolo sottratto dal centro:

```yaml
- name: "anello"
  type: "csg"
  operation: "subtraction"
  material: "oro"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 1.0
    height: 0.4
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.7
    height: 1.5
```

## Tubo Cavo

Un cilindro con un cilindro coassiale sottratto:

```yaml
- name: "tubo"
  type: "csg"
  operation: "subtraction"
  material: "metallo_ruggine"
  left:
    type: "cylinder"
    center: [0, 0, 0]
    radius: 0.5
    height: 3.0
  right:
    type: "cylinder"
    center: [0, -0.5, 0]
    radius: 0.4
    height: 4.0
```

## Ingranaggio Semplificato

Un disco con foro centrale e denti cubici aggiunti con union:

```yaml
- name: "ingranaggio"
  type: "csg"
  operation: "subtraction"
  material: "acciaio_satinato"
  left:
    # Corpo principale: disco con foro
    type: "csg"
    operation: "subtraction"
    left:
      type: "cylinder"
      center: [0, 0, 0]
      radius: 1.0
      height: 0.3
    right:
      type: "cylinder"
      center: [0, -0.2, 0]
      radius: 0.3
      height: 0.8
  right:
    # (placeholder — aggiungere denti con union non è pratico in YAML puro;
    #  questo preset mostra la struttura base con foro centrale)
    type: "box"
    scale: [0.01, 0.01, 0.01]
    translate: [999, 999, 999]   # Nodo vuoto: box infinitamente piccolo lontano
```

---

---

[← Torna all'indice](../04-libreria-csg.md)
