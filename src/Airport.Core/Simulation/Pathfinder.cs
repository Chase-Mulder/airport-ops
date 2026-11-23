using Airport.Core.Domain;

namespace Airport.Core.Simulation;

/// <summary>A* search over any grid or graph.</summary>
public static class Pathfinder
{
    private static readonly (int dx, int dy)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// Returns the cells after <paramref name="start"/> up to and including <paramref name="goal"/>,
    /// an empty list when start == goal, or null when no route exists.
    /// The goal is always enterable even if it is not passable (depots, stands) or blocked.
    /// </summary>
    public static List<Cell>? FindPath(
        Cell start,
        Cell goal,
        Func<Cell, IEnumerable<Cell>> neighbors,
        Func<Cell, bool> passable,
        IReadOnlySet<Cell>? blocked = null)
    {
        if (start == goal) return new List<Cell>();

        var open = new PriorityQueue<Cell, int>();
        var cameFrom = new Dictionary<Cell, Cell>();
        var gScore = new Dictionary<Cell, int> { [start] = 0 };
        open.Enqueue(start, start.Manhattan(goal));

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal) return Reconstruct(cameFrom, current);

            var currentG = gScore[current];
            foreach (var next in neighbors(current))
            {
                var isGoal = next == goal;
                if (!isGoal && !passable(next)) continue;
                if (!isGoal && blocked is not null && blocked.Contains(next)) continue;

                var tentative = currentG + 1;
                if (gScore.TryGetValue(next, out var existing) && tentative >= existing) continue;

                gScore[next] = tentative;
                cameFrom[next] = current;
                open.Enqueue(next, tentative + next.Manhattan(goal));
            }
        }
        return null;
    }

    /// <summary>4-connected neighbors inside a width × height grid.</summary>
    public static Func<Cell, IEnumerable<Cell>> GridNeighbors(int width, int height) => c =>
        Directions
            .Select(d => new Cell(c.X + d.dx, c.Y + d.dy))
            .Where(n => n.X >= 0 && n.Y >= 0 && n.X < width && n.Y < height);

    /// <summary>Neighbors following the edges of a directed graph.</summary>
    public static Func<Cell, IEnumerable<Cell>> GraphNeighbors(IReadOnlyDictionary<Cell, List<Cell>> graph) => c =>
        graph.TryGetValue(c, out var list) ? list : Enumerable.Empty<Cell>();

    private static List<Cell> Reconstruct(Dictionary<Cell, Cell> cameFrom, Cell current)
    {
        var path = new List<Cell> { current };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            path.Add(current);
        }
        path.Reverse();
        path.RemoveAt(0); // drop the start cell
        return path;
    }
}
