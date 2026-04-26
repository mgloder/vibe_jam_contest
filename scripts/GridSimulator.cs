using Godot;

public partial class GridSimulator : Node2D
{
	[Export]
	public int GridWidth { get; set; } = 960;

	[Export]
	public int GridHeight { get; set; } = 540;

	[Export]
	public int CellSize { get; set; } = 1;

	[Export]
	private Label _statusLabel = null!;
	private Label _statsLabel = null!;

	public override void _Ready()
	{
		_statusLabel = GetNode<Label>("%StatusLabel");
		_statsLabel = GetNode<Label>("%StatsLabel");
		UpdateHud();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		// Keep a minimal reset hotkey while the project is still scaffolding.
		if (key.Keycode == Key.R)
		{
			QueueRedraw();
			UpdateHud();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Draw()
	{
		var origin = GetGridOrigin();
		var boardSize = new Vector2(GridWidth * CellSize, GridHeight * CellSize);
		var drawGridLines = CellSize >= 4;

		// Retro-style base board with hard-edged palette blocks.
		DrawRect(new Rect2(origin, boardSize), new Color(0.07f, 0.08f, 0.12f));

		var cellPad = CellSize <= 2 ? 0f : 1f;
		var cellSize = new Vector2(CellSize - cellPad * 2f, CellSize - cellPad * 2f);
		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				var px = origin + new Vector2(x * CellSize + cellPad, y * CellSize + cellPad);
				var checker = ((x + y) & 1) == 0;
				var color = checker ? new Color(0.13f, 0.15f, 0.21f) : new Color(0.11f, 0.13f, 0.18f);
				DrawRect(new Rect2(px, cellSize), color);
			}
		}

		if (drawGridLines)
		{
			var lineColor = new Color(0.20f, 0.25f, 0.35f, 0.88f);
			for (var x = 0; x <= GridWidth; x++)
			{
				var xPos = origin.X + x * CellSize;
				DrawLine(new Vector2(xPos, origin.Y), new Vector2(xPos, origin.Y + boardSize.Y), lineColor, 1f);
			}

			for (var y = 0; y <= GridHeight; y++)
			{
				var yPos = origin.Y + y * CellSize;
				DrawLine(new Vector2(origin.X, yPos), new Vector2(origin.X + boardSize.X, yPos), lineColor, 1f);
			}
		}

		DrawRect(new Rect2(origin, boardSize), new Color(0.48f, 0.63f, 0.88f), false, 2f);
	}

	private Vector2 GetGridOrigin()
	{
		var viewport = GetViewportRect().Size;
		var boardWidth = GridWidth * CellSize;
		var boardHeight = GridHeight * CellSize;
		var x = Mathf.Floor((viewport.X - boardWidth) * 0.5f);
		var y = Mathf.Floor((viewport.Y - boardHeight) * 0.5f + 24f);
		return new Vector2(x, y);
	}

	private void UpdateHud()
	{
		_statusLabel.Text = "Empty simulation board scaffold";
		_statsLabel.Text = $"Grid: {GridWidth}x{GridHeight}  |  Cell: {CellSize}px  |  Press R to redraw";
	}
}
