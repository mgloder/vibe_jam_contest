using Godot;
using System;
using System.Collections.Generic;

public enum ColonyCharacterVisualType
{
	PixelBody,
	TextureSprite
}

public sealed class ColonyCharacter
{
	public string Id { get; }
	public string DisplayName { get; set; }
	public Vector2I Cell { get; set; }
	public ColonyCharacterVisualType VisualType { get; }
	public string TexturePath { get; }
	public float TextureTargetHeightPx { get; }
	public Vector2 TextureAnchor { get; }
	public string[] SpriteRows { get; }
	public Vector2I SpritePivot { get; }
	public IReadOnlyDictionary<char, Color> Palette { get; }

	private ColonyCharacter(
		string id,
		string displayName,
		Vector2I cell,
		ColonyCharacterVisualType visualType,
		string texturePath,
		float textureTargetHeightPx,
		Vector2 textureAnchor,
		string[] spriteRows,
		Vector2I spritePivot,
		IReadOnlyDictionary<char, Color> palette
	)
	{
		Id = id;
		DisplayName = displayName;
		Cell = cell;
		VisualType = visualType;
		TexturePath = texturePath;
		TextureTargetHeightPx = textureTargetHeightPx;
		TextureAnchor = textureAnchor;
		SpriteRows = spriteRows;
		SpritePivot = spritePivot;
		Palette = palette;
	}

	public static ColonyCharacter CreateStarter(Vector2I cell)
	{
		return CreateSoldier(cell);
	}

	public static ColonyCharacter CreateSoldier(Vector2I cell)
	{
		return new ColonyCharacter(
			id: "starter-colonist-001",
			displayName: "Soldier",
			cell: cell,
			visualType: ColonyCharacterVisualType.TextureSprite,
			texturePath: "res://sprites/sample_s.png",
			textureTargetHeightPx: 150f,
			textureAnchor: new Vector2(0.5f, 0.6f),
			spriteRows: Array.Empty<string>(),
			spritePivot: new Vector2I(3, 4),
			palette: new Dictionary<char, Color>()
		);
	}

	public static ColonyCharacter CreateRandomized(Vector2I cell, RandomNumberGenerator rng)
	{
		var spriteSets = new[]
		{
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
			}
		};

		var paletteSets = new[]
		{
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.16f, 0.17f, 0.20f),
				['H'] = new Color(0.74f, 0.52f, 0.22f),
				['S'] = new Color(0.95f, 0.81f, 0.68f),
				['B'] = new Color(0.38f, 0.66f, 0.96f),
				['C'] = new Color(0.86f, 0.42f, 0.46f),
				['J'] = new Color(0.47f, 0.83f, 0.57f),
				['T'] = new Color(0.10f, 0.14f, 0.22f),
				['L'] = new Color(0.30f, 0.35f, 0.43f),
				['P'] = new Color(0.20f, 0.22f, 0.30f),
				['K'] = new Color(0.28f, 0.25f, 0.36f)
			},
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.50f, 0.16f, 0.20f),
				['H'] = new Color(0.90f, 0.78f, 0.44f),
				['S'] = new Color(0.78f, 0.58f, 0.44f),
				['B'] = new Color(0.50f, 0.82f, 0.92f),
				['C'] = new Color(0.64f, 0.58f, 0.91f),
				['J'] = new Color(0.90f, 0.60f, 0.28f),
				['T'] = new Color(0.16f, 0.10f, 0.08f),
				['L'] = new Color(0.45f, 0.38f, 0.31f),
				['P'] = new Color(0.24f, 0.20f, 0.17f),
				['K'] = new Color(0.22f, 0.27f, 0.42f)
			},
			new Dictionary<char, Color>
			{
				['G'] = new Color(0.12f, 0.34f, 0.18f),
				['H'] = new Color(0.18f, 0.22f, 0.58f),
				['S'] = new Color(0.92f, 0.72f, 0.58f),
				['B'] = new Color(0.98f, 0.68f, 0.30f),
				['C'] = new Color(0.34f, 0.88f, 0.62f),
				['J'] = new Color(0.96f, 0.44f, 0.52f),
				['T'] = new Color(0.08f, 0.12f, 0.24f),
				['L'] = new Color(0.24f, 0.28f, 0.40f),
				['P'] = new Color(0.30f, 0.23f, 0.42f),
				['K'] = new Color(0.36f, 0.30f, 0.18f)
			}
		};

		var chosenSprite = spriteSets[rng.RandiRange(0, spriteSets.Length - 1)];
		var chosenPalette = paletteSets[rng.RandiRange(0, paletteSets.Length - 1)];
		var name = $"Colonist {rng.RandiRange(100, 999)}";

		return new ColonyCharacter(
			id: Guid.NewGuid().ToString("N"),
			displayName: name,
			cell: cell,
			visualType: ColonyCharacterVisualType.PixelBody,
			texturePath: string.Empty,
			textureTargetHeightPx: 0f,
			textureAnchor: new Vector2(0.5f, 0.6f),
			spriteRows: chosenSprite,
			spritePivot: new Vector2I(3, 4),
			palette: chosenPalette
		);
	}
}
