using Godot;

/// <summary>Brief interstitial after Play; then loads the grid simulator. Shows a random enemy-flavored line.</summary>
public partial class LoadingScreen : Control
{
	private const string NextScene = "res://scenes/grid_simulator.tscn";
	private const float MinDisplaySec = 2.0f;
	private static readonly string[] EnemyLines =
	{
		"The hush on the map is a mouth that never quite closes—only opens wider at the border.",
		"Peace was a field we tilled; the other thing remembers how roots taste before rot.",
		"We wrote laws for neighbors; the whispers in the static write teeth into them.",
		"Every line you call “safe” is a tide-mark—older hungers lick the other side of it."
	};

	private readonly RandomNumberGenerator _rng = new();
	private Label? _quoteLabel;
	private double _t;

	public override void _Ready()
	{
		_rng.Randomize();
		_quoteLabel = GetNodeOrNull<Label>("%QuoteLabel");
		if (_quoteLabel != null && EnemyLines.Length > 0)
			_quoteLabel.Text = EnemyLines[_rng.RandiRange(0, EnemyLines.Length - 1)];

		SetProcess(true);
		_t = 0;
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (_t < MinDisplaySec)
			return;
		SetProcess(false);
		var err = GetTree().ChangeSceneToFile(NextScene);
		if (err != Error.Ok)
			GD.PrintErr("Failed to load grid sim from loading: ", err);
	}
}
