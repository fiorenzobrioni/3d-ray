using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace BigBenGen
{
    class Program
    {
        static int entityCount = 0;

        static void Main(string[] args)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# Ray-Tracing Scene - Elizabeth Tower (Big Ben) - London");
            sb.AppendLine("# Procedurally generated with thousands of fine primitives");
            sb.AppendLine();

            // ── World ──
            sb.AppendLine("world:");
            sb.AppendLine("  ambient_light: [0.06, 0.06, 0.09]");
            sb.AppendLine("  background: [0.18, 0.25, 0.50]  # Twilight London sky");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"pavement\"");
            sb.AppendLine("    y: 0.0");
            sb.AppendLine();

            // ── Camera ──
            sb.AppendLine("camera:");
            sb.AppendLine("  position: [18, 3.5, -35]   # Low dramatic angle from across the Thames");
            sb.AppendLine("  look_at: [0, 32, 0]");
            sb.AppendLine("  fov: 50");
            sb.AppendLine("  aperture: 0.08");
            sb.AppendLine("  focal_dist: 42.0");
            sb.AppendLine();

            // ── Materials ──
            sb.AppendLine("materials:");
            WriteMat(sb, "pavement",      "lambertian", 0.35f, 0.33f, 0.30f);
            WriteMat(sb, "limestone",     "lambertian", 0.82f, 0.78f, 0.68f);
            WriteMat(sb, "limestone_dark","lambertian", 0.65f, 0.60f, 0.50f);
            WriteMat(sb, "stone_detail",  "lambertian", 0.75f, 0.72f, 0.65f);
            WriteMat(sb, "clock_white",   "lambertian", 0.95f, 0.93f, 0.88f);
            WriteMat(sb, "clock_rim",     "metal",      0.85f, 0.75f, 0.20f, 0.15f);
            WriteMat(sb, "clock_hand",    "metal",      0.10f, 0.10f, 0.10f, 0.3f);
            WriteMat(sb, "iron_dark",     "metal",      0.18f, 0.16f, 0.14f, 0.5f);
            WriteMat(sb, "gold_trim",     "metal",      0.90f, 0.75f, 0.15f, 0.1f);
            WriteMat(sb, "copper_roof",   "metal",      0.25f, 0.45f, 0.30f, 0.25f);
            WriteMat(sb, "glass_window",  "dielectric", 1.5f);
            WriteMat(sb, "warm_light",    "lambertian", 1.0f, 0.90f, 0.60f);
            WriteMat(sb, "dark_recess",   "lambertian", 0.12f, 0.10f, 0.08f);
            WriteMat(sb, "thames_water",  "metal",      0.12f, 0.18f, 0.22f, 0.05f);
            WriteMat(sb, "bridge_stone",  "lambertian", 0.55f, 0.52f, 0.48f);
            WriteMat(sb, "red_bus",       "lambertian", 0.80f, 0.10f, 0.08f);
            WriteMat(sb, "fence_iron",    "metal",      0.15f, 0.14f, 0.13f, 0.4f);
            sb.AppendLine();

            // ── Lights ──
            sb.AppendLine("lights:");
            sb.AppendLine("  - type: \"directional\"");
            sb.AppendLine("    direction: [-0.3, -0.8, 0.2]");
            sb.AppendLine("    color: [1.0, 0.85, 0.65]");
            sb.AppendLine("    intensity: 0.7");
            sb.AppendLine();
            sb.AppendLine("  - type: \"directional\"");
            sb.AppendLine("    direction: [0.4, -0.4, -0.6]");
            sb.AppendLine("    color: [0.40, 0.45, 0.65]");
            sb.AppendLine("    intensity: 0.20");
            sb.AppendLine();
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [0, 55, -5]");
            sb.AppendLine("    color: [1.0, 0.95, 0.80]");
            sb.AppendLine("    intensity: 300.0");
            sb.AppendLine();
            sb.AppendLine("  - type: \"point\"         # Ayrton Light glow");
            sb.AppendLine("    position: [0, 62, 0]");
            sb.AppendLine("    color: [1.0, 0.92, 0.55]");
            sb.AppendLine("    intensity: 120.0");
            sb.AppendLine();

            sb.AppendLine("entities:");

            // ============================================================
            //  THAMES WATER
            // ============================================================
            AddBox(sb, "thames_water", 0, -0.3f, 15, 60, 0.3f, 25, "thames_water");

            // ============================================================
            //  WESTMINSTER BRIDGE FRAGMENT
            // ============================================================
            AddBox(sb, "bridge_deck", 0, 0.5f, -18, 20, 0.5f, 3, "bridge_stone");
            AddBox(sb, "bridge_wall_l", -19, 1.2f, -18, 1, 0.7f, 3, "bridge_stone");
            AddBox(sb, "bridge_wall_r", 19, 1.2f, -18, 1, 0.7f, 3, "bridge_stone");
            // Bridge arches (decorative pillars)
            for (int i = 0; i < 6; i++)
            {
                float bx = -15 + i * 6;
                AddCyl(sb, $"bridge_pier_{i}", bx, -0.5f, -18, 1.2f, 1.5f, "bridge_stone");
            }

            // ============================================================
            //  EMBANKMENT & SURROUNDINGS
            // ============================================================
            AddBox(sb, "embankment", 0, 0.3f, -8, 30, 0.3f, 8, "pavement");

            // Iron fence along embankment
            for (int i = 0; i < 40; i++)
            {
                float fx = -20 + i * 1.0f;
                AddCyl(sb, $"fence_post_{i}", fx, 0.6f, -14.5f, 0.04f, 1.0f, "fence_iron");
                // Horizontal rail
                if (i < 39)
                {
                    AddBox(sb, $"fence_rail_{i}", fx + 0.5f, 1.45f, -14.5f, 0.5f, 0.02f, 0.02f, "fence_iron");
                    AddBox(sb, $"fence_rail_b_{i}", fx + 0.5f, 0.9f, -14.5f, 0.5f, 0.02f, 0.02f, "fence_iron");
                }
            }

            // Lamp posts
            for (int i = 0; i < 5; i++)
            {
                float lx = -16 + i * 8;
                BuildLampPost(sb, $"lamp_{i}", lx, 0.6f, -13);
            }

            // Red double-decker bus (simplified silhouette)
            AddBox(sb, "bus_body", -10, 1.8f, -10, 2.5f, 1.5f, 1.0f, "red_bus");
            AddBox(sb, "bus_upper", -10, 3.8f, -10, 2.5f, 1.0f, 1.0f, "red_bus");
            AddCyl(sb, "bus_wheel_fl", -12, 0.6f, -9.2f, 0.4f, 0.2f, "iron_dark");
            AddCyl(sb, "bus_wheel_fr", -12, 0.6f, -10.8f, 0.4f, 0.2f, "iron_dark");
            AddCyl(sb, "bus_wheel_rl", -8, 0.6f, -9.2f, 0.4f, 0.2f, "iron_dark");
            AddCyl(sb, "bus_wheel_rr", -8, 0.6f, -10.8f, 0.4f, 0.2f, "iron_dark");

            // ============================================================
            //  TOWER BASE / PLATFORM
            // ============================================================
            BuildPlatform(sb);

            // ============================================================
            //  MAIN TOWER SHAFT
            // ============================================================
            BuildMainShaft(sb);

            // ============================================================
            //  CLOCK SECTION
            // ============================================================
            BuildClockSection(sb);

            // ============================================================
            //  BELFRY LEVEL
            // ============================================================
            BuildBelfry(sb);

            // ============================================================
            //  ROOF & SPIRE
            // ============================================================
            BuildRoofAndSpire(sb);

            // ── Write Output ──
            string outPath = Path.Combine("..", "..", "..", "scenes", "big-ben.yaml");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Big Ben scene generated: {entityCount} entities → {outPath}");
        }

        // ================================================================
        //  TOWER SECTIONS
        // ================================================================

        static void BuildPlatform(StringBuilder sb)
        {
            // Three-stepped stone platform
            AddBox(sb, "platform_3", 0, 0.25f, 0, 8.0f, 0.25f, 8.0f, "limestone_dark");
            AddBox(sb, "platform_2", 0, 0.75f, 0, 7.5f, 0.25f, 7.5f, "limestone_dark");
            AddBox(sb, "platform_1", 0, 1.25f, 0, 7.0f, 0.25f, 7.0f, "limestone_dark");

            // Wide entrance arch (front, -Z side)
            AddBox(sb, "entrance_recess", 0, 3.5f, -6.55f, 2.5f, 2.5f, 0.5f, "dark_recess");
            AddBox(sb, "entrance_frame_l", -2.7f, 3.5f, -6.6f, 0.3f, 2.8f, 0.4f, "limestone");
            AddBox(sb, "entrance_frame_r", 2.7f, 3.5f, -6.6f, 0.3f, 2.8f, 0.4f, "limestone");
            AddBox(sb, "entrance_frame_top", 0, 6.1f, -6.6f, 3.0f, 0.3f, 0.4f, "limestone");
            // Pointed arch over entrance
            AddSph(sb, "entrance_arch_sph", 0, 6.3f, -6.7f, 1.5f, "limestone");
        }

        static void BuildMainShaft(StringBuilder sb)
        {
            float baseY = 1.5f;
            float shaftWidth = 5.5f;

            // Main shaft body in sections with stone bands
            for (int section = 0; section < 6; section++)
            {
                float y = baseY + section * 5.5f;
                float h = 5.5f;
                string mat = (section % 2 == 0) ? "limestone" : "limestone";
                AddBox(sb, $"shaft_sec_{section}", 0, y + h / 2, 0, shaftWidth, h / 2, shaftWidth, mat);

                // Horizontal stone band at top of each section
                AddBox(sb, $"shaft_band_{section}", 0, y + h, 0,
                       shaftWidth + 0.15f, 0.15f, shaftWidth + 0.15f, "stone_detail");

                // Corner pilasters for each section (8 pilasters: 4 corners × 2 per corner)
                float pw = 0.25f;
                float pd = 0.3f;
                float[] cx = { -shaftWidth, shaftWidth, -shaftWidth, shaftWidth };
                float[] cz = { -shaftWidth, -shaftWidth, shaftWidth, shaftWidth };
                for (int c = 0; c < 4; c++)
                {
                    AddBox(sb, $"pilaster_{section}_{c}", cx[c], y + h / 2, cz[c],
                           pw, h / 2, pd, "stone_detail");
                }

                // Gothic windows on each face (front, back, left, right)
                BuildShaftWindows(sb, section, y, h, shaftWidth);
            }
        }

        static void BuildShaftWindows(StringBuilder sb, int section, float y, float h, float w)
        {
            float winH = 3.0f;
            float winW = 1.0f;
            float winY = y + h / 2 - 0.5f;

            // Front face (-Z): 2 windows
            for (int i = 0; i < 2; i++)
            {
                float wx = -1.5f + i * 3.0f;
                AddBox(sb, $"win_f_{section}_{i}", wx, winY, -(w + 0.05f),
                       winW / 2, winH / 2, 0.15f, "dark_recess");
                // Window frame
                AddBox(sb, $"wfr_f_{section}_{i}_l", wx - winW / 2 - 0.08f, winY, -(w + 0.06f),
                       0.08f, winH / 2 + 0.15f, 0.1f, "stone_detail");
                AddBox(sb, $"wfr_f_{section}_{i}_r", wx + winW / 2 + 0.08f, winY, -(w + 0.06f),
                       0.08f, winH / 2 + 0.15f, 0.1f, "stone_detail");
                AddBox(sb, $"wfr_f_{section}_{i}_t", wx, winY + winH / 2 + 0.1f, -(w + 0.06f),
                       winW / 2 + 0.16f, 0.1f, 0.1f, "stone_detail");
                // Pointed arch cap
                AddSph(sb, $"warch_f_{section}_{i}", wx, winY + winH / 2 + 0.1f, -(w + 0.08f),
                       0.5f, "stone_detail");
            }

            // Back face (+Z): 2 windows
            for (int i = 0; i < 2; i++)
            {
                float wx = -1.5f + i * 3.0f;
                AddBox(sb, $"win_b_{section}_{i}", wx, winY, w + 0.05f,
                       winW / 2, winH / 2, 0.15f, "dark_recess");
                AddBox(sb, $"wfr_b_{section}_{i}_l", wx - winW / 2 - 0.08f, winY, w + 0.06f,
                       0.08f, winH / 2 + 0.15f, 0.1f, "stone_detail");
                AddBox(sb, $"wfr_b_{section}_{i}_r", wx + winW / 2 + 0.08f, winY, w + 0.06f,
                       0.08f, winH / 2 + 0.15f, 0.1f, "stone_detail");
                AddBox(sb, $"wfr_b_{section}_{i}_t", wx, winY + winH / 2 + 0.1f, w + 0.06f,
                       winW / 2 + 0.16f, 0.1f, 0.1f, "stone_detail");
                AddSph(sb, $"warch_b_{section}_{i}", wx, winY + winH / 2 + 0.1f, w + 0.08f,
                       0.5f, "stone_detail");
            }

            // Left face (-X): 2 windows
            for (int i = 0; i < 2; i++)
            {
                float wz = -1.5f + i * 3.0f;
                AddBox(sb, $"win_l_{section}_{i}", -(w + 0.05f), winY, wz,
                       0.15f, winH / 2, winW / 2, "dark_recess");
                AddBox(sb, $"wfr_l_{section}_{i}_l", -(w + 0.06f), winY, wz - winW / 2 - 0.08f,
                       0.1f, winH / 2 + 0.15f, 0.08f, "stone_detail");
                AddBox(sb, $"wfr_l_{section}_{i}_r", -(w + 0.06f), winY, wz + winW / 2 + 0.08f,
                       0.1f, winH / 2 + 0.15f, 0.08f, "stone_detail");
                AddBox(sb, $"wfr_l_{section}_{i}_t", -(w + 0.06f), winY + winH / 2 + 0.1f, wz,
                       0.1f, 0.1f, winW / 2 + 0.16f, "stone_detail");
                AddSph(sb, $"warch_l_{section}_{i}", -(w + 0.08f), winY + winH / 2 + 0.1f, wz,
                       0.5f, "stone_detail");
            }

            // Right face (+X): 2 windows
            for (int i = 0; i < 2; i++)
            {
                float wz = -1.5f + i * 3.0f;
                AddBox(sb, $"win_r_{section}_{i}", w + 0.05f, winY, wz,
                       0.15f, winH / 2, winW / 2, "dark_recess");
                AddBox(sb, $"wfr_r_{section}_{i}_l", w + 0.06f, winY, wz - winW / 2 - 0.08f,
                       0.1f, winH / 2 + 0.15f, 0.08f, "stone_detail");
                AddBox(sb, $"wfr_r_{section}_{i}_r", w + 0.06f, winY, wz + winW / 2 + 0.08f,
                       0.1f, winH / 2 + 0.15f, 0.08f, "stone_detail");
                AddBox(sb, $"wfr_r_{section}_{i}_t", w + 0.06f, winY + winH / 2 + 0.1f, wz,
                       0.1f, 0.1f, winW / 2 + 0.16f, "stone_detail");
                AddSph(sb, $"warch_r_{section}_{i}", w + 0.08f, winY + winH / 2 + 0.1f, wz,
                       0.5f, "stone_detail");
            }
        }

        static void BuildClockSection(StringBuilder sb)
        {
            float baseY = 34.5f; // top of main shaft
            float w = 6.0f;
            float h = 6.0f;

            // Clock section body
            AddBox(sb, "clock_body", 0, baseY + h / 2, 0, w, h / 2, w, "limestone");

            // Decorative cornice below clock section
            AddBox(sb, "clock_cornice_bot", 0, baseY, 0, w + 0.4f, 0.3f, w + 0.4f, "stone_detail");
            // Cornice above clock section
            AddBox(sb, "clock_cornice_top", 0, baseY + h, 0, w + 0.4f, 0.3f, w + 0.4f, "stone_detail");

            // Gold trim bands
            AddBox(sb, "gold_band_bot", 0, baseY + 0.3f, 0, w + 0.2f, 0.1f, w + 0.2f, "gold_trim");
            AddBox(sb, "gold_band_top", 0, baseY + h - 0.3f, 0, w + 0.2f, 0.1f, w + 0.2f, "gold_trim");

            // === CLOCK FACES (4 sides) ===
            float clockR = 2.5f;
            float clockY = baseY + h / 2;

            // Front (-Z)
            BuildClockFace(sb, "cf_front", 0, clockY, -(w + 0.1f), 0, 0, -1, clockR);
            // Back (+Z)
            BuildClockFace(sb, "cf_back", 0, clockY, w + 0.1f, 0, 0, 1, clockR);
            // Left (-X)
            BuildClockFace(sb, "cf_left", -(w + 0.1f), clockY, 0, -1, 0, 0, clockR);
            // Right (+X)
            BuildClockFace(sb, "cf_right", w + 0.1f, clockY, 0, 1, 0, 0, clockR);

            // Corner pilasters on clock section
            float[] cpx = { -w, w, -w, w };
            float[] cpz = { -w, -w, w, w };
            for (int c = 0; c < 4; c++)
            {
                AddBox(sb, $"clock_pilaster_{c}", cpx[c], baseY + h / 2, cpz[c],
                       0.35f, h / 2, 0.35f, "stone_detail");
                // Small pinnacle on top of each pilaster
                AddBox(sb, $"clock_pin_{c}", cpx[c], baseY + h + 0.6f, cpz[c],
                       0.15f, 0.6f, 0.15f, "limestone");
                AddSph(sb, $"clock_pin_ball_{c}", cpx[c], baseY + h + 1.3f, cpz[c],
                       0.12f, "gold_trim");
            }
        }

        static void BuildClockFace(StringBuilder sb, string pfx, float x, float y, float z,
                                    float nx, float ny, float nz, float radius)
        {
            // Determine offset direction for depth
            float dx = nx * 0.01f;
            float dy = ny * 0.01f;
            float dz = nz * 0.01f;

            bool isFrontBack = (nz != 0);

            if (isFrontBack)
            {
                // Clock dial (flat cylinder) - we approximate with a thin box
                // Gold rim ring - approximate with slightly larger box behind
                AddBox(sb, $"{pfx}_rim", x, y, z + nz * 0.05f,
                       radius + 0.2f, radius + 0.2f, 0.1f, "clock_rim");
                AddBox(sb, $"{pfx}_face", x, y, z + nz * 0.1f,
                       radius, radius, 0.08f, "clock_white");

                // Hour markers (12 positions around the face)
                for (int h = 0; h < 12; h++)
                {
                    double ang = h * Math.PI / 6.0;
                    float mx = x + (float)Math.Sin(ang) * (radius - 0.35f);
                    float my = y + (float)Math.Cos(ang) * (radius - 0.35f);
                    AddBox(sb, $"{pfx}_mark_{h}", mx, my, z + nz * 0.15f,
                           0.08f, 0.2f, 0.05f, "clock_hand");
                }

                // Hour hand — points to 12 o'clock (vertical up from center)
                float hhLen = radius * 0.5f;
                AddBox(sb, $"{pfx}_hr_hand", x, y + hhLen / 2, z + nz * 0.18f,
                       0.1f, hhLen / 2, 0.04f, "clock_hand");

                // Minute hand — points to 3 o'clock (horizontal right from center)
                float mhLen = radius * 0.75f;
                AddBox(sb, $"{pfx}_min_hand", x + mhLen / 2, y, z + nz * 0.2f,
                       mhLen / 2, 0.06f, 0.03f, "clock_hand");

                // Center hub
                AddCyl(sb, $"{pfx}_hub", x, y - 0.15f, z + nz * 0.22f, 0.2f, 0.3f, "gold_trim");
            }
            else
            {
                // Left/Right faces (X-normal)
                AddBox(sb, $"{pfx}_rim", x + nx * 0.05f, y, z,
                       0.1f, radius + 0.2f, radius + 0.2f, "clock_rim");
                AddBox(sb, $"{pfx}_face", x + nx * 0.1f, y, z,
                       0.08f, radius, radius, "clock_white");

                for (int h = 0; h < 12; h++)
                {
                    double ang = h * Math.PI / 6.0;
                    float mz = z + (float)Math.Sin(ang) * (radius - 0.35f);
                    float my = y + (float)Math.Cos(ang) * (radius - 0.35f);
                    AddBox(sb, $"{pfx}_mark_{h}", x + nx * 0.15f, my, mz,
                           0.05f, 0.2f, 0.08f, "clock_hand");
                }

                // Hour hand — points to 12 o'clock (vertical up from center)
                float hhL = radius * 0.5f;
                AddBox(sb, $"{pfx}_hr_hand", x + nx * 0.18f, y + hhL / 2, z,
                       0.04f, hhL / 2, 0.1f, "clock_hand");

                // Minute hand — points to 3 o'clock (horizontal along Z from center)
                float mL = radius * 0.75f;
                AddBox(sb, $"{pfx}_min_hand", x + nx * 0.2f, y, z + mL / 2,
                       0.03f, 0.06f, mL / 2, "clock_hand");

                AddCyl(sb, $"{pfx}_hub", x + nx * 0.22f, y - 0.15f, z, 0.2f, 0.3f, "gold_trim");
            }
        }

        static void BuildBelfry(StringBuilder sb)
        {
            float baseY = 40.8f; // above clock section
            float w = 5.8f;
            float h = 7.0f;

            // Belfry body
            AddBox(sb, "belfry_body", 0, baseY + h / 2, 0, w, h / 2, w, "limestone");

            // Belfry cornice
            AddBox(sb, "belfry_cornice", 0, baseY + h, 0, w + 0.5f, 0.3f, w + 0.5f, "stone_detail");
            AddBox(sb, "belfry_cornice_b", 0, baseY, 0, w + 0.3f, 0.25f, w + 0.3f, "stone_detail");

            // Open arches on each face (the sound openings for the bell)
            // Front arches
            BuildBelfryArches(sb, "belf_f", 0, baseY, -(w + 0.05f), true, h);
            // Back arches
            BuildBelfryArches(sb, "belf_b", 0, baseY, w + 0.05f, true, h);
            // Left arches
            BuildBelfryArches(sb, "belf_l", -(w + 0.05f), baseY, 0, false, h);
            // Right arches
            BuildBelfryArches(sb, "belf_r", w + 0.05f, baseY, 0, false, h);

            // The bell inside (simplified)
            AddCyl(sb, "big_ben_bell", 0, baseY + 1.0f, 0, 2.0f, 2.5f, "gold_trim");
            AddSph(sb, "bell_dome", 0, baseY + 3.5f, 0, 2.0f, "gold_trim");
            AddCyl(sb, "bell_clapper", 0, baseY + 0.5f, 0, 0.15f, 2.0f, "iron_dark");

            // Corner turrets at belfry level
            float[] tx = { -(w + 0.8f), w + 0.8f, -(w + 0.8f), w + 0.8f };
            float[] tz = { -(w + 0.8f), -(w + 0.8f), w + 0.8f, w + 0.8f };
            for (int t = 0; t < 4; t++)
            {
                AddCyl(sb, $"belf_turret_{t}", tx[t], baseY, tz[t], 0.8f, h + 2.0f, "limestone");
                AddCyl(sb, $"belf_turret_cap_{t}", tx[t], baseY + h + 2.0f, tz[t], 1.0f, 0.3f, "stone_detail");
                // Turret pinnacle
                AddBox(sb, $"belf_turret_pin_{t}", tx[t], baseY + h + 2.8f, tz[t], 0.2f, 0.8f, 0.2f, "limestone");
                AddBox(sb, $"belf_turret_pin2_{t}", tx[t], baseY + h + 3.8f, tz[t], 0.12f, 0.5f, 0.12f, "limestone");
                AddSph(sb, $"belf_turret_ball_{t}", tx[t], baseY + h + 4.4f, tz[t], 0.1f, "gold_trim");
            }
        }

        static void BuildBelfryArches(StringBuilder sb, string pfx, float x, float baseY, float z,
                                       bool isFrontBack, float h)
        {
            float archH = 4.5f;
            float archW = 1.6f;
            float archY = baseY + 1.5f;

            if (isFrontBack)
            {
                // Two arched openings
                for (int i = 0; i < 2; i++)
                {
                    float ax = -2.5f + i * 5.0f;
                    // Dark recess
                    AddBox(sb, $"{pfx}_arch_{i}", ax, archY + archH / 2, z,
                           archW / 2, archH / 2, 0.2f, "dark_recess");
                    // Frame columns
                    AddCyl(sb, $"{pfx}_col_{i}_l", ax - archW / 2 - 0.15f, archY, z, 0.15f, archH, "stone_detail");
                    AddCyl(sb, $"{pfx}_col_{i}_r", ax + archW / 2 + 0.15f, archY, z, 0.15f, archH, "stone_detail");
                    // Pointed arch top
                    AddSph(sb, $"{pfx}_atop_{i}", ax, archY + archH, z, 0.8f, "stone_detail");
                    // Column capitals
                    AddBox(sb, $"{pfx}_cap_{i}_l", ax - archW / 2 - 0.15f, archY + archH, z,
                           0.22f, 0.15f, 0.22f, "stone_detail");
                    AddBox(sb, $"{pfx}_cap_{i}_r", ax + archW / 2 + 0.15f, archY + archH, z,
                           0.22f, 0.15f, 0.22f, "stone_detail");
                }
                // Central dividing column
                AddCyl(sb, $"{pfx}_mid_col", 0, archY, z, 0.18f, archH, "stone_detail");
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    float az = -2.5f + i * 5.0f;
                    AddBox(sb, $"{pfx}_arch_{i}", x, archY + archH / 2, az,
                           0.2f, archH / 2, archW / 2, "dark_recess");
                    AddCyl(sb, $"{pfx}_col_{i}_l", x, archY, az - archW / 2 - 0.15f, 0.15f, archH, "stone_detail");
                    AddCyl(sb, $"{pfx}_col_{i}_r", x, archY, az + archW / 2 + 0.15f, 0.15f, archH, "stone_detail");
                    AddSph(sb, $"{pfx}_atop_{i}", x, archY + archH, az, 0.8f, "stone_detail");
                    AddBox(sb, $"{pfx}_cap_{i}_l", x, archY + archH, az - archW / 2 - 0.15f,
                           0.22f, 0.15f, 0.22f, "stone_detail");
                    AddBox(sb, $"{pfx}_cap_{i}_r", x, archY + archH, az + archW / 2 + 0.15f,
                           0.22f, 0.15f, 0.22f, "stone_detail");
                }
                AddCyl(sb, $"{pfx}_mid_col", x, archY, 0, 0.18f, archH, "stone_detail");
            }
        }

        static void BuildRoofAndSpire(StringBuilder sb)
        {
            float baseY = 48.1f;

            // Stepped pyramid roof
            AddBox(sb, "roof_1", 0, baseY + 1.0f, 0, 6.0f, 1.0f, 6.0f, "copper_roof");
            AddBox(sb, "roof_2", 0, baseY + 2.5f, 0, 5.0f, 0.8f, 5.0f, "copper_roof");
            AddBox(sb, "roof_3", 0, baseY + 3.8f, 0, 4.0f, 0.7f, 4.0f, "copper_roof");
            AddBox(sb, "roof_4", 0, baseY + 4.8f, 0, 3.0f, 0.6f, 3.0f, "copper_roof");
            AddBox(sb, "roof_5", 0, baseY + 5.6f, 0, 2.0f, 0.5f, 2.0f, "copper_roof");
            AddBox(sb, "roof_6", 0, baseY + 6.3f, 0, 1.2f, 0.4f, 1.2f, "copper_roof");

            // Decorative finials at roof corners
            for (int i = 0; i < 4; i++)
            {
                float fx = (i < 2 ? -1 : 1) * 5.5f;
                float fz = (i % 2 == 0 ? -1 : 1) * 5.5f;
                AddCyl(sb, $"roof_finial_{i}", fx, baseY, fz, 0.3f, 3.0f, "limestone");
                AddBox(sb, $"roof_finial_cap_{i}", fx, baseY + 3.3f, fz, 0.15f, 0.5f, 0.15f, "limestone");
                AddSph(sb, $"roof_finial_ball_{i}", fx, baseY + 3.9f, fz, 0.12f, "gold_trim");
            }

            // Spire
            float spireBase = baseY + 6.8f;
            AddCyl(sb, "spire_shaft", 0, spireBase, 0, 0.6f, 4.0f, "copper_roof");
            AddCyl(sb, "spire_mid", 0, spireBase + 4.0f, 0, 0.4f, 2.0f, "copper_roof");
            AddCyl(sb, "spire_top", 0, spireBase + 6.0f, 0, 0.2f, 1.5f, "copper_roof");
            AddCyl(sb, "spire_tip", 0, spireBase + 7.5f, 0, 0.08f, 1.0f, "gold_trim");

            // Cross at the very top
            AddBox(sb, "cross_vert", 0, spireBase + 9.0f, 0, 0.04f, 0.5f, 0.04f, "gold_trim");
            AddBox(sb, "cross_horiz", 0, spireBase + 9.3f, 0, 0.25f, 0.04f, 0.04f, "gold_trim");

            // Ayrton Light (lantern at top)
            AddCyl(sb, "ayrton_cage", 0, spireBase + 7.8f, 0, 0.35f, 0.8f, "iron_dark");
            AddSph(sb, "ayrton_glow", 0, spireBase + 8.2f, 0, 0.25f, "warm_light");

            // Additional roof ornamental band
            AddBox(sb, "roof_gallery", 0, baseY + 0.15f, 0, 6.3f, 0.15f, 6.3f, "stone_detail");

            // Small balustrade around roof base
            int rPosts = 12;
            for (int side = 0; side < 4; side++)
            {
                for (int p = 0; p < rPosts; p++)
                {
                    float t = (p / (float)(rPosts - 1)) * 2 - 1; // -1..1
                    float px, pz;
                    switch (side)
                    {
                        case 0: px = t * 5.8f; pz = -6.0f; break;  // front
                        case 1: px = t * 5.8f; pz = 6.0f; break;   // back
                        case 2: px = -6.0f; pz = t * 5.8f; break;  // left
                        default: px = 6.0f; pz = t * 5.8f; break;  // right
                    }
                    AddCyl(sb, $"balus_{side}_{p}", px, baseY, pz, 0.06f, 0.8f, "limestone");
                }
                // Top rail
                float rx1, rz1;
                switch (side)
                {
                    case 0: rx1 = 0; rz1 = -6.0f; AddBox(sb, $"rail_{side}", rx1, baseY + 0.8f, rz1, 5.8f, 0.05f, 0.08f, "limestone"); break;
                    case 1: rx1 = 0; rz1 = 6.0f;  AddBox(sb, $"rail_{side}", rx1, baseY + 0.8f, rz1, 5.8f, 0.05f, 0.08f, "limestone"); break;
                    case 2: rx1 = -6.0f; rz1 = 0; AddBox(sb, $"rail_{side}", rx1, baseY + 0.8f, rz1, 0.08f, 0.05f, 5.8f, "limestone"); break;
                    default: rx1 = 6.0f; rz1 = 0;  AddBox(sb, $"rail_{side}", rx1, baseY + 0.8f, rz1, 0.08f, 0.05f, 5.8f, "limestone"); break;
                }
            }
        }

        // ================================================================
        //  SURROUNDINGS
        // ================================================================

        static void BuildLampPost(StringBuilder sb, string pfx, float x, float y, float z)
        {
            AddCyl(sb, $"{pfx}_pole", x, y, z, 0.08f, 3.5f, "iron_dark");
            AddCyl(sb, $"{pfx}_arm", x, y + 3.5f, z, 0.12f, 0.3f, "iron_dark");
            AddSph(sb, $"{pfx}_lamp", x, y + 4.0f, z, 0.25f, "warm_light");
            // Lamp cage
            AddCyl(sb, $"{pfx}_cage", x, y + 3.6f, z, 0.2f, 0.6f, "iron_dark");
        }

        // ================================================================
        //  PRIMITIVE HELPERS  (same pattern as ChessGen)
        // ================================================================

        static string VFmt(float v) => string.Format(CultureInfo.InvariantCulture, "{0:0.000}", v);

        static void AddCyl(StringBuilder sb, string name, float x, float y, float z, float r, float h, string mat)
        {
            entityCount++;
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"cylinder\"");
            sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    radius: {VFmt(r)}");
            sb.AppendLine($"    height: {VFmt(h)}");
            sb.AppendLine($"    material: \"{mat}\"");
        }

        static void AddSph(StringBuilder sb, string name, float x, float y, float z, float r, string mat)
        {
            entityCount++;
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"sphere\"");
            sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    radius: {VFmt(r)}");
            sb.AppendLine($"    material: \"{mat}\"");
        }

        static void AddBox(StringBuilder sb, string name, float x, float y, float z,
                           float dx, float dy, float dz, string mat)
        {
            entityCount++;
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine($"    scale: [{VFmt(dx * 2)}, {VFmt(dy * 2)}, {VFmt(dz * 2)}]");
            sb.AppendLine($"    translate: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    material: \"{mat}\"");
            sb.AppendLine();
        }

        static void WriteMat(StringBuilder sb, string id, string type, float r, float g, float b, float fuzz = -1)
        {
            sb.AppendLine($"  - id: \"{id}\"");
            sb.AppendLine($"    type: \"{type}\"");
            sb.AppendLine($"    color: [{VFmt(r)}, {VFmt(g)}, {VFmt(b)}]");
            if (fuzz >= 0)
                sb.AppendLine($"    fuzz: {VFmt(fuzz)}");
            sb.AppendLine();
        }

        static void WriteMat(StringBuilder sb, string id, string type, float refrIndex)
        {
            sb.AppendLine($"  - id: \"{id}\"");
            sb.AppendLine($"    type: \"{type}\"");
            sb.AppendLine($"    refraction_index: {VFmt(refrIndex)}");
            sb.AppendLine();
        }
    }
}
