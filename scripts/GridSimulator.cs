using System;
using Godot;
using System.Collections.Generic;

public partial class GridSimulator : Node2D
{
	[Export]
	public int GridWidth { get; set; } = 960;

	[Export]
	public int GridHeight { get; set; } = 540;

	/// <summary>Maximum pixels per simulation cell (when the window is large). The board is scaled down to fit the visible area to the left of the control panel.</summary>
	[Export]
	public int CellSize { get; set; } = 2;

	[Export]
	public int BuildingGrowthPerTick { get; set; } = 3;

	[Export]
	public float BuildingGrowthIntervalSec { get; set; } = 0.2f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float TerrainSmoothness { get; set; } = 0.6f;
	[Export]
	public bool DrawMajorGridLines { get; set; } = false;

	private Label? _statusLabel;
	private Label? _statsLabel;
	private Control? _topPanel;
	private GridControlPanel? _controlPanel;
	private readonly List<ColonyCharacter> _characters = new();
	private readonly List<EnemyCharacter> _enemies = new();
	private readonly List<Queue<Vector2I>> _enemyPathQueues = new();
	private readonly List<float> _enemyMoveBudget = new();
	/// <summary>Invalid sentinel for <see cref="_enemyPursueCells"/>; grid coords are always non-negative.</summary>
	private static readonly Vector2I EnemyPursueNone = new(-1, -1);
	private readonly List<float> _enemyRestSecondsRemaining = new();
	private readonly List<Vector2I> _enemyPursueCells = new();
	private Servant? _servant;
	private Vector2I? _servantPursueCell;
	private readonly Queue<Vector2I> _servantPathQueue = new();
	private float _servantMoveBudget;
	private readonly RandomNumberGenerator _rng = new();
	private bool _isCharacterVisible = true;
	private TerrainType[,] _terrain = null!;
	private Image? _terrainBoardImage;
	private ImageTexture? _terrainBoardTexture;
	private float _characterMoveAccumulatorSec;

	private enum ColonyAttackPoolKind
	{
		Servant,
		Enemy
	}

	/// <summary>Reused to avoid per-tick List allocations in combat.</summary>
	private readonly List<(int manh, int j)> _enemyCombatPool = new();
	private readonly List<(int manh, ColonyAttackPoolKind kind, int index)> _colonyCombatPool = new();
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
	private Texture2D _buildingTexture3 = null!;
	private Texture2D _buildingTexture4 = null!;
	private readonly List<Texture2D> _buildingFootprintTextures = new();
	private const int BuildingSizeMultiplier = 2;
	/// <summary>Draw <see cref="_buildingTexture3"/> this many times larger (centered on the same footprint; path logic unchanged).</summary>
	private const float Build3TextureDisplayScale = 2f;
	/// <summary>Major grid line spacing in cell units (one “major step” in design docs).</summary>
	public const int MinGridLineStepCells = 10;
	/// <summary>Seconds between path-movement ticks. Smaller = smoother motion; each tick accrues budget for 4-way steps.</summary>
	private const float CharacterMoveStepSec = 0.1f;
	/// <summary>Caps fixed-timestep catch-up so a single engine frame never runs an unbounded number of sim steps (e.g. after focus loss, debugger, or a long hitch).</summary>
	private const int MaxSimSubstepsPerFrame = 20;
	/// <summary>Per-unit cap on 4-way path consumption in one sim tick; avoids a tight loop if the queue/path desyncs.</summary>
	private const int MaxPathConsumeIterationsPerUnit = 2048;
	/// <summary>
	/// Budget units added each tick, aligned to the major grid: enough to cross one 10-cell span
	/// on default-cost terrain in one tick (flat ground uses 1 unit per cell; forest uses 2).
	/// </summary>
	private const int MovementBudgetPerGridMajorStep = MinGridLineStepCells;
	private const int DefaultTerrainMoveCost = 1;
	private const int ForestTerrainMoveCost = 2; // 0.5x speed
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
	/// <summary>Pixels per grid cell for drawing and on-screen fit; ≤ <see cref="CellSize"/>, never larger than needed to fit the window.</summary>
	private float _boardPixelsPerCell = 1f;
	private Vector2 _boardGridOrigin;
	private Vector2 _lastLayoutViewportSize = Vector2.Zero;
	/// <summary>True after the first <see cref="_Process"/>; used to avoid skipping the initial <see cref="QueueRedraw"/> before any sim step.</summary>
	private bool _processHasRun;

	public override void _Ready()
	{
		_rng.Randomize();
		_statusLabel = GetNodeOrNull<Label>("%StatusLabel");
		_statsLabel = GetNodeOrNull<Label>("%StatsLabel");
		_topPanel = GetNodeOrNull<Control>("UI/TopPanel");
		_controlPanel = GetNodeOrNull<GridControlPanel>("%ControlPanel");
		if (_controlPanel != null)
		{
			_controlPanel.RandomizeCharacterRequested += OnRandomizeCharacterPressed;
			_controlPanel.BuildingSimulatorRequested += OnOpenBuildingSimulatorPressed;
			_controlPanel.GenerateTerrainRequested += OnGenerateTerrainPressed;
			_controlPanel.TerrainRemoveRequested += OnTerrainRemoveRequested;
			_controlPanel.BuildingExpandSizeChanged += OnBuildingExpandSizeChanged;
			_controlPanel.TerrainSmoothnessChanged += OnTerrainSmoothnessChanged;
			_controlPanel.ShowCharacterRequested += OnShowCharacterPressed;
			_controlPanel.RemoveCharacterRequested += OnRemoveCharacterPressed;
		}

		// Ensure the simulator node processes even if parent/owner toggles process mode.
		SetProcess(true);

		_terrain = new TerrainType[GridWidth, GridHeight];
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		RefreshTerrainBoardTexture();
		_buildingTexture3 = GD.Load<Texture2D>("res://sprites/buildings/build3.png");
		_buildingTexture4 = GD.Load<Texture2D>("res://sprites/buildings/build4.png");
		_buildingGrowthTimer = new Timer();
		_buildingGrowthTimer.WaitTime = BuildingGrowthIntervalSec;
		_buildingGrowthTimer.OneShot = false;
		_buildingGrowthTimer.Timeout += OnBuildingGrowthTick;
		AddChild(_buildingGrowthTimer);

		InitializeStartingCharacters();
		InitializeStartingEnemies();
		(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		// Scene starts without a Servant; use PlaceServant() from gameplay or terrain tools when you add that flow.
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		for (var pe = 0; pe < _enemies.Count; pe++)
			TryReplanEnemyPathWithDestinationFallback(pe);
		_controlPanel?.SetBuildingExpandSize(BuildingGrowthPerTick);
		_controlPanel?.SetTerrainSmoothness(TerrainSmoothness);
		UpdateBoardLayout();
		_lastLayoutViewportSize = GetViewport().GetVisibleRect().Size;
		UpdateHud();
		QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		// Keep a minimal reset hotkey while the project is still scaffolding.
		if (key.Keycode == Key.R)
		{
			UpdateBoardLayout();
			UpdateHud();
			QueueRedraw();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		var d = (float)delta;
		_servantRestSecondsRemaining = Mathf.Max(0f, _servantRestSecondsRemaining - d);
		EnsureEnemyPathStateSize();
		for (var ei = 0; ei < _enemies.Count; ei++)
			_enemyRestSecondsRemaining[ei] = Mathf.Max(0f, _enemyRestSecondsRemaining[ei] - d);

		// One layout pass per frame (was duplicated inside <see cref="UpdateHud"/> every call).
		UpdateBoardLayout();
		_characterMoveAccumulatorSec += d;
		// Prevent spiral-of-death: a huge delta in one frame must not run thousands of sim ticks in a single _Process.
		_characterMoveAccumulatorSec = Mathf.Min(
			_characterMoveAccumulatorSec,
			CharacterMoveStepSec * MaxSimSubstepsPerFrame);

		if (_characterMoveAccumulatorSec < CharacterMoveStepSec)
		{
			UpdateHud();
			// No sim step: board pixels are static — avoid 60 full redraws/sec unless the window or HUD changed.
			var v = GetViewport().GetVisibleRect().Size;
			if (!_processHasRun || v != _lastLayoutViewportSize)
			{
				_lastLayoutViewportSize = v;
				QueueRedraw();
			}
			_processHasRun = true;
			return;
		}

		while (_characterMoveAccumulatorSec >= CharacterMoveStepSec)
		{
			_characterMoveAccumulatorSec -= CharacterMoveStepSec;
			StepCharactersTowardDestination();
		}

		_lastLayoutViewportSize = GetViewport().GetVisibleRect().Size;
		_processHasRun = true;
		UpdateHud();
		QueueRedraw();
	}

	public override void _Draw()
	{
		var s = _boardPixelsPerCell;
		var origin = _boardGridOrigin;
		var boardSize = new Vector2(GridWidth * s, GridHeight * s);

		// One scaled texture for the full terrain (avoids hundreds of thousands of per-cell draw calls per frame).
		if (_terrainBoardTexture != null)
			DrawTextureRect(_terrainBoardTexture, new Rect2(origin, boardSize), false);
		else
		{
			DrawRect(new Rect2(origin, boardSize), new Color(0.07f, 0.08f, 0.12f));
		}

		DrawBuildingSprites(origin);

		if (DrawMajorGridLines)
		{
			var gridStep = Mathf.Max(MinGridLineStepCells, 1);
			var lineColor = new Color(0.28f, 0.28f, 0.30f, 0.78f);
			for (var x = 0; x <= GridWidth; x += gridStep)
			{
				var xPos = origin.X + x * s;
				DrawLine(new Vector2(xPos, origin.Y), new Vector2(xPos, origin.Y + boardSize.Y), lineColor, 1f);
			}
			for (var y = 0; y <= GridHeight; y += gridStep)
			{
				var yPos = origin.Y + y * s;
				DrawLine(new Vector2(origin.X, yPos), new Vector2(origin.X + boardSize.X, yPos), lineColor, 1f);
			}
		}

		if (_isCharacterVisible)
		{
			for (var i = 0; i < _characters.Count; i++)
			{
				if (_characters[i].Health > 0)
					DrawCharacterDestination(_characters[i], origin);
				DrawCharacter(_characters[i], origin);
			}
			for (var ei = 0; ei < _enemies.Count; ei++)
			{
				if (_enemies[ei].Health > 0)
					DrawCharacterDestinationForEnemy(_enemies[ei], origin);
				DrawEnemy(_enemies[ei], origin);
			}
			if (_servant != null)
				DrawServant(_servant, origin, drawDead: _servant.Health <= 0);
		}
		DrawRect(new Rect2(origin, boardSize), new Color(0.48f, 0.63f, 0.88f), false, 2f);
	}

	private void DrawCharacterDestination(ColonyCharacter character, Vector2 gridOrigin) =>
		DrawUnitDestinationRect(character.Destination, gridOrigin, new Color(0.93f, 0.86f, 0.22f, 0.92f));

	private void DrawCharacterDestinationForEnemy(EnemyCharacter enemy, Vector2 gridOrigin) =>
		DrawUnitDestinationRect(enemy.Destination, gridOrigin, new Color(0.93f, 0.55f, 0.22f, 0.92f));

	private void DrawUnitDestinationRect(Vector2I destination, Vector2 gridOrigin, Color color)
	{
		var s = _boardPixelsPerCell;
		var markerTopLeft = gridOrigin + new Vector2(destination.X * s, destination.Y * s);
		var markerSize = Mathf.Max(1f, s);
		var inset = markerSize <= 2f ? 0f : 1f;
		var rect = new Rect2(
			markerTopLeft + new Vector2(inset, inset),
			new Vector2(Mathf.Max(1f, markerSize - inset * 2f), Mathf.Max(1f, markerSize - inset * 2f))
		);
		DrawRect(rect, color, false, 1f);
	}

	private void DrawCharacter(ColonyCharacter character, Vector2 gridOrigin)
	{
		var s = _boardPixelsPerCell;
		var cellTopLeft = gridOrigin + new Vector2(character.Cell.X * s, character.Cell.Y * s);
		var pixelScale = Mathf.Max(2, s * 2);

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

		DrawNpcToolColony(character, cellTopLeft, pixelScale);
	}

	private void DrawEnemy(EnemyCharacter e, Vector2 gridOrigin)
	{
		var s = _boardPixelsPerCell;
		var cellTopLeft = gridOrigin + new Vector2(e.Cell.X * s, e.Cell.Y * s);
		var pixelScale = Mathf.Max(2, s * 2);
		if (e.Health <= 0)
		{
			DrawLayingDownCharacter(cellTopLeft, pixelScale);
			return;
		}

		for (var y = 0; y < e.SpriteRows.Length; y++)
		{
			var row = e.SpriteRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!e.Palette.TryGetValue(token, out var color))
					continue;
				var px = cellTopLeft + new Vector2(
					(x - e.SpritePivot.X) * pixelScale,
					(y - e.SpritePivot.Y) * pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}
		DrawNpcToolEnemy(e, cellTopLeft, pixelScale);
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
	private void DrawServant(Servant servant, Vector2 gridOrigin, bool drawDead)
	{
		var s = _boardPixelsPerCell;
		var cellTopLeft = gridOrigin + new Vector2(servant.Cell.X * s, servant.Cell.Y * s);
		var basePixel = Mathf.Max(2, s * 2);
		if (drawDead)
		{
			DrawLayingDownCharacter(cellTopLeft, basePixel);
			return;
		}

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

	private void DrawNpcToolColony(ColonyCharacter character, Vector2 cellTopLeft, float pixelScale)
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

	private void DrawNpcToolEnemy(EnemyCharacter e, Vector2 cellTopLeft, float pixelScale)
	{
		if (e.Tool == CharacterToolType.None || e.ToolRows.Length == 0)
			return;
		for (var y = 0; y < e.ToolRows.Length; y++)
		{
			var row = e.ToolRows[y];
			for (var x = 0; x < row.Length; x++)
			{
				var token = row[x];
				if (token == '.')
					continue;
				if (!e.ToolPalette.TryGetValue(token, out var color))
					continue;
				var px = cellTopLeft + new Vector2(
					(x - e.ToolPivot.X) * pixelScale,
					(y - e.ToolPivot.Y) * pixelScale
				);
				DrawRect(new Rect2(px, new Vector2(pixelScale, pixelScale)), color);
			}
		}
	}

	/// <summary>Keeps the full <see cref="GridWidth"/>×<see cref="GridHeight"/> sim visible, capped by <see cref="CellSize"/>; reserves space on the right only when a <see cref="GridControlPanel"/> is present.</summary>
	private void UpdateBoardLayout()
	{
		var v = GetViewport().GetVisibleRect().Size;
		if (v.X < 2f || v.Y < 2f)
		{
			_boardPixelsPerCell = Mathf.Max(1f, CellSize);
			_boardGridOrigin = new Vector2(16f, 16f);
			return;
		}
		float panelLeftX;
		if (_controlPanel != null)
		{
			panelLeftX = _controlPanel.GetGlobalRect().Position.X;
			if (panelLeftX < 4f)
				panelLeftX = v.X - 240f;
		}
		else
			panelLeftX = v.X;
		var leftPadding = 16f;
		var rightPadding = 12f;
		var availableWidth = panelLeftX - rightPadding - leftPadding;
		var topPanelBottom = _topPanel?.GetGlobalRect().End.Y ?? 0f;
		if (topPanelBottom < 1f)
			topPanelBottom = 0f;
		var topPadding = 16f;
		var bottomPadding = 16f;
		var topBound = topPanelBottom + topPadding;
		var availableHeight = v.Y - topBound - bottomPadding;
		var fitW = availableWidth / Mathf.Max(1, GridWidth);
		var fitH = availableHeight / Mathf.Max(1, GridHeight);
		_boardPixelsPerCell = Mathf.Max(1f, Mathf.Min((float)CellSize, Mathf.Min(fitW, fitH)));
		var s = _boardPixelsPerCell;
		var boardWidth = GridWidth * s;
		var boardHeight = GridHeight * s;
		var x = Mathf.Floor(leftPadding + (availableWidth - boardWidth) * 0.5f);
		x = Mathf.Max(leftPadding, x);
		var y = Mathf.Floor(topBound + (availableHeight - boardHeight) * 0.5f);
		y = Mathf.Max(topBound, y);
		_boardGridOrigin = new Vector2(x, y);
	}

	/// <summary>Rebuilds the 1-px-per-cell CPU terrain image; call after any change to <see cref="_terrain"/>.</summary>
	private void RefreshTerrainBoardTexture()
	{
		if (GridWidth <= 0 || GridHeight <= 0)
			return;
		if (_terrainBoardImage == null
		    || _terrainBoardImage.GetWidth() != GridWidth
		    || _terrainBoardImage.GetHeight() != GridHeight)
		{
			_terrainBoardImage = Image.CreateEmpty(GridWidth, GridHeight, false, Image.Format.Rgb8);
		}
		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
				_terrainBoardImage.SetPixel(x, y, TerrainSystem.TerrainToColor(_terrain[x, y], x, y));
		}
		if (_terrainBoardTexture == null)
			_terrainBoardTexture = ImageTexture.CreateFromImage(_terrainBoardImage);
		else
			_terrainBoardTexture.Update(_terrainBoardImage);
	}

	/// <summary>Updates on-screen text and control panel. Call <see cref="UpdateBoardLayout"/> once per frame first (e.g. from <see cref="_Process"/> or before this from UI callbacks).</summary>
	private void UpdateHud()
	{
		if (_statusLabel != null)
		{
			var projectState = _hasActiveBuildingProject ? "Building" : _buildingSimEnabled ? "Seeking Next" : "Idle";
			var buildingState = _buildingSimEnabled ? $"ON ({_buildingCells.Count})" : "OFF";
			_statusLabel.Text = $"Character simulator scaffold | Terrain tools: Remove mode | Building Sim: {buildingState} | Project: {projectState}";
		}
		if (_statsLabel != null)
		{
			var visibility = _isCharacterVisible ? "Shown" : "Removed";
			var civilianCount = 0;
			var expertCount = 0;
			var soldierCount = 0;
			ColonyCharacter? profileC = null, profileE = null, profileS = null;
			for (var i = 0; i < _characters.Count; i++)
			{
				switch (_characters[i].Type)
				{
					case ColonyCharacterType.Civilian:
						civilianCount++;
						profileC ??= _characters[i];
						break;
					case ColonyCharacterType.Expert:
						expertCount++;
						profileE ??= _characters[i];
						break;
					case ColonyCharacterType.Soldier:
						soldierCount++;
						profileS ??= _characters[i];
						break;
				}
			}
			var crazyCount = 0;
			var monsterCount = 0;
			EnemyCharacter? profileCr = null, profileM = null;
			for (var i = 0; i < _enemies.Count; i++)
			{
				if (_enemies[i].Type == EnemyType.Crazy)
				{
					crazyCount++;
					profileCr ??= _enemies[i];
				}
				else if (_enemies[i].Type == EnemyType.Monster)
				{
					monsterCount++;
					profileM ??= _enemies[i];
				}
			}
			var civStr = profileC != null ? $"{profileC.MaxHealth}HP/{profileC.Attack}atk" : "1/0";
			var expStr = profileE != null ? $"{profileE.MaxHealth}HP/{profileE.Attack}atk" : "6/3";
			var solStr = profileS != null ? $"{profileS.MaxHealth}HP/{profileS.Attack}atk" : "2/1";
			var crStr = profileCr != null ? $"{profileCr.MaxHealth}HP/{profileCr.Attack}atk" : "2/1";
			var mStr = profileM != null ? $"{profileM.MaxHealth}HP/{profileM.Attack}atk" : "3/2";
			var drawW = GridWidth * _boardPixelsPerCell;
			var drawH = GridHeight * _boardPixelsPerCell;
			_statsLabel.Text = $"Grid: {GridWidth}x{GridHeight} sim → {drawW:0.#}x{drawH:0.#} px display ({_boardPixelsPerCell:0.##} px/cell, cap {CellSize})  |  Colony: {_characters.Count} (C:{civilianCount} E:{expertCount} S:{soldierCount})  Enemies: {_enemies.Count} (Cr:{crazyCount} M:{monsterCount})  |  Building: {_buildingWidthCells}x{_buildingHeightCells}  |  Profiles: C {civStr}  E {expStr}  S {solStr}  Cr {crStr}  M {mStr}  |  NPC State: {visibility}";
		}
		_controlPanel?.SetCharacterVisibilityState(_isCharacterVisible);
		_controlPanel?.SetCharacterRandomizeEnabled(_isCharacterVisible);
	}

	private void OnRandomizeCharacterPressed()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var current = _characters[i];
			_characters[i] = ColonyCharacter.CreateByType(current.Type, current.Cell, current.DisplayName, current.Destination);
		}
		for (var e = 0; e < _enemies.Count; e++)
		{
			var cur = _enemies[e];
			_enemies[e] = EnemyCharacter.CreateByType(cur.Type, cur.Cell, cur.DisplayName, cur.Destination);
		}
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		for (var ri = 0; ri < _characters.Count; ri++)
			TryReplanPathWithDestinationFallback(ri);
		for (var re = 0; re < _enemies.Count; re++)
			TryReplanEnemyPathWithDestinationFallback(re);
		UpdateBoardLayout();
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

		UpdateBoardLayout();
		UpdateHud();
		QueueRedraw();
	}

	private void OnBuildingExpandSizeChanged(int delta)
	{
		BuildingGrowthPerTick = Mathf.Clamp(BuildingGrowthPerTick + delta, 1, 64);
		_controlPanel?.SetBuildingExpandSize(BuildingGrowthPerTick);
		UpdateBoardLayout();
		UpdateHud();
	}

	private void OnGenerateTerrainPressed()
	{
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		RefreshTerrainBoardTexture();
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		PlaceServant();
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		for (var pe = 0; pe < _enemies.Count; pe++)
			TryReplanEnemyPathWithDestinationFallback(pe);
		UpdateBoardLayout();
		UpdateHud();
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
		RefreshTerrainBoardTexture();

		RelocateEnemiesToWalkableGround();
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		for (var pe = 0; pe < _enemies.Count; pe++)
			TryReplanEnemyPathWithDestinationFallback(pe);
		EnsureServantOnWalkable();
		UpdateBoardLayout();
		UpdateHud();
		QueueRedraw();
	}

	private void OnTerrainSmoothnessChanged(float value)
	{
		TerrainSmoothness = Mathf.Clamp(value, 0f, 1f);
		_controlPanel?.SetTerrainSmoothness(TerrainSmoothness);
		// Apply immediately so slider feedback is obvious.
		TerrainSystem.InitializeGaussianConstrained(_terrain, _rng, TerrainSmoothness);
		RefreshTerrainBoardTexture();
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		PlaceServant();
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		for (var pe = 0; pe < _enemies.Count; pe++)
			TryReplanEnemyPathWithDestinationFallback(pe);
		UpdateBoardLayout();
		UpdateHud();
		QueueRedraw();
	}

	private void OnShowCharacterPressed()
	{
		_isCharacterVisible = true;
		UpdateBoardLayout();
		UpdateHud();
		QueueRedraw();
	}

	private void OnRemoveCharacterPressed()
	{
		_isCharacterVisible = false;
		UpdateBoardLayout();
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

		UpdateBoardLayout();
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
		if (_buildingFootprints.Count == 0 || _buildingFootprints.Count != _buildingFootprintTextures.Count)
			return;

		var s = _boardPixelsPerCell;
		for (var i = 0; i < _buildingFootprints.Count; i++)
		{
			var tex = _buildingFootprintTextures[i];
			if (tex == null)
				continue;
			var footprint = _buildingFootprints[i];
			var footprintTop = gridOrigin + new Vector2(footprint.Position.X * s, footprint.Position.Y * s);
			var baseSize = new Vector2(footprint.Size.X * s, footprint.Size.Y * s);
			Vector2 topLeft;
			Vector2 drawSize;
			if (ReferenceEquals(tex, _buildingTexture3))
			{
				drawSize = baseSize * Build3TextureDisplayScale;
				topLeft = footprintTop - baseSize * 0.5f * (Build3TextureDisplayScale - 1f);
			}
			else
			{
				topLeft = footprintTop;
				drawSize = baseSize;
			}
			DrawTextureRect(tex, new Rect2(topLeft, drawSize), false);
		}
	}

	private void GenerateRandomBuildingsAfterTerrain(int count)
	{
		if (_buildingWidthCells <= 0 || _buildingHeightCells <= 0)
			(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();

		_buildingCells.Clear();
		_buildingFootprints.Clear();
		_buildingFootprintTextures.Clear();
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

			var art = placed == 0 ? _buildingTexture3 : _buildingTexture4;
			AddBuildingFootprint(origin, art);
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

	private void AddBuildingFootprint(Vector2I origin, Texture2D sprite)
	{
		_buildingFootprints.Add(new Rect2I(origin, new Vector2I(_buildingWidthCells, _buildingHeightCells)));
		_buildingFootprintTextures.Add(sprite);
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
		var spawnCells = BuildSpawnCells(center, 8, spacing: 12);

		var spawnPlan = new[]
		{
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
				: FindRandomValidDestinationCell(c.Cell);
		}
	}

	/// <summary>Initial load: no hostiles. Spawn enemies later from gameplay / editor if you add that flow.</summary>
	private void InitializeStartingEnemies()
	{
		_enemies.Clear();
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

	private void RelocateEnemiesToWalkableGround()
	{
		for (var i = 0; i < _enemies.Count; i++)
		{
			var e = _enemies[i];
			if (e.Health <= 0)
				continue;
			if (IsWalkableCharacterCell(e.Cell))
				continue;
			e.Cell = FindNearestWalkableCell(e.Cell);
		}
	}

	/// <summary>Spawn/refresh the Cthulhu token at a random valid anchor; BFS nudges the anchor so the full token avoids water and buildings (see <see cref="IsWalkableForServantAnchorCell(Servant, Vector2I)"/>).</summary>
	private void PlaceServant()
	{
		var start = FindRandomValidDestinationCell();
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

	private void OnEnemyDied(int index)
	{
		var e = _enemies[index];
		e.Destination = e.Cell;
		_enemyPathQueues[index].Clear();
		_enemyMoveBudget[index] = 0f;
		EnsureEnemyPathStateSize();
		if (index < _enemyRestSecondsRemaining.Count)
		{
			_enemyRestSecondsRemaining[index] = 0f;
			_enemyPursueCells[index] = EnemyPursueNone;
		}
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

	private void EnsureEnemyDestinationsAreValid()
	{
		for (var i = 0; i < _enemies.Count; i++)
		{
			var e = _enemies[i];
			if (e.Health <= 0)
			{
				e.Destination = e.Cell;
				continue;
			}
			var d = e.Destination;
			if (d.X < 0 || d.Y < 0 || d.X >= GridWidth || d.Y >= GridHeight || !IsValidDestinationCell(d))
				e.Destination = FindRandomValidDestinationCell(e.Cell);
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

	private void EnsureEnemyPathStateSize()
	{
		while (_enemyPathQueues.Count < _enemies.Count)
		{
			_enemyPathQueues.Add(new Queue<Vector2I>());
			_enemyMoveBudget.Add(0f);
			_enemyRestSecondsRemaining.Add(0f);
			_enemyPursueCells.Add(EnemyPursueNone);
		}
		while (_enemyPathQueues.Count > _enemies.Count)
		{
			_enemyPathQueues.RemoveAt(_enemyPathQueues.Count - 1);
			_enemyMoveBudget.RemoveAt(_enemyMoveBudget.Count - 1);
			_enemyRestSecondsRemaining.RemoveAt(_enemyRestSecondsRemaining.Count - 1);
			_enemyPursueCells.RemoveAt(_enemyPursueCells.Count - 1);
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

	private bool ReplanEnemyPath(int index)
	{
		EnsureEnemyPathStateSize();
		var e = _enemies[index];
		var pathQueue = _enemyPathQueues[index];
		pathQueue.Clear();
		_enemyMoveBudget[index] = 0f;
		if (e.Cell == e.Destination)
			return true;
		var fullPath = GridAStar.FindPath(
			GridWidth,
			GridHeight,
			e.Cell,
			e.Destination,
			IsWalkableCharacterCell,
			GetPathMoveCost
		);
		if (fullPath == null)
			return false;
		for (var s = 1; s < fullPath.Count; s++)
			pathQueue.Enqueue(fullPath[s]);
		return true;
	}

	private void TryReplanEnemyPathWithDestinationFallback(int index)
	{
		if (ReplanEnemyPath(index))
			return;
		for (var attempt = 0; attempt < 8; attempt++)
		{
			_enemies[index].Destination = FindRandomValidDestinationCell(_enemies[index].Cell);
			if (ReplanEnemyPath(index))
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

			_characterMoveBudget[i] += MovementBudgetPerGridMajorStep * (character.MajorStepsPerSecond / 2f);
			var moved = false;
			var consumeIters = 0;
			while (_characterMoveBudget[i] > 0f && consumeIters < MaxPathConsumeIterationsPerUnit)
			{
				consumeIters++;
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
				if (cost <= 0f)
					break;
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

		EnsureEnemyPathStateSize();
		for (var ei = 0; ei < _enemies.Count; ei++)
		{
			var e = _enemies[ei];
			var pathQueue = _enemyPathQueues[ei];
			if (e.Health <= 0)
				continue;
			if (_enemyRestSecondsRemaining[ei] > 0f)
				continue;
			if (!TryGetEnemyChaseTarget(ei, out var chase, out _))
			{
				pathQueue.Clear();
				_enemyPursueCells[ei] = EnemyPursueNone;
				continue;
			}

			e.Destination = chase!.Cell;
			if (pathQueue.Count == 0 || _enemyPursueCells[ei] != chase.Cell)
			{
				_enemyPursueCells[ei] = chase.Cell;
				ReplanEnemyPath(ei);
			}

			_enemyMoveBudget[ei] += MovementBudgetPerGridMajorStep * (e.MajorStepsPerSecond / 2f);
			var movedEnemy = false;
			var enemyConsumeIters = 0;
			while (_enemyMoveBudget[ei] > 0f && enemyConsumeIters < MaxPathConsumeIterationsPerUnit)
			{
				enemyConsumeIters++;
				if (pathQueue.Count == 0)
				{
					if (!ReplanEnemyPath(ei))
						break;
					if (pathQueue.Count == 0)
						break;
				}
				var next = pathQueue.Peek();
				var distE = Mathf.Abs(next.X - e.Cell.X) + Mathf.Abs(next.Y - e.Cell.Y);
				if (distE != 1)
				{
					ReplanEnemyPath(ei);
					if (pathQueue.Count == 0)
						break;
					continue;
				}
				if (!IsWalkableCharacterCell(next))
				{
					ReplanEnemyPath(ei);
					if (pathQueue.Count == 0)
						break;
					continue;
				}
				var costE = (float)GetPathMoveCost(next);
				if (costE <= 0f)
					break;
				if (_enemyMoveBudget[ei] < costE)
					break;
				_enemyMoveBudget[ei] -= costE;
				pathQueue.Dequeue();
				e.Cell = next;
				movedEnemy = true;
			}
			if (movedEnemy && e.Cell == e.Destination)
				pathQueue.Clear();
		}

		StepEnemyCombat();
		StepColonyCharacterCombat();
		StepServant();
	}

	/// <summary>Enemies only hit colonists, not the Servant or each other.</summary>
	private void StepEnemyCombat()
	{
		EnsureEnemyPathStateSize();
		for (var ei = 0; ei < _enemies.Count; ei++)
		{
			var a = _enemies[ei];
			if (!a.IsAlive || !a.CanAttack)
				continue;
			if (_enemyRestSecondsRemaining[ei] > 0f)
				continue;
			var r = a.AttackRangeChebyshev;
			if (r <= 0)
				continue;
			var rFine = GetCombatChebyshevRadiusInFineCells(r);
			var pool = _enemyCombatPool;
			pool.Clear();
			for (var j = 0; j < _characters.Count; j++)
			{
				var t = _characters[j];
				if (!t.IsAlive)
					continue;
				if (ChebyshevDistance(a.Cell, t.Cell) > rFine)
					continue;
				pool.Add((ManhattanDistance(a.Cell, t.Cell), j));
			}
			pool.Sort((x, y) => x.manh.CompareTo(y.manh));
			var hits = 0;
			for (var p = 0; p < pool.Count && hits < a.MaxAttackTargets; p++)
			{
				var j = pool[p].j;
				var t = _characters[j];
				if (!t.IsAlive)
					continue;
				var th = t.Health;
				t.Health = Mathf.Max(0, t.Health - a.Attack);
				if (th > 0 && t.Health <= 0)
					OnCharacterDied(j);
				hits++;
			}
			if (hits > 0)
				_enemyRestSecondsRemaining[ei] = ServantPostAttackStopSec;
		}
	}

	/// <summary>Colony strikers hit <see cref="EnemyCharacter"/> and (if in range) the <see cref="Servant"/>; they do not damage other colonists.</summary>
	private void StepColonyCharacterCombat()
	{
		for (var ai = 0; ai < _characters.Count; ai++)
		{
			var a = _characters[ai];
			if (!a.IsAlive || !a.CanAttack)
				continue;
			var r = a.AttackRangeChebyshev;
			if (r <= 0)
				continue;
			var rFine = GetCombatChebyshevRadiusInFineCells(r);
			var pool = _colonyCombatPool;
			pool.Clear();
			for (var ei = 0; ei < _enemies.Count; ei++)
			{
				var e = _enemies[ei];
				if (!e.IsAlive)
					continue;
				if (ChebyshevDistance(a.Cell, e.Cell) > rFine)
					continue;
				pool.Add((ManhattanDistance(a.Cell, e.Cell), ColonyAttackPoolKind.Enemy, ei));
			}
			if (ColonyStrikerCanDamageServant(a) && _servant != null && _servant.Health > 0 &&
			    ChebyshevDistance(a.Cell, _servant.Cell) <= rFine)
				pool.Add((ManhattanDistance(a.Cell, _servant.Cell), ColonyAttackPoolKind.Servant, 0));
			pool.Sort((x, y) => x.manh.CompareTo(y.manh));
			var hits = 0;
			for (var p = 0; p < pool.Count && hits < a.MaxAttackTargets; p++)
			{
				var item = pool[p];
				switch (item.kind)
				{
					case ColonyAttackPoolKind.Servant:
						_servant!.Health = Mathf.Max(0, _servant.Health - a.Attack);
						hits++;
						break;
					case ColonyAttackPoolKind.Enemy:
					{
						var e = _enemies[item.index];
						if (!e.IsAlive)
							continue;
						var th = e.Health;
						e.Health = Mathf.Max(0, e.Health - a.Attack);
						if (th > 0 && e.Health <= 0)
							OnEnemyDied(item.index);
						hits++;
						break;
					}
				}
			}
		}
	}

	/// <summary>Only attack-capable colonists (Expert, Soldier) can damage the Servant at range.</summary>
	private static bool ColonyStrikerCanDamageServant(ColonyCharacter a) => a.CanAttack;

	/// <summary>Chase the closest living <see cref="ColonyCharacter"/>; melee when within design 3×3 (one major ring on this sim grid).</summary>
	private void StepServant()
	{
		if (_servant is not { } servant)
			return;
		if (servant.Health <= 0)
		{
			_servantPathQueue.Clear();
			return;
		}
		if (_servantRestSecondsRemaining > 0f)
			return;

		if (!TryGetServantChaseTarget(out var target, out var targetIndex))
		{
			_servantPathQueue.Clear();
			return;
		}

		var servantMeleeRFine = GetCombatChebyshevRadiusInFineCells(1);
		if (ChebyshevDistance(servant.Cell, target.Cell) <= servantMeleeRFine)
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

		_servantMoveBudget += MovementBudgetPerGridMajorStep * (Servant.MajorStepsPerSecond / 2f);
		var moved = false;
		var servantConsumeIters = 0;
		while (_servantMoveBudget > 0f && servantConsumeIters < MaxPathConsumeIterationsPerUnit)
		{
			servantConsumeIters++;
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
			if (cost <= 0f)
				break;
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

	/// <summary>Nearest living <see cref="ColonyCharacter"/> to this enemy by anchor Manhattan; ties favor lower list index.</summary>
	private bool TryGetEnemyChaseTarget(int enemyIndex, out ColonyCharacter? target, out int index)
	{
		target = null;
		index = -1;
		if (enemyIndex < 0 || enemyIndex >= _enemies.Count)
			return false;
		var s = _enemies[enemyIndex];
		if (s.Health <= 0)
			return false;
		ColonyCharacter? best = null;
		var bestD = int.MaxValue;
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (!c.IsAlive)
				continue;
			var d = ManhattanDistance(s.Cell, c.Cell);
			if (d < bestD || d == bestD && (index < 0 || i < index))
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

	/// <summary>Nearest living NPC by anchor <see cref="ColonyCharacter.Cell"/> Manhattan; ties favor lower list index.</summary>
	private bool TryGetServantChaseTarget(out ColonyCharacter? target, out int index)
	{
		target = null;
		index = -1;
		if (_servant is not { } s)
			return false;
		ColonyCharacter? best = null;
		var bestD = int.MaxValue;
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (!c.IsAlive)
				continue;
			var d = ManhattanDistance(s.Cell, c.Cell);
			if (d < bestD || d == bestD && (index < 0 || i < index))
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

	private static int ChebyshevDistance(Vector2I a, Vector2I b) =>
		Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y));

	/// <summary>
	/// Unit stats use “major board” reach (3×3 / 5×5 in design). This sim’s path grid is fine (one step per cell);
	/// one major step = <see cref="MinGridLineStepCells"/> fine cells. Converts r=1/2 into Chebyshev radius on the sim grid.
	/// </summary>
	private static int GetCombatChebyshevRadiusInFineCells(int attackRangeStat)
	{
		if (attackRangeStat <= 0)
			return 0;
		return attackRangeStat * MinGridLineStepCells;
	}

	/// <summary>Token board AABB for the Servant (includes <see cref="Servant.PixelScaleMultiplierVsCharacter"/>).</summary>
	/// <param name="anchorCell">Cell grid position for the Servant (same as <see cref="Servant.Cell"/> for current pose).</param>
	private Rect2 GetServantBoardRectForAnchor(Servant s, Vector2I anchorCell)
	{
		var w = s.SpriteRows.Length == 0 ? 1 : s.SpriteRows[0].Length;
		var h = Mathf.Max(1, s.SpriteRows.Length);
		var px = _boardPixelsPerCell;
		var basePixel = Mathf.Max(2, px * 2);
		var pixelScale = basePixel * Servant.PixelScaleMultiplierVsCharacter;
		return GetTokenBoardRectInGridCells(anchorCell, w, h, s.SpritePivot, pixelScale, px);
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
		Vector2I cell, int spriteW, int spriteH, Vector2I pivot, float pixelScale, float cellPixelSize)
	{
		var cs = Mathf.Max(1f, cellPixelSize);
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

	/// <summary>Attack-capable units (and the Servant while alive) are treated as threats for flee targeting.</summary>
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
		if (_servant != null && _servant.Health > 0)
			list.Add(_servant.Cell);
		for (var e = 0; e < _enemies.Count; e++)
		{
			var en = _enemies[e];
			if (en.CanAttack && en.Health > 0)
				list.Add(en.Cell);
		}
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
		_buildingFootprintTextures.Clear();
		_activeBuildingQueue.Clear();
		_hasActiveBuildingProject = false;
		_buildingSimEnabled = false;
		_buildingGrowthTimer?.Stop();
	}

}
