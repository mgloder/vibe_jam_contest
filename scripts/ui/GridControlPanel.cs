using Godot;
using System;

public partial class GridControlPanel : PanelContainer
{
	private Button _showCharacterButton = null!;
	private Button _removeCharacterButton = null!;
	private Button _grassButton = null!;
	private Button _forestButton = null!;
	private Button _mountainButton = null!;
	private Button _waterButton = null!;

	public event Action? RandomizeCharacterRequested;
	public event Action? ShowCharacterRequested;
	public event Action? RemoveCharacterRequested;
	public event Action<TerrainType>? TerrainSelected;

	public override void _Ready()
	{
		var randomizeButton = GetNode<Button>("%RandomizeCharacterButton");
		_showCharacterButton = GetNode<Button>("%ShowCharacterButton");
		_removeCharacterButton = GetNode<Button>("%RemoveCharacterButton");
		_grassButton = GetNode<Button>("%GrassButton");
		_forestButton = GetNode<Button>("%ForestButton");
		_mountainButton = GetNode<Button>("%MountainButton");
		_waterButton = GetNode<Button>("%WaterButton");

		randomizeButton.Pressed += () => RandomizeCharacterRequested?.Invoke();
		_showCharacterButton.Pressed += () => ShowCharacterRequested?.Invoke();
		_removeCharacterButton.Pressed += () => RemoveCharacterRequested?.Invoke();
		_grassButton.Pressed += () => TerrainSelected?.Invoke(TerrainType.Grass);
		_forestButton.Pressed += () => TerrainSelected?.Invoke(TerrainType.Forest);
		_mountainButton.Pressed += () => TerrainSelected?.Invoke(TerrainType.Mountain);
		_waterButton.Pressed += () => TerrainSelected?.Invoke(TerrainType.Water);
	}

	public void SetCharacterVisibilityState(bool isVisible)
	{
		_showCharacterButton.Disabled = isVisible;
		_removeCharacterButton.Disabled = !isVisible;
	}

	public void SetCharacterRandomizeEnabled(bool enabled)
	{
		var randomizeButton = GetNode<Button>("%RandomizeCharacterButton");
		randomizeButton.Disabled = !enabled;
	}

	public void SetSelectedTerrain(TerrainType? terrain)
	{
		_grassButton.Disabled = terrain == TerrainType.Grass;
		_forestButton.Disabled = terrain == TerrainType.Forest;
		_mountainButton.Disabled = terrain == TerrainType.Mountain;
		_waterButton.Disabled = terrain == TerrainType.Water;
	}
}
