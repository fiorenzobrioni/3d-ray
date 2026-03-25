using RayTracer.Core;

namespace RayTracer.Geometry;

public class HittableList : IHittable
{
    public int Seed { get; set; }
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

        // OPT-01: indexed for loop avoids IEnumerator<T> heap allocation.
        // HittableList is on the hot rendering path (infinite planes + small scenes).
        var list = Objects;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Hit(ray, tMin, closest, ref tempRec))
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
