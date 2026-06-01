using System.Runtime.CompilerServices;

namespace RayTracer.Volumetrics;

/// <summary>
/// Fixed-capacity, zero-alloc stack of dielectric indices of refraction tracked
/// through the path tracer, mirroring <see cref="MediumStack"/> but carrying the
/// <em>material's</em> IOR rather than a participating medium. The renderer
/// pushes a dielectric's IOR on a transmissive (refractive) entry and pops it on
/// exit, so the top of the stack is the IOR of the medium the ray is currently
/// travelling through.
///
/// <para>This decouples relative-IOR tracking from the medium stack on purpose:
/// a plain glass sphere has no participating medium (its
/// <c>MediumInterface.Interior</c> is null, so <see cref="MediumStack"/> stays
/// empty) yet still has an IOR of 1.5. Nested dielectrics — wine inside a glass
/// cup, ice in water — need the enclosing IOR to compute the relative refraction
/// ratio η_outside/η_inside instead of always assuming vacuum (η = 1) outside.
/// </para>
///
/// <para><see cref="Top"/> returns 1.0 (air/vacuum) when empty and
/// <see cref="Enclosing"/> returns 1.0 when fewer than two entries are present,
/// so an empty or single-entry stack reduces every relative-IOR computation
/// exactly to the legacy air-relative form — keeping non-nested scenes
/// bit-identical.
/// </para>
///
/// <para>Copy semantics match <see cref="MediumStack"/>: a value struct captured
/// by copy at refraction events, so the recursive frame mutates its own copy and
/// the caller's view is preserved.
/// </para>
/// </summary>
public struct IorStack
{
    public const int Capacity = 8;

    private Slots _slots;
    private int _count;

    public readonly int Depth => _count;

    /// <summary>
    /// IOR of the medium the ray is currently inside — the stack top, or 1.0
    /// (air/vacuum) when the stack is empty. This is the "outside" index when
    /// <em>entering</em> a new dielectric on a front-face refraction.
    /// </summary>
    public readonly float Top
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count == 0 ? 1f : _slots[_count - 1];
    }

    /// <summary>
    /// IOR of the medium enclosing the current one — the entry below the top,
    /// or 1.0 (air/vacuum) when fewer than two dielectrics are stacked. This is
    /// the "outside" index when <em>exiting</em> the current dielectric on a
    /// back-face refraction.
    /// </summary>
    public readonly float Enclosing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count >= 2 ? _slots[_count - 2] : 1f;
    }

    /// <summary>
    /// Pushes a dielectric IOR onto the stack. Returns <c>false</c> if the stack
    /// was full and the oldest entry had to be dropped (degenerate — only with
    /// pathologically deep refractive nesting), mirroring <see cref="MediumStack.Push(IMedium?)"/>.
    /// </summary>
    public bool Push(float ior)
    {
        if (_count >= Capacity)
        {
            // Overflow: shift down, drop the oldest slot — keep the most
            // recently entered dielectrics visible to the rest of the recursion.
            for (int i = 0; i < Capacity - 1; i++)
                _slots[i] = _slots[i + 1];
            _slots[Capacity - 1] = ior;
            return false;
        }
        _slots[_count] = ior;
        _count++;
        return true;
    }

    /// <summary>
    /// Pops the top IOR and returns it, or 1.0 (air) if the stack was empty
    /// (defensive: numerical edge cases on grazing-angle refraction can desync
    /// the Enter/Exit pairing in rare paths).
    /// </summary>
    public float Pop()
    {
        if (_count == 0) return 1f;
        _count--;
        return _slots[_count];
    }

    /// <summary>
    /// Inline-array backing storage (C# 12 / .NET 8+), avoiding the managed
    /// array header + GC pressure of a <c>float[8]</c>.
    /// </summary>
    [InlineArray(Capacity)]
    private struct Slots
    {
        private float _element0;
    }
}
