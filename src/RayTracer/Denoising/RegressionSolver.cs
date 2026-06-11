namespace RayTracer.Denoising;

/// <summary>
/// Tiny dense Cholesky solver for the per-window weighted normal equations of
/// the NFOR first-order regression: (XᵀWX + λ·diag) β = XᵀW y, with an 8×8
/// symmetric positive (semi-)definite Gram matrix shared across the three RGB
/// right-hand sides. Hand-rolled — no external math dependency.
/// </summary>
internal static class RegressionSolver
{
    /// <summary>
    /// Solves G·βᵢ = rhsᵢ in place for each right-hand side, after adding the
    /// Tikhonov term λ = <paramref name="lambdaScale"/> · trace(G)/dim to the
    /// diagonal. <paramref name="gram"/> is a full row-major dim×dim matrix
    /// (lower triangle authoritative); it is overwritten by the factorisation.
    /// Returns false when the matrix is not positive definite even after
    /// regularisation (caller falls back to the weighted mean).
    /// </summary>
    public static bool Solve(Span<double> gram, Span<double> rhs, int dim, int rhsCount,
                             double lambdaScale = 1e-3)
    {
        // Tikhonov regularisation, scaled to the matrix magnitude.
        double trace = 0;
        for (int i = 0; i < dim; i++) trace += gram[i * dim + i];
        double lambda = lambdaScale * trace / dim + 1e-12;
        for (int i = 0; i < dim; i++) gram[i * dim + i] += lambda;

        // Cholesky factorisation G = L·Lᵀ (lower triangle written in place).
        for (int i = 0; i < dim; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = gram[i * dim + j];
                for (int k = 0; k < j; k++)
                    sum -= gram[i * dim + k] * gram[j * dim + k];
                if (i == j)
                {
                    if (sum <= 0) return false;
                    gram[i * dim + i] = Math.Sqrt(sum);
                }
                else
                {
                    gram[i * dim + j] = sum / gram[j * dim + j];
                }
            }
        }

        // Forward + back substitution per right-hand side.
        for (int r = 0; r < rhsCount; r++)
        {
            var b = rhs.Slice(r * dim, dim);
            for (int i = 0; i < dim; i++)
            {
                double sum = b[i];
                for (int k = 0; k < i; k++) sum -= gram[i * dim + k] * b[k];
                b[i] = sum / gram[i * dim + i];
            }
            for (int i = dim - 1; i >= 0; i--)
            {
                double sum = b[i];
                for (int k = i + 1; k < dim; k++) sum -= gram[k * dim + i] * b[k];
                b[i] = sum / gram[i * dim + i];
            }
        }
        return true;
    }
}
