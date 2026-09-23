using System;
using System.Collections.Generic;
using UnityEngine;
using static Utils;

/// <summary>
/// The waterfall sensor's actual work: line generation, CFAR detection and tracking. Owned by GameState and
/// ticked every real frame from RunDriver, independent of whichever console mode the player has on screen —
/// a hidden screen does not go deaf. Only Enabled = false (the player switching the sensor off) or the run
/// not being in flight stops it. WaterfallScreen is just a renderer over this: it reads Texture / LatestLine
/// to draw, and drives IntegrationOn from its own toggle.
/// </summary>
public sealed class WaterfallProcessor
{
    public readonly WaterfallSpec spec;

    /// <summary>Player-facing on/off. Off stops processing (and its power draw) entirely.</summary>
    public bool Enabled = true;
    public bool IntegrationOn = true;

    private readonly List<float[]> _integrator = new List<float[]>();
    private readonly List<Detection> _detections = new List<Detection>();
    private float[] _score;
    private float _accumulator; // real seconds banked toward the next line

    public int Bins { get; private set; }

    // Row-time bookkeeping, mirroring WaterfallTexture's own ring-buffer index exactly (same start value, same
    // increment per PushLine call), so a simulated time can be mapped back to the on-screen row it landed on.
    // Lets TrackOverlay draw a track's bearing HISTORY on the exact rows it was detected on, instead of only
    // the current bearing — important once the ship has turned since, or the row would land in the wrong place.
    private int _rowCount;
    private double[] _rowTime;
    private int _headMirror;

    private WaterfallTexture _waterfall;
    public Texture Texture { get { return _waterfall != null ? _waterfall.Texture : null; } }
    public Rect UvRect { get { return _waterfall != null ? _waterfall.UvRect : new Rect(0f, 0f, 1f, 1f); } }
    public float[] LatestLine { get; private set; }

    /// <summary>Raised whenever a new line lands, so a visible screen knows to redraw. Never raised while hidden
    /// — nothing subscribes then, and that's fine: the buffer itself keeps accumulating regardless.</summary>
    public event Action LineAdded;

    public WaterfallProcessor(WaterfallSpec spec)
    {
        this.spec = spec;
        Rebuild();
    }

    /// <summary>Resets the buffer and integrator for a new run (or after a spec swap).</summary>
    public void Rebuild()
    {
        Bins = spec.UsefulBins;
        int lines = Mathf.Max(128, spec.maxIntegration * 16); // ample scrollback, independent of any screen's pixel size
        _waterfall?.Destroy();
        _waterfall = new WaterfallTexture(Bins, lines);
        _integrator.Clear();
        LatestLine = null;
        _accumulator = 0f;

        _rowCount = lines;
        _rowTime = new double[_rowCount];
        for (int i = 0; i < _rowCount; i++) _rowTime[i] = double.NegativeInfinity;
        _headMirror = 0;
    }

    /// <summary>Maps a simulated time to the waterfall image's vertical fraction (0 = bottom/oldest edge,
    /// 1 = top/newest edge) for the row that time landed on. False if it's older than the buffered window
    /// (already scrolled off) or newer than anything recorded yet.</summary>
    public bool TryGetRowFraction(double time, out float yFrac01)
    {
        yFrac01 = 0f;
        if (_rowTime == null || _rowCount == 0) return false;

        for (int age = 0; age < _rowCount; age++)
        {
            int idx = ((_headMirror - 1 - age) % _rowCount + _rowCount) % _rowCount;
            double t = _rowTime[idx];
            if (double.IsNegativeInfinity(t)) return false; // ran off the start of a not-yet-full buffer
            if (t <= time)
            {
                yFrac01 = 1f - age / (float)_rowCount;
                return true;
            }
        }
        return false; // older than the whole buffered window
    }

    private void RecordRowTime(double simTime)
    {
        _rowTime[_headMirror] = simTime;
        _headMirror = (_headMirror + 1) % _rowCount;
    }

    /// <summary>Call every real frame regardless of what's on screen (RunDriver).</summary>
    public void Tick(float realDeltaSeconds)
    {
        if (!Enabled || spec == null) return;
        if (Game.Run == null || Game.Run.Phase != RunPhase.Flight) return;

        float warp = Game.Clock != null ? Mathf.Max(1f, Game.Clock.WarpFactor) : 1f;
        float wait = Mathf.Max(spec.minUpdateInterval, spec.lineIntervalSeconds / warp);

        _accumulator += realDeltaSeconds;
        // A stall (e.g. a huge frame hitch) should not fire hundreds of lines at once.
        if (_accumulator > wait * 8f) _accumulator = wait * 8f;

        while (_accumulator >= wait)
        {
            _accumulator -= wait;
            GenerateLine();
        }
    }

    private void GenerateLine()
    {
        double simTime = Game.Clock != null ? Game.Clock.SimSeconds : Time.timeAsDouble;
        float  heading = Game.State != null ? (float)Game.State.Ship.headingDeg : 0f;

        float[] baseNoise = new float[Bins];
        for (int i = 0; i < baseNoise.Length; i++)
            baseNoise[i] = GaussianNoise(0f, spec.noiseSigma);

        CelestialBody[] bodies = UnityEngine.Object.FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        Transform ship = SystemManager.Current != null && SystemManager.Current.PlayerShip != null
                       ? SystemManager.Current.PlayerShip.transform : null;
        foreach (CelestialBody body in bodies)
        {
            float azimuth = ComputeAzimuth(body, ship);
            SpreadSignal(baseNoise, azimuth, body.apparentLuminosity);
        }

        float[] average = SlidingAverage(baseNoise, GameConstants.CFAR_WINDOW, GameConstants.CFAR_GUARD);
        float[] stdev   = SlidingStdDev(baseNoise, GameConstants.CFAR_WINDOW, GameConstants.CFAR_GUARD);

        float[] cfarNoise = new float[baseNoise.Length];
        for (int i = 0; i < baseNoise.Length; i++)
        {
            float sigma = (stdev[i] > 1e-9f) ? stdev[i] : 2f * Mathf.Abs(average[i]);
            if (sigma < 1e-12f) sigma = 1e-12f;
            cfarNoise[i] = (baseNoise[i] - average[i]) / sigma;
        }

        float[] lineData;
        if (IntegrationOn)
        {
            lineData = Integrate(cfarNoise);
        }
        else
        {
            _integrator.Clear();
            lineData = cfarNoise;
        }

        RunDetection(lineData, simTime, heading);

        lineData = ApplyCompression(lineData, 0f, 3f, 10f);
        LatestLine = lineData;

        RecordRowTime(simTime);
        _waterfall.PushLine(lineData);
        if (LineAdded != null) LineAdded();
    }

    private void RunDetection(float[] lineData, double simTime, float heading)
    {
        if (Game.State == null) return;

        int   nInt = IntegrationOn ? Mathf.Max(1, _integrator.Count) : 1;
        float gain = spec.SnrGain(nInt);
        if (_score == null || _score.Length != lineData.Length) _score = new float[lineData.Length];
        for (int i = 0; i < lineData.Length; i++) _score[i] = lineData[i] * gain;

        // Bins already encode world bearing directly now (see ComputeAzimuth), so no heading offset here —
        // heading is still passed through to Tracks.Update below, purely as sample metadata.
        Detector.Detect(_score, spec.detectionThresholdSigma, spec.BeamwidthDeg, simTime, 0f, _detections);

        float gateDeg = spec.gateBins * 360f / lineData.Length;
        ShipState ship = Game.State.Ship;
        Game.State.Tracks.Update(simTime, _detections, gateDeg, spec.maxTracks, spec.dropAfterMisses, ship.x, ship.z, heading);
    }

    // World bearing (0 = +Z, clockwise-positive — same convention as ShipState.headingDeg), NOT ship-relative:
    // the waterfall bins are laid out in this fixed frame, so a contact sits still in the image regardless of
    // the ship's heading, and only the heading tick (drawn by TrackOverlay) sweeps across as the ship turns.
    private static float ComputeAzimuth(CelestialBody body, Transform ship)
    {
        if (ship == null) return body.GetData().Az;

        Vector3 rel = body.transform.position - ship.position;
        return Vector3.SignedAngle(Vector3.forward, new Vector3(rel.x, 0f, rel.z), Vector3.up);
    }

    private void SpreadSignal(float[] buffer, float azimuth, float signal)
    {
        int   n          = buffer.Length;
        float binsPerDeg = n / 360f;
        float sigmaBins  = Mathf.Max(0.3f, spec.PsfSigmaDeg * binsPerDeg);
        int   half       = Mathf.CeilToInt(3f * sigmaBins);

        float centre = (azimuth + 180f) * binsPerDeg;
        int   c0     = Mathf.FloorToInt(centre);

        for (int o = -half; o <= half; o++)
        {
            int   idx = c0 + o;
            float d   = (idx + 0.5f) - centre;
            float w   = Mathf.Exp(-0.5f * d * d / (sigmaBins * sigmaBins));
            buffer[((idx % n) + n) % n] += signal * w;
        }
    }

    private static float GaussianNoise(float mean, float stddev)
    {
        float u1 = Mathf.Max(1e-6f, 1f - UnityEngine.Random.value);
        float u2 = 1f - UnityEngine.Random.value;
        float normal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stddev * normal;
    }

    private float[] Integrate(float[] line)
    {
        _integrator.Add(line);
        while (_integrator.Count > spec.maxIntegration)
            _integrator.RemoveAt(0);

        float[] outline = new float[line.Length];
        foreach (float[] l in _integrator)
            for (int i = 0; i < outline.Length; i++)
                outline[i] += l[i];

        float inv = 1f / _integrator.Count;
        for (int i = 0; i < outline.Length; i++)
            outline[i] *= inv;
        return outline;
    }

    public void Clear()
    {
        _waterfall?.Clear();
    }

    public void Destroy()
    {
        _waterfall?.Destroy();
    }
}
