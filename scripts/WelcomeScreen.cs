using Godot;

public partial class WelcomeScreen : Control
{
	[Export]
	public string NextScenePath { get; set; } = "res://scenes/snake_game.tscn";

	public override void _Ready()
	{
		var startButton = GetNode<Button>("Center/VBox/StartButton");
		startButton.Pressed += OnStartPressed;
	}

	private void OnStartPressed()
	{
		// Defer so the scene change runs after the GUI/input stack unwinds (reliable with Button).
		CallDeferred(nameof(LoadNextScene));
	}

	private void LoadNextScene()
	{
		var path = NextScenePath;
		if (string.IsNullOrWhiteSpace(path))
		{
			GD.PrintErr("WelcomeScreen: NextScenePath is empty.");
			return;
		}

		var err = GetTree().ChangeSceneToFile(path);
		if (err != Error.Ok)
			GD.PrintErr($"WelcomeScreen: could not load '{path}' ({err}).");
	}
}
