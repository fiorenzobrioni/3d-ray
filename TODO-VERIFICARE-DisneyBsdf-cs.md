## **1. Importance Sampling per Sheen (Charlie BRDF) e Clearcoat**

---

### **Cosa significa "Importance Sampling"?**
L’**importance sampling** è una tecnica usata in **Monte Carlo rendering** per **ridurre la varianza** (e quindi il rumore) nelle immagini generate da un raytracer. In pratica, invece di campionare direzioni **uniformemente** (es. cosine-weighted per il diffuso), si campionano direzioni **proporzionalmente all’energia del lobo** che si sta valutando.
Questo significa che si generano più campioni dove il BRDF è più forte (es. vicino al picco speculare), riducendo il rumore nelle aree più importanti.

---

---

### **Sheen: Il lobo "Charlie" (Estevez-Kulla 2017)**
Il tuo codice usa il **Charlie BRDF** per il sheen, che è un modello fisicamente basato per materiali come **velluto, seta, o tessuti con lucentezza direzionale**. Questo lobo è **anisotropo** e ha un picco molto pronunciato in direzioni **grazing-angle** (vicino alla tangente della superficie).

#### **Problema con il Cosine-Weighted Sampling**
Attualmente, nel tuo codice (`ScatterSheen`), usi:
```csharp
Vector3 scatterDir = N + MathUtils.RandomUnitVector(); // Cosine-weighted sampling
```
Questo significa che stai campionando direzioni **proporzionalmente a `N·L`** (cosine-weighted), che è ottimale per il **lobo diffuso (Lambertian)**, ma **non per il Charlie BRDF**, che ha un picco molto più stretto e direzionale.

**Risultato:**
- **Alta varianza** (rumore) nelle aree dove il sheen è forte (es. vicino ai bordi degli oggetti).
- **Campioni sprecati** in direzioni dove il sheen contribuisce poco.

---

#### **Soluzione: Importance Sampling per Charlie BRDF**
Per implementare l’**importance sampling** per il Charlie BRDF, dovresti:
1. **Derivare la PDF analitica** del lobo Charlie.
2. **Invertire la CDF** (Cumulative Distribution Function) per generare campioni secondo quella PDF.
3. **Aggiornare il metodo `ScatterSheen`** per usare questo nuovo sampling.

##### **Passo 1: PDF del Charlie BRDF**
Il **Charlie BRDF** è definito come:
```
f_sheen(V, L) = (F_sheen / π) * D_Charlie(H) * G_Charlie(V, L, H)
```
dove:
- `D_Charlie(H)` è la **Normal Distribution Function (NDF)** invertita (a forma di "U").
- `G_Charlie(V, L, H)` è il **geometry term** (maschera/ombra).
- `F_sheen` è il **Fresnel** (solitamente costante per il sheen).

La **PDF** per il sampling è:
```
pdf(L) = (D_Charlie(H) * G_Charlie(V, L, H) * |N·L|) / (4 * |N·V| * |N·H|)
```
*(Nota: Questa è una semplificazione. La PDF esatta dipende dall’implementazione specifica del Charlie BRDF.)*

##### **Passo 2: Invertire la CDF**
Il **Charlie NDF** è una **Gaussian invertita**, quindi la sua CDF **non ha una soluzione analitica chiusa**. Tuttavia, puoi:
- Usare **numerical inversion** (es. rejection sampling o tabulazione).
- Usare un’**approssimazione analitica** (es. come fatto in [Mitsuba](https://mitsuba.readthedocs.io/) o [PBRT](https://www.pbr-book.org/)).

##### **Passo 3: Implementazione in C#**
Ecco un esempio **semplificato** di come potresti modificare `ScatterSheen` per usare importance sampling:

```csharp
private bool ScatterSheen(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                          in ShadingParams sp, float probability,
                          out Vector3 attenuation, out Ray scattered)
{
    // 1. Campiona H (half-vector) secondo la NDF Charlie
    Vector3 H = SampleCharlieNDF(V, N, sp.SheenRoughness);

    // 2. Calcola L = reflect(-V, H)
    Vector3 L = MathUtils.Reflect(-V, H);

    // 3. Verifica che L sia nel semisfero corretto
    if (Vector3.Dot(L, N) <= 0)
    {
        attenuation = Vector3.Zero;
        scattered = new Ray(rec.Point, N);
        return false;
    }

    scattered = new Ray(rec.Point, L);

    // 4. Calcola il BRDF e la PDF
    float NdotV = MathF.Max(Vector3.Dot(N, V), 1e-4f);
    float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-4f);
    float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
    float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);

    float sheenBrdf = SheenCharlie.Brdf(NdotV, NdotL, NdotH, sp.SheenRoughness);
    float pdf = SheenCharlie.Pdf(NdotV, NdotL, NdotH, sp.SheenRoughness); // Da implementare

    // 5. Calcola il peso del campione
    float lum = MathUtils.Luminance(baseCol);
    Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
    Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
    attenuation = sp.Sheen * sheenBrdf * sheenCol / MathF.Max(pdf, 1e-6f);

    // 6. Compensa per la probabilità del lobo
    float safeProbability = MathF.Max(probability, 0.1f);
    attenuation /= safeProbability;

    return true;
}
```

##### **Dove trovare le formule esatte?**
- **Paper originale**: [Estevez & Kulla 2017 - "Production Friendly Microfacet Sheen BRDF"](https://hal.archives-ouvertes.fr/hal-01509746/document)
- **Implementazione in Mitsuba**: [Mitsuba Sheen BRDF](https://github.com/mitsuba-renderer/mitsuba2/blob/master/src/render/bsdf.cpp)
- **Implementazione in PBRT**: [PBRT Sheen](https://github.com/mmp/pbrt-v4/blob/master/src/core/reflection.cpp)

---
---

### **Clearcoat: Importance Sampling**
Il **clearcoat** nel tuo codice usa già **VNDF (Visible Normal Distribution Function)** per il sampling speculare, che è **ottimale** per lobi GGX. Tuttavia, potresti migliorare ulteriormente:
1. **Usare una PDF più accurata** per il clearcoat, che tenga conto del **Fresnel** e del **geometry term**.
2. **Precalcolare la CDF** per il clearcoat (se il costo del sampling è troppo alto).

Esempio di **PDF per clearcoat GGX**:
```
pdf(L) = (D_GGX(H) * G1(V) * |N·L|) / (4 * |N·V| * |N·H|)
```
Dove:
- `D_GGX(H)` è la NDF GGX.
- `G1(V)` è il geometry term (Smith G1).

**Nota:** Il tuo codice usa già VNDF per il clearcoat (`Microfacet.SampleGgxVndfAniso`), che è **molto vicino all’ottimo**. Non c’è molto da migliorare qui, a meno che tu non voglia ottimizzare ulteriormente per casi specifici (es. clearcoat molto rugoso).

---

---
---
---

## **2. Analisi del Tuo Codice: Bug o Possibili Miglioramenti**

Ho analizzato il tuo `DisneyBsdf` in dettaglio. **Non ho trovato bug gravi**, ma ci sono alcuni **punti critici** e **possibili miglioramenti** che vale la pena discutere.

---

### **🔴 Potenziali Bug o Comportamenti Inattesi**

#### **1. `EvalParams`: Caching Thread-Safe ma Potenzialmente Stale**
```csharp
[ThreadStatic] private static DisneyBsdf? _spLastInstance;
[ThreadStatic] private static int     _spLastSeed;
[ThreadStatic] private static float   _spLastU;
[ThreadStatic] private static float   _spLastV;
[ThreadStatic] private static Vector3 _spLastLocalPoint;
[ThreadStatic] private static ShadingParams _spCached;
```
**Problema:**
- Il caching è **thread-safe** (grazie a `[ThreadStatic]`), ma **non è reentrant**.
- Se lo stesso thread chiama `EvalParams` **ricorsivamente** (es. in un raytracer che usa thread pool e riutilizza i thread), il cache potrebbe **restituire valori sbagliati**.
- **Esempio:**
  - Thread A chiama `EvalParams` per `HitRecord rec1` → cache salvato.
  - Thread A chiama `EvalParams` per `HitRecord rec2` (stesso thread, ma `rec` diverso) → **viene restituito il cache di `rec1`** se `rec2` ha gli stessi `U, V, LocalPoint, Seed`.

**Soluzione:**
- Aggiungere un **contatore di profondità** per evitare caching ricorsivo:
  ```csharp
  [ThreadStatic] private static int _spCacheDepth;
  private ShadingParams EvalParams(HitRecord rec)
  {
      if (_spCacheDepth > 0) // Se siamo in una chiamata ricorsiva, non usare il cache
          return ComputeShadingParams(rec);

      _spCacheDepth++;
      try
      {
          // ... logica di caching esistente ...
      }
      finally
      {
          _spCacheDepth--;
      }
  }
  ```

---

#### **2. `ScatterTransmission`: Possibile Divisione per Zero in `eta * sinTheta`**
```csharp
float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
bool cannotRefract = !ThinWalled && eta * sinTheta > 1f;
```
**Problema:**
- Se `cosTheta` è **molto vicino a 1** (es. `0.9999999`), allora `1 - cosTheta * cosTheta` potrebbe essere **negativo a causa di errori di floating-point**, e `MathF.Max(0f, ...)` lo porta a `0`.
- `sinTheta = sqrt(0) = 0` → `eta * sinTheta = 0` → `cannotRefract = false` anche se `eta > 1` (es. raggio che entra in un materiale con `ior = 1.5`).
- **Risultato:** Il raggio **non viene riflesso** quando dovrebbe (total internal reflection).

**Soluzione:**
- Usare una **tolleranza** per `cosTheta`:
  ```csharp
  float cosThetaClamped = Math.Clamp(cosTheta, -1f, 1f);
  float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosThetaClamped * cosThetaClamped));
  bool cannotRefract = !ThinWalled && eta * sinTheta > 1f;
  ```

---

#### **3. `SampleGgxVndfAniso`: Possibile Campionamento di Direzioni Non Valide**
Nel tuo codice usi:
```csharp
Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, sp.AlphaX, sp.AlphaY, rnd1, rnd2);
```
**Problema:**
- Se `Vloc.Z` (la componente normale di `V` in tangent space) è **molto piccolo** (es. `V` quasi parallelo alla superficie), il **VNDF sampling** potrebbe generare `Hloc` con `Hloc.Z <= 0`, che porta a:
  - `L = reflect(-V, H)` con `L` **sotto la superficie** (e quindi scartato in `ScatterSpecular`).
  - **Aumento della varianza** (molti campioni scartati).

**Soluzione:**
- **Clampare `Vloc.Z`** a un valore minimo (es. `1e-4`):
  ```csharp
  Vector3 Vloc = frame.ToLocal(V);
  Vloc.Z = MathF.Max(Vloc.Z, 1e-4f); // Evita V quasi tangente
  Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, sp.AlphaX, sp.AlphaY, rnd1, rnd2);
  ```

---

#### **4. `EvalFresnel`: Thin-Film e Fresnel Schlick**
```csharp
private static Vector3 EvalFresnel(float cosTheta, Vector3 f0, in ShadingParams sp)
{
    if (sp.ThinFilmThicknessNm <= 0f)
        return FresnelSchlick(cosTheta, f0);
    return ThinFilm.Evaluate(cosTheta, sp.ThinFilmIor, sp.ThinFilmThicknessNm, f0);
}
```
**Problema:**
- **`ThinFilm.Evaluate`** (che non vedo nel tuo codice) deve essere **fisicamente corretto**.
- Se `ThinFilm.Evaluate` **non gestisce correttamente** il caso in cui `cosTheta` è **negativo** (raggio che esce dal materiale), potrebbe restituire valori sbagliati.
- **Esempio:** Se `cosTheta < 0` (raggio che esce), il thin-film Fresnel dovrebbe usare `|cosTheta|` e **invertire l’ordine degli indici di rifrazione** (eta1 ↔ eta2).

**Soluzione:**
- Assicurati che `ThinFilm.Evaluate` gestisca `cosTheta < 0`:
  ```csharp
  public static Vector3 Evaluate(float cosTheta, float filmIor, float thicknessNm, Vector3 substrateF0)
  {
      float absCosTheta = MathF.Abs(cosTheta);
      float eta1 = 1.0f; // Aria
      float eta2 = filmIor;
      float eta3 = 1.5f; // Substrato (es. vetro) - da derivare da substrateF0

      // Calcola F0 del film (eta1 ↔ eta2)
      float r12 = (eta1 - eta2) / (eta1 + eta2);
      float F0_film = r12 * r12;

      // Calcola F0 del substrato (eta2 ↔ eta3)
      float r23 = (eta2 - eta3) / (eta2 + eta3);
      float F0_substrate = r23 * r23;

      // Usa il modello di Belcour-Barla per thin-film
      return ThinFilmBelcourBarla(absCosTheta, thicknessNm, F0_film, F0_substrate, substrateF0);
  }
  ```

---

#### **5. `ScatterDiffuse`: Possibile `NaN` in `ssRaw`**
```csharp
float ssRaw = 1.25f * (fssI * fssO * (1f / (NdotV + NdotL + 0.001f) - 0.5f) + 0.5f);
float ss = Math.Clamp(ssRaw, 0f, 2f);
```
**Problema:**
- Se `NdotV + NdotL` è **molto piccolo** (es. `1e-6`), allora `1 / (NdotV + NdotL + 0.001f)` può essere **molto grande** (es. `~1000`), portando `ssRaw` a valori **estremi** (es. `1.25 * (1000 * ...)`).
- Anche se `Math.Clamp` evita `NaN`, il valore potrebbe essere **numericamente instabile**.

**Soluzione:**
- **Clampare `NdotV + NdotL`** a un valore minimo:
  ```csharp
  float denom = MathF.Max(NdotV + NdotL, 0.001f) + 0.001f;
  float ssRaw = 1.25f * (fssI * fssO * (1f / denom - 0.5f) + 0.5f);
  ```

---

### **🟡 Possibili Miglioramenti (Non Bug, ma Ottimizzazioni)**

#### **1. `EvalParams`: Evitare Valutazioni Ridondanti delle Texture**
Nel tuo codice, ogni parametro viene valutato **singolarmente**:
```csharp
float metallic = Metallic.Value(u, v, p, seed);
float roughness = Roughness.Value(u, v, p, seed);
...
```
**Problema:**
- Se `Metallic`, `Roughness`, ecc. sono **texture complesse** (es. noise, immagini), ogni `Value()` potrebbe essere **costoso**.
- **Soluzione:** Se le texture condividono lo stesso `(u, v, p, seed)`, potresti **valutarle in batch** (se possibile).

**Esempio:**
```csharp
// Se tutte le texture sono FloatTexture, potresti valutarle insieme
var (metallic, roughness, subsurface, ...) = FloatTexture.EvaluateAll(
    new[] { Metallic, Roughness, Subsurface, ... },
    u, v, p, seed
);
```

---

#### **2. `ScatterSpecular`: Evitare `Vector3.Normalize` Ridondanti**
Nel tuo codice:
```csharp
Vector3 Hraw = V + L;
float hLenSq = Hraw.LengthSquared();
if (hLenSq < 1e-14f) return Vector3.Zero;
Vector3 H = Hraw / MathF.Sqrt(hLenSq);
```
**Miglioramento:**
- Puoi **evitare la `sqrt`** se ti serve solo `H` per calcolare `NdotH` e `VdotH`:
  ```csharp
  Vector3 Hraw = V + L;
  float hLenSq = Hraw.LengthSquared();
  if (hLenSq < 1e-14f) return Vector3.Zero;
  float invHLen = 1f / MathF.Sqrt(hLenSq);
  Vector3 H = Hraw * invHLen; // Normalizza
  float NdotH = Vector3.Dot(N, Hraw) * invHLen; // Evita una seconda normalizzazione
  float VdotH = Vector3.Dot(V, Hraw) * invHLen;
  ```

---

#### **3. `ComputeF0`: Ottimizzare il Calcolo di `F0` per Metalli**
```csharp
Vector3 F0 = ComputeF0(baseCol, sp);
```
**Miglioramento:**
- Per i **metalli**, `F0 = baseColor` (nessun calcolo necessario).
- Per i **dielettrici**, `F0 = 0.04 * specular * (1 - specularTint) + specularTint * baseColor`.
- **Ottimizzazione:**
  ```csharp
  private static Vector3 ComputeF0(Vector3 baseCol, in ShadingParams sp)
  {
      if (sp.Metallic >= 0.99f) // Quasi metallo puro
          return baseCol;

      // Dielettrico
      float r = (sp.Ior - 1f) / (sp.Ior + 1f);
      float f0d = r * r;
      float scaledF0 = f0d * 2f * sp.Specular;

      float lum = MathUtils.Luminance(baseCol);
      Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
      Vector3 dielectricF0 = Vector3.Lerp(new Vector3(scaledF0), scaledF0 * tintCol, sp.SpecularTint);

      return Vector3.Lerp(dielectricF0, baseCol, sp.Metallic);
  }
  ```

---
---
---
## **🔵 Riepilogo: Bug e Miglioramenti**

| **Categoria**               | **Problema**                                                                 | **Soluzione**                                                                                     | **Priorità** |
|-----------------------------|-----------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------|--------------|
| **Thread-Safety**           | Caching di `EvalParams` non reentrant                                       | Aggiungere un contatore di profondità per evitare caching ricorsivo                          | ⚠️ Alta       |
| **Total Internal Reflection** | `sinTheta` calcolato male per `cosTheta` vicino a 1                        | Clampare `cosTheta` a `[-1, 1]` prima di calcolare `sinTheta`                                  | ⚠️ Alta       |
| **VNDF Sampling**           | `Vloc.Z` troppo piccolo → campioni invalidi                                | Clampare `Vloc.Z` a `1e-4`                                                                       | ⚠️ Media      |
| **Thin-Film Fresnel**       | `cosTheta < 0` non gestito correttamente                                  | Usare `|cosTheta|` e invertire `eta1` ↔ `eta2` in `ThinFilm.Evaluate`                          | ⚠️ Alta       |
| **Subsurface Scattering**   | `ssRaw` può essere numericamente instabile                                | Clampare `NdotV + NdotL` a un valore minimo                                                     | 🟡 Bassa       |
| **Ottimizzazione Texture**  | Valutazioni ridondanti delle texture                                       | Valutare le texture in batch (se possibile)                                                     | 🟡 Bassa       |
| **Ottimizzazione Normalize**| `Vector3.Normalize` ridondanti in `ScatterSpecular`                       | Usare `invHLen` per evitare una seconda normalizzazione                                       | 🟡 Bassa       |
| **Ottimizzazione F0**       | Calcolo di `F0` non ottimizzato per metalli                                 | Ritornare direttamente `baseCol` per `Metallic >= 0.99f`                                      | 🟡 Bassa       |

---
---
---
## **📌 Conclusione**
### **1. Importance Sampling per Sheen e Clearcoat**
- **Sheen (Charlie BRDF):**
  - Attualmente usi **cosine-weighted sampling**, che **non è ottimale** per il lobo Charlie (picco stretto e direzionale).
  - **Soluzione:** Implementare **importance sampling** usando la PDF del Charlie BRDF (derivabile dal paper di Estevez-Kulla 2017).
  - **Difficoltà:** Media (serve invertire la CDF del Charlie NDF, che non ha soluzione analitica chiusa).

- **Clearcoat:**
  - Usi già **VNDF sampling**, che è **ottimale** per GGX.
  - **Miglioramento:** Assicurati che la PDF tenga conto del **Fresnel** e del **geometry term** per una stima ancora più accurata.

---

### **2. Bug nel Tuo Codice**
Non ho trovato **bug gravi**, ma ci sono **alcuni punti critici** che potrebbero causare:
- **Comportamenti inattesi** in casi limite (es. `cosTheta` vicino a 1, `V` quasi tangente).
- **Instabilità numerica** (es. `ssRaw` troppo grande, `sinTheta` calcolato male).
- **Caching non reentrant** (potrebbe causare errori in thread riutilizzati).

**Consiglio:**
- **Correggi i punti critici** (soprattutto `cosTheta` clamping e caching reentrant).
- **Testa con scene estreme** (es. materiali con `ior = 10`, `roughness = 0`, `thinFilmThickness = 800nm`).

---
---
### **🔧 Prossimi Passi**
1. **Correggi i bug critici** (soprattutto `cosTheta` clamping e caching).
2. **Implementa l’importance sampling per il sheen** (se vuoi ridurre il rumore).
3. **Testa con scene di riferimento** (es. confronta con Arnold o RenderMan).
4. **Ottimizza ulteriormente** (es. batch texture evaluation, evitare normalizzazioni ridondanti).
