# Project snapshot

_Last updated: 2026-04-19_

## What this is

A **Godot 4.6** game project using **C#** (`.NET 8`, `Godot.NET.Sdk` 4.6.2). Entry flow: **welcome UI** → **Snake** (`scenes/snake_game.tscn`) via **Start**. Snake: arrow keys, apples grow the snake, **self-collision** ends the run; **180° turns** (e.g. left↔right, up↔down) are ignored while moving in the opposite direction. **Walls** also end the run (grid bounds).

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
| `scenes/welcome.tscn` | Welcome screen + **Start** |
| `scripts/WelcomeScreen.cs` | Start → `ChangeSceneToFile` (default `res://scenes/snake_game.tscn`; export `NextScenePath`) |
| `scenes/snake_game.tscn` | Snake: `Node2D` + `MoveTimer` + HUD |
| `scripts/SnakeGame.cs` | Grid snake logic, `_Draw` for snake/apple, wall + self collision |
| `node_2d.tscn` | Unused placeholder |
| `NewScript.cs` | Unused placeholder |
| `icon.svg` | Application icon |

## Code notes

- Snake grid: defaults **40×22** cells, **32** px per cell (exports on `SnakeGame`). Tune `MoveIntervalSec` for speed.
- `WelcomeScreen.NextScenePath` can point at another scene if the flow changes.

## Open questions / next steps

- Sound, high scores, pause, or return-to-menu from Snake.
- Whether to keep the display name `"New Game Project"` or rename in `project.godot` and solution files.
- Pixel-perfect scaling: adjust `[display]` stretch for fixed-resolution pixel art.
