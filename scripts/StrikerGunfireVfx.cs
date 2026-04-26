using Godot;

/// <summary>Visual gunfire: shader muzzle + optional tracer to first target, when expert or soldier strikers attack.</summary>
public partial class StrikerGunfireVfx : Node2D
{
	private const float MuzzleSizePx = 44f;
	private const float TracerHeightPx = 3f;
	private const float DurationSec = 0.14f;
	private ColorRect? _muzzle;
	private ShaderMaterial? _muzzleMat;
	private ColorRect? _tracer;
	private ShaderMaterial? _tracerMat;
	private double _t;

	/// <param name="from">Shooter’s sim cell.</param>
	/// <param name="to">First struck unit’s cell (enemy or Servant); tracer omitted if same as <paramref name="from"/>.</param>
	public void Begin(GridSimulator owner, Vector2I from, Vector2I to)
	{
		ZIndex = 98;
		Position = Vector2.Zero;
		var fromP = owner.GetCellCenterBoardPx(from);
		var toP = owner.GetCellCenterBoardPx(to);
		var muzzleShader = GD.Load<Shader>("res://shaders/gunfire_muzzle.gdshader");
		var tracerShader = GD.Load<Shader>("res://shaders/gunfire_tracer.gdshader");
		if (muzzleShader == null || tracerShader == null)
		{
			GD.PrintErr("Gunfire shader missing (gunfire_muzzle or gunfire_tracer).");
			QueueFree();
			return;
		}

		var half = MuzzleSizePx * 0.5f;
		_muzzle = new ColorRect
		{
			Position = new Vector2(fromP.X - half, fromP.Y - half),
			Size = new Vector2(MuzzleSizePx, MuzzleSizePx),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_muzzleMat = new ShaderMaterial { Shader = muzzleShader };
		_muzzleMat.SetShaderParameter("t", 0f);
		_muzzle.Material = _muzzleMat;
		AddChild(_muzzle);

		var seg = toP - fromP;
		var len = seg.Length();
		if (len > 2f)
		{
			var ang = seg.Angle();
			var n = new Node2D
			{
				Position = fromP,
				Rotation = ang
			};
			_tracer = new ColorRect
			{
				Position = new Vector2(0f, -TracerHeightPx * 0.5f),
				Size = new Vector2(len, TracerHeightPx),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_tracerMat = new ShaderMaterial { Shader = tracerShader };
			_tracerMat.SetShaderParameter("t", 0f);
			_tracer.Material = _tracerMat;
			n.AddChild(_tracer);
			AddChild(n);
		}

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_t += delta;
		var p = Mathf.Min(1f, (float)(_t / DurationSec));
		_muzzleMat?.SetShaderParameter("t", p);
		_tracerMat?.SetShaderParameter("t", p);
		if (_t >= DurationSec)
		{
			SetProcess(false);
			QueueFree();
		}
	}
}
