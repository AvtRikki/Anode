using System.Collections.Concurrent;

namespace Anode.Render;

public static class SceneTriangulator
{
    /// <summary>
    /// Triangulates every polygon of the scene in parallel, largest first, so GPU backends can upload
    /// without stalling the render thread. Polygons that already have triangles are skipped.
    /// </summary>
    public static void Triangulate(IRenderScene scene, CancellationToken cancellationToken = default)
    {
        var polygons = scene.Layers
            .SelectMany(l => l.Polygons)
            .Where(p => !p.IsTriangulated)
            .OrderByDescending(p => p.Points.Length)
            .ToArray();

        // Load-balancing partitioner hands out items one by one, so the few huge zone fills
        // at the front land on different cores instead of one contiguous range.
        Parallel.ForEach(
            Partitioner.Create(polygons, loadBalance: true),
            new ParallelOptions { CancellationToken = cancellationToken },
            polygon => _ = polygon.Triangles);
    }
}
