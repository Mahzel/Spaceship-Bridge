# Handoff: Navigation UI (system map, orbit determination, maneuver planner, timeline, galaxy map) (2026-09-24)

This continues `claude/session-handoff-ui-tracking-overhaul.md` and `claude/handoff-loadout-and-imager-fixes.md`. It was a design pass only when first written; a follow-up session then answered the "Read first" questions and open questions 1-3 below, and built item 1's foundation (see "Progress" at the bottom). Read that section first if you're picking this up.

## Open questions 1-3: answered

1. **Hardware gating:** gate it like a sensor. Added `ProbeSystem.NavComputer` to `Core/Loadout.cs` (cost row `{0,3,6,10}`), plus a `NavTier` static class naming what each tier unlocks (`HasSystemView` level 1, `HasTransferPlanner`/`HasGalaxyMap` level 2, `HasOrbitDetermination` level 3 - only `HasSystemView` does anything yet). `RefitScreen` picked it up automatically since it loops `Loadout.All`; it got its own "NAVIGATION" section header and a level-0 label fix (see Progress).
2. **Where it lives:** full-screen mode (the user's stated direction: "everything is going to move to full screen mode in the end"). `NavScreen` is a full-screen dim+panel overlay, same family as `DebriefScreen`/`RefitScreen` but manually toggled (open while `RunPhase.Flight`, not phase-driven) via a NAV button on `StatusBar` (only shown once a nav computer is fitted). No existing "everything moves full-screen" doc exists yet; if that migration becomes its own project, `NavScreen`'s panel-anchor approach (stretch to ~94%x90% of the screen rather than a fixed centered box) is a reasonable template.
3. **2D vs 3D:** top-down (world X/Z) as the doc recommended, but with inclination made visible from the start per the user's explicit ask (overriding the doc's own default deferral) via a small semicircle dial widget in the sidebar (0deg = left/equatorial-prograde, 90 = top/polar, 180 = right/equatorial-retrograde) plus AN/DN markers on the plot itself. Full elevation-strip / 3D-ish view is still out of scope.

## Goal

Navigation is currently text: orbit readouts, a node list, jump and wake panels. Text doesn't work for spatial reasoning. The aim is a set of graphical navigation tools that:

- show the ship's orbit and planned burns visually,
- show the system without breaking the tracking-centric design (the player sees only what the sensors have tracked),
- make time and events (burns, SOI changes, close approaches) easy to plan around,
- give a galaxy map for jump planning.

## Design principle: two knowledge tiers

- **Own ship is exact.** Orbit, maneuver nodes, predicted path, Δv and fuel all come from `ShipOrbit`, `ManeuverPlan` and `KeplerOrbit`. No uncertainty is drawn.
- **Everything else comes from the catalogue and tracks.** The map draws a body only if it is:
  - catalogued (Sol bodies via `Core/Catalogue.cs`), or
  - a track. Tracks with no range are drawn as a bearing line and tracks with a range as a range arc or dot with a σ bar. They are not drawn as a precise position.
- **The map must never call `SensorSight` or `FindObjectsByType<CelestialBody>()`.** `SensorSight` is for sensors only. The omniscient view stays in `DevHud`, where it can be an optional "show truth" layer for debugging.
- Consequence: an uncharted system's map is nearly empty at arrival and fills in as the player tracks and identifies bodies. That is intended.

## Read first (not read in the design pass)

- **Orbit and maneuvers:** `UI/NodePanel.cs`, `UI/OrbitPanel.cs`, `UI/ManeuverPanel.cs`, `Core/ShipOrbit.cs`, `Core/ManeuverPlan.cs`, `Physics/KeplerOrbit.cs`, `Physics/OrbitalMechanics.cs`, `Physics/Vec3d.cs`.
- **Jump and galaxy:** `UI/JumpPanel.cs`, `UI/WakePanel.cs`, `UI/AtlasPanel.cs`, and whatever `Jump` is (referenced by `Jump.Restore` in the save code). Find where the galaxy position and the system-generation-from-seed entry point live.
- **Resources and comms:** `UI/ReactorPanel.cs`, `UI/TransmitPanel.cs`, `Core/GameClock.cs` (the `WarpLadder`).
- **UI framework:** `UI/GameUI.cs`, `UI/SystemsDock.cs`, `UI/DraggablePanel.cs`, `UI/PointerAim.cs`, `UI/AimKeys.cs`, `Core/Settings.cs` (`GameAction`, `InputMap`), `UI/UITheme.cs`.
- **Tracking data:** `Tracking/TrackManager.cs` (what `Track` stores: bearing history, elevation, range, σ values, `TrackInfo`), `Core/Catalogue.cs`.

Questions to answer from those files before designing in detail:

1. What do `NodePanel` and `OrbitPanel` already show and edit? Which parts become the readout side of the new map and which become redundant?
2. Does `ManeuverPlan` already compute a predicted post-burn trajectory, SOI transitions and closest approaches, or does the map need to generate those?
3. Do track histories store the ship's position at each sample? If not, triangulation (item 3 below) requires adding it.
4. Are galaxy positions stored as coordinates, or only as system IDs? Is there any galaxy-scale data structure yet?
5. How is the SOI/primary handled in `ShipOrbit`, and is there an event when it changes?

## Known constraints (from earlier sessions)

- **Units:** Keplerian mechanics run in AU, years and solar masses. Orbits are double precision (`Vec3d`). The map should draw relative to the focus in `Vec3d` and convert to float only at the last step.
- **Convention:** world bearing 0 = +Z, clockwise; elevation + up, geometric (`SensorSight.WorldElevation` convention), not the negated `CelestialBody.elevation` field.
- **Theme:** `UI/UITheme.cs` holds C# defaults, but `Assets/Resources/UITheme.asset` is what loads at runtime. Any new colours (orbit, predicted path, node, uncertainty band, SOI ring) need both. Screens capture theme colours at build; `GameUI.RebuildUI` rebuilds panels when theme or text size changes, so new screens must be rebuildable.
- **Input:** new hotkeys go through `GameAction` and `InputMap`, so they are rebindable and blocked while menus or TMP inputs are open.
- **Saves:** maneuvers, target, tracks and clock are already saved. UI state (map zoom, frame, layers) is not saved. Keep it that way unless the user asks.
- **Pause:** aiming works while paused. The map should stay fully interactive while paused (planning is the main reason to pause).
- **No compile or play feedback** through the file bridge. Standard push sequence: `device_list_dir` for fresh mtimes, write under outputs, `SendUserFile`, `device_commit_files` with `expectedMtimeMs`, re-list and compare sizes (an upload has been truncated before). Every change must be reasoned through by hand.

## Rendering approach

- Use a custom `MaskableGraphic` that builds mesh polylines (a shared `MapCanvas` component), not drawing on a `Texture2D`. Lines stay crisp at any zoom and the mesh is cheap to rebuild.
- Provide primitives: polyline (with width and colour), dashed polyline, filled/outlined circle, ring, arc, arrowhead, marker glyphs (Pe, Ap, AN, DN, node, SOI).
- Adaptive sampling for ellipses and hyperbolas: sample by true anomaly, denser near periapsis. Cap the vertex count.
- Interaction goes through `PointerAim` (click versus drag, local coordinates). Mouse wheel zooms around the cursor. Right-drag or middle-drag pans, left-click selects, left-drag on a handle edits.
- Note from the earlier handoff: dragging on sensor images now aims instead of moving the panel. The map should follow the same rule, with the panel dragged by its border or tab row.
- Label declutter: simple priority-based label placement, and hide labels under a minimum pixel separation.

## Roadmap

Ordered by dependency. Item 1 is the base for everything else.

### 1. Map core and own-ship orbit

- Top-down ecliptic view. Add a small side-elevation strip later (full 3D is out of scope for now).
- **Zoom:** linear and log-radial modes. The range runs from moons around a planet to 60+ AU, so log-radial is needed to see both. Log mode distorts orbit shapes, so show a clear "LOG" indicator.
- **Reference frame:** selector for star, a selected body, or ship. It auto-follows the current primary and switches when the ship crosses an SOI.
- **Ship orbit:** ellipse (or hyperbola) from the current state, with:
  - Pe and Ap markers (with altitude/distance labels),
  - AN and DN markers,
  - a live ν marker for the ship,
  - SOI and Hill sphere rings for bodies that have them.
- **Time scrubber:** shows ghost positions of the ship (and known bodies) at a future time, driven by the same propagation the orbit uses. It must not change the game clock.
- Layer toggles: catalogue, tracks, sensor cones, SOI rings, labels.
- Readout block (small): primary, a, e, i, period, time to Pe/Ap. This replaces the text-only orbit panel as the main view, but the text panel can stay as the detailed view.

### 2. Catalogue and track layers

- **Catalogued bodies:** drawn with orbit ellipses from the catalogue elements (Sol only for now; the Sol data is in `Data/SolSystem.cs`, which should not be read by the player-facing map except through `Catalogue`).
- **Tracks:**
  - **Bearing only:** a ray from the ship's position at the time of the last fix, with a fading tail.
  - **Ranged:** a dot at the bearing/range/elevation position with a σ bar (arc along range, wedge in bearing). Elevation is optional (`hasElevation`); without it the dot is placed in the ecliptic plane and flagged.
  - **Identified:** the class icon and name, and the catalogue orbit if it matches.
- **Selection:** clicking a track selects it and sets `Game.State` track selection, so the sensor screens and the map stay linked. Selecting a catalogued body sets the NAV target as `SystemScreen` does now.
- **Sensor overlay:** cones for the imager (FOV around its aim), waterfall fan (elevation tilt and half-width), radar sweep sector and spectrometer slit, drawn from the ship. Uses the same specs and current aim as the sensor screens, so read those values rather than duplicate them.
- Tracks are cleared on system jump (`Tracks.Generation`); the map must reset its cached track geometry on generation change.

### 3. Maneuver planner on the map

- Click on the ship orbit to place a node at that anomaly, or drag an existing node along the orbit.
- Handles for prograde, retrograde, normal, anti-normal, radial in and radial out. Drag to change the Δv component, with a mouse-wheel fine step and a numeric field for exact values.
- **Predicted path:** post-burn trajectory drawn in a second colour, propagated through following nodes and SOI changes for a chosen horizon.
- **Markers:** resulting Pe and Ap, SOI entry and exit, closest approach to the selected target, impact/atmosphere-entry warning.
- **Cost readout:** Δv for the node, total Δv, burn duration, H2 and power cost, and remaining Δv budget (Tsiolkovsky from the current H2 mass; check what `ReactorPanel` and the ship model already expose).
- `NodePanel` remains the precise editor; the map is the fast way to place and shape nodes. Both edit the same `ManeuverPlan` so they must stay in sync.

### 4. Timeline strip

A bar (bottom of the map or a global strip) listing upcoming events in time order:

- maneuver nodes and the start of each burn,
- SOI changes,
- Pe and Ap passages,
- closest approaches to the selected target,
- comms windows or blackouts (light-time to home, occlusion by a body), hooked to `TransmitPanel`,
- the earliest jump window, if the jump rules define one.

Each event has a "warp to" action that uses the `GameClock.WarpLadder`. **Auto-drop warp** before an event (a configurable margin) so the player doesn't skip a burn. This is the highest-value usability feature in the list, and item 3 depends on it for the burn-start case.

- Event generation should be one function producing a list of `NavEvent { time, kind, label, severity }` from the ship orbit, plan and known targets. The strip, the map markers and the alerts all read the same list.

### 5. Transfer helper

- Select a target (catalogued or identified body). Show the phase angle, current versus ideal phase, the next Hohmann window, and its Δv and time of flight.
- A "create nodes" button that inserts the two burns into `ManeuverPlan`.
- Assumes coplanar circular orbits at first; show a warning when inclination or eccentricity make the estimate rough.
- **Later:** porkchop plot (departure × arrival, colour = Δv, click to create nodes). It needs a Lambert solver in double precision, and it should be a separate task.

### 6. Orbit determination for tracked bodies (gameplay hook)

- Fit an orbit for a tracked body from its history: bearing (and elevation where available) plus range, combined with the ship's own known position at each sample, gives triangulation.
- The tracker's σ becomes an uncertainty band on the drawn ellipse (dashed, widening with σ), which shrinks as more data arrives.
- Extend the level ladder (Bearing / Rate / Ranged / Identified) with an orbit level: none, rough, fitted, refined.
- Requires the ship position (or a way to reconstruct it) in each track sample. If the track history does not store it, adding it changes the save format (`TrackSave`), so check backward compatibility with old saves (`has*` flags pattern used elsewhere).
- Keep the fit simple first (two-body, Kepler elements, weighted least squares over the primary's frame) and validate it the way the tracker was validated: simulated contacts against the true orbit, with a harness.
- A fitted orbit could also reconcile with the catalogue (as `Catalogue.Match` does for bearings) and feed dispute logic in `Review`. That is optional.

### 7. Galaxy map

- 2D disc with pan and zoom, generated from the seed. Sol is the anchor.
- **Systems:** atlas systems are marked with their state (visited, catalogued, disputed, unvisited). Unvisited stars show only what is observable at that range (class, distance), not their planets.
- **Jump:** range circle, selected destination, route preview with H2 and power cost (read the real cost model from `JumpPanel`).
- **Zoom hierarchy:** galaxy → sector → system. Entering the system level hands off to the system map (item 1).
- Depends on the answer to open question 4 above (whether galaxy coordinates exist). If not, that model comes first: a deterministic mapping from seed to star positions, so the map is consistent with `SystemFactory.Generate` for a given system ID.
- Filters: visited, catalogued, disputed, in range.

### 8. Smaller additions

- **Burn HUD:** heading tape or simple navball-style indicator with prograde/retrograde markers, Δv remaining, burn countdown. This could live on the status bar or the existing ship console.
- **Alerts:** periapsis below a body's surface or atmosphere, an unplanned SOI change, a close approach, low Δv margin. All come from the `NavEvent` list.
- **Δv budget widget:** remaining Δv, minus each planned node's cost.
- **Hazard layer (optional):** thermal zones by distance to the star.

## Suggested order

1. Map core and ship orbit (item 1)
2. Catalogue and track layers (item 2)
3. Node placement and predicted path (item 3)
4. Timeline strip (item 4)
5. Transfer helper (item 5)
6. Track-based orbit determination (item 6)
7. Galaxy map (item 7)

Items 1 to 4 are one connected chain, and the player-visible win comes at item 3. Item 7 is largely independent once galaxy data exists, so it can be moved forward if the user prefers.

## Open questions for the user

1. **Hardware gating:** should navigation tools be part of the loadout (a nav computer with Mk I to III giving longer prediction horizon, better orbit fits, more nodes), or always available? The loadout system (`Core/Loadout.cs`) already supports adding a `ProbeSystem`.
2. **Where it lives:** a new tab in the sensor console, its own dockable panel (`SystemsDock` / `DraggablePanel`), or a full-screen mode?
3. **2D or 3D:** is top-down plus an elevation strip enough for now?
4. **Precision on tracked bodies:** should tracked-only bodies ever show a full orbit ellipse before the fit level, or only bearing and range marks?
5. **Time scrubber scope:** ship only, or ship plus catalogued bodies? Bodies need propagation from catalogue elements.

## First steps for the new session

1. Read the files under "Read first" and answer the five questions above.
2. Confirm the open questions with the user (at least 1, 2 and 3).
3. Build the `MapCanvas` primitive and item 1 as a read-only view of the current ship orbit, before adding interaction.
4. Only then add layers and editing. Push in small steps given there is no compile feedback.

## Progress (this session)

The five "Read first" questions and open questions 1-3 are answered above. Item 1 (map core and own-ship
orbit) is built, read-only, no interaction beyond zoom +/-:

- **`Core/ShipOrbit.cs`**: two additions used by the map (and reusable by later items) - `OffsetAtTrueAnomaly(nu,
  out offset)` (exact position from the conic's own shape, no time propagation, so periapsis can be sampled as
  densely as apoapsis later) and `NodeCrossings(out ascending, out descending)` (where the conic crosses the
  reference plane, i.e. AN/DN, derived straight from the perifocal basis rather than needing Omega/omega).
- **`UI/MapCanvas.cs`**: the mesh-polyline primitive the design doc asked for (`MaskableGraphic`, not a
  Texture2D scope). Has polyline, dashed polyline, circle, arc (bearing convention: 0 = up, clockwise, matching
  every sensor screen), plus marker glyphs (cross, dot, triangle, arrow). No mitre joins on strokes - fine at
  the ~2px widths used here.
- **`UI/NavScreen.cs`**: the NAV full-screen overlay. Draws the ship's own conic (ellipse or the in-range branch
  of a hyperbola), the primary at the focus, Pe/Ap crosses, AN/DN dots, a live true-anomaly ship marker, a
  readout block (primary, a, e, i, period, true anomaly) and the inclination dial. Linear zoom (+/- buttons,
  auto-fit on open) only - no pan, no log-radial mode, no reference-frame selector yet.
- **`Core/Loadout.cs`**: `ProbeSystem.NavComputer` (appended at the end of the enum, so old save indices are
  unaffected), a cost row, and `NavTier` naming what each tier unlocks (only `HasSystemView` is wired up).
- **`UI/RefitScreen.cs`**: picks the new system up automatically (loops `Loadout.All`); added a "NAVIGATION"
  section header and fixed the level-0 label (was defaulting to "STOCK", now "NONE" like a sensor).
- **`UI/StatusBar.cs`** / **`UI/GameUI.cs`**: a NAV button (shown only once a nav computer is fitted) toggles
  `NavScreen`, open only while `RunPhase.Flight`.
- **`UI/UITheme.cs`** + **`Resources/UITheme.asset`**: four new colours (`navOrbit`, `navBody`, `navNode`,
  `navPlane`), added to both per the theme note above.
- **`UI/Loc.cs`**: all new strings (`ui.nav.*`, `refit.sys.navcomputer`, `refit.d.nav.*`, `refit.navigation`).

**Not compile-checked.** A Unity 6000.5.0f1 batchmode run was attempted from this session to verify, but it
exited immediately (return code 1) without reaching script import - almost certainly a licensing/instance-lock
conflict with the Editor already being open elsewhere, not a real signal either way. Open the project and check
the Console before relying on any of this.

**Known gaps / rough edges to fix on first look:**
- Ellipse sampling is uniform in true anomaly (`EllipsePoints = 96`), not adaptive/denser-near-periapsis as the
  doc suggests - fine visually at ordinary eccentricities, worth revisiting for very eccentric orbits.
- No pan; re-centering only happens via the zoom auto-fit on open.
- Label placement for Pe/Ap/AN/DN/ship is a fixed offset with no decluttering - the doc already flagged this as
  a later pass, but worth watching once markers can overlap (e.g. near-equatorial low-eccentricity orbits put Pe
  and AN close together).
- `NavScreen.Draw()`'s `AutoFit` reads `_mapRect.rect` right after a forced layout rebuild on open; should be
  correct but hasn't been visually confirmed given the compile-check gap above.

**Next up, per the "Suggested order" above:** item 2 (catalogue/track layers) is the natural next step - it
reuses `Core/Catalogue.cs` and `Tracking/TrackManager.cs` (already confirmed to store `shipX/shipZ` per sample,
which item 6's triangulation will need) and slots into the same `MapCanvas` this session built.
