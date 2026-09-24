using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place sensors ask "what is physically out there". This is the physics side of the fence: the
/// waterfall, radar, imager and spectrometer use it to work out what their hardware would actually receive
/// from a direction. Player-facing screens (System view, track lists) must NOT call it. They read
/// Game.State.Tracks, which holds only what the sensors have measured.
///
/// The body list is cached once per frame, so four sensors asking in the same frame cost one
/// FindObjectsByType instead of four.
/// </summary>
public static class SensorSight
{
    private static readonly List<CelestialBody> _cache = new List<CelestialBody>();
    private static int _cacheFrame = -1;

    /// <summary>Every live, named body in the scene (barycenters and pooled leftovers filtered out).</summary>
    public static List<CelestialBody> AllBodies()
    {
        if (_cacheFrame == Time.frameCount) return _cache;
        _cacheFrame = Time.frameCount;
        _cache.Clear();
        CelestialBody[] found = Object.FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        foreach (CelestialBody b in found)
            if (b != null && b.gameObject.activeInHierarchy && !string.IsNullOrEmpty(b.bodyName)) _cache.Add(b);
        return _cache;
    }

    /// <summary>Drops the cache immediately, e.g. right after a system swap in the same frame.</summary>
    public static void Invalidate() { _cacheFrame = -1; }

    public static Transform Ship()
    {
        return SystemManager.Current != null && SystemManager.Current.PlayerShip != null
             ? SystemManager.Current.PlayerShip.transform : null;
    }

    /// <summary>World bearing of a body from the ship: 0 = +Z, clockwise-positive, (-180, 180]. Same frame as
    /// the waterfall bins, track bearings and ShipState.headingDeg.</summary>
    public static float WorldAzimuth(CelestialBody body, Transform ship)
    {
        if (ship == null) return body.GetData().Az;
        Vector3 rel = body.transform.position - ship.position;
        return Vector3.SignedAngle(Vector3.forward, new Vector3(rel.x, 0f, rel.z), Vector3.up);
    }

    /// <summary>Elevation of a body above the ship's horizontal (XZ) plane, degrees, +up. Geometric sign: NOT the
    /// negated CelestialBody.elevation field. Every aimed sensor (imager, radar, waterfall fan, spectrometer
    /// slit) and every track elevation uses this convention.</summary>
    public static float WorldElevation(CelestialBody body, Transform ship)
    {
        if (ship == null) return -body.elevation;
        Vector3 rel = body.transform.position - ship.position;
        float horiz = new Vector2(rel.x, rel.z).magnitude;
        return Mathf.Atan2(rel.y, horiz) * Mathf.Rad2Deg;
    }

    private static SystemData _nodeMapFor;
    private static readonly Dictionary<string, int> _nodeByName = new Dictionary<string, int>();

    /// <summary>
    /// Line-of-sight (radial) velocity of a body relative to the ship, km/s, positive = receding. The body's
    /// velocity is its analytic orbital state (sum up the parent chain), the ship's its own; projected on the
    /// ship-to-body direction. This is what Doppler-shifts the body's spectrum.
    /// </summary>
    public static double RadialVelocityKmS(CelestialBody body, Transform ship)
    {
        SystemManager sm = SystemManager.Current;
        if (body == null || ship == null || sm == null || sm.CurrentData == null || Game.State == null || Game.Clock == null)
            return 0.0;
        SystemData data = sm.CurrentData;
        if (_nodeMapFor != data)
        {
            _nodeMapFor = data;
            _nodeByName.Clear();
            for (int i = 0; i < data.nodes.Count; i++) _nodeByName[data.nodes[i].name] = i;
        }
        if (!_nodeByName.TryGetValue(body.bodyName, out int idx)) return 0.0;

        OrbitalMechanics.NodeState(data, idx, Game.Clock.SimSeconds, out Vec3d _, out Vec3d vGame);
        Vec3d vBody = vGame * ShipState.KmPerUnit;
        ShipState s = Game.State.Ship;
        Vec3d rel = new Vec3d(body.transform.position - ship.position);
        double d = rel.Magnitude;
        if (d < 1e-9) return 0.0;
        Vec3d los = rel / d;
        return Vec3d.Dot(vBody - new Vec3d(s.vx, s.vy, s.vz), los);
    }

    /// <summary>Great-circle angle between two (bearing, elevation) directions, degrees.</summary>
    public static float AngularDistance(float az1, float el1, float az2, float el2)
    {
        float e1 = el1 * Mathf.Deg2Rad, e2 = el2 * Mathf.Deg2Rad;
        float dAz = BearingMath.Diff(az1, az2) * Mathf.Deg2Rad;
        float c = Mathf.Sin(e1) * Mathf.Sin(e2) + Mathf.Cos(e1) * Mathf.Cos(e2) * Mathf.Cos(dAz);
        return Mathf.Acos(Mathf.Clamp(c, -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>Bodies within halfWidthDeg (great-circle) of the (bearing, elevation) direction, brightest first.
    /// This is a pointed instrument's field: the spectrometer slit, for instance.</summary>
    public static void CollectInCone(float worldBearingDeg, float elevationDeg, float halfWidthDeg, List<CelestialBody> result)
    {
        result.Clear();
        Transform ship = Ship();
        List<CelestialBody> all = AllBodies();
        for (int i = 0; i < all.Count; i++)
        {
            CelestialBody b = all[i];
            float d = AngularDistance(WorldAzimuth(b, ship), WorldElevation(b, ship), worldBearingDeg, elevationDeg);
            // The field touches an extended body's disk, not just its center (b.angularSize is its angular radius).
            if (d <= halfWidthDeg + Mathf.Max(0f, b.angularSize)) result.Add(b);
        }
        result.Sort((a, b) => b.apparentLuminosity.CompareTo(a.apparentLuminosity));
    }

    /// <summary>Power gain of a Gaussian beam at dDeg off its axis, where halfWidthDeg is the half-power
    /// (-3 dB) half-width. 1 on axis, 0.5 at the half-width, falling fast beyond.</summary>
    public static float BeamGain(float dDeg, float halfWidthDeg)
    {
        float sigma = Mathf.Max(1e-4f, halfWidthDeg) / 1.1774f; // exp(-0.5 (h/sigma)^2) = 0.5
        return Mathf.Exp(-0.5f * dDeg * dDeg / (sigma * sigma));
    }

    /// <summary>World yaw of the ship transform's nose. CelestialBody.azimuth is measured from this, so
    /// shipRelativeAz = Diff(worldBearing, ShipWorldYaw()).</summary>
    public static float ShipWorldYaw(Transform ship)
    {
        if (ship == null) return 0f;
        Vector3 f = ship.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-12f) return 0f;
        return Vector3.SignedAngle(Vector3.forward, f, Vector3.up);
    }

    public static double RangeAu(CelestialBody body, Transform ship)
    {
        if (ship == null) return body.distance / GameConstants.GAME_UNITS_PER_UA;
        return Vector3.Distance(ship.position, body.transform.position) / GameConstants.GAME_UNITS_PER_UA;
    }

    /// <summary>
    /// Bodies whose world bearing lies within halfWidthDeg of worldBearingDeg at ANY elevation, brightest first
    /// (by apparentLuminosity). A bearing-only query: use the (bearing, elevation) overload for pointed optics.
    /// </summary>
    public static void CollectInCone(float worldBearingDeg, float halfWidthDeg, List<CelestialBody> result)
    {
        result.Clear();
        Transform ship = Ship();
        List<CelestialBody> all = AllBodies();
        for (int i = 0; i < all.Count; i++)
        {
            CelestialBody b = all[i];
            float az = WorldAzimuth(b, ship);
            if (Mathf.Abs(BearingMath.Diff(az, worldBearingDeg)) <= halfWidthDeg) result.Add(b);
        }
        result.Sort((a, b) => b.apparentLuminosity.CompareTo(a.apparentLuminosity));
    }
}
