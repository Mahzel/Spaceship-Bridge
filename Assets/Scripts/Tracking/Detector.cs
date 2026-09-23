using System;
using System.Collections.Generic;

/// <summary>One CFAR detection: where the sensor thinks something is, not where it truly is.</summary>
public struct Detection
{
    public double time;     // simulated seconds
    public float  bearing;  // world bearing, degrees [0, 360)
    public float  snr;      // detection score (sigmas, after integration gain)
    public float  sigmaDeg; // 1-sigma bearing accuracy, degrees
}

public static class BearingMath
{
    public static float Wrap360(float a)
    {
        a %= 360f;
        return a < 0f ? a + 360f : a;
    }

    /// <summary>Wraps to [-180, 180).</summary>
    public static float Wrap180(float a)
    {
        a = Wrap360(a);
        return a >= 180f ? a - 360f : a;
    }

    /// <summary>Signed shortest angle from b to a, in degrees.</summary>
    public static float Diff(float a, float b) { return Wrap180(a - b); }
}

/// <summary>
/// Turns a line of CFAR scores (unit-variance noise, azimuth -180..180 relative to the ship) into detections.
/// A detection is a local maximum above the threshold that is also the largest value within one beamwidth,
/// so the skirt of a bright source does not produce a string of false contacts. Position is sub-bin.
/// Pure C#, no Unity dependency.
/// </summary>
public static class Detector
{
    public static void Detect(float[] score, float thresholdSigma, float minSeparationDeg,
                              double time, float headingDeg, List<Detection> output)
    {
        output.Clear();
        int n = score.Length;
        if (n < 3) return;

        float degPerBin = 360f / n;
        int r = Math.Max(1, (int)Math.Ceiling(minSeparationDeg / degPerBin));

        for (int i = 0; i < n; i++)
        {
            float v = score[i];
            if (v < thresholdSigma) continue;

            bool isMax = true;
            for (int o = -r; o <= r && isMax; o++)
            {
                if (o == 0) continue;
                float w = score[((i + o) % n + n) % n];
                if (w > v || (w == v && o < 0)) isMax = false;
            }
            if (!isMax) continue;

            float y0 = score[(i - 1 + n) % n];
            float y2 = score[(i + 1) % n];
            float denom = y0 - 2f * v + y2;
            float off = 0f;
            if (denom < -1e-9f)
            {
                off = 0.5f * (y0 - y2) / denom;
                if (off > 0.5f) off = 0.5f;
                if (off < -0.5f) off = -0.5f;
            }

            float azRel = (i + 0.5f + off) * degPerBin - 180f;
            var d = new Detection();
            d.time = time;
            d.bearing = BearingMath.Wrap360(azRel + headingDeg);
            d.snr = v;
            // Centroid accuracy ~ half a beamwidth divided by the SNR (floored: quantisation, array errors).
            d.sigmaDeg = Math.Max(0.02f, 0.5f * minSeparationDeg / Math.Max(v, 1f));
            output.Add(d);
        }
    }
}
