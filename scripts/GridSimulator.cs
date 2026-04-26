using System;
using Godot;
using System.Collections.Generic;

public partial class GridSimulator : Node2D
{
	[Export]
	public int GridWidth { get; set; } = 960;

	[Export]
	public int GridHeight { get; set; } = 360;

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

	[Export] public int InitialCurrency { get; set; } = 12;

	/// <summary>Player resource shown in the top bar; clamped 0–100 in UI and when set.</summary>
	public int PlayerCurrency
	{
		get => _playerCurrency;
		set
		{
			_playerCurrency = Mathf.Clamp(value, PlayerCurrencyMin, PlayerCurrencyMax);
			UpdateCurrencyUi();
		}
	}

	private const int PlayerCurrencyMin = 0;
	private const int PlayerCurrencyMax = 100;

	private Label? _statusLabel;
	private Label? _statsLabel;
	private Control? _topPanel;
	private Control? _abilityBarPanel;
	/// <summary>First ability card wrapper; <see cref="OnFirstAbilitySlotGuiInput"/> spawns a Servant on left click (each use adds one).</summary>
	private Control? _abilityCardSlot1Holder;
	private ProgressBar? _currencyBar;
	private Label? _currencyValueLabel;
	private GridControlPanel? _controlPanel;
	private int _playerCurrency;
	private readonly List<ColonyCharacter> _characters = new();
	private readonly List<EnemyCharacter> _enemies = new();
	private readonly List<Queue<Vector2I>> _enemyPathQueues = new();
	private readonly List<float> _enemyMoveBudget = new();
	/// <summary>Invalid sentinel for <see cref="_enemyPursueCells"/>; grid coords are always non-negative.</summary>
	private static readonly Vector2I EnemyPursueNone = new(-1, -1);
	private readonly List<float> _enemyRestSecondsRemaining = new();
	private readonly List<Vector2I> _enemyPursueCells = new();
	private readonly List<Servant> _servants = new();
	private readonly List<Queue<Vector2I>> _servantPathQueues = new();
	private readonly List<float> _servantMoveBudget = new();
	private readonly List<float> _servantRestSecondsRemaining = new();
	private readonly List<Vector2I> _servantPursueCells = new();
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
	/// <summary>Attack-capable colonists pause this long after a strike (no move, no attack).</summary>
	private readonly List<float> _characterRestSecondsRemaining = new();
	private readonly HashSet<Vector2I> _buildingCells = new();
	private readonly List<Rect2I> _buildingFootprints = new();
	private bool _buildingSimEnabled;
	private Timer _buildingGrowthTimer = null!;
	/// <summary>Spawns a soldier from a map building on an interval when civilian count is high enough (see <see cref="OnBuildingRecruitSoldierTimeout"/>).</summary>
	private Timer _buildingRecruitSoldierTimer = null!;
	/// <summary>Spawns an expert from a map building on an interval when soldier count is high enough (see <see cref="OnBuildingRecruitExpertTimeout"/>).</summary>
	private Timer _buildingRecruitExpertTimer = null!;
	/// <summary>Suffix for display names of units spawned from building timers.</summary>
	private int _buildingRecruitNameCounter;
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
	/// <summary>Seconds any attacker (Servant, enemy, colony striker) pauses after a hit.</summary>
	private const float PostAttackStopSec = 1f;
	/// <summary>Min gap (px) between the sim board’s bottom and the top of the ability dock, in the same space as <see cref="_boardGridOrigin"/> (includes room under the 2px board stroke).</summary>
	private const float AbilityBarTopClearancePx = 20f;
	/// <summary>Currency cost for the Servant card (key 1 / first slot). Each use adds a new Servant; existing ones are kept.</summary>
	private const int ServantCardCurrencyCost = 6;
	private const string GameOverScenePath = "res://scenes/game_over.tscn";
	private const string WinGameScenePath = "res://scenes/win_game.tscn";
	private const string ServantKillRoarSfxPath = "res://scenes/music/monster_roars.mp3";
	private const string ServantDeathSfxPath = "res://scenes/music/monsterdeathscream.mp3";
	private const string CivilianMassScreamSfxPath = "res://scenes/music/Massscreams.mp3";
	private const string ColonyStrikerGunshotSfxPath = "res://scenes/music/Gunshots.mp3";
	private const string ServantSummonSfxPath = "res://scenes/music/Summonmonsters.mp3";
	/// <summary>True after lose condition: queue <see cref="GameOverScenePath"/> (see <see cref="TryTriggerGameOver"/>).</summary>
	private bool _gameOverSceneQueued;
	/// <summary>True when victory is queued; blocks game over in the same run.</summary>
	private bool _winGameSceneQueued;
	/// <summary>True while the Servant-card lightning VFX is playing; blocks game over and duplicate card use.</summary>
	private bool _servantLightningActive;
	private Servant? _lightningPendingServant;

	/// <summary>Lowest token cost among ability cards; lose if below this with no living Servant.</summary>
	public static int GetMinAbilityCardTokenCost() => ServantCardCurrencyCost;
	/// <summary>Also win if current tokens exceed this (exclusive: need 51+ when value is 50).</summary>
	private const int PlayerCurrencyWinMinExclusive = 50;
	/// <summary>Recruit a soldier from a building only while living civilian count is strictly greater than this.</summary>
	private const int BuildingRecruitCivilianCountMin = 3;
	/// <summary>Recruit an expert from a building only while living soldier count is strictly greater than this.</summary>
	private const int BuildingRecruitSoldierCountMinForExpert = 3;
	private const float BuildingRecruitSoldierIntervalSec = 5f;
	private const float BuildingRecruitExpertIntervalSec = 10f;
	/// <summary>Tokens (currency) granted when a colonist dies, by roster type.</summary>
	private const int TokenRewardOnCivilianDeath = 1;
	private const int TokenRewardOnExpertDeath = 6;
	private const int TokenRewardOnSoldierDeath = 3;
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

	/// <summary>Pixels per grid cell for drawing and on-screen fit; ≤ <see cref="CellSize"/>, never larger than needed to fit the window.</summary>
	private float _boardPixelsPerCell = 1f;
	private Vector2 _boardGridOrigin;
	private Vector2 _lastLayoutViewportSize = Vector2.Zero;
	/// <summary>True after the first <see cref="_Process"/>; used to avoid skipping the initial <see cref="QueueRedraw"/> before any sim step.</summary>
	private bool _processHasRun;
	private AudioStreamPlayer? _servantKillRoarSfx;
	private AudioStreamPlayer? _servantDeathSfx;
	private AudioStreamPlayer? _civilianMassScreamSfx;
	private AudioStreamPlayer? _colonyStrikerGunshotSfx;
	private AudioStreamPlayer? _servantSummonSfx;

	public override void _Ready()
	{
		_rng.Randomize();
		_statusLabel = GetNodeOrNull<Label>("%StatusLabel");
		_statsLabel = GetNodeOrNull<Label>("%StatsLabel");
		_topPanel = GetNodeOrNull<Control>("UI/TopPanel");
		_abilityBarPanel = GetNodeOrNull<Control>("UI/AbilityBar");
		_abilityCardSlot1Holder = GetNodeOrNull<Control>("UI/AbilityBar/AbilityMargin/AbilityCardRow/AbilityCardHolder1");
		if (_abilityCardSlot1Holder != null)
			_abilityCardSlot1Holder.GuiInput += OnFirstAbilitySlotGuiInput;
		_controlPanel = GetNodeOrNull<GridControlPanel>("%ControlPanel");
		_currencyBar = GetNodeOrNull<ProgressBar>("%CurrencyBar");
		_currencyValueLabel = GetNodeOrNull<Label>("%CurrencyValueLabel");
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

		var roar = ResourceLoader.Load<AudioStream>(ServantKillRoarSfxPath);
		if (roar != null)
		{
			_servantKillRoarSfx = new AudioStreamPlayer
			{
				Stream = roar,
				VolumeDb = -3f,
				Bus = "Master"
			};
			AddChild(_servantKillRoarSfx);
		}

		var deathScream = ResourceLoader.Load<AudioStream>(ServantDeathSfxPath);
		if (deathScream != null)
		{
			_servantDeathSfx = new AudioStreamPlayer
			{
				Stream = deathScream,
				VolumeDb = -2f,
				Bus = "Master"
			};
			AddChild(_servantDeathSfx);
		}

		var massScream = ResourceLoader.Load<AudioStream>(CivilianMassScreamSfxPath);
		if (massScream != null)
		{
			_civilianMassScreamSfx = new AudioStreamPlayer
			{
				Stream = massScream,
				VolumeDb = -1f,
				Bus = "Master"
			};
			AddChild(_civilianMassScreamSfx);
		}

		var gunshots = ResourceLoader.Load<AudioStream>(ColonyStrikerGunshotSfxPath);
		if (gunshots != null)
		{
			_colonyStrikerGunshotSfx = new AudioStreamPlayer
			{
				Stream = gunshots,
				VolumeDb = -2f,
				Bus = "Master"
			};
			AddChild(_colonyStrikerGunshotSfx);
		}

		var summon = ResourceLoader.Load<AudioStream>(ServantSummonSfxPath);
		if (summon != null)
		{
			_servantSummonSfx = new AudioStreamPlayer
			{
				Stream = summon,
				VolumeDb = -1f,
				Bus = "Master"
			};
			AddChild(_servantSummonSfx);
		}

		InitializeStartingCharacters();
		InitializeStartingEnemies();
		(_buildingWidthCells, _buildingHeightCells) = GetFixedBuildingSizeCellsFromCharacter();
		GenerateRandomBuildingsAfterTerrain(2);
		RelocateCharactersToWalkableGround();
		RelocateEnemiesToWalkableGround();
		// Scene starts without Servants; use the ability card to add them.
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		RecalculateCivilianFleeDestinationsFromCurrentEnemies();
		EnsurePathStateSize();
		EnsureEnemyPathStateSize();
		for (var pi = 0; pi < _characters.Count; pi++)
			TryReplanPathWithDestinationFallback(pi);
		for (var pe = 0; pe < _enemies.Count; pe++)
			TryReplanEnemyPathWithDestinationFallback(pe);
		_buildingRecruitSoldierTimer = new Timer
		{
			WaitTime = BuildingRecruitSoldierIntervalSec,
			OneShot = false,
			Autostart = true
		};
		_buildingRecruitSoldierTimer.Timeout += OnBuildingRecruitSoldierTimeout;
		AddChild(_buildingRecruitSoldierTimer);
		_buildingRecruitExpertTimer = new Timer
		{
			WaitTime = BuildingRecruitExpertIntervalSec,
			OneShot = false,
			Autostart = true
		};
		_buildingRecruitExpertTimer.Timeout += OnBuildingRecruitExpertTimeout;
		AddChild(_buildingRecruitExpertTimer);
		_controlPanel?.SetBuildingExpandSize(BuildingGrowthPerTick);
		_controlPanel?.SetTerrainSmoothness(TerrainSmoothness);
		_playerCurrency = Mathf.Clamp(InitialCurrency, PlayerCurrencyMin, PlayerCurrencyMax);
		UpdateBoardLayout();
		_lastLayoutViewportSize = GetViewport().GetVisibleRect().Size;
		UpdateHud();
		QueueRedraw();
	}

	public override void _ExitTree()
	{
		if (_abilityCardSlot1Holder != null)
			_abilityCardSlot1Holder.GuiInput -= OnFirstAbilitySlotGuiInput;
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
			return;
		}

		// First ability slot: spend currency and spawn an additional Servant.
		if (key.Keycode is Key.Key1 or Key.Kp1)
		{
			if (TryUseServantCard())
				GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>Pay <see cref="ServantCardCurrencyCost"/>, play lightning at the spawn cell, then add the Servant when the strike finishes.</summary>
	private bool TryUseServantCard()
	{
		if (_servantLightningActive)
			return false;
		if (_playerCurrency < ServantCardCurrencyCost)
			return false;
		if (!TryGetNextServantSpawn(out var proto) || proto == null)
			return false;
		PlayerCurrency = _playerCurrency - ServantCardCurrencyCost;
		_servantLightningActive = true;
		StartServantLightning(proto);
		UpdateHud();
		QueueRedraw();
		return true;
	}

	/// <summary>Center of the sim cell in this node’s local pixels (matches <see cref="_Draw"/>).</summary>
	public Vector2 GetCellCenterBoardPx(Vector2I cell)
	{
		var s = _boardPixelsPerCell;
		return _boardGridOrigin + new Vector2((cell.X + 0.5f) * s, (cell.Y + 0.5f) * s);
	}

	/// <summary>Top edge of the terrain board in local pixels (for lightning origin).</summary>
	public float GetBoardLayoutTopY() => _boardGridOrigin.Y;

	private void StartServantLightning(Servant proto)
	{
		_lightningPendingServant = proto;
		var vfx = new LightningStrikeVfx();
		vfx.PlaybackComplete += OnServantLightningPlaybackComplete;
		AddChild(vfx);
		vfx.Begin(this, proto.Cell);
	}

	private void OnServantLightningPlaybackComplete()
	{
		if (_lightningPendingServant != null)
		{
			CommitServantFromLightning(_lightningPendingServant);
			_lightningPendingServant = null;
		}
		_servantLightningActive = false;
		UpdateHud();
		QueueRedraw();
	}

	private bool TryGetNextServantSpawn(out Servant? proto)
	{
		proto = null;
		var newIndex = _servants.Count;
		var taken = new HashSet<Vector2I>();
		for (var j = 0; j < _servants.Count; j++)
			taken.Add(_servants[j].Cell);
		var start = FindRandomValidDestinationCell(null, taken);
		var p = Servant.CreateAt(start);
		var cell = FindNearestCellSatisfying(start, a =>
			IsWalkableForServantAnchorCell(p, a) && !IsCellOccupiedByOtherServant(a, newIndex));
		p.Cell = cell;
		proto = p;
		return true;
	}

	private void CommitServantFromLightning(Servant proto)
	{
		_servantSummonSfx?.Play();
		_servants.Add(proto);
		_servantPathQueues.Add(new Queue<Vector2I>());
		_servantMoveBudget.Add(0f);
		_servantRestSecondsRemaining.Add(0f);
		_servantPursueCells.Add(EnemyPursueNone);
	}

	private void OnFirstAbilitySlotGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;
		if (TryUseServantCard())
			_abilityCardSlot1Holder?.AcceptEvent();
	}

	public override void _Process(double delta)
	{
		var d = (float)delta;
		EnsureServantStateSize();
		for (var si = 0; si < _servantRestSecondsRemaining.Count; si++)
			_servantRestSecondsRemaining[si] = Mathf.Max(0f, _servantRestSecondsRemaining[si] - d);
		EnsureEnemyPathStateSize();
		for (var ei = 0; ei < _enemies.Count; ei++)
			_enemyRestSecondsRemaining[ei] = Mathf.Max(0f, _enemyRestSecondsRemaining[ei] - d);
		EnsurePathStateSize();
		for (var ci = 0; ci < _characterRestSecondsRemaining.Count; ci++)
			_characterRestSecondsRemaining[ci] = Mathf.Max(0f, _characterRestSecondsRemaining[ci] - d);

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
			TryTriggerWin();
			TryTriggerGameOver();
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
		TryTriggerWin();
		TryTriggerGameOver();
	}

	/// <summary>Victory: no living civilian remains, or current tokens are greater than <see cref="PlayerCurrencyWinMinExclusive"/>.</summary>
	private void TryTriggerWin()
	{
		if (_winGameSceneQueued || _gameOverSceneQueued)
			return;
		if (_playerCurrency > PlayerCurrencyWinMinExclusive)
		{
			StopBuildingRecruitTimers();
			_winGameSceneQueued = true;
			SetProcess(false);
			CallDeferred(nameof(DeferredChangeToWin));
			return;
		}
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.Type == ColonyCharacterType.Civilian && c.Health > 0)
				return;
		}
		StopBuildingRecruitTimers();
		_winGameSceneQueued = true;
		SetProcess(false);
		CallDeferred(nameof(DeferredChangeToWin));
	}

	private void DeferredChangeToWin()
	{
		var err = GetTree().ChangeSceneToFile(WinGameScenePath);
		if (err != Error.Ok)
			GD.PrintErr("Win game scene failed: ", err);
	}

	/// <summary>Transition when tokens cannot afford the cheapest card and there is no living Servant.</summary>
	private void TryTriggerGameOver()
	{
		if (_gameOverSceneQueued || _winGameSceneQueued)
			return;
		if (_servantLightningActive)
			return;
		if (_playerCurrency >= GetMinAbilityCardTokenCost())
			return;
		for (var i = 0; i < _servants.Count; i++)
		{
			if (_servants[i].Health > 0)
				return;
		}
		StopBuildingRecruitTimers();
		_gameOverSceneQueued = true;
		SetProcess(false);
		CallDeferred(nameof(DeferredChangeToGameOver));
	}

	private void DeferredChangeToGameOver()
	{
		var err = GetTree().ChangeSceneToFile(GameOverScenePath);
		if (err != Error.Ok)
			GD.PrintErr("Game over scene failed: ", err);
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
			for (var si = 0; si < _servants.Count; si++)
			{
				var sv = _servants[si];
				DrawServant(sv, origin, drawDead: sv.Health <= 0);
			}
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
		var d = ClampToGridBounds(destination);
		var markerTopLeft = gridOrigin + new Vector2(d.X * s, d.Y * s);
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
		// _Draw() uses this node's local space. Top of the ability bar in local coords matches the bottom limit for the sim board when the Game node is the default (0,0) canvas.
		var bottomOfPlayArea = v.Y - bottomPadding;
		if (_abilityBarPanel != null)
		{
			var barTopGlobal = _abilityBarPanel.GetGlobalRect().Position;
			bottomOfPlayArea = ToLocal(barTopGlobal).Y - AbilityBarTopClearancePx;
		}
		var availableHeight = Mathf.Max(0f, bottomOfPlayArea - topBound);
		var fitW = availableWidth / Mathf.Max(1, GridWidth);
		var fitH = availableHeight / Mathf.Max(1, GridHeight);
		_boardPixelsPerCell = Mathf.Max(1f, Mathf.Min((float)CellSize, Mathf.Min(fitW, fitH)));
		var s = _boardPixelsPerCell;
		var boardWidth = GridWidth * s;
		var boardHeight = GridHeight * s;
		var x = Mathf.Floor(leftPadding + (availableWidth - boardWidth) * 0.5f);
		x = Mathf.Max(leftPadding, x);
		var y = Mathf.Floor(topBound + (availableHeight - boardHeight) * 0.5f);
		var maxY = bottomOfPlayArea - boardHeight;
		y = Mathf.Min(y, maxY);
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
		UpdateCurrencyUi();
	}

	private void UpdateCurrencyUi()
	{
		if (_currencyBar != null)
		{
			_currencyBar.MinValue = PlayerCurrencyMin;
			_currencyBar.MaxValue = PlayerCurrencyMax;
			_currencyBar.Value = _playerCurrency;
		}
		if (_currencyValueLabel != null)
			_currencyValueLabel.Text = $"{_playerCurrency} / {PlayerCurrencyMax}";
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
		RelocateAllServantsToWalkable();
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
		RelocateAllServantsToWalkable();
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
		RelocateAllServantsToWalkable();
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

		// Roster is complete: civilians get unique flee goals in roster order; others keep random dest.
		AssignUniqueCivilianFleeDestinationsInOrder();
		for (var j = 0; j < _characters.Count; j++)
		{
			var c = _characters[j];
			if (c.Type != ColonyCharacterType.Civilian)
				c.Destination = FindRandomValidDestinationCell(c.Cell);
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

	private void EnsureServantStateSize()
	{
		while (_servantPathQueues.Count < _servants.Count)
		{
			_servantPathQueues.Add(new Queue<Vector2I>());
			_servantMoveBudget.Add(0f);
			_servantRestSecondsRemaining.Add(0f);
			_servantPursueCells.Add(EnemyPursueNone);
		}
	}

	private static bool IsCellOccupiedByOtherServant(Vector2I cell, IReadOnlyList<Servant> servants, int exceptIndex)
	{
		for (var j = 0; j < servants.Count; j++)
		{
			if (j == exceptIndex)
				continue;
			if (servants[j].Cell == cell)
				return true;
		}
		return false;
	}

	private bool IsCellOccupiedByOtherServant(Vector2I cell, int exceptIndex) =>
		IsCellOccupiedByOtherServant(cell, _servants, exceptIndex);

	/// <summary>After terrain changes, keep every Servant on a valid, non-overlapping anchor.</summary>
	private void RelocateAllServantsToWalkable()
	{
		EnsureServantStateSize();
		for (var i = 0; i < _servants.Count; i++)
		{
			var s = _servants[i];
			if (IsValidServantAnchorForIndex(i, s.Cell))
				continue;
			s.Cell = FindNearestCellSatisfying(s.Cell, a => IsValidServantAnchorForIndex(i, a));
		}
	}

	private bool IsValidServantAnchorForIndex(int i, Vector2I a) =>
		IsWalkableForServantAnchorCell(_servants[i], a) && !IsCellOccupiedByOtherServant(a, i);

	private void OnCharacterDied(int index)
	{
		var c = _characters[index];
		var wasCivilian = c.Type == ColonyCharacterType.Civilian;
		GrantTokensForColonistDeath(c.Type);
		c.Destination = ClampToGridBounds(c.Cell);
		_characterPathQueues[index].Clear();
		_characterMoveBudget[index] = 0f;
		if (index < _characterRestSecondsRemaining.Count)
			_characterRestSecondsRemaining[index] = 0f;
		if (wasCivilian)
		{
			_civilianMassScreamSfx?.Play();
			AssignUniqueCivilianFleeDestinationsInOrder();
			for (var i = 0; i < _characters.Count; i++)
			{
				if (!_characters[i].IsAlive || _characters[i].Type != ColonyCharacterType.Civilian)
					continue;
				TryReplanPathWithDestinationFallback(i);
			}
		}
	}

	/// <summary>Top-bar currency: each colonist death pays tokens (civilian 1, expert 6, soldier 3), clamped to <see cref="PlayerCurrencyMax"/>.</summary>
	private void GrantTokensForColonistDeath(ColonyCharacterType type)
	{
		var n = type switch
		{
			ColonyCharacterType.Civilian => TokenRewardOnCivilianDeath,
			ColonyCharacterType.Expert => TokenRewardOnExpertDeath,
			ColonyCharacterType.Soldier => TokenRewardOnSoldierDeath,
			_ => 0
		};
		if (n <= 0)
			return;
		PlayerCurrency = _playerCurrency + n;
	}

	private void OnEnemyDied(int index)
	{
		var e = _enemies[index];
		e.Destination = ClampToGridBounds(e.Cell);
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
				character.Destination = ClampToGridBounds(character.Cell);
				continue;
			}
			if (character.Type == ColonyCharacterType.Civilian)
				continue;

			var d = ClampToGridBounds(character.Destination);
			if (IsValidDestinationCell(d))
				character.Destination = d;
			else
				character.Destination = FindRandomValidDestinationCell(character.Cell);
		}

		if (AnyCivilianInvalidDestination() || HasDuplicateCivilianDestinations())
			AssignUniqueCivilianFleeDestinationsInOrder();

		for (var i = 0; i < _characters.Count; i++)
		{
			if (_characters[i].Health <= 0)
				continue;
			_characters[i].Destination = ClampToGridBounds(_characters[i].Destination);
		}
	}

	private bool AnyCivilianInvalidDestination()
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.Type != ColonyCharacterType.Civilian || c.Health <= 0)
				continue;
			if (!IsValidDestinationCell(ClampToGridBounds(c.Destination)))
				return true;
		}
		return false;
	}

	private bool HasDuplicateCivilianDestinations()
	{
		var seen = new HashSet<Vector2I>();
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.Type != ColonyCharacterType.Civilian || c.Health <= 0)
				continue;
			if (!seen.Add(ClampToGridBounds(c.Destination)))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Every living civilian gets a new goal in roster order; each goal is distinct from earlier civilians in this pass.
	/// </summary>
	private void AssignUniqueCivilianFleeDestinationsInOrder()
	{
		var taken = new HashSet<Vector2I>();
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.Type != ColonyCharacterType.Civilian || c.Health <= 0)
				continue;
			c.Destination = FindCivilianFleeDestination(c, c.Cell, taken);
			var d = ClampToGridBounds(c.Destination);
			if (taken.Contains(d) || !IsValidDestinationCell(d))
				c.Destination = FindFirstWalkableDestinationExcluding(taken, c.Cell);
			d = ClampToGridBounds(c.Destination);
			taken.Add(d);
		}
	}

	private void EnsureEnemyDestinationsAreValid()
	{
		for (var i = 0; i < _enemies.Count; i++)
		{
			var e = _enemies[i];
			if (e.Health <= 0)
			{
				e.Destination = ClampToGridBounds(e.Cell);
				continue;
			}
			var d = ClampToGridBounds(e.Destination);
			if (IsValidDestinationCell(d))
				e.Destination = d;
			else
				e.Destination = FindRandomValidDestinationCell(e.Cell);
		}
	}

	/// <summary>Snaps a cell to sim grid indices [0, <see cref="GridWidth"/>-1] × [0, <see cref="GridHeight"/>-1] so path goals and UI markers never leave the board.</summary>
	private Vector2I ClampToGridBounds(Vector2I p)
	{
		var maxX = Mathf.Max(0, GridWidth - 1);
		var maxY = Mathf.Max(0, GridHeight - 1);
		return new Vector2I(Mathf.Clamp(p.X, 0, maxX), Mathf.Clamp(p.Y, 0, maxY));
	}

	private bool IsValidDestinationCell(Vector2I cell) =>
		IsWalkableCharacterCell(cell);

	/// <summary>Row-major first walkable cell that is not in <paramref name="excluded"/>, and not the optional <paramref name="avoidCell"/>.</summary>
	private Vector2I FindFirstWalkableDestinationExcluding(ISet<Vector2I> excluded, Vector2I? avoidCell)
	{
		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				var cell = new Vector2I(x, y);
				if (!IsValidDestinationCell(cell))
					continue;
				if (excluded.Contains(cell))
					continue;
				if (avoidCell.HasValue && cell == avoidCell.Value)
					continue;
				return cell;
			}
		}
		return ClampToGridBounds(FindNearestWalkableCell(avoidCell ?? new Vector2I(GridWidth / 2, GridHeight / 2)));
	}

	/// <summary>
	/// 4-way BFS from a clamped start; first cell matching <paramref name="isValidAnchor"/> (default single-cell <see cref="IsWalkableCharacterCell"/> = land, no building).
	/// </summary>
	private Vector2I FindNearestCellSatisfying(Vector2I start, Func<Vector2I, bool> isValidAnchor)
	{
		var clampedStart = ClampToGridBounds(start);
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

	private Vector2I FindRandomValidDestinationCell(Vector2I? avoidCell = null, ISet<Vector2I>? alsoAvoidDestinations = null)
	{
		if (GridWidth < 1 || GridHeight < 1)
			return new Vector2I(0, 0);
		var maxX = GridWidth - 1;
		var maxY = GridHeight - 1;
		var maxAttempts = 2000;
		for (var i = 0; i < maxAttempts; i++)
		{
			var x = _rng.RandiRange(0, maxX);
			var y = _rng.RandiRange(0, maxY);
			var c = new Vector2I(x, y);
			if (!IsValidDestinationCell(c))
				continue;
			if (avoidCell.HasValue && c == avoidCell.Value)
				continue;
			if (alsoAvoidDestinations != null && alsoAvoidDestinations.Contains(c))
				continue;
			return c;
		}

		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				var c = new Vector2I(x, y);
				if (!IsValidDestinationCell(c))
					continue;
				if (avoidCell.HasValue && c == avoidCell.Value)
					continue;
				if (alsoAvoidDestinations != null && alsoAvoidDestinations.Contains(c))
					continue;
				return c;
			}
		}

		if (alsoAvoidDestinations != null && alsoAvoidDestinations.Count > 0)
		{
			var unblocked = FindFirstWalkableDestinationExcluding(alsoAvoidDestinations, avoidCell);
			if (IsValidDestinationCell(unblocked) && !alsoAvoidDestinations.Contains(unblocked))
				return unblocked;
			return FindRandomValidDestinationCell(avoidCell, null);
		}
		return ClampToGridBounds(FindNearestWalkableCell(new Vector2I(GridWidth / 2, GridHeight / 2)));
	}

	private int GetPathMoveCost(Vector2I cell) =>
		_terrain[cell.X, cell.Y] == TerrainType.Forest ? ForestTerrainMoveCost : DefaultTerrainMoveCost;

	private void EnsurePathStateSize()
	{
		while (_characterPathQueues.Count < _characters.Count)
		{
			_characterPathQueues.Add(new Queue<Vector2I>());
			_characterMoveBudget.Add(0f);
			_characterRestSecondsRemaining.Add(0f);
		}

		while (_characterPathQueues.Count > _characters.Count)
		{
			_characterPathQueues.RemoveAt(_characterPathQueues.Count - 1);
			_characterMoveBudget.RemoveAt(_characterMoveBudget.Count - 1);
			_characterRestSecondsRemaining.RemoveAt(_characterRestSecondsRemaining.Count - 1);
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
		character.Destination = ClampToGridBounds(character.Destination);
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
		e.Destination = ClampToGridBounds(e.Destination);
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
		EnsureCharacterDestinationsAreValid();
		EnsureEnemyDestinationsAreValid();
		for (var i = 0; i < _characters.Count; i++)
		{
			var character = _characters[i];
			var pathQueue = _characterPathQueues[i];
			if (character.Health <= 0)
				continue;
			if (_characterRestSecondsRemaining[i] > 0f)
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

			e.Destination = ClampToGridBounds(chase!.Cell);
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
				_enemyRestSecondsRemaining[ei] = PostAttackStopSec;
		}
	}

	/// <summary>Colony strikers hit <see cref="EnemyCharacter"/> and (if in range) the <see cref="Servant"/>; they do not damage other colonists.</summary>
	private void StepColonyCharacterCombat()
	{
		EnsurePathStateSize();
		for (var ai = 0; ai < _characters.Count; ai++)
		{
			var a = _characters[ai];
			if (!a.IsAlive || !a.CanAttack)
				continue;
			if (_characterRestSecondsRemaining[ai] > 0f)
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
			for (var si = 0; si < _servants.Count; si++)
			{
				var sv = _servants[si];
				if (sv.Health <= 0)
					continue;
				if (ColonyStrikerCanDamageServant(a) && ChebyshevDistance(a.Cell, sv.Cell) <= rFine)
					pool.Add((ManhattanDistance(a.Cell, sv.Cell), ColonyAttackPoolKind.Servant, si));
			}
			pool.Sort((x, y) => x.manh.CompareTo(y.manh));
			var hits = 0;
			for (var p = 0; p < pool.Count && hits < a.MaxAttackTargets; p++)
			{
				var item = pool[p];
				switch (item.kind)
				{
					case ColonyAttackPoolKind.Servant:
					{
						if (item.index < 0 || item.index >= _servants.Count)
							continue;
						var sv = _servants[item.index];
						if (sv.Health <= 0)
							continue;
						var th = sv.Health;
						sv.Health = Mathf.Max(0, sv.Health - a.Attack);
						if (th > 0 && sv.Health <= 0)
							_servantDeathSfx?.Play();
						hits++;
						break;
					}
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
			if (hits > 0)
			{
				if (a.Type is ColonyCharacterType.Expert or ColonyCharacterType.Soldier)
					_colonyStrikerGunshotSfx?.Play();
				_characterRestSecondsRemaining[ai] = PostAttackStopSec;
			}
		}
	}

	/// <summary>Only attack-capable colonists (Expert, Soldier) can damage the Servant at range.</summary>
	private static bool ColonyStrikerCanDamageServant(ColonyCharacter a) => a.CanAttack;

	/// <summary>Each living Servant chases the closest colonist; melee on the fine grid’s 3×3 ring at design r=1.</summary>
	private void StepServant()
	{
		EnsureServantStateSize();
		for (var si = 0; si < _servants.Count; si++)
		{
			var servant = _servants[si];
			var pathQueue = _servantPathQueues[si];
			if (servant.Health <= 0)
			{
				pathQueue.Clear();
				continue;
			}

			if (_servantRestSecondsRemaining[si] > 0f)
				continue;

			if (!TryGetServantChaseTarget(servant, out var target, out var targetIndex))
			{
				pathQueue.Clear();
				continue;
			}

			var servantMeleeRFine = GetCombatChebyshevRadiusInFineCells(1);
			if (ChebyshevDistance(servant.Cell, target!.Cell) <= servantMeleeRFine)
			{
				pathQueue.Clear();
				if (target.IsAlive)
				{
					var h = target.Health;
					target.Health = Mathf.Max(0, h - Servant.AttackDamage);
					if (h > 0 && !target.IsAlive)
					{
						OnCharacterDied(targetIndex);
						_servantKillRoarSfx?.Play();
					}
					_servantRestSecondsRemaining[si] = PostAttackStopSec;
				}
				continue;
			}

			if (pathQueue.Count == 0 || _servantPursueCells[si] != target.Cell)
			{
				_servantPursueCells[si] = target.Cell;
				ReplanServantPath(si, target.Cell);
			}

			if (pathQueue.Count == 0)
				continue;

			_servantMoveBudget[si] += MovementBudgetPerGridMajorStep * (Servant.MajorStepsPerSecond / 2f);
			var moved = false;
			var servantConsumeIters = 0;
			while (_servantMoveBudget[si] > 0f && servantConsumeIters < MaxPathConsumeIterationsPerUnit)
			{
				servantConsumeIters++;
				if (pathQueue.Count == 0)
				{
					if (!ReplanServantPath(si, target.Cell))
						break;
					if (pathQueue.Count == 0)
						break;
				}

				var next = pathQueue.Peek();
				var d = Mathf.Abs(next.X - servant.Cell.X) + Mathf.Abs(next.Y - servant.Cell.Y);
				if (d != 1)
				{
					ReplanServantPath(si, target.Cell);
					if (pathQueue.Count == 0)
						break;
					continue;
				}

				if (!IsWalkableForServantPathCell(si, next))
				{
					ReplanServantPath(si, target.Cell);
					if (pathQueue.Count == 0)
						break;
					continue;
				}

				var cost = (float)GetPathMoveCost(next);
				if (cost <= 0f)
					break;
				if (_servantMoveBudget[si] < cost)
					break;

				_servantMoveBudget[si] -= cost;
				pathQueue.Dequeue();
				servant.Cell = next;
				moved = true;
			}

			if (moved && servant.Cell == target.Cell)
				pathQueue.Clear();
		}
	}

	private bool ReplanServantPath(int servantIndex, Vector2I destination)
	{
		if (servantIndex < 0 || servantIndex >= _servants.Count)
			return false;
		var pathQueue = _servantPathQueues[servantIndex];
		var s = _servants[servantIndex];
		pathQueue.Clear();
		_servantMoveBudget[servantIndex] = 0f;

		if (s.Cell == destination)
			return true;

		var fullPath = GridAStar.FindPath(
			GridWidth,
			GridHeight,
			s.Cell,
			destination,
			c => IsWalkableForServantPathCell(servantIndex, c),
			GetPathMoveCost
		);
		if (fullPath == null)
			return false;

		for (var n = 1; n < fullPath.Count; n++)
			pathQueue.Enqueue(fullPath[n]);

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

	/// <summary>Nearest living colonist to this Servant by anchor Manhattan; ties favor lower list index.</summary>
	private static bool TryGetNearestLivingColonistForServantChase(
		Servant s, IReadOnlyList<ColonyCharacter> characters, out ColonyCharacter? target, out int index)
	{
		target = null;
		index = -1;
		ColonyCharacter? best = null;
		var bestD = int.MaxValue;
		for (var i = 0; i < characters.Count; i++)
		{
			var c = characters[i];
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

	private bool TryGetServantChaseTarget(Servant s, out ColonyCharacter? target, out int index) =>
		TryGetNearestLivingColonistForServantChase(s, _characters, out target, out index);

	private Vector2I FindNewDestinationAfterArrival(ColonyCharacter c) =>
		c.Type == ColonyCharacterType.Civilian
			? FindCivilianFleeDestination(c, c.Cell, BuildReservedCivilianDestinationsExcluding(c))
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

	/// <summary>
	/// Whether the Servant can stand on <paramref name="anchorCell"/> for pathfinding and sim steps. Uses the same
	/// O(1) test as other units. (A previous version iterated the full pixel-scaled token AABB in grid cells, which
	/// is hundreds to thousands of cells per check when the board is zoomed out — and A* calls that per open-set
	/// neighbor, which froze the game.) The Cthulhu sprite may extend visually over adjacent cells; collision is
	/// logical single-cell, like other tokens.
	/// </summary>
	private bool IsWalkableForServantAnchorCell(Servant _, Vector2I anchorCell)
	{
		if (anchorCell.X < 0 || anchorCell.Y < 0 || anchorCell.X >= GridWidth || anchorCell.Y >= GridHeight)
			return false;
		return IsWalkableCharacterCell(anchorCell);
	}

	/// <summary>Walkable terrain, and the cell is not the anchor of another Servant (other than <paramref name="servantIndex"/>).</summary>
	private bool IsWalkableForServantPathCell(int servantIndex, Vector2I cell)
	{
		if (servantIndex < 0 || servantIndex >= _servants.Count)
			return false;
		if (!IsWalkableForServantAnchorCell(_servants[servantIndex], cell))
			return false;
		return !IsCellOccupiedByOtherServant(cell, servantIndex);
	}

	/// <summary>Attack-capable units (and living Servants) are treated as threats for flee targeting.</summary>
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
		for (var si = 0; si < _servants.Count; si++)
		{
			if (_servants[si].Health > 0)
				list.Add(_servants[si].Cell);
		}
		for (var e = 0; e < _enemies.Count; e++)
		{
			var en = _enemies[e];
			if (en.CanAttack && en.Health > 0)
				list.Add(en.Cell);
		}
		return list;
	}

	/// <summary>Valid, non-water cell that maximizes distance to the nearest current enemy (Manhattan). No enemies → random valid. <paramref name="reservedCivilianDestinations"/> excludes goals already taken by other living civilians when non-null.</summary>
	private Vector2I FindCivilianFleeDestination(
		ColonyCharacter self,
		Vector2I? avoidCell = null,
		ISet<Vector2I>? reservedCivilianDestinations = null
	)
	{
		var enemies = GetEnemyCellsForCivilian(self);
		if (enemies.Count == 0)
		{
			if (reservedCivilianDestinations != null && reservedCivilianDestinations.Count > 0)
			{
				var pick = FindFirstWalkableDestinationExcluding(reservedCivilianDestinations, avoidCell ?? self.Cell);
				if (IsValidDestinationCell(pick) && !reservedCivilianDestinations.Contains(pick))
					return pick;
			}
			return FindRandomValidDestinationCell(avoidCell ?? self.Cell, reservedCivilianDestinations);
		}

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
				if (reservedCivilianDestinations != null && reservedCivilianDestinations.Contains(cell))
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
		{
			var ex = reservedCivilianDestinations ?? new HashSet<Vector2I>();
			return FindFirstWalkableDestinationExcluding(ex, avoidCell);
		}
		if (reservedCivilianDestinations != null && reservedCivilianDestinations.Contains(best))
			return FindFirstWalkableDestinationExcluding(reservedCivilianDestinations, avoidCell);
		return ClampToGridBounds(best);
	}

	/// <summary>Current destination goals of all other living civilians, for choosing a new goal that does not collide with peers.</summary>
	private HashSet<Vector2I> BuildReservedCivilianDestinationsExcluding(ColonyCharacter self)
	{
		var set = new HashSet<Vector2I>();
		for (var i = 0; i < _characters.Count; i++)
		{
			var o = _characters[i];
			if (o.Type != ColonyCharacterType.Civilian || o.Health <= 0)
				continue;
			if (o.Id == self.Id)
				continue;
			set.Add(ClampToGridBounds(o.Destination));
		}
		return set;
	}

	private Vector2I GetDestinationForPathFallback(ColonyCharacter c) =>
		c.Type == ColonyCharacterType.Civilian
			? FindCivilianFleeDestination(c, c.Cell, BuildReservedCivilianDestinationsExcluding(c))
			: FindRandomValidDestinationCell(c.Cell);

	private void RecalculateCivilianFleeDestinationsFromCurrentEnemies() =>
		AssignUniqueCivilianFleeDestinationsInOrder();

	private void StopBuildingRecruitTimers()
	{
		_buildingRecruitSoldierTimer.Stop();
		_buildingRecruitExpertTimer.Stop();
	}

	private void OnBuildingRecruitSoldierTimeout()
	{
		if (_winGameSceneQueued || _gameOverSceneQueued)
			return;
		if (CountLivingColonyType(ColonyCharacterType.Civilian) <= BuildingRecruitCivilianCountMin)
			return;
		TrySpawnFromRandomBuilding(ColonyCharacterType.Soldier, "Recruit Soldier");
	}

	private void OnBuildingRecruitExpertTimeout()
	{
		if (_winGameSceneQueued || _gameOverSceneQueued)
			return;
		if (CountLivingColonyType(ColonyCharacterType.Soldier) <= BuildingRecruitSoldierCountMinForExpert)
			return;
		TrySpawnFromRandomBuilding(ColonyCharacterType.Expert, "Hired Expert");
	}

	private int CountLivingColonyType(ColonyCharacterType type)
	{
		var n = 0;
		for (var i = 0; i < _characters.Count; i++)
		{
			var c = _characters[i];
			if (c.IsAlive && c.Type == type)
				n++;
		}
		return n;
	}

	/// <summary>True if a living colonist, enemy, or servant occupies this single-cell anchor.</summary>
	private bool IsCellOccupiedByAnyGroundUnit(Vector2I cell)
	{
		for (var i = 0; i < _characters.Count; i++)
		{
			if (_characters[i].IsAlive && _characters[i].Cell == cell)
				return true;
		}
		for (var i = 0; i < _enemies.Count; i++)
		{
			if (_enemies[i].IsAlive && _enemies[i].Cell == cell)
				return true;
		}
		for (var i = 0; i < _servants.Count; i++)
		{
			if (_servants[i].Health > 0 && _servants[i].Cell == cell)
				return true;
		}
		return false;
	}

	private void TrySpawnFromRandomBuilding(ColonyCharacterType type, string namePrefix)
	{
		if (_buildingFootprints.Count == 0)
			return;
		var iFoot = _rng.RandiRange(0, _buildingFootprints.Count - 1);
		var fp = _buildingFootprints[iFoot];
		var start = fp.Position + new Vector2I(fp.Size.X / 2, fp.Size.Y / 2);
		var cell = FindNearestCellSatisfying(
			start,
			c => IsWalkableCharacterCell(c) && !IsCellOccupiedByAnyGroundUnit(c));
		if (!IsWalkableCharacterCell(cell) || IsCellOccupiedByAnyGroundUnit(cell))
			return;

		_buildingRecruitNameCounter++;
		var c = ColonyCharacter.CreateByType(type, cell, $"{namePrefix} {_buildingRecruitNameCounter}", null);
		_characters.Add(c);
		EnsurePathStateSize();
		var newIndex = _characters.Count - 1;
		_characters[newIndex].Destination = FindRandomValidDestinationCell(_characters[newIndex].Cell);
		TryReplanPathWithDestinationFallback(newIndex);
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
