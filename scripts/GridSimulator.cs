using System;
using Godot;
using System.Collections.Generic;
using System.Linq;

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
	private readonly List<ColonyCharacter> _characters = new();
	private Servant? _servant;
	private Vector2I? _servantPursueCell;
	private readonly Queue<Vector2I> _servantPathQueue = new();
	private float _servantMoveBudget;
	private readonly RandomNumberGenerator _rng = new();
	private bool _isCharacterVisible = true;
	private TerrainType[,] _terrain = null!;
	private float _characterMoveAccumulatorSec;
	private readonly List<Queue<Vector2I>> _characterPathQueues = new();
	private readonly List<float> _characterMoveBudget = new();
	private readonly HashSet<Vector2I> _buildingCells = new();
	private readonly List<Rect2I> _buildingFootprints = new();
	private bool _buildingSimEnabled;
	private Timer _buildingGrowthTimer = null!;
	private readonly Queue<Vector2I> _activeBuildingQueue = new();
	private bool _hasActiveBuildingProject;
	private Vector2I _activeBuildingOrigin;
	private Vector2I _lastCompletedBuildingOrigin;
	private int _buildingWidthCells;
	private int _buildingHeightCells;
	private Texture2D _buildingTexture = null!;
	private const int BuildingSizeMultiplier = 2;
	private const int MinGridLineStepCells = 10;
	/// <summary>Seconds between path-movement ticks. Each tick adds <see cref="MovementBudgetPerGridMajorStep"/> to spend on cells.</summary>
	private const float CharacterMoveStepSec = 0.5f;
	/// <summary>
	/// Budget units added each tick, aligned to the major grid: enough to cross one 10-cell span
	/// on default-cost terrain in one tick (flat ground uses 1 unit per cell; forest uses 2).
	/// </summary>
	private const int MovementBudgetPerGridMajorStep = MinGridLineStepCells;
	private const int DefaultTerrainMoveCost = 1;
	private const int ForestTerrainMoveCost = 2; // 0.5x speed
	/// <summary>Manhattan reach for Servant attacks, aligned to one major grid line step.</summary>
	private const int ServantAttackRangeCells = MinGridLineStepCells;
	/// <summary>Seconds the Servant pauses after a strike (no move, no attack).</summary>
	private const float ServantPostAttackStopSec = 1f;
	private static readonly string[] LayingDownSpriteRows =
	{
		"....SSSSS....",
		"..LDDDDDDLLL.",
		"....L...L...."
	};
	private static readonly Vector2I LayingDownSpritePivot = new(5, 1);
	private static readonly Dictionary<char, Color> LayingDownPalette = new()
	{
		['S'] = new Color(0.14f, 0.10f, 0.10f), // ground shadow
		['D'] = new Color(0.32f, 0.32f, 0.40f), // body
		['L'] = new Color(0.22f, 0.24f, 0.30f)  // limbs
	};

	private float _servantRestSecondsRemaining;

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
		_buildingTexture = GD.Load<Texture2D>("res://sprites/buildings/building.png");
		_buildingGrowthTimer = new Timer();
		_buildingGrowthTimer.WaitTime = BuildingGrowthIntervalSec;
		_buildingGrowthTimer.OneShot = false;
		_buildingGrowthTimer.Timeout += OnBuildingGrowthTick;
		AddChild(_buildingGrowthTimer);

		InitializeStartingCharacters();
		(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		PlaceServant();
		EnsureCharacterDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
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

	public override void _Process(double delta)
	{
		var d = (float)delta;
		_servantRestSecondsRemaining = Mathf.Max(0f, _servantRestSecondsRemaining - d);

		_characterMoveAccumulatorSec += d;
		if (_characterMoveAccumulatorSec < CharacterMoveStepSec)
		{
			UpdateHud();
			QueueRedraw();
			return;
		}

		while (_characterMoveAccumulatorSec >= CharacterMoveStepSec)
		{
			_characterMoveAccumulatorSec -= CharacterMoveStepSec;
			StepCharactersTowardDestination();
		}

		UpdateHud();
		QueueRedraw();
	}

	public override void _Draw()
	{
		var origin = GetGridOrigin();
		var boardSize = new Vector2(GridWidth * CellSize, GridHeight * CellSize);

		// Retro-style base board with hard-edged palette blocks.
		DrawRect(new Rect2(origin, boardSize), new Color(0.07f, 0.08f, 0.12f));

		// Keep tiles exactly on grid boundaries.
		var cellPad = 0f;
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

		DrawBuildingSprites(origin);

		var gridStep = Mathf.Max(MinGridLineStepCells, 1);
		var lineColor = new Color(0.28f, 0.28f, 0.30f, 0.78f);
		for (var x = 0; x <= GridWidth; x += gridStep)
		{
			var xPos = origin.X + x * CellSize;
			DrawLine(new Vector2(xPos, origin.Y), new Vector2(xPos, origin.Y + boardSize.Y), lineColor, 1f);
		}

		for (var y = 0; y <= GridHeight; y += gridStep)
		{
			var yPos = origin.Y + y * CellSize;
			DrawLine(new Vector2(origin.X, yPos), new Vector2(origin.X + boardSize.X, yPos), lineColor, 1f);
		}

		if (_isCharacterVisible)
		{
			for (var i = 0; i < _characters.Count; i++)
			{
				if (_characters[i].Health > 0)
					DrawCharacterDestination(_characters[i], origin);
				DrawCharacter(_characters[i], origin);
			}
			if (_servant != null)
				DrawServant(_servant, origin);
		}
		DrawRect(new Rect2(origin, boardSize), new Color(0.48f, 0.63f, 0.88f), false, 2f);
	}

	private void DrawCharacterDestination(ColonyCharacter character, Vector2 gridOrigin)
	{
		var markerTopLeft = gridOrigin + new Vector2(character.Destination.X * CellSize, character.Destination.Y * CellSize);
		var markerSize = Mathf.Max(1f, CellSize);
		var inset = markerSize <= 2f ? 0f : 1f;
		var rect = new Rect2(
			markerTopLeft + new Vector2(inset, inset),
			new Vector2(Mathf.Max(1f, markerSize - inset * 2f), Mathf.Max(1f, markerSize - inset * 2f))
		);
		DrawRect(rect, new Color(0.93f, 0.86f, 0.22f, 0.92f), false, 1f);
	}

	private void DrawCharacter(ColonyCharacter character, Vector2 gridOrigin)
	{
		var cellTopLeft = gridOrigin + new Vector2(character.Cell.X * CellSize, character.Cell.Y * CellSize);
		var pixelScale = Mathf.Max(2, CellSize * 2);

		if (character.Health <= 0)
		{
			DrawLayingDownCharacter(cellTopLeft, pixelScale);
			return;
		}

		for (var y = 0; y < character.SpriteRows.Length; y++)
		{
			var row = character.SpriteRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!character.Palette.TryGetValue(token, out var color))
					continue;

				var px = cellTopLeft + new Vector2(
					(x - character.SpritePivot.X) * pixelScale,
					(y - character.SpritePivot.Y) * pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}

		DrawNpcTool(character, cellTopLeft, pixelScale);
	}

	/// <summary>Prone silhouette when <see cref="ColonyCharacter.Health"/> is 0 (body remains on the map).</summary>
	private void DrawLayingDownCharacter(Vector2 cellTopLeft, float pixelScale)
	{
		for (var y = 0; y < LayingDownSpriteRows.Length; y++)
		{
			var row = LayingDownSpriteRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!LayingDownPalette.TryGetValue(token, out var color))
					continue;
				color = new Color(color.R, color.G, color.B, color.A * 0.9f);
				var px = cellTopLeft + new Vector2(
					(x - LayingDownSpritePivot.X) * pixelScale,
					(y - LayingDownSpritePivot.Y) * pixelScale + pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}
	}

	/// <summary>Same token/sprite path as <see cref="DrawCharacter"/>, with per-token size scaled by <see cref="Servant.PixelScaleMultiplierVsCharacter"/>.</summary>
	private void DrawServant(Servant servant, Vector2 gridOrigin)
	{
		var cellTopLeft = gridOrigin + new Vector2(servant.Cell.X * CellSize, servant.Cell.Y * CellSize);
		var basePixel = Mathf.Max(2, CellSize * 2);
		var pixelScale = basePixel * Servant.PixelScaleMultiplierVsCharacter;

		for (var y = 0; y < servant.SpriteRows.Length; y++)
		{
			var row = servant.SpriteRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!servant.Palette.TryGetValue(token, out var color))
					continue;

				var px = cellTopLeft + new Vector2(
					(x - servant.SpritePivot.X) * pixelScale,
					(y - servant.SpritePivot.Y) * pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}
	}

	private void DrawNpcTool(ColonyCharacter character, Vector2 cellTopLeft, float pixelScale)
	{
		if (character.Tool == CharacterToolType.None || character.ToolRows.Length == 0)
			return;

		for (var y = 0; y < character.ToolRows.Length; y++)
		{
			var row = character.ToolRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!character.ToolPalette.TryGetValue(token, out var color))
					continue;

				var px = cellTopLeft + new Vector2(
					(x - character.ToolPivot.X) * pixelScale,
					(y - character.ToolPivot.Y) * pixelScale
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
		var civilianCount = _characters.Count(c => c.Type == ColonyCharacterType.Civilian);
		var expertCount = _characters.Count(c => c.Type == ColonyCharacterType.Expert);
		var soldierCount = _characters.Count(c => c.Type == ColonyCharacterType.Soldier);
		var profileC = _characters.FirstOrDefault(c => c.Type == ColonyCharacterType.Civilian);
		var profileE = _characters.FirstOrDefault(c => c.Type == ColonyCharacterType.Expert);
		var profileS = _characters.FirstOrDefault(c => c.Type == ColonyCharacterType.Soldier);
		var civStr = profileC != null ? $"{profileC.MaxHealth}HP/{profileC.Attack}atk" : "1/0";
		var expStr = profileE != null ? $"{profileE.MaxHealth}HP/{profileE.Attack}atk" : "4/3";
		var solStr = profileS != null ? $"{profileS.MaxHealth}HP/{profileS.Attack}atk" : "2/1";
		_statsLabel.Text = $"Grid: {GridWidth}x{GridHeight}  |  NPCs: {_characters.Count} (C:{civilianCount} E:{expertCount} S:{soldierCount})  |  Building: {_buildingWidthCells}x{_buildingHeightCells}  |  Profiles: C {civStr}  E {expStr}  S {solStr}  |  NPC State: {visibility}";
		_controlPanel.SetCharacterVisibilityState(_isCharacterVisible);
		_controlPanel.SetCharacterRandomizeEnabled(_isCharacterVisible);
	}

	private void OnRandomizeCharacterPressed()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var current = _characters[i];
			_characters[i] = ColonyCharacter.CreateByType(current.Type, current.Cell, current.DisplayName, current.Destination);
		}
		EnsurePathStateSize();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		for (var ri = 0; ri < _characters.Count; ri++)
			TryReplanPathWithDestinationFallback(ri);
		UpdateHud();
		QueueRedraw();
	}

	private void OnOpenBuildingSimulatorPressed()
	{
		_buildingSimEnabled = !_buildingSimEnabled;
		if (_buildingSimEnabled)
		{
			_buildingGrowthTimer.Start();
		}
		else
		{
			_buildingGrowthTimer.Stop();
		}

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
		RelocateCharactersToWalkableGround();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		PlaceServant();
		EnsureCharacterDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
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

		EnsureCharacterDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		EnsureServantOnWalkable();
		QueueRedraw();
	}

	private void OnTerrainSmoothnessChanged(float value)
	{
		TerrainSmoothness = Mathf.Clamp(value, 0f, 1f);
		_controlPanel.SetTerrainSmoothness(TerrainSmoothness);
		// Apply immediately so slider feedback is obvious.
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		RelocateCharactersToWalkableGround();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		PlaceServant();
		EnsureCharacterDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
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
		if (!_buildingSimEnabled || !_hasActiveBuildingProject)
			return;

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

	private (int widthCells, int heightCells) GetFixedBuildingSizeCellsFromCharacter()
	{
		// Stable rule: building footprint is always 2x character footprint.
		var referenceCharacter = _characters.Count > 0 ? _characters[0] : ColonyCharacter.CreateStarter(new Vector2I(0, 0));
		var spriteWidth = referenceCharacter.SpriteRows.Length == 0 ? 7 : referenceCharacter.SpriteRows[0].Length;
		var spriteHeight = Mathf.Max(1, referenceCharacter.SpriteRows.Length);
		var pixelScale = Mathf.Max(2, CellSize * 2);
		var characterWidthCells = Mathf.Max(1, Mathf.CeilToInt((spriteWidth * pixelScale) / (float)CellSize));
		var characterHeightCells = Mathf.Max(1, Mathf.CeilToInt((spriteHeight * pixelScale) / (float)CellSize));
		var rawWidth = characterWidthCells * BuildingSizeMultiplier;
		var rawHeight = characterHeightCells * BuildingSizeMultiplier;
		return (
			RoundUpToStep(rawWidth, MinGridLineStepCells),
			RoundUpToStep(rawHeight, MinGridLineStepCells)
		);
	}

	private void DrawBuildingSprites(Vector2 gridOrigin)
	{
		if (_buildingTexture == null)
			return;

		for (var i = 0; i < _buildingFootprints.Count; i++)
		{
			var footprint = _buildingFootprints[i];
			var topLeft = gridOrigin + new Vector2(footprint.Position.X * CellSize, footprint.Position.Y * CellSize);
			var drawSize = new Vector2(footprint.Size.X * CellSize, footprint.Size.Y * CellSize);
			DrawTextureRect(_buildingTexture, new Rect2(topLeft, drawSize), false);
		}
	}

	private void GenerateRandomBuildingsAfterTerrain(int count)
	{
		if (_buildingWidthCells <= 0 || _buildingHeightCells <= 0)
			(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();

		_buildingCells.Clear();
		_buildingFootprints.Clear();
		_activeBuildingQueue.Clear();
		_hasActiveBuildingProject = false;

		var placed = 0;
		var attempts = 0;
		var maxAttempts = 500;
		while (placed < count && attempts < maxAttempts)
		{
			attempts++;
			var maxX = Mathf.Max(0, GridWidth - _buildingWidthCells);
			var maxY = Mathf.Max(0, GridHeight - _buildingHeightCells);
			var maxGridX = maxX / MinGridLineStepCells;
			var maxGridY = maxY / MinGridLineStepCells;
			var origin = new Vector2I(
				_rng.RandiRange(0, maxGridX) * MinGridLineStepCells,
				_rng.RandiRange(0, maxGridY) * MinGridLineStepCells
			);
			if (!CanPlaceBuildingFootprint(origin))
				continue;

			AddBuildingFootprint(origin);
			_lastCompletedBuildingOrigin = origin;
			placed++;
		}
	}

	private bool CanPlaceBuildingFootprint(Vector2I origin)
	{
		if (origin.X < 0 || origin.Y < 0)
			return false;
		if (origin.X + _buildingWidthCells > GridWidth || origin.Y + _buildingHeightCells > GridHeight)
			return false;

		for (var y = 0; y < _buildingHeightCells; y++)
		{
			for (var x = 0; x < _buildingWidthCells; x++)
			{
				var cell = origin + new Vector2I(x, y);
				if (_terrain[cell.X, cell.Y] != TerrainType.Grass)
					return false;
				if (_buildingCells.Contains(cell))
					return false;
			}
		}

		return true;
	}

	private void AddBuildingFootprint(Vector2I origin)
	{
		_buildingFootprints.Add(new Rect2I(origin, new Vector2I(_buildingWidthCells, _buildingHeightCells)));
		for (var y = 0; y < _buildingHeightCells; y++)
		{
			for (var x = 0; x < _buildingWidthCells; x++)
			{
				_buildingCells.Add(origin + new Vector2I(x, y));
			}
		}
	}

	private static int RoundUpToStep(int value, int step)
	{
		if (step <= 1)
			return Mathf.Max(1, value);
		var safe = Mathf.Max(1, value);
		return ((safe + step - 1) / step) * step;
	}

	private void InitializeStartingCharacters()
	{
		_characters.Clear();
		var center = new Vector2I(GridWidth / 2, GridHeight / 2);
		var spawnCells = BuildSpawnCells(center, 10, spacing: 12);

		var spawnPlan = new[]
		{
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Civilian,
			ColonyCharacterType.Expert,
			ColonyCharacterType.Soldier,
			ColonyCharacterType.Soldier
		};

		for (var i = 0; i < spawnPlan.Length; i++)
		{
			var type = spawnPlan[i];
			var label = $"{type} {i + 1}";
			var safeCell = FindNearestWalkableCell(spawnCells[i]);
			_characters.Add(ColonyCharacter.CreateByType(type, safeCell, label, null));
		}

		// Roster is complete: civilians flee toward the farthest point from all attackers; others keep random dest.
		for (var j = 0; j < _characters.Count; j++)
		{
			var c = _characters[j];
			c.Destination = c.Type == ColonyCharacterType.Civilian
				? FindCivilianFleeDestination(c, c.Cell)
				: FindRandomValidDestinationCell();
		}
	}

	private List<Vector2I> BuildSpawnCells(Vector2I center, int count, int spacing)
	{
		var cells = new List<Vector2I>(count);
		var cols = 5;
		var rows = Mathf.CeilToInt(count / (float)cols);
		var startX = center.X - ((cols - 1) * spacing) / 2;
		var startY = center.Y - ((rows - 1) * spacing) / 2;

		for (var i = 0; i < count; i++)
		{
			var col = i % cols;
			var row = i / cols;
			var x = Mathf.Clamp(startX + col * spacing, 0, GridWidth - 1);
			var y = Mathf.Clamp(startY + row * spacing, 0, GridHeight - 1);
			cells.Add(new Vector2I(x, y));
		}

		return cells;
	}

	private void RelocateCharactersToWalkableGround()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var character = _characters[i];
			if (character.Health <= 0)
				continue;
			if (IsWalkableCharacterCell(character.Cell))
				continue;

			character.Cell = FindNearestWalkableCell(character.Cell);
		}
	}

	/// <summary>Spawn/refresh the Cthulhu token sprite east of map center; anchor is BFSed so the full token board avoids water and buildings (see <see cref="IsWalkableForServantAnchorCell(Servant, Vector2I)"/>).</summary>
	private void PlaceServant()
	{
		var center = new Vector2I(GridWidth / 2, GridHeight / 2);
		var preferred = new Vector2I(
			Mathf.Clamp(center.X + 64, 0, GridWidth - 1),
			Mathf.Clamp(center.Y + 32, 0, GridHeight - 1)
		);
		var start = FindNearestWalkableCell(preferred);
		var proto = Servant.CreateAt(start);
		var cell = FindNearestCellSatisfying(start, a => IsWalkableForServantAnchorCell(proto, a));
		proto.Cell = cell;
		_servant = proto;
		_servantPursueCell = null;
		_servantPathQueue.Clear();
		_servantMoveBudget = 0f;
		_servantRestSecondsRemaining = 0f;
	}

	private void EnsureServantOnWalkable()
	{
		if (_servant == null)
			return;
		if (IsWalkableForServantAnchorCell(_servant, _servant.Cell))
			return;
		_servant.Cell = FindNearestCellSatisfying(_servant.Cell, a => IsWalkableForServantAnchorCell(_servant, a));
	}

	private void OnCharacterDied(int index)
	{
		var c = _characters[index];
		c.Destination = c.Cell;
		_characterPathQueues[index].Clear();
		_characterMoveBudget[index] = 0f;
	}

	private void EnsureCharacterDestinationsAreValid()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var character = _characters[i];
			if (character.Health <= 0)
			{
				character.Destination = character.Cell;
				continue;
			}
			var destination = character.Destination;
			if (destination.X < 0 || destination.Y < 0 || destination.X >= GridWidth || destination.Y >= GridHeight ||
				!IsValidDestinationCell(destination))
			{
				character.Destination = character.Type == ColonyCharacterType.Civilian
					? FindCivilianFleeDestination(character, character.Cell)
					: FindRandomValidDestinationCell(character.Cell);
			}
		}
	}

	private bool IsValidDestinationCell(Vector2I cell) =>
		IsWalkableCharacterCell(cell);

	/// <summary>
	/// 4-way BFS from a clamped start; first cell matching <paramref name="isValidAnchor"/> (default single-cell <see cref="IsWalkableCharacterCell"/> = land, no building).
	/// </summary>
	private Vector2I FindNearestCellSatisfying(Vector2I start, Func<Vector2I, bool> isValidAnchor)
	{
		var clampedStart = new Vector2I(
			Mathf.Clamp(start.X, 0, GridWidth - 1),
			Mathf.Clamp(start.Y, 0, GridHeight - 1)
		);
		if (isValidAnchor(clampedStart))
			return clampedStart;

		var visited = new bool[GridWidth, GridHeight];
		var queue = new Queue<Vector2I>();
		var dirs = new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
		visited[clampedStart.X, clampedStart.Y] = true;
		queue.Enqueue(clampedStart);

		while (queue.Count > 0)
		{
			var cell = queue.Dequeue();
			for (var i = 0; i < dirs.Length; i++)
			{
				var next = cell + dirs[i];
				if (next.X < 0 || next.Y < 0 || next.X >= GridWidth || next.Y >= GridHeight)
					continue;
				if (visited[next.X, next.Y])
					continue;

				visited[next.X, next.Y] = true;
				if (isValidAnchor(next))
					return next;
				queue.Enqueue(next);
			}
		}

		return clampedStart;
	}

	private Vector2I FindNearestWalkableCell(Vector2I start) =>
		FindNearestCellSatisfying(start, IsWalkableCharacterCell);

	private Vector2I FindRandomValidDestinationCell(Vector2I? avoidCell = null)
	{
		var maxAttempts = 2000;
		for (var i = 0; i < maxAttempts; i++)
		{
			var x = _rng.RandiRange(0, GridWidth - 1);
			var y = _rng.RandiRange(0, GridHeight - 1);
			var c = new Vector2I(x, y);
			if (!IsValidDestinationCell(c))
				continue;
			if (avoidCell.HasValue && c == avoidCell.Value)
				continue;
			return c;
		}

		return FindNearestWalkableCell(new Vector2I(GridWidth / 2, GridHeight / 2));
	}

	private int GetPathMoveCost(Vector2I cell) =>
		_terrain[cell.X, cell.Y] == TerrainType.Forest ? ForestTerrainMoveCost : DefaultTerrainMoveCost;

	private void EnsurePathStateSize()
	{
		while (_characterPathQueues.Count < _characters.Count)
		{
			_characterPathQueues.Add(new Queue<Vector2I>());
			_characterMoveBudget.Add(0f);
		}

		while (_characterPathQueues.Count > _characters.Count)
		{
			_characterPathQueues.RemoveAt(_characterPathQueues.Count - 1);
			_characterMoveBudget.RemoveAt(_characterMoveBudget.Count - 1);
		}
	}

	private bool ReplanCharacterPath(int index)
	{
		EnsurePathStateSize();
		var character = _characters[index];
		var pathQueue = _characterPathQueues[index];
		pathQueue.Clear();
		_characterMoveBudget[index] = 0f;

		if (character.Cell == character.Destination)
			return true;

		var fullPath = GridAStar.FindPath(
			GridWidth,
			GridHeight,
			character.Cell,
			character.Destination,
			IsWalkableCharacterCell,
			GetPathMoveCost
		);
		if (fullPath == null)
			return false;

		for (var s = 1; s < fullPath.Count; s++)
			pathQueue.Enqueue(fullPath[s]);

		return true;
	}

	private void TryReplanPathWithDestinationFallback(int index)
	{
		if (ReplanCharacterPath(index))
			return;
		for (var attempt = 0; attempt < 8; attempt++)
		{
			_characters[index].Destination = GetDestinationForPathFallback(_characters[index]);
			if (ReplanCharacterPath(index))
				return;
		}
	}

	private void StepCharactersTowardDestination()
	{
		EnsurePathStateSize();
		for (var i = 0; i < _characters.Count; i++)
		{
			var character = _characters[i];
			var pathQueue = _characterPathQueues[i];
			if (character.Health <= 0)
				continue;

			if (character.Cell == character.Destination)
			{
				character.Destination = FindNewDestinationAfterArrival(character);
				TryReplanPathWithDestinationFallback(i);
				continue;
			}

			_characterMoveBudget[i] += MovementBudgetPerGridMajorStep;
			var moved = false;
			while (_characterMoveBudget[i] > 0f)
			{
				if (pathQueue.Count == 0)
				{
					if (!ReplanCharacterPath(i))
					{
						TryReplanPathWithDestinationFallback(i);
					}

					if (pathQueue.Count == 0)
						break;
				}

				var next = pathQueue.Peek();
				var dist = Mathf.Abs(next.X - character.Cell.X) + Mathf.Abs(next.Y - character.Cell.Y);
				if (dist != 1)
				{
					ReplanCharacterPath(i);
					if (pathQueue.Count == 0)
					{
						TryReplanPathWithDestinationFallback(i);
					}

					if (pathQueue.Count == 0)
						break;
					continue;
				}

				if (!IsWalkableCharacterCell(next))
				{
					ReplanCharacterPath(i);
					if (pathQueue.Count == 0)
					{
						TryReplanPathWithDestinationFallback(i);
					}

					if (pathQueue.Count == 0)
						break;
					continue;
				}

				var cost = (float)GetPathMoveCost(next);
				if (_characterMoveBudget[i] < cost)
					break;

				_characterMoveBudget[i] -= cost;
				pathQueue.Dequeue();
				character.Cell = next;
				moved = true;
			}

			if (moved)
			{
				// Reached destination along path: trim so next tick picks new dest if needed
				if (character.Cell == character.Destination)
					pathQueue.Clear();
			}
		}

		StepServant();
	}

	/// <summary>Chase the closest (by board-to-board border gap) <see cref="ColonyCharacter"/>; in range when L1 gap from Servant board to NPC board ≤ one major step (overlapping boards = gap 0).</summary>
	private void StepServant()
	{
		if (_servant is not { } servant)
			return;
		if (_servantRestSecondsRemaining > 0f)
			return;

		if (!TryGetServantChaseTarget(out var target, out var targetIndex))
		{
			_servantPathQueue.Clear();
			return;
		}

		var gap = ManhattanBorderGapBetweenRects(GetServantBoardRect(servant), GetColonyCharacterBoardRect(target));
		if (gap <= ServantAttackRangeCells)
		{
			_servantPathQueue.Clear();
			if (target.IsAlive)
			{
				var h = target.Health;
				target.Health = Mathf.Max(0, h - Servant.AttackDamage);
				if (h > 0 && !target.IsAlive)
					OnCharacterDied(targetIndex);
				_servantRestSecondsRemaining = ServantPostAttackStopSec;
			}
			return;
		}

		if (_servantPathQueue.Count == 0 || _servantPursueCell != target.Cell)
		{
			_servantPursueCell = target.Cell;
			ReplanServantPath(target.Cell);
		}

		if (_servantPathQueue.Count == 0)
			return;

		_servantMoveBudget += MovementBudgetPerGridMajorStep;
		var moved = false;
		while (_servantMoveBudget > 0f)
		{
			if (_servantPathQueue.Count == 0)
			{
				if (!ReplanServantPath(target.Cell))
					break;
				if (_servantPathQueue.Count == 0)
					break;
			}

			var next = _servantPathQueue.Peek();
			var d = Mathf.Abs(next.X - servant.Cell.X) + Mathf.Abs(next.Y - servant.Cell.Y);
			if (d != 1)
			{
				ReplanServantPath(target.Cell);
				if (_servantPathQueue.Count == 0)
					break;
				continue;
			}

			if (!IsWalkableForServantAnchorCell(next))
			{
				ReplanServantPath(target.Cell);
				if (_servantPathQueue.Count == 0)
					break;
				continue;
			}

			var cost = (float)GetPathMoveCost(next);
			if (_servantMoveBudget < cost)
				break;

			_servantMoveBudget -= cost;
			_servantPathQueue.Dequeue();
			servant.Cell = next;
			moved = true;
		}

		if (moved && servant.Cell == target.Cell)
			_servantPathQueue.Clear();
	}

	private bool ReplanServantPath(Vector2I destination)
	{
		if (_servant == null)
			return false;
		_servantPathQueue.Clear();
		_servantMoveBudget = 0f;

		if (_servant.Cell == destination)
			return true;

		var fullPath = GridAStar.FindPath(
			GridWidth,
			GridHeight,
			_servant.Cell,
			destination,
			c => IsWalkableForServantAnchorCell(c),
			GetPathMoveCost
		);
		if (fullPath == null)
			return false;

		for (var s = 1; s < fullPath.Count; s++)
			_servantPathQueue.Enqueue(fullPath[s]);

		return true;
	}

	/// <summary>Nearest living NPC by L1 border gap between token boards (grid space); ties favor lower list index.</summary>
	private bool TryGetServantChaseTarget(out ColonyCharacter? target, out int index)
	{
		target = null;
		index = -1;
		if (_servant is not { } s)
			return false;
		var servantR = GetServantBoardRect(s);
		ColonyCharacter? best = null;
		var bestD = float.MaxValue;
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (!c.IsAlive)
				continue;
			var d = ManhattanBorderGapBetweenRects(servantR, GetColonyCharacterBoardRect(c));
			if (d < bestD)
			{
				bestD = d;
				best = c;
				index = i;
			}
		}
		if (best == null)
			return false;
		target = best;
		return true;
	}

	private Vector2I FindNewDestinationAfterArrival(ColonyCharacter c) =>
		c.Type == ColonyCharacterType.Civilian
			? FindCivilianFleeDestination(c, c.Cell)
			: FindRandomValidDestinationCell(c.Cell);

	private static int ManhattanDistance(Vector2I a, Vector2I b) =>
		Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y);

	/// <summary>Axis-aligned token board in float grid coordinates; same placement as <see cref="DrawCharacter"/> / <see cref="DrawServant"/> (half-open via <see cref="Rect2.End"/>).</summary>
	private Rect2 GetColonyCharacterBoardRect(ColonyCharacter c)
	{
		var w = c.SpriteRows.Length == 0 ? 1 : c.SpriteRows[0].Length;
		var h = Mathf.Max(1, c.SpriteRows.Length);
		var pixelScale = Mathf.Max(2, CellSize * 2);
		return GetTokenBoardRectInGridCells(c.Cell, w, h, c.SpritePivot, pixelScale, CellSize);
	}

	/// <summary>Token board AABB for the Servant (includes <see cref="Servant.PixelScaleMultiplierVsCharacter"/>).</summary>
	/// <param name="anchorCell">Cell grid position for the Servant (same as <see cref="Servant.Cell"/> for current pose).</param>
	private Rect2 GetServantBoardRectForAnchor(Servant s, Vector2I anchorCell)
	{
		var w = s.SpriteRows.Length == 0 ? 1 : s.SpriteRows[0].Length;
		var h = Mathf.Max(1, s.SpriteRows.Length);
		var basePixel = Mathf.Max(2, CellSize * 2);
		var pixelScale = basePixel * Servant.PixelScaleMultiplierVsCharacter;
		return GetTokenBoardRectInGridCells(anchorCell, w, h, s.SpritePivot, pixelScale, CellSize);
	}

	private Rect2 GetServantBoardRect(Servant s) => GetServantBoardRectForAnchor(s, s.Cell);

	/// <summary>True if the Servant token AABB in grid space only covers walkable (non-water, non-building) cells — used by the same 4-way A* as other units.</summary>
	private bool IsWalkableForServantAnchorCell(Servant s, Vector2I anchorCell)
	{
		var r = GetServantBoardRectForAnchor(s, anchorCell);
		var ex = r.Position.X + r.Size.X;
		var ey = r.Position.Y + r.Size.Y;
		var y0 = (int)System.Math.Floor(r.Position.Y);
		var y1 = (int)System.Math.Ceiling(ey);
		var x0 = (int)System.Math.Floor(r.Position.X);
		var x1 = (int)System.Math.Ceiling(ex);
		for (var y = y0; y < y1; y++)
		{
			for (var x = x0; x < x1; x++)
			{
				if (x < 0 || y < 0 || x >= GridWidth || y >= GridHeight)
					return false;
				if (!IsWalkableCharacterCell(new Vector2I(x, y)))
					return false;
			}
		}
		return true;
	}

	/// <summary>Current Servant; same rules as A* and step (no water under any part of the board).</summary>
	private bool IsWalkableForServantAnchorCell(Vector2I anchorCell) =>
		_servant != null && IsWalkableForServantAnchorCell(_servant, anchorCell);

	private static Rect2 GetTokenBoardRectInGridCells(
		Vector2I cell, int spriteW, int spriteH, Vector2I pivot, float pixelScale, int cellSize)
	{
		var cs = Mathf.Max(1, cellSize);
		var leftX = (0f - pivot.X) * pixelScale;
		var rightX = (spriteW - pivot.X) * pixelScale;
		var topY = (0f - pivot.Y) * pixelScale;
		var bottomY = (spriteH - pivot.Y) * pixelScale;
		var xMin = (cell.X * cs + leftX) / cs;
		var yMin = (cell.Y * cs + topY) / cs;
		var xMax = (cell.X * cs + rightX) / cs;
		var yMax = (cell.Y * cs + bottomY) / cs;
		return new Rect2(xMin, yMin, xMax - xMin, yMax - yMin);
	}

	/// <summary>L1 distance between the two axis-aligned boards: sum of 1D gaps (0 when rectangles overlap in 2D — including NPC fully inside Servant area).</summary>
	private static float ManhattanBorderGapBetweenRects(Rect2 a, Rect2 b)
	{
		var gx = AxisGapHalfOpen(a.Position.X, a.End.X, b.Position.X, b.End.X);
		var gy = AxisGapHalfOpen(a.Position.Y, a.End.Y, b.Position.Y, b.End.Y);
		return gx + gy;
	}

	/// <summary>Distance between two half-open intervals on the line; 0 if they overlap or touch.</summary>
	private static float AxisGapHalfOpen(float a0, float a1, float b0, float b1)
	{
		if (a1 <= b0)
			return b0 - a1;
		if (b1 <= a0)
			return a0 - b1;
		return 0f;
	}

	/// <summary>Attack-capable units (Expert, Soldier) are treated as threats for flee targeting.</summary>
	private List<Vector2I> GetEnemyCellsForCivilian(ColonyCharacter self)
	{
		var list = new List<Vector2I>();
		for (var i = 0; i < _characters.Count; i++)
		{
			var o = _characters[i];
			if (o.Id == self.Id)
				continue;
			if (o.CanAttack && o.Health > 0)
				list.Add(o.Cell);
		}
		if (_servant != null)
			list.Add(_servant.Cell);
		return list;
	}

	/// <summary>Valid, non-water cell that maximizes distance to the nearest current enemy (Manhattan). No enemies → random valid.</summary>
	private Vector2I FindCivilianFleeDestination(ColonyCharacter self, Vector2I? avoidCell = null)
	{
		var enemies = GetEnemyCellsForCivilian(self);
		if (enemies.Count == 0)
			return FindRandomValidDestinationCell(avoidCell ?? self.Cell);

		var bestScore = -1;
		var best = new Vector2I(
			Mathf.Clamp(self.Cell.X, 0, GridWidth - 1),
			Mathf.Clamp(self.Cell.Y, 0, GridHeight - 1)
		);

		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				var cell = new Vector2I(x, y);
				if (!IsValidDestinationCell(cell))
					continue;
				if (avoidCell.HasValue && cell == avoidCell.Value)
					continue;
				var minD = int.MaxValue;
				for (var e = 0; e < enemies.Count; e++)
				{
					var d = ManhattanDistance(cell, enemies[e]);
					if (d < minD)
						minD = d;
				}
				if (minD > bestScore)
				{
					bestScore = minD;
					best = cell;
				}
			}
		}

		if (bestScore < 0)
			return FindRandomValidDestinationCell(avoidCell);
		return best;
	}

	private Vector2I GetDestinationForPathFallback(ColonyCharacter c) =>
		c.Type == ColonyCharacterType.Civilian
			? FindCivilianFleeDestination(c, c.Cell)
			: FindRandomValidDestinationCell(c.Cell);

	private void RecalculateCivilianFleeDestinationsFromCurrentEnemies()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.Type != ColonyCharacterType.Civilian)
				continue;
			c.Destination = FindCivilianFleeDestination(c, c.Cell);
		}
	}

	private bool IsWalkableCharacterCell(Vector2I cell)
	{
		if (cell.X < 0 || cell.Y < 0 || cell.X >= GridWidth || cell.Y >= GridHeight)
			return false;
		if (_buildingCells.Contains(cell))
			return false;
		return _terrain[cell.X, cell.Y] != TerrainType.Water;
	}

	private void RemoveAllBuildingsFromBoard()
	{
		_buildingCells.Clear();
		_buildingFootprints.Clear();
		_activeBuildingQueue.Clear();
		_hasActiveBuildingProject = false;
		_buildingSimEnabled = false;
		_buildingGrowthTimer?.Stop();
	}

}
