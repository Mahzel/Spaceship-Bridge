using System;
using System.Collections.Generic;

/// <summary>
/// Wake conditions: what interrupts time warp. While warping, the AI "sleeps"; when a condition trips, warp drops
/// to x1 and a message is shown. Too tight and false wakes burn power for nothing; too loose and events are missed.
/// Fed by track events and polled once per frame for the power condition. Plain C#, no scene dependencies.
/// </summary>
public sealed class WakeMonitor
{
    // Settings (kept across runs: they are the player's preferences)
    public bool  newContact  = true;
    public bool  lostContact = true;
    public bool  lowPower    = true;
    public float lowPowerPct = 25f;
    public bool  loudContact = false;
    public float loudSigma   = 20f;

    // Optional sector: new/loud contact conditions only count inside it (world bearing, degrees)
    public bool  useSector;
    public float sectorCenterDeg;
    public float sectorHalfDeg = 30f;

    public bool InSector(float bearingDeg)
    {
        return !useSector || Math.Abs(BearingMath.Diff(bearingDeg, sectorCenterDeg)) <= sectorHalfDeg;
    }

    private readonly GameClock _clock;
    private readonly GameState _state;
    private readonly RunController _run;

    private string _pending;
    private bool _lowPowerFired;
    private readonly HashSet<int> _loudFired = new HashSet<int>();
    private readonly List<Track> _scratch = new List<Track>();

    public string LastMessage { get; private set; }
    /// <summary>Increments on every wake, so a UI can tell a new message from an old one.</summary>
    public int MessageSerial { get; private set; }

    public WakeMonitor(GameClock clock, GameState state, RunController run)
    {
        _clock = clock;
        _state = state;
        _run = run;

        _state.Tracks.TrackConfirmed += OnConfirmed;
        _state.Tracks.TrackLost      += OnLost;
        _state.Tracks.Changed        += OnTracksChanged;
        _run.LaunchRequested         += Reset;
    }

    private void Reset()
    {
        _pending = null;
        _lowPowerFired = false;
        _loudFired.Clear();
    }

    private void OnConfirmed(Track t)
    {
        if (newContact && InSector(t.bearing) && _pending == null) _pending = Loc.Get("wake.new", t.name);
    }

    private void OnLost(Track t)
    {
        if (lostContact && _pending == null) _pending = Loc.Get("wake.lost", t.name);
    }

    private void OnTracksChanged()
    {
        if (!loudContact) return;
        _state.Tracks.CollectConfirmed(_scratch);
        for (int i = 0; i < _scratch.Count; i++)
        {
            Track t = _scratch[i];
            if (t.lastSnr >= loudSigma && InSector(t.bearing) && _loudFired.Add(t.id) && _pending == null)
                _pending = Loc.Get("wake.loud", t.name, t.lastSnr);
        }
    }

    /// <summary>Call once per frame, after the clock and the run have ticked.</summary>
    public void Poll()
    {
        if (_run.Phase != RunPhase.Flight) { _pending = null; return; }

        if (lowPower)
        {
            float pct = _state.PowerCapacity > 0f ? 100f * _state.PowerStored / _state.PowerCapacity : 0f;
            if (pct < lowPowerPct)
            {
                if (!_lowPowerFired && _pending == null) { _pending = Loc.Get("wake.power", pct); _lowPowerFired = true; }
            }
            else _lowPowerFired = false;
        }

        if (_pending == null) return;

        string message = _pending;
        _pending = null;
        if (_clock.WarpFactor > 1.0001f)
        {
            _clock.SetWarp(1f);
            LastMessage = message;
            MessageSerial++;
        }
    }
}
