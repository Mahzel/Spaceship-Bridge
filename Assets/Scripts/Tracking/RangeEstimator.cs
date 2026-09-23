using System;
using System.Collections.Generic;

/// <summary>Result of bearing-only target motion analysis. An estimate with an uncertainty, never the truth.</summary>
public struct RangeEstimate
{
    public bool   valid;
    public double range;        // ship -> target now, game units
    public double rangeSigma;   // 1-sigma, game units
    public double x, z;         // estimated target position now, game units
    public double vxKmS, vzKmS; // estimated target velocity, km/s
    public int    samples;
    public double spanDays;

    /// <summary>True once the uncertainty is under half the range: the geometry (a manoeuvre) finally constrains it.</summary>
    public bool Observable { get { return valid && rangeSigma < 0.5 * range; } }
}

/// <summary>
/// Bearing-only target motion analysis (TMA). Model: the target moves in a straight line at constant velocity,
/// T(t) = T0 + V * (t - tref). Fitted by Levenberg-Marquardt on the true bearing residuals
/// (a linear "pseudo-measurement" solution is only used as one of several starting points, because that
/// estimator is badly biased when the bearings are nearly parallel, which is the normal case at long range).
/// The covariance (J^T J)^-1 gives the range sigma.
///
/// With a constant-velocity ship the range is unobservable: sigma stays huge. A burn changes the geometry and
/// sigma collapses. That is the gameplay: range comes from manoeuvring, not from a sensor.
/// Pure C#, no Unity dependency.
/// </summary>
public static class RangeEstimator
{
    private const double SecondsPerDay = 86400.0;
    public const int MinSamples = 6;
    private const int MaxRows = 128;
    private const int MaxIterations = 30;

    private static readonly double[] StartRanges = { 200.0, 1000.0, 5000.0, 20000.0 };

    public static RangeEstimate Estimate(List<BearingSample> history)
    {
        var result = new RangeEstimate();
        int total = history.Count;
        result.samples = total;
        if (total < MinSamples) return result;

        BearingSample last = history[total - 1];
        double tref = last.time;
        double span = (tref - history[0].time) / SecondsPerDay;
        if (span < 1e-3) return result;
        result.spanDays = span;

        // Evenly subsample to at most MaxRows measurements.
        int n = Math.Min(total, MaxRows);
        double[] th = new double[n], tau = new double[n], px = new double[n], pz = new double[n], sig = new double[n];
        for (int i = 0; i < n; i++)
        {
            int idx = n == total ? i : (int)((long)i * (total - 1) / (n - 1));
            BearingSample s = history[idx];
            th[i] = s.bearing * Math.PI / 180.0;
            tau[i] = ((s.time - tref) / SecondsPerDay) / span;        // in [-1, 0]
            px[i] = s.shipX; pz[i] = s.shipZ;
            sig[i] = Math.Max(0.005, s.sigmaDeg) * Math.PI / 180.0;
        }

        double bestCost = double.MaxValue;
        double[] bestP = null;
        double[,] bestCov = null;

        double lastTh = th[n - 1];
        double sx = Math.Sin(lastTh), sz = Math.Cos(lastTh);

        double[] init = PseudoLinear(th, tau, px, pz, n);
        for (int k = -1; k < StartRanges.Length; k++)
        {
            double[] p = new double[4];
            if (k < 0)
            {
                if (init == null) continue;
                Array.Copy(init, p, 4);
            }
            else
            {
                p[0] = px[n - 1] + StartRanges[k] * sx;
                p[1] = pz[n - 1] + StartRanges[k] * sz;
            }

            double[,] cov;
            double cost;
            if (!Fit(th, tau, px, pz, sig, n, p, out cost, out cov)) continue;
            if (cost < bestCost) { bestCost = cost; bestP = p; bestCov = cov; }
        }
        if (bestP == null) return result;

        double dx = bestP[0] - last.shipX, dz = bestP[1] - last.shipZ;
        double range = Math.Sqrt(dx * dx + dz * dz);
        if (range < 1e-9) return result;
        double ux = dx / range, uz = dz / range;
        double var = ux * ux * bestCov[0, 0] + 2 * ux * uz * bestCov[0, 1] + uz * uz * bestCov[1, 1];

        // Plausibility: an estimate that puts the target thousands of AU away or moving at hundreds of km/s is a
        // failed fit (model mismatch, crossing tracks), not a measurement.
        double vKmS = Math.Sqrt(bestP[2] * bestP[2] + bestP[3] * bestP[3]) * ShipState.KmPerUnit / SecondsPerDay / span;
        if (range > 2e5 || vKmS > 300.0) return result;

        result.valid = true;
        result.range = range;
        result.rangeSigma = Math.Sqrt(Math.Max(var, 0.0));
        result.x = bestP[0];
        result.z = bestP[1];
        double toKmS = ShipState.KmPerUnit / SecondsPerDay / span; // (units per span) -> km/s
        result.vxKmS = bestP[2] * toKmS;
        result.vzKmS = bestP[3] * toKmS;
        return result;
    }

    // ---------------------------------------------------------------------
    // Linear pseudo-measurement solution: (T + V tau - P) x d = 0. Biased, but a fine starting point.
    private static double[] PseudoLinear(double[] th, double[] tau, double[] px, double[] pz, int n)
    {
        double[,] m = new double[4, 4];
        double[] rhs = new double[4];
        double[] row = new double[4];
        for (int i = 0; i < n; i++)
        {
            double c = Math.Cos(th[i]), s = Math.Sin(th[i]);
            row[0] = c; row[1] = -s; row[2] = c * tau[i]; row[3] = -s * tau[i];
            double b = c * px[i] - s * pz[i];
            for (int r = 0; r < 4; r++)
            {
                rhs[r] += row[r] * b;
                for (int q = 0; q < 4; q++) m[r, q] += row[r] * row[q];
            }
        }
        double[] u; double[,] inv;
        if (!SolveSym(m, rhs, out u, out inv)) return null;
        return u;
    }

    // Levenberg-Marquardt on the bearing residuals. p is updated in place; cov = (J^T J)^-1 at the solution.
    private static bool Fit(double[] th, double[] tau, double[] px, double[] pz, double[] sig, int n,
                            double[] p, out double cost, out double[,] cov)
    {
        cost = Cost(th, tau, px, pz, sig, n, p);
        cov = null;
        double lambda = 1e-3;
        double[,] jtj = new double[4, 4];
        double[] jtr = new double[4];

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            Normal(th, tau, px, pz, sig, n, p, jtj, jtr);

            bool improved = false;
            for (int tries = 0; tries < 12; tries++)
            {
                double[,] a = (double[,])jtj.Clone();
                for (int d = 0; d < 4; d++) a[d, d] += lambda * (jtj[d, d] + 1e-30);
                double[] step; double[,] inv;
                if (!SolveSym(a, jtr, out step, out inv)) { lambda *= 10.0; continue; }

                double[] q = new double[4];
                for (int d = 0; d < 4; d++) q[d] = p[d] + step[d];
                double c2 = Cost(th, tau, px, pz, sig, n, q);
                if (c2 < cost)
                {
                    double rel = (cost - c2) / Math.Max(cost, 1e-12);
                    Array.Copy(q, p, 4);
                    cost = c2;
                    lambda = Math.Max(lambda * 0.3, 1e-9);
                    improved = true;
                    if (rel < 1e-9) iter = MaxIterations;
                    break;
                }
                lambda *= 10.0;
            }
            if (!improved) break;
        }

        Normal(th, tau, px, pz, sig, n, p, jtj, jtr);
        double[] dummy; double[,] covariance;
        if (!SolveSym(jtj, jtr, out dummy, out covariance))
        {
            // Unobservable: report a huge uncertainty instead of failing.
            covariance = new double[4, 4];
            for (int d = 0; d < 4; d++) covariance[d, d] = 1e12;
        }
        cov = covariance;
        return true;
    }

    private static double Residual(double[] th, double[] tau, double[] px, double[] pz, double[] sig, int i, double[] p)
    {
        double dx = p[0] + p[2] * tau[i] - px[i];
        double dz = p[1] + p[3] * tau[i] - pz[i];
        double phi = Math.Atan2(dx, dz);
        double e = th[i] - phi;
        while (e > Math.PI) e -= 2 * Math.PI;
        while (e < -Math.PI) e += 2 * Math.PI;
        return e / sig[i];
    }

    private static double Cost(double[] th, double[] tau, double[] px, double[] pz, double[] sig, int n, double[] p)
    {
        double c = 0;
        for (int i = 0; i < n; i++) { double r = Residual(th, tau, px, pz, sig, i, p); c += r * r; }
        return c;
    }

    // J^T J and J^T r (with r = (measured - predicted)/sigma, so the step is p += (J^T J)^-1 J^T r).
    private static void Normal(double[] th, double[] tau, double[] px, double[] pz, double[] sig, int n,
                               double[] p, double[,] jtj, double[] jtr)
    {
        Array.Clear(jtj, 0, 16);
        Array.Clear(jtr, 0, 4);
        double[] g = new double[4];
        for (int i = 0; i < n; i++)
        {
            double dx = p[0] + p[2] * tau[i] - px[i];
            double dz = p[1] + p[3] * tau[i] - pz[i];
            double r2 = Math.Max(dx * dx + dz * dz, 1e-12);
            // d(phi)/dTx = dz/r2, d(phi)/dTz = -dx/r2 ; velocity terms scale with tau. Gradient of the prediction / sigma:
            g[0] = dz / r2 / sig[i];
            g[1] = -dx / r2 / sig[i];
            g[2] = tau[i] * g[0];
            g[3] = tau[i] * g[1];
            double r = Residual(th, tau, px, pz, sig, i, p);
            for (int a = 0; a < 4; a++)
            {
                jtr[a] += g[a] * r;
                for (int b = 0; b < 4; b++) jtj[a, b] += g[a] * g[b];
            }
        }
    }

    // Solves M x = rhs and returns M^-1. Fails on a (relatively) singular matrix.
    private static bool SolveSym(double[,] m0, double[] rhs, out double[] x, out double[,] inv)
    {
        double[,] m = new double[4, 8];
        double maxDiag = 0;
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++) m[r, c] = m0[r, c];
            m[r, 4 + r] = 1.0;
            maxDiag = Math.Max(maxDiag, Math.Abs(m0[r, r]));
        }
        if (maxDiag <= 0) { x = null; inv = null; return false; }

        for (int col = 0; col < 4; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 4; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-15 * maxDiag) { x = null; inv = null; return false; }
            if (piv != col)
                for (int c = 0; c < 8; c++) { double t = m[col, c]; m[col, c] = m[piv, c]; m[piv, c] = t; }
            double d = m[col, col];
            for (int c = 0; c < 8; c++) m[col, c] /= d;
            for (int r = 0; r < 4; r++)
            {
                if (r == col) continue;
                double f = m[r, col];
                if (f == 0.0) continue;
                for (int c = 0; c < 8; c++) m[r, c] -= f * m[col, c];
            }
        }

        inv = new double[4, 4];
        x = new double[4];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++) { inv[r, c] = m[r, 4 + c]; x[r] += inv[r, c] * rhs[c]; }
        return true;
    }
}
