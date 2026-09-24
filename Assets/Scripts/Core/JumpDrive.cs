using System;
using System.Collections.Generic;

public struct JumpPlan
{
    public GalaxySystem target;
    public double distanceLy;
    public float  hydrogenCost;
    public double days;          // simulated transit time
    public float  returnCost;    // hydrogen to get home from the target
    public bool   isHome;
}

/// <summary>
/// Interstellar jumps. A jump costs hydrogen in proportion to distance and takes simulated time, during which
/// the probe runs on its own power (no solar income in transit). It ends only when the probe arrives, so a probe
/// that runs out of power in transit dies. Jumping to the home system ends the run as a return.
/// Where the probe is (system, galactic position) lives here. Plain C#, no scene dependencies.
/// </summary>
public sealed class JumpDrive
{
    private const double SecondsPerDay = 86400.0;

    private readonly GameClock _clock;
    private readonly GameState _state;
    private readonly RunController _run;

    private readonly List<GalaxySystem> _nearby = new List<GalaxySystem>();
    private string _nearbyFor;

    public string CurrentSystemId { get; private set; }
    public double X { get; private set; }
    public double Z { get; private set; }

    /// <summary>Raised when the probe has arrived in a (non-home) system and it must be spawned.</summary>
    public event Action<string> Arrived;

    public JumpDrive(GameClock clock, GameState state, RunController run)
    {
        _clock = clock;
        _state = state;
        _run = run;
        _run.LaunchRequested += ResetToHome;
    }

    public bool AtHome { get { return CurrentSystemId == Galaxy.Home(_state.WorldSeed).id; } }

    public double DistanceFromHomeLy { get { return Math.Sqrt(X * X + Z * Z); } }

    /// <summary>Save/load: where the probe is in the galaxy.</summary>
    public void Restore(string systemId, double x, double z)
    {
        CurrentSystemId = systemId;
        X = x; Z = z;
        _nearbyFor = null;
    }

    public void ResetToHome()
    {
        GalaxySystem home = Galaxy.Home(_state.WorldSeed);
        CurrentSystemId = home.id;
        X = home.x;
        Z = home.z;
        _nearbyFor = null;
    }

    /// <summary>Systems in scan range of the current position, nearest first (cached until the probe moves).</summary>
    public IList<GalaxySystem> Nearby()
    {
        if (_nearbyFor != CurrentSystemId)
        {
            Galaxy.Nearby(_state.WorldSeed, X, Z, _state.Probe.jumpScanLy, CurrentSystemId, _nearby);
            _nearbyFor = CurrentSystemId;
        }
        return _nearby;
    }

    public JumpPlan Plan(GalaxySystem target)
    {
        var p = new JumpPlan();
        p.target = target;
        p.distanceLy = target.DistanceTo(X, Z);
        p.hydrogenCost = (float)(p.distanceLy * _state.Probe.hydrogenPerLy);
        p.days = p.distanceLy * _state.Probe.jumpDaysPerLy;
        p.returnCost = (float)(target.DistanceTo(0.0, 0.0) * _state.Probe.hydrogenPerLy);
        p.isHome = target.id == Galaxy.Home(_state.WorldSeed).id;
        return p;
    }

    public bool CanAfford(JumpPlan plan)
    {
        return plan.hydrogenCost <= _state.Hydrogen + 1e-4f;
    }

    public bool Execute(GalaxySystem target)
    {
        if (_run.Phase != RunPhase.Flight) return false;

        JumpPlan plan = Plan(target);
        if (!CanAfford(plan)) return false;

        _state.ConsumeHydrogen(plan.hydrogenCost);

        // Transit: time passes, the probe lives on its own batteries.
        float energy = (float)(_state.TotalLoadPerDay * plan.days);
        if (_state.ReactorLevel > 0f)
        {
            float perDay = _state.ReactorHydrogenPerDay;
            double reactorDays = perDay > 0f ? Math.Min(plan.days, _state.Hydrogen / perDay) : plan.days;
            _state.ConsumeHydrogen((float)(perDay * reactorDays));
            energy -= (float)(_state.ReactorOutputPerDay * reactorDays);
            if (_state.Hydrogen <= 0f) _state.SetReactorLevel(0f);
        }
        _clock.SetTime(_clock.SimSeconds + plan.days * SecondsPerDay);
        _state.ChangePower(-energy);

        CurrentSystemId = target.id;
        X = target.x;
        Z = target.z;
        _nearbyFor = null;

        if (_state.IsProbeDead) { _run.EndRun(RunEndCause.PowerDepleted); return true; }
        if (plan.isHome)        { _run.EndRun(RunEndCause.ReturnedHome);  return true; }

        if (Arrived != null) Arrived(target.id);
        return true;
    }
}
