# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Stern's 1980 arcade game Berzerk, in C# on .NET 10 (`net10.0`) and Avalonia 12. It is one
project at the repository root, with no tests. The graphics are drawn in code. The sound effects come from
`tools/make_sounds.py`, and the robots' voices from `tools/make_voice.sh`.

## Commands

```bash
dotnet build AVABerzerk.slnx                  # there is no global.json: any .NET 10 SDK will do
dotnet run --project AVABerzerk.csproj        # play it; takes no command-line arguments
python3 tools/make_sounds.py                  # rebuild the effects in Sounds/ (standard-library Python only)
tools/make_voice.sh                           # rebuild Sounds/Voice*.wav: needs Piper and a VCTK voice
```

There are no tests yet. `make_voice.sh` reads `PIPER`, `VOICE` and `SPEAKER` to find the Piper binary and
voice model, and runs `tools/robot_voice.py` on each line to make it sound like the cabinet's robots.

## Layout

- `Program.cs`, `App.axaml`, `MainWindow.axaml`: the Avalonia start-up and the window that holds the canvas.
- `Engine/`: the game.
  - `GameEngine.cs` runs the states (title, play, pause, game over, attract, initials, high scores), scoring
    and lives. `GameEngine.Room.cs` handles the robots, bolts, walls and Evil Otto in the current room.
  - `AttractPilot.cs` plays the humanoid in the attract-mode demonstration.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) feed the engine.
  - `SoundManager.cs` plays the WAV files and queues the robot voice lines.
  - `HighScoreTable.cs` and `InitialsEntry.cs` do what their names say.
- `Models/`: `Room.cs` (a room's walls and doors), `Entities.cs` (humanoid, robot, bolt, Otto and the robot
  progression) and `Explosion.cs`.
- `Views/`: `GameCanvas` (split over partial files) runs the timer, handles keys and draws every screen.
- `Sounds/`: the WAV files, copied to the output folder. `Images/`: the README's screenshots.
- `tools/`: the sound and voice generators.

## Rules that keep it working

- **Frame time, not fixed steps.** `GameCanvas` ticks a 16 ms `DispatcherTimer` and passes the real elapsed
  time, capped at 0.1 s, to `GameEngine.Update`. Movement and timers must scale by that `deltaTime`.
- **Rooms are seeded, the rest is not.** `Room` builds its maze from `new Random(Seed(x, y))`, so a room is
  the same every time it is entered; keep that. Robot counts, the attract pilot and explosions use unseeded
  `Random` instances.
- **Input goes through `InputHandler`.** `Stick` is screen-down positive: up on the keys, D-pad or stick gives
  a negative `y`. Keep any new control on that sign.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. Keep new SDL
  calls inside it.
- **Audio.** `SoundManager` plays WAV files with NetCoreAudio, which needs `aplay` on Linux. Playback errors
  are swallowed, so a missing player means silence, not a crash.
- **Files.** High scores are saved in the app-data folder (`SpecialFolder.ApplicationData`, `AVABerzerk`).
  Ctrl+S saves screenshots to the Pictures folder.
- **Line endings.** Keep each file's existing line endings (some files, such as `Program.cs` and
  `ControllerManager.cs`, are CRLF) so diffs show only real changes.

## Making a change

- Build with no new warnings, and play the change, since there are no tests to catch it.
- Update `README.md` (features, controls, screenshots in `Images/`) to match.
- New sounds go into `tools/make_sounds.py` and voice lines into `tools/make_voice.sh`, not as hand-edited
  WAV files.

## CI and releases

`.github/workflows/build.yml` builds every push and pull request (it runs no tests). It then publishes
self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
