using System;
using Godot;

/// <summary>Short lightning bolt + ground flash; invokes <see cref="PlaybackComplete"/> when done.</summary>
public partial class LightningStrikeVfx : Node2D
{
	private const float BoltWidthPx = 36f;
	private const float SkyOffsetPx = 40f;
	private const float ImpactSizePx = 72f;
	private const float DurationSec = 0.5f;
	private ColorRect? _bolt;
	private ColorRect? _impact;
	private ShaderMaterial? _boltMat;
	private ShaderMaterial? _impactMat;
	private double _t;

	/// <summary>Called from <see cref="GridSimulator"/> after the effect ends; servant is added there.</summary>
	public event Action? PlaybackComplete;

	/// <summary>Plays a strike in <paramref name="owner"/>'s local board space: bolt from above the sim top to the cell center.</summary>
	public void Begin(GridSimulator owner, Vector2I cell)
	{
		ZIndex = 100;
		Position = Vector2.Zero;

		var hit = owner.GetCellCenterBoardPx(cell);
		var topY = owner.GetBoardLayoutTopY() - SkyOffsetPx;
		var h = Mathf.Max(12f, hit.Y - topY);
		var w = BoltWidthPx;

		var boltShader = GD.Load<Shader>("res://shaders/lightning_bolt.gdshader");
		var impShader = GD.Load<Shader>("res://shaders/lightning_impact.gdshader");
		if (boltShader == null || impShader == null)
		{
			GD.PrintErr("Lightning shader missing; spawning Servant without VFX.");
			PlaybackComplete?.Invoke();
			QueueFree();
			return;
		}

		_bolt = new ColorRect
		{
			Position = new Vector2(hit.X - w * 0.5f, topY),
			Size = new Vector2(w, h),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_boltMat = new ShaderMaterial { Shader = boltShader };
		_boltMat.SetShaderParameter("flash", 0.15f);
		_bolt.Material = _boltMat;
		AddChild(_bolt);

		_impact = new ColorRect
		{
			Position = new Vector2(hit.X - ImpactSizePx * 0.5f, hit.Y - ImpactSizePx * 0.5f),
			Size = new Vector2(ImpactSizePx, ImpactSizePx),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_impactMat = new ShaderMaterial { Shader = impShader };
		_impactMat.SetShaderParameter("strength", 0f);
		_impact.Material = _impactMat;
		AddChild(_impact);

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_t += delta;
		// Perceived strike: quick ramp up, flicker, fade
		if (_t < 0.08f)
		{
			_flashBolt(Mathf.Lerp(0.2f, 2.2f, (float)(_t / 0.08)));
		}
		else if (_t < DurationSec * 0.72f)
		{
			var pulse = 1.85f + 0.2f * Mathf.Sin((float)_t * 40f);
			_flashBolt(pulse);
			_impactMat?.SetShaderParameter("strength", 1.35f);
		}
		else
		{
			var fade = 1f - (float)((_t - DurationSec * 0.72f) / (DurationSec * 0.28f));
			_flashBolt(2f * fade);
			_impactMat?.SetShaderParameter("strength", 1.35f * fade);
		}

		if (_t >= DurationSec)
		{
			SetProcess(false);
			PlaybackComplete?.Invoke();
			QueueFree();
		}
	}

	private void _flashBolt(float f)
	{
		if (_boltMat != null)
			_boltMat.SetShaderParameter("flash", f);
	}
}
