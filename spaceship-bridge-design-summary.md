# Spaceship Bridge: Design Summary (for implementation)

Goal: turn the Unity sensor simulator into a playable game, releasable as early access.
Tone: calm, meditative survey game (Noctis-like). Tension comes from quiet arithmetic (power, storage, doubt), never from combat.

---

## 1. Premise

- The player secretly plays an **AI probe** with no life support. **Power is its life.**
- If power runs out, the probe dies and a **replacement probe** launches from home, with some upgrades removed.
- If the probe returns home with its data, the player chooses upgrades.
- The "bridge" UI is the AI's internal model of its sensors.
- The AI nature is revealed gradually (logs in wake cycles, no windows, no crew).

## 2. Structure

- **World seed = one game.** Successive runs in the same seed are the game loop. A new seed means a new world, starting from scratch.
- **Galaxy catalog generated at game start:** one lightweight record per star (position, luminosity, type), deterministic from hash(worldSeed, cell). Used for the background sky, the diffuse noise floor per bearing, and real distances for pre-scan.
- System IDs map to galaxy coordinates.
- **Save file = world seed + atlas + wrecks + unlocked hardware + trust.** Systems are regenerated from the seed, not saved.
- **Roguelite runs (expeditions):** each run has a loadout, a hydrogen budget, and a return decision.

## 3. Sensor progression

Hardware sets the ceiling. The player picks the operating point below it. Every setting has a real tradeoff.

| Tier | Sensor | Information gained |
|---|---|---|
| 1 | Waterfall only | Bearing only. Range requires maneuvering and target motion analysis (bearing rate). |
| 2 | Imager | Elevation, angular size, phase/albedo |
| 3 | Spectrometer | Composition |
| 4 | Radar (active) | Range, after light delay. Costs power. |
| 5 | Long-range pre-scan | Tentative signatures of unvisited systems |

**Accuracy caps by hardware (via `SensorSpec` ScriptableObjects per tier, held in `GameState`):**
- Waterfall: integration depth (SNR gain ~ sqrt(N), cost = latency and smearing of moving targets), bin count tied to aperture, noise floor sigma, minimum update interval.
- Beamwidth is a physical constant of the array (theta ~ lambda/D), expressed in **degrees**, not bins. Bins finer than half a beamwidth add nothing. Aperture upgrades are the only way to separate close bearings.
- Imager: FOV range, blockSize limits (angular resolution), integrator depth, dynamic range.
- Spectrometer: `spectrumWidth` becomes resolving power R = lambda / delta-lambda. Later: close line pairs and Doppler-shifted lines.
- Locked sensors show a dark "no hardware installed" panel (`ComputerScreen` base).
- Upgrade physical parameters and let derived numbers follow. Avoid arbitrary "level 3 = 40 bins" caps.
- Compute minimum detectable flux from sigma and N so upgrade steps map to "how far out can I see a rocky body".

**Long-range pre-scan (late game):**
- Transit photometry: dip depth ~ (Rp/Rs)^2, period from repeated dips. Plot in `DSPGraph`.
- Radial velocity: Doppler shift of spectral lines gives period and minimum mass.
- Returns `Observation { estimate, sigma }`, never the truth. Candidates go from possible to probable to confirmed as sigma shrinks. False alarms (eclipsing binaries, variability) follow the Pd/Pfa tradeoff.

## 4. Physical systems

### Power and consumables
- Solar panels: flux ~ 1/d^2 from `CelestialBody` luminosities. Free but weak far from stars.
- Crude reactor: higher output burns hydrogen faster and adds waste heat.
- **Hydrogen is the single shared consumable** (reactor, propulsion, scooping).
- Sensors draw power. Not everything can run at once (power and data budget).
- Refueling:
  - Gas giant scooping: needs orbit or upper-atmosphere dip, with drag, heat, and a fast rate. The spectrometer finds H-rich bodies.
  - Low-power mode (reactor off, panels only) collecting solar wind hydrogen: very slow, scales as 1/d^2.
- Point of no return: a run ends when too much has been spent to get home. A stranded run keeps its transmitted data.

### Active radar and light delay
- Pulse round trip = 2d/c (about 16.6 min at 1 AU). A pulse is a commitment: fire, warp, wait for the echo.
- Aim at where the target will be. Orbit estimate uncertainty means poor bearing gives missed pulses.
- Active radar gives instant-ish range but costs power (and could later announce presence).

### Time warp as the AI's idle mode
- Warp costs consumables every second.
- The player sets **wake conditions** beforehand: contact above X sigma in a sector, transit window, radar echo. Too tight causes false wakes that burn power. Too loose means missed events.
- Decide which systems are warp-independent (UI, audio). Orbits and coroutines currently follow `Time.timeScale`.
- Observation windows (transits, occultations, best phase) come from deterministic Keplerian orbits, so planning around them is real gameplay.

### Transmission and the link model
Two channels:
1. **Transmit home:** costs power scaling with range (required power ~ d^2). Lower power means more packet loss and less reward.
2. **Return home with data:** full data packet, plus upgrade choice, salvage, refit.

- Success chance per packet is a smooth function of link margin (dB). Show live margin and expected rate per data tier.
- Data tiers: **Stub** (position, type; tiny), **Characterization** (temperature, size, composition summary), **Full raw** (spectra, light curves; large).
- Optional coding-rate choice: heavier error correction lowers payload but raises reliability.
- **Results are only learned at the end of the run** (return or death). Roll loss from hash(worldSeed, packetID) so reloading cannot reroll.
- Keep an engineering log across runs ("~X dB margin gave Y% delivered") so players learn the model empirically.
- Acks take d/c to return. The probe never knows what arrived until the debrief.
- **Last transmission:** a dying probe dumps as much as remaining power allows.
- Later upgrades: antenna gain (needs precise pointing at home), deployable relay beacons (cost hydrogen mass, persistent in the world).

### Storage
- Limited abstract capacity. Stubs are tiny. Raw waterfall history, spectra, and light curves are large.
- Sending does not free space (the probe does not know it arrived). Keeping a copy allows a blind retransmit.
- **Recording is deliberate:** the player chooses what to keep. Lossy compression can shrink recordings.
- When full: dump the least valuable data, transmit, or return home.
- Power pushes the probe toward stars. Storage pushes it toward home.

## 5. Tracking and association (data model)

- Trackers work from **CFAR detections**, not true azimuth. Gate of +/- N bins around the last bearing.
- Track quality rises with hits and falls with misses. False tracks are possible at low thresholds. Crossing and merging tracks must be resolved by the player.
- Trackers are limited by hardware tier.
- Hold tracks in **world azimuth** (not ship-relative).
- Bearing history feeds triangulation when the waterfall is the only sensor.
- Data model:
  - `Observation { sensor, time, measurement, sigma, hiddenTrueBodyId }`
  - `Track { id, name, linkedObservationIds[] }`
- **Associations across sensors are manual.** The UI shows evidence, never the answer:
  - bearing gate overlap
  - bearing-rate consistency over time (wrong pairings diverge, so patience helps)
  - physical plausibility residuals (spectrum says gas giant, angular size says rocky)
  - range consistency once radar is available
- Link, unlink, merge, split are allowed until data leaves the probe. Transmitted packets are frozen.
- Recording is per track and per layer, started and stopped by the player. Auto-drop when a track is lost.
- Tracking logic is pure data, separate from rendering, so the same tracks feed the imager overlay and radar screen.

## 6. Trust ("credits") and the atlas

- **Credits are not money.** They measure how much the humans trust the AI program.
- Trust drives the loadout budget for the next probe.
  - High trust: costlier hardware allocated.
  - Low trust: budget shrinks, and below a threshold granted upgrades are **scrapped or redirected** to better AI programs.
  - Scrapping happens at debrief only, on downward threshold crossing. Upgrades that produced no verified data are cut first.
  - The baseline waterfall-only probe is untouchable (recovery floor).
  - Hysteresis: trust falls faster than it rises.
- Floor at zero for now. Scrutiny scales inversely with trust, which discourages speculative submissions at low trust.
- Guarantee that the baseline probe can always complete a low-tier objective near home (objectives refresh each run).
- **Objectives:** defined targets (habitable planet, palladium-rich body...), guaranteed reachable but hidden within fuel range. Optional commissions board.
- **Recognition = information value** (scan completeness x rarity), not raw discovery count.

**Persistent atlas:**
- `AtlasEntry { claimedProperties, provenance, confidence, status (Unverified/Verified/Disputed/Retracted), reviewCount }`
- Wrong associations write **false entries** that persist until a later run re-observes and corrects the body.
- Humans may spot and flag errors at each debrief review cycle. Detection chance grows with severity, scrutiny (near home and well charted), and time. Roll from hash(worldSeed, entryID, reviewCycle).
- Cost when caught: clawback plus severity-scaled trust penalty. Entry becomes Disputed, not silently deleted.
- Confidence declared at submission: Tentative (pays less, small penalty) or Confirmed (pays full, hurts if wrong).
- Correcting a wrong entry gives a bonus.
- Show provenance on every entry.
- Tune so a well-supported tentative entry is positive value and a blind guess is not.
- Trust display: **visible number for now** (with labeled bands and a change log with causes). Later inferred through in-world signals (message tone, offered upgrades, review frequency). Keep a numeric debug overlay permanently.
- Store trust as one value in `GameState`. Everything reads it through named functions (`GetLoadoutBudget()`, `GetReviewChance()`).

## 7. Atmosphere features

- **Technosignatures:** rare narrowband lines, strictly periodic pulses, or spectral features no composition explains. The player must rule out natural sources.
- **Sonification of the waterfall:** hiss for noise, tonal ridges for contacts, a soft ping for radar.
- **Wrecks:** dead probes persist in the world. They can be found and salvaged for hydrogen, upgrades, and unsent data.
- **Player-named discoveries** and a personal atlas as the meta-progression.
- Optional later: rival AI programs producing background atlas entries from the world seed.

---

## 8. Issues in the existing code (fix during refactor)

**Architecture**
- `SystemGenerator.cs` contains class `SystemManager`. Unity requires MonoBehaviour class and file names to match, so rename the file.
- Split generation into `SystemData` (pure data, deterministic from system ID, no GameObjects) and a spawner. Pre-scan needs to read a system without instantiating it.
- Use a local `System.Random(seed)` for generation. `Random.InitState` mutates global state and disturbs other randomness (noise, etc.).
- `DetermineChemicalComposition` returns fixed percentages per body type, so every rocky planet has an identical spectrum. Add per-body variation (abundances, trace elements, anomalies).
- Central `GameClock` with a warp factor. `OrbitalComponent` uses a magic `/ 8760f` and `YearsToGameSeconds = years * 10`; unify time units.
- `Barycenter.Update` and `OrbitalComponent` coroutines have no guaranteed order, which can give one-frame lag in barycenter position.
- `GameState` (unlocked sensors, trust, power, storage, active objective) instead of each screen finding what it needs.

**Imager**
- `ScanRoutine` calls `FindAllCelestialBodies()` per pixel and `FindAnyObjectByType<Spectrometer>` per scan line. Cache and refresh on system change.
- `CalculateDirectionalLuminosity` sorts the body list (`OrderBy`) and allocates arrays (`ApplyCompression`) per pixel.
- `OnBodySelected` computes a local `selected` but `_selectedBody` is never assigned, so target tracking in the scan loop never activates.
- `ToggleZoom` uses `GetComponent<TMP_Text>()` on the button (text lives in a child), so the label never updates. `InitializeZoomButton` correctly uses `GetComponentInChildren`.
- Screens read `rect.width` once in `Start`. Use a Canvas Scaler and test several resolutions.

**Waterfall / DSP / Spectrometer**
- `WaterfallScreen` uses true azimuth (`GetData()`), not detections. Needs a detection layer for trackers.
- PSF `spreadSigma = 1.2` is in bins, so the slider changes the beamwidth by accident. Express it in degrees.
- `AddWaterfallLine` instantiates one GameObject per point per line and moves every line each update. Replace with a scrolling `Texture2D` (or ring buffer).
- `integration = 20` is hardcoded and `ArrayList` is used. Move to a spec and `List<float[]>`.
- `DSPGraph` and `Spectrometer.DrawDSP` create one GameObject per segment. Use a texture, mesh, or `LineRenderer`.

## 9. Suggested implementation order

**Foundations**
1. Rename `SystemGenerator.cs`, add `GameClock`, `GameState`, `SensorSpec`, and the seeded-RNG `SystemData` / spawner split.
2. World seed and galaxy catalog. Performance fixes (texture-based waterfall, caching in the imager).

**Slice A: waterfall-only run**
3. Detection layer and trackers on the waterfall. Bearing-only, range via ship maneuver.
4. Power model (panels, reactor, hydrogen), jump with fuel, time warp with wake conditions.

**Slice B: data**
5. Storage and deliberate recording. Debrief screen.
6. Transmission with the link model and delayed results. Atlas (basic).

**Slice C: trust and loop**
7. Trust, loadout budget, upgrade selection, death and replacement probe, baseline objectives.

**Slice D: more sensors**
8. Imager, then spectrometer unlocks (with composition variation). Manual association UI.
9. False atlas entries, review cycles, provenance.

**Slice E: depth**
10. Radar with light delay, long-range pre-scan, technosignatures, sonification, wrecks.

## 10. Open questions

- Stage 1: does the player get range by maneuvering (needs ship control), or is the waterfall just a detection screen handing off to the imager?
- Should trust become fully inferred (diegetic) at release, or stay visible as a number?
- Rival AI programs: keep as a later feature, or cut?
- Early access path: vertical slice, playtests, itch.io demo, Steam page early for wishlists, Next Fest, early access.
