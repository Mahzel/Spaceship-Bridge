using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One cause of a trust change, as shown in the debrief and kept in the trust history.</summary>
[Serializable]
public sealed class TrustChange
{
    public int run;
    public float delta;       // after TrustFallMultiplier for losses
    public string reason;     // already localised
}

/// <summary>
/// What home does with a returned (or dead) probe's results at each debrief. Trust is how much the humans trust
/// the AI program; it moves only here:
///   1. REWARD: every Atlas entry filed this run pays trust in proportion to its value (a Tentative declaration was
///      already paid TentativeValueFactor when filed). Capped per run.
///   2. CORRECTIONS: a DISPUTED entry is set right when a later run files a confirmed, identified and genuine entry
///      in the same system (the system got properly re-surveyed). Bonus per corrected entry.
///   3. REVIEW: every non-catalogued entry not yet disputed is reviewed. Only a false one can be caught. The chance
///      grows with its severity (Confirmed claims and raw data are scrutinised harder), with scrutiny near home,
///      with the number of reviews it has already gone through (time), and falls as trust rises (GameState.
///      GetReviewChance). The roll is deterministic: hash(world seed, review cycle = run, entry id), so reloading
///      a save can't change it. A caught entry is DISPUTED: its value is clawed back and trust drops, much more
///      for a Confirmed claim than a Tentative one.
///   4. A probe lost to power depletion costs a little trust.
/// Losses go through GameState.ApplyTrustDelta, so they are amplified by TrustFallMultiplier (hysteresis).
/// </summary>
public static class Review
{
    public const float TrustPerValue = 0.5f;
    public const float MaxRewardPerRun = 20f;
    public const float CorrectionBonus = 3f;
    public const float CaughtTentativePenalty = 2f;
    public const float CaughtConfirmedPenalty = 6f;
    public const float ClawbackTrustPerValue = 0.5f;
    public const float ProbeLostPenalty = 2f;

    public static void Process(GameState state, int run, RunEndCause cause, List<AtlasEntry> filedThisRun, RunSummary summary)
    {
        if (state == null || summary == null) return;
        summary.trustBefore = state.Trust;
        summary.trustChanges.Clear();
        summary.entriesFiled = filedThisRun.Count;

        // 1. Reward
        float value = 0f;
        foreach (AtlasEntry e in filedThisRun) value += e.value;
        float reward = Mathf.Min(MaxRewardPerRun, value * TrustPerValue);
        if (reward > 0.01f) Apply(state, summary, run, reward, Loc.Get("rev.reward", filedThisRun.Count, value));

        IList<AtlasEntry> all = state.Atlas.Entries;

        // 2. Corrections: systems re-surveyed this run with a confirmed, identified, genuine entry.
        var resurveyed = new HashSet<string>();
        foreach (AtlasEntry e in filedThisRun)
            if (!e.isFalse && e.confidence == Confidence.Confirmed && !string.IsNullOrEmpty(e.bodyName))
                resurveyed.Add(e.systemId);
        foreach (AtlasEntry e in all)
        {
            if (!e.caught || e.corrected || e.runNumber >= run || !resurveyed.Contains(e.systemId)) continue;
            e.corrected = true;
            summary.entriesCorrected++;
            Apply(state, summary, run, CorrectionBonus, Loc.Get("rev.corrected", e.label, e.systemId));
        }

        // 3. Review
        foreach (AtlasEntry e in all)
        {
            if (e.catalogued || e.caught) continue;
            float chance = ChanceFor(state, e);
            e.reviewCount++;
            summary.entriesReviewed++;
            if (!e.isFalse) continue; // a true entry can't be caught: there is nothing wrong to find
            double roll = Transmitter.Roll(state.WorldSeed, run, e.id, -2, -2);
            if (roll >= chance) continue;

            e.caught = true;
            summary.entriesDisputed++;
            float penalty = (e.confidence == Confidence.Confirmed ? CaughtConfirmedPenalty : CaughtTentativePenalty)
                          + ClawbackTrustPerValue * e.value;
            string reason = Loc.Get(e.confidence == Confidence.Confirmed ? "rev.caught.c" : "rev.caught.t", e.label, e.systemId, e.value);
            e.value = 0f; // clawed back
            Apply(state, summary, run, -penalty, reason);
        }

        // 4. Probe lost
        if (cause == RunEndCause.PowerDepleted)
            Apply(state, summary, run, -ProbeLostPenalty, Loc.Get("rev.lost"));

        summary.trustAfter = state.Trust;
        state.Atlas.NotifyChanged();
    }

    /// <summary>Chance that this review catches the entry IF it is false.</summary>
    public static float ChanceFor(GameState state, AtlasEntry e)
    {
        float severity = e.confidence == Confidence.Confirmed ? 0.7f : 0.3f;
        if (e.kind == DataKind.Raw) severity += 0.1f;
        float chance = state.GetReviewChance(Mathf.Clamp01(severity));
        if (e.systemId == SolSystem.Id) chance += 0.15f;        // near home: well charted, heavily scrutinised
        chance += 0.05f * Mathf.Min(e.reviewCount, 6);          // time: every review cycle digs a little deeper
        return Mathf.Clamp(chance, 0f, 0.95f);
    }

    private static void Apply(GameState state, RunSummary summary, int run, float delta, string reason)
    {
        float before = state.Trust;
        state.ApplyTrustDelta(delta);
        var c = new TrustChange { run = run, delta = state.Trust - before, reason = reason };
        summary.trustChanges.Add(c);
        state.TrustHistory.Add(c);
    }
}
