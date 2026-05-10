// Ecco un programma completo in C# .NET 10 che genera una scena YAML di un tempio
// romano dettagliato per il motore di rendering 3D-Ray.

// Il codice è progettato per sfruttare appieno la potenza procedurale del C# al
// fine di calcolare posizioni, griglie di colonne e profili matematici complessi
// (come le scanalature delle colonne tramite la primitiva extrusion), producendo
// un file YAML perfettamente formattato.

// Caratteristiche della scena generata:

// 1.  Primitiva extrusion: Usata per modellare il fusto scanalato delle colonne
//     (con profilo catmull_rom generato matematicamente per creare le scanalature
//     e parametro taper per l'entasi) e per i frontoni triangolari del tetto (con
//     profilo linear e rotazione).
// 2.  Template e Istanze: Un'intera colonna (base, fusto, capitello) è definita
//     come template e poi istanziata 34 volte lungo il perimetro.
// 3.  CSG (Operazioni Booleane): La "Cella" (stanza interna del tempio) è un
//     blocco di marmo scavato usando subtraction nidificate per rimuovere
//     l'interno e creare il varco della porta.
// 4.  Materiali PBR: Marmo Disney (con subsurface scattering), oro, pietra ruvida
//     per il terreno, e fuoco emissivo.
// 5.  Illuminazione e Volumetria: Cielo al tramonto (Golden Hour) con
//     illuminazione direzionale e sfere di fuoco emissive interne che partecipano
//     al NEE (Next Event Estimation).
// 6.  Camere Multiple: 4 angazioni distinte configurate e pronte per il render.

using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Generatore di scena 3D-Ray per un Tempio Romano.
/// Sfrutta primitive avanzate come Extrusion (per colonne scanalate e frontoni) e CSG.
/// </summary>
class Program
{
    static void Main()
    {
        // Imposta la localizzazione invariante per i numeri decimali (es. 1.5 invece di 1,5)
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string outputPath = ResolveOutputPath();
        StringBuilder yaml = new StringBuilder();

        yaml.AppendLine("""
        # =========================================================================
        # SCENA: Tempio Romano Classico (Dimensionamento Dinamico)
        # GENERATO TRAMITE: C# .NET 10 Procedural Generator
        # =========================================================================

        world:
          ground:
            type: "infinite_plane"
            point: [0, 0, 0]
            normal: [0, 1, 0]
            material: "ground_stone"
          sky:
            type: "gradient"
            zenith_color: [0.15, 0.25, 0.60]
            horizon_color: [0.85, 0.45, 0.20]
            ground_color: [0.20, 0.15, 0.12]
            sun:
              direction: [-0.8, -0.4, 0.5]
              color: [1.0, 0.85, 0.70]
              intensity: 8.0
              size: 2.0
              falloff: 32.0

        cameras:
          - name: "front_dramatic"
            position: [0, 2.5, -30]
            look_at: [0, 8.4, 0]
            fov: 55
            aperture: 0.05
            focal_dist: 28
          - name: "side_perspective"
            position: [-30, 12, -31]
            look_at: [0, 5.5, -5]
            fov: 44
            aperture: 0.02
            focal_dist: 40
          - name: "interior"
            position: [0, 4, -8]
            look_at: [0, 4, 10]
            fov: 60
            aperture: 0.0
            focal_dist: 10
          - name: "top_isometric"
            position: [-35, 45, -35]
            look_at: [0, 0, 0]
            fov: 35
            aperture: 0.0
            focal_dist: 60

        lights:
          - type: "point"
            position: [-3, 3.5, 2]
            color: [1.0, 0.5, 0.1]
            intensity: 15.0
            soft_radius: 0.2
          - type: "point"
            position: [3, 3.5, 2]
            color: [1.0, 0.5, 0.1]
            intensity: 15.0
            soft_radius: 0.2

        materials:
          - id: "white_marble"
            type: "disney"
            color: [0.9, 0.88, 0.85]
            roughness: 0.2
            subsurface: 0.3
            subsurface_color: [0.95, 0.9, 0.8]
          - id: "dark_marble"
            type: "disney"
            color: [0.2, 0.2, 0.22]
            roughness: 0.3
          - id: "gold"
            type: "metal"
            color: [1.0, 0.85, 0.3]
            fuzz: 0.1
          - id: "roof_tiles"
            type: "lambertian"
            color: [0.6, 0.25, 0.15]
          - id: "ground_stone"
            type: "lambertian"
            color: [0.4, 0.38, 0.35]
          - id: "fire"
            type: "emissive"
            color: [1.0, 0.6, 0.1]
            intensity: 8.0

        templates:
        """);

        // Generazione del profilo scanalato (fluted) per l'estrusione della colonna
        int lobes = 16;
        double rOuter = 0.8;
        double rInner = 0.65;
        StringBuilder profileBuilder = new StringBuilder();
        for (int i = 0; i < lobes * 2; i++)
        {
            double angle = (Math.PI * 2 * i) / (lobes * 2);
            double r = (i % 2 == 0) ? rOuter : rInner;
            double x = Math.Cos(angle) * r;
            double z = Math.Sin(angle) * r;
            profileBuilder.AppendLine($"          - [{x:F3}, {z:F3}]");
        }

        yaml.AppendLine($$"""
          - name: "roman_column"
            material: "white_marble"
            children:
              - type: "box"
                scale: [2.2, 0.4, 2.2]
                translate: [0, 0.2, 0]
              - type: "cylinder"
                center: [0, 0.4, 0]
                radius: 1.0
                height: 0.3
              - type: "cylinder"
                center: [0, 0.7, 0]
                radius: 0.9
                height: 0.2
              - type: "extrusion"
                profile_type: "catmull_rom"
                height: 7.5
                taper: 0.85
                curve_samples: 24
                translate: [0, 0.9, 0]
                caps: "both"
                profile:
        {{profileBuilder.ToString().TrimEnd()}}
              - type: "cylinder"
                center: [0, 8.4, 0]
                radius: 0.85
                height: 0.3
              - type: "box"
                scale: [2.2, 0.4, 2.2]
                translate: [0, 8.7, 0]
          
          - name: "brazier"
            children:
              - type: "cylinder"
                center: [0, 0, 0]
                radius: 0.3
                height: 1.0
                material: "dark_marble"
              - type: "cone"
                center: [0, 1.0, 0]
                radius: 0.1
                top_radius: 0.6
                height: 0.5
                material: "gold"
              - type: "sphere"
                center: [0, 1.7, 0]
                radius: 0.4
                material: "fire"
        """);

        yaml.AppendLine("\nentities:");

        // =========================================================================
        // CALCOLI DINAMICI DELLA GEOMETRIA (Tutto parte dalle colonne)
        // =========================================================================
        int numColX = 6;
        int numColZ = 13;  // Classico rapporto 6x13
        double spacingX = 3.2;
        double spacingZ = 3.2;

        // 1. Ingombro esatto dei centri delle colonne
        double colExtentX = (numColX - 1) * spacingX; // 16.0
        double colExtentZ = (numColZ - 1) * spacingZ; // 38.4
        
        // Offset iniziale per centrare perfettamente la griglia sull'origine (0,0)
        double startX = -colExtentX / 2.0;
        double startZ = -colExtentZ / 2.0;

        double baseHeight = 1.5; // Altezza del crepidoma (3 scalini da 0.5)
        double colHeight = 8.9;  // Altezza definita nel template della colonna

        // 2. CREPIDOMA (Scalinata che abbraccia perfettamente la griglia calcolata)
        double marginTopStep = 1.2; // Margine tra il centro colonna e il bordo del gradino alto
        double topStepX = colExtentX + marginTopStep * 2;
        double topStepZ = colExtentZ + marginTopStep * 2;

        for (int i = 0; i < 3; i++)
        {
            double stepH = 0.5;
            double shrink = (2 - i) * 0.8; // Il gradino più basso è il più largo
            double stepX = topStepX + shrink * 2;
            double stepZ = topStepZ + shrink * 2;
            
            double posY = stepH / 2.0 + (i * stepH);
            yaml.AppendLine($$"""
              - name: "step_{{i}}"
                type: "box"
                scale: [{{stepX:F2}}, {{stepH:F2}}, {{stepZ:F2}}]
                translate: [0, {{posY:F2}}, 0]
                material: "white_marble"
            """);
        }

        // 3. ISTANZE DELLE COLONNE
        for (int x = 0; x < numColX; x++)
        {
            for (int z = 0; z < numColZ; z++)
            {
                if (x == 0 || x == numColX - 1 || z == 0 || z == numColZ - 1)
                {
                    double px = startX + (x * spacingX);
                    double pz = startZ + (z * spacingZ);
                    yaml.AppendLine($$"""
                      - name: "col_{{x}}_{{z}}"
                        type: "instance"
                        template: "roman_column"
                        translate: [{{px:F2}}, {{baseHeight:F2}}, {{pz:F2}}]
                    """);
                }
            }
        }

        // 4. ARCHITRAVE E FREGIO (Dimensionati sulla griglia colonne)
        double entabBaseY = baseHeight + colHeight;
        
        double architraveX = colExtentX + 2.0;
        double architraveZ = colExtentZ + 2.0;
        double architraveH = 1.2;
        
        double friezeX = architraveX - 0.4;
        double friezeZ = architraveZ - 0.4;
        double friezeH = 1.0;

        double corniceX = architraveX + 1.2;
        double corniceZ = architraveZ + 1.2;
        double corniceH = 0.5;

        yaml.AppendLine($$"""
          - name: "architrave"
            type: "box"
            scale: [{{architraveX:F2}}, {{architraveH:F2}}, {{architraveZ:F2}}]
            translate: [0, {{entabBaseY + (architraveH/2):F2}}, 0]
            material: "white_marble"
          - name: "frieze"
            type: "box"
            scale: [{{friezeX:F2}}, {{friezeH:F2}}, {{friezeZ:F2}}]
            translate: [0, {{entabBaseY + architraveH + (friezeH/2):F2}}, 0]
            material: "gold"
          - name: "cornice"
            type: "box"
            scale: [{{corniceX:F2}}, {{corniceH:F2}}, {{corniceZ:F2}}]
            translate: [0, {{entabBaseY + architraveH + friezeH + (corniceH/2):F2}}, 0]
            material: "white_marble"
        """);

        // 5. FRONTONI TRAMITE EXTRUSION (Centrati alle estremità della Cornice)
        double pedBaseY = entabBaseY + architraveH + friezeH + corniceH;
        double pedWidth = corniceX;
        double pedHeight = 4.2;
        double pedDepth = 2.0;

        // La primitiva extrusion estrude lungo il suo Y locale. Ruotandola di 90 su X, l'asse 
        // di estrusione punta in direzione del -Z globale.
        double frontPedZ = -(corniceZ / 2.0) + pedDepth; // Si ferma esattamente a filo sul davanti
        double backPedZ = (corniceZ / 2.0);              // Si ferma esattamente a filo sul retro

        yaml.AppendLine($$"""
          - name: "front_pediment"
            type: "extrusion"
            profile_type: "linear"
            height: {{pedDepth:F2}}
            translate: [0, {{pedBaseY:F2}}, {{frontPedZ:F2}}]
            rotate: [90, 0, 0]
            material: "white_marble"
            caps: "both"
            profile:
              - [{{-pedWidth/2:F2}}, 0.0]
              - [{{pedWidth/2:F2}}, 0.0]
              - [0.0, {{-pedHeight:F2}}] # Negativo su Y-profilo compensa la rotazione
              
          - name: "back_pediment"
            type: "extrusion"
            profile_type: "linear"
            height: {{pedDepth:F2}}
            translate: [0, {{pedBaseY:F2}}, {{backPedZ:F2}}]
            rotate: [90, 0, 0]
            material: "white_marble"
            caps: "both"
            profile:
              - [{{-pedWidth/2:F2}}, 0.0]
              - [{{pedWidth/2:F2}}, 0.0]
              - [0.0, {{-pedHeight:F2}}]
        """);

        // 6. TETTO (Calcolo di Pendenza e Posizionamento)
        double slopeLength = Math.Sqrt(Math.Pow(pedWidth / 2.0, 2) + Math.Pow(pedHeight, 2));
        double roofW = slopeLength + 0.8; // Lieve sporgenza laterale
        double roofL = corniceZ + 0.6;    // Lieve sporgenza frontale/posteriore
        double roofH = 0.4;
        
        // Calcolo dell'angolo di pendenza in gradi
        double roofAngle = Math.Atan2(pedHeight, pedWidth / 2.0) * (180.0 / Math.PI);
        
        // Calcolo della posizione tramite la normale della superficie
        double normX = Math.Sin(roofAngle * Math.PI / 180.0);
        double normY = Math.Cos(roofAngle * Math.PI / 180.0);
        
        double roofRightX = (pedWidth / 4.0) + normX * (roofH / 2.0);
        double roofRightY = pedBaseY + (pedHeight / 2.0) + normY * (roofH / 2.0);

        yaml.AppendLine($$"""
          - name: "roof_right"
            type: "box"
            scale: [{{roofW:F2}}, {{roofH:F2}}, {{roofL:F2}}]
            translate: [{{roofRightX:F2}}, {{roofRightY:F2}}, 0]
            rotate: [0, 0, {{-roofAngle:F2}}]
            material: "roof_tiles"
            
          - name: "roof_left"
            type: "box"
            scale: [{{roofW:F2}}, {{roofH:F2}}, {{roofL:F2}}]
            translate: [{{-roofRightX:F2}}, {{roofRightY:F2}}, 0]
            rotate: [0, 0, {{roofAngle:F2}}]
            material: "roof_tiles"
        """);

        // 7. CELLA CENTRALE (Stanza scavata col CSG)
        double cellaW = 8.0;
        double cellaL = 26.0;
        double cellaCenterZ = 2.0; // Spostata un po' all'indietro per il pronao
        double cellaCenterY = baseHeight + (colHeight / 2.0);

        double wallThick = 0.8;
        double innerW = cellaW - (wallThick * 2);
        double innerL = cellaL - (wallThick * 2);

        // Varco della porta posizionato per "sfondare" la parete frontale della cella
        double doorW = 3.6;
        double doorH = 6.5;
        double doorZ = cellaCenterZ - (cellaL / 2.0);

        yaml.AppendLine($$"""
          - name: "cella_walls"
            type: "csg"
            operation: "subtraction"
            material: "white_marble"
            left:
              type: "box"
              scale: [{{cellaW:F2}}, {{colHeight:F2}}, {{cellaL:F2}}]
              translate: [0, {{cellaCenterY:F2}}, {{cellaCenterZ:F2}}]
            right:
              type: "csg"
              operation: "union"
              left:
                type: "box"
                scale: [{{innerW:F2}}, {{colHeight + 0.2:F2}}, {{innerL:F2}}] # +0.2 per sfondare tetto/pavimento
                translate: [0, {{cellaCenterY:F2}}, {{cellaCenterZ:F2}}]
              right:
                type: "box"
                scale: [{{doorW:F2}}, {{doorH:F2}}, 4.0] # Spessore passante
                translate: [0, {{baseHeight + (doorH/2):F2}}, {{doorZ:F2}}]
        """);

        // 8. BRACIERI INTERNI
        yaml.AppendLine("""
          - name: "brazier_left"
            type: "instance"
            template: "brazier"
            translate: [-2.5, 1.5, 2]
            
          - name: "brazier_right"
            type: "instance"
            template: "brazier"
            translate: [2.5, 1.5, 2]
        """);

        // Scrivi il file su disco
        File.WriteAllText(outputPath, yaml.ToString());
        Console.WriteLine($"Scena generata con successo: {Path.GetFullPath(outputPath)}");
        Console.WriteLine("Puoi ora renderizzare usando: dotnet run -- -i tempio_romano.yaml -c front_dramatic");
    }

    // Walk up from the running binary until we find the repo root (identified by
    // the presence of a `scenes/` directory), then point at scenes/chess.yaml.
    // Robust whether `dotnet run` is invoked from the repo root or from the
    // project directory.
    static string ResolveOutputPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "scenes")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate repo root (no 'scenes/' directory found above the binary).");
        return Path.Combine(dir.FullName, "scenes", "tempio-romano.yaml");
    }
}
