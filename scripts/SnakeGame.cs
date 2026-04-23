using Godot;
using System.Collections.Generic;

public partial class SnakeGame : Node2D
{
	[Export]
	public int CellSize { get; set; } = 28;

	[Export]
	public int GridWidth { get; set; } = 30;

	[Export]
	public int GridHeight { get; set; } = 18;

	[Export]
	public double MoveIntervalSec { get; set; } = 0.14;

	private readonly List<Vector2I> _snake = new();
	private Vector2I _direction = new(1, 0);
	private Vector2I _queuedDirection = new(1, 0);
	private Vector2I _apple;
	private bool _gameOver;
	private int _score;
	private int _bestScore;

	private Timer _timer = null!;
	private Label _scoreLabel = null!;
	private Label _lengthLabel = null!;
	private Label _bestLabel = null!;
	private Label _statusLabel = null!;
	private Label _gameOverLabel = null!;
	private Control _gameOverPanel = null!;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Randomize();
		_timer = GetNode<Timer>("%MoveTimer");
		_scoreLabel = GetNode<Label>("%ScoreLabel");
		_lengthLabel = GetNode<Label>("%LengthLabel");
		_bestLabel = GetNode<Label>("%BestLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_gameOverLabel = GetNode<Label>("%GameOverLabel");
		_gameOverPanel = GetNode<Control>("%GameOverPanel");
		_timer.WaitTime = MoveIntervalSec;
		_timer.Timeout += OnMoveTick;
		ResetGame();
	}

	public override void _Draw()
	{
		var origin = GetGridPixelOrigin();
		var gridPx = new Vector2(GridWidth * CellSize, GridHeight * CellSize);
		var shadowOffset = new Vector2(0f, 10f);
		DrawRect(new Rect2(origin + shadowOffset, gridPx), new Color(0f, 0f, 0f, 0.18f));
		DrawRect(new Rect2(origin, gridPx), new Color(0.05f, 0.08f, 0.06f));

		for (var y = 0; y < GridHeight; y++)
		{
			for (var x = 0; x < GridWidth; x++)
			{
				var tilePos = origin + new Vector2(x * CellSize, y * CellSize);
				var tileColor = (x + y) % 2 == 0
					? new Color(0.10f, 0.16f, 0.11f)
					: new Color(0.12f, 0.19f, 0.13f);
				DrawRect(new Rect2(tilePos, new Vector2(CellSize, CellSize)), tileColor);
			}
		}

		DrawRect(new Rect2(origin, gridPx), new Color(0.32f, 0.48f, 0.34f), false, 3f);

		var inset = new Vector2(3f, 3f);
		var cellInner = new Vector2(CellSize - 6f, CellSize - 6f);

		for (var i = 0; i < _snake.Count; i++)
		{
			var cell = _snake[i];
			var pos = origin + new Vector2(cell.X * CellSize, cell.Y * CellSize) + inset;
			var shadowRect = new Rect2(pos + new Vector2(1f, 2f), cellInner);
			var bodyRect = new Rect2(pos, cellInner);
			var color = i == 0 ? new Color(0.53f, 0.95f, 0.54f) : new Color(0.28f, 0.78f, 0.35f);
			var highlight = i == 0 ? new Color(0.79f, 1f, 0.82f) : new Color(0.54f, 0.92f, 0.59f);

			DrawRect(shadowRect, new Color(0f, 0f, 0f, 0.18f));
			DrawRect(bodyRect, color);
			DrawRect(new Rect2(pos + new Vector2(2f, 2f), cellInner - new Vector2(6f, 10f)), highlight);
		}

		if (_snake.Count > 0)
		{
			var headPos = origin + new Vector2(_snake[0].X * CellSize, _snake[0].Y * CellSize) + inset;
			DrawHeadEyes(headPos, cellInner);
		}

		var applePos = origin + new Vector2(_apple.X * CellSize, _apple.Y * CellSize) + inset;
		DrawRect(new Rect2(applePos + new Vector2(1f, 2f), cellInner), new Color(0f, 0f, 0f, 0.18f));
		DrawRect(new Rect2(applePos, cellInner), new Color(0.91f, 0.18f, 0.25f));
		DrawRect(new Rect2(applePos + new Vector2(3f, 3f), cellInner - new Vector2(10f, 12f)), new Color(1f, 0.43f, 0.48f));
		DrawRect(new Rect2(applePos + new Vector2(cellInner.X * 0.42f, -2f), new Vector2(3f, 8f)), new Color(0.39f, 0.25f, 0.16f));
		DrawRect(new Rect2(applePos + new Vector2(cellInner.X * 0.52f, 1f), new Vector2(8f, 4f)), new Color(0.33f, 0.72f, 0.36f));
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
		_bestScore = Mathf.Max(_bestScore, _score);
		_gameOverLabel.Text = $"Score {_score}  |  Length {_snake.Count}\nPress R to restart and chase a better run.";
		_gameOverPanel.Visible = true;
		UpdateHud();
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
		_gameOverPanel.Visible = false;
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
		_scoreLabel.Text = _score.ToString();
		_lengthLabel.Text = _snake.Count.ToString();
		_bestLabel.Text = _bestScore.ToString();
		_statusLabel.Text = _gameOver
			? "Crash detected. Reset with R and try a cleaner route."
			: $"Heading {DirectionName(_direction)}. Apples eaten: {_score}.";
	}

	private Vector2 GetGridPixelOrigin()
	{
		var vs = GetViewportRect().Size;
		var gw = GridWidth * CellSize;
		var gh = GridHeight * CellSize;
		var topSafeArea = 120f;
		var bottomSafeArea = 72f;
		var availableHeight = vs.Y - topSafeArea - bottomSafeArea;
		return new Vector2((vs.X - gw) * 0.5f, topSafeArea + (availableHeight - gh) * 0.5f);
	}

	private void DrawHeadEyes(Vector2 headPos, Vector2 cellInner)
	{
		var eyeSize = new Vector2(4f, 4f);
		var leftEye = headPos + new Vector2(5f, 5f);
		var rightEye = headPos + new Vector2(cellInner.X - 9f, 5f);

		if (_direction == Vector2I.Left)
		{
			leftEye = headPos + new Vector2(4f, 5f);
			rightEye = headPos + new Vector2(4f, cellInner.Y - 9f);
		}
		else if (_direction == Vector2I.Right)
		{
			leftEye = headPos + new Vector2(cellInner.X - 8f, 5f);
			rightEye = headPos + new Vector2(cellInner.X - 8f, cellInner.Y - 9f);
		}
		else if (_direction == Vector2I.Down)
		{
			leftEye = headPos + new Vector2(5f, cellInner.Y - 8f);
			rightEye = headPos + new Vector2(cellInner.X - 9f, cellInner.Y - 8f);
		}

		DrawRect(new Rect2(leftEye, eyeSize), Colors.Black);
		DrawRect(new Rect2(rightEye, eyeSize), Colors.Black);
	}

	private static string DirectionName(Vector2I direction)
	{
		if (direction == Vector2I.Up)
			return "up";
		if (direction == Vector2I.Down)
			return "down";
		if (direction == Vector2I.Left)
			return "left";
		return "right";
	}
}
