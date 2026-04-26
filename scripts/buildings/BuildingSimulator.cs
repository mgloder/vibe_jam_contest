using Godot;
using System.Collections.Generic;

public partial class BuildingSimulator : Node2D
{
	[Export]
	public int CellSize { get; set; } = 20;

	[Export]
	public int GrowthPerStep { get; set; } = 6;

	[Export]
	public float AutoExpandIntervalSec { get; set; } = 0.15f;

	private readonly HashSet<Vector2I> _buildingCells = new();
	private readonly RandomNumberGenerator _rng = new();
	private Vector2 _cameraCenter = Vector2.Zero;
	private bool _autoExpand;

	private Timer _expandTimer = null!;
	private Label _statusLabel = null!;
	private Label _countLabel = null!;

	public override void _Ready()
	{
		_rng.Randomize();
		_expandTimer = GetNode<Timer>("%ExpandTimer");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_countLabel = GetNode<Label>("%CountLabel");

		_expandTimer.WaitTime = AutoExpandIntervalSec;
		_expandTimer.Timeout += OnExpandTimerTimeout;

		GetNode<Button>("%StepButton").Pressed += StepExpansion;
		GetNode<Button>("%AutoButton").Pressed += ToggleAutoExpand;
		GetNode<Button>("%ClearButton").Pressed += ClearAll;
		GetNode<Button>("%CenterButton").Pressed += CenterView;

		UpdateHud();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
		{
			var cell = ScreenToCell(mouse.Position);
			_buildingCells.Add(cell);
			UpdateHud();
			QueueRedraw();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		var panStep = 8f;
		switch (key.Keycode)
		{
			case Key.W:
			case Key.Up:
				_cameraCenter.Y -= panStep;
				QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case Key.S:
			case Key.Down:
				_cameraCenter.Y += panStep;
				QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case Key.A:
			case Key.Left:
				_cameraCenter.X -= panStep;
				QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case Key.D:
			case Key.Right:
				_cameraCenter.X += panStep;
				QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case Key.N:
				StepExpansion();
				GetViewport().SetInputAsHandled();
				break;
			case Key.Space:
				ToggleAutoExpand();
				GetViewport().SetInputAsHandled();
				break;
			case Key.C:
				ClearAll();
				GetViewport().SetInputAsHandled();
				break;
			case Key.R:
				CenterView();
				GetViewport().SetInputAsHandled();
				break;
		}
	}

	public override void _Draw()
	{
		var viewport = GetViewportRect().Size;
		DrawRect(new Rect2(Vector2.Zero, viewport), new Color(0.06f, 0.07f, 0.10f));

		var (minX, maxX, minY, maxY) = GetVisibleCellBounds();

		var lineColor = new Color(0.16f, 0.20f, 0.28f, 0.8f);
		for (var x = minX; x <= maxX + 1; x++)
		{
			var from = CellCornerToScreen(new Vector2I(x, minY));
			var to = CellCornerToScreen(new Vector2I(x, maxY + 1));
			DrawLine(from, to, lineColor, 1f);
		}

		for (var y = minY; y <= maxY + 1; y++)
		{
			var from = CellCornerToScreen(new Vector2I(minX, y));
			var to = CellCornerToScreen(new Vector2I(maxX + 1, y));
			DrawLine(from, to, lineColor, 1f);
		}

		var pad = 2f;
		var cellRectSize = new Vector2(CellSize - pad * 2f, CellSize - pad * 2f);
		for (var y = minY; y <= maxY; y++)
		{
			for (var x = minX; x <= maxX; x++)
			{
				var c = new Vector2I(x, y);
				if (!_buildingCells.Contains(c))
					continue;

				var basePos = CellCornerToScreen(c) + new Vector2(pad, pad);
				var checker = ((x + y) & 1) == 0;
				var color = checker ? new Color(0.75f, 0.56f, 0.32f) : new Color(0.67f, 0.47f, 0.25f);
				DrawRect(new Rect2(basePos, cellRectSize), color);
			}
		}
	}

	private void StepExpansion()
	{
		if (_buildingCells.Count == 0)
		{
			_buildingCells.Add(Vector2I.Zero);
			UpdateHud();
			QueueRedraw();
			return;
		}

		var frontier = new List<Vector2I>();
		var frontierSet = new HashSet<Vector2I>();
		var dirs = new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };

		foreach (var cell in _buildingCells)
		{
			foreach (var d in dirs)
			{
				var n = cell + d;
				if (_buildingCells.Contains(n) || !frontierSet.Add(n))
					continue;
				frontier.Add(n);
			}
		}

		if (frontier.Count == 0)
			return;

		var additions = Mathf.Min(GrowthPerStep, frontier.Count);
		for (var i = 0; i < additions; i++)
		{
			var pick = _rng.RandiRange(0, frontier.Count - 1);
			var c = frontier[pick];
			frontier[pick] = frontier[^1];
			frontier.RemoveAt(frontier.Count - 1);
			_buildingCells.Add(c);
		}

		UpdateHud();
		QueueRedraw();
	}

	private void ToggleAutoExpand()
	{
		_autoExpand = !_autoExpand;
		if (_autoExpand)
			_expandTimer.Start();
		else
			_expandTimer.Stop();
		UpdateHud();
	}

	private void ClearAll()
	{
		_buildingCells.Clear();
		UpdateHud();
		QueueRedraw();
	}

	private void CenterView()
	{
		_cameraCenter = Vector2.Zero;
		QueueRedraw();
	}

	private void OnExpandTimerTimeout()
	{
		if (_autoExpand)
			StepExpansion();
	}

	private Vector2I ScreenToCell(Vector2 screenPos)
	{
		var viewport = GetViewportRect().Size;
		var worldCell = ((screenPos - viewport * 0.5f) / CellSize) + _cameraCenter;
		return new Vector2I(Mathf.FloorToInt(worldCell.X), Mathf.FloorToInt(worldCell.Y));
	}

	private Vector2 CellCornerToScreen(Vector2I cell)
	{
		var viewport = GetViewportRect().Size;
		var local = (new Vector2(cell.X, cell.Y) - _cameraCenter) * CellSize;
		return local + viewport * 0.5f;
	}

	private (int minX, int maxX, int minY, int maxY) GetVisibleCellBounds()
	{
		var viewport = GetViewportRect().Size;
		var halfW = viewport.X / CellSize * 0.5f;
		var halfH = viewport.Y / CellSize * 0.5f;
		var minX = Mathf.FloorToInt(_cameraCenter.X - halfW) - 2;
		var maxX = Mathf.CeilToInt(_cameraCenter.X + halfW) + 2;
		var minY = Mathf.FloorToInt(_cameraCenter.Y - halfH) - 2;
		var maxY = Mathf.CeilToInt(_cameraCenter.Y + halfH) + 2;
		return (minX, maxX, minY, maxY);
	}

	private void UpdateHud()
	{
		_statusLabel.Text = _autoExpand ? "Auto Expand: ON" : "Auto Expand: OFF";
		_countLabel.Text = $"Cells: {_buildingCells.Count}  |  Center: ({Mathf.FloorToInt(_cameraCenter.X)}, {Mathf.FloorToInt(_cameraCenter.Y)})";
	}
}
