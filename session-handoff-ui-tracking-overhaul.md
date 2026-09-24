# Spaceship Bridge — Session Handoff: UI & Tracking-Centric Redesign

This handoff moves work from the last session into a new one. The last session was a world-generation deep dive: moons, atmospheres and surface temperature, planet sub-types, and star metallicity and age. The new session focuses on UI/UX and a design shift. Today the player can see everything in a system. After the redesign, the player sees only what the ship's sensors are tracking.

Start the new session by reading this doc. Then re-read the files named below before designing anything. This summary only scopes the work; it doesn't reproduce the files' current content.

## Where things stand

### World generation

Files: `Data/SystemFactory.cs`, `Data/SystemData.cs`, `Physics/AtmosphereModel.cs`, `Data/SpectralLineTable.cs`.

This part is complete and working. Everything is generated from one seed by `SystemFactory.Generate`:

- **Stars**: multiplicity, H-R position, metallicity ([Fe/H] in dex) and age (from the main-sequence lifetime relation t ≈ 10 Gyr × M^-2.5).
- **Planets**: type, mass, and equilibrium temperature. Atmospheres use real Jeans-escape retention. Surface temperature is greenhouse-adjusted with the grey-atmosphere formula Ts⁴ = Teq⁴(1 + 0.75τ). Each planet gets a surface class: Barren, Hellscape, Frozen, Temperate, Desert, Icy Rock, Frozen World, Ice Giant, Gas Giant, Hot Jupiter, or Belt.
- **Moons**: orbits are bounded by the parent planet's Hill sphere, spaced log-uniformly, and checked for collisions. Each moon gets its own atmosphere and surface class.

Every node (star, planet, moon, barycenter) is a `NodeData` in a flat list with a generic `parent` index.

### Orbital mechanics

Files: `Physics/OrbitalMechanics.cs`, `Physics/KeplerOrbit.cs`, `Core/ShipOrbit.cs`, `Gameplay/SystemManager.cs`, `Physics/CelestialBody.cs`, `Core/ManeuverPlan.cs`.

Keplerian mechanics run in AU, years and solar masses. Every spawned body carries a live `CelestialBody` MonoBehaviour. Every 0.1 s, for every body, it computes azimuth, elevation, distance, phase and apparent luminosity relative to the ship. It does this whether or not the body has been detected. `SystemManager.SpawnBody` copies all `NodeData` fields onto the component, including the new atmosphere, surface-class and star-diversity fields. Maneuver-node planning is done and has its own tab (`UI/NodePanel.cs`).

### Tracking, sensors and UI already on disk

These files exist but weren't read in the last session. Read them before designing anything:

- **Tracking**: `Tracking/Detector.cs`, `Tracking/RangeEstimator.cs`, `Tracking/TrackManager.cs`, `UI/TrackOverlay.cs`, `UI/TrackPanel.cs`. `Game.State.Tracks` is cleared on every system jump, so a real track pipeline already exists. Tracks are not a blank slate.
- **Radar**: `Display/RadarScreen.cs`, `Sensors/RadarProcessor.cs`, `Core/Sensors/RadarSpec.cs`.
- **Other sensors**: `Display/ImagerScreen.cs`, `Display/SpectrometerScreen.cs` (resolving-power line blending and dwell-based ID confidence), `Display/WaterfallScreen.cs` with `Sensors/WaterfallProcessor.cs`, and `Display/SensorConsole.cs` (switches between sensor modes).
- **Dev and run flow**: `Core/DevHud.cs` (an existing dev overlay), `Core/RunController.cs`, `UI/DebriefScreen.cs`, `UI/GameUI.cs`, `UI/StatusBar.cs`, `UI/SystemsDock.cs`, `UI/DraggablePanel.cs`.
- **Other panels**: `UI/AtlasPanel.cs`, `UI/JumpPanel.cs`, `UI/ManeuverPanel.cs`, `UI/OrbitPanel.cs`, `UI/ReactorPanel.cs`, `UI/TransmitPanel.cs`, `UI/WakePanel.cs`, `UI/DataPanel.cs`.

### What the system view shows today

`Display/SystemScreen.cs` is a row table: name, class, azimuth, elevation, distance. It is omniscient. It lists every `CelestialBody` returned by `FindObjectsByType`, with no filtering. Rows are clickable and set `Game.State.SetTarget`.

### Two gotchas

- **Theme files**: `UI/UITheme.cs` holds the C# defaults, but `Assets/Resources/UITheme.asset` is what actually loads at runtime. Any color or theme change must edit both.
- **No compile feedback**: work goes through the file bridge, with no Unity compile or play feedback. Every change has to be reasoned through by hand. Standard push sequence:
  1. Get fresh mtimes with `device_list_dir`.
  2. Write the file under outputs.
  3. Call `SendUserFile`.
  4. Call `device_commit_files` with `expectedMtimeMs`.
  5. Re-list the folder and compare file sizes. An upload has already been cut off partway once.

## Requested improvements — roadmap

The six items are ordered below by dependency. Each builds on groundwork from the one before. This is not necessarily the order to tackle them in.

### 1. Consolidate tracks as the single source of truth

`TrackManager`, `Detector` and `RangeEstimator` already exist. The work here is to audit them, not to invent a track system:

- What creates a track (passive detection, active radar, both)?
- How are tracks characterised over time (bearing only, then range, then identity)?
- Is `CelestialBody.apparentLuminosity` feeding the detection thresholds?

Every later item should read from the track list instead of from `FindObjectsByType<CelestialBody>()`.

### 2. Radar display: real bearing/range contacts

`RadarScreen`, `RadarProcessor` and `RadarSpec` exist. Per the request, radar currently gives range but not a real bearing-and-range contact picture. The goal is an active sweep that produces bearing/range returns, which then create or update tracks in `TrackManager`. A track should fade and age when it isn't re-detected. It should not be a perfect live readout.

For consistency, reuse the signal-processing patterns already in `WaterfallProcessor`: PSF spread, CFAR-style detection and a noise floor.

### 3. System view: tracked-object data, not a bird's-eye view

Swap `SystemScreen.RefreshBodies()` from "every body" to "every track." Then define what a fresh contact shows compared with a well-characterised one:

- **New contact**: bearing and range right away.
- **After dwell**: composition, surface class and atmosphere only once spectrometer dwell has built enough confidence.

This follows the same dwell-to-confidence idea the spectrometer overlay already uses.

### 4. Imager and spectrometer: tracked objects only

`ImagerScreen` and `SpectrometerScreen` should only accept a valid, current track as a target. You can't image or take a spectrum of something the sensors haven't picked up. Once item 1 is the single source of truth, this is mostly a gating change.

### 5. Game menu, general settings, new-game settings

This is mostly independent of the tracking items. It needs three pieces:

- **Menus**: a main menu and a pause menu.
- **Settings**: UI theme and readability, audio if any, default time multiplier, and key bindings.
- **New-game flow**: choose or enter a world seed. `SystemManager.SetBaseSeed` and `GameConstants.DEFAULT_BASE_SEED` are the existing hooks.

Check `RunController`, `GameUI` and `DebriefScreen` first. Part of a run lifecycle already exists (`Game.Run.BeginRun`, `LaunchRequested`), and the menu should plug into it rather than work around it.

### 6. Dev tool overlay: omniscient system view and body inspector

`Core/DevHud.cs` already exists. Extend it rather than starting a new overlay. Once item 3 makes the normal view tracking-only, keep the old omniscient table here, behind a shortcut or console command. Add an inspector that shows the full generated `NodeData` for any body: composition, atmosphere, surface class, metallicity and age, and orbital elements. Build this last, because its purpose is to keep access to the pre-tracking behaviour.

## Suggested first steps for the new session

1. Read the tracking pipeline (`Tracking/*`, `UI/TrackOverlay.cs`, `UI/TrackPanel.cs`), the radar pipeline (`RadarScreen`, `RadarProcessor`, `RadarSpec`), `Core/DevHud.cs`, `Core/RunController.cs`, `UI/SystemsDock.cs` and `Display/SensorConsole.cs`. Much of the foundation is already there.
2. Confirm with the user which item to start with. Items 1–4 are one connected chain. Items 5 (menus and settings) and 6 (dev overlay) are independent and would give a quicker visible win.

## Progress: items 1–4 done (2026-09-23 session)

Design decisions (confirmed with the user):
- **Track → body link**: there is none. Sensors aim down a track's bearing and see whatever is physically there. Identity comes only from spectrometer dwell, written into `Track.info` (`TrackInfo`).
- **Radar sweep**: it produces bearing/range returns that fade on the scope. The player clicks a return to mark a track, and the track's range is seeded from it. Tracks are still player-marked only. A sweep return that falls inside an existing track's gate also refreshes that track's range.

What changed (all compile-checked with csc against the project's Unity 6000.5 DLLs; the only error is the pre-existing editor-only `EditorUtility` in `generateCrosshair.cs`):
- `Tracking/SensorSight.cs` (new): the only place sensors query real bodies. It caches the body list once per frame. It also provides world azimuth, ship yaw, range and cone queries. Player UI must not call it.
- `Tracking/TrackManager.cs`: `Track` now has `tmaRange`, `radarFix`/`radarFixTime`/`radarRangeRateKmS`, and `info` (a `TrackInfo`). `range` is now the best fused estimate. `BestRange` ages the radar fix by 1%/day plus the known range rate and compares it with TMA, which counts only when observable. The best range is recomputed every waterfall line. New members: `ApplyRadarFix`, `AgedRadarFix`, `LevelOf` (Bearing/Rate/Ranged/Identified), and `Track.Locked`.
- `Sensors/RadarProcessor.cs`: a sweep builds a bearing×range grid (1° cells × 512 bins over the selected scale). The pipeline is Gaussian noise, then R⁻⁴ echoes scaled by radius²·albedo, then a CA-CFAR along range with guard cells, then 3×3 local-max picking with sub-cell interpolation, producing `RadarReturn`s. Returns are revealed as their round-trip time elapses. The track ping now writes a radar fix instead of overwriting `range`. Returns clear when `Tracks.Generation` changes.
- `Core/Sensors/RadarSpec.cs`: new sweep fields, all with defaults: `sweepCellDeg`, `rangeBins`, `rangeScalesAu`, `detectionThresholdSigma` (5.0, about 0.3 false returns per 60° sweep, measured), `referenceSnr`, `referenceRangeAu` and `returnPersistenceDays`.
- `Display/RadarScreen.cs`: a PPI scope (440 px, north up) with range rings, the aimed sector, and an echo-horizon arc while a ping is out. It draws fading returns, and track lines with a range dot and a σ bar. Clicking a return marks it as a track (or selects the track already marked from it). A click elsewhere aims the sweep in SWEEP mode, or selects the nearest track line in TRACK mode. The range-scale buttons come from the spec.
- `Display/SystemScreen.cs`: now lists tracks only. Columns: TRACK / STATUS / BRG / RANGE±σ / CLASS / DETAIL, plus a detail block for the selected track. Clicking a row selects the track. It sets the NODE target only once the track is identified, using `info.catalogName`.
- `Display/SpectrometerScreen.cs` and `SpectrometerSpec.slitHalfWidthDeg` (0.5°): the spectrometer locks only onto Confirmed tracks (`<`/`>` to cycle, `SEL` for the selected track). It collects the bodies inside the slit cone, and several in the cone produce a luminosity-weighted blend. Dwell accumulates on `TrackInfo`. At identify time it writes class, composition, atmosphere, temperatures and stellar data. If the signature in the slit changes, the ID resets.
- `Display/ImagerScreen.cs`: aim modes are FREE AIM or a Confirmed track (`SEL`, `<`/`>`). Azimuth is slaved to the track bearing. Elevation stays where it is, because tracks carry no elevation. Rendering uses `SensorSight`.
- `Sensors/WaterfallProcessor.cs`: bodies now come from `SensorSight` (no behaviour change).
- `UI/Loc.cs`: new radar, imager, spectrometer and system strings.

Known gaps / next steps:
- The old omniscient system table is gone from the player UI. Item 6 (dev overlay) should bring it back in `DevHud`.
- The imager has no elevation control. A track gives azimuth only, so bodies well off the ecliptic may fall outside the frame at narrow FOVs.
- Remaining: item 5 (menus, settings, new-game seed flow), then item 6.
- Needs a play test: radar SNR balance (`referenceSnr` 40 at 10 AU for an Earth-sized body), scope readability, and how the spectrometer ID feels.

## Manual X/Y sensor aiming (same session)

User decisions: the waterfall aims by elevation tilt only (X is a cursor); a track's elevation comes from where the sensor was aimed, with the imager FIX giving the fine value; controls are steppers, click/drag, and arrow keys (Shift = fine). The spectrometer has no manual aim: it follows its track in X and Y.

- **Convention**: world bearing (0 = +Z, clockwise), elevation + up, geometric (`SensorSight.WorldElevation`), NOT the negated `CelestialBody.elevation` field. The imager now renders in the world frame (it previously used ship-relative azimuth and the negated elevation, so its vertical axis was flipped).
- **Tracks**: `hasElevation`, `elevationDeg`, `elevationSigmaDeg`, `elevationFixTime`, `elevationSource` (Waterfall/Radar/Imager). `TrackManager.ApplyElevationFix` only replaces the estimate with one that is at least as good as the current one has aged to (`ElevationAgeDegPerDay` = 0.25). `AgedElevationSigma(tr, t)`.
- **Waterfall**: `WaterfallSpec.fanHalfWidthElDeg` (15) and `maxTiltDeg` (80). `WaterfallProcessor.AimElevationDeg` weights each body by `SensorSight.BeamGain`, which is Gaussian with half power at the half-width. Detections carry elevation = tilt, σ = 0.6 × fan half-width. Screen: TILT −/+, Up/Down keys, vertical drag on the image. There is also a bearing cursor (Left/Right keys, drawn in amber), and Enter or MARK marks a track there. Clicking still marks.
- **Radar**: `RadarSpec.sweepElevationHalfWidthDeg` (5) and `maxTiltDeg` (85). A sweep fires at the manual elevation (two-way gain = gain²), and returns carry elevation = aim, σ = 0.6 × half-width. A TRACK ping points at the track's elevation over ±max(trackBeamDeg, 2σ) (the manual elevation plus the sweep fan if the track has none) and writes an elevation fix σ = 0.6 × that half-width. Screen: EL −/+, Up/Down; Left/Right and dragging round the scope swing the sweep bearing.
- **Imager**: FREE AIM (AZ/EL steppers at FOV/10, arrow keys at FOV/2 per second, drag to pan, click to recentre) or slaved to a track. Azimuth follows the track; elevation follows only if the track's σ ≤ ¼ of the vertical half-FOV, otherwise it stays manual so you can nod along the bearing line. A manual move clears the integrator. **FIX** takes the brightest integrated block within 12% of the FOV of the crosshair (signal ≥ 0.1, bearing within max(1°, 3 blocks) of the track) and writes an elevation fix with σ = half a block. An exposure floor of 0.1 (about 20× the block noise) keeps empty sky dark.
- **Spectrometer**: the slit is a great-circle cone of `slitHalfWidthDeg` around (track bearing, track elevation). It refuses to integrate with no elevation, or with σ > `maxPointingSigmaDeg` (1°).
- **New files**: `UI/PointerAim.cs` (click-vs-drag, local coordinates) and `UI/AimKeys.cs` (Input System arrow keys and Enter, silent while a TMP input field has focus).
- **Side effect**: dragging on the waterfall image, radar scope or imager image now aims instead of moving the sensor panel. Drag the panel by its border or tab row.
- **Intended player loop**: waterfall → mark → lock (coarse elevation from the tilt) → imager slaved, nod the elevation until the contact is in frame, FIX (or a radar TRACK ping) → spectrometer.

## Follow-ups: locking, re-ping, waterfall zoom, orbit precision (same session)

- **Faint contacts**: a track can be confirmed by imager FIX or radar support, not only by waterfall lock (`TrackManager.ApplySupport`, `SupportHoldDays` = 10, shown as HOLD). Clicking the waterfall with a track selected moves that track (`Retarget`) instead of creating one; clicking the selected track's button again deselects it.
- **Spectrometer**: activating its tab picks the selected track (`SpectrometerScreen.Show()`).
- **Radar**: re-ping cancels the pending ping (`RadarProcessor.CancelPending`).
- **Waterfall**: visual zoom of the bitmap only (processing keeps the full span; `WaterfallView.cs`, texture wrap Repeat), azimuth scale on the X axis. Spectrometer has a wavelength axis.
- **Prefabs**: `PlanetPrefab`/`StarPrefab` script GUID fixed (missing-script warning).
- **Orbits**: rewritten in double precision (`Physics/Vec3d.cs`): conic propagation re-resolved only on a burn, a primary change or a reset; analytic node velocities (`KeplerOrbit.StateAt`, `OrbitalMechanics.NodeState`). Validated against RK4. Fixes the sudden instability / frame jump.

## Item 5: menus, settings, new game (same session)

User decisions: the game starts in a main menu over a paused world; NEW GAME resets everything (seed, clock, run number, atlas, trust, tracks, orbit); settings cover UI scale, text size, colour theme, time defaults and key bindings; only settings persist (no game save yet).

- **`Core/Settings.cs` (new)**: `SettingsData` (JSON in PlayerPrefs `settings.v1`, sanitized on load, missing fields keep defaults), `Settings.Data/Save/Changed`. `GameAction` enum (aim ×4, Fine, Confirm, Pause, WarpUp, WarpDown, Menu), two key slots each. `InputMap.Held/Pressed` read the bindings and go silent while `Blocked` (any menu open) or a TMP input has focus; `MenuPressedRaw` ignores Blocked.
- **`UI/MenuUI.cs` (new)**: Main (NEW GAME / SETTINGS / QUIT), New Game (seed field, RANDOM, START), Pause (RESUME / SETTINGS / NEW GAME / MAIN MENU / QUIT, the last two confirmed), Settings with DISPLAY / TIME / CONTROLS tabs. Changes apply and save immediately. Rebinding: click a slot, press a key (Esc cancels, Backspace clears); a key taken elsewhere is removed from its old action. `MenuState` lives in GameUI so the page survives a rebuild.
- **`UI/GameUI.cs`**: panels are rebuilt from scratch (`RebuildUI`) when theme or text size changes (screens capture colours at build). UI scale only changes `CanvasScaler.referenceResolution`. Hotkeys: Menu opens the pause menu / goes back; Pause, WarpUp, WarpDown in flight. Button selection is dropped every frame so Space/Enter never re-click a button. Focus loss pauses (if enabled) and focus return resumes only if it was that pause.
- **`UI/UITheme.cs`**: `Current` is a runtime clone of the asset; presets Phosphor (asset as-is), Amber, Cyan, HighContrast; text size scales the three font sizes by 0.85 / 1 / 1.2. The asset itself is never modified.
- **`Core/GameClock.cs`**: 13-rung `WarpLadder` (1 s/s … 60 d/s), `StepWarp`, `SetWarpLadder`, `WarpLabel`. `RunController.BeginRun` applies the default warp and start-paused setting.
- **New game flow**: `Game.Boot` ends paused; `SystemManager.Start` no longer launches a run. `Game.NewGame(seed)` sets the seed (also PlayerPrefs `BaseSeed`), clock to 0, `GameState.ResetWorld` (atlas, trust, tracks, orbit, maneuver, target), `Run.ResetForNewGame`, then `BeginRun`. `Game.ReturnToMainMenu` = `Run.Abandon` (Idle, paused, no debrief). Debrief gained a MAIN MENU button. `Game.Quit` stops play mode in the editor.
- **Known gaps**: the status-bar warp buttons still use their own 4 presets (they coexist with the ladder). No game save.

## Item 6: dev overlay (same session)

- **`Core/DevHud.cs`** (rewritten, IMGUI): added by `Game.Boot` when `Debug.isDebugBuild` (editor and dev builds). F1 toggles it. It is a draggable, resizable window scaled to the screen height. An invisible uGUI blocker on a top canvas (sort order 32000) stops clicks going through to the console. The old IMGUI debrief is gone; `DebriefScreen` handles that now.
  - **RUN**: power, clock/warp, ship position and velocity, orbit (primary, a, e, i, ν, peri/apo, period). Buttons for debug base rate and refill power.
  - **SYSTEM**: every node in `CurrentData`, with true bearing, elevation, range (AU) and apparent luminosity from the ship, and the player track sitting on it (≤ 2°, great-circle when the track has elevation). Sorts: Tree (indented) / Range / Bearing / Brightness / Name. Barycenters toggle. Click a row to inspect it.
  - **INSPECT**: full NodeData: parent/children navigation, observed geometry, physical data (stars in solar units; planets in M⊕/MJ, R⊕, T_eq vs T_surface), orbital elements (plus current M, peri/apo, period), bulk composition, atmosphere, spectrum (line counts, strongest lines). Set NAV target button.
  - **TRACKS**: for each track, the nearest true body, bearing error, elevation error vs aged σ, best range ± σ vs true range, and ID correctness. The row turns red beyond 3σ or on a wrong ID.
- **`SystemManager.NodeTransform(int)`**: node index → spawned transform, for dev tools only.

## Later fixes and the Sol home system (same session)

- **UI**: `UIBar` puts its text on its own row above an 8 px fill track. The waterfall cursor shows as edge ticks, plus a full-height line for 3 s after it moves. Waterfall track marks are purple (selected: magenta; searching: red), and the heading tick is blue (`UITheme.trackLocked/trackSelected/headingColor`).
- **Tracker** (`TrackManager`):
  - History is kept in time order. Radar samples are stamped at fire time and inserted, so they no longer hid the waterfall trail.
  - The rate is a weighted fit over the last 5 days (at least 4 hits), and is trusted (`hasRate`) only once its σ ≤ 0.5°/d.
  - `PredictGate` tries a quadratic fit over 6 hits, then the linear rate, then coasting (≤ 1°/d drift). Each is used only if its own σ over the gap is ≤ 3°. Gate = base + 3σ + 0.2·|move|, capped at 12°. After a miss, a coast gate around the last bearing also applies.
  - Tested against simulated curving contacts at 15 d/line. Very fast close passes can still drop.
- **Radar**: `ApplyRadarFix` takes the bearing the echo was measured on. A TRACK echo is not filed if the track moved more than max(2·beam, 1°) meanwhile (the result line says so).
- **Pause**: the waterfall stops generating lines, the imager stops scanning and integrating, and the spectrometer stops accruing dwell and redrawing. The radar was already sim-time based. Aiming still works while paused.
- **Sol** (`Data/SolSystem.cs`): `SystemFactory.HomeSystemID` is always `"SOL"`, hand-built at J2000 (SimSeconds 0).
  - Planets use Standish mean elements. Moons are given in their parent's equator frame and rotated into the ecliptic by the rotation pole (Uranus flipped).
  - Real mass, radius, albedo, temperatures and atmospheres. Moon phases and dwarf mean anomalies are approximate.
  - Bodies: Sun, 8 planets, Moon, Galileans, 7 Saturnian, 5 Uranian, Triton, Pluto + Charon, Ceres, Main Belt, and Haumea, Makemake, Eris, Sedna (these four not catalogued).
  - The probe starts in a 100,000 km prograde parking orbit around Earth, on the night side (`SystemData.startNode/startOrbitRadiusGame`, `SystemManager.PlaceInParkingOrbit`).
- **Catalogue** (`Core/Catalogue.cs`): the catalogued Sol bodies are seeded into the Atlas at boot and on New Game (`AtlasEntry.catalogued`, shown as CATALOGUE). `DataStore.CatalogueMatch` marks a record whose track points within 1° of a catalogued body. Such a record is worth 0, the Data panel shows "known: X", and on delivery it folds into the catalogue entry.
- **Molecular bands** (`SpectralLineTable.AddAtmosphereBands`, via `SystemFactory.BuildSpectrum`): CH4 543/619/727, NH3 552/645, H2O 592/651/694/723, O2 628/687/761, CO2 782/788 nm. Depth = 100·√share·column, where column = log10(1+1000·P)/3 (≤ 2; an envelope counts as 4). Procedural giant envelopes now carry CH4 (if T < 700 K) and NH3 (if T < 250 K), with no RNG draws. The spectrometer band now runs to 800 nm (constant and `SpectrometerSpec_T3.asset`).
- **Stale tracks** (follow-up to the adaptive gate):
  - **When the gate may widen:** only while following a live contact. That means a locked track for up to `dropAfterMisses` missed lines, or a fresh mark still acquiring.
  - **SNR outside the base gate:** a detection needs SNR ≥ max(5, 0.5 × `Track.snrAvg`).
  - **SNR inside it while coasting:** after a missed line, even a base-gate hit needs ≥ 0.5 × `snrAvg`.
  - **Lost tracks** (`lostLock`) freeze at their last bearing, with no extrapolation, a base gate and a strong-SNR requirement.
  - **Re-locking:** needs `ConfirmHits` hits without a gap longer than `dropAfterMisses`.
  - **Harness results:** a faded contact went stale in 30/30 runs, drifting ≤ 2° at warp up to 0.5 d/line. Curving contacts were still followed.
- **Atlas browser** (`UI/AtlasPanel.cs` rewritten):
  - **Levels:** SYSTEMS (home first) > SYSTEM > BODY, plus an "Unidentified contacts" list. A breadcrumb and BACK climb up; 12 rows per page with < >.
  - **Body tree:** regenerated from `SystemFactory.Generate` and cached per seed. It shows only named bodies plus their ancestors; unsurveyed ancestors are dimmed, moons are indented, barycenters skipped.
  - **Body details:** spectrometer-level data. Catalogued bodies also get mass, radius and orbit.
  - **Filing by body:** `DataRecord.bodyName` and `AtlasEntry.bodyName` come from the track's identification (`info.catalogName`, also picked up mid-recording) or the catalogue match.
  - **Distances:** `Loc.Distance` shows km below 0.01 AU (Orbit panel apsides).
- **Imager FIX on resolved disks:** it floods the blob above half its peak and takes the brightness-weighted centroid. It refuses if the blob touches the frame edge, and the gate grows to the blob's radius. `Track.angularRadiusDeg` loosens the spectrometer's pointing limit by half the radius, and `SensorSight.CollectInCone` counts a body when the cone touches its disk.
- **Spectrometer zoom:** a visual zoom (×1 to ×16, wheel/drag/buttons, FULL) with a rebuilt wavelength axis. It doesn't change resolution.
- **Doppler:** every source's lines are scaled by (1 + v_r/c), with v_r from `SensorSight.RadialVelocityKmS` (analytic node velocity minus ship velocity, along the line of sight).
  - The readout's error is σ ≈ (c/R)/(2√lines)/√(1+dwell), with a fixed error draw per signature. At R = 200 that is about ±70 km/s, so the shift is invisible.
- **Future (agreed):** a high-tier spectrometer with R ≥ 10,000 (±1–3 km/s) could feed its measured v_r to the track as a range rate, like the radar does.
- **Gaps fixed:**
  - **Status-bar warp:** it snaps to `GameClock.WarpLadder`, and `SetWarp` normalises 60 s to 1 m and 60 m to 1 h. ×60 is clickable only in days.
  - **Atlas body page:** 5 rows per page.
  - **Catalogue.Match:** needs `hasElevation` with an aged σ ≤ 2°. The limit is max(1°, 2σ, disk radius). A raw recording can gain its catalogue match mid-way.
- **Spectra v2** (`SpectralLineTable.BuildStellar`/`BuildReflected`, `SystemFactory.IlluminatingStarSpectrum`):
  - **Stars:** absorption lines only. Each species' strength is a bell curve in ln T around its peak, times √(10^[Fe/H]) for metals:
    - He 20 kK, H 9.5 kK, Ca II 5 kK, Mg 5 kK, Fe 4.8 kK, Na 4 kK.
    - TiO 3 kK, only below 4200 K.
  - **Planets and moons:** their star's lines plus the atmosphere bands (molecular scale 200; CO2 bands ×0.1 intrinsic).
  - **Removed:** the old composition-based atomic lines and the placeholder O/C/Si/Al/N lines.
- **Next, on hold until the user has tested the gaps and spectra:**
  1. **Saving:** chosen FULL mid-flight state, but sensor settings and displays are NOT saved (reset on load).
     - Keep: seed, atlas, trust, runs, hardware, clock, ship state/orbit/maneuvers, tracks WITH history, recordings/storage, pending transmissions, power/H2.
     - Slots: NAMED save slots.
  2. **Debrief review + upgrades:**
     - Upgrades: every system is available; the probe's loadout is free-form as long as the summed cost of the chosen systems/tiers stays within the trust budget (`GetLoadoutBudget`).
       This lets the player specialise runs (e.g. waterfall + spectrometer one run, imaging another).
     - Later: imaging as its own data kind, and a way to point the spectrometer without the imager or radar.
     - Review includes declared confidence when SENDING data: Tentative pays less with a small penalty if wrong; Confirmed pays full and hurts more if wrong.
       Re-observing a Disputed (caught-wrong) body gives a correction bonus.
- **Save system DONE** (`Core/SaveGame.cs`, `Game.CaptureSave/ApplySave/Autosave`):
  - **Storage:** JSON via JsonUtility in `persistentDataPath/saves/<name>.json`, plus a `.meta` header for the list. Writes go to a temp file first, then move into place.
  - **Saved:** seed, trust, objective, installed spec names, atlas, link log, clock, run phase/number/start/summary, galaxy position and system id, ship state, power/H2/reactor, maneuver queue, target, tracks (via `TrackSave`, with history and TrackInfo), data records and current-run transmissions.
  - **Not saved:** sensor settings, displays (waterfall image, radar scope), wake settings, link power/robust, a radar ping in flight.
  - **Load order:** restore installed sensors and atlas, set the clock paused, `ResetProbe`, restore resources and link, `Jump.Restore`, `SystemManager.JumpToSystem`, then overwrite ship state, tracks (generation restored), data, maneuvers, target, and finally `Run.Restore`.
  - **Empty strings:** JsonUtility writes null strings as "", so load turns them back into nulls. Null class fields use `has*` flags.
  - **Autosave:** the "Autosave" slot at every launch (`RunController.Launched`) and every debrief.
  - **Menus:**
    - **Main:** CONTINUE (latest save), LOAD GAME.
    - **Pause:** SAVE GAME, LOAD GAME.
    - **Slot pages:** 8 rows per page; overwrite, delete and load-mid-run each ask for confirmation.
