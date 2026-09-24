using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================================================
// Probe loadout, chosen at the REFIT between runs. Nothing is bought: home lends hardware in proportion to how
// much it trusts the program. Every system and tier is always on offer; the only rule is that the summed cost of
// what's fitted stays within the trust budget (Loadout.Budget). That lets a run specialise (waterfall +
// spectrometer one run, a big imager the next) instead of climbing one fixed ladder.
//
// Levels: 0 = not fitted (sensors) / stock (modules), 1..3 = Mk I..Mk III. The waterfall Mk I is always fitted and
// free: a zero-trust probe can still hear contacts and earn its way back.
//
// Sensor tiers are built from the spec assets in Resources/Specs: the asset of each type is Mk I (your current
// tuning), and Mk II / Mk III are clones improved by LoadoutSpecs.Improve. An asset named exactly
// "<Type>Spec_Mk2" (e.g. ImagerSpec_Mk2) overrides the generated tier, so any tier can be hand-tuned in the editor.
// Modules scale the ProbeSpec asset (battery, tank, storage, transmitter).
// =============================================================================================================

public enum ProbeSystem { Waterfall, Imager, Spectrometer, Radar, Battery, Tank, Storage, Transmitter, NavComputer }

[Serializable]
public sealed class LoadoutPick
{
    public ProbeSystem system;
    public int level;
}

public sealed class Loadout
{
    public const int MaxLevel = 3;
    public static readonly ProbeSystem[] All = (ProbeSystem[])Enum.GetValues(typeof(ProbeSystem));

    // Total cost of each level (not incremental), per system, indexed [system][level].
    private static readonly int[][] Costs =
    {
        new[] { 0, 0, 4, 8 },   // Waterfall (Mk I free, always fitted)
        new[] { 0, 3, 6, 10 },  // Imager
        new[] { 0, 3, 6, 10 },  // Spectrometer
        new[] { 0, 4, 7, 11 },  // Radar
        new[] { 0, 2, 4, 7 },   // Battery
        new[] { 0, 2, 4, 7 },   // Tank
        new[] { 0, 2, 4, 7 },   // Storage
        new[] { 0, 2, 4, 7 },   // Transmitter
        new[] { 0, 3, 6, 10 },  // NavComputer
    };

    private readonly int[] _level = new int[All.Length];

    public event Action Changed;

    public Loadout() { ResetToBaseline(); }

    public static bool IsSensor(ProbeSystem s) => s <= ProbeSystem.Radar;
    public static int MinLevel(ProbeSystem s) => s == ProbeSystem.Waterfall ? 1 : 0;
    public static int Cost(ProbeSystem s, int level) => Costs[(int)s][Mathf.Clamp(level, 0, MaxLevel)];

    /// <summary>
    /// Hardware budget home will lend at this trust. Grows with trust squared: trust 50 gives 8 points (two
    /// Mk I sensors and a module), trust 75 gives 17, trust 100 gives 30 - still well short of everything at
    /// Mk III (74), so a run always has to choose.
    /// </summary>
    public static int Budget(float trust)
    {
        float f = Mathf.Clamp01(trust / GameState.TrustMax);
        return Mathf.FloorToInt(30f * f * f + 0.5f);
    }

    public int Level(ProbeSystem s) => _level[(int)s];
    public bool Fitted(ProbeSystem s) => _level[(int)s] > 0;

    public void Set(ProbeSystem s, int level)
    {
        level = Mathf.Clamp(level, MinLevel(s), MaxLevel);
        if (_level[(int)s] == level) return;
        _level[(int)s] = level;
        Changed?.Invoke();
    }

    public int TotalCost
    {
        get { int c = 0; foreach (ProbeSystem s in All) c += Cost(s, _level[(int)s]); return c; }
    }

    /// <summary>Waterfall Mk I and stock modules only (New Game).</summary>
    public void ResetToBaseline()
    {
        for (int i = 0; i < _level.Length; i++) _level[i] = MinLevel(All[i]);
        Changed?.Invoke();
    }

    public List<LoadoutPick> ToList()
    {
        var list = new List<LoadoutPick>();
        foreach (ProbeSystem s in All) list.Add(new LoadoutPick { system = s, level = _level[(int)s] });
        return list;
    }

    public void FromList(List<LoadoutPick> picks)
    {
        for (int i = 0; i < _level.Length; i++) _level[i] = MinLevel(All[i]);
        if (picks != null)
            foreach (LoadoutPick p in picks)
                if (p != null && (int)p.system >= 0 && (int)p.system < _level.Length)
                    _level[(int)p.system] = Mathf.Clamp(p.level, MinLevel(p.system), MaxLevel);
        Changed?.Invoke();
    }

    /// <summary>Saves from before loadouts existed: every sensor worked, so fit them all at Mk I.</summary>
    public void SetLegacy()
    {
        ResetToBaseline();
        _level[(int)ProbeSystem.Imager] = 1;
        _level[(int)ProbeSystem.Spectrometer] = 1;
        _level[(int)ProbeSystem.Radar] = 1;
        Changed?.Invoke();
    }
}

/// <summary>Turns loadout levels into the spec objects the sensors and the probe actually run on.</summary>
public static class LoadoutSpecs
{
    private static readonly Dictionary<string, SensorSpec> _cache = new Dictionary<string, SensorSpec>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _cache.Clear();

    public static Type SpecType(ProbeSystem s)
    {
        switch (s)
        {
            case ProbeSystem.Waterfall:    return typeof(WaterfallSpec);
            case ProbeSystem.Imager:       return typeof(ImagerSpec);
            case ProbeSystem.Spectrometer: return typeof(SpectrometerSpec);
            case ProbeSystem.Radar:        return typeof(RadarSpec);
            default: return null;
        }
    }

    private static SensorKind KindOf(ProbeSystem s)
    {
        switch (s)
        {
            case ProbeSystem.Imager:       return SensorKind.Imager;
            case ProbeSystem.Spectrometer: return SensorKind.Spectrometer;
            case ProbeSystem.Radar:        return SensorKind.Radar;
            default:                       return SensorKind.Waterfall;
        }
    }

    /// <summary>The spec of that sensor at that level (1..3), or null for level 0 / a module.</summary>
    public static SensorSpec Sensor(ProbeSystem s, int level, IList<SensorSpec> assets)
    {
        Type type = SpecType(s);
        if (type == null || level <= 0) return null;
        level = Mathf.Min(level, Loadout.MaxLevel);
        string name = type.Name + "_Mk" + level;
        if (_cache.TryGetValue(name, out SensorSpec cached) && cached != null) return cached;

        SensorSpec spec = null, baseAsset = null;
        if (assets != null)
            foreach (SensorSpec a in assets)
            {
                // Match by C# type, not by the asset's kind field: an asset's kind is only fixed by OnValidate
                // when it's edited in the inspector, so a stale one can say "Waterfall" on a radar.
                if (a == null || a.GetType() != type) continue;
                if (a.name == name) { spec = a; break; }
                if (baseAsset == null && a.name.IndexOf("_Mk", StringComparison.Ordinal) < 0) baseAsset = a;
            }

        if (spec == null)
        {
            spec = baseAsset != null ? UnityEngine.Object.Instantiate(baseAsset) : (SensorSpec)ScriptableObject.CreateInstance(type);
            spec.name = name;
            Improve(spec, level);
        }
        spec.kind = KindOf(s);
        spec.tier = level;
        _cache[name] = spec;
        return spec;
    }

    /// <summary>Mk II and Mk III from Mk I. k = 0, 1, 2.</summary>
    private static void Improve(SensorSpec spec, int level)
    {
        int k = level - 1;
        if (k <= 0) return;
        float p2 = Mathf.Pow(2f, k);

        switch (spec)
        {
            case WaterfallSpec w:
                // Bigger array: beam halves per tier (1 deg -> 0.5 -> 0.25 at the default 70 m), more gain.
                w.apertureMeters *= p2;
                w.noiseSigma *= Mathf.Pow(0.7f, k);
                w.maxBins = Mathf.Max(w.maxBins, w.UsefulBins);
                w.gateBins = Mathf.RoundToInt(w.gateBins * p2); // same association gate in degrees: the tracker
                                                                 // was tuned for it; the finer beam still sharpens bearings
                w.maxTracks += 2 * k;
                w.powerDraw *= Mathf.Pow(1.5f, k);
                break;

            case ImagerSpec im:
                // Longer focal length (narrower minimum FOV), quieter detector, deeper integrator.
                im.fovMin /= p2;
                im.blockNoise /= p2;
                im.integratorDepth = Mathf.RoundToInt(im.integratorDepth * (1f + 0.5f * k));
                im.powerDraw *= Mathf.Pow(1.5f, k);
                break;

            case SpectrometerSpec sp:
                // Finer grating: R 200 -> 1 000 -> 10 000. Mk III separates the Na D doublet easily and reads
                // Doppler shifts to a few km/s.
                sp.resolvingPower *= k == 1 ? 5f : 50f;
                sp.maxPointingSigmaDeg *= 1f + 0.5f * k; // wider slit acceptance / better guiding
                sp.powerDraw *= Mathf.Pow(1.5f, k);
                break;

            case RadarSpec r:
                // x4 echo SNR per tier (bigger dish + transmitter): detection range grows x1.41 (R^-4).
                r.referenceSnr *= Mathf.Pow(4f, k);
                r.trackBeamDeg /= p2;
                r.energyPerPing *= Mathf.Pow(1.5f, k);
                break;
        }
    }

    // ---- Modules ------------------------------------------------------------------------------------------

    private static readonly float[] CapacityFactor = { 1f, 1.5f, 2f, 3f };
    private static readonly float[] TxRangeFactor = { 1f, 1.41f, 2f, 2.83f }; // transmitter power x2 per tier
    private static readonly float[] TxRateFactor = { 1f, 1.5f, 2f, 3f };

    /// <summary>A copy of the base probe with the fitted modules applied. The base asset is never modified.</summary>
    public static ProbeSpec Probe(ProbeSpec baseSpec, Loadout lo)
    {
        ProbeSpec p = baseSpec != null ? UnityEngine.Object.Instantiate(baseSpec) : ScriptableObject.CreateInstance<ProbeSpec>();
        p.name = "ProbeSpec_Fitted";
        if (lo == null) return p;
        p.powerCapacity    *= CapacityFactor[lo.Level(ProbeSystem.Battery)];
        p.hydrogenCapacity *= CapacityFactor[lo.Level(ProbeSystem.Tank)];
        p.storageCapacity  *= CapacityFactor[lo.Level(ProbeSystem.Storage)];
        p.txRangeLy        *= TxRangeFactor[lo.Level(ProbeSystem.Transmitter)];
        p.txRate           *= TxRateFactor[lo.Level(ProbeSystem.Transmitter)];
        return p;
    }

    /// <summary>One line of what this level gives, for the refit screen.</summary>
    public static string Describe(ProbeSystem s, int level, IList<SensorSpec> assets, ProbeSpec baseProbe)
    {
        if (Loadout.IsSensor(s))
        {
            if (level <= 0) return Loadout.MinLevel(s) > 0 ? "" : Loc.Get("refit.none");
            SensorSpec spec = Sensor(s, level, assets);
            switch (spec)
            {
                case WaterfallSpec w:
                    return Loc.Get("refit.d.waterfall", w.BeamwidthDeg, w.detectionThresholdSigma * w.noiseSigma * 1000f, w.maxTracks, w.powerDraw);
                case ImagerSpec im:
                    return Loc.Get("refit.d.imager", im.fovMin, im.blockNoise * 1000f, im.integratorDepth, im.powerDraw);
                case SpectrometerSpec sp:
                    return Loc.Get("refit.d.spectro", sp.resolvingPower, 299792.458f / sp.resolvingPower, sp.powerDraw);
                case RadarSpec r:
                    // Where an Earth-sized, albedo-0.3 body is still at the detection threshold.
                    float reach = r.referenceRangeAu * Mathf.Pow(r.referenceSnr / Mathf.Max(0.1f, r.detectionThresholdSigma), 0.25f);
                    return Loc.Get("refit.d.radar", reach, r.trackBeamDeg, r.energyPerPing);
            }
            return "";
        }

        ProbeSpec b = baseProbe != null ? baseProbe : ScriptableObject.CreateInstance<ProbeSpec>();
        switch (s)
        {
            case ProbeSystem.Battery:     return Loc.Get("refit.d.battery", b.powerCapacity * CapacityFactor[level]);
            case ProbeSystem.Tank:        return Loc.Get("refit.d.tank", b.hydrogenCapacity * CapacityFactor[level], b.hydrogenCapacity * CapacityFactor[level] / Mathf.Max(0.01f, b.hydrogenPerKmS));
            case ProbeSystem.Storage:     return Loc.Get("refit.d.storage", b.storageCapacity * CapacityFactor[level]);
            case ProbeSystem.Transmitter: return Loc.Get("refit.d.tx", b.txRangeLy * TxRangeFactor[level], b.txRate * TxRateFactor[level]);
            case ProbeSystem.NavComputer: return level > 0 ? Loc.Get("refit.d.nav." + level) : Loc.Get("refit.d.nav.0");
        }
        return "";
    }
}

/// <summary>What each NavComputer tier unlocks. Only HasSystemView does anything yet (the NAV screen); the
/// others name where the rest of the navigation roadmap (transfer planner, galaxy map, orbit determination
/// from tracks) plugs in once it's built, so the tier gate doesn't have to be revisited then.</summary>
public static class NavTier
{
    public static bool HasSystemView(int level)       => level >= 1; // ship orbit, Pe/Ap/AN/DN, node preview
    public static bool HasTransferPlanner(int level)   => level >= 2; // node placement on the map, predicted path
    public static bool HasGalaxyMap(int level)         => level >= 2;
    public static bool HasOrbitDetermination(int level) => level >= 3; // fit tracked bodies' orbits from history
}
