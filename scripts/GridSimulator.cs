using Godot;
using System.Collections.Generic;

public partial class GridSimulator : Node2D
{
	[Export]
	public int GridWidth { get; set; } = 960;

	[Export]
	public int GridHeight { get; set; } = 540;

	[Export]
	public int CellSize { get; set; } = 1;

	[Export]
	public int BuildingGrowthPerTick { get; set; } = 3;

	[Export]
	public float BuildingGrowthIntervalSec { get; set; } = 0.2f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float TerrainSmoothness { get; set; } = 0.6f;

	private Label _statusLabel = null!;
	private Label _statsLabel = null!;
	private Control _topPanel = null!;
	private GridControlPanel _controlPanel = null!;
	private ColonyCharacter _firstNpc = null!;
	private readonly RandomNumberGenerator _rng = new();
	private bool _isCharacterVisible = true;
	private TerrainType[,] _terrain = null!;
	private readonly HashSet<Vector2I> _buildingCells = new();
	private bool _buildingSimEnabled;
	private Timer _buildingGrowthTimer = null!;
	private readonly Queue<Vector2I> _activeBuildingQueue = new();
	private bool _hasActiveBuildingProject;
	private Vector2I _activeBuildingOrigin;
	private Vector2I _lastCompletedBuildingOrigin;
	private int _buildingWidthCells;
	private int _buildingHeightCells;
	private const int BuildingSizeMultiplier = 2;

	public override void _Ready()
	{
		_rng.Randomize();
		_statusLabel = GetNode<Label>("%StatusLabel");
		_statsLabel = GetNode<Label>("%StatsLabel");
		_topPanel = GetNode<Control>("UI/TopPanel");
		_controlPanel = GetNode<GridControlPanel>("%ControlPanel");
		_controlPanel.RandomizeCharacterRequested += OnRandomizeCharacterPressed;
		_controlPanel.BuildingSimulatorRequested += OnOpenBuildingSimulatorPressed;
		_controlPanel.GenerateTerrainRequested += OnGenerateTerrainPressed;
		_controlPanel.TerrainRemoveRequested += OnTerrainRemoveRequested;
		_controlPanel.BuildingExpandSizeChanged += OnBuildingExpandSizeChanged;
		_controlPanel.TerrainSmoothnessChanged += OnTerrainSmoothnessChanged;
		_controlPanel.ShowCharacterRequested += OnShowCharacterPressed;
		_controlPanel.RemoveCharacterRequested += OnRemoveCharacterPressed;

		_terrain = new TerrainType[GridWidth, GridHeight];
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		RemoveAllBuildingsFromBoard();
		_buildingGrowthTimer = new Timer();
		_buildingGrowthTimer.WaitTime = BuildingGrowthIntervalSec;
		_buildingGrowthTimer.OneShot = false;
		_buildingGrowthTimer.Timeout += OnBuildingGrowthTick;
		AddChild(_buildingGrowthTimer);

		var centerCell = new Vector2I(GridWidth / 2, GridHeight / 2);
		_firstNpc = ColonyCharacter.CreateStarter(centerCell);
		(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();
		_controlPanel.SetBuildingExpandSize(BuildingGrowthPerTick);
		_controlPanel.SetTerrainSmoothness(TerrainSmoothness);
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

		DrawNpcTool(cellTopLeft, pixelScale);
	}

	private void DrawNpcTool(Vector2 cellTopLeft, float pixelScale)
	{
		if (_firstNpc.Tool == CharacterToolType.None || _firstNpc.ToolRows.Length == 0)
			return;

		for (var y = 0; y < _firstNpc.ToolRows.Length; y++)
		{
			var row = _firstNpc.ToolRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!_firstNpc.ToolPalette.TryGetValue(token, out var color))
					continue;

				var px = cellTopLeft + new Vector2(
					(x - _firstNpc.ToolPivot.X) * pixelScale,
					(y - _firstNpc.ToolPivot.Y) * pixelScale
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
		var projectState = _hasActiveBuildingProject ? "Building" : _buildingSimEnabled ? "Seeking Next" : "Idle";
		var buildingState = _buildingSimEnabled ? $"ON ({_buildingCells.Count})" : "OFF";
		_statusLabel.Text = $"Character simulator scaffold | Terrain tools: Remove mode | Building Sim: {buildingState} | Project: {projectState}";
		var visibility = _isCharacterVisible ? "Shown" : "Removed";
		_statsLabel.Text = $"Grid: {GridWidth}x{GridHeight}  |  NPC: {_firstNpc.DisplayName} ({_firstNpc.Type}) @ ({_firstNpc.Cell.X}, {_firstNpc.Cell.Y})  |  Building: {_buildingWidthCells}x{_buildingHeightCells}  |  NPC State: {visibility}";
		_controlPanel.SetCharacterVisibilityState(_isCharacterVisible);
		_controlPanel.SetCharacterRandomizeEnabled(_isCharacterVisible);
	}

	private void OnRandomizeCharacterPressed()
	{
		_firstNpc = ColonyCharacter.CreateRandomized(_firstNpc.Cell, _rng);
		UpdateHud();
		QueueRedraw();
	}

	private void OnOpenBuildingSimulatorPressed()
	{
		// Building feature disabled for now; keep board building-free.
		RemoveAllBuildingsFromBoard();
		UpdateHud();
		QueueRedraw();
	}

	private void OnBuildingExpandSizeChanged(int delta)
	{
		BuildingGrowthPerTick = Mathf.Clamp(BuildingGrowthPerTick + delta, 1, 64);
		_controlPanel.SetBuildingExpandSize(BuildingGrowthPerTick);
		UpdateHud();
	}

	private void OnGenerateTerrainPressed()
	{
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		RemoveAllBuildingsFromBoard();
		QueueRedraw();
	}

	private void OnTerrainRemoveRequested(TerrainType terrainType)
	{
		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				if (_terrain[x, y] == terrainType)
					_terrain[x, y] = TerrainType.None;
			}
		}

		// Removing building should clear both terrain-building tiles and constructed building cells.
		if (terrainType == TerrainType.Building)
			RemoveAllBuildingsFromBoard();

		QueueRedraw();
	}

	private void OnTerrainSmoothnessChanged(float value)
	{
		TerrainSmoothness = Mathf.Clamp(value, 0f, 1f);
		_controlPanel.SetTerrainSmoothness(TerrainSmoothness);
		// Apply immediately so slider feedback is obvious.
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
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

	private void OnBuildingGrowthTick()
	{
		if (!_buildingSimEnabled)
			return;

		if (!_hasActiveBuildingProject && !TryStartNextBuildingProject())
		{
			_buildingSimEnabled = false;
			_buildingGrowthTimer.Stop();
			UpdateHud();
			return;
		}

		var additions = Mathf.Min(BuildingGrowthPerTick, _activeBuildingQueue.Count);
		for (var i = 0; i < additions; i++)
		{
			var cell = _activeBuildingQueue.Dequeue();
			_buildingCells.Add(cell);
		}

		if (_activeBuildingQueue.Count == 0)
		{
			_hasActiveBuildingProject = false;
			_lastCompletedBuildingOrigin = _activeBuildingOrigin;
		}

		UpdateHud();
		QueueRedraw();
	}

	private bool TryStartNextBuildingProject()
	{
		if (_buildingCells.Count == 0)
		{
			StartBuildingProject(GetInitialBuildingOrigin());
			return true;
		}

		var dirs = new List<Vector2I> { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };
		Shuffle(dirs);
		var gap = 0;

		foreach (var dir in dirs)
		{
			var candidate = _lastCompletedBuildingOrigin;
			if (dir == Vector2I.Left)
				candidate += new Vector2I(-_buildingWidthCells - gap, 0);
			else if (dir == Vector2I.Right)
				candidate += new Vector2I(_buildingWidthCells + gap, 0);
			else if (dir == Vector2I.Up)
				candidate += new Vector2I(0, -_buildingHeightCells - gap);
			else if (dir == Vector2I.Down)
				candidate += new Vector2I(0, _buildingHeightCells + gap);

			if (!CanPlaceBuildingAt(candidate))
				continue;

			StartBuildingProject(candidate);
			return true;
		}

		return false;
	}

	private Vector2I GetInitialBuildingOrigin()
	{
		return new Vector2I(
			Mathf.Clamp(_firstNpc.Cell.X - _buildingWidthCells / 2, 0, GridWidth - _buildingWidthCells),
			Mathf.Clamp(_firstNpc.Cell.Y - _buildingHeightCells / 2, 0, GridHeight - _buildingHeightCells)
		);
	}

	private void StartBuildingProject(Vector2I origin)
	{
		_hasActiveBuildingProject = true;
		_activeBuildingOrigin = origin;
		_activeBuildingQueue.Clear();

		for (var y = 0; y < _buildingHeightCells; y++)
		{
			for (var x = 0; x < _buildingWidthCells; x++)
				_activeBuildingQueue.Enqueue(origin + new Vector2I(x, y));
		}
	}

	private bool CanPlaceBuildingAt(Vector2I origin)
	{
		if (origin.X < 0 || origin.Y < 0)
			return false;
		if (origin.X + _buildingWidthCells > GridWidth || origin.Y + _buildingHeightCells > GridHeight)
			return false;

		for (var y = 0; y < _buildingHeightCells; y++)
		{
			for (var x = 0; x < _buildingWidthCells; x++)
			{
				var c = origin + new Vector2I(x, y);
				if (_buildingCells.Contains(c))
					return false;
			}
		}

		return true;
	}

	private (int widthCells, int heightCells) GetFixedBuildingSizeCellsFromCharacter()
	{
		// Stable rule: building footprint is always 2x character footprint.
		var spriteWidth = _firstNpc.SpriteRows.Length == 0 ? 7 : _firstNpc.SpriteRows[0].Length;
		var spriteHeight = Mathf.Max(1, _firstNpc.SpriteRows.Length);
		var pixelScale = Mathf.Max(2, CellSize * 2);
		var characterWidthCells = Mathf.Max(1, Mathf.CeilToInt((spriteWidth * pixelScale) / (float)CellSize));
		var characterHeightCells = Mathf.Max(1, Mathf.CeilToInt((spriteHeight * pixelScale) / (float)CellSize));
		return (characterWidthCells * BuildingSizeMultiplier, characterHeightCells * BuildingSizeMultiplier);
	}

	private void Shuffle(List<Vector2I> values)
	{
		for (var i = values.Count - 1; i > 0; i--)
		{
			var j = _rng.RandiRange(0, i);
			(values[i], values[j]) = (values[j], values[i]);
		}
	}

	private static Color BuildingCellColor(int x, int y)
	{
		var checker = ((x + y) & 1) == 0;
		return checker ? new Color(0.73f, 0.55f, 0.31f) : new Color(0.65f, 0.47f, 0.24f);
	}

	private void RemoveAllBuildingsFromBoard()
	{
		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				if (_terrain[x, y] == TerrainType.Building ||
					_terrain[x, y] == TerrainType.Road ||
					_terrain[x, y] == TerrainType.Mountain)
					_terrain[x, y] = TerrainType.Grass;
			}
		}

		_buildingCells.Clear();
		_activeBuildingQueue.Clear();
		_hasActiveBuildingProject = false;
		_buildingSimEnabled = false;
		_buildingGrowthTimer?.Stop();
	}

}
