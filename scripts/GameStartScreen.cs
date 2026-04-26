using Godot;

/// <summary>Entry screen: start a new run in the grid simulator.</summary>
public partial class GameStartScreen : Control
{
	private const string GridSimScene = "res://scenes/grid_simulator.tscn";

	public override void _Ready()
	{
		var start = GetNode<Button>("%StartButton");
		start.Pressed += OnStartPressed;
	}

	private void OnStartPressed()
	{
		var err = GetTree().ChangeSceneToFile(GridSimScene);
		if (err != Error.Ok)
			GD.PrintErr("Failed to load grid sim: ", err);
	}
}
