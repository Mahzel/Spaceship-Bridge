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
