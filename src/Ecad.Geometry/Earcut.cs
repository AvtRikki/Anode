using System.Numerics;

namespace Ecad.Geometry;

/// <summary>
/// Ear-clipping triangulation for a single simple polygon (no separate hole rings), following the
/// algorithm of mapbox/earcut (ISC license): z-order hashed ear tests, then local self-intersection
/// cure and polygon splitting as fallbacks. KiCad zone fills are already fractured into single outlines
/// whose holes are joined by zero-width bridges, which these fallbacks handle.
/// </summary>
public static class Earcut
{
    /// <summary>Appends triangle vertex indices (three per triangle) into <paramref name="triangles"/>.</summary>
    public static void Triangulate(ReadOnlySpan<Vector2> points, List<int> triangles)
    {
        int n = points.Length;
        if (n < 3)
        {
            return;
        }

        var outer = LinkedList(points);
        if (outer is null || outer.Next == outer.Prev)
        {
            return;
        }

        double minX = 0, minY = 0, invSize = 0;
        if (n > 80)
        {
            minX = double.MaxValue;
            minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            invSize = Math.Max(maxX - minX, maxY - minY);
            invSize = invSize != 0 ? 32767 / invSize : 0;
        }

        new Context(triangles, minX, minY, invSize).EarcutLinked(outer, 0);
    }

    public static List<int> Triangulate(ReadOnlySpan<Vector2> points)
    {
        var result = new List<int>((points.Length - 2) * 3);
        Triangulate(points, result);
        return result;
    }

    private sealed class Node(int i, double x, double y)
    {
        public readonly int I = i;
        public readonly double X = x;
        public readonly double Y = y;
        public Node Prev = null!;
        public Node Next = null!;
        public int Z;
        public Node? PrevZ;
        public Node? NextZ;
    }

    private readonly struct Context(List<int> triangles, double minX, double minY, double invSize)
    {
        public void EarcutLinked(Node? ear, int pass)
        {
            if (ear is null)
            {
                return;
            }

            if (pass == 0 && invSize != 0)
            {
                IndexCurve(ear);
            }

            var stop = ear;
            while (ear.Prev != ear.Next)
            {
                var prev = ear.Prev;
                var next = ear.Next;

                if (invSize != 0 ? IsEarHashed(ear) : IsEar(ear))
                {
                    triangles.Add(prev.I);
                    triangles.Add(ear.I);
                    triangles.Add(next.I);
                    RemoveNode(ear);
                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }

                ear = next;
                if (ear == stop)
                {
                    if (pass == 0)
                    {
                        EarcutLinked(FilterPoints(ear, null), 1);
                    }
                    else if (pass == 1)
                    {
                        var cured = CureLocalIntersections(FilterPoints(ear, null)!);
                        EarcutLinked(cured, 2);
                    }
                    else
                    {
                        SplitEarcut(ear);
                    }

                    break;
                }
            }
        }

        private static bool IsEar(Node ear)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;
            if (Area(a, b, c) >= 0)
            {
                return false;
            }

            double x0 = Math.Min(a.X, Math.Min(b.X, c.X)), y0 = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double x1 = Math.Max(a.X, Math.Max(b.X, c.X)), y1 = Math.Max(a.Y, Math.Max(b.Y, c.Y));

            for (var p = c.Next; p != a; p = p.Next)
            {
                if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1
                    && PointInTriangleExceptFirst(a, b, c, p) && Area(p.Prev, p, p.Next) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsEarHashed(Node ear)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;
            if (Area(a, b, c) >= 0)
            {
                return false;
            }

            double x0 = Math.Min(a.X, Math.Min(b.X, c.X)), y0 = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double x1 = Math.Max(a.X, Math.Max(b.X, c.X)), y1 = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            int minZ = ZOrder(x0, y0);
            int maxZ = ZOrder(x1, y1);

            bool Blocks(Node p) =>
                p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 && p != a && p != c
                && PointInTriangleExceptFirst(a, b, c, p) && Area(p.Prev, p, p.Next) >= 0;

            var pz = ear.PrevZ;
            var nz = ear.NextZ;
            while (pz is not null && pz.Z >= minZ && nz is not null && nz.Z <= maxZ)
            {
                if (Blocks(pz))
                {
                    return false;
                }

                pz = pz.PrevZ;
                if (Blocks(nz))
                {
                    return false;
                }

                nz = nz.NextZ;
            }

            for (; pz is not null && pz.Z >= minZ; pz = pz.PrevZ)
            {
                if (Blocks(pz))
                {
                    return false;
                }
            }

            for (; nz is not null && nz.Z <= maxZ; nz = nz.NextZ)
            {
                if (Blocks(nz))
                {
                    return false;
                }
            }

            return true;
        }

        private Node CureLocalIntersections(Node start)
        {
            var p = start;
            do
            {
                var a = p.Prev;
                var b = p.Next.Next;
                if (!Equals(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.I);
                    triangles.Add(p.I);
                    triangles.Add(b.I);
                    RemoveNode(p);
                    RemoveNode(p.Next);
                    p = start = b;
                }

                p = p.Next;
            }
            while (p != start);

            return FilterPoints(p, null)!;
        }

        private void SplitEarcut(Node start)
        {
            var a = start;
            do
            {
                var b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        var c = SplitPolygon(a, b);
                        a = FilterPoints(a, a.Next)!;
                        c = FilterPoints(c, c.Next)!;
                        EarcutLinked(a, 0);
                        EarcutLinked(c, 0);
                        return;
                    }

                    b = b.Next;
                }

                a = a.Next;
            }
            while (a != start);
        }

        private void IndexCurve(Node start)
        {
            var p = start;
            do
            {
                if (p.Z == 0)
                {
                    p.Z = ZOrder(p.X, p.Y);
                }

                p.PrevZ = p.Prev;
                p.NextZ = p.Next;
                p = p.Next;
            }
            while (p != start);

            p.PrevZ!.NextZ = null;
            p.PrevZ = null;
            SortLinked(p);
        }

        private int ZOrder(double px, double py)
        {
            int x = (int)((px - minX) * invSize);
            int y = (int)((py - minY) * invSize);

            x = (x | (x << 8)) & 0x00FF00FF;
            x = (x | (x << 4)) & 0x0F0F0F0F;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;

            y = (y | (y << 8)) & 0x00FF00FF;
            y = (y | (y << 4)) & 0x0F0F0F0F;
            y = (y | (y << 2)) & 0x33333333;
            y = (y | (y << 1)) & 0x55555555;

            return x | (y << 1);
        }
    }

    private static Node? LinkedList(ReadOnlySpan<Vector2> points)
    {
        Node? last = null;
        if (SignedArea(points) > 0)
        {
            for (int i = 0; i < points.Length; i++)
            {
                last = InsertNode(i, points[i].X, points[i].Y, last);
            }
        }
        else
        {
            for (int i = points.Length - 1; i >= 0; i--)
            {
                last = InsertNode(i, points[i].X, points[i].Y, last);
            }
        }

        if (last is not null && Equals(last, last.Next))
        {
            RemoveNode(last);
            last = last.Next;
        }

        return last;
    }

    private static double SignedArea(ReadOnlySpan<Vector2> points)
    {
        double sum = 0;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            sum += ((double)points[j].X - points[i].X) * ((double)points[i].Y + points[j].Y);
        }

        return sum;
    }

    private static Node? FilterPoints(Node? start, Node? end)
    {
        if (start is null)
        {
            return start;
        }

        end ??= start;
        var p = start;
        bool again;
        do
        {
            again = false;
            if (Equals(p, p.Next) || Area(p.Prev, p, p.Next) == 0)
            {
                RemoveNode(p);
                p = end = p.Prev;
                if (p == p.Next)
                {
                    break;
                }

                again = true;
            }
            else
            {
                p = p.Next;
            }
        }
        while (again || p != end);

        return end;
    }

    private static void SortLinked(Node list)
    {
        int inSize = 1;
        int numMerges;
        Node? head = list;
        do
        {
            var p = head;
            head = null;
            Node? tail = null;
            numMerges = 0;

            while (p is not null)
            {
                numMerges++;
                var q = p;
                int pSize = 0;
                for (int i = 0; i < inSize; i++)
                {
                    pSize++;
                    q = q.NextZ;
                    if (q is null)
                    {
                        break;
                    }
                }

                int qSize = inSize;
                while (pSize > 0 || (qSize > 0 && q is not null))
                {
                    Node e;
                    if (pSize != 0 && (qSize == 0 || q is null || p!.Z <= q.Z))
                    {
                        e = p!;
                        p = p!.NextZ;
                        pSize--;
                    }
                    else
                    {
                        e = q!;
                        q = q!.NextZ;
                        qSize--;
                    }

                    if (tail is not null)
                    {
                        tail.NextZ = e;
                    }
                    else
                    {
                        head = e;
                    }

                    e.PrevZ = tail;
                    tail = e;
                }

                p = q;
            }

            tail!.NextZ = null;
            inSize *= 2;
        }
        while (numMerges > 1);
    }

    private static bool PointInTriangleExceptFirst(Node a, Node b, Node c, Node p) =>
        !(a.X == p.X && a.Y == p.Y) && PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y);

    private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
        (cx - px) * (ay - py) >= (ax - px) * (cy - py)
        && (ax - px) * (by - py) >= (bx - px) * (ay - py)
        && (bx - px) * (cy - py) >= (cx - px) * (by - py);

    private static bool IsValidDiagonal(Node a, Node b) =>
        a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b)
        && ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b)
             && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0))
            || (Equals(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));

    private static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

    private static bool Equals(Node p1, Node p2) => p1.X == p2.X && p1.Y == p2.Y;

    private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
    {
        int o1 = Math.Sign(Area(p1, q1, p2));
        int o2 = Math.Sign(Area(p1, q1, q2));
        int o3 = Math.Sign(Area(p2, q2, p1));
        int o4 = Math.Sign(Area(p2, q2, q1));

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return (o1 == 0 && OnSegment(p1, p2, q1))
               || (o2 == 0 && OnSegment(p1, q2, q1))
               || (o3 == 0 && OnSegment(p2, p1, q2))
               || (o4 == 0 && OnSegment(p2, q1, q2));
    }

    private static bool OnSegment(Node p, Node q, Node r) =>
        q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

    private static bool IntersectsPolygon(Node a, Node b)
    {
        var p = a;
        do
        {
            if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b))
            {
                return true;
            }

            p = p.Next;
        }
        while (p != a);

        return false;
    }

    private static bool LocallyInside(Node a, Node b) =>
        Area(a.Prev, a, a.Next) < 0
            ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
            : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

    private static bool MiddleInside(Node a, Node b)
    {
        var p = a;
        bool inside = false;
        double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
        do
        {
            if ((p.Y > py) != (p.Next.Y > py) && p.Next.Y != p.Y
                && px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)
            {
                inside = !inside;
            }

            p = p.Next;
        }
        while (p != a);

        return inside;
    }

    private static Node SplitPolygon(Node a, Node b)
    {
        var a2 = new Node(a.I, a.X, a.Y);
        var b2 = new Node(b.I, b.X, b.Y);
        var an = a.Next;
        var bp = b.Prev;

        a.Next = b;
        b.Prev = a;

        a2.Next = an;
        an.Prev = a2;

        b2.Next = a2;
        a2.Prev = b2;

        bp.Next = b2;
        b2.Prev = bp;

        return b2;
    }

    private static Node InsertNode(int i, double x, double y, Node? last)
    {
        var p = new Node(i, x, y);
        if (last is null)
        {
            p.Prev = p;
            p.Next = p;
        }
        else
        {
            p.Next = last.Next;
            p.Prev = last;
            last.Next.Prev = p;
            last.Next = p;
        }

        return p;
    }

    private static void RemoveNode(Node p)
    {
        p.Next.Prev = p.Prev;
        p.Prev.Next = p.Next;
        if (p.PrevZ is not null)
        {
            p.PrevZ.NextZ = p.NextZ;
        }

        if (p.NextZ is not null)
        {
            p.NextZ.PrevZ = p.PrevZ;
        }
    }
}
