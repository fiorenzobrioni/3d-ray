using System.Runtime.CompilerServices;
using RayTracer.Geometry;

namespace RayTracer.Volumetrics;

/// <summary>
/// Fixed-capacity, zero-alloc stack of participating media tracked through
/// the path tracer. The renderer pushes <see cref="MediumInterface.Interior"/>
/// on refractive entry and pops on exit (see Renderer.ShadeSampleBounce).
/// The top of the stack — combined with the optional global medium — is the
/// volume the ray is currently traversing.
///
/// <para>Why a stack instead of a single "current medium" pointer: nested
/// transmissives (a marble bust inside a glass dome, ice in a cocktail glass,
/// a fish in a water tank) require ordered push/pop semantics. PBRT §11.3.5.
/// Capacity 8 is well above the practical maximum for any reasonable scene.
/// </para>
///
/// <para>Copy semantics: this is a regular value struct (not a ref struct)
/// so the renderer can capture-by-copy at refraction events — the recursive
/// frame mutates its own copy and the caller's view is preserved. ~72 bytes
/// per copy (8 references + count), negligible vs the BVH traversal cost.
/// </para>
/// </summary>
public struct MediumStack
{
    public const int Capacity = 8;

    private Slots _slots;
    private Entities _entities;
    private int _count;

    public readonly int Depth => _count;

    public readonly IMedium? Top
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count == 0 ? null : _slots[_count - 1];
    }

    /// <summary>
    /// The geometry bounding <see cref="Top"/>, or <c>null</c> when the top
    /// medium is unbounded (global / world medium pushed without an entity).
    /// Lets NEE clip the shadow ray's in-medium transmittance segment to the
    /// portion that is actually inside the bound medium (see
    /// Renderer.ClipShadowToBoundary) instead of attenuating over the full
    /// Euclidean distance to the light.
    /// </summary>
    public readonly IHittable? TopEntity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count == 0 ? null : _entities[_count - 1];
    }

    /// <summary>
    /// Pushes a medium onto the stack. Returns <c>true</c> on success,
    /// <c>false</c> if the stack was full and the oldest entry had to be
    /// dropped to make room (degenerate — only happens with pathologically
    /// deep refractive nesting, the renderer warns once per render).
    /// </summary>
    public bool Push(IMedium? medium) => Push(medium, null);

    /// <summary>
    /// Pushes a medium together with the geometry that bounds it (see
    /// <see cref="TopEntity"/>). Pass <c>null</c> for an unbounded medium.
    /// </summary>
    public bool Push(IMedium? medium, IHittable? boundEntity)
    {
        if (_count >= Capacity)
        {
            // Overflow: shift down, drop the oldest slot. This keeps the most
            // recently entered media visible to the rest of the recursion —
            // which is the worse of two bad choices but still correct for the
            // remaining stack depth budget.
            for (int i = 0; i < Capacity - 1; i++)
            {
                _slots[i] = _slots[i + 1];
                _entities[i] = _entities[i + 1];
            }
            _slots[Capacity - 1] = medium;
            _entities[Capacity - 1] = boundEntity;
            return false;
        }
        _slots[_count] = medium;
        _entities[_count] = boundEntity;
        _count++;
        return true;
    }

    /// <summary>
    /// Pops the top medium and returns it. Returns <c>null</c> if the stack
    /// was empty (defensive: a balanced renderer never pops past empty, but
    /// numerical edge cases on refraction at grazing angles can desync the
    /// Enter/Exit pairing in rare paths).
    /// </summary>
    public IMedium? Pop()
    {
        if (_count == 0) return null;
        _count--;
        IMedium? v = _slots[_count];
        _slots[_count] = null;
        _entities[_count] = null;
        return v;
    }

    /// <summary>
    /// Inline-array backing storage. C# 12 / .NET 8+ feature — the runtime
    /// exposes <c>[i]</c> indexing over the single declared field. Saves the
    /// 16-byte managed-array header + GC pressure vs an <c>IMedium?[8]</c>.
    /// </summary>
    [InlineArray(Capacity)]
    private struct Slots
    {
        private IMedium? _element0;
    }

    /// <summary>
    /// Parallel backing storage for the per-slot bounding geometry, kept in
    /// lock-step with <see cref="Slots"/> on every push/pop.
    /// </summary>
    [InlineArray(Capacity)]
    private struct Entities
    {
        private IHittable? _element0;
    }
}
