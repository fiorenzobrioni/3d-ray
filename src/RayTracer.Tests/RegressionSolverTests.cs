using RayTracer.Denoising;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for the hand-rolled Cholesky normal-equations solver used by
/// the NFOR regression (8×8 Gram, 3 RGB right-hand sides).
/// </summary>
public class RegressionSolverTests
{
    private const int Dim = 8;

    /// <summary>Builds a well-conditioned SPD matrix G = AᵀA + I.</summary>
    private static double[] RandomSpd(Random rng, int dim)
    {
        var a = new double[dim * dim];
        for (int i = 0; i < a.Length; i++) a[i] = rng.NextDouble() * 2 - 1;
        var g = new double[dim * dim];
        for (int i = 0; i < dim; i++)
        for (int j = 0; j < dim; j++)
        {
            double sum = i == j ? 1.0 : 0.0;
            for (int k = 0; k < dim; k++) sum += a[k * dim + i] * a[k * dim + j];
            g[i * dim + j] = sum;
        }
        return g;
    }

    private static double[] Multiply(double[] g, double[] x, int dim)
    {
        var y = new double[dim];
        for (int i = 0; i < dim; i++)
        {
            double sum = 0;
            for (int j = 0; j < dim; j++) sum += g[i * dim + j] * x[j];
            y[i] = sum;
        }
        return y;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1337)]
    public void Solve_RandomSpdSystem_MatchesDirectVerification(int seed)
    {
        var rng = new Random(seed);
        var g = RandomSpd(rng, Dim);
        var gCopy = (double[])g.Clone();

        var expected = new double[Dim];
        for (int i = 0; i < Dim; i++) expected[i] = rng.NextDouble() * 4 - 2;
        var rhs = Multiply(g, expected, Dim);
        var rhsCopy = (double[])rhs.Clone();

        // Negligible regularisation so G·x = b holds to numerical precision.
        bool solved = RegressionSolver.Solve(gCopy, rhsCopy, Dim, rhsCount: 1, lambdaScale: 0);
        Assert.True(solved);
        for (int i = 0; i < Dim; i++)
            Assert.True(Math.Abs(rhsCopy[i] - expected[i]) < 1e-8,
                $"x[{i}] = {rhsCopy[i]} expected {expected[i]}");
    }

    [Fact]
    public void Solve_RecoverPlantedCoefficients_FromWeightedObservations()
    {
        // y(q) = βᵀf(q) exactly; the weighted normal equations must recover β.
        var rng = new Random(7);
        var beta = new double[Dim];
        for (int i = 0; i < Dim; i++) beta[i] = rng.NextDouble() * 2 - 1;

        var gram = new double[Dim * Dim];
        var rhs = new double[Dim];
        for (int obs = 0; obs < 200; obs++)
        {
            var f = new double[Dim];
            f[0] = 1.0;
            for (int i = 1; i < Dim; i++) f[i] = rng.NextDouble() * 2 - 1;
            double y = 0;
            for (int i = 0; i < Dim; i++) y += beta[i] * f[i];
            double w = 0.1 + rng.NextDouble();
            for (int i = 0; i < Dim; i++)
            {
                rhs[i] += w * f[i] * y;
                for (int j = 0; j <= i; j++)
                    gram[i * Dim + j] += w * f[i] * f[j];
            }
        }

        bool solved = RegressionSolver.Solve(gram, rhs, Dim, rhsCount: 1, lambdaScale: 1e-9);
        Assert.True(solved);
        for (int i = 0; i < Dim; i++)
            Assert.True(Math.Abs(rhs[i] - beta[i]) < 1e-4,
                $"β[{i}] = {rhs[i]} expected {beta[i]}");
    }

    [Fact]
    public void Solve_RankDeficientGram_RegularisationKeepsItFiniteAndStable()
    {
        // Two identical feature columns → singular normal equations. The
        // Tikhonov term must keep the factorisation alive and the solution
        // finite (no exception, no NaN/Inf blow-up).
        var rng = new Random(11);
        var gram = new double[Dim * Dim];
        var rhs = new double[Dim];
        for (int obs = 0; obs < 100; obs++)
        {
            var f = new double[Dim];
            f[0] = 1.0;
            for (int i = 1; i < Dim - 1; i++) f[i] = rng.NextDouble();
            f[Dim - 1] = f[1];   // duplicated column
            double y = 3.0 * f[1] + 0.5;
            for (int i = 0; i < Dim; i++)
            {
                rhs[i] += f[i] * y;
                for (int j = 0; j <= i; j++)
                    gram[i * Dim + j] += f[i] * f[j];
            }
        }

        bool solved = RegressionSolver.Solve(gram, rhs, Dim, rhsCount: 1);
        Assert.True(solved);
        foreach (double x in rhs.AsSpan(0, Dim))
        {
            Assert.False(double.IsNaN(x) || double.IsInfinity(x));
            Assert.True(Math.Abs(x) < 100, $"coefficient {x} exploded");
        }
    }
}
