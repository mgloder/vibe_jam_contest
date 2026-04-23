# Project snapshot

_Last updated: 2026-04-19_

## What this is

A **Godot 4.6** game project using **C#** (`.NET 8`, `Godot.NET.Sdk` 4.6.2). Entry flow: **welcome UI** → **Snake** (`scenes/snake_game.tscn`) via **Start**. Snake: arrow keys, apples grow the snake, **self-collision** ends the run; **180-degree turns** (e.g. left/right, up/down) are ignored while moving in the opposite direction. **Walls** also end the run (grid bounds).

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
| `scenes/welcome.tscn` | Layered welcome screen with title card, feature pills, and **Start Game** |
| `scripts/WelcomeScreen.cs` | Start → `ChangeSceneToFile` (default `res://scenes/snake_game.tscn`; export `NextScenePath`) |
| `scenes/snake_game.tscn` | Snake: `Node2D`, layered background, stats cards, controls panel, game-over overlay |
| `scripts/SnakeGame.cs` | Grid snake logic, custom board rendering, session best score, wall + self collision |
| `node_2d.tscn` | Unused placeholder |
| `NewScript.cs` | Unused placeholder |
| `icon.svg` | Application icon |

## Code notes

- Snake defaults are tuned for presentation: **30×18** cells, **28** px per cell, **0.14 s** move interval (exports on `SnakeGame`).
- `WelcomeScreen.NextScenePath` can point at another scene if the flow changes.
- `WelcomeScreen` now focuses the Start button on load for cleaner keyboard/controller-style navigation.

## Open questions / next steps

- Sound, persistent high scores, pause, or return-to-menu from Snake.
- Whether to keep the display name `"New Game Project"` or rename in `project.godot` and solution files.
- Pixel-perfect scaling: adjust `[display]` stretch for fixed-resolution pixel art.
