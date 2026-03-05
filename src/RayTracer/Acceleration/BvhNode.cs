using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// Bounding Volume Hierarchy node for O(log N) ray intersection.
/// Recursively splits the object list by a random axis.
/// </summary>
public class BvhNode : IHittable
{
    private readonly IHittable _left;
    private readonly IHittable _right;
    private readonly AABB _box;

    public BvhNode(List<IHittable> objects, int start, int end)
    {
        int axis = Random.Shared.Next(3);
        Comparison<IHittable> comparator = axis switch
        {
            0 => (a, b) => CompareAxis(a, b, 0),
            1 => (a, b) => CompareAxis(a, b, 1),
            _ => (a, b) => CompareAxis(a, b, 2)
        };

        int span = end - start;

        if (span == 1)
        {
            _left = _right = objects[start];
        }
        else if (span == 2)
        {
            if (comparator(objects[start], objects[start + 1]) < 0)
            {
                _left = objects[start];
                _right = objects[start + 1];
            }
            else
            {
                _left = objects[start + 1];
                _right = objects[start];
            }
        }
        else
        {
            objects.Sort(start, span, Comparer<IHittable>.Create(comparator));
            int mid = start + span / 2;
            _left = new BvhNode(objects, start, mid);
            _right = new BvhNode(objects, mid, end);
        }

        _box = AABB.SurroundingBox(_left.BoundingBox(), _right.BoundingBox());
    }

    public BvhNode(List<IHittable> objects) : this(objects, 0, objects.Count) { }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_box.Hit(ray, tMin, tMax))
            return false;

        bool hitLeft = _left.Hit(ray, tMin, tMax, ref rec);
        bool hitRight = _right.Hit(ray, tMin, hitLeft ? rec.T : tMax, ref rec);
        return hitLeft || hitRight;
    }

    public AABB BoundingBox() => _box;

    private static int CompareAxis(IHittable a, IHittable b, int axis)
    {
        var boxA = a.BoundingBox();
        var boxB = b.BoundingBox();
        float va = axis switch { 0 => boxA.Min.X, 1 => boxA.Min.Y, _ => boxA.Min.Z };
        float vb = axis switch { 0 => boxB.Min.X, 1 => boxB.Min.Y, _ => boxB.Min.Z };
        return va.CompareTo(vb);
    }
}
