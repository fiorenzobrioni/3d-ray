using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Prints the full statistical report of every catalogued material
/// (PIANO-REVIEW.md, Phase 0 baseline). Opt-in: it is a measuring tool, not a
/// gate. Run with
/// <c>dotnet test --filter "Category=Audit" --logger "console;verbosity=detailed"</c>.
/// </summary>
public class BsdfAuditTests
{
    private readonly ITestOutputHelper _out;
    public BsdfAuditTests(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Audit")]
    public void PrintReport()
    {
        if (Environment.GetEnvironmentVariable("RAYTRACER_AUDIT") != "1")
        {
            _out.WriteLine("Set RAYTRACER_AUDIT=1 to run the BSDF audit.");
            return;
        }
        string? only = Environment.GetEnvironmentVariable("RAYTRACER_AUDIT_ONLY");
        foreach (var (name, material) in BsdfCatalog.All())
        {
            if (only != null && name != only) continue;
            if (only != null)
            {
                var diag = new List<string>();
                foreach (float cosV in BsdfCatalog.Views)
                {
                    diag.Clear();
                    _out.WriteLine(BsdfTestKit.Run(name, material, cosV, diagnostics: diag).ToString());
                    foreach (var line in diag) _out.WriteLine(line);
                }
                continue;
            }
            foreach (float cosV in BsdfCatalog.Views)
                _out.WriteLine(BsdfTestKit.Run(name, material, cosV).ToString());
            _out.WriteLine($"{name,-28} reciprocity max rel err = {BsdfTestKit.ReciprocityError(material):0.0000}, " +
                           $"EvaluateDirect vs Evaluate*cos max rel err = {BsdfTestKit.DirectVsEvaluateError(material):0.0000}");
        }
    }
}
