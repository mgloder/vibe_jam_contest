using Godot;

/// <summary>
/// One-shot board-space burst at a cell, used when a civilian is converted to a <see cref="EnemyType.Crazy"/>
/// in <see cref="GridSimulator.ConvertCiviliansInSoulWhisperRange"/>.
/// </summary>
public partial class CrazySpawnVfx : Node2D
{
	private const float SizePx = 92f;
	private const float DurationSec = 0.52f;
	private ColorRect? _quad;
	private ShaderMaterial? _mat;
	private double _t;

	/// <summary>Play centered on the sim cell, in <paramref name="owner"/>’s board pixel space.</summary>
	public void Begin(GridSimulator owner, Vector2I cell)
	{
		ZIndex = 99;
		Position = Vector2.Zero;
		var center = owner.GetCellCenterBoardPx(cell);
		var s = SizePx;
		var sh = GD.Load<Shader>("res://shaders/crazy_spawn_corruption.gdshader");
		if (sh == null)
		{
			GD.PrintErr("crazy_spawn_corruption.gdshader missing.");
			QueueFree();
			return;
		}

		_quad = new ColorRect
		{
			Position = new Vector2(center.X - s * 0.5f, center.Y - s * 0.5f),
			Size = new Vector2(s, s),
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
