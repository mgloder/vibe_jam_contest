using Godot;

/// <summary>Shown when the player cannot afford any ability card and has no living Servant.</summary>
public partial class GameOverScreen : Control
{
	private const string GridSimScene = "res://scenes/grid_simulator.tscn";
	private const string StartScene = "res://scenes/game_start.tscn";

	public override void _Ready()
	{
		var again = GetNode<Button>("%PlayAgainButton");
		var menu = GetNode<Button>("%MainMenuButton");
		again.Pressed += OnPlayAgainPressed;
		menu.Pressed += OnMainMenuPressed;
	}

	private void OnPlayAgainPressed() => GetTree().ChangeSceneToFile(GridSimScene);
	private void OnMainMenuPressed() => GetTree().ChangeSceneToFile(StartScene);
}
