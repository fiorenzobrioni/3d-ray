using RayForge.App;

namespace RayForge;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            using var app = new Application("RayForge", 1600, 1000);
            return app.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex}");
            return 1;
        }
    }
}
