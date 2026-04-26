using Godot;
using System;
using System.Collections.Generic;

public enum ColonyCharacterType
{
	Civilian,
	Expert,
	Soldier
}

public enum CharacterToolType
{
	None,
	Hammer,
	ShortGun
}

public sealed class ColonyCharacter
{
	public string Id { get; }
	public ColonyCharacterType Type { get; }
	public CharacterToolType Tool { get; }
	public string DisplayName { get; set; }
	public Vector2I Cell { get; set; }
	public Vector2I Destination { get; set; }
	/// <summary>Current hit points. Civilian: 1; Soldier: 2; Expert: 4.</summary>
	public int Health { get; set; }
	public int MaxHealth { get; }
	/// <summary>Attack power. Civilian: 0; Soldier: 1; Expert: 3.</summary>
	public int Attack { get; }
	/// <summary>True if <see cref="Attack"/> is greater than zero.</summary>
	public bool CanAttack => Attack > 0;
	public string[] SpriteRows { get; }
	public Vector2I SpritePivot { get; }
	public IReadOnlyDictionary<char, Color> Palette { get; }
	public string[] ToolRows { get; }
	public Vector2I ToolPivot { get; }
	public IReadOnlyDictionary<char, Color> ToolPalette { get; }

	private ColonyCharacter(
		string id,
		ColonyCharacterType type,
		CharacterToolType tool,
		string displayName,
		Vector2I cell,
		Vector2I destination,
		int maxHealth,
		int health,
		int attack,
		string[] spriteRows,
		Vector2I spritePivot,
		IReadOnlyDictionary<char, Color> palette,
		string[] toolRows,
		Vector2I toolPivot,
		IReadOnlyDictionary<char, Color> toolPalette
	)
	{
		Id = id;
		Type = type;
		Tool = tool;
		DisplayName = displayName;
		Cell = cell;
		Destination = destination;
		MaxHealth = maxHealth;
		Health = health;
		Attack = attack;
		SpriteRows = spriteRows;
		SpritePivot = spritePivot;
		Palette = palette;
		ToolRows = toolRows;
		ToolPivot = toolPivot;
		ToolPalette = toolPalette;
	}

	public static ColonyCharacter CreateStarter(Vector2I cell)
	{
		return CreateByType(ColonyCharacterType.Civilian, cell, "Starter Civilian");
	}

	public static ColonyCharacter CreateByType(ColonyCharacterType type, Vector2I cell, string? displayName = null, Vector2I? destination = null)
	{
		var (spriteRows, palette, toolRows, toolPalette, tool, defaultName) = type switch
		{
			ColonyCharacterType.Civilian => BuildCivilianVisual(),
			ColonyCharacterType.Expert => BuildExpertVisual(),
			ColonyCharacterType.Soldier => BuildSoldierVisual(),
			_ => BuildCivilianVisual()
		};

		var (maxHealth, attack) = type switch
		{
			ColonyCharacterType.Civilian => (1, 0),
			ColonyCharacterType.Expert => (4, 3),
			ColonyCharacterType.Soldier => (2, 1),
			_ => (1, 0)
		};

		return new ColonyCharacter(
			id: Guid.NewGuid().ToString("N"),
			type: type,
			tool: tool,
			displayName: displayName ?? defaultName,
			cell: cell,
			destination: destination ?? cell,
			maxHealth: maxHealth,
			health: maxHealth,
			attack: attack,
			spriteRows: spriteRows,
			spritePivot: new Vector2I(3, 4),
			palette: palette,
			toolRows: toolRows,
			toolPivot: new Vector2I(3, 4),
			toolPalette: toolPalette
		);
	}

	public static ColonyCharacter CreateRandomized(Vector2I cell, RandomNumberGenerator rng)
	{
		var type = (ColonyCharacterType)rng.RandiRange(0, 2);
		var name = $"{type} {rng.RandiRange(100, 999)}";
		return CreateByType(type, cell, name);
	}

	private static (string[] spriteRows, Dictionary<char, Color> palette, string[] toolRows, Dictionary<char, Color> toolPalette, CharacterToolType tool, string defaultName) BuildCivilianVisual()
	{
		return (
			new[]
			{
				"..GGG..",
				".GSSSG.",
				".GSSSG.",
				"..BBB..",
				".BBTBB.",
				".BBTBB.",
				"..LLL..",
				".L...L.",
				"L.....L"
			},
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.16f, 0.17f, 0.20f),
				['S'] = new Color(0.95f, 0.81f, 0.68f),
				['B'] = new Color(0.38f, 0.66f, 0.96f),
				['T'] = new Color(0.10f, 0.14f, 0.22f),
				['L'] = new Color(0.30f, 0.35f, 0.43f)
			},
			Array.Empty<string>(),
			new Dictionary<char, Color>(),
			CharacterToolType.None,
			"Civilian"
		);
	}

	private static (string[] spriteRows, Dictionary<char, Color> palette, string[] toolRows, Dictionary<char, Color> toolPalette, CharacterToolType tool, string defaultName) BuildExpertVisual()
	{
		return (
			new[]
			{
				"...G...",
				"..SSS..",
				".SSSSS.",
				"..CCC..",
				".CCTCC.",
				".CCTCC.",
				"..PPP..",
				".P...P.",
				"P.....P"
			},
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.12f, 0.34f, 0.18f),
				['S'] = new Color(0.92f, 0.72f, 0.58f),
				['C'] = new Color(0.34f, 0.88f, 0.62f),
				['T'] = new Color(0.08f, 0.12f, 0.24f),
				['P'] = new Color(0.30f, 0.23f, 0.42f)
			},
			new[]
			{
				".......",
				".......",
				".......",
				".....M.",
				"....MMM",
				".....M.",
				"...HH..",
				"...H...",
				"...H..."
			},
			new Dictionary<char, Color>
			{
				['M'] = new Color(0.58f, 0.60f, 0.64f), // hammer head
				['H'] = new Color(0.48f, 0.30f, 0.16f)  // wooden handle
			},
			CharacterToolType.Hammer,
			"Expert"
		);
	}

	private static (string[] spriteRows, Dictionary<char, Color> palette, string[] toolRows, Dictionary<char, Color> toolPalette, CharacterToolType tool, string defaultName) BuildSoldierVisual()
	{
		return (
			new[]
			{
				"..HHH..",
				".HSSSH.",
				"..SSS..",
				".JJJJJ.",
				".JJTJJ.",
				"..JJJ..",
				"..KKK..",
				".K...K.",
				"K.....K"
			},
			new Dictionary<char, Color>
			{
				['H'] = new Color(0.74f, 0.52f, 0.22f),
				['S'] = new Color(0.78f, 0.58f, 0.44f),
				['J'] = new Color(0.47f, 0.83f, 0.57f),
				['T'] = new Color(0.16f, 0.10f, 0.08f),
				['K'] = new Color(0.22f, 0.27f, 0.42f)
			},
			new[]
			{
				".......",
				".......",
				"...GGGG",
				"..GGBBG",
				"..GGGGG",
				"..G....",
				".......",
				".......",
				"......."
			},
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.18f, 0.20f, 0.24f), // gun body
				['B'] = new Color(0.48f, 0.52f, 0.58f)  // gun highlights
			},
			CharacterToolType.ShortGun,
			"Soldier"
		);
	}
}
