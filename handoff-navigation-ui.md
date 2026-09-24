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

## Progress (this session - item 2: catalogue and track layers)

Read-only, toggle-able layers added on top of item 1's own-ship plot, both gated by the same `HasSystemView`
tier (no new hardware requirement):

- **`Physics/KeplerOrbit.cs`**: refactored the perifocal->world rotation out of `OffsetAt` into a shared
  `Rotate` helper, and added `OffsetAtTrueAnomaly(in OrbitElements, double nu)` - the `OrbitElements` analogue
  of `ShipOrbit.OffsetAtTrueAnomaly`, so a catalogued body's whole ellipse can be traced by anomaly (matching
  the ship's own sampling) instead of walking simulated time.
- **`Core/Catalogue.cs`**: `CollectOrbitsAroundPrimary(primaryIndex, result)` - the catalogue layer's only data
  source. Returns catalogued bodies (`Atlas.FindCatalogued`) of the CURRENT system whose `NodeData.parent`
  equals the ship's current primary index, each with its `OrbitElements` (already relative to that same
  primary, so no frame conversion is needed) and radius. Reads only the Atlas + the generated `SystemData` -
  never `SensorSight` or a live `CelestialBody`, per the roadmap's hard rule. Restricting to "same parent as
  ship's primary" is deliberate: it's exactly the set the map can draw in the same frame as the ship's own
  conic, and it naturally becomes "the planets" when the primary is the star and "the moons" when the primary
  is a planet, without any extra frame-walking code.
- **`UI/NavScreen.cs`**: two new draw passes, `DrawCatalogueLayer` and `DrawTrackLayer`, plus a "CATALOGUE" /
  "TRACKS" toggle button pair in the sidebar (`UIKit.SetButtonActive` highlight, same convention as other
  screens' selected-option buttons). Labels are pooled (`_catalogueLabels`/`_trackLabels`, grown to the
  largest count seen, hidden rather than destroyed when the count shrinks) since these layers have a variable
  number of entries, unlike item 1's fixed Pe/Ap/AN/DN/ship labels.
  - **Catalogue layer**: dim ellipse (`UITheme.navCatalogue`, new colour, added to both the C# default and
    `Resources/UITheme.asset`) sampled the same way as the ship's own conic, plus a small dot at the body's
    actual position now (`KeplerOrbit.OffsetAt`) and a name label.
  - **Track layer**: colour follows `UITheme.WaterfallTrackColor` (searching/locked/selected), same as the
    waterfall and Track panel. A track with an observable range (`TrackManager`'s TMA/radar fusion,
    `tr.range.Observable`) draws as a dot - its `RangeEstimate.x/z` are in the SYSTEM frame (ship-relative,
    per `ApplyRadarFix`), so they're re-based onto the primary via `SystemData.PositionOf(primaryIndex, now)`
    before going through the same `toScreen` the rest of the map uses - plus a short 1-sigma tick along the
    bearing line. A track with no observable range (bearing-only, or ranged but not locked) draws as a dashed
    ray from the ship's own screen position along its measured bearing, a fixed `BearingRayPx` (160px) long -
    a real "ray to infinity with a fading tail" per the design doc, not implemented (no gradient alpha in
    `MapCanvas` yet; flagged below).

**Not compile-checked** (same batchmode/license-lock issue as item 1's session - see that note above; nothing
about this pass should have removed the blocker). Open the project and check the Console before relying on it.

**Known gaps / rough edges to fix on first look:**
- Bearing-only ray has no "fading tail" - it's one dashed colour end to end. `MapCanvas` would need a
  per-vertex alpha gradient (or several shorter fading dashed segments) to do this properly.
- No sensor-cone overlay yet (imager FOV, waterfall fan, radar sweep, spectrometer slit) - still open from the
  roadmap's item 2 scope.
- Tracks are not reset/reacquired on `TrackManager.Generation` change (system jump) in any special way here -
  worth confirming the pooled labels don't show stale entries for one frame right after a jump (should self
  correct next redraw tick since `DrawTrackLayer` rebuilds fully every call, but not visually confirmed).
- Track/catalogue label overlap: same "no declutter yet" gap item 1 already flagged for Pe/Ap/AN/DN, now
  worse with an unbounded number of tracks/bodies - still deferred, per the roadmap.
- Catalogue layer only draws bodies sharing the ship's current primary; a body one level up or down (e.g. a
  moon of a DIFFERENT planet than the one the ship orbits) is invisible until the ship's primary changes. This
  matches "system view" scope on purpose (see above) but is worth restating if it looks like a bug.

**Next up, per the "Suggested order" above:** item 3 (node placement and predicted path) is the natural next
step - the map core, the ship's own conic and now the catalogue/track layers are all in place for a target to
click against. Item 4 (timeline strip) depends on it. The sensor-cone overlay (still open from item 2's own
scope) could also be picked up first if preferred, since it's a smaller, independent addition to
`DrawTrackLayer`'s sibling passes.

## Progress (this session - NavComputer baseline, item 5: transfer helper)

- **`Core/Loadout.cs`**: `NavComputer` is now baseline like the waterfall - `MinLevel` returns 1 for it (was
  0), and its cost row changed from `{0,3,6,10}` to `{0,0,6,10}` (Mk I free). `ResetToBaseline`/`FromList`
  already drive off `MinLevel`, so every new game and every loaded save (old ones included - `FromList`
  clamps a saved level up to `MinLevel`) now starts with `HasSystemView` true and the NAV button visible from
  the first flight, no refit trip required. Mk II/III (transfer planner, galaxy map, orbit determination)
  still cost trust same as before.
- **`Core/ManeuverPlan.cs`**: turns out roadmap item 5's Hohmann math already existed
  (`SolveHohmann`/`Node.TotalDvKmS`, used by `UI/NodePanel.cs`'s existing text-only "plot a transfer" button)
  - what was missing was the WINDOW: `SolveHohmann` arms its departure burn at whatever time it's given, with
  no check that the target will actually be at the rendezvous point when the ship gets there. Added
  `ComputeTransferWindow(target, now)`: the classic phase-angle formula (`gammaIdeal = pi - n2*transferTime`)
  against the current angular separation between ship and target (both read off the same shared-primary
  frame `NavScreen` already uses), giving phase now vs. ideal, wait time, the resulting depart time, and the
  same Δv/time-of-flight `SolveHohmann` would produce. Refactored the shared dv/transfer-time algebra into a
  small private `SolveHohmannGeometry` so `SolveHohmann` and `ComputeTransferWindow` can't drift apart.
  `SolveHohmann`'s time parameter is now named `departureSimSeconds` (was `nowSimSeconds`) to make clear it's
  not always "right now" anymore - `NodePanel`'s existing call site (passes `Game.Clock.SimSeconds`, i.e.
  still "now") is unaffected, it just keeps making the same phase-blind transfer it always did.
- **`UI/NavScreen.cs`**: a TRANSFER section in the sidebar (target name, phase now/ideal, wait/window, Δv +
  ToF, a CREATE NODES button) reading `Game.State.TargetBodyName` - the same field `Display/SystemScreen.cs`
  sets when a row there is clicked, so a target picked on either screen shows up on both. Clicking a track's
  marker directly on the NAV map (new `PointerAim` on `_mapRect`, `_map.raycastTarget` flipped on) now does
  the same selection `SystemScreen.Select` does (`Tracks.SelectedId` + `SetTarget` from `info.catalogName` if
  identified) - `_trackHits`, a list of `(trackId, lastDrawnScreenPos)` rebuilt every `DrawTrackLayer` call,
  is what the click hit-tests against (nearest within `TrackClickRadiusPx`). CREATE NODES recomputes the
  window fresh (it may have shifted since the last redraw tick) and arms `SolveHohmann`'s two burns at
  `w.departSimSeconds` via the existing `ManeuverPlan.SetPair` - the same queue `NodePanel`'s ARM/WARP/CLEAR
  buttons already operate on, so warping to the burn works with no changes there.
- Only catalogued/tracked bodies sharing the ship's current primary are ever offered as a transfer target
  (same restriction `SolveHohmann` already had - "same primary only"); the sidebar just says so via the
  "unavailable" message rather than silently doing nothing.

**Not compile-checked** (same batchmode/license-lock issue noted throughout this file). The phase-angle math
in particular deserves an in-Editor sanity check against a known case (e.g. Earth -> Mars from a circular
LEO-scale start) before trusting the wait-time numbers.

**Known gaps / rough edges to fix on first look:**
- `ComputeTransferWindow` assumes both ship and target orbit the primary in the SAME rotational sense
  (prograde); a retrograde target would get a wrong wait time. `SolveHohmann`'s own burn-size math doesn't
  care about direction, only the new phase-window code does.
- No on-map preview of the transfer ellipse itself, and no indication of WHERE (which point on the ship's
  current orbit) the departure burn happens - the sidebar's numbers are the only feedback until item 3 (node
  placement + predicted path) lands and can draw it.
- CREATE NODES silently no-ops if the target isn't found in the current `SystemData` (e.g. target set, then
  the ship jumped systems without clearing it) - matches `NodePanel.PlotTransfer`'s existing behaviour, not a
  new gap, but worth a "target lost" message if it comes up in testing.
- No warp-to-departure convenience button on the NAV screen itself; `NodePanel`'s existing WARP button (NODE
  tab of `SystemsDock`) already works on the same queue, so this is a nice-to-have, not a blocker.

## Progress (this session - track-derived orbit fit, discarding the NodeData "cheat")

The transfer planner used to look its target up by name in the generated `SystemData` (`NodeData`) -
omniscient ground truth, gated only by spectrometer identification (so the player could know exactly where
a body was and where it was going the instant its class resolved, regardless of what the sensors had
actually measured). Per discussion with the user: that's backwards for an exploration game - the planner
should only ever see what the ship itself has worked out, uncertainty included, and identification
shouldn't be a gate on "does this track have a plannable orbit" at all (range is what matters; a bad fit
making a bad burn is the intended risk, not a bug to route around).

- **`Tracking/OrbitFit.cs`** (new): `TryFit(Track, out OrbitElements)`. Not a new fitting algorithm - the
  track's `range` (`TrackManager.BestRange`, TMA or radar) already carries a full state vector (TMA's
  constant-velocity model fits position AND velocity), so this is just: read that state vector, subtract the
  primary's own position/velocity (`OrbitalMechanics.NodeState`, the same call `ShipOrbit` itself uses) to
  get it into the primary-relative frame, then `OrbitalMechanics.StateToElements` - the exact conversion
  `ShipOrbit.Resolve` runs on the ship's own state. It "refines with time" for free: `RangeEstimate.rangeSigma`
  already shrinks as the ship's own orbital arc builds parallax (this used to need a burn only because the
  ship coasted in a literal straight line before the `ShipOrbit` rewrite - now that it's genuinely orbiting,
  TMA converges from that curvature alone, no burn required, matching what the user suspected). Only valid
  while the target shares the ship's CURRENT primary - a one-hop frame change, not a general solver; a target
  around a different body isn't handled yet.
- **`Core/ManeuverPlan.cs`**: `SolveHohmann` and `ComputeTransferWindow` now take `in OrbitElements target`
  instead of `NodeData target` - `NodeData`/`SystemData` are gone from this file entirely. The caller is
  responsible for having a legitimately-known orbit (i.e. `OrbitFit.TryFit`'s output); the burn math itself
  doesn't care where the elements came from, so this also makes the file honestly reusable if a later pass
  wants to plan against a hand-entered or catalogue-sourced orbit for some other reason.
  `UI/NodePanel.cs`'s "plot a transfer" and `UI/NavScreen.cs`'s TRANSFER section both now read the SELECTED
  TRACK (`Game.State.Tracks.SelectedId`) and call `OrbitFit.TryFit` directly - no more catalog-name lookup.
- **`Display/SystemScreen.cs`** / **`UI/NavScreen.cs`**: `Select`/`OnMapClicked` no longer gate
  `Game.State.SetTarget` on `tr.info.identified` - a track becomes targetable as soon as it's selected;
  `TargetBodyName` is now purely a DISPLAY label (the track's own name, not a catalog match) and is never
  looked up again by the planner. `Core/GameState.cs`'s doc comment on `TargetBodyName` updated to say so.
- **Not changed**: the catalogue layer (`Core/Catalogue.cs`, roadmap item 2) still reads `NodeData` via
  `CollectOrbitsAroundPrimary` - that's a different, intentionally-scoped feature (catalogued Sol bodies are
  genuinely pre-known before the expedition, per `SolSystem`'s own doc comment), not the same "cheat" as
  looking up an unidentified contact's true orbit. Clicking a catalogue marker still doesn't select anything;
  only tracks are targetable, on purpose (two-knowledge-tiers principle).

**Not compile-checked** (same batchmode/license-lock issue noted throughout this file). Numerically this is
low-risk (every formula reused is already exercised elsewhere - `StateToElements` by `ShipOrbit`, `NodeState`
by `ShipOrbit`/`FindPrimary`, the Hohmann geometry by the existing `SolveHohmann`), but the frame-subtraction
in `OrbitFit.TryFit` (primary position/velocity subtracted from the track's system-frame estimate) is new
combination of existing pieces and deserves a specific in-Editor check: track something with an obvious orbit
(a Sol planet, tracked and ranged but NOT identified), open NAV, and confirm the TRANSFER section's Δv/ToF
numbers land in a sane ballpark before trusting CREATE NODES.

**Known gaps from this pass:**
- `DevHud`'s "Set NAV target" debug button (`Core/DevHud.cs`) only sets the display-only `TargetBodyName` now
  - it never drove `Tracks.SelectedId`, so it was already a display-only shortcut in spirit; now it's also
    functionally inert for actually plotting a transfer (the planner needs a selected TRACK, not a name). Low
    priority (dev-only tool), but worth wiring to `Tracks.SelectedId` too if DevHud's transfer-testing flow is
    used often.
- `OrbitFit`'s "no blending across samples" simplification (see its own doc comment) means a very long,
  uncorrected track will eventually see its fit quality degrade again as the target's own orbital curvature
  breaks the TMA's straight-line-target assumption, rather than continuing to sharpen forever. Not wrong, but
  worth confirming the degradation is graceful (`Observable` just goes false) rather than producing a
  confidently-wrong orbit.
- Target orbit fit has no uncertainty visualization yet (the roadmap's "dashed, widening with sigma" ellipse
  from item 6) - CREATE NODES uses the point estimate only. The NAV map doesn't draw the fitted ellipse at
  all yet, only the sidebar's numbers.

## Progress (this session - three bugs reported from an actual play session)

- **NAV sidebar TRANSFER text overlap:** `UI/NavScreen.cs`'s TARGET/PHASE/WINDOW/dV rows (and `_incl`)
  are built with EMPTY text at `Build()` time (`RefreshTransfer`/`Refresh` fill them in later); a plain
  `TextMeshProUGUI` with no text reports 0 `preferredHeight` to its `VerticalLayoutGroup`, and that 0 doesn't
  reliably get corrected once real text lands, unlike `OrbitPanel`'s labels (which sit on a node that also
  carries its own `ContentSizeFitter` - that's what forces ITS re-layout on every text change; this sidebar's
  `inner` VStack has no such component). Symptom: every one of those rows rendered on top of its neighbour.
  Fixed by giving each of those labels an explicit `minHeight` via `UIKit.Size`, so their row height no
  longer depends on TMA's on-demand preferred-size timing at all. Scoped to the labels actually reported
  broken (plus `_incl`, same latent shape); worth keeping in mind as a general trap for any OTHER
  empty-at-build, VStack-only (no ContentSizeFitter) label added later in this codebase.
- **ARM silently clobbering a plotted transfer:** `ManeuverPlan` has no "planned vs armed" distinction -
  `SetPair`/`SetSingle` both write directly into the one live queue `Tick()` auto-fires from the instant sim
  time reaches it (see the class's own doc comment: queuing IS arming). `NavScreen.CreateTransferNodes` and
  `NodePanel.PlotTransfer` both already call `SetPair` themselves, so a plotted transfer is armed the moment
  it's created - no extra step needed. `NodePanel.Arm()` is a SEPARATE affordance (hand-set prograde/normal
  steppers, both default 0) writing to that same queue via `SetSingle`; pressing it after already plotting a
  transfer, without having touched the steppers, silently replaced the real two-burn plan with a burn that
  does nothing (`ManeuverPlan.Execute` already no-ops below its dv floor) - the node still gets popped off the
  queue when its time arrives, so it looked exactly like "the burn doesn't get executed". Fixed by refusing to
  arm a zero-dv node in `NodePanel.Arm()`; also relabelled `CREATE NODES` -> `CREATE + ARM NODES` and
  `PLOT TRANSFER` -> `PLOT + ARM TRANSFER`, and added a hint line under NodePanel's own ARM row spelling out
  that it queues a SEPARATE hand-set burn, not a confirm step for what's already plotted. The user's ask (an
  actual PLANNED-vs-ARMED state, so nothing fires until an explicit confirm) is a bigger `ManeuverPlan`
  change, not done here - flagging it as a real design question for a later pass, not just a UI polish one.
- **Waterfall "ghost contact" at high time-warp with a fast-drifting body:** `WaterfallProcessor.Tick`'s
  cadence (`wait = max(minUpdateInterval, lineIntervalSeconds / warp)`) already scales with warp, but its
  `Integrate()` window (`spec.maxIntegration` lines) didn't - past `wait`'s floor, every extra generated line
  represents `minUpdateInterval * warp` more SIM seconds, unbounded as warp climbs. A body sweeping fast in
  world bearing (own orbital motion near a primary counts, not just a moving target - exactly the close-orbit
  transfer scenario this session's earlier `OrbitFit` work is built around) then gets smeared, while
  integrated, across enough bins for CFAR (`Tracking/Detector.cs`) to read the smear as more than one local
  maximum. Fixed by capping the integrator at `spec.maxIntegration / warp` lines instead of a flat count, so
  the SIM-TIME SPAN it covers stays close to warp-1 behaviour instead of growing with warp; integration gain
  drops off at high warp as a direct consequence (matches a real sensor - you can't usefully integrate a fast
  sweep by staring at it for a simulated eternity). `WaterfallProcessor.GenerateLine`/`Integrate` both now take
  `warp` as a parameter instead of reading it implicitly.

**Not compile-checked**, same as everything else in this file. The waterfall fix in particular is worth a
specific in-Editor check: warp up while tracking something with a fast-changing bearing (a close lunar
transfer is exactly the repro) and confirm only one track/contact appears, not a duplicate.

## Progress (this session - NAV map clipping/pan, radar close-range scale, countdown formatting)

- **NAV map overflow:** `UI/NavScreen.cs`'s `_mapRect` had no clipping - a catalogue orbit far bigger than
  whatever the ship's own orbit had auto-fit the zoom to (a distant planet's orbit next to a sub-1AU ship
  orbit, screenshotted) rendered straight through the sidebar and up over the topbar. Added a `RectMask2D` on
  `_mapRect`. `MapCanvas` itself moved to a new child node (`Canvas`) because Unity's clip search starts at a
  Graphic's PARENT - a mask on the graphic's own GameObject doesn't clip it; the marker labels and the
  `OrbitPanel`/`TrackOrbitPanel` corner overlays, already direct children of `_mapRect`, needed no such move.
  Also added drag-to-pan (`_panOffsetPx`, reset on `OnShown()`/re-fit) on the same request.
- **Radar close-range scale:** `rangeScalesAu`'s old spread (5/20/60/200 AU) bottomed out useless at lunar
  distance (~0.0026 AU) - the return sat dead-centre with no usable range-bin resolution, and the result
  readout (`ui.radar.result.rangerate.brg`, hardcoded `{0:F2} AU`) rounded anything under ~0.005 AU to
  "0.00 AU" regardless. This is very likely the actual explanation for "radar ranging didn't help refine the
  moon distance" - the fix may well have been fine, just unreadable. `RadarSpec.rangeScalesAu`'s default now
  spans ~1,000 km up through 200 AU (11 tiers); `RadarScreen`'s scale row changed from one button per tier
  (would never have fit that many) to a -/+ stepper; both the tier label and the fire-result readout now go
  through `Loc.Distance` (already existed, auto-switches km/AU at 0.01 AU) instead of a flat `F2 AU`.
- **Countdown precision:** `ui.node.queue` ("Node in X.X d") and `ui.nav.transfer.window.wait` ("WINDOW IN
  X.X d") both only had one decimal day of resolution - useless for actually watching a countdown to a burn
  in its last seconds, per direct complaint. Added `Loc.Countdown(seconds)` (`D:HH:MM:SS`, day segment
  dropped under 24h) and switched both to it.
- **Not investigated - needs a repro, not a guess:** "the last manoeuver sent me orbiting the sun." Read
  through `ManeuverPlan.SolveHohmann`/`SolveHohmannGeometry` looking for a sign/unit bug and didn't find an
  obvious one; the far more likely explanation is the intended consequence of this session's own earlier
  change (`OrbitFit`/no-NodeData rework) - the screenshotted target fit had e=0.951, a genuinely poor/early
  TMA fit, and `SolveHohmann` treats the target as CIRCULAR at its fitted semi-major axis, which is a bad
  approximation for a fit that eccentric. That combination could plausibly produce a burn large/wrong enough
  to eject the ship from Earth orbit entirely - which is exactly "uncertainty has teeth," the explicit design
  goal from a few messages earlier in this same session. Flagging rather than guess-patching orbital math with
  no way to test it: worth deciding whether that's working as intended (get a better fix before committing to
  CREATE NODES) or whether the planner should refuse/warn below some fit-quality threshold.
