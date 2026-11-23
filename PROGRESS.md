# C# port progress

Branch: `csharp-port`. Do not push to `main`. The pull request is a draft until the founder says to merge.

## Done

- License read and recorded in `docs/PORT-PLAN.md` (MIT, copyright (c) 2026 Jerry Smith; three.js MIT stays with the browser build).
- Every `js/` file mapped to a C# type or a Godot scene.
- Playable milestone in `godot/`:
  - Core loop and state: orbit, Helion system, patched-conic SOI, part catalog, stock craft and delta-v, rocket flight, world step and warp.
  - Input: the flight keys, wheel zoom.
  - Rendering: floating-origin planet, part meshes, chase and orbit camera, HUD. `media/shot-centre.jpg` used as-is on the title screen.
  - Audio: procedural engine, pad, and one-shots. The repo has no sound files.
  - Menus: title, stock-rocket picker, About (original text plus a port section), settings, pause.
  - Save: `user://starwright-flight.json`.
- xUnit: 20 tests, all passing. `dotnet build` of `godot/Starwright.csproj` succeeds on Godot.NET.Sdk 4.7.2 with 0 warnings.
- Camera fix: the flight camera is perspective with near 0.5 and far 100,000, aimed with an up vector that is not parallel to the view, and it is current only during flight. Bodies are scaled into that range. Headless Godot 4.7.2 on `scenes/main.tscn` and `scenes/flight_check.tscn` logs no `create_frustum_points` errors. Hopper I and Skylark II both print `CAMERA_OK`.

## Files

- `docs/PORT-PLAN.md` the map and the license note
- `godot/` the Godot 4.7.2 C# project
- `godot/src/Starwright.Core/` the simulation
- `godot/src/Starwright.Core.Tests/` the xUnit suite
- `godot/scripts/AppRoot.cs`, `FlightView.cs`, `Synth.cs`, `FlightCheck.cs`
- `godot/src/Starwright.Core/RenderCamera.cs` camera and body placement used by the view and the tests
- `tools/export-catalog.mjs` regenerates `parts.json` and `stock-crafts.json`

The browser game is untouched apart from `.gitignore` entries for `bin/`, `obj/`, and `.godot/`.

## Exact next step

Port the rest of `js/core/vessel.js` that this milestone skipped, in this order, and translate the matching tests as you go:

1. Ground contact springs (`_contact`, `_buildContacts`) so landings are not a single sphere test.
2. Wheels and rovers (`wheel_*`, `Trundle Scout`), then enable the runway button.
3. Wing-borne flight (`wingCoefficients` is already in `Vessel` for fins; planes need the tilted-wing path and gear), then enable Kite Trainer and Skimmer.
4. Docking ports (`World._dockingStep`) so Latch Target and Latch Chaser can meet.

After that, port `SURFACES` from `js/core/bodies.js` into `Body.TerrainHeight` and `BiomeAt`. Do not start the assembly building until a rocket can land and a wheel can roll.
