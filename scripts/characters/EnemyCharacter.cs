using Godot;
using System;
using System.Collections.Generic;

public sealed class EnemyCharacter
{
	public string Id { get; }
	public EnemyType Type { get; }
	public CharacterToolType Tool { get; }
	public string DisplayName { get; set; }
	public Vector2I Cell { get; set; }
	public Vector2I Destination { get; set; }
	public int Health { get; set; }
	public bool IsAlive => Health > 0;
	public int MaxHealth { get; }
	public int Attack { get; }
	public bool CanAttack => Attack > 0;
	/// <summary>Design reach in major rings; scaled by <see cref="GridSimulator.MinGridLineStepCells"/> in <see cref="GridSimulator"/> combat.</summary>
	public int AttackRangeChebyshev { get; }
	public int MaxAttackTargets { get; }
	public float MajorStepsPerSecond { get; }
	public string[] SpriteRows { get; }
	public Vector2I SpritePivot { get; }
	public IReadOnlyDictionary<char, Color> Palette { get; }
	public string[] ToolRows { get; }
	public Vector2I ToolPivot { get; }
	public IReadOnlyDictionary<char, Color> ToolPalette { get; }

	private EnemyCharacter(
		string id,
		EnemyType type,
		CharacterToolType tool,
		string displayName,
		Vector2I cell,
		Vector2I destination,
		int maxHealth,
		int health,
		int attack,
		int attackRangeChebyshev,
		int maxAttackTargets,
		float majorStepsPerSecond,
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
		AttackRangeChebyshev = attackRangeChebyshev;
		MaxAttackTargets = maxAttackTargets;
		MajorStepsPerSecond = majorStepsPerSecond;
		SpriteRows = spriteRows;
		SpritePivot = spritePivot;
		Palette = palette;
		ToolRows = toolRows;
		ToolPivot = toolPivot;
		ToolPalette = toolPalette;
	}

	public static EnemyCharacter CreateByType(EnemyType type, Vector2I cell, string? displayName = null, Vector2I? destination = null)
	{
		var (spriteRows, palette, toolRows, toolPalette, tool, defaultName) = type switch
		{
			EnemyType.Crazy => BuildCrazyVisual(),
			EnemyType.Monster => BuildMonsterVisual(),
			_ => BuildCrazyVisual()
		};

		var (maxHealth, attack, rCheb, maxTargets, majPerS) = type switch
		{
			EnemyType.Crazy => (2, 1, 1, 1, 2.4f),
			EnemyType.Monster => (3, 2, 1, 2, 2.4f),
			_ => (2, 1, 1, 1, 2.4f)
		};

		return new EnemyCharacter(
			Guid.NewGuid().ToString("N"),
			type,
			tool,
			displayName ?? defaultName,
			cell,
			destination ?? cell,
			maxHealth,
			maxHealth,
			attack,
			rCheb,
			maxTargets,
			majPerS,
			spriteRows,
			new Vector2I(3, 4),
			palette,
			toolRows,
			new Vector2I(3, 4),
			toolPalette
		);
	}

	public static EnemyCharacter CreateRandomized(Vector2I cell, RandomNumberGenerator rng)
	{
		var t = (EnemyType)rng.RandiRange(0, 1);
		return CreateByType(t, cell, $"{t} {rng.RandiRange(100, 999)}");
	}

	private static (string[] spriteRows, Dictionary<char, Color> palette, string[] toolRows, Dictionary<char, Color> toolPalette, CharacterToolType tool, string defaultName) BuildCrazyVisual()
	{
		return (
			new[]
			{
				"..RRR..",
				".RBBBR.",
				"..BBB..",
				".OOTO..",
				".OOTOO.",
				"..OOO..",
				"..LLL..",
				".L...L.",
				"L.....L"
			},
			new Dictionary<char, Color>
			{
				['R'] = new Color(0.75f, 0.20f, 0.32f),
				['B'] = new Color(0.85f, 0.25f, 0.30f),
				['O'] = new Color(0.95f, 0.85f, 0.30f),
				['T'] = new Color(0.12f, 0.08f, 0.10f),
				['L'] = new Color(0.35f, 0.22f, 0.28f)
			},
			new[]
			{
				".......",
				".......",
				"....KK.",
				"...KKK.",
				"....K..",
				"....K..",
				"....K..",
				"....K..",
				"......."
			},
			new Dictionary<char, Color> { ['K'] = new Color(0.55f, 0.50f, 0.55f) },
			CharacterToolType.Blade,
			"Crazy"
		);
	}

	private static (string[] spriteRows, Dictionary<char, Color> palette, string[] toolRows, Dictionary<char, Color> toolPalette, CharacterToolType tool, string defaultName) BuildMonsterVisual()
	{
		return (
			new[]
			{
				"..V.V..",
				".VAAAV.",
				".VAAAV.",
				"..PPP..",
				".PPEPP.",
				".PPPPE.",
				"..UUU..",
				".U...U.",
				"U.....U"
			},
			new Dictionary<char, Color>
			{
				['V'] = new Color(0.32f, 0.10f, 0.40f),
				['A'] = new Color(0.22f, 0.12f, 0.32f),
				['P'] = new Color(0.55f, 0.28f, 0.60f),
				['E'] = new Color(0.80f, 0.20f, 0.30f),
				['U'] = new Color(0.28f, 0.18f, 0.38f)
			},
			new[]
			{
				".......",
				".......",
				"....CCC",
				"...CC.C",
				"....CC.",
				"....C..",
				".......",
				".......",
				"......."
			},
			new Dictionary<char, Color> { ['C'] = new Color(0.50f, 0.20f, 0.50f) },
			CharacterToolType.Claw,
			"Monster"
		);
	}
}
