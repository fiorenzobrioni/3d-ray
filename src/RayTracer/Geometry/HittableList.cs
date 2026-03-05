using RayTracer.Core;

namespace RayTracer.Geometry;

public class HittableList : IHittable
{
    public List<IHittable> Objects { get; } = new();

    public HittableList() { }

    public HittableList(IEnumerable<IHittable> objects)
    {
        Objects.AddRange(objects);
    }

    public void Add(IHittable obj) => Objects.Add(obj);

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        var tempRec = new HitRecord();
        bool hitAnything = false;
        float closest = tMax;

        foreach (var obj in Objects)
        {
            if (obj.Hit(ray, tMin, closest, ref tempRec))
            {
                hitAnything = true;
                closest = tempRec.T;
                rec = tempRec;
            }
        }
        return hitAnything;
    }

    public AABB BoundingBox()
    {
        if (Objects.Count == 0) return AABB.Empty;

        var box = Objects[0].BoundingBox();
        for (int i = 1; i < Objects.Count; i++)
            box = AABB.SurroundingBox(box, Objects[i].BoundingBox());
        return box;
    }
}
