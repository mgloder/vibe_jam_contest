using Godot;
using System.Collections.Generic;

public enum TerrainType
{
	None,
	Grass,
	Forest,
	Water
}

public static class TerrainSystem
{
	public static void InitializeEmpty(TerrainType[,] terrain)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
				terrain[x, y] = TerrainType.None;
		}
	}

	public static void InitializeGaussianConstrained(TerrainType[,] terrain, RandomNumberGenerator rng, float smoothness)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var maxDim = Mathf.Max(width, height);
		var smooth = Mathf.Clamp(smoothness, 0f, 1f);

		// 1) Base: start with grass as the core terrain.
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
				terrain[x, y] = TerrainType.Grass;
		}

		// 2) Independent natural terrains.
		PaintGaussianBlobs(terrain, rng, TerrainType.Water, blobCount: 10, minRadius: maxDim * 0.02f, maxRadius: maxDim * 0.055f, replaceFilter: TerrainType.Grass, smoothness: smooth);

		// 3) Forests: only on grass (includes prior mountain share).
		PaintGaussianBlobs(terrain, rng, TerrainType.Forest, blobCount: 50, minRadius: maxDim * 0.02f, maxRadius: maxDim * 0.082f, replaceFilter: TerrainType.Grass, smoothness: smooth);
		var smoothPasses = 1 + Mathf.RoundToInt(smooth * 8f); // 1..9 passes
		SmoothNaturalAdjacency(terrain, passes: smoothPasses);

	}

	public static Color TerrainToColor(TerrainType terrain, int x, int y)
	{
		var checker = ((x + y) & 1) == 0;
		return terrain switch
		{
			TerrainType.None => checker ? new Color(0.13f, 0.15f, 0.21f) : new Color(0.11f, 0.13f, 0.18f),
			TerrainType.Grass => checker ? new Color(0.22f, 0.36f, 0.20f) : new Color(0.20f, 0.33f, 0.18f),
			TerrainType.Forest => checker ? new Color(0.11f, 0.25f, 0.13f) : new Color(0.09f, 0.22f, 0.11f),
			TerrainType.Water => checker ? new Color(0.14f, 0.29f, 0.49f) : new Color(0.12f, 0.25f, 0.43f),
			_ => new Color(0.2f, 0.2f, 0.2f)
		};
	}

	private static void PaintGaussianBlobs(
		TerrainType[,] terrain,
		RandomNumberGenerator rng,
		TerrainType paintType,
		int blobCount,
		float minRadius,
		float maxRadius,
		TerrainType replaceFilter,
		float smoothness
	)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var dirs = new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };

		for (var i = 0; i < blobCount; i++)
		{
			var cx = Mathf.Clamp(Mathf.RoundToInt(NextGaussian(rng, width * 0.5f, width * 0.22f)), 0, width - 1);
			var cy = Mathf.Clamp(Mathf.RoundToInt(NextGaussian(rng, height * 0.5f, height * 0.22f)), 0, height - 1);
			var radius = rng.RandfRange(minRadius, maxRadius);
			var radiusSq = radius * radius;
			var center = new Vector2I(cx, cy);
			if (terrain[cx, cy] != replaceFilter)
				continue;

			// Grow one connected region from the seed, bounded by a soft circle.
			var frontier = new List<Vector2I> { center };
			var visited = new HashSet<Vector2I> { center };
			terrain[cx, cy] = paintType;

			var targetCells = Mathf.Max(8, Mathf.RoundToInt(Mathf.Pi * radiusSq * rng.RandfRange(0.35f, 0.68f)));
			var painted = 1;

			while (frontier.Count > 0 && painted < targetCells)
			{
				var pick = rng.RandiRange(0, frontier.Count - 1);
				var current = frontier[pick];
				frontier[pick] = frontier[^1];
				frontier.RemoveAt(frontier.Count - 1);

				ShuffleDirections(dirs, rng);
				for (var d = 0; d < dirs.Length && painted < targetCells; d++)
				{
					var n = current + dirs[d];
					if (!visited.Add(n))
						continue;
					if (n.X < 0 || n.Y < 0 || n.X >= width || n.Y >= height)
						continue;
					if (terrain[n.X, n.Y] != replaceFilter)
						continue;

					var dx = n.X - cx;
					var dy = n.Y - cy;
					var d2 = dx * dx + dy * dy;
					if (d2 > radiusSq)
						continue;

					// High acceptance near center; smoothness raises edge acceptance to make softer transitions.
					var influence = Mathf.Exp(-d2 / (2f * radiusSq * 0.26f));
					var edgeBoost = Mathf.Lerp(0.0f, 0.28f, Mathf.Clamp(smoothness, 0f, 1f));
					var threshold = 0.24f + rng.Randf() * 0.18f - edgeBoost;
					if (influence < threshold)
						continue;

					terrain[n.X, n.Y] = paintType;
					frontier.Add(n);
					painted++;
				}
			}
		}
	}

	private static void ShuffleDirections(Vector2I[] dirs, RandomNumberGenerator rng)
	{
		for (var i = dirs.Length - 1; i > 0; i--)
		{
			var j = rng.RandiRange(0, i);
			(dirs[i], dirs[j]) = (dirs[j], dirs[i]);
		}
	}


	private static void SmoothNaturalAdjacency(TerrainType[,] terrain, int passes)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var buffer = new TerrainType[width, height];

		for (var pass = 0; pass < passes; pass++)
		{
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var current = terrain[x, y];
					if (!IsNatural(current))
					{
						buffer[x, y] = current;
						continue;
					}

					var sameNeighbors = CountNeighborsOfType(terrain, x, y, current);
					if (sameNeighbors >= 3)
					{
						buffer[x, y] = current;
						continue;
					}

					var dominant = MostCommonNaturalNeighbor(terrain, x, y);
					buffer[x, y] = dominant == TerrainType.None ? current : dominant;
				}
			}

			CopyTerrain(buffer, terrain);
		}
	}

	private static bool IsNatural(TerrainType t)
	{
		return t == TerrainType.Grass || t == TerrainType.Forest || t == TerrainType.Water;
	}

	private static int CountNeighborsOfType(TerrainType[,] terrain, int x, int y, TerrainType target)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var count = 0;
		for (var dy = -1; dy <= 1; dy++)
		{
			for (var dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dy == 0)
					continue;
				var nx = x + dx;
				var ny = y + dy;
				if (nx < 0 || ny < 0 || nx >= width || ny >= height)
					continue;
				if (terrain[nx, ny] == target)
					count++;
			}
		}

		return count;
	}

	private static TerrainType MostCommonNaturalNeighbor(TerrainType[,] terrain, int x, int y)
	{
		var grass = CountNeighborsOfType(terrain, x, y, TerrainType.Grass);
		var forest = CountNeighborsOfType(terrain, x, y, TerrainType.Forest);
		var water = CountNeighborsOfType(terrain, x, y, TerrainType.Water);

		// Keep water slightly constrained during smoothing.
		var waterWeighted = Mathf.Max(0, water - 1);

		var max = grass;
		var type = TerrainType.Grass;
		if (forest > max)
		{
			max = forest;
			type = TerrainType.Forest;
		}
		if (waterWeighted > max)
		{
			max = waterWeighted;
			type = TerrainType.Water;
		}

		return max == 0 ? TerrainType.None : type;
	}

	private static void CopyTerrain(TerrainType[,] from, TerrainType[,] to)
	{
		var width = from.GetLength(0);
		var height = from.GetLength(1);
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
				to[x, y] = from[x, y];
		}
	}

	private static float NextGaussian(RandomNumberGenerator rng, float mean, float stdDev)
	{
		var u1 = Mathf.Max(0.0001f, rng.Randf());
		var u2 = Mathf.Max(0.0001f, rng.Randf());
		var randStdNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.Pi * u2);
		return mean + stdDev * randStdNormal;
	}
}
