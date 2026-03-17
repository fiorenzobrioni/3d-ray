using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;

namespace RayTracer.Scene;

public class SceneLoader
{
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, Vector3 AmbientLight, Vector3 Background)
        Load(string yamlPath, int imageWidth, int imageHeight)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var data = deserializer.Deserialize<SceneData>(yaml);
        if (data == null) throw new InvalidOperationException("Failed to parse YAML scene.");

        // Materials dictionary
        var materials = new Dictionary<string, IMaterial>();
        if (data.Materials != null)
        {
            foreach (var m in data.Materials)
            {
                if (m.Id == null) continue;
                materials[m.Id] = CreateMaterial(m);
            }
        }

        // World
        var ambientLight = ToVector3(data.World?.AmbientLight) ?? new Vector3(0.1f);
        var background = ToVector3(data.World?.Background) ?? new Vector3(0.5f, 0.7f, 1.0f);

        var objects = new List<IHittable>();

        // Ground plane
        if (data.World?.Ground != null)
        {
            var groundMat = GetMaterial(materials, data.World.Ground.Material);
            float groundY = data.World.Ground.Y;
            objects.Add(new InfinitePlane(
                new Vector3(0, groundY, 0), Vector3.UnitY, groundMat));
        }

        // Entities
        if (data.Entities != null)
        {
            foreach (var e in data.Entities)
            {
                var mat = GetMaterial(materials, e.Material);
                var hittable = CreateEntity(e, mat);
                if (hittable != null) objects.Add(hittable);
            }
        }

        // Build BVH from all objects
        IHittable world;
        if (objects.Count > 4)
        {
            // Separate infinite planes (they can't have finite BBs) from finite objects
            var finiteObjects = new List<IHittable>();
            var infiniteObjects = new List<IHittable>();
            foreach (var obj in objects)
            {
                if (obj is InfinitePlane)
                    infiniteObjects.Add(obj);
                else
                    finiteObjects.Add(obj);
            }

            var bvhObjects = new List<IHittable>();
            if (finiteObjects.Count > 0)
                bvhObjects.Add(new BvhNode(finiteObjects));
            bvhObjects.AddRange(infiniteObjects);
            world = new HittableList(bvhObjects);
        }
        else
        {
            world = new HittableList(objects);
        }

        // Camera
        float aspect = (float)imageWidth / imageHeight;
        var camData = data.Camera ?? new CameraData();
        var camPos = ToVector3(camData.Position) ?? new Vector3(0, 1, -5);
        var camLookAt = ToVector3(camData.LookAt) ?? Vector3.Zero;
        var camera = new Camera.Camera(
            camPos, camLookAt, Vector3.UnitY,
            camData.Fov, aspect,
            camData.Aperture, camData.FocalDist);

        // Lights
        var lights = new List<ILight>();
        if (data.Lights != null)
        {
            foreach (var l in data.Lights)
            {
                var light = CreateLight(l);
                if (light != null) lights.Add(light);
            }
        }
        // If no lights defined, add a default sun + ambient fill
        if (lights.Count == 0)
        {
            lights.Add(new DirectionalLight(
                new Vector3(-1, -1, -1), Vector3.One, 0.8f));
            lights.Add(new PointLight(
                new Vector3(0, 10, -5), Vector3.One, 100f));
        }

        return (world, camera, lights, ambientLight, background);
    }

    private static IMaterial CreateMaterial(MaterialData m)
    {
        ITexture albedo;
        if (m.Texture != null)
        {
            albedo = CreateTexture(m.Texture);
        }
        else
        {
            var color = ToVector3(m.Color) ?? new Vector3(0.5f);
            albedo = new SolidColor(color);
        }

        return m.Type?.ToLowerInvariant() switch
        {
            "lambertian" => new Lambertian(albedo),
            "metal" => new Metal(albedo, m.Fuzz),
            "dielectric" => new Dielectric(m.RefractionIndex, albedo),
            _ => new Lambertian(albedo)
        };
    }

    private static ITexture CreateTexture(TextureData t)
    {
        ITexture tex = t.Type?.ToLowerInvariant() switch
        {
            "checker" => new CheckerTexture(t.Scale, 
                t.Colors != null && t.Colors.Count > 0 ? ToVector3(t.Colors[0]) ?? Vector3.One : Vector3.One,
                t.Colors != null && t.Colors.Count > 1 ? ToVector3(t.Colors[1]) ?? Vector3.Zero : Vector3.Zero),
            "noise" => new NoiseTexture(t.Scale),
            "marble" => new MarbleTexture(t.Scale,
                t.Colors != null && t.Colors.Count > 0 ? ToVector3(t.Colors[0]) ?? new Vector3(0.9f) : new Vector3(0.9f),
                t.Colors != null && t.Colors.Count > 1 ? ToVector3(t.Colors[1]) ?? new Vector3(0.1f) : new Vector3(0.1f)),
            "wood" => new WoodTexture(t.Scale, 
                t.NoiseStrength ?? 2.0f,
                t.Colors != null && t.Colors.Count > 0 ? ToVector3(t.Colors[0]) ?? new Vector3(0.85f, 0.65f, 0.40f) : new Vector3(0.85f, 0.65f, 0.40f),
                t.Colors != null && t.Colors.Count > 1 ? ToVector3(t.Colors[1]) ?? new Vector3(0.60f, 0.40f, 0.20f) : new Vector3(0.60f, 0.40f, 0.20f)),
            _ => new SolidColor(t.Colors != null && t.Colors.Count > 0 ? ToVector3(t.Colors[0]) ?? Vector3.One : Vector3.One)
        };

        ApplyTextureParams(tex, t);
        return tex;
    }

    private static void ApplyTextureParams(ITexture tex, TextureData t)
    {
        if (tex is NoiseTexture nt)
        {
            if (t.Offset != null) nt.Offset = ToVector3(t.Offset) ?? Vector3.Zero;
            if (t.Rotation != null) nt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            nt.RandomizeOffset = t.RandomizeOffset;
            nt.RandomizeRotation = t.RandomizeRotation;
        }
        else if (tex is MarbleTexture mt)
        {
            if (t.Offset != null) mt.Offset = ToVector3(t.Offset) ?? Vector3.Zero;
            if (t.Rotation != null) mt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            mt.RandomizeOffset = t.RandomizeOffset;
            mt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) mt.NoiseStrength = t.NoiseStrength.Value;
        }
        else if (tex is WoodTexture wt)
        {
            if (t.Offset != null) wt.Offset = ToVector3(t.Offset) ?? Vector3.Zero;
            if (t.Rotation != null) wt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            wt.RandomizeOffset = t.RandomizeOffset;
            wt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) wt.NoiseStrength = t.NoiseStrength.Value;
        }
    }

    private static IHittable? CreateEntity(EntityData e, IMaterial mat)
    {
        IHittable? entity = e.Type?.ToLowerInvariant() switch
        {
            "sphere" => new Sphere(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, mat),
            "box" => new Box(ToVector3(e.Min) ?? -Vector3.One, ToVector3(e.Max) ?? Vector3.One, mat),
            "triangle" => new Triangle(ToVector3(e.V0) ?? Vector3.Zero, ToVector3(e.V1) ?? Vector3.UnitX, ToVector3(e.V2) ?? Vector3.UnitY, mat),
            "cylinder" => new Cylinder(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.Height, mat),
            "plane" or "infinite_plane" => new InfinitePlane(ToVector3(e.Point) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, mat),
            _ => null
        };

        if (entity != null)
        {
            entity.Seed = e.Seed ?? Random.Shared.Next();
        }

        return entity;
    }

    private static ILight? CreateLight(LightData l)
    {
        var color = ToVector3(l.Color) ?? Vector3.One;
        return l.Type?.ToLowerInvariant() switch
        {
            "point" => new PointLight(
                ToVector3(l.Position) ?? new Vector3(0, 10, 0), color, l.Intensity),
            "directional" or "sun" => new DirectionalLight(
                ToVector3(l.Direction) ?? new Vector3(-1, -1, -1), color, l.Intensity),
            _ => null
        };
    }

    private static IMaterial GetMaterial(Dictionary<string, IMaterial> dict, string? id)
    {
        if (id != null && dict.TryGetValue(id, out var mat)) return mat;
        return new Lambertian(new Vector3(0.5f));
    }

    private static Vector3? ToVector3(List<float>? list)
    {
        if (list == null || list.Count < 3) return null;
        return new Vector3(list[0], list[1], list[2]);
    }
}
