using Godot;

/// <summary>
/// One-shot “blood spring” in board space when a civilian becomes a <see cref="EnemyType.Crazy"/>; rect matches
/// the colonist’s drawn body + tool bounds (see <see cref="GridSimulator.GetColonyCharacterSpriteAabbBoardPx"/>).
/// </summary>
public partial class CrazySpawnVfx : Node2D
{
	private const float MinSizePx = 6f;
	private const float DurationSec = 0.62f;
	private ColorRect? _quad;
	private ShaderMaterial? _mat;
	private double _t;

	/// <summary>Play on <paramref name="fromCivilian"/> (before removal) so AABB matches that frame’s art.</summary>
	public void Begin(GridSimulator owner, ColonyCharacter fromCivilian)
	{
		ZIndex = 99;
		Position = Vector2.Zero;
		owner.GetColonyCharacterSpriteAabbBoardPx(fromCivilian, out var topLeft, out var size);
		size = new Vector2(Mathf.Max(size.X, MinSizePx), Mathf.Max(size.Y, MinSizePx));
		var sh = GD.Load<Shader>("res://shaders/crazy_spawn_corruption.gdshader");
		if (sh == null)
		{
			GD.PrintErr("crazy_spawn_corruption.gdshader missing.");
			QueueFree();
			return;
		}

		_quad = new ColorRect
		{
			Position = topLeft,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_mat = new ShaderMaterial { Shader = sh };
		_mat.SetShaderParameter("progress", 0f);
		_quad.Material = _mat;
		AddChild(_quad);
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_t += delta;
		var p = Mathf.Min(1f, (float)(_t / DurationSec));
		_mat?.SetShaderParameter("progress", p);
		if (_t >= DurationSec)
		{
			SetProcess(false);
			QueueFree();
		}
	}
}
