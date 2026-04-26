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
	private Label _expandSizeValueLabel = null!;
	private Label _terrainSmoothnessValueLabel = null!;

	public event Action? RandomizeCharacterRequested;
	public event Action? ShowCharacterRequested;
	public event Action? RemoveCharacterRequested;
	public event Action? BuildingSimulatorRequested;
	public event Action? GenerateTerrainRequested;
	public event Action<int>? BuildingExpandSizeChanged;
	public event Action<float>? TerrainSmoothnessChanged;
	public event Action<TerrainType>? TerrainRemoveRequested;

	public override void _Ready()
	{
		var randomizeButton = GetNode<Button>("%RandomizeCharacterButton");
		var buildingButton = GetNode<Button>("%OpenBuildingSimulatorButton");
		var generateTerrainButton = GetNode<Button>("%GenerateTerrainButton");
		_showCharacterButton = GetNode<Button>("%ShowCharacterButton");
		_removeCharacterButton = GetNode<Button>("%RemoveCharacterButton");
		var expandSizeDownButton = GetNode<Button>("%ExpandSizeDownButton");
		var expandSizeUpButton = GetNode<Button>("%ExpandSizeUpButton");
		_expandSizeValueLabel = GetNode<Label>("%ExpandSizeValueLabel");
		var terrainSmoothnessSlider = GetNode<HSlider>("%TerrainSmoothnessSlider");
		_terrainSmoothnessValueLabel = GetNode<Label>("%TerrainSmoothnessValueLabel");
		_grassButton = GetNode<Button>("%GrassButton");
		_forestButton = GetNode<Button>("%ForestButton");
		_mountainButton = GetNode<Button>("%MountainButton");
		_waterButton = GetNode<Button>("%WaterButton");

		randomizeButton.Pressed += () => RandomizeCharacterRequested?.Invoke();
		buildingButton.Pressed += () => BuildingSimulatorRequested?.Invoke();
		generateTerrainButton.Pressed += () => GenerateTerrainRequested?.Invoke();
		_showCharacterButton.Pressed += () => ShowCharacterRequested?.Invoke();
		_removeCharacterButton.Pressed += () => RemoveCharacterRequested?.Invoke();
		expandSizeDownButton.Pressed += () => BuildingExpandSizeChanged?.Invoke(-1);
		expandSizeUpButton.Pressed += () => BuildingExpandSizeChanged?.Invoke(1);
		terrainSmoothnessSlider.ValueChanged += value => TerrainSmoothnessChanged?.Invoke((float)value);
		_grassButton.Pressed += () => TerrainRemoveRequested?.Invoke(TerrainType.Grass);
		_forestButton.Pressed += () => TerrainRemoveRequested?.Invoke(TerrainType.Forest);
		_mountainButton.Pressed += () => TerrainRemoveRequested?.Invoke(TerrainType.Mountain);
		_waterButton.Pressed += () => TerrainRemoveRequested?.Invoke(TerrainType.Water);
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

	public void SetBuildingExpandSize(int value)
	{
		_expandSizeValueLabel.Text = value.ToString();
	}

	public void SetTerrainSmoothness(float value)
	{
		var clamped = Mathf.Clamp(value, 0f, 1f);
		var slider = GetNode<HSlider>("%TerrainSmoothnessSlider");
		slider.Value = clamped;
		_terrainSmoothnessValueLabel.Text = $"{Mathf.RoundToInt(clamped * 100f)}%";
	}
}
