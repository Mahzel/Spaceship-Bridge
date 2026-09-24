# Handoff — 2026-09-24 session (agent switch)

Branch `claude/nav-ui-work-89jo2r`, pushed through commit `689a4cf`. Working tree clean. **Nothing in this
session has been compile-checked or opened in the Unity Editor** (no Editor/batchmode available to the prior
agent) — that's the single biggest thing the next session needs to do before anything else.

## First thing to do

Open the project in the Editor, let it compile, and actually play the NAV/SENSORS/JUMP/ATLAS screens. A lot
landed this session (~20 commits) across UI layout and gameplay logic; none of it has been seen rendered.
Start with **the layout fix in `689a4cf`** (see "Needs verification" below) since it's the most likely thing
to still be visibly wrong.

## What happened this session, roughly in order

1. **UI polish/bugfixes from play-testing reports**: NAV sidebar text overlap (empty-label-zero-height bug),
   ARM silently clobbering an armed transfer plan, waterfall integration "ghost contacts" at high time-warp,
   NAV map rendering outside its own bounds (added `RectMask2D` + drag-to-pan), radar range scale bottoming
   out at 5 AU (added km-scale tiers via a stepper), MET/countdown time formatting (`Loc.Countdown`,
   `D:HH:MM:SS`), tracked-range display units (`Loc.Distance`, auto km/AU).
2. **Gameplay/physics fixes**: radar TRACK-mode ETA was leaking omniscient info (fixed to use the track's own
   estimate); waterfall bearing-correction on a selected track used to wipe its whole history (now folds in as
   a new sample via `TrackManager.CorrectBearing`); radar range fixes never fed velocity into the orbit solver
   (fixed — likely also explains an earlier "burn sent me into a solar orbit" report); a locked track's
   position could drift ~60° off its own measured bearing under warp (fixed — `TrackManager.BestRange` now
   pins position to the live bearing while actively locked).
3. **New features (Atlas)**: `RECALL` on an Atlas body seeds a fresh, aimable "ghost" track from known data
   (`Tracking/GhostContact.cs`) — precise if catalogued, a coasted/orbit-propagated guess otherwise; raw
   survey records now also carry a determined orbit (`OrbitFit`) when one was found, not just a bearing/range.
4. **NAV roadmap items** (`handoff-navigation-ui.md` tracks these in detail): uncertainty ellipse for a
   selected track's fitted orbit, predicted path for an armed maneuver node, a timeline strip (upcoming
   Pe/Ap/burns + WARP TO), a Δv budget widget, a galaxy map on the JUMP tab (turned out the backend -
   `Data/Galaxy.cs`, `Core/JumpDrive.cs` - already existed and was already correct, just never had a visual
   map), and a periapsis-below-surface impact alert.
5. **Last commit (`689a4cf`)**: fixed a layout bug the timeline strip (item 4 above) itself caused — the map's
   old 600px `minHeight` plus the new timeline strip no longer fit in a modest window, so the map rendered on
   top of the timeline and the timeline's WARP TO button sat on top of (and blocked) CREATE + ARM NODES.
   Shrunk both. **Flagged explicitly, not just fixed-and-forgotten**: the sidebar's own content (inclination
   dial + zoom/layer rows + the whole TRANSFER section) was hand-estimated at ~500px and might *still* be
   tight against the timeline strip even after this fix — the numbers were worked out by reading `UIKit.Size`
   calls, not by seeing it rendered. If it's still cramped, the honest fix is a scrollable sidebar
   (`ScrollRect`, not used anywhere else in this codebase — build it carefully, not improvised), not more
   pixel-shaving between fixed heights.

## Needs verification, roughly in priority order

1. **The NAV tab layout** (`689a4cf`): does the timeline strip now sit cleanly below the map+sidebar row with
   no overlap, at whatever window size you actually test at? Is the sidebar's own bottom content (CREATE +
   ARM NODES button) fully visible and clickable?
2. **The periapsis-impact alert** (`3a1c520`): arrange a periapsis that dips below a body's surface (or just
   sanity-check the math: `radiusSol * GameConstants.SOLAR_RADIUS_IN_METERS / GameConstants.AU_IN_METERS *
   GameConstants.GAME_UNITS_PER_UA` should come out close to a body's known real radius in game units) and
   confirm the timeline row actually goes red and the label reads right. This is the one change this session
   where a units mistake would either cry wolf or (worse) stay silent about a real crash.
3. **Track bearing-pin fix** (`045986e`): lock a nearby body (Moon-from-Earth-orbit repro), warp forward hard,
   confirm the NAV marker/TARGET ORBIT panel track the waterfall's own bearing line throughout, not drift off.
4. **Radar velocity → orbit solver** (`af222ef`): range a track with radar (TRACK mode) and check NAV's TARGET
   ORBIT panel actually tightens/stays sane instead of distorting.
5. **Waterfall CORRECT** (`af222ef`): click-correct an already-selected, already-ranged/identified track and
   confirm it keeps its range/elevation/identity instead of resetting to a fresh searching track.
6. **GhostContact / Atlas RECALL** (`6c88447`, `3283674`): recall a catalogued body and a non-catalogued
   survey entry, confirm the seeded track's bearing/range look sane and it shows up selected.
7. **Galaxy map** (`0765b49`): open the JUMP tab, confirm the map renders, pans/zooms, and the ship/home/nearby
   dots land where the (unchanged, already-working) list says they should.
8. Everything else in the "Not compile-checked" callouts throughout `handoff-navigation-ui.md` — it has a
   `## Progress (this session - ...)` entry per feature/fix, each with its own specific "worth checking" note.

## Key files

- `handoff-navigation-ui.md` — the full, detailed log for the NAV/tracking/sensor work this session and prior
  ones. Read this first for any one feature's exact reasoning, not this summary.
- `handoff-ui-shell.md` — the earlier UI-shell rework (sidebar + major/minor modes) this session's NAV work
  sits on top of.
- `Assets/Scripts/UI/NavScreen.cs` — the single most-touched file this session (map, timeline, layers, sizing
  fixes all live here).
- `Assets/Scripts/Tracking/TrackManager.cs`, `Tracking/GhostContact.cs`, `Tracking/OrbitFit.cs` — the
  tracking/orbit-determination core, several fixes this session.
- `Assets/Scripts/Core/NavEvents.cs` — new this session, the timeline/alert event generator.

## Known deliberate scope cuts (not bugs, just not built)

- Timeline strip: no SOI-change, closest-approach, comms-window, or jump-window events yet (only Pe/Ap +
  queued burns). Severity doesn't reorder which events are shown, only their colour.
- Galaxy map: read-only (no click-to-select-target), no Atlas visited/catalogued/disputed markers, no
  sector/galaxy zoom hierarchy.
- Predicted-path/uncertainty ellipses: rough visual stand-ins (radial scaling by a sigma fraction), not a real
  covariance propagation.
