using System;

/// <summary>Refit: choosing the next probe's loadout, before a launch (appended last: saves store the int).</summary>
public enum RunPhase { Idle, Flight, Debrief, Refit }
public enum RunEndCause { None, PowerDepleted, ReturnedHome }

/// <summary>What the debrief screen shows about a finished run.</summary>
[System.Serializable]
public sealed class RunSummary
{
    public int          runNumber;
    public double       durationDays;   // simulated days
    public RunEndCause  cause;
    public int          dataRecords;   // records on board at the end
    public float        dataValue;     // their total information value
    public float        storageUsed;
    public int          lastGaspRecords;   // records sent in the dying transmission
    public int          txCount, txPackets, txPacketsOk;
    public float        txValueSent, txValueReceived;
    public float        returnedValue;     // value carried home by a returning probe
    public float        totalValue;        // returned + received

    // Review at home (see Review.Process)
    public float trustBefore, trustAfter;
    public int   entriesFiled, entriesReviewed, entriesDisputed, entriesCorrected;
    public System.Collections.Generic.List<TrustChange> trustChanges = new System.Collections.Generic.List<TrustChange>();
}

/// <summary>
/// The expedition loop: launch, fly, die (or return), debrief, relaunch.
/// Plain C#: the clock, the state and the environment (stellar flux) are passed in, so it can be
/// tested without a scene. Power is life: it drains every simulated day and the run ends at zero.
///
/// Loads and solar output are per SIMULATED day (see ProbeSpec). This is the seam where hydrogen,
/// recording, transmission and trust will plug in later.
/// </summary>
public sealed class RunController
{
    private const double SecondsPerDay = 86400.0;

    private readonly GameClock _clock;
    private readonly GameState _state;

    public RunPhase Phase { get; private set; } = RunPhase.Idle;
    public int      RunNumber { get; private set; }
    public double   RunStartSimSeconds { get; private set; }
    public RunSummary LastSummary { get; private set; }

    /// <summary>Solar income and total load of the last tick, units per simulated day (for the HUD).</summary>
    public float IncomePerDay  { get; private set; }   // solar + reactor
    public float SolarPerDay   { get; private set; }
    public float ReactorPerDay { get; private set; }
    public float LoadPerDay   { get; private set; }

    public double RunElapsedDays => (_clock.SimSeconds - RunStartSimSeconds) / SecondsPerDay;

    /// <summary>Raised when a probe must be placed in the home system (start of every run).</summary>
    public event Action LaunchRequested;
    public event Action<RunSummary> RunEnded;
    /// <summary>Raised at the very end of BeginRun, once the new probe sits in the home system.</summary>
    public event Action Launched;
    public event Action Changed;

    public RunController(GameClock clock, GameState state)
    {
        _clock = clock;
        _state = state;
    }

    /// <summary>Starts a new run with a fresh probe (used for the first launch and every replacement).</summary>
    public void BeginRun()
    {
        RunNumber++;
        _state.ResetProbe();
        RunStartSimSeconds = _clock.SimSeconds;
        Phase = RunPhase.Flight;
        // Launch pacing comes from Settings > Time (default warp, start paused).
        _clock.SetWarpLadder(Settings.Data.defaultWarp);
        _clock.SetPaused(Settings.Data.startPaused);

        LaunchRequested?.Invoke();
        Changed?.Invoke();
        Launched?.Invoke(); // the probe is in place: a good moment to autosave
    }

    /// <summary>Opens the refit (probe loadout) before the next launch: after a debrief, or at New Game.</summary>
    public void BeginRefit()
    {
        Phase = RunPhase.Refit;
        _clock.SetPaused(true);
        Changed?.Invoke();
    }

    /// <summary>Refit > BACK: returns to the debrief it came from (if there is one).</summary>
    public void BackToDebrief()
    {
        if (Phase != RunPhase.Refit || LastSummary == null) return;
        Phase = RunPhase.Debrief;
        Changed?.Invoke();
    }

    /// <summary>Save/load: the run's phase and history. Does not touch the probe (see Game.ApplySave).</summary>
    public void Restore(RunPhase phase, int runNumber, double runStartSimSeconds, RunSummary lastSummary)
    {
        Phase = phase;
        RunNumber = runNumber;
        RunStartSimSeconds = runStartSimSeconds;
        LastSummary = lastSummary;
        Changed?.Invoke();
    }

    /// <summary>New Game: back to "no run yet" (the next BeginRun is run #1).</summary>
    public void ResetForNewGame()
    {
        RunNumber = 0;
        LastSummary = null;
        Phase = RunPhase.Idle;
        Changed?.Invoke();
    }

    /// <summary>Abandons the current run without a debrief (pause menu > Main Menu). Nothing is filed to the
    /// atlas and nothing is transmitted: the expedition is simply left, and the main menu shows.</summary>
    public void Abandon()
    {
        Phase = RunPhase.Idle;
        _clock.SetPaused(true);
        Changed?.Invoke();
    }

    /// <summary>Call once per frame, after the clock has ticked. stellarFlux is in solar constants at the probe.</summary>
    public void Tick(float stellarFlux)
    {
        if (Phase != RunPhase.Flight) return;

        SolarPerDay   = _state.Probe.solarOutput * stellarFlux;
        ReactorPerDay = _state.ReactorOutputPerDay;
        IncomePerDay  = SolarPerDay + ReactorPerDay;
        LoadPerDay    = _state.TotalLoadPerDay;

        double days = _clock.LastDelta / SecondsPerDay;
        if (days <= 0.0) return;

        // The reactor burns hydrogen; when the tank runs dry it shuts down.
        if (_state.ReactorLevel > 0f)
        {
            _state.ConsumeHydrogen(_state.ReactorHydrogenPerDay * (float)days);
            if (_state.Hydrogen <= 0f) _state.SetReactorLevel(0f);
        }

        _state.ChangePower((float)((IncomePerDay - LoadPerDay) * days));
        _state.Data.Tick(days, _clock.SimSeconds, _state.Tracks);
        if (_state.IsProbeDead) EndRun(RunEndCause.PowerDepleted);
    }

    private readonly System.Collections.Generic.List<AtlasEntry> _filed = new System.Collections.Generic.List<AtlasEntry>();

    private void Filed(AtlasEntry e)
    {
        if (e != null && !e.catalogued && !_filed.Contains(e)) _filed.Add(e);
    }

    public void EndRun(RunEndCause cause)
    {
        if (Phase != RunPhase.Flight) return;
        _filed.Clear();

        Phase = RunPhase.Debrief;

        // A dying probe spends its reserve on one last transmission.
        int gasp = 0;
        if (cause == RunEndCause.PowerDepleted) gasp = _state.Link.LastGasp(_state.Probe.lastGaspEnergy);

        // The results of everything sent become known only now.
        bool home = cause == RunEndCause.ReturnedHome;
        var onBoard = new System.Collections.Generic.HashSet<int>();
        if (home) foreach (DataRecord r in _state.Data.Records) onBoard.Add(r.id);
        TransmitResult tx = _state.Link.Resolve(_state.WorldSeed, RunNumber, onBoard);
        float returned = home ? _state.Data.TotalValue : 0f;

        // Atlas entries: filed only from data that actually made it home this run, either carried back by a
        // returning probe or transmitted and received per tx.recordsReceived (Data still holds those records —
        // Data.Clear() only happens on the next ResetProbe, not here).
        if (home)
        {
            foreach (DataRecord r in _state.Data.Records)
                Filed(_state.Atlas.Log(r, _state.WorldSeed, RunNumber, _clock.SimSeconds, r.confidence));
        }
        else
        {
            for (int i = 0; i < tx.recordsReceived.Count; i++)
            {
                DataRecord r = _state.Data.Find(tx.recordsReceived[i]);
                if (r != null) Filed(_state.Atlas.Log(r, _state.WorldSeed, RunNumber, _clock.SimSeconds,
                                                      _state.Link.ConfidenceSent(r.id, r.confidence)));
            }
        }

        LastSummary = new RunSummary
        {
            runNumber    = RunNumber,
            durationDays = RunElapsedDays,
            cause        = cause,
            dataRecords  = _state.Data.Records.Count,
            dataValue    = _state.Data.TotalValue,
            storageUsed  = _state.Data.Used,
            lastGaspRecords = gasp,
            txCount = tx.transmissions, txPackets = tx.packetsSent, txPacketsOk = tx.packetsOk,
            txValueSent = tx.valueSent, txValueReceived = tx.valueReceived,
            returnedValue = returned,
            totalValue = returned + tx.valueReceived
        };

        // Home reviews the atlas: rewards what arrived, catches some false entries, credits corrections.
        Review.Process(_state, RunNumber, cause, _filed, LastSummary);

        _clock.SetPaused(true); // the world holds still during the debrief
        RunEnded?.Invoke(LastSummary);
        Changed?.Invoke();
    }
}
