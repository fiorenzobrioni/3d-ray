using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// Bounding Volume Hierarchy node for O(log N) ray intersection.
/// Recursively splits the object list by a random axis.
/// </summary>
public class BvhNode : IHittable
{
    public int Seed { get; set; }
    private readonly IHittable _left;
    private readonly IHittable _right;
    private readonly AABB _box;

    public BvhNode(List<IHittable> objects, int start, int end)
    {
        // Determine longest axis from the centroid bounds for optimal splitting
        int axis = ComputeLongestAxis(objects, start, end);
        Comparison<IHittable> comparator = (a, b) => CompareAxis(a, b, axis);

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

    /// <summary>
    /// Selects the axis (0=X, 1=Y, 2=Z) with the longest extent across the
    /// centroids of the objects in [start, end), for optimal BVH splitting.
    /// </summary>
    private static int ComputeLongestAxis(List<IHittable> objects, int start, int end)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (int i = start; i < end; i++)
        {
            var box = objects[i].BoundingBox();
            // Use centroid of each AABB
            float cx = (box.Min.X + box.Max.X) * 0.5f;
            float cy = (box.Min.Y + box.Max.Y) * 0.5f;
            float cz = (box.Min.Z + box.Max.Z) * 0.5f;

            if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
            if (cz < minZ) minZ = cz; if (cz > maxZ) maxZ = cz;
        }

        float extX = maxX - minX;
        float extY = maxY - minY;
        float extZ = maxZ - minZ;

        if (extX >= extY && extX >= extZ) return 0;
        if (extY >= extZ) return 1;
        return 2;
    }
}

