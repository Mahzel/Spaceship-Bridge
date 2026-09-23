using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One piece of survey data as it becomes permanently known back home. Created only when a DataRecord actually
/// makes it there — carried back by a returning probe, or transmitted and received (see Transmitter.Resolve) —
/// never from anything still stuck on a dead or in-flight probe.
///
/// A false entry is rolled once, at creation, deterministically: it is not a bug or a later corruption, it's
/// the record having been wrong from the moment it was logged (a bearing/range claim built on a shaky track).
/// isFalse is ground truth the player is never shown directly — the whole point is that a false entry looks
/// exactly like a real one until (if ever) it is caught at review.
/// </summary>
public sealed class AtlasEntry
{
    public int id;
    public string systemId;
    public string label;
    public DataKind kind;
    public float value;

    /// <summary>Track quality this entry's data was captured at (DataRecord.sourceQuality). Not shown as a
    /// "this one might be fake" flag in the UI — that would give the false entries away — but useful context.</summary>
    public float sourceQuality;

    public int runNumber;
    public double recordedTime;

    /// <summary>Ground truth: does this entry correspond to nothing real. Never surfaced directly in the UI.</summary>
    public bool isFalse;

    /// <summary>Set once a debrief review flags this entry as wrong. Only entries with isFalse can ever be
    /// caught; catching one is a separate, later roll (GameState.GetReviewChance) — not wired up yet.</summary>
    public bool caught;
}

/// <summary>
/// The persistent survey log: every DataRecord that ever made it home, across every run this session.
/// Owned directly by GameState, and — unlike Tracks/Data/Waterfall — never touched by ResetProbe, so entries
/// accumulate across the whole play session. Session-only for now: nothing here is serialized to disk yet: a
/// fresh app launch starts with an empty Atlas (see GameState's save-file comment for the eventual scope).
/// Plain C#, no scene dependencies.
/// </summary>
public sealed class Atlas
{
    private readonly List<AtlasEntry> _entries = new List<AtlasEntry>();
    private int _nextId = 1;

    public IList<AtlasEntry> Entries { get { return _entries; } }
    public event Action Changed;

    /// <summary>
    /// Files one delivered DataRecord. If an entry already exists for the same contact (same system + track
    /// name) filed THIS run, the record is merged into it (value adds up, sourceQuality takes the worse of the
    /// two, kind upgrades to Raw if either side is) rather than creating a second sighting of the same thing —
    /// a stub logged early and a raw recording started later on the same track both belong to one entry.
    /// Merging never re-rolls isFalse: a contact's false/real status is decided once, the first time it's
    /// filed, from that first record's sourceQuality — not nudged by everything merged in afterwards.
    ///
    /// The false-entry roll itself reuses Transmitter's own deterministic hash (rather than inventing a second
    /// RNG scheme) with sentinel packet=-1/attempt=-1 args — real packet rolls only ever use non-negative
    /// indices, so this can never collide with (or get re-rolled by re-checking) a packet-loss roll. Odds of a
    /// false entry fall as sourceQuality rises: a record captured entirely off a quality-1.0 confirmed track is
    /// never wrong; one built on a near-zero quality coasting track is wrong most of the time.
    /// </summary>
    public AtlasEntry Log(DataRecord r, int worldSeed, int runNumber, double time)
    {
        if (r == null) return null;

        AtlasEntry existing = FindForMerge(r.systemId, r.label, runNumber);
        if (existing != null)
        {
            existing.value += r.value;
            existing.sourceQuality = Mathf.Min(existing.sourceQuality, r.sourceQuality);
            existing.recordedTime = time;
            if (r.kind == DataKind.Raw) existing.kind = DataKind.Raw;
            if (Changed != null) Changed();
            return existing;
        }

        var e = new AtlasEntry();
        e.id = _nextId++;
        e.systemId = r.systemId;
        e.label = r.label;
        e.kind = r.kind;
        e.value = r.value;
        e.sourceQuality = r.sourceQuality;
        e.runNumber = runNumber;
        e.recordedTime = time;

        float falseChance = Mathf.Clamp01(0.85f * (1f - Mathf.Clamp01(r.sourceQuality)));
        double roll = Transmitter.Roll(worldSeed, runNumber, r.id, -1, -1);
        e.isFalse = roll < falseChance;

        _entries.Add(e);
        if (Changed != null) Changed();
        return e;
    }

    private AtlasEntry FindForMerge(string systemId, string label, int runNumber)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            AtlasEntry e = _entries[i];
            if (e.runNumber == runNumber && e.systemId == systemId && e.label == label) return e;
        }
        return null;
    }

    /// <summary>Marks an entry as caught-wrong at review. Not called anywhere yet — the review/debrief side of
    /// GetReviewChance isn't wired up. Kept ready for that pass.</summary>
    public void MarkCaught(int entryId)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].id == entryId) { _entries[i].caught = true; break; }
        if (Changed != null) Changed();
    }
}
