using Godot;

public partial class GridSimulator : Node2D
{
	[Export]
	public int GridWidth { get; set; } = 960;

	[Export]
	public int GridHeight { get; set; } = 540;

	[Export]
	public int CellSize { get; set; } = 1;

	private Label _statusLabel = null!;
	private Label _statsLabel = null!;
	private Control _topPanel = null!;
	private GridControlPanel _controlPanel = null!;
	private ColonyCharacter _firstNpc = null!;
	private readonly RandomNumberGenerator _rng = new();
	private bool _isCharacterVisible = true;
	private TerrainType _selectedTerrain = TerrainType.Grass;
	private bool _hasSelectedTerrain;
	private TerrainType[,] _terrain = null!;

	public override void _Ready()
	{
		_rng.Randomize();
		_statusLabel = GetNode<Label>("%StatusLabel");
		_statsLabel = GetNode<Label>("%StatsLabel");
		_topPanel = GetNode<Control>("UI/TopPanel");
		_controlPanel = GetNode<GridControlPanel>("%ControlPanel");
		_controlPanel.RandomizeCharacterRequested += OnRandomizeCharacterPressed;
		_controlPanel.ShowCharacterRequested += OnShowCharacterPressed;
		_controlPanel.RemoveCharacterRequested += OnRemoveCharacterPressed;
		_controlPanel.TerrainSelected += SelectTerrain;

		_terrain = new TerrainType[GridWidth, GridHeight];
		TerrainSystem.InitializeEmpty(_terrain);

		var centerCell = new Vector2I(GridWidth / 2, GridHeight / 2);
		_firstNpc = ColonyCharacter.CreateStarter(centerCell);
		UpdateHud();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton &&
			mouseButton.Pressed &&
			mouseButton.ButtonIndex == MouseButton.Left)
		{
			PaintTerrainAt(mouseButton.Position);
			GetViewport().SetInputAsHandled();
			return;
		}

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
				var color = TerrainSystem.TerrainToColor(_terrain[x, y], x, y);
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

		if (_isCharacterVisible)
			DrawFirstNpc(origin);
		DrawRect(new Rect2(origin, boardSize), new Color(0.48f, 0.63f, 0.88f), false, 2f);
	}

	private void DrawFirstNpc(Vector2 gridOrigin)
	{
		var cellTopLeft = gridOrigin + new Vector2(_firstNpc.Cell.X * CellSize, _firstNpc.Cell.Y * CellSize);
		var pixelScale = Mathf.Max(2, CellSize * 2);

		for (var y = 0; y < _firstNpc.SpriteRows.Length; y++)
		{
			var row = _firstNpc.SpriteRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!_firstNpc.Palette.TryGetValue(token, out var color))
					continue;

				var px = cellTopLeft + new Vector2(
					(x - _firstNpc.SpritePivot.X) * pixelScale,
					(y - _firstNpc.SpritePivot.Y) * pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}
	}

	private Vector2 GetGridOrigin()
	{
		var viewport = GetViewportRect().Size;
		var boardWidth = GridWidth * CellSize;
		var boardHeight = GridHeight * CellSize;

		// Reserve space for the right-side control panel so the board never renders underneath it.
		var panelLeftX = _controlPanel.GetGlobalRect().Position.X;
		var leftPadding = 16f;
		var rightPadding = 12f;
		var availableWidth = panelLeftX - rightPadding - leftPadding;
		var x = Mathf.Floor(leftPadding + (availableWidth - boardWidth) * 0.5f);
		x = Mathf.Max(leftPadding, x);

		// Reserve space for the top info panel.
		var topPanelBottom = _topPanel.GetGlobalRect().End.Y;
		var topPadding = 10f;
		var bottomPadding = 16f;
		var topBound = topPanelBottom + topPadding;
		var availableHeight = viewport.Y - topBound - bottomPadding;
		var y = Mathf.Floor(topBound + (availableHeight - boardHeight) * 0.5f);
		y = Mathf.Max(topBound, y);

		return new Vector2(x, y);
	}

	private void UpdateHud()
	{
		var brushText = _hasSelectedTerrain ? _selectedTerrain.ToString() : "None (pick a terrain button)";
		_statusLabel.Text = $"Character simulator scaffold | Brush: {brushText}";
		var visibility = _isCharacterVisible ? "Shown" : "Removed";
		_statsLabel.Text = $"Grid: {GridWidth}x{GridHeight}  |  NPC: {_firstNpc.DisplayName} @ ({_firstNpc.Cell.X}, {_firstNpc.Cell.Y})  |  State: {visibility}";
		_controlPanel.SetCharacterVisibilityState(_isCharacterVisible);
		_controlPanel.SetCharacterRandomizeEnabled(_isCharacterVisible);
		_controlPanel.SetSelectedTerrain(_hasSelectedTerrain ? _selectedTerrain : null);
	}

	private void OnRandomizeCharacterPressed()
	{
		_firstNpc = ColonyCharacter.CreateRandomized(_firstNpc.Cell, _rng);
		UpdateHud();
		QueueRedraw();
	}

	private void OnShowCharacterPressed()
	{
		_isCharacterVisible = true;
		UpdateHud();
		QueueRedraw();
	}

	private void OnRemoveCharacterPressed()
	{
		_isCharacterVisible = false;
		UpdateHud();
		QueueRedraw();
	}

	private void SelectTerrain(TerrainType terrain)
	{
		_selectedTerrain = terrain;
		_hasSelectedTerrain = true;
		UpdateHud();
	}

	private void PaintTerrainAt(Vector2 mousePosition)
	{
		var origin = GetGridOrigin();
		var local = mousePosition - origin;
		if (local.X < 0 || local.Y < 0)
			return;

		var x = (int)(local.X / CellSize);
		var y = (int)(local.Y / CellSize);
		if (x < 0 || y < 0 || x >= GridWidth || y >= GridHeight)
			return;
		if (!_hasSelectedTerrain)
			return;

		_terrain[x, y] = _selectedTerrain;
		QueueRedraw();
	}

}
