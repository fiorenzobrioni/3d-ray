using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Coverage for caustic casters nested inside <c>type: group</c> entities. A
/// flagged child must register its seeding geometry in WORLD space — composing
/// the group's (and any ancestor group's) transform — so the manifold walk and
/// the segment cull operate where the geometry is actually rendered. A group
/// itself is never a single manifold caster, and the nested chain must compose
/// correctly through multiple levels.
/// </summary>
[Collection("SceneLoader")]
public class GroupCausticTests
{
    private static (System.Collections.Generic.IReadOnlyList<IHittable> casters, int registered) LoadCasters(string yaml)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try
        {
            SceneLoader.Load(path, 64, 48, enableCaustics: true);
            var casters = SceneLoader.LastCausticCasters;
            int registered = new CausticCasterRegistry(casters).Count;
            return (casters, registered);
        }
        finally { File.Delete(path); }
    }

    private static AABB Bounds(IHittable h) => h.BoundingBox();

    [Fact]
    public void FlaggedChild_InTransformedGroup_RegistersInWorldSpace()
    {
        // Group translated to x=3; child sphere centred at object y=1.4. The
        // registered seeding geometry must sit at world (3, 1.4, 0), not the
        // group-local (0, 1.4, 0).
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [3, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
entities:
  - name: ""grp""
    type: ""group""
    translate: [3, 0, 0]
    children:
      - name: ""ball""
        type: ""sphere""
        center: [0, 1.4, 0]
        radius: 1.0
        material: ""glass""
        caustic_caster: true
";
        var (casters, registered) = LoadCasters(yaml);

        Assert.Single(casters);
        Assert.Equal(1, registered); // the seeder built successfully

        var box = Bounds(casters[0]);
        Vector3 center = (box.Min + box.Max) * 0.5f;
        Assert.True((center - new Vector3(3f, 1.4f, 0f)).Length() < 1e-3f,
            $"world caster centre {center} should be (3, 1.4, 0)");
    }

    [Fact]
    public void FlaggedChild_InNestedGroups_ComposesTransformChain()
    {
        // outer group +x=2, inner group +y=1, child sphere object-centre z=0.5 with
        // its own translate +x=0.5 ⇒ world centre (2.5, 1, 0.5).
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [2, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
entities:
  - name: ""outer""
    type: ""group""
    translate: [2, 0, 0]
    children:
      - name: ""inner""
        type: ""group""
        translate: [0, 1, 0]
        children:
          - name: ""ball""
            type: ""sphere""
            radius: 0.8
            translate: [0.5, 0, 0.5]
            material: ""glass""
            caustic_caster: true
";
        var (casters, registered) = LoadCasters(yaml);

        Assert.Single(casters);
        Assert.Equal(1, registered);

        var box = Bounds(casters[0]);
        Vector3 center = (box.Min + box.Max) * 0.5f;
        Assert.True((center - new Vector3(2.5f, 1f, 0.5f)).Length() < 1e-3f,
            $"world caster centre {center} should be (2.5, 1, 0.5)");
    }

    [Fact]
    public void GlassCsgChild_InGroup_RegistersAndSeeds()
    {
        // A CSG solid (curved boundary) flagged inside a translated group must
        // register and build a working seeder in world space.
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [1, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
entities:
  - name: ""grp""
    type: ""group""
    translate: [1, 0.8, 0]
    children:
      - name: ""die""
        type: ""csg""
        operation: ""intersection""
        material: ""glass""
        caustic_caster: true
        left:  { type: ""sphere"", radius: 0.7 }
        right: { type: ""box"", scale: [1, 1, 1] }
";
        var (casters, registered) = LoadCasters(yaml);

        Assert.Single(casters);
        Assert.Equal(1, registered);

        var box = Bounds(casters[0]);
        Vector3 center = (box.Min + box.Max) * 0.5f;
        Assert.True((center - new Vector3(1f, 0.8f, 0f)).Length() < 1e-3f,
            $"world CSG caster centre {center} should be (1, 0.8, 0)");
    }

    [Fact]
    public void GlassCsgChild_WithOwnTransform_InGroup_RegistersInWorldSpace()
    {
        // CSG child carrying its OWN translate, nested in a translated group ⇒ the
        // loader produces Transform(Transform(CsgObject)). The seeder must flatten
        // the chain; the world centre is group+child translate composed.
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [1, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
entities:
  - name: ""grp""
    type: ""group""
    translate: [1, 0, 0]
    children:
      - name: ""die""
        type: ""csg""
        operation: ""intersection""
        material: ""glass""
        caustic_caster: true
        translate: [0, 0.9, 0.2]
        left:  { type: ""sphere"", radius: 0.7 }
        right: { type: ""box"", scale: [2, 2, 2] }
";
        var (casters, registered) = LoadCasters(yaml);

        Assert.Single(casters);
        Assert.Equal(1, registered);

        var box = Bounds(casters[0]);
        Vector3 center = (box.Min + box.Max) * 0.5f;
        Assert.True((center - new Vector3(1f, 0.9f, 0.2f)).Length() < 1e-3f,
            $"world CSG caster centre {center} should be (1, 0.9, 0.2)");
    }

    [Fact]
    public void GroupItself_FlaggedCaster_IsRejected()
    {
        // Flagging the GROUP (not its children) registers nothing — a group is not
        // a single manifold caster. The child here is diffuse so auto-classification
        // does not independently register it, isolating the group-flag behaviour.
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [0, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""matte""
    type: ""lambertian""
    color: [0.7, 0.7, 0.7]
entities:
  - name: ""grp""
    type: ""group""
    caustic_caster: true
    children:
      - name: ""ball""
        type: ""sphere""
        center: [0, 1.4, 0]
        radius: 1.0
        material: ""matte""
";
        var (casters, _) = LoadCasters(yaml);
        Assert.Empty(casters);
    }

    [Fact]
    public void AutoClassification_RegistersSpecularCurvedEntity_WithoutFlags()
    {
        // No caustic_caster/caustic_receiver flags anywhere: the engine must
        // auto-register the glass sphere (curved geometry + transmissive material)
        // as a caster, while the diffuse box stays out of the registry.
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [0, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
  - id: ""matte""
    type: ""lambertian""
    color: [0.7, 0.7, 0.7]
entities:
  - name: ""ball""
    type: ""sphere""
    center: [0, 1.4, 0]
    radius: 1.0
    material: ""glass""
  - name: ""crate""
    type: ""box""
    scale: [1, 1, 1]
    translate: [3, 0.5, 0]
    material: ""matte""
";
        var (casters, registered) = LoadCasters(yaml);
        Assert.Single(casters);   // only the glass sphere; the diffuse box is excluded
        Assert.Equal(1, registered);
    }

    [Fact]
    public void AutoClassification_OptOut_ExcludesCaster()
    {
        // caustic_caster: false must veto auto-registration even for an otherwise
        // eligible specular curved entity.
        string yaml = @"
camera:
  position: [0, 4, 8]
  look_at: [0, 1, 0]
world:
  sky: { type: ""flat"", color: [0,0,0] }
materials:
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
entities:
  - name: ""ball""
    type: ""sphere""
    center: [0, 1.4, 0]
    radius: 1.0
    material: ""glass""
    caustic_caster: false
";
        var (casters, _) = LoadCasters(yaml);
        Assert.Empty(casters);
    }
}
