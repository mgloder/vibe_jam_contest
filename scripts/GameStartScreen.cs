using Godot;

/// <summary>Entry screen: start a new run via the loading interstitial, then the grid simulator.</summary>
public partial class GameStartScreen : Control
{
	private const string LoadingScene = "res://scenes/loading.tscn";

	public override void _Ready()
	{
		var start = GetNode<Button>("%StartButton");
		start.Pressed += OnStartPressed;
	}

	private void OnStartPressed()
	{
		var err = GetTree().ChangeSceneToFile(LoadingScene);
		if (err != Error.Ok)
			GD.PrintErr("Failed to load loading scene: ", err);
	}
}
