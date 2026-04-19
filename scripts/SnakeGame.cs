using Godot;
using System.Collections.Generic;

public partial class SnakeGame : Node2D
{
	[Export]
	public int CellSize { get; set; } = 32;

	[Export]
	public int GridWidth { get; set; } = 40;

	[Export]
	public int GridHeight { get; set; } = 22;

	[Export]
	public double MoveIntervalSec { get; set; } = 0.125;

	private readonly List<Vector2I> _snake = new();
	private Vector2I _direction = new(1, 0);
	private Vector2I _queuedDirection = new(1, 0);
	private Vector2I _apple;
	private bool _gameOver;
	private int _score;

	private Timer _timer = null!;
	private Label _scoreLabel = null!;
	private Label _gameOverLabel = null!;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Randomize();
		_timer = GetNode<Timer>("%MoveTimer");
		_scoreLabel = GetNode<Label>("%ScoreLabel");
		_gameOverLabel = GetNode<Label>("%GameOverLabel");
		_timer.WaitTime = MoveIntervalSec;
		_timer.Timeout += OnMoveTick;
		ResetGame();
	}

	public override void _Draw()
	{
		var origin = GetGridPixelOrigin();
		var gridPx = new Vector2(GridWidth * CellSize, GridHeight * CellSize);
		DrawRect(new Rect2(origin, gridPx), new Color(0.06f, 0.09f, 0.07f));
		DrawRect(new Rect2(origin, gridPx), new Color(0.15f, 0.2f, 0.14f), false, 2f);

		var inset = new Vector2(1.5f, 1.5f);
		var cellInner = new Vector2(CellSize - 3f, CellSize - 3f);

		for (var i = 0; i < _snake.Count; i++)
		{
			var cell = _snake[i];
			var pos = origin + new Vector2(cell.X * CellSize, cell.Y * CellSize) + inset;
			var color = i == 0 ? new Color(0.45f, 0.95f, 0.45f) : new Color(0.25f, 0.75f, 0.35f);
			DrawRect(new Rect2(pos, cellInner), color);
		}

		var applePos = origin + new Vector2(_apple.X * CellSize, _apple.Y * CellSize) + inset;
		DrawRect(new Rect2(applePos, cellInner), new Color(0.95f, 0.2f, 0.2f));
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (_gameOver && key.Keycode == Key.R)
		{
			ResetGame();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_gameOver)
			return;

		var dir = KeycodeToDirection(key.Keycode);
		if (dir.HasValue)
		{
			TryQueueDirection(dir.Value);
			GetViewport().SetInputAsHandled();
		}
	}

	private static Vector2I? KeycodeToDirection(Key key)
	{
		return key switch
		{
			Key.Up => new Vector2I(0, -1),
			Key.Down => new Vector2I(0, 1),
			Key.Left => new Vector2I(-1, 0),
			Key.Right => new Vector2I(1, 0),
			_ => null,
		};
	}

	private void TryQueueDirection(Vector2I dir)
	{
		if (dir.X + _direction.X == 0 && dir.Y + _direction.Y == 0)
			return;
		_queuedDirection = dir;
	}

	private void OnMoveTick()
	{
		if (_gameOver)
			return;

		_direction = _queuedDirection;
		var head = _snake[0];
		var newHead = head + _direction;

		if (newHead.X < 0 || newHead.Y < 0 || newHead.X >= GridWidth || newHead.Y >= GridHeight)
		{
			Fail();
			return;
		}

		var eating = newHead == _apple;
		if (HitsSelfInstance(newHead, eating))
		{
			Fail();
			return;
		}

		_snake.Insert(0, newHead);
		if (eating)
		{
			_score++;
			SpawnApple();
		}
		else
		{
			_snake.RemoveAt(_snake.Count - 1);
		}

		UpdateHud();
		QueueRedraw();
	}

	private bool HitsSelfInstance(Vector2I newHead, bool willEatApple)
	{
		for (var i = 0; i < _snake.Count; i++)
		{
			if (_snake[i] != newHead)
				continue;
			if (!willEatApple && i == _snake.Count - 1)
				return false;
			return true;
		}

		return false;
	}

	private void Fail()
	{
		_gameOver = true;
		_timer.Stop();
		_gameOverLabel.Text = $"Game over — Score {_score}. Press R to restart.";
		_gameOverLabel.Visible = true;
	}

	private void ResetGame()
	{
		_gameOver = false;
		_score = 0;
		_snake.Clear();

		var mid = new Vector2I(GridWidth / 2, GridHeight / 2);
		_snake.Add(mid);
		_snake.Add(mid + new Vector2I(-1, 0));
		_snake.Add(mid + new Vector2I(-2, 0));
		_direction = new Vector2I(1, 0);
		_queuedDirection = _direction;

		SpawnApple();
		_gameOverLabel.Visible = false;
		UpdateHud();
		QueueRedraw();

		_timer.WaitTime = MoveIntervalSec;
		if (!_timer.IsStopped())
			_timer.Stop();
		_timer.Start();
	}

	private void SpawnApple()
	{
		var occupied = new HashSet<Vector2I>(_snake);
		for (var attempt = 0; attempt < 4000; attempt++)
		{
			var c = new Vector2I(_rng.RandiRange(0, GridWidth - 1), _rng.RandiRange(0, GridHeight - 1));
			if (!occupied.Contains(c))
			{
				_apple = c;
				return;
			}
		}
	}

	private void UpdateHud()
	{
		_scoreLabel.Text = $"Score: {_score}";
	}

	private Vector2 GetGridPixelOrigin()
	{
		var vs = GetViewportRect().Size;
		var gw = GridWidth * CellSize;
		var gh = GridHeight * CellSize;
		return new Vector2((vs.X - gw) * 0.5f, (vs.Y - gh) * 0.5f);
	}
}
