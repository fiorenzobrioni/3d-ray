using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// xUnit collection for every test class that calls <see cref="RayTracer.Scene.SceneLoader.Load"/>.
///
/// <para><c>SceneLoader</c> keeps per-load results in <b>static</b> state
/// (<c>_deferredMessages</c>) — it is designed to be called once, single-threaded.
/// xUnit runs distinct test classes in parallel by default, so two classes loading
/// scenes concurrently would clear/append the same static lists (a non-thread-safe
/// <c>List</c>) and read each other's results, surfacing as intermittent CI failures.</para>
///
/// <para>Placing all <c>Load</c>-callers in one collection makes xUnit run them
/// sequentially (still in parallel with unrelated collections), which is enough to make
/// the shared static state safe without reworking <c>SceneLoader</c> into an instance.</para>
/// </summary>
[CollectionDefinition("SceneLoader")]
public sealed class SceneLoaderCollection { }
