using Godot;

/// <summary>Shown when no living civilians remain on the sim board.</summary>
public partial class WinGameScreen : Control
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
