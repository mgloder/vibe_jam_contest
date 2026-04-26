# Project snapshot

_Last updated: 2026-04-26_

## What this is

A clean **Godot 4.6** starter using **C#** (`.NET 8`, `Godot.NET.Sdk` 4.6.2). Entry flow: **welcome UI** (`scenes/welcome.tscn`) → **gameplay placeholder** (`scenes/gameplay_placeholder.tscn`) via the main button. Use this as a base shell for a new game prototype.

## Engine & tooling

| Item | Value |
|------|--------|
| Godot | 4.6 (`config/features` includes `"4.6"`) |
| Language | C# (`[dotnet]` section; assembly name `"New Game Project"`) |
| Target framework | `net8.0` (Android build path uses `net9.0`) |
| Root namespace | `NewGameProject` |
| Rendering | Forward Plus |
| 3D physics | Jolt (project default) |
| Windows rendering device | D3D12 |
| 2D display | 1280×720 viewport, stretch **canvas_items**, aspect **expand** |

## Repository layout (meaningful paths)

| Path | Role |
|------|------|
| `project.godot` | Project config; `run/main_scene` = `res://scenes/welcome.tscn` |
| `New Game Project.csproj` / `New Game Project.sln` | C# build |
| `scenes/welcome.tscn` | Generic welcome/menu shell for bootstrapping a new game |
| `scripts/WelcomeScreen.cs` | Main button → `ChangeSceneToFile` (default `res://scenes/gameplay_placeholder.tscn`; export `NextScenePath`) |
| `scenes/gameplay_placeholder.tscn` | Temporary target scene to replace with your real gameplay entry point |
| `scenes/snake_game.tscn` | Existing Snake prototype scene retained for reference |
| `scripts/SnakeGame.cs` | Existing Snake prototype logic retained for reference |
| `icon.svg` | Application icon |

## Code notes

- `WelcomeScreen.NextScenePath` is export-driven, so scene flow can be rewired per scene without code changes.
- `WelcomeScreen` focuses the main button on load for keyboard/controller-friendly navigation.
- Snake files are optional legacy prototype content and are no longer the default flow.

## Open questions / next steps

- Rename display/project/assembly names once the real game title is decided.
- Replace `scenes/gameplay_placeholder.tscn` with the first actual gameplay scene.
- Decide whether to delete or archive the Snake prototype once the new game direction is stable.
