using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;

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
        var color = ToVector3(m.Color) ?? new Vector3(0.5f);
        return m.Type?.ToLowerInvariant() switch
        {
            "lambertian" => new Lambertian(color),
            "metal" => new Metal(color, m.Fuzz),
            "dielectric" => new Dielectric(m.RefractionIndex),
            _ => new Lambertian(color)
        };
    }

    private static IHittable? CreateEntity(EntityData e, IMaterial mat)
    {
        return e.Type?.ToLowerInvariant() switch
        {
            "sphere" => new Sphere(
                ToVector3(e.Center) ?? Vector3.Zero, e.Radius, mat),
            "box" => new Box(
                ToVector3(e.Min) ?? -Vector3.One,
                ToVector3(e.Max) ?? Vector3.One, mat),
            "triangle" => new Triangle(
                ToVector3(e.V0) ?? Vector3.Zero,
                ToVector3(e.V1) ?? Vector3.UnitX,
                ToVector3(e.V2) ?? Vector3.UnitY, mat),
            "cylinder" => new Cylinder(
                ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.Height, mat),
            "plane" or "infinite_plane" => new InfinitePlane(
                ToVector3(e.Point) ?? Vector3.Zero,
                ToVector3(e.Normal) ?? Vector3.UnitY, mat),
            _ => null
        };
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
