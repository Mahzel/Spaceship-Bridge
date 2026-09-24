using System.Linq;
using UnityEngine;

/// <summary>
/// Global access point to the game services. Screens and systems read Game.Clock / Game.State / Game.Run
/// instead of finding objects in the scene.
///
/// Bootstraps itself before the first scene loads, so no scene setup is required.
/// </summary>
public static class Game
{
    public static GameClock     Clock { get; private set; }
    public static GameState     State { get; private set; }
    public static RunController Run   { get; private set; }
    public static JumpDrive     Jump  { get; private set; }
    public static WakeMonitor   Wake  { get; private set; }

    // Keeps things correct when "Enter Play Mode without domain reload" is enabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Clock = null;
        State = null;
        Run   = null;
        Jump  = null;
        Wake  = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Clock = new GameClock();
        State = new GameState();
        State.WorldSeed = PlayerPrefs.GetInt("BaseSeed", GameConstants.DEFAULT_BASE_SEED);

        // Specs live in Assets/Resources/Specs/: each sensor's asset is its Mk I, the ProbeSpec the stock probe.
        // The loadout (baseline: waterfall Mk I, stock modules) turns them into the fitted probe.
        State.SpecAssets.AddRange(Resources.LoadAll<SensorSpec>("Specs"));
        var probeSpecs = Resources.LoadAll<ProbeSpec>("Specs");
        if (probeSpecs.Length > 0) State.BaseProbe = probeSpecs[0];
        State.ApplyLoadout();

        Run  = new RunController(Clock, State);
        State.Link.Now = () => Clock.SimSeconds;
        State.Link.RunNumber = () => Run != null ? Run.RunNumber : 1;
        State.Link.DistanceAu = () =>
        {
            double ly = Jump != null ? Jump.DistanceFromHomeLy * Transmitter.AuPerLy : 0.0;
            double au = System.Math.Sqrt(State.Ship.x * State.Ship.x + State.Ship.z * State.Ship.z) / 100.0;
            return System.Math.Sqrt(ly * ly + au * au);
        };
        Jump = new JumpDrive(Clock, State, Run);
        Wake = new WakeMonitor(Clock, State, Run);

        // Earlier versions created [Game] with HideFlags.DontSave, which survives leaving Play mode as a
        // frozen ghost (dead buttons, old values). Remove any leftover before creating the real one.
        foreach (GameObject leftover in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (leftover.name == "[Game]" && (leftover.hideFlags & HideFlags.DontSave) != 0)
                Object.DestroyImmediate(leftover);
        }

        // DontDestroyOnLoad only: it is destroyed when Play mode ends, no ghost left behind.
        var go = new GameObject("[Game]");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GameClockDriver>();
        go.AddComponent<RunDriver>();

        // Always-present UI: status bar, panels, debrief and the menus.
        go.AddComponent<GameUI>();

        // Omniscient developer overlay (F1): editor and development builds only.
        if (Debug.isDebugBuild) go.AddComponent<DevHud>();

        // Autosave into the "Autosave" slot at every launch and every debrief.
        Run.Launched += Autosave;
        Run.RunEnded += _ => Autosave();

        // Nothing runs until the player picks NEW GAME on the main menu (GameUI shows it while Phase is Idle).
        Clock.SetPaused(true);
    }

    /// <summary>
    /// Starts a fresh expedition in the world `seed`: clock back to 0, atlas and trust cleared, then the refit
    /// for run #1 (LAUNCH there sends it to the home system). The seed is remembered (PlayerPrefs "BaseSeed").
    /// </summary>
    public static void NewGame(int seed)
    {
        if (State == null || Run == null || Clock == null) return;
        State.WorldSeed = seed;
        PlayerPrefs.SetInt("BaseSeed", seed);
        PlayerPrefs.Save();

        Clock.SetTime(0.0);
        State.ResetWorld();
        Run.ResetForNewGame();
        Run.BeginRefit(); // the refit screen's LAUNCH -> BeginRun -> JumpDrive home + SystemManager spawns home
    }

    // ---------------------------------------------------------------------------------------------------------
    // Save / load (see SaveGame.cs). Only an expedition in progress (flight or debrief) can be saved.

    /// <summary>The refit before run #1 isn't saveable: no system has been entered yet.</summary>
    public static bool CanSave => Run != null && Run.Phase != RunPhase.Idle && !(Run.Phase == RunPhase.Refit && Run.RunNumber == 0)
                                  && State != null && SystemManager.Current != null;

    public static SaveData CaptureSave()
    {
        if (!CanSave) return null;
        var d = new SaveData();
        d.meta.version = SaveSystem.Version;
        d.meta.savedAtUtc = System.DateTime.UtcNow.ToString("o");
        d.meta.worldSeed = State.WorldSeed;
        d.meta.runNumber = Run.RunNumber;
        d.meta.phase = Run.Phase;
        d.meta.day = Clock.SimSeconds / 86400.0;
        d.meta.systemId = Jump != null ? Jump.CurrentSystemId : "";

        d.worldSeed = State.WorldSeed;
        d.trust = State.Trust;
        d.trustHistory.AddRange(State.TrustHistory);
        d.objectiveId = State.ActiveObjectiveId;
        d.hasLoadout = true;
        d.loadout.AddRange(State.Loadout.ToList());
        d.atlas.AddRange(State.Atlas.Entries);
        d.atlasNextId = State.Atlas.NextIdForSave;
        d.linkLog.AddRange(State.Link.Log);

        d.simSeconds = Clock.SimSeconds;
        d.phase = Run.Phase;
        d.runNumber = Run.RunNumber;
        d.runStartSimSeconds = Run.RunStartSimSeconds;
        d.hasSummary = Run.LastSummary != null;
        d.lastSummary = Run.LastSummary;

        d.systemId = Jump.CurrentSystemId;
        d.galaxyX = Jump.X; d.galaxyZ = Jump.Z;
        ShipState ship = State.Ship;
        d.shipX = ship.x; d.shipY = ship.y; d.shipZ = ship.z;
        d.shipVx = ship.vx; d.shipVy = ship.vy; d.shipVz = ship.vz; d.shipHeading = ship.headingDeg;

        d.power = State.PowerStored;
        d.hydrogen = State.Hydrogen;
        d.reactorLevel = State.ReactorLevel;
        d.maneuver.AddRange(State.Maneuver.QueueForSave);
        d.targetBody = State.TargetBodyName;

        foreach (Track t in State.Tracks.All) d.tracks.Add(TrackSave.From(t));
        d.trackNextId = State.Tracks.NextIdForSave;
        d.trackNextName = State.Tracks.NextNameForSave;
        d.trackSelected = State.Tracks.SelectedId;
        d.trackGeneration = State.Tracks.Generation;
        d.trackPendingName = State.Tracks.PendingName;

        d.records.AddRange(State.Data.Records);
        d.recordNextId = State.Data.NextIdForSave;
        d.compressNewRaw = State.Data.compressNewRaw;
        d.sent.AddRange(State.Link.Sent);
        d.sentNextId = State.Link.NextIdForSave;
        d.linkLogged = State.Link.LoggedForSave;
        return d;
    }

    /// <summary>
    /// Restores a saved expedition. Order matters: the probe is refitted first (capacities, sensor processors),
    /// then the system is spawned (which wipes tracks and parks the ship), and only then are the ship's state,
    /// tracks, storage and transmissions put back exactly as saved. Sensor settings and displays start fresh.
    /// The game comes back paused.
    /// </summary>
    public static bool ApplySave(SaveData d)
    {
        SystemManager sm = SystemManager.Current;
        if (d == null || sm == null || State == null || Run == null || Clock == null || Jump == null) return false;

        // JsonUtility writes null strings as "": put the nulls back where code tests for null.
        foreach (DataRecord r in d.records) { r.catalogueName = NullIfEmpty(r.catalogueName); r.bodyName = NullIfEmpty(r.bodyName); }
        foreach (AtlasEntry e in d.atlas) e.bodyName = NullIfEmpty(e.bodyName);
        foreach (TrackSave t in d.tracks)
        {
            t.signatureKey = NullIfEmpty(t.signatureKey); t.catalogName = NullIfEmpty(t.catalogName);
            t.bodyType = NullIfEmpty(t.bodyType); t.surfaceClass = NullIfEmpty(t.surfaceClass);
        }

        State.WorldSeed = d.worldSeed;
        PlayerPrefs.SetInt("BaseSeed", d.worldSeed);
        PlayerPrefs.Save();

        // Saves from before loadouts: every sensor worked then, so keep them (at Mk I).
        if (d.hasLoadout) State.Loadout.FromList(d.loadout);
        else State.Loadout.SetLegacy();
        State.Atlas.Restore(d.atlas, d.atlasNextId);

        Clock.SetPaused(true);
        Clock.SetTime(d.simSeconds);
        State.ResetProbe();   // fits the loadout: capacities, loads, sensors; clears tracks/data/link/maneuver
        State.RestoreResources(d.power, d.hydrogen, d.reactorLevel, d.trust, NullIfEmpty(d.objectiveId));
        State.TrustHistory.Clear();
        if (d.trustHistory != null) State.TrustHistory.AddRange(d.trustHistory);
        State.Link.Restore(d.sent, d.linkLog, d.sentNextId, d.linkLogged);

        Jump.Restore(d.systemId, d.galaxyX, d.galaxyZ);
        sm.JumpToSystem(d.systemId); // spawns the bodies at d.simSeconds and parks the ship (overwritten below)

        ShipState ship = State.Ship;
        ship.Place(d.shipX, d.shipY, d.shipZ, d.shipHeading);
        ship.vx = d.shipVx; ship.vy = d.shipVy; ship.vz = d.shipVz;
        State.ShipOrbit.Reset(); // re-solved from the restored state on the next frame

        var tracks = new System.Collections.Generic.List<Track>();
        foreach (TrackSave ts in d.tracks) tracks.Add(ts.ToTrack());
        State.Tracks.Restore(tracks, d.trackNextId, d.trackNextName, d.trackSelected, d.trackGeneration, d.trackPendingName);
        State.Data.Restore(d.records, d.recordNextId, d.compressNewRaw);
        State.Maneuver.Restore(d.maneuver);
        State.SetTarget(NullIfEmpty(d.targetBody));

        Run.Restore(d.phase, d.runNumber, d.runStartSimSeconds, d.hasSummary ? d.lastSummary : null);
        return true;
    }

    public static void Autosave()
    {
        if (!CanSave) return;
        if (!SaveSystem.Save(SaveSystem.AutosaveName, out string err)) Debug.LogWarning("[Autosave] " + err);
    }

    private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>Leaves the current expedition for the main menu (no debrief, nothing filed).</summary>
    public static void ReturnToMainMenu()
    {
        if (Run != null) Run.Abandon();
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

/// <summary>Advances the clock from unscaled real time, before any other script runs.</summary>
[DefaultExecutionOrder(-1000)]
public class GameClockDriver : MonoBehaviour
{
    private void Update()
    {
        Game.Clock?.Tick(Time.unscaledDeltaTime);
    }
}

/// <summary>Runs the expedition loop each frame, after SystemManager has positioned the bodies.</summary>
[DefaultExecutionOrder(-50)]
public class RunDriver : MonoBehaviour
{
    private void Update()
    {
        if (Game.Run == null) return;

        float flux = SystemManager.Current != null ? SystemManager.Current.StellarFluxAtShip() : 0f;
        Game.Run.Tick(flux);
        if (Game.Wake != null) Game.Wake.Poll();

        // Sensor processing runs every real frame regardless of which console mode is on screen.
        if (Game.State != null && Game.State.Waterfall != null) Game.State.Waterfall.Tick(Time.unscaledDeltaTime);
        if (Game.State != null && Game.State.Radar != null) Game.State.Radar.Tick(Time.unscaledDeltaTime);

        // Maneuver nodes fire the instant SimSeconds reaches them, which is what SystemManager (running
        // earlier this same frame, DefaultExecutionOrder -100) has already advanced ShipOrbit to.
        if (Game.State != null && Game.State.Maneuver != null) Game.State.Maneuver.Tick();
    }
}
