# 9. Regole e Best Practices CSG

1. **Entrambi i figli sono obbligatori.** Se `left` o `right` mancano, l'entità viene saltata con un warning in console.
2. **`operation` deve essere un valore valido.** Valori accettati: `union`, `intersection`, `subtraction` (alias: `subtract`, `difference`). Un valore errato salta l'entità.
3. **`infinite_plane` non può essere figlio CSG.** Il piano infinito non ha un AABB finita e non produce un intervallo di ray chiuso. Usa invece un box molto grande e piatto come piano finito.
4. **La sottrazione non è commutativa.** `A \ B` e `B \ A` producono forme diverse. Nella `subtraction`, `left` è il solido che rimane, `right` è lo stampo che viene rimosso.
5. **Le normali nella subtraction vengono invertite.** La superficie di `right` che risulta dopo il taglio ha la normale rivolta verso l'interno del solido `right` — invertita automaticamente dal motore per produrre l'orientazione corretta verso l'esterno della cavità.
6. **Materiale per figlio vs materiale fallback.** Se un figlio specifica `material:`, usa quel materiale. Se non lo specifica, eredita il `material:` del nodo padre CSG. Se anche il padre non ha `material:`, viene usato il grigio di fallback del motore.
7. **Trasformazioni sul nodo padre CSG.** Puoi applicare `translate`, `rotate`, `scale` al nodo CSG radice per posizionarlo nella scena senza dover modificare le coordinate dei figli.
8. **Profondità di annidamento.** Non c'è un limite fisso, ma ogni livello aggiunge 2 test di intersezione. Per alberi con più di 4–5 livelli, testa le prestazioni con `-s 1` prima di fare il render finale.
9. **Debug del YAML.** Se un'entità CSG non appare, controlla la console: il motore stampa warning espliciti per ogni problema di configurazione (figli mancanti, operation sconosciuta, figlio non creato).
10. **Intersezione con oggetti trasparenti.** Le operazioni CSG funzionano con materiali dielettrici e Disney (spec_trans > 0), ma i riflessi interni sono calcolati solo fino a `--depth` rimbalzi. Per lenti con rifrazioni realistiche, usa `-d 20` o più.

---

[← Torna all'indice](../04-libreria-csg.md)
