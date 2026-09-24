using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime state of the current world / expedition. Plain C#, no scene dependencies.
/// Saved by SaveGame (see Game.CaptureSave), including the loadout.
/// Systems are regenerated from WorldSeed and are never saved.
///
/// Anything that changes a value goes through a setter/method here so screens can subscribe to Changed.
/// </summary>
public sealed class GameState
{
    // --- World -------------------------------------------------------------
    public int WorldSeed;

    // --- Probe -------------------------------------------------------------
    /// <summary>Baseline numbers for the probe. Replaced by an asset in Resources/Specs if one exists.</summary>
    public ProbeSpec Probe;

    // --- Probe resources (abstract units, tune in ProbeSpec) ----------------
    public float PowerStored     { get; private set; } = 1000f;
    public float PowerCapacity   { get; private set; } = 1000f;
    public float Hydrogen        { get; private set; } = 100f;
    public float HydrogenCapacity{ get; private set; } = 100f;
    public float StorageUsed     => Data.Used;
    public float StorageCapacity => Data.Capacity;

    // --- Power loads (units per simulated day), by name --------------------
    private readonly Dictionary<string, float> _loads = new Dictionary<string, float>();

    // --- Trust -------------------------------------------------------------
    // Single value. Everything reads it through the named functions below.
    public const float TrustMax  = 100f;
    public const float TrustStart = 50f;
    /// <summary>Trust falls faster than it rises (hysteresis).</summary>
    public const float TrustFallMultiplier = 1.5f;

    public float Trust { get; private set; } = TrustStart;

    /// <summary>Every trust change and its cause, across the expedition (see Review). Saved.</summary>
    public readonly List<TrustChange> TrustHistory = new List<TrustChange>();

    // --- Hardware ----------------------------------------------------------
    private readonly Dictionary<SensorKind, SensorSpec> _installed = new Dictionary<SensorKind, SensorSpec>();

    /// <summary>What the player chose at the refit. Turned into installed specs and the fitted Probe by
    /// ApplyLoadout at every launch (ResetProbe). Saved.</summary>
    public readonly Loadout Loadout = new Loadout();

    /// <summary>The ProbeSpec asset as authored (stock modules). Probe is this with the fitted modules applied.</summary>
    public ProbeSpec BaseProbe;

    /// <summary>Every SensorSpec asset in Resources/Specs (Mk I of each sensor, plus any hand-made tier overrides).</summary>
    public readonly List<SensorSpec> SpecAssets = new List<SensorSpec>();

    // --- Motion --------------------------------------------------------------
    public readonly ShipState Ship = new ShipState();

    // --- Orbit: the ship's real osculating orbit around whatever currently dominates it gravitationally.
    // Advanced every frame by SystemManager.MoveShip, alongside Ship. -----------------------------------
    public readonly ShipOrbit ShipOrbit = new ShipOrbit();

    // --- Maneuver planning: queued burns previewed against ShipOrbit and executed when SimSeconds reaches
    // them. Ticked every frame by RunDriver, alongside ShipOrbit's own advance. -----------------------------
    public readonly ManeuverPlan Maneuver = new ManeuverPlan();

    /// <summary>Display name of the track selected as the transfer target (SYSTEM screen, NODE tab or the NAV
    /// map - all three set Tracks.SelectedId + this together). Display only: the actual orbit the planner
    /// (ManeuverPlan.SolveHohmann) uses comes from OrbitFit.TryFit on the selected track itself, never a
    /// lookup by this name. Null/empty until the player selects a track. Not persisted across a system jump
    /// (see ResetProbe/SystemManager).</summary>
    public string TargetBodyName { get; private set; }

    public void SetTarget(string name)
    {
        TargetBodyName = name;
        Changed?.Invoke();
    }

    // --- Tracking (bearing tracks built from waterfall detections) ---------
    public readonly TrackManager Tracks = new TrackManager();

    // --- Storage: what the probe has recorded ------------------------------------
    public readonly DataStore Data = new DataStore();

    // --- Sensors: processing runs continuously once installed and powered, regardless of what's on screen ----
    public WaterfallProcessor Waterfall { get; private set; }

    // --- Radar: active sensor, ticks a pending ping's countdown regardless of what's on screen -------------
    public RadarProcessor Radar { get; private set; }

    // --- Link home -------------------------------------------------------------
    public readonly Transmitter Link;

    // --- Atlas: the persistent survey log. Session-only (not touched by ResetProbe, so entries survive
    // across runs the same way Trust does; nothing here is serialized to disk yet). ------------------------
    public readonly Atlas Atlas = new Atlas();

    public string ActiveObjectiveId;

    public event Action Changed;

    public GameState()
    {
        Probe = ScriptableObject.CreateInstance<ProbeSpec>(); // defaults until Game loads an asset
        BaseProbe = Probe;
        Link = new Transmitter(this);
        Data.CatalogueMatch = Catalogue.Match;
        Catalogue.Seed(Atlas);
    }

    // ---------------------------------------------------------------------
    #region Hardware
    public bool IsSensorInstalled(SensorKind kind) => _installed.ContainsKey(kind);

    /// <summary>Returns the installed spec of that type, or null if the hardware is not installed.</summary>
    public T GetSpec<T>() where T : SensorSpec
    {
        foreach (var s in _installed.Values)
        {
            T typed = s as T;
            if (typed != null) return typed;
        }
        return null;
    }

    public void InstallSensor(SensorSpec spec)
    {
        if (spec == null) return;
        _installed[spec.kind] = spec;
        Changed?.Invoke();
    }

    public void RemoveSensor(SensorKind kind)
    {
        if (_installed.Remove(kind)) Changed?.Invoke();
    }

    /// <summary>
    /// Fits the chosen loadout: installs exactly the chosen sensor tiers and rebuilds Probe from BaseProbe with
    /// the chosen modules. Called at every launch (ResetProbe), so capacities are filled from the new values.
    /// </summary>
    public void ApplyLoadout()
    {
        _installed.Clear();
        foreach (ProbeSystem s in Loadout.All)
        {
            if (!Loadout.IsSensor(s)) continue;
            SensorSpec spec = LoadoutSpecs.Sensor(s, Loadout.Level(s), SpecAssets);
            if (spec != null) _installed[spec.kind] = spec;
        }
        Probe = LoadoutSpecs.Probe(BaseProbe, Loadout);
        Changed?.Invoke();
    }

    /// <summary>
    /// DEV ONLY (DevHud HARDWARE tab): refits the probe in flight, without resetting the run. Tracks, storage,
    /// orbit and transmissions are kept; power and hydrogen keep their fill fraction of the new capacities. A sensor
    /// whose tier changed gets a fresh processor (the waterfall's scrollback and a radar ping in flight are lost).
    /// Ignores the trust budget.
    /// </summary>
    public void DevRefitLive()
    {
        float powerFrac = PowerCapacity > 0f ? PowerStored / PowerCapacity : 1f;
        float h2Frac = HydrogenCapacity > 0f ? Hydrogen / HydrogenCapacity : 1f;

        ApplyLoadout();

        PowerCapacity = Probe.powerCapacity;
        PowerStored = Mathf.Clamp01(powerFrac) * PowerCapacity;
        HydrogenCapacity = Probe.hydrogenCapacity;
        Hydrogen = Mathf.Clamp01(h2Frac) * HydrogenCapacity;
        Data.Capacity = Probe.storageCapacity;

        WaterfallSpec wf = GetSpec<WaterfallSpec>();
        if (wf != null && (Waterfall == null || Waterfall.spec != wf))
        {
            bool on = Waterfall == null || Waterfall.Enabled;
            float tilt = Waterfall != null ? Waterfall.AimElevationDeg : 0f;
            Waterfall = new WaterfallProcessor(wf);
            Waterfall.Enabled = on;
            Waterfall.AimElevationDeg = tilt;
        }
        SetWaterfallLoad(wf);

        RadarSpec rs = GetSpec<RadarSpec>();
        if (rs != null && (Radar == null || Radar.spec != rs)) Radar = new RadarProcessor(rs);
        if (Radar != null) Radar.Enabled = rs != null;

        Changed?.Invoke();
    }

    /// <summary>DEV ONLY: sets trust directly (to try the refit budget at other levels).</summary>
    public void DevSetTrust(float trust)
    {
        Trust = Mathf.Clamp(trust, 0f, TrustMax);
        Changed?.Invoke();
    }

    /// <summary>Hardware points home lends at the current trust (see Loadout.Budget).</summary>
    public int LoadoutBudget => Loadout.Budget(Trust);
    public bool LoadoutWithinBudget => Loadout.TotalCost <= LoadoutBudget;
    #endregion

    // ---------------------------------------------------------------------
    #region Run lifecycle
    /// <summary>
    /// New Game: forgets everything the expedition built up across runs (atlas, trust, objective), on top of
    /// what every relaunch resets (ResetProbe, which BeginRun calls next). Installed hardware stays: that's
    /// the baseline probe, not progress. The world seed is set separately by Game.NewGame.
    /// </summary>
    /// <summary>Save/load: the probe's consumables and the expedition's standing.</summary>
    public void RestoreResources(float power, float hydrogen, float reactorLevel, float trust, string objectiveId)
    {
        PowerStored = Mathf.Clamp(power, 0f, PowerCapacity);
        Hydrogen = Mathf.Clamp(hydrogen, 0f, HydrogenCapacity);
        ReactorLevel = Mathf.Clamp01(reactorLevel);
        Trust = Mathf.Clamp(trust, 0f, TrustMax);
        ActiveObjectiveId = objectiveId;
        Changed?.Invoke();
    }

    public void ResetWorld()
    {
        Atlas.Clear();
        Catalogue.Seed(Atlas);
        Trust = TrustStart;
        TrustHistory.Clear();
        ActiveObjectiveId = null;
        Tracks.Clear();
        ShipOrbit.Reset();
        Maneuver.Clear();
        TargetBodyName = null;
        Loadout.ResetToBaseline();
        Changed?.Invoke();
    }

    /// <summary>
    /// Fills the probe for a new run: full power, hydrogen and storage, and the always-on loads
    /// (computer + baseline waterfall). Loads set by other systems (e.g. a scanning imager) are kept.
    /// </summary>
    public void ResetProbe()
    {
        ApplyLoadout();
        PowerCapacity    = Probe.powerCapacity;
        PowerStored      = PowerCapacity;
        HydrogenCapacity = Probe.hydrogenCapacity;
        Hydrogen         = HydrogenCapacity;
        Data.Clear();
        Data.Capacity              = Probe.storageCapacity;
        Data.stubSize              = Probe.stubSize;
        Data.rawSizePerDay         = Probe.rawSizePerDay;
        Data.rawValuePerDay        = Probe.rawValuePerDay;
        Data.compressedSizeFactor  = Probe.compressedSizeFactor;
        Data.compressedValueFactor = Probe.compressedValueFactor;

        Tracks.Clear();
        ShipOrbit.Reset(); // the old orbit (if any) described a probe that no longer exists
        Maneuver.Clear();  // a node planned in the old system means nothing in the new one
        TargetBodyName = null;
        Link.NewRun();
        ReactorLevel = 0f;

        _loads["core"] = Probe.coreDraw;
        WaterfallSpec wf = GetSpec<WaterfallSpec>();
        if (wf != null)
        {
            if (Waterfall == null || Waterfall.spec != wf) Waterfall = new WaterfallProcessor(wf);
            else Waterfall.Rebuild();
            Waterfall.Enabled = true;
        }
        SetWaterfallLoad(wf);

        // The radar processor always exists (screens and RunDriver read it), but it only fires if a radar is fitted.
        RadarSpec radarSpec = GetSpec<RadarSpec>();
        if (Radar == null || (radarSpec != null && Radar.spec != radarSpec))
            Radar = new RadarProcessor(radarSpec != null ? radarSpec : ScriptableObject.CreateInstance<RadarSpec>());
        Radar.Clear();
        Radar.Enabled = radarSpec != null;

        Changed?.Invoke();
    }

    /// <summary>Powering the sensor off stops its draw along with its processing (see WaterfallProcessor).</summary>
    public void SetWaterfallEnabled(bool on)
    {
        if (Waterfall != null) Waterfall.Enabled = on;
        SetWaterfallLoad(GetSpec<WaterfallSpec>());
    }

    private void SetWaterfallLoad(WaterfallSpec wf)
    {
        float draw = wf != null ? wf.powerDraw : Probe.defaultWaterfallDraw;
        _loads["waterfall"] = (Waterfall == null || Waterfall.Enabled) ? draw : 0f;
    }
    #endregion

    // ---------------------------------------------------------------------
    #region Resources
    /// <summary>Sets (or with 0, removes) a named power load, in units per simulated day.</summary>
    public void SetLoad(string key, float unitsPerDay)
    {
        if (unitsPerDay <= 0f) _loads.Remove(key);
        else                   _loads[key] = unitsPerDay;
    }

    public float TotalLoadPerDay
    {
        get { float t = 0f; foreach (var v in _loads.Values) t += v; return t; }
    }

    /// <summary>Adds (or, if negative, removes) power, clamped to [0, capacity].</summary>
    public void ChangePower(float delta)
    {
        PowerStored = Mathf.Clamp(PowerStored + delta, 0f, PowerCapacity);
        Changed?.Invoke();
    }

    /// <summary>Returns the amount actually consumed (may be less if the tank runs dry).</summary>
    public float ConsumePower(float amount)
    {
        float used = Mathf.Clamp(amount, 0f, PowerStored);
        PowerStored -= used;
        Changed?.Invoke();
        return used;
    }

    public void AddPower(float amount) => ChangePower(amount);

    public float ConsumeHydrogen(float amount)
    {
        float used = Mathf.Clamp(amount, 0f, Hydrogen);
        Hydrogen -= used;
        Changed?.Invoke();
        return used;
    }

    public void AddHydrogen(float amount)
    {
        Hydrogen = Mathf.Clamp(Hydrogen + amount, 0f, HydrogenCapacity);
        Changed?.Invoke();
    }

    /// <summary>Hydrogen needed for a burn of this size (either direction).</summary>
    public float BurnCost(float dvKmS) => Mathf.Abs(dvKmS) * Probe.hydrogenPerKmS;

    /// <summary>
    /// Discrete burn along the ship's heading (negative = retro). Spends hydrogen and changes the velocity.
    /// Returns false, changing nothing, if there is not enough hydrogen.
    /// </summary>
    public bool TryBurn(float dvKmS)
    {
        float cost = BurnCost(dvKmS);
        if (dvKmS == 0f || cost > Hydrogen + 1e-4f) return false;
        ConsumeHydrogen(cost);
        Ship.ApplyDeltaV(dvKmS);
        Changed?.Invoke();
        return true;
    }

    // --- Reactor ---------------------------------------------------------------
    /// <summary>0 = off, 1 = full. Output is linear in level, hydrogen burn is level^1.5 (pushing harder is less efficient).</summary>
    public float ReactorLevel { get; private set; }

    public void SetReactorLevel(float level)
    {
        level = Mathf.Clamp01(level);
        if (Hydrogen <= 0f) level = 0f;
        ReactorLevel = level;
        Changed?.Invoke();
    }

    public float ReactorOutputPerDay   => Probe.reactorOutput * ReactorLevel;
    public float ReactorHydrogenPerDay => Probe.reactorHydrogenPerDay * Mathf.Pow(ReactorLevel, 1.5f);

    public bool IsProbeDead => PowerStored <= 0f;
    #endregion

    // ---------------------------------------------------------------------
    #region Trust
    public void ApplyTrustDelta(float delta)
    {
        if (delta < 0f) delta *= TrustFallMultiplier;
        Trust = Mathf.Clamp(Trust + delta, 0f, TrustMax);
        Changed?.Invoke();
    }

    /// <summary>Chance per debrief cycle that a wrong atlas entry is spotted. Placeholder.</summary>
    public float GetReviewChance(float severity01)
        => Mathf.Clamp01(0.1f + 0.6f * severity01 - 0.3f * (Trust / TrustMax));
    #endregion
}
