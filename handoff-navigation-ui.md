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

## Progress (this session - radar TRACK-mode ETA cheat, Atlas RECALL / ghost contacts)

- **Radar TRACK ETA cheat:** `RadarProcessor.FireTrack` computed its pending-ping ETA from
  `FindNearestInBeam`'s own result (`found`/`rangeAu`) - an omniscient "what's actually in the beam" lookup,
  leaking both whether anything was there and roughly how far before the ping had a chance to tell the player
  anything. Sweep never did this (always the selected max range - already correct, per the user's own stated
  spec). Fixed: TRACK's ETA now uses the track's own current range ESTIMATE (`tr.range`, TMA or a prior radar
  fix) when it has one, and falls back to the full instrumented max range otherwise - matching sweep. Only the
  wait time the player is told changes; `Resolve()`'s actual hit/miss/range is unchanged, still read from the
  true beam contents at fire time (that's the sensor doing its job, not the cheat).

- **Atlas RECALL (ghost contacts):** a body already on file (Atlas) can be recalled as a fresh, aimable
  Tentative track in the current run - new `Tracking/GhostContact.cs`, a RECALL button on `AtlasPanel`'s BODY
  page. Two confidence levels, deliberately NOT the same:
  - **Catalogued bodies** (a full `NodeData.orbit` on file): `GhostContact.FromCatalogued` uses the same
    analytic Kepler propagation `ShipOrbit`/`OrbitFit` already use (`OrbitalMechanics.NodeState`) - the TRUE
    current bearing/elevation/range/range-rate, not a guess, because a catalogued orbit genuinely isn't one.
  - **Any other survey entry:** `AtlasEntry` gained `recordedBearing`/`recordedRange` (carried over from the
    source `DataRecord.bearing`/`.range` in `Atlas.Log()`, both the new-entry and merge paths - freshest fix
    wins, same rule already used for `recordedTime`). `GhostContact.FromEntry` coasts that old position+
    velocity fix forward to now at constant velocity - the SAME straight-line model TMA itself assumes - with
    no elevation (never measured) and a sigma that widens with how stale the fix is (+5%/day, uncapped).
  - Neither path calls `ApplySupport`: that would artificially confirm a lock the player hasn't actually
    re-acquired. The seeded track stays Tentative (red/searching) until a real sensor finds something there -
    `SensorSight` is never touched either (player-facing code must not call it; the geometry is a small
    duplicated bearing/elevation formula against `Game.State.Ship`'s own known position).
  - AtlasPanel's BODY page now tracks a RECALL target every `Refresh()` (`BuildBody` sets
    `_recallData`/`_recallNodeIndex` for the precise path or `_recallEntry` for the rough one), shows which
    kind is available (or that neither is, for a body with no position fix on file at all - only ever true for
    an entry logged before this change, or a bearing-only claim), and selects the new track
    (`Tracks.SelectedId`) on success so the player can jump straight to a sensor's SEL.
  - `ElevationSource` gained an `Atlas` value (the elevation-fix source for a catalogued recall).

**Not compile-checked**, same caveat as everywhere else in this file. `GhostContact`'s unit handling is worth
a specific look: `OrbitalMechanics.NodeState`'s velocity is game-units/simSecond, `ShipState.vx/vy/vz` is
km/s against that same sim-second clock (per `ManeuverPlan.Execute`, burns add straight in with no extra
rate scaling) - the conversion there is spatial-only (`* ShipState.KmPerUnit`), mirroring exactly how
`OrbitFit.TryFit` already converts `RangeEstimate.vxKmS`/`.vzKmS` the other way (`/ KmPerUnit`). Worth
confirming a RECALLed catalogued body's seeded range-rate actually reads sane in the UI before trusting it.

## Progress (this session - waterfall corrections keep history, radar velocity feeds the orbit solver)

- **Waterfall CORRECT keeps history:** `WaterfallScreen`'s click-to-mark, when a track is SELECTED, used to
  call `TrackManager.Retarget` - which wiped everything (history, rate, range, elevation, radar fix, identity)
  and started the track over as freshly Tentative. Per the user: a bearing correction on an existing contact
  should be a CORRECTION, not a new contact - prior sensor work (radar ranges, spectrometry, imager fixes,
  the fitted rate) shouldn't evaporate every time the player nudges the bearing. `Retarget` is gone, replaced
  by `TrackManager.CorrectBearing`, which builds a synthetic `Detection` from the click and runs it through
  the SAME `ApplyHit` path a real waterfall/imager/radar detection uses - the correction becomes one more
  weighted sample in the track's continuing history (feeding TMA/the rate fit exactly like a real hit would),
  not a wholesale reset. A correction built on bad data just becomes noise the history-weighted fits wash out
  over time, same principle the user described; Drop + mark fresh is still there if it never converges. The
  button/hint text changed MOVE -> CORRECT to say what it now actually does.

- **Radar velocity into the orbit solver:** `OrbitFit.TryFit` converts whatever `tr.range` (`BestRange`)
  currently is into a full state vector - position AND velocity - for `StateToElements`. `ApplyRadarFix` was
  never filling in the radar fix's own `vxKmS`/`vzKmS` at all (always 0,0), so whenever `BestRange` picked the
  radar fix over TMA (tighter sigma, which a good ping usually is), OrbitFit silently got handed "not moving
  relative to the primary" - a badly wrong velocity for anything actually orbiting something, discarding all
  of radar's own precision instead of using it. This is very likely part of why the earlier "last manoeuvre
  sent me orbiting the sun" report happened, not just a missing feature.
  - Fixed: a TRACK-mode ping's own `hasRate` (radial/line-of-sight Doppler, near-exact) now replaces the
    along-LOS component of whatever velocity TMA's own bearing-history fit already had (if any) - radar can't
    measure the TANGENTIAL component at all, so that part still has to come from TMA when it exists; with no
    TMA velocity on file yet, the fix is radial-only (still strictly better than zero).
  - Also fixed a related bug in `AgedRadarFix`: it already aged `range` forward using the measured range rate,
    but left `x`/`z` pinned at fire time - a stale position paired with an aged range described two different
    moments at once. Now `x`/`z` age forward too, using the (now-populated) velocity.

**Not compile-checked**, same caveat as everywhere else in this file. Both changes are worth a specific
in-Editor look: does a waterfall CORRECT on a track with an existing radar fix/identity visibly keep them (no
more red-flash-back-to-searching-with-nothing), and does ranging a track with radar now visibly tighten
(not distort) NAV's TARGET ORBIT panel compared to before.

## Progress (this session - Atlas records carry the determined orbit, not just a bearing/range)

Per the user: raw survey data should also carry whatever orbit `OrbitFit` managed to determine while it was
being captured, so a later RECALL can predict a body's position at ANY future time from real orbital
mechanics, not just a straight-line coast that only stays honest briefly. Stubs deliberately don't get this
(too short-lived to be worth it) - and structurally can't, since the hook only runs on an active raw recording.

- **`OrbitFit.TryFit` gained an overload** that also outputs the primary's name (`NodeData.name`, the same
  stable identifier `AtlasPanel`/`GetSystem` already look bodies up by) - a raw `OrbitElements` on its own
  doesn't say what it's centred on, and a caller that PERSISTS the fit (unlike NavScreen/NodePanel, which use
  it immediately against the ship's current primary) needs to remember that too. The original single-out
  overload is now a thin wrapper; none of its four existing call sites changed.
- **`DataRecord` and `AtlasEntry` both gained `hasOrbit`/`orbit`/`orbitPrimaryName`.** `DataStore.OnSensorLine`
  (runs per waterfall line for an active RAW recording only - never a stub) calls the new `TryFit` overload
  every line and keeps whichever fit last succeeded - it sharpens for free the same way `OrbitFit` already
  does, per its own doc comment. `Atlas.Log()` carries it into the delivered entry on both the new-entry and
  merge paths; on merge, an orbit is only ever REPLACED by a newer one, never cleared by a later record that
  didn't happen to have one (unlike `recordedBearing`/`recordedRange`, which always take the freshest
  regardless - an orbit fit is rarer and more valuable, so a merge shouldn't downgrade it).
- **`GhostContact.FromEntry` now prefers the determined orbit when there is one** (`FromEntryOrbit`, new
  private method): propagates `AtlasEntry.orbit` to now with `KeplerOrbit.StateAt` relative to
  `orbitPrimaryName`'s CURRENT position (`OrbitalMechanics.NodeState`, looked up by name in the CURRENT
  system) - the exact same mechanism `FromCatalogued` already used, just off a fitted orbit instead of a
  known-true one, so it gets a small nonzero sigma (2%) instead of the catalogued case's near-zero (0.1%).
  Falls back to the old straight-line coast (`FromEntryCoast`, renamed from the previous `FromEntry` body)
  when there's no orbit on file, or the named primary isn't in the current system at all.
- **New guard, found while wiring this up:** `AtlasPanel.BuildBody` now only offers RECALL (any of the three
  kinds) while the ship is actually IN the entry's own system (`SystemManager.Current.CurrentSystemID`) - a
  bearing/range computed against the ship's CURRENT position means nothing for a body in a system the ship
  isn't in. This was already a real gap in last session's RECALL work (both the catalogued and coast paths
  used the ship's live position unconditionally); worth calling out since it wasn't caught at the time.
- `AtlasPanel`'s RECALL button now shows three tiers: `RECALL (known orbit)` (catalogued), `RECALL
  (determined orbit)` (a survey entry with `hasOrbit`), `RECALL (rough, aged fix)` (coast-only), `RECALL (no
  fix on file)` (disabled). `BuildBody`'s own `bestFix` selection now prefers ANY orbit-bearing entry over a
  fresher one without, before falling back to "most recent" as the tiebreaker.

**Not compile-checked**, same caveat as everywhere else in this file. Worth an in-Editor look specifically at:
a long raw recording's RECALL eventually offering "determined orbit" instead of "rough, aged fix" once
`OrbitFit` converges; and that an orbit-based recall's predicted bearing actually tracks a body correctly
across a real time skip (warp forward, then RECALL again and see if the new prediction still points at it).

## Progress (this session - a locked track's position can no longer drift off its own measured bearing)

User-reported, with screenshots: NAV plotted the Moon roughly 60 degrees away from its true position after a
time-warp jump, while the waterfall's own bearing line for it barely moved and stayed accurate throughout -
the track was never lost, so nothing should have been free to drift that far. Root cause: `TrackManager.
BestRange` returns whichever of TMA's fit or the (now velocity-aware, see the radar-into-the-orbit-solver
entry above) aged radar fix has the tighter sigma, and NEITHER of those `(x, z)` points was ever checked
against the track's own CURRENT measured bearing - a straight-line coast (radar) or a fit off noisy history
(TMA) can end up pointing somewhere the sensor plainly isn't looking anymore, especially over a big warp jump,
since a genuinely orbiting target's real curvature breaks any straight-line extrapolation more the longer it
runs. The bearing itself is about the one thing measured fresh and essentially exactly every line; nothing
was using that fact to keep the position estimate honest.

Fixed directly per the user's own framing ("constrain bearing/ranges to measured values ... should not allow
such deviation while the contact is tracked"): `BestRange` now takes the ship's position and, whenever the
track is ACTIVELY locked (`Confirmed`, `consecutiveMisses == 0` - a hit landed this exact update, not
coasting on a miss), pins the returned estimate's `(x, z)` onto the ray from the ship at the track's live
`bearing`, at whatever range magnitude the fit/fusion produced. Range and velocity are left alone - bearing is
the one quantity actually being re-measured every line, so it's the one worth trusting absolutely while the
lock holds. Between hits (miss-coasting, or a lost/searching track) the pin doesn't reapply, so any drift is
now capped at "since the last hit", not compounded across an entire warp session.

`BestRange` gained `shipX`/`shipZ` parameters; all 6 call sites (5 in `TrackManager` itself, `DevHud`'s debug
overlay) updated to pass them through - every one of them already had the ship's position in scope or (DevHud,
a dev-only tool) reads it straight from `Game.State.Ship`.

**Not compile-checked**, same caveat as everywhere else in this file. Worth a specific in-Editor repro of the
exact reported scenario: lock a nearby body (Moon-from-Earth-orbit is the given repro), warp forward hard, and
confirm the NAV marker/TARGET ORBIT stay consistent with the waterfall's own bearing line throughout, not just
immediately after a hit.

## Progress (this session - roadmap item 6's uncertainty ellipse, drawn on the map)

Filled the gap flagged at the end of the earlier track-derived-orbit-fit pass: "Target orbit fit has no
uncertainty visualization yet... the NAV map doesn't draw the fitted ellipse at all yet, only the sidebar's
numbers." User picked this to work on next (while away from a testing environment) out of a short list.

- **`UI/NavScreen.cs`**: new `DrawTargetOrbitLayer` (called from `Draw()`, right after the track layer, under
  the live ship dot) draws the SELECTED track's fitted orbit (`OrbitFit.TryFit` - never NodeData, same rule as
  everywhere else) as a dashed ellipse in the track's own colour (`UITheme.WaterfallTrackColor`, same colour
  its dot/label already use). When the track's range has an actual sigma, two fainter dashed ellipses bracket
  it: the same shape with `semiMajorAxis` scaled by `(1 +/- sigmaFraction)` about the primary - a rough
  physical stand-in for positional uncertainty, not a real covariance propagation, but it satisfies what the
  roadmap actually asked for: the band visibly narrows as the range estimate tightens (more tracking time,
  a radar ping) and disappears once it's tight enough not to matter. New `DrawFittedEllipse` helper factors
  out the sample-and-polyline loop shared with the wide/narrow band, same pattern `DrawCatalogueLayer` already
  used for catalogued orbits.
- No orbit drawn at all under the exact same conditions `TrackOrbitPanel` already shows "No stable orbit" for
  (nothing selected, unranged, no primary in common, a degenerate fit) - the two can't disagree, since both
  gate on the same `OrbitFit.TryFit` call.

**Update, same session:** the deferred half got done too - `ManeuverPlan.Preview` gained an `elements` field
(the full `OrbitElements`, populated in `PreviewNode` alongside the summary numbers it already returned;
`NodePanel`'s existing callers are untouched, they just don't read the new field). `NavScreen.Draw()` gained
`DrawPredictedPathLayer`, called whenever `Game.State.Maneuver.Armed`: previews the NEXT queued node
(`ManeuverPlan.Next`) with the exact same `PreviewNode` call `NodePanel`'s own PERI/APO/e/i readout already
uses, and draws the resulting orbit as a dashed ellipse in `navNode` yellow (reuses `DrawFittedEllipse`, the
same helper the uncertainty-ellipse work above added) - so a queued node now visibly previews its own result
on the map, not just as sidebar numbers, satisfying roadmap item 3's "predicted path" alongside item 6's
uncertainty band. Own-ship data, so no fit/uncertainty band on this one - it's either armed or it isn't.

**Not compile-checked**, same caveat as everywhere else in this file. Worth an in-Editor look at: whether the
+/-sigma band ellipses are visually distinguishable from the central target-orbit fit at typical zoom (too
close together to read as a band vs. just a thicker line); whether `band.a *= 0.35f` reads as intended in both
themes; and whether the predicted-path ellipse and a selected track's own fitted-orbit ellipse stay visually
distinct when both are on screen at once (navNode yellow vs. whatever WaterfallTrackColor gave the track).

## Progress (this session - item 4, timeline strip)

User said "keep going down the roadmap" (away from a testing environment, working from the suggested-order
list). Scoped down to what's cheaply and honestly computable right now - see the new file's own doc comment
for exactly what's NOT generated yet (SOI changes, closest approach to a tracked target, comms windows, jump
windows - each needs its own prediction machinery this pass didn't build).

- **New `Core/NavEvents.cs`**: `NavEvent { time, kind, label }` + `Collect(List<NavEvent>)`, sorted soonest
  first. Generates the ship's own next periapsis/apoapsis passage (exact - `KeplerOrbit.MeanAnomalyDeg` against
  `ShipOrbit.Elements`, the same conic `OrbitPanel` already reads; skipped for a hyperbolic/unbound orbit,
  where Ap doesn't exist and Pe may already be behind the ship) and each queued `ManeuverPlan` node's burn
  time (`QueueForSave`, already public). Per the roadmap's own instruction ("one function producing the list,
  every consumer reads the same one") - the strip below is the only consumer so far, but alerts/map markers
  can read the exact same list later without new plumbing.
- **`UI/NavScreen.cs`** gained a fixed-height TIMELINE strip under the Map+Sidebar row (own outer-LayoutElement
  / inner-Stretch+VStack decoupling, same pattern as the sidebar and `GameShell`'s), up to 4 rows, each a
  countdown (`Loc.Countdown`, the `D:HH:MM:SS` format added a few commits back) + label + a WARP TO button.
  Rows past however many events exist are hidden, not blank. `RefreshTimeline` runs inside `Draw()`'s own
  throttled cadence, right where `RefreshTransfer` already runs.
- **WARP TO** does different things depending on the row's kind, both deliberately reusing existing machinery
  rather than inventing a second warp-management system: a BURN row calls `ManeuverPlan.StartWarpToNode()` -
  the exact same continuous, auto-drops-as-it-approaches warp the NODES tab's own WARP TO NODE button already
  does (targets the queue's own next node regardless of which burn row was clicked, since the plan fires
  strictly in order anyway - clicking a later burn just means transiting through the earlier one first, which
  is correct). A Pe/Ap row isn't an armed/continuous thing, so it gets a one-shot coarse jump instead, reusing
  `ManeuverPlan.PickWarp`'s own staged {1,10,30,60}x{s,m,h,d} ladder - made `public` (was `private`) rather
  than duplicated, since both are the same "coarse-to-fine warp for a countdown" concept. This only PARTIALLY
  satisfies the roadmap's "auto-drop warp before an event (a configurable margin) so the player doesn't skip
  a burn" framing - real continuous auto-drop only happens for burns (via the pre-existing mechanism), not for
  Pe/Ap passages, which was a deliberate scope call (an always-on auto-drop for EVERY event, including ones
  the player doesn't care about, seemed more likely to be annoying than helpful) rather than an oversight.

**Not compile-checked**, same caveat as everywhere else in this file. Worth an in-Editor look at: whether the
NAV tab still has enough vertical room for the map's own 600px minimum height now that the timeline strip
(~142px fixed) also claims space in the same column - should be fine at typical window sizes but wasn't
checked against a small one; and whether a timeline BURN row's WARP TO reliably lands the clock near the
right event when TWO nodes are queued (departure + arrival, the common case from CREATE NODES).

## Progress (this session - item 8's Δv budget widget)

Kept going down the roadmap's own suggested order after item 4. Picked the Δv budget widget specifically
over the rest of item 8 (Burn HUD, Alerts, hazard layer) and item 7 (galaxy map, blocked on an open design
question about whether galaxy coordinates exist yet) because it's the one piece buildable from data already
on hand with NO new unit-conversion risk - an "alerts" pass (periapsis below a body's surface, say) would
need a body's radius converted into the same game-units frame as orbital elements, and that conversion isn't
exercised anywhere else in the codebase yet to crib from; better to get it right later with a chance to
verify it than guess at it now with no way to test.

- **`Core/GameState.cs`** gained `MaxDvKmS`: `BurnCost` run backwards (`Hydrogen / Probe.hydrogenPerKmS`) -
  the dv the CURRENT hydrogen on hand could still buy, in one direction. Same formula `BurnCost`/`TryBurn`
  already use, just inverted, so there's no new conversion to get wrong.
- **`UI/NodePanel.cs`** gained a `dv budget {A} km/s   queued {B} km/s   margin {C} km/s` line under the
  existing queue readout: sums every queued node's `TotalDvKmS` (not just the next one - the WHOLE plan's
  cost) and compares it to `MaxDvKmS`. Text goes `t.danger`-red when the queue would cost more than the
  hydrogen on hand can pay for. A simple "if every queued burn fired right now" snapshot, not accounting for
  hydrogen regenerating (reactor/solar) between now and a later burn - matches the roadmap's own plain framing
  ("remaining Δv, minus each planned node's cost"), not a full mission-planning fuel budget.

**Not compile-checked**, same caveat as everywhere else in this file. Low risk relative to most of this
session's other changes (one new backwards-formula property, one new UI label, no geometry/physics touched),
but still worth a look: does the margin actually go red and stay legible against `t.panelColor` in both themes
when a plan is over budget.
