using Hexa.NET.ImGui;

namespace RayForge.UI;

/// <summary>
/// Renders the fullscreen dockspace, the main menu bar and (for Phase 0) the ImGui demo
/// window. Once Phase 1+ panels exist (Outliner, Inspector, Viewport...) they get docked here.
/// </summary>
internal sealed class MainDockspace
{
    private bool _showDemo = true;
    public bool RequestExit { get; private set; }

    public void Draw()
    {
        DrawDockspaceHost();
        DrawMenuBar();

        if (_showDemo)
        {
            ImGui.ShowDemoWindow(ref _showDemo);
        }
    }

    private static void DrawDockspaceHost()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoCollapse
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoBringToFrontOnFocus
                  | ImGuiWindowFlags.NoNavFocus
                  | ImGuiWindowFlags.MenuBar
                  | ImGuiWindowFlags.NoDocking;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));

        bool open = true;
        ImGui.Begin("##RayForgeDockspaceHost", ref open, flags);
        ImGui.PopStyleVar(3);

        uint dockId = ImGui.GetID("RayForgeDockspace");
        ImGui.DockSpace(dockId, new System.Numerics.Vector2(0, 0), ImGuiDockNodeFlags.PassthruCentralNode);

        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Scene", "Ctrl+N")) { /* TODO Phase 2 */ }
                if (ImGui.MenuItem("Open...", "Ctrl+O")) { /* TODO Phase 2 */ }
                if (ImGui.MenuItem("Save", "Ctrl+S")) { /* TODO Phase 2 */ }
                if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S")) { /* TODO Phase 2 */ }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit", "Alt+F4")) RequestExit = true;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z")) { /* TODO Phase 2 */ }
                if (ImGui.MenuItem("Redo", "Ctrl+Y")) { /* TODO Phase 2 */ }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Add"))
            {
                ImGui.MenuItem("Sphere", false);     // Phase 3
                ImGui.MenuItem("Box", false);
                ImGui.MenuItem("Plane", false);
                ImGui.MenuItem("Cylinder", false);
                ImGui.MenuItem("Torus", false);
                ImGui.Separator();
                ImGui.MenuItem("Group", false);
                ImGui.MenuItem("CSG Union", false);
                ImGui.MenuItem("CSG Intersection", false);
                ImGui.MenuItem("CSG Subtraction", false);
                ImGui.Separator();
                ImGui.MenuItem("Light", false);      // Phase 4
                ImGui.MenuItem("Camera", false);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("ImGui Demo", string.Empty, ref _showDemo);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Render"))
            {
                ImGui.MenuItem("Render Preview", false); // Phase 6
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }
}
