using System.Numerics;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// The parameters of OpenPBR Surface v1.1 (Academy Software Foundation), by
/// their specification names and defaults. Every value may be a constant or a
/// texture. Units: lengths in scene units, <see cref="ThinFilmThickness"/> in
/// micrometres, <see cref="EmissionLuminance"/> as radiance in scene units
/// (the renderer has no absolute exposure yet).
/// </summary>
public sealed class OpenPbrParameters
{
    // Base
    public FloatTexture BaseWeight { get; init; } = 1f;
    public ITexture BaseColor { get; init; } = new SolidColor(new Vector3(0.8f));
    public FloatTexture BaseMetalness { get; init; } = 0f;
    public FloatTexture BaseDiffuseRoughness { get; init; } = 0f;

    // Specular
    public FloatTexture SpecularWeight { get; init; } = 1f;
    public ITexture SpecularColor { get; init; } = new SolidColor(Vector3.One);
    public FloatTexture SpecularRoughness { get; init; } = 0.3f;
    public FloatTexture SpecularRoughnessAnisotropy { get; init; } = 0f;
    public FloatTexture SpecularIor { get; init; } = 1.5f;

    /// <summary>Extension: turns the anisotropy direction about the normal by
    /// this fraction of a full turn (OpenPBR takes it from the tangent only).</summary>
    public FloatTexture SpecularRotation { get; init; } = 0f;

    // Transmission
    public FloatTexture TransmissionWeight { get; init; } = 0f;
    public ITexture TransmissionColor { get; init; } = new SolidColor(Vector3.One);
    public FloatTexture TransmissionDepth { get; init; } = 0f;
    public ITexture TransmissionScatter { get; init; } = new SolidColor(Vector3.Zero);
    public FloatTexture TransmissionScatterAnisotropy { get; init; } = 0f;

    // Subsurface
    public FloatTexture SubsurfaceWeight { get; init; } = 0f;
    public ITexture SubsurfaceColor { get; init; } = new SolidColor(new Vector3(0.8f));
    public FloatTexture SubsurfaceRadius { get; init; } = 1f;
    public ITexture SubsurfaceRadiusScale { get; init; } = new SolidColor(new Vector3(1f, 0.5f, 0.25f));
    public FloatTexture SubsurfaceScatterAnisotropy { get; init; } = 0f;

    // Coat
    public FloatTexture CoatWeight { get; init; } = 0f;
    public ITexture CoatColor { get; init; } = new SolidColor(Vector3.One);
    public FloatTexture CoatRoughness { get; init; } = 0f;
    public FloatTexture CoatRoughnessAnisotropy { get; init; } = 0f;
    public FloatTexture CoatIor { get; init; } = 1.6f;
    public FloatTexture CoatDarkening { get; init; } = 1f;

    // Fuzz
    public FloatTexture FuzzWeight { get; init; } = 0f;
    public ITexture FuzzColor { get; init; } = new SolidColor(Vector3.One);
    public FloatTexture FuzzRoughness { get; init; } = 0.5f;

    // Thin film
    public FloatTexture ThinFilmWeight { get; init; } = 0f;
    public FloatTexture ThinFilmThickness { get; init; } = 0.5f;
    public FloatTexture ThinFilmIor { get; init; } = 1.4f;

    // Emission
    public FloatTexture EmissionLuminance { get; init; } = 0f;
    public ITexture EmissionColor { get; init; } = new SolidColor(Vector3.One);

    // Geometry
    public FloatTexture GeometryOpacity { get; init; } = 1f;
    public bool ThinWalled { get; init; }
    public NormalMapTexture? CoatNormal { get; init; }
}
