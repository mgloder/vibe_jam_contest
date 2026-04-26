using Godot;
using System;
using System.Collections.Generic;

public static class GridAStar
{
	/// <summary>4-neighbor weighted A*; cost is paid to *enter* the cell.</summary>
	public static List<Vector2I>? FindPath(
		int width,
		int height,
		Vector2I start,
		Vector2I goal,
		Func<Vector2I, bool> isWalkable,
		Func<Vector2I, int> moveCost
	)
	{
		if (start == goal)
			return new List<Vector2I> { start };

		if (start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height)
			return null;
		if (goal.X < 0 || goal.Y < 0 || goal.X >= width || goal.Y >= height)
			return null;
		if (!isWalkable(start) || !isWalkable(goal))
			return null;

		var gScore = new Dictionary<Vector2I, int>();
		var came = new Dictionary<Vector2I, Vector2I>();
		var open = new PriorityQueue<Vector2I, int>();
		var closed = new HashSet<Vector2I>();

		gScore[start] = 0;
		open.Enqueue(start, Heuristic(start, goal));

		var dirs = new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };

		while (open.Count > 0)
		{
			open.TryDequeue(out var current, out _);
			if (closed.Contains(current))
				continue;
			closed.Add(current);
			if (current == goal)
				return Reconstruct(came, start, current);

			if (!gScore.TryGetValue(current, out var currentG) || currentG == int.MaxValue)
				continue;

			for (var d = 0; d < dirs.Length; d++)
			{
				var next = current + dirs[d];
				if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height)
					continue;
				if (!isWalkable(next))
					continue;

				var cost = moveCost(next);
				if (cost < 0 || cost >= int.MaxValue / 2)
					continue;
				var tentative = currentG + cost;
				if (tentative < gScore.GetValueOrDefault(next, int.MaxValue))
				{
					came[next] = current;
					gScore[next] = tentative;
					var f = tentative + Heuristic(next, goal);
					open.Enqueue(next, f);
				}
			}
		}

		return null;
	}

	private static int Heuristic(Vector2I a, Vector2I b)
	{
		return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y);
	}

	private static List<Vector2I> Reconstruct(Dictionary<Vector2I, Vector2I> came, Vector2I start, Vector2I goal)
	{
		var list = new List<Vector2I>();
		var at = goal;
		while (at != start)
		{
			list.Add(at);
			at = came[at];
		}

		list.Add(start);
		list.Reverse();
		return list;
	}
}
