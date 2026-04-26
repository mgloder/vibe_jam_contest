using Godot;
using System.Collections.Generic;

public enum TerrainType
{
	None,
	Grass,
	Forest,
	Mountain,
	Water,
	Road,
	Building
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

		// 2) Independent natural terrains: tuned to reduce water and boost mountain.
		PaintGaussianBlobs(terrain, rng, TerrainType.Water, blobCount: 10, minRadius: maxDim * 0.02f, maxRadius: maxDim * 0.055f, replaceFilter: TerrainType.Grass, smoothness: smooth);
		PaintGaussianBlobs(terrain, rng, TerrainType.Mountain, blobCount: 30, minRadius: maxDim * 0.03f, maxRadius: maxDim * 0.082f, replaceFilter: TerrainType.Grass, smoothness: smooth);

		// 3) Forests: only on grass.
		PaintGaussianBlobs(terrain, rng, TerrainType.Forest, blobCount: 20, minRadius: maxDim * 0.02f, maxRadius: maxDim * 0.048f, replaceFilter: TerrainType.Grass, smoothness: smooth);
		var smoothPasses = 1 + Mathf.RoundToInt(smooth * 8f); // 1..9 passes
		SmoothNaturalAdjacency(terrain, passes: smoothPasses);

		// 4) Buildings (town patches): only on grass, never on water/mountain/forest.
		var townCenters = PaintBuildingPatchesOnGrass(terrain, rng);
		SoftenBuildingEdges(terrain);

		// 5) Roads: only laid onto grass cells.
		PaintRoadsOnGrass(terrain, townCenters);
		SoftenRoadEdges(terrain);
	}

	public static Color TerrainToColor(TerrainType terrain, int x, int y)
	{
		var checker = ((x + y) & 1) == 0;
		return terrain switch
		{
			TerrainType.None => checker ? new Color(0.13f, 0.15f, 0.21f) : new Color(0.11f, 0.13f, 0.18f),
			TerrainType.Grass => checker ? new Color(0.22f, 0.36f, 0.20f) : new Color(0.20f, 0.33f, 0.18f),
			TerrainType.Forest => checker ? new Color(0.11f, 0.25f, 0.13f) : new Color(0.09f, 0.22f, 0.11f),
			TerrainType.Mountain => checker ? new Color(0.38f, 0.40f, 0.42f) : new Color(0.33f, 0.35f, 0.37f),
			TerrainType.Water => checker ? new Color(0.14f, 0.29f, 0.49f) : new Color(0.12f, 0.25f, 0.43f),
			TerrainType.Road => checker ? new Color(0.42f, 0.37f, 0.30f) : new Color(0.37f, 0.32f, 0.26f),
			TerrainType.Building => checker ? new Color(0.62f, 0.48f, 0.32f) : new Color(0.56f, 0.42f, 0.27f),
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

	private static Godot.Collections.Array<Vector2I> PaintBuildingPatchesOnGrass(TerrainType[,] terrain, RandomNumberGenerator rng)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var centers = new Godot.Collections.Array<Vector2I>();

		var targetTowns = 9;
		for (var t = 0; t < targetTowns; t++)
		{
			var cx = Mathf.Clamp(Mathf.RoundToInt(NextGaussian(rng, width * 0.5f, width * 0.2f)), 0, width - 1);
			var cy = Mathf.Clamp(Mathf.RoundToInt(NextGaussian(rng, height * 0.5f, height * 0.2f)), 0, height - 1);
			if (terrain[cx, cy] != TerrainType.Grass)
				continue;

			centers.Add(new Vector2I(cx, cy));
			var patchRadius = rng.RandfRange(width * 0.01f, width * 0.02f);
			var radiusSq = patchRadius * patchRadius;
			var minX = Mathf.Max(0, Mathf.FloorToInt(cx - patchRadius));
			var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + patchRadius));
			var minY = Mathf.Max(0, Mathf.FloorToInt(cy - patchRadius));
			var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + patchRadius));

			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					if (terrain[x, y] != TerrainType.Grass)
						continue;

					var dx = x - cx;
					var dy = y - cy;
					var d2 = dx * dx + dy * dy;
					if (d2 > radiusSq)
						continue;
					if (rng.Randf() < 0.56f)
						terrain[x, y] = TerrainType.Building;
				}
			}
		}

		return centers;
	}

	private static void PaintRoadsOnGrass(TerrainType[,] terrain, Godot.Collections.Array<Vector2I> centers)
	{
		if (centers.Count < 2)
			return;

		for (var i = 1; i < centers.Count; i++)
		{
			var from = centers[i - 1];
			var to = centers[i];
			PaintRoadPath(terrain, from, to);
		}
	}

	private static void PaintRoadPath(TerrainType[,] terrain, Vector2I from, Vector2I to)
	{
		var x = from.X;
		var y = from.Y;
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);

		var maxSteps = width + height;
		for (var step = 0; step < maxSteps && (x != to.X || y != to.Y); step++)
		{
			if (x != to.X)
				x += x < to.X ? 1 : -1;
			else if (y != to.Y)
				y += y < to.Y ? 1 : -1;

			if (x < 0 || y < 0 || x >= width || y >= height)
				break;

			// Rule: roads only on grass.
			if (terrain[x, y] == TerrainType.Grass)
				terrain[x, y] = TerrainType.Road;
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

	private static void SoftenBuildingEdges(TerrainType[,] terrain)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var buffer = new TerrainType[width, height];
		CopyTerrain(terrain, buffer);

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				if (terrain[x, y] != TerrainType.Building)
					continue;

				var nearbyBuildings = CountNeighborsOfType(terrain, x, y, TerrainType.Building);
				// Remove tiny specks to make building patches smoother.
				if (nearbyBuildings <= 1)
					buffer[x, y] = TerrainType.Grass;
			}
		}

		CopyTerrain(buffer, terrain);
	}

	private static void SoftenRoadEdges(TerrainType[,] terrain)
	{
		var width = terrain.GetLength(0);
		var height = terrain.GetLength(1);
		var buffer = new TerrainType[width, height];
		CopyTerrain(terrain, buffer);

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				if (terrain[x, y] != TerrainType.Road)
					continue;

				var nearbyRoads = CountNeighborsOfType(terrain, x, y, TerrainType.Road);
				// Remove isolated road singletons for cleaner transitions.
				if (nearbyRoads == 0)
					buffer[x, y] = TerrainType.Grass;
			}
		}

		CopyTerrain(buffer, terrain);
	}

	private static bool IsNatural(TerrainType t)
	{
		return t == TerrainType.Grass || t == TerrainType.Forest || t == TerrainType.Mountain || t == TerrainType.Water;
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
		var mountain = CountNeighborsOfType(terrain, x, y, TerrainType.Mountain);
		var water = CountNeighborsOfType(terrain, x, y, TerrainType.Water);

		// Slight weighting bias to encourage more mountain and less water during smoothing.
		var mountainWeighted = mountain + 1;
		var waterWeighted = Mathf.Max(0, water - 1);

		var max = grass;
		var type = TerrainType.Grass;
		if (forest > max)
		{
			max = forest;
			type = TerrainType.Forest;
		}
		if (mountainWeighted > max)
		{
			max = mountainWeighted;
			type = TerrainType.Mountain;
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
