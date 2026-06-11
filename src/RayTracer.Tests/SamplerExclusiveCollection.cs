using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// xUnit collection for tests that depend on the PROCESS-GLOBAL sampler mode
/// (<see cref="RayTracer.Core.Sampling.Sampler.SetKind"/>) staying fixed for
/// their whole duration — e.g. bit-identity assertions that render the same
/// scene multiple times under Sobol and compare exactly.
///
/// <para>Most render tests call <c>SetKind(Prng)</c> defensively and tolerate
/// a concurrent flip (their assertions are threshold-based). Exact-equality
/// tests cannot: another class flipping the kind between (or during) their
/// renders changes the random stream and the image. Rather than serialising
/// every <c>SetKind</c> caller, this collection sets
/// <c>DisableParallelization</c> so its classes run while nothing else does;
/// the rest of the suite keeps full parallelism.</para>
/// </summary>
[CollectionDefinition("SamplerExclusive", DisableParallelization = true)]
public sealed class SamplerExclusiveCollection { }
