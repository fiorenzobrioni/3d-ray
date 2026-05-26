# Subsurface Scattering — Integratore Random Walk

Il subsurface scattering di 3D-Ray è un random walk volumetrico brute-force, dispatchato sugli eventi di refrazione in geometria legata a un medium `homogeneous` con scattering. Non esiste scorciatoia "diffusion approximation", non c'è dipole, non c'è kernel separabile — ogni fotone che entra nel volume viene trasportato dall'estimatore free-flight completo finché non esce dal boundary, non viene ucciso dalla Russian Roulette, o non raggiunge il cap hard sui bounce volumetrici.

È la stessa ricetta che Cycles spedisce come `random_walk_v2` e Arnold come modalità di default `randomwalk` di `standard_surface`. Il motivo per cui ha rimpiazzato il dipole quasi ovunque in produzione: è **l'**integratore path-traced per geometria arbitraria, gestisce feature sottili e curvatura correttamente, energy-conserva per costruzione, e condivide la sua macchina di sampling con il path tracer volumetrico esistente.

## Derivazione

L'equazione del rendering volumetrico (PBRT §15.1) descrive la radianza lungo un raggio dentro un mezzo partecipante con coefficiente di assorbimento σ_a, coefficiente di scattering σ_s, e phase function f_p:

```
L(p, ω) = T_r(t_b) · L(p + t_b ω, ω)
       + ∫₀^t_b T_r(t) · [σ_a L_e + σ_s L_s] dt
```

dove T_r(t) = exp(-σ_t · t) è la transmittance e σ_t = σ_a + σ_s è l'estinzione. Per un mezzo non-emissivo (il caso SSS — la superficie emette, non il volume) L_e = 0 e si stima l'integrale Monte-Carlo con:

1. Sample della distanza free-flight `t` dalla pdf esponenziale σ_t · exp(-σ_t · t).
2. Se t ≥ t_boundary, il raggio scappa — return della radianza al boundary pesata da transmittance.
3. Altrimenti evento di scattering a p + t · ω; sample nuova direzione ω′ da f_p; riparte con throughput moltiplicato per σ_s / σ_t (albedo single-scatter).

Il throughput accumulato all'uscita è il fattore BTDF "see-through" del volume SSS, applicato alla radianza che attraversa il boundary di uscita.

## Hero-wavelength MIS

I coefficienti σ sono RGB (uno σ_t per canale). Un walk parallel 3-canali ingenuo spara 3 mean-free-path di lavoro per evento di scatter; un walk single-channel unbiased pesca un canale per scatter e scarta gli altri due, ma sottostima eventi rari che sarebbero scappati lungo un canale diverso.

L'estimatore hero-wavelength (Wilkie et al. 2014, reso popolare da Cycles 2.7) trova la via di mezzo. Ogni evento di scatter picka un canale hero `c` con probabilità proporzionale a `throughput[c]`; la distanza free-flight è estratta da σ_t[c]; e il throughput è poi balance-weighted attraverso tutti e 3 i canali:

```
β  *=  σ_s · exp(-σ_t · t) / Σ_c q[c] · σ_t[c] · exp(-σ_t[c] · t)
```

dove `q[c] = throughput[c] / sum(throughput)` è il peso hero-pick. Il risultato è spettralmente unbiased: nel limite l'estimatore per canale matcha il run parallel a 3 canali, ma in pratica converge 2-3× più veloce perché ogni walk ammortizza il costo di intersezione di un path su tutti e tre i canali.

La phase function è HG con `g` dal medium (default 0). La densità del sample HG matcha la densità di eval HG, quindi phase/pdf = 1 — niente fattore MIS extra sulla direzione di scatter.

## Energy & Fresnel coupling

La transmission Fresnel di ingresso è applicata dalla BSDF di superficie *prima* del walk: il throughput passato a `RandomWalkSubsurface` è già `T_entry · viewBSDF`. Il walk stesso non rivede mai la superficie finché non prova a uscire.

A un hit di boundary durante il walk, la BSDF di superficie viene campionata per decidere se il fotone:

- Rifrange fuori (probabilità = trasmissione Fresnel). Il walk termina; il raggio di uscita rientra in `TraceRay` per continuare ad accumulare radianza in world space.
- Riflette indietro (probabilità = riflessione Fresnel / TIR). Il walk continua con la direzione riflessa; il contatore bounce decrementa normalmente. Gestisce il look "marmo lucido" — senza, tutta la luce che colpisce il retro della superficie ad angolo grazing leakerebbe fuori invece che rifrangere dentro al volume.

## Russian Roulette + cap max-bounces

La Russian Roulette in-walk parte da `b >= RrStartBounce` (default 3, configurabile via `--sss-quality`). La probabilità di sopravvivenza è `q = max(β.X, β.Y, β.Z)` clampato in `[0.05, 0.95]`. Sulla sopravvivenza il throughput viene diviso per `q` per restare unbiased; alla morte il walk ritorna la radianza accumulata.

Il cap hard sui bounce (default 64) è un guard rail contro path low-albedo worst-case che RR terminerebbe eventualmente ma non abbastanza in fretta per limitare il costo per-pixel. Su un medium tipo latte (albedo ≈ 1) la maggior parte dei walk termina per RR attorno al bounce 30-40; su un medium denso ceroso il cap subentra.

## Clamp firefly indiretto dentro al walk

Eventi di scatter profondi producono contributi indiretti deboli su un ampio range direzionale — esattamente la configurazione che produce firefly su luci sotto-campionate in profondità. Il walk applica una rampa depth-aware:

```
clamp(b) = _indirectMaxSampleRadiance / (1 + 0.1 · b)
```

così l'NEE al bounce 2 è clampato al limite indiretto globale, ma l'NEE al bounce 32 è clampato molto più stretto (≈ 25% del globale). Matcha `clamp_walk_volume` di Cycles e il clamp depth-aware stile `indirect_specular` di Arnold.

## CLI knobs

| Flag | Default | Note |
|---|---|---|
| `--sss-mode auto\|off` | `auto` | `off` declassa i media pushati ad absorption-only (Beer-Lambert legacy), utile per preview / A/B. |
| `--sss-quality preview\|normal\|high` | eredita da `-q` | Configurazione one-shot di MaxVolumeBounces / RrStartBounce / NeeInsideWalk. |
| `--max-volume-bounces <n>` | dipende dal preset (16/64/256) | Cap hard sulla depth del walk. Tradeoff costo/energia su media densi. |

I preset di qualità seguono la stessa convenzione Preview/Normal/High del flag principale `--quality`:

| Preset | MaxVolumeBounces | RrStartBounce | NeeInsideWalk | Tier |
|---|---|---|---|---|
| `preview` | 16 | 1 | off | Composizione / sanity check. La luce entra solo per refrazione al boundary. |
| `normal`  | 64 | 3 | on  | Default di produzione. NEE campiona luci a ogni evento di scatter interno. |
| `high`    | 256 | 6 | on  | Portfolio / hero shot. Walk profondi completamente risolti. |

Quando l'utente passa `-q draft-small`, il tier SSS è di default `preview`. `-q medium`/`-q final`/`-q ultra` defaultano a `normal`/`high`/`high`. Un `--sss-quality` esplicito vince sempre sul tier inferito.

## Reference preset Jensen 2001

Coefficienti σ riproducibili per materiali comuni (`σ_t = σ_a + σ_s`, unità 1/world-unit, assumendo 1 wu ≈ 1 m):

| Materiale | σ_a (R, G, B) | σ_s (R, G, B) | Phase | Note |
|---|---|---|---|---|
| Marmo (bianco) | 0.0021, 0.0041, 0.0071 | 2.19, 2.62, 3.00 | HG, g=0 | Look Carrara — leggermente caldo |
| Skin1 (Caucasica) | 0.032, 0.17, 0.48 | 9.25, 11.0, 12.6 | HG, g=0.92 | HG forward forte, rosso-dominante |
| Skin2 (Più scura) | 0.063, 0.21, 0.40 | 6.4, 8.9, 10.5 | HG, g=0.92 | Più melanina |
| Latte intero | 0.0011, 0.0024, 0.014 | 2.55, 3.21, 3.77 | iso | Leggero giallo |
| Panna | 0.0002, 0.00028, 0.00136 | 7.38, 5.47, 3.15 | iso | Estinzione forward forte, sensazione "cremosa" |
| Cera / candela | 0.012, 0.012, 0.022 | 4.0, 4.0, 3.6 | HG, g=0.4 | Traslucida, shift giallo |
| Giada | 0.027, 0.0078, 0.043 | 3.0, 4.5, 4.0 | HG, g=0.4 | Verde-shifted |
| Ketchup | 0.061, 0.97, 1.45 | 0.18, 0.07, 0.03 | iso | σ_a alto in G/B → rosso profondo |
| Mela (rossa) | 0.0030, 0.0034, 0.0460 | 2.29, 2.39, 1.97 | iso | Bleed caldo sottile |

Sono i valori canonici post-Jensen che trovi renderizzati nella documentazione Mitsuba / PBRT. Per altri materiali, deriva σ dai σ_s′ ridotti pubblicati via `σ_s = σ_s′ / (1 − g)` e picka `g` secondo l'anisotropia tipica del medium (skin/wax ≈ 0.9 forward, latte/marmo ≈ 0).

## Migrazione dal legacy Disney `subsurface`

Scene pre-Fase-2 che impostano `subsurface > 0` sui materiali Disney non sono più SSS — il parametro è parsato ma ignorato, e il loader emette un warning. Sostituiscilo con un binding `interior_medium`:

```yaml
# PRIMA (Fase 1)
materials:
  - id: marble
    type: disney
    color: [1.0, 1.0, 1.0]
    subsurface: 0.8                     # lobe legacy "flat HK"

# DOPO (Fase 3+)
mediums:
  - id: marble_int
    type: homogeneous
    sigma_a: [0.0021, 0.0041, 0.0071]
    sigma_s: [2.19, 2.62, 3.00]

materials:
  - id: marble_surface
    type: disney
    color: [1.0, 1.0, 1.0]
    spec_trans: 1.0
    ior: 1.5

entities:
  - type: sphere
    material: marble_surface
    interior_medium: marble_int
```

Il tool standalone `src/Tools/MigrateFakeSss/` automatizza questa riscrittura per un albero YAML.
