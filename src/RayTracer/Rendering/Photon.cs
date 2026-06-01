using System.Numerics;

namespace RayTracer.Rendering;

/// <summary>
/// A single caustic photon: a packet of radiant power deposited on a
/// diffuse/glossy surface after travelling along a path that crossed at least
/// one specular (delta) interface from a light (<c>L S+ D</c>). The renderer's
/// camera pass estimates the focused-caustic radiance at a surface point by a
/// density estimate over the nearby photons (see <see cref="PhotonMap"/>).
/// </summary>
public readonly struct Photon
{
    /// <summary>World-space deposit point on the receiving surface.</summary>
    public readonly Vector3 Position;
    /// <summary>Unit direction the photon arrived FROM (surface → previous vertex), i.e. the incident light direction used to evaluate the receiver BSDF.</summary>
    public readonly Vector3 IncidentDir;
    /// <summary>Per-channel radiant power Φ carried by the photon (already divided by the emitted photon count).</summary>
    public readonly Vector3 Power;

    public Photon(Vector3 position, Vector3 incidentDir, Vector3 power)
    {
        Position    = position;
        IncidentDir = incidentDir;
        Power       = power;
    }
}
