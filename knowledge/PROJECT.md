# Project snapshot

_Last updated: 2026-04-26_

## What this is

A **Godot 4.6** pixel-grid game foundation using **C#** (`.NET 8`, `Godot.NET.Sdk` 4.6.2). Entry flow: **welcome UI** (`scenes/welcome.tscn`) → **grid simulator** (`scenes/grid_simulator.tscn`) via Start. The board is intentionally empty now to prepare for a RimWorld-style systems game.

## Engine & tooling

| Item | Value |
|------|--------|
| Godot | 4.6 (`config/features` includes `"4.6"`) |
| Language | C# (`[dotnet]` section; assembly `EdgeOfChaos`) — display name Edge of Chaos |
| Target framework | `net8.0` (Android build path uses `net9.0`) |
| Root namespace | `EdgeOfChaos` |
| Rendering | Forward Plus |
| 3D physics | Jolt (project default) |
| Windows rendering device | D3D12 |
| 2D display | 1280×720 viewport, stretch **canvas_items**, aspect **expand** |

## Repository layout (meaningful paths)

| Path | Role |
|------|------|
| `project.godot` | Project config; `run/main_scene` = `res://scenes/welcome.tscn` |
| `EdgeOfChaos.csproj` / `EdgeOfChaos.sln` | C# build |
| `scenes/welcome.tscn` | Welcome/menu shell for launching the simulation |
| `scripts/WelcomeScreen.cs` | Start button → `ChangeSceneToFile` (default `res://scenes/grid_simulator.tscn`; export `NextScenePath`) |
| `scenes/grid_simulator.tscn` | Main simulation scene with HUD, timer, and controls help |
| `scripts/GridSimulator.cs` | Empty large pixel board renderer and starter HUD for upcoming RimWorld-style systems |
| `scenes/snake_game.tscn` | Existing Snake prototype scene retained for reference |
| `scripts/SnakeGame.cs` | Existing Snake prototype logic retained for reference |
| `icon.svg` | Application icon |

## Code notes

- `GridSimulator` currently defaults to a very large board: **960×540** cells at **1 px** cell size.
- Current controls are intentionally minimal while scaffolding (`R` redraw).
- No gameplay simulation rules are active yet; this scene is a visual and structural base.
- `WelcomeScreen.NextScenePath` is export-driven, so scene flow can be rewired per scene without code changes.
- `WelcomeScreen` focuses the main button on load for keyboard/controller-friendly navigation.
- Snake files are optional legacy prototype content and are no longer the default flow.

## Open questions / next steps

- Choose simulation-specific mechanics on top of Life rules (resources, agents, hazards, terrain, economy, etc.).
- Decide whether grid edges should wrap toroidally or remain bounded.
- Decide whether to delete or archive the Snake prototype now that the simulator is the default game direction.
