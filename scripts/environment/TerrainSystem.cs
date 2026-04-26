using Godot;

public enum TerrainType
{
	None,
	Grass,
	Forest,
	Mountain,
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
			_ => new Color(0.2f, 0.2f, 0.2f)
		};
	}
}
