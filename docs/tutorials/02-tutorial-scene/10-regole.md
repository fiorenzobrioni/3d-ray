# 10. Regole e Best Practices

## Sintassi e Funzionamento
1. **Colori:** Nelle texture usa sempre una lista di liste: `colors: [[R,G,B], [R,G,B]]`.
2. **Coordinate:** Y positivo è sempre verso l'alto.
3. **Box:** Usa sempre `scale` + `translate` per i box. Il cubo unitario ha centro nell'origine.
4. **IDs materiale:** Ogni `id` deve essere univoco. I riferimenti `material` nelle entità sono **case-sensitive** e devono corrispondere esattamente. Un ID non trovato produce un materiale grigio di fallback senza errore.
5. **BVH:** Il motore ottimizza automaticamente le scene con più di 4 oggetti usando una BVH basata sull'asse con maggiore estensione dei centroidi.
6. **Luci di default:** Se la sezione `lights` è completamente assente dal YAML, il motore aggiunge automaticamente una directional + una point light. Per avere zero luci (scene HDRI-only o emissive-only), scrivi esplicitamente `lights: []`.
7. **Area Light:** I campi `corner`, `u` e `v` sono tutti obbligatori. Se uno è mancante, la luce viene saltata con un warning in console.
8. **Sky:** Usa `background` per interni, `sky: { type: "gradient" }` per outdoor procedurale, `sky: { type: "hdri" }` per illuminazione fotografica. Se `sky` è presente, `background` viene ignorato. Se usi il sun disk del gradient sky, allinea la `direction` con la directional light per coerenza.
9. **Image Texture:** I percorsi in `texture: { type: "image", path: "..." }` sono relativi alla directory del file YAML della scena. File non trovato → fallback magenta visibile con warning in console.
10. **HDRI:** Il percorso in `sky: { type: "hdri", path: "..." }` è relativo alla directory del YAML. Usa `rotation` per ruotare l'ambiente e allineare il sole/finestra con la scena. Con HDRI, usa `lights: []` per luce solo dall'environment map, oppure aggiungi luci per ombre direzionali extra.
11. **Normal Map:** Il `uv_scale` della normal map deve coincidere con quello della texture albedo per evitare disallineamenti. File non trovato → warning in console, superficie rimane liscia. La normale piatta di riferimento è RGB `(128, 128, 255)`: usare `flat-normal.png` generata da NormalMapGen per verificare che il sistema funzioni senza perturbazioni.
12. **CSG:** Entrambi i figli (`left` e `right`) sono obbligatori. Il tipo `infinite_plane` non è supportato come figlio CSG. Per alberi annidati, il campo `name` sui nodi intermedi non è obbligatorio ma aiuta il debug in caso di warning in console.

## Performance
13. **Campioni e area light:** Il costo reale per pixel è `samples × shadow_samples` per ogni area light. Con `-s 128 -S 16`, ogni pixel lancia oltre 2000 raggi. Usa `-S 4` da CLI per il draft — non serve modificare il YAML!
14. **Vetro e dielettrico:** I materiali dielettrici (vetro) sono i più costosi perché ogni rimbalzo può generare sia riflessione che rifrazione. Aumenta `--depth` per scene con molto vetro.
15. **CSG:** Ogni nodo CSG lancia fino a 4 raggi interni per intersezione (due per figlio). Il costo rimane contenuto grazie al rigetto AABB anticipato. Per scene con molti oggetti CSG profondi, usa `-s` basso durante il design.

## Checklist prima del render finale

- [ ] Se la scena usa `cameras:` (lista), hai verificato con `--list-cameras` che i nomi siano corretti.
- [ ] Tutti gli `id` dei materiali sono univoci e referenziati correttamente nelle entità.
- [ ] La `camera.position` non si trova all'interno di un oggetto solido.
- [ ] Le texture con variazioni per-oggetto hanno `randomize_offset` o `randomize_rotation` attivo.
- [ ] Il file YAML usa correttamente gli **spazi** per l'indentazione (niente TAB).
- [ ] È stata eseguita un'anteprima a bassa risoluzione (`-w 400 -s 1 -S 4`).
- [ ] Le area light hanno `corner`, `u` e `v` tutti definiti.
- [ ] Se la scena deve essere buia, `background` è `[0, 0, 0]` e `sky` è assente.
- [ ] I seed degli oggetti con texture randomizzate sono fissi (se vuoi risultati riproducibili tra render).
- [ ] Se usi gradient sky con sun disk, la `direction` è allineata con la directional light.
- [ ] I file delle image texture e degli HDRI esistono nel percorso indicato (relativo al YAML).
- [ ] Per scene HDRI-only o emissive-only, usa `lights: []` esplicito (non omettere la sezione).
- [ ] Se usi `normal_map`, il `uv_scale` coincide con quello della texture albedo.
- [ ] I file delle normal map esistono nel percorso indicato (file mancante → superficie liscia senza errore, ma visivamente sbagliato).
- [ ] Le normal map OpenGL-style non richiedono `flip_y`; le DirectX-style richiedono `flip_y: true`.
- [ ] Ogni nodo `csg` ha sia `left` che `right` definiti, e `operation` è uno dei valori validi (`union`, `intersection`, `subtraction`).
- [ ] I figli CSG non usano `infinite_plane` come tipo.

---

[← Torna all'indice](../02-tutorial-scene.md)
