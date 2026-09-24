# Handoff: UI shell rework (persistent sidebar + major/minor modes) (2026-09-24)

## Why

The gameplay screen was seven independently-positioned, draggable floating windows (`SystemsDock`,
`SensorConsole`, `OrbitPanel`, `ManeuverPanel`, `TrackPanel`, `AtlasPanel`) plus `NavScreen` as its own
full-screen dim+panel overlay toggled by a dedicated status-bar button. Getting cramped as more got added
(NAV's catalogue/track layers and TRANSFER section, most recently). Agreed direction: one persistent frame -
a left sidebar picking a major mode, a collapsible track strip between the topbar and the mode area, and a
content area the active major mode fills completely.

## Mapping (as agreed)

- **Sidebar (major modes):** SENSORS / NAVIGATION / COMMS / SYSTEMS / ATLAS.
- **SENSORS:** unchanged internally - `SensorConsole`'s own WATERFALL / IMAGER / SPECTROMETER / CONTACTS /
  RADAR tabs, just filling the shell's content rect now instead of an absolute-positioned floating window.
  CONTACTS is the renamed `ContactsScreen` (was `SystemScreen` - collided with the new SYSTEMS major mode
  even though it's unrelated, a tracks overview, not reactor/wake).
- **NAVIGATION:** a new minor-tab dock, `NavigationMode`. NAV (default) / MANOEUVERS / JUMP / NODES.
  - NAV is `NavScreen`, no longer a full-screen overlay - see below.
  - `OrbitPanel` is NOT a minor mode. It's pinned to the bottom-right corner of the NAV tab specifically
    (built into the same shared body as `NavScreen`, so its own corner anchor resolves against the whole
    tab area, not just whatever room NavScreen's internal map/sidebar split leaves free). This also let
    NavScreen's own sidebar drop the primary/apsides/shape/period/true-anomaly text it used to duplicate -
    Orbit already shows all of that (and handles the hyperbolic/escape case NavScreen's block never did).
  - MANOEUVERS = `ManeuverPanel`, JUMP = `JumpPanel`, NODES = `NodePanel` - all three needed zero internal
    changes (Jump/Node already built plain content with no background/anchor of their own, from when they
    lived in the old `SystemsDock`; Maneuver's own background/anchor were stripped to match).
- **COMMS:** new `CommsMode`. DATA (`DataPanel`) / TX (`TransmitPanel`).
- **SYSTEMS:** new `SystemsMode`. REACTOR (`ReactorPanel`) / WAKE (`WakePanel`).
- **ATLAS:** its own major mode, straight to `AtlasPanel` (no minor tabs) - previously a floating panel too.
- **Track panel -> track strip:** the user's own answer here was "collapsible between topbar and mode
  screen, in any mode" - so it's neither a Sensors-only nor a Navigation-only thing, it's `GameShell`'s own
  persistent strip, always shown (collapsible via a header toggle button), above the sidebar+content row,
  full width. Makes sense: track selection drives both the sensor SEL state and the NAV transfer target.
- **Menu / Refit / Debrief:** left alone, still their own full-screen modal overlays (pre-run, post-run,
  pause) - explicitly deferred, no clear win to folding them into the sidebar frame yet.

## New files

- **`UI/GameShell.cs`**: the frame itself. Builds the sidebar (5 buttons), the track strip (`TrackPanel`,
  adapted - see below), and a content area hosting the 5 major-mode objects, `SetActive`-toggling like the
  old `SystemsDock` did for its tabs. Two refresh entry points, `RefreshFast(dt)` (every real frame - Sensors'
  aimed reads and DSP redraws, NavScreen's self-throttled redraw check) and `RefreshSlow()` (10 Hz - the
  track strip, Comms, Systems, Atlas, and the NAVIGATION sidebar button's fitted-gating), mirroring the exact
  cadence `GameUI` already used before this rework so nothing changed there beyond which object owns what.
- **`UI/NavigationMode.cs`**, **`UI/CommsMode.cs`**, **`UI/SystemsMode.cs`**: the three minor-tab docks, all
  the same hand-rolled tab-row + body-array + `SetActive` + `Refresh` pattern `SystemsDock` established (no
  generic "Dock" abstraction was introduced - matches how every dock/console in this codebase is already its
  own small class, not worth breaking that convention for three call sites).

## Changed files (stripped their own absolute anchor / `DraggablePanel.Attach`, now fill whatever parent
## they're given via `UIKit.Stretch` or plain flexible sizing)

- **`Display/SensorConsole.cs`**: `Build()` now `UIKit.Stretch`es instead of anchoring bottom-right with a
  `ContentSizeFitter`; dropped its `DraggablePanel.Attach`.
- **`UI/AtlasPanel.cs`**: same change, was top-left anchored.
- **`UI/OrbitPanel.cs`**: was top-left anchored to the WHOLE CANVAS; now bottom-right anchored to WHATEVER
  PARENT it's given (`NavigationMode`'s shared NAV-tab body). Kept its own small background panel (it's
  meant to read as a distinct floating readout box over the map, unlike the plain dock-tab panels).
- **`UI/ManeuverPanel.cs`**: dropped its own background `Image`/anchor/`ContentSizeFitter`/drag entirely,
  now matches `NodePanel`'s "just a `UIKit.Node` + `flexibleWidth`" style since it's a plain minor-tab body.
  `Build()` now returns `GameObject` (was `void`) to match the other tab-body panels' signature.
- **`UI/TrackPanel.cs`**: became the track strip - dropped its own top-right canvas anchor and drag, added a
  HIDE/SHOW collapse toggle next to its header that hides everything below (the mark-row, the row list, the
  rename field) while keeping the header line, `ContentSizeFitter` handling the height change either way.
- **`UI/NavScreen.cs`**: the big one. No more `Image dim` / `Image panel` / Back button / `_open` /
  `Toggle()`/`Open()`/`Close()`/`IsOpen` - it's now `Build(parent) -> GameObject` like every other tab body,
  visibility owned entirely by `NavigationMode`'s tab switch. `OnShown()` replaces `Open()`'s "re-fit the
  zoom to whatever orbit is current" (called by `NavigationMode.SetActive` when switching TO the NAV tab) and
  also force-rebuilds the map rect's layout before the next `AutoFit` reads it, the same safeguard the old
  `Refresh()` did right after `_root.SetActive(true)`. Also removed the sidebar's `_primary`/`_shape`/
  `_period`/`_nu` labels and their `Draw()`/`SetSidebarActive()` plumbing - `OrbitPanel` (now pinned right
  there) already shows all of that, including the hyperbolic/escape case NavScreen's own block never
  bothered with. Kept `_incl` + the inclination dial (unique to this screen).
- **`UI/StatusBar.cs`**: `Build()` lost its `onNavToggle` callback parameter and the NAV button entirely -
  Navigation is a normal sidebar major mode now, reachable the same way as Sensors/Comms/Systems/Atlas, not
  a special status-bar button. The `NavTier.HasSystemView` gating that used to hide/show that button moved
  to `GameShell.RefreshSlow()`, greying out the NAVIGATION sidebar button's `interactable` instead.
- **`UI/GameUI.cs`**: now owns `StatusBar` + `GameShell` + the three modal screens (Debrief/Refit/Menu)
  instead of nine separate objects. `RebuildUI()`/`OnRunChanged()`/`Update()` all updated to match - the
  fast/slow refresh split GameShell exposes is called from the exact same places the old per-panel calls were.
- **`Display/SystemScreen.cs` -> `Display/ContactsScreen.cs`** (`git mv`, same GUID): class renamed
  `SystemScreen` -> `ContactsScreen`. Only the class name and the Sensors tab's displayed label changed
  ("SYSTEM" -> "CONTACTS") - internal `Loc` keys stayed `ui.system.*` (a much larger, riskier rename with no
  player-visible benefit).
- **`UI/UIKit.cs`**: `Size()` gained an optional `flexibleHeight` parameter (appended at the end, so every
  existing named-argument call site is unaffected) - needed so a tab body or the NAV map area can claim
  remaining vertical space in a `VStack`, which nothing in this codebase needed before every panel was its
  own auto-sized floating window.

## Deleted

- **`UI/SystemsDock.cs`**: fully absorbed into `NavigationMode` (Jump/Node) + `CommsMode` (Data/Tx) +
  `SystemsMode` (Reactor/Wake).

## Not touched, worth knowing about

- **`UI/DraggablePanel.cs`** is now dead code - nothing calls `Attach` anymore. Left in place rather than
  deleted: it's a small, self-contained, still-correct utility, and `MenuUI`/`RefitScreen`/`DebriefScreen`
  could plausibly want it later. Delete it in a follow-up if it's still unused once those three are decided.
- Sensors/Comms/Systems content doesn't yet fill 100% of its major mode's vertical space - each tab body
  auto-heights from its own content (unchanged from before), so there's blank space below shorter panels
  (Jump/Node/Reactor/Wake/Data/Tx) inside their now-taller container. Not broken, just not making full use of
  the room. A `flexibleHeight` pass on the shorter panels (or a different fill strategy) is a natural
  follow-up once this is confirmed to actually look cramped in practice - didn't want to guess at that
  without seeing it rendered.

## Not compile-checked

No working Unity batchmode in this environment (same issue noted throughout `handoff-navigation-ui.md`).
This is a much larger structural change than anything in this repo's recent sessions - the individual pieces
(each stripped panel, each new dock class) are small and mechanical, but there's a lot of them, and the
"does the whole thing actually lay out and look right" question can only be answered by opening the project.
Specific things worth checking first:
- Does the sidebar actually sit flush against the left edge, full height below the status bar, at 160px?
- Does the track strip's collapse toggle actually shrink it to just the header row?
- Does `OrbitPanel` really land bottom-right of the NAV tab (not overlapping the existing 340px sidebar, not
  clipped off-screen)?
- Switching major modes and minor tabs a few times each, to catch any `SetActive` mismatch or stale label.
