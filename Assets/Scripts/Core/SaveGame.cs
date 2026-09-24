using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// =============================================================================================================
// Save files: named slots under Application.persistentDataPath/saves, one JSON file per slot plus a tiny
// ".meta" header for the slot list. A save holds the expedition AND the probe mid-flight, but not sensor
// settings or display contents (those start fresh on load: exposure, zoom, tilt, waterfall image, radar scope).
// Systems themselves are never saved: they regenerate from the world seed.
// =============================================================================================================

/// <summary>What the slot list shows without opening the full file.</summary>
[Serializable]
public sealed class SaveMeta
{
    public int version;
    public string slotName;
    public string savedAtUtc;     // ISO 8601
    public int worldSeed;
    public int runNumber;
    public RunPhase phase;
    public double day;            // simulated days since the expedition began
    public string systemId;
}

[Serializable]
public sealed class TrackSave
{
    public int id;
    public string name;
    public TrackStatus status;
    public float bearing, rateDegPerDay, rateSigmaDegPerDay;
    public bool hasRate;
    public double lastTime;
    public float lastSnr, snrAvg, quality;
    public int hits, misses, consecutiveMisses, updates;
    public List<BearingSample> history = new List<BearingSample>();
    public RangeEstimate range, tmaRange, radarFix;
    public double radarFixTime, radarRangeRateKmS;
    public bool hasRadarRate;
    public bool hasElevation;
    public float elevationDeg, elevationSigmaDeg;
    public double elevationFixTime;
    public ElevationSource elevationSource;
    public float angularRadiusDeg;
    public double supportHoldUntil;
    public ElevationSource lastSupport;
    public bool lostLock;

    // TrackInfo
    public float specDwellSeconds;
    public string signatureKey;
    public bool identified, blended, isStar;
    public string catalogName, bodyType, surfaceClass;
    public float temperatureK, surfaceTemperatureK, metallicity, ageGyr;
    public List<ChemicalComposition> composition = new List<ChemicalComposition>();
    public bool hasAtmosphere;
    public Atmosphere atmosphere;

    public static TrackSave From(Track t)
    {
        var s = new TrackSave
        {
            id = t.id, name = t.name, status = t.status, bearing = t.bearing, rateDegPerDay = t.rateDegPerDay,
            rateSigmaDegPerDay = float.IsInfinity(t.rateSigmaDegPerDay) ? -1f : t.rateSigmaDegPerDay,
            hasRate = t.hasRate, lastTime = t.lastTime, lastSnr = t.lastSnr, snrAvg = t.snrAvg, quality = t.quality,
            hits = t.hits, misses = t.misses, consecutiveMisses = t.consecutiveMisses, updates = t.updates,
            range = t.range, tmaRange = t.tmaRange, radarFix = t.radarFix, radarFixTime = t.radarFixTime,
            radarRangeRateKmS = t.radarRangeRateKmS, hasRadarRate = t.hasRadarRate,
            hasElevation = t.hasElevation, elevationDeg = t.elevationDeg, elevationSigmaDeg = t.elevationSigmaDeg,
            elevationFixTime = t.elevationFixTime, elevationSource = t.elevationSource,
            angularRadiusDeg = t.angularRadiusDeg, supportHoldUntil = t.supportHoldUntil,
            lastSupport = t.lastSupport, lostLock = t.lostLock,
            specDwellSeconds = t.info.specDwellSeconds, signatureKey = t.info.signatureKey,
            identified = t.info.identified, blended = t.info.blended, isStar = t.info.isStar,
            catalogName = t.info.catalogName, bodyType = t.info.bodyType, surfaceClass = t.info.surfaceClass,
            temperatureK = t.info.temperatureK, surfaceTemperatureK = t.info.surfaceTemperatureK,
            metallicity = t.info.metallicity, ageGyr = t.info.ageGyr,
            hasAtmosphere = t.info.atmosphere != null, atmosphere = t.info.atmosphere,
        };
        s.history.AddRange(t.history);
        s.composition.AddRange(t.info.composition);
        return s;
    }

    public Track ToTrack()
    {
        var t = new Track
        {
            id = id, name = name, status = status, bearing = bearing, rateDegPerDay = rateDegPerDay,
            rateSigmaDegPerDay = rateSigmaDegPerDay < 0f ? float.PositiveInfinity : rateSigmaDegPerDay,
            hasRate = hasRate, lastTime = lastTime, lastSnr = lastSnr, snrAvg = snrAvg, quality = quality,
            hits = hits, misses = misses, consecutiveMisses = consecutiveMisses, updates = updates,
            range = range, tmaRange = tmaRange, radarFix = radarFix, radarFixTime = radarFixTime,
            radarRangeRateKmS = radarRangeRateKmS, hasRadarRate = hasRadarRate,
            hasElevation = hasElevation, elevationDeg = elevationDeg, elevationSigmaDeg = elevationSigmaDeg,
            elevationFixTime = elevationFixTime, elevationSource = elevationSource,
            angularRadiusDeg = angularRadiusDeg, supportHoldUntil = supportHoldUntil,
            lastSupport = lastSupport, lostLock = lostLock,
        };
        t.history.AddRange(history);
        t.info.specDwellSeconds = specDwellSeconds;
        t.info.signatureKey = signatureKey;
        t.info.identified = identified; t.info.blended = blended; t.info.isStar = isStar;
        t.info.catalogName = catalogName; t.info.bodyType = bodyType; t.info.surfaceClass = surfaceClass;
        t.info.temperatureK = temperatureK; t.info.surfaceTemperatureK = surfaceTemperatureK;
        t.info.metallicity = metallicity; t.info.ageGyr = ageGyr;
        t.info.composition.AddRange(composition);
        t.info.atmosphere = hasAtmosphere ? atmosphere : null;
        return t;
    }
}

[Serializable]
public sealed class SaveData
{
    public SaveMeta meta = new SaveMeta();

    // Expedition
    public int worldSeed;
    public float trust;
    public List<TrustChange> trustHistory = new List<TrustChange>();
    public string objectiveId;
    public List<string> installedSpecs = new List<string>(); // obsolete (before loadouts), read by nothing
    public bool hasLoadout;
    public List<LoadoutPick> loadout = new List<LoadoutPick>();
    public List<AtlasEntry> atlas = new List<AtlasEntry>();
    public int atlasNextId;
    public List<LinkLogEntry> linkLog = new List<LinkLogEntry>();

    // Clock and run
    public double simSeconds;
    public RunPhase phase;
    public int runNumber;
    public double runStartSimSeconds;
    public bool hasSummary;
    public RunSummary lastSummary;

    // Where the probe is
    public string systemId;
    public double galaxyX, galaxyZ;
    public double shipX, shipY, shipZ, shipVx, shipVy, shipVz, shipHeading;

    // Probe
    public float power, hydrogen, reactorLevel;
    public List<ManeuverPlan.Node> maneuver = new List<ManeuverPlan.Node>();
    public string targetBody;

    // Tracks (with their full history)
    public List<TrackSave> tracks = new List<TrackSave>();
    public int trackNextId, trackNextName, trackSelected, trackGeneration;
    public string trackPendingName;

    // Storage and transmissions of this run
    public List<DataRecord> records = new List<DataRecord>();
    public int recordNextId;
    public bool compressNewRaw;
    public List<Transmission> sent = new List<Transmission>();
    public int sentNextId;
    public bool linkLogged;
}

/// <summary>Named save slots on disk. Everything goes through Game.CaptureSave / Game.ApplySave.</summary>
public static class SaveSystem
{
    public const int Version = 1;
    public const string AutosaveName = "Autosave";
    private const string Ext = ".json", MetaExt = ".meta";

    public static string Folder => Path.Combine(Application.persistentDataPath, "saves");

    /// <summary>Slot name as typed by the player -> safe file name (letters, digits, space, - and _).</summary>
    public static string FileSafe(string slotName)
    {
        var sb = new StringBuilder();
        foreach (char c in (slotName ?? "").Trim())
            sb.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_');
        string s = sb.ToString().Trim();
        return s.Length == 0 ? "Unnamed" : (s.Length > 48 ? s.Substring(0, 48) : s);
    }

    public static bool Exists(string slotName) => File.Exists(Path.Combine(Folder, FileSafe(slotName) + Ext));

    public static bool Save(string slotName, out string error)
    {
        error = null;
        try
        {
            SaveData d = Game.CaptureSave();
            if (d == null) { error = "nothing to save"; return false; }
            d.meta.slotName = (slotName ?? "").Trim().Length > 0 ? slotName.Trim() : FileSafe(slotName);
            Directory.CreateDirectory(Folder);
            string file = FileSafe(slotName);
            // Write to a temp file first, so a crash mid-write can't corrupt an existing save.
            string path = Path.Combine(Folder, file + Ext), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(d));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            File.WriteAllText(Path.Combine(Folder, file + MetaExt), JsonUtility.ToJson(d.meta));
            return true;
        }
        catch (Exception e) { error = e.Message; Debug.LogWarning("[Save] " + e); return false; }
    }

    public static bool Load(string slotName, out string error)
    {
        error = null;
        try
        {
            string path = Path.Combine(Folder, FileSafe(slotName) + Ext);
            if (!File.Exists(path)) { error = "no such save"; return false; }
            SaveData d = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (d == null || d.meta == null) { error = "unreadable save"; return false; }
            if (d.meta.version > Version) { error = "save is from a newer version"; return false; }
            if (!Game.ApplySave(d)) { error = "could not restore the game"; return false; }
            return true;
        }
        catch (Exception e) { error = e.Message; Debug.LogWarning("[Load] " + e); return false; }
    }

    public static void Delete(string slotName)
    {
        try
        {
            string file = FileSafe(slotName);
            File.Delete(Path.Combine(Folder, file + Ext));
            File.Delete(Path.Combine(Folder, file + MetaExt));
        }
        catch (Exception e) { Debug.LogWarning("[Save] delete: " + e.Message); }
    }

    /// <summary>Every slot, most recent first.</summary>
    public static List<SaveMeta> List()
    {
        var list = new List<SaveMeta>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (string f in Directory.GetFiles(Folder, "*" + MetaExt))
            {
                try
                {
                    SaveMeta m = JsonUtility.FromJson<SaveMeta>(File.ReadAllText(f));
                    if (m != null && File.Exists(Path.ChangeExtension(f, Ext))) list.Add(m);
                }
                catch { /* skip a damaged header */ }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Save] list: " + e.Message); }
        list.Sort((a, b) => string.CompareOrdinal(b.savedAtUtc, a.savedAtUtc));
        return list;
    }

    /// <summary>The most recently written slot, or null.</summary>
    public static SaveMeta Latest()
    {
        List<SaveMeta> all = List();
        return all.Count > 0 ? all[0] : null;
    }
}
