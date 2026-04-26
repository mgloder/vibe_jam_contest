using Godot;

/// <summary>
/// Wraps an ability <see cref="Panel"/> in the bottom bar. The panel uses position layout and is resized to
/// <see cref="Size"/> on every <see cref="Resized"/> so labels always get the slot width. Hover lifts via
/// <see cref="Control.Position"/>.(y).
/// </summary>
public partial class AbilityCardHolder : Control
{
	[Export] public float HoverLiftPixels { get; set; } = 3f;
	[Export] public double HoverTweenSec { get; set; } = 0.1;

	private Control? _card;
	private Tween? _tween;
	private bool _hovering;
	private int _zRest;
	private const int ZHover = 1;

	public override void _Ready()
	{
		ClipContents = false;
		MouseFilter = MouseFilterEnum.Stop;

		if (GetChildCount() > 0)
			_card = GetChild(0) as Control;
		if (_card == null)
			return;
		_zRest = ZIndex;
		_card.MouseFilter = MouseFilterEnum.Ignore;
		Resized += OnSlotResized;
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
		OnSlotResized();
		Callable.From(OnSlotResized).CallDeferred();
	}

	public override void _ExitTree()
	{
		Resized -= OnSlotResized;
		MouseEntered -= OnMouseEntered;
		MouseExited -= OnMouseExited;
	}

	private void OnSlotResized()
	{
		if (_card == null)
			return;
		_card.Size = Size;
		ApplyVisualLift(animate: false);
	}

	private void OnMouseEntered()
	{
		_hovering = true;
		ZIndex = _zRest + ZHover;
		ApplyVisualLift(animate: true);
	}

	private void OnMouseExited()
	{
		_hovering = false;
		ZIndex = _zRest;
		ApplyVisualLift(animate: true);
	}

	private void ApplyVisualLift(bool animate)
	{
		if (_card == null)
			return;
		var y = -(_hovering ? HoverLiftPixels : 0f);
		var to = new Vector2(0f, y);
		if (animate)
		{
			_tween?.Kill();
			_tween = CreateTween();
			_tween.TweenProperty(_card, "position", to, (float)HoverTweenSec)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		}
		else
			_card.Position = to;
	}
}
