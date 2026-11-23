# Starwright C# port plan

Authorized by the founder on 2026-11-22. Work stays on the branch `csharp-port`. Nothing is pushed to `main`. The pull request stays a draft until the founder says to merge.

## License

Starwright is released under the MIT License, copyright (c) 2026 Jerry Smith. The notice at the repository root (`LICENSE`) covers this port. MIT requires that the copyright notice and the permission notice travel with every copy and every substantial portion of the software. This port does not relicense anything.

`js/lib/three.module.min.js` (three.js r170) is also MIT, copyright (c) 2010-2024 three.js authors. The terms are in `js/lib/LICENSE-three.txt`. The browser build keeps that file. The Godot build does not ship three.js. Rendering in the port is Godot's own renderer.

Files in `media/` are part of the MIT-licensed work. The port uses them as they are. It does not re-encode the trailer, the gif, or the screenshots.

Godot Engine is MIT too, but it is a tool the player installs (Godot 4.7.2 .NET). It is not vendored in this repository.

The game remains an independent original. It is not affiliated with or endorsed by the makers of Kerbal Space Program, by SpaceX, by Anthropic, or by xAI.

## How the port is laid out

The JavaScript game stays where it is and still runs from `index.html`. The Godot project lives in `godot/`.

| Path | Role |
|---|---|
| `godot/project.godot` | Godot 4.7 project, C#, Forward+ |
| `godot/Starwright.csproj` | Game assembly, `Godot.NET.Sdk/4.7.2`, `net8.0` |
| `godot/src/Starwright.Core` | Simulation library. No Godot types, so xUnit can run it |
| `godot/src/Starwright.Core.Tests` | xUnit port of the simulation checks we have covered |
| `godot/scripts` | Screens, the flight view, procedural audio |
| `godot/src/Starwright.Core/Data/*.json` | Part catalog and stock craft, exported from the JS sources |

The simulation uses double precision (`DVec3`, `DQuat`). Godot's `Vector3` is single precision, and a solar system of 1e11 m does not fit in a float. The flight view keeps the craft at the camera and moves the planet (a floating origin). Only the render edge converts doubles to floats.

Regenerate the embedded catalog after a catalog change in `js/core`:

```
node tools/export-catalog.mjs
```

## Module map

Every `js/` file, and where it goes. "This milestone" means it is in the Godot build the founder can play now. "Later" means the class or scene is named, and the JavaScript is still the source of truth.

### Core simulation (`js/core`, pure, no DOM)

| JavaScript | C# | Status |
|---|---|---|
| `js/core/noise.js` | `NoiseField`, `Rng` | This milestone. Same mulberry32 and gradient noise |
| `js/core/orbit.js` | `Orbit` | This milestone. Kepler solvers, elements, state, JSON |
| `js/core/bodies.js` | `Body`, `HelionSystem` | Orbital elements, atmospheres, SOI, launch sites. Terrain height is 0 (smooth spheres). The `SURFACES` height field and biome sampler are later |
| `js/core/trajectory.js` | `Trajectory` | This milestone. Patched conics, SOI changes, maneuver basis, closest approach |
| `js/core/parts.js` | `PartCatalog` + `Data/parts.json` | This milestone. All 186 listed parts, thrust and isp |
| `js/core/craft.js` | `Craft` + `Data/stock-crafts.json` | Stock designs load with their real staging, and `Stats` / `DeltaV` match the JS budgets. The builder edits (`attachStack`, symmetry, auto-stage while editing) are later |
| `js/core/vessel.js` | `Vessel` | Rocket flight: thrust, fuel sections, staging, separation, stack drag, fin lift, heating, parachutes, SAS, reaction wheels, sphere landing. Wheels, full wing-borne flight, docking, EVA, and the contact-spring ground model are later |
| `js/core/world.js` | `World` | Launch, physics substeps, warp, rails coast, SOI, debris from staging. Docking, EVA, experiments, recovery are later |
| `js/core/autopilot.js` | `Autopilot` (not written yet) | Later. Ascent, nodes, landing, rendezvous, planes, rovers |
| `js/core/career.js` | `Career` (not written yet) | Later. Funds, science, tech tree, milestones. Flight save is `SaveGame` instead |
| `js/core/contracts.js` | `Contracts` (not written yet) | Later |
| `js/core/scenarios.js` | `Scenarios` (not written yet) | Later. The flight menu launches stock craft from the pad, which is how First Orbit begins, but the 15 scripted mid-flight setups are not built |

### Shell, input, audio, menus (`js/ui`, `js/main.js`)

| JavaScript | C# | Status |
|---|---|---|
| `js/main.js` | `AppRoot` | Screen loop for title, flight, pause. Career, research, missions, crew screens are later |
| `js/ui/input.js` | `AppRoot` keys + `InputMath.NormalizeWheel` | Flight keys match the browser map (W/S pitch, A/D yaw, Q/E roll, Z/X throttle, Space stage, T SAS, G legs, U solar, V camera, comma/period warp, F5/F9). The browser wheel helper is ported and tested. Remapping UI is later |
| `js/ui/audio.js` | `Synth` | Procedural, same idea as the WebAudio bus: engine rumble, a quiet pad, and one-shots. No sound files exist in the repo |
| `js/ui/about.js` | `AboutContent` | The five original sections, word for word, plus a "This build" section so this executable does not pretend it still draws with three.js |
| `js/ui/dom.js` | Godot `Control` nodes in `AppRoot` | Title, craft list, About, settings, pause, HUD |
| `js/ui/hub.js` | Title and craft picker in `AppRoot` | The 3D Flight Center is later. The title uses `media/shot-centre.jpg` as-is |
| `js/ui/flight.js` | `AppRoot` flight input and pause | The flight computer, action groups, and target selection are later |
| `js/ui/hud.js` | HUD label in `AppRoot` | Altitude, speed, apoapsis, periapsis, throttle, stage, SAS, warp |
| `js/ui/flightscene.js` | `FlightView` | Planet sphere, craft meshes, chase and orbit camera |
| `js/ui/navball.js` | Pitch and heading numbers on the HUD | The 3D ball is later |
| `js/ui/builder.js`, `js/ui/builder/*` | `Builder` scene, later | Assembly hall, panels, symmetry |
| `js/ui/mapview.js`, `js/ui/map/*` | `Map` scene, later | Maneuver nodes are in `Trajectory` but there is no map UI |
| `js/ui/flightcam.js` | Chase / orbit in `FlightView` | Later: locked and free cameras |
| `js/ui/scenarios.js` | Later | |
| `js/ui/missions.js`, `guide.js`, `crew.js`, `research.js`, `science.js`, `sciencetext.js`, `careerui.js`, `tracking.js`, `baseview.js`, `interior.js`, `governor.js`, `admin.js`, `perf.js`, `perfbench.js`, `demo.js`, `demo/*`, `tex.js` | Later, one scene or panel each | |

### Rendering (`js/render`)

The browser renderer is three.js. This milestone draws with Godot primitives: a sphere per body in that body's colour, a translucent atmosphere shell, a directional light, a starfield, and a mesh per part (cylinder for stack parts, box for radial parts, a flame cone on a burning engine). `media/` images are not rebaked.

| JavaScript | Later C# / scene |
|---|---|
| `js/render/globe.js`, `bodytex.js`, `bodybake.js`, `terrain.js`, `terrainmesh.js`, `terrainworker.js`, `worlds.js`, `worldshaders.js`, `worldstyle.js` | `PlanetView` once `Body.TerrainHeight` is the real height field |
| `js/render/atmosphere.js`, `sky.js`, `planetgfx.js` | Atmosphere shader and sky |
| `js/render/partmesh.js`, `parts/*` | Part meshes that match the browser shapes |
| `js/render/fx.js`, `post.js` | Exhaust, explosions, bloom |
| `js/render/flora.js`, `floratex.js`, `scatter.js`, `treegen.js`, `baseenv.js`, `basegfx.js` | Space centre and vegetation |
| `js/render/workpool.js` | Not needed. Godot threads replace the worker pool |

`js/lib/three.module.min.js` stays with the browser build. It is not ported.

`js/perf.js` is the browser frame timer. Not ported.

## Tests

`tests/unit/orbit.test.js` is ported (noise, round trips, elements, solvers, JSON).

New xUnit cases cover the Helion system (19 bodies, SOI, Verda prograde, launch site), stock-craft delta-v (Skylark vacuum dv at least 3600 m/s and liftoff TWR 1.3 to 1.9, Pell Runner at least 5600 m/s), a Hopper liftoff, a coasting orbit, an SOI exit, save and load, the About text, and wheel normalisation.

Not yet ported: the rest of `tests/unit` (vessel contact, wheels, wings, docking, career, craft editing, scenarios) and all of `tests/e2e` and `tests/verify`. Those stay the browser suite. They should be translated as each system lands, not rewritten as screenshot checks.

## Play this milestone

Godot 4.7.2 .NET, open `godot/project.godot`.

1. Fly, then Hopper I or Skylark II.
2. Z for full throttle, Space to light the stage, D to tip east, Space again when a stage is spent.
3. Comma and period change time warp. F5 saves. Esc pauses.

Runway craft (Kite Trainer, Skimmer, Trundle Scout) are listed and disabled. Their wheels and wings are not in this build.

## What remains, in order

1. Finish `vessel.js`: contact springs, wheels, wing-borne flight, docking, EVA, so the disabled craft can fly and the vessel unit tests can be ported.
2. Port `bodies.js` `SURFACES` so terrain height and biomes match, then replace the sphere with a mesh.
3. Port `craft.js` editing and the assembly building (`js/ui/builder.js`).
4. Map view and maneuver-node handles on top of `Trajectory`.
5. `scenarios.js`, then career, contracts, science, crew, missions.
6. `autopilot.js`.
7. Part meshes and the space centre, reusing `media/` as-is.
8. Translate the rest of `tests/unit` as those systems arrive.

## Founder decisions still open

- This pull request is a draft. Merge only if the founder says so.
- The About screen keeps the original five sections and adds "This build". Say if that sixth section should go.
- Worlds are smooth spheres until the height field is ported. The pad is at height 0, which matches the browser game's launch site.
- The HUD delta-v is the burn that is lit now, not the full staged total the browser HUD shows. The design total is what the unit test checks.
- No export preset is chosen (Windows, macOS, Linux). The founder plays from the Godot editor for now.
