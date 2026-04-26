using Godot;

/// <summary>Shown when all hostiles are eliminated and the colony still has survivors.</summary>
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
