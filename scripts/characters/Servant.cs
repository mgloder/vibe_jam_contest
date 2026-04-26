using Godot;
using System.Collections.Generic;

/// <summary>
/// Monster: Servant, drawn as a Cthulhu-style silhouette.
/// Renders at <see cref="PixelScaleMultiplierVsCharacter"/> times the same base pixel size used for <see cref="ColonyCharacter"/> art.
/// Pathfinding in <see cref="GridSimulator"/> uses the same 4-way <c>GridAStar</c> as other units, with
/// a walkable test so the full token board never covers water (or buildings). Combat only chases
/// and damages <see cref="ColonyCharacter.IsAlive"/> characters.
/// </summary>
public sealed class Servant
{
	public const string DefaultDisplayName = "Servant (Cthulhu)";

	/// <summary>Draw with <c>colonyCharacterPixelScale * this</c> (1× matches NPC token size).</summary>
	public const int PixelScaleMultiplierVsCharacter = 1;

	/// <summary>Damage per hit when in melee range to a <see cref="ColonyCharacter"/>.</summary>
	public const int AttackDamage = 1;

	public string DisplayName { get; }
	public Vector2I Cell { get; set; }

	/// <summary>Rows top-to-bottom; each row same width. '.' = transparent.</summary>
	public string[] SpriteRows { get; }
	public Vector2I SpritePivot { get; }
	public IReadOnlyDictionary<char, Color> Palette { get; }

	private Servant(
		string displayName,
		Vector2I cell,
		string[] spriteRows,
		Vector2I spritePivot,
		IReadOnlyDictionary<char, Color> palette
	)
	{
		DisplayName = displayName;
		Cell = cell;
		SpriteRows = spriteRows;
		SpritePivot = spritePivot;
		Palette = palette;
	}

	public static Servant CreateAt(Vector2I cell, string? displayName = null) =>
		new(
			displayName ?? DefaultDisplayName,
			cell,
			BuildCthulhuShapeRows(),
			new Vector2I(8, 12),
			BuildCthulhuPalette()
		);

	/// <summary>~2x character token grid (wider/taller blob + tentacles + head mass).</summary>
	private static string[] BuildCthulhuShapeRows()
	{
		// 18 rows x 16 cols — Cthulhu: dome head, many tentacles, stub wings, bulk body
		return new[]
		{
			"....TTTTTTTT....",
			"...TTTBBBBTTT...",
			"..TTTBBBBBBTTT..",
			".TTTBBBBEBBBETT.",
			".TBBBBBBEBBBEET.",
			"TTTBBBBBEEEBBTTT",
			"TTBBPBBPBBBBTTT.",
			".TBBPBBWWBBBBT..",
			"..TBBBWWWWBBT...",
			"..TBBWBBBBWBT...",
			".TTBWWBWWBWBTT..",
			"TTBWWWBWWWBWTTT.",
			".TBBBBBBBBBTT...",
			".TTBTTBTBBTTT...",
			"..TTBTTBTTBTTT..",
			"..TBTBTBTBTTBTT.",
			".TBTBTTBTTBTTBTT",
			"TTBTTBTTBTTBTTB.",
		};
	}

	private static Dictionary<char, Color> BuildCthulhuPalette() =>
		new()
		{
			// B — deep carapace; P — sick purple; T — tentacle teal; E — eldritch green eyes; W — leathery wing
			['B'] = new Color(0.12f, 0.14f, 0.20f),
			['P'] = new Color(0.28f, 0.12f, 0.38f),
			['T'] = new Color(0.10f, 0.36f, 0.32f),
			['E'] = new Color(0.22f, 0.75f, 0.28f),
			['W'] = new Color(0.18f, 0.22f, 0.32f)
		};
}
