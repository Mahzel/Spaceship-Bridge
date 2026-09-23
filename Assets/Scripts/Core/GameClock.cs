using System;

/// <summary>
/// Single source of truth for simulated time.
///
/// SimSeconds is the time orbits, power drain, light delay and wake conditions all read.
/// It is advanced from REAL (unscaled) time by GameClockDriver, multiplied by the warp factor,
/// so UI, audio and coroutines that use Time.deltaTime are no longer tied to the simulation speed.
///
/// Default pacing is real time: BaseRate is 1.0, so at warp "1s" one real second is one simulated
/// second. The player controls pace with a multiplier {1,10,30,60} crossed with a unit {s,m,h,d} of
/// simulated time per real second (WarpMultiplier / WarpUnitKind, set together via SetWarp(mult, unit)) -
/// giving fine control, especially useful for slowing right down for a delicate approach (e.g. a gas
/// giant refuel pass). GameConstants.TIME_MULTIPLIER (86400, "1 real second = 1 simulated day") is no
/// longer the default; it's kept only as a debug/fast-forward tool that overrides BaseRate directly
/// (see DevHud's debug rate toggle).
///
/// Because orbits are Keplerian, position is a pure function of SimSeconds. That lets you ask
/// "where will body X be at time T" (transit windows, radar aiming) without simulating forward.
/// </summary>
public sealed class GameClock
{
    public enum WarpUnit { Seconds, Minutes, Hours, Days }

    /// <summary>Simulated seconds that pass per real second before the warp multiplier is applied.
    /// Defaults to 1 (real time). GameConstants.TIME_MULTIPLIER (86400) can be set here as a debug
    /// fast-forward, but is no longer the default pacing.</summary>
    public double BaseRate { get; set; } = 1.0;

    /// <summary>Total simulated seconds since the world started.</summary>
    public double SimSeconds { get; private set; }

    /// <summary>Simulated seconds added during the last Tick.</summary>
    public double LastDelta { get; private set; }

    /// <summary>Combined rate: simulated seconds per real second, i.e. BaseRate * WarpMultiplier * unit-in-seconds.
    /// This is what Tick() actually applies.</summary>
    public float WarpFactor { get; private set; } = 1f;

    /// <summary>The player-facing multiplier axis (1/10/30/60).</summary>
    public float WarpMultiplier { get; private set; } = 1f;

    /// <summary>The player-facing unit axis (seconds/minutes/hours/days of simulated time per real second).</summary>
    public WarpUnit WarpUnitKind { get; private set; } = WarpUnit.Seconds;

    public bool  Paused     { get; private set; }

    public double SimYears => SimSeconds / GameConstants.SECONDS_PER_YEAR;

    public event Action<float> WarpChanged;
    public event Action<bool>  PausedChanged;

    public void Tick(float realDeltaSeconds)
    {
        if (Paused) { LastDelta = 0; return; }
        LastDelta   = realDeltaSeconds * BaseRate * WarpFactor;
        SimSeconds += LastDelta;
    }

    /// <summary>Sets both warp axes together (e.g. 30, WarpUnit.Minutes = "30 simulated minutes per real second").</summary>
    public void SetWarp(float multiplier, WarpUnit unit)
    {
        multiplier = Math.Max(0f, multiplier);
        float factor = multiplier * UnitSeconds(unit);
        WarpMultiplier = multiplier;
        WarpUnitKind   = unit;
        if (factor == WarpFactor) return;
        WarpFactor = factor;
        WarpChanged?.Invoke(WarpFactor);
    }

    /// <summary>Back-compat: sets a raw simulated-seconds-per-real-second factor directly, expressed on the
    /// seconds axis (used by old callers / saves that only know a flat factor).</summary>
    public void SetWarp(float factor) => SetWarp(Math.Max(0f, factor), WarpUnit.Seconds);

    public static float UnitSeconds(WarpUnit unit)
    {
        switch (unit)
        {
            case WarpUnit.Minutes: return 60f;
            case WarpUnit.Hours:   return 3600f;
            case WarpUnit.Days:    return 86400f;
            default:               return 1f;
        }
    }

    public void SetPaused(bool paused)
    {
        if (paused == Paused) return;
        Paused = paused;
        PausedChanged?.Invoke(Paused);
    }

    /// <summary>Restores a saved time (or resets to 0 for a new run).</summary>
    public void SetTime(double simSeconds)
    {
        SimSeconds = simSeconds;
        LastDelta  = 0;
    }
}
