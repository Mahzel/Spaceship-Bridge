using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawner and runtime driver for the current star system.
///
/// The system itself is pure data (SystemData, made by SystemFactory, deterministic from
/// world seed + system ID). This class only:
///   - instantiates GameObjects from that data (pooled bodies),
///   - moves them every frame from the GameClock, parents first,
///   - places the probe.
/// </summary>
[DefaultExecutionOrder(-100)]
public class SystemManager : MonoBehaviour
{
    public static SystemManager Current { get; private set; }

    [Header("Prefabs")]
    public GameObject starPrefab;
    public GameObject planetPrefab;
    public GameObject playerShipPrefab;

    [Header("Configuration")]
    public int defaultBaseSeed = GameConstants.DEFAULT_BASE_SEED;

    public SystemData CurrentData { get; private set; }
    public GameObject PlayerShip  { get; private set; }
    public string CurrentSystemID => CurrentData?.id;

    private ObjectPool<Transform> _pool;
    private readonly List<Transform> _nodeTransforms = new();
    private Vector3[] _positions = new Vector3[0];

    private int BaseSeed => Game.State != null ? Game.State.WorldSeed : defaultBaseSeed;

    // =========================================================================
    #region Unity
    private void Awake()
    {
        Current = this;
        if (Game.Run != null) Game.Run.LaunchRequested += LaunchToHome;
        if (Game.Jump != null) Game.Jump.Arrived += JumpToSystem;
        _pool   = new ObjectPool<Transform>(planetPrefab.transform, GameConstants.POOL_SIZE, transform);

        // Created in Awake so any screen looking up the PlayerShip tag in Start() finds it.
        // It stays a direct child of this object and is never parented to a pooled body.
        if (playerShipPrefab != null)
        {
            PlayerShip = Instantiate(playerShipPrefab, transform);
            PlayerShip.AddComponent<ShipDeactivationTrace>(); // DEBUG temporaire
        }
    }

    private void Start()
    {
        // The first run no longer starts here: the main menu's NEW GAME does it (Game.NewGame), and its launch
        // request jumps to the home system. Without the Game services (a bare test scene), spawn something.
        if (Game.Run == null) JumpToNewSystem();
    }

    private void OnDestroy()
    {
        if (Game.Run != null) Game.Run.LaunchRequested -= LaunchToHome;
        if (Game.Jump != null) Game.Jump.Arrived -= JumpToSystem;
        if (Current == this) Current = null;
    }

    private void Update()
    {
        if (CurrentData == null || Game.Clock == null) return;

        ApplyPositions(Game.Clock.SimSeconds);
        MoveShip();
    }
    #endregion

    // =========================================================================
    #region Public API
    /// <summary>Places the probe in the home system, which is fixed by the world seed.</summary>
    public void LaunchToHome() => JumpToSystem(SystemFactory.HomeSystemID(BaseSeed));

    /// <summary>
    /// Total stellar flux at the probe in solar constants (1.0 = 1 AU from a 1 solar-luminosity star),
    /// summed over every star, falling as 1/d^2. Drives solar power.
    /// </summary>
    public float StellarFluxAtShip()
    {
        if (CurrentData == null || PlayerShip == null) return 0f;

        Vector3 shipPos = PlayerShip.transform.position - transform.position;
        float flux = 0f;
        for (int i = 0; i < CurrentData.nodes.Count && i < _positions.Length; i++)
        {
            NodeData n = CurrentData.nodes[i];
            if (n.kind != NodeKind.Star) continue;

            float au = (shipPos - _positions[i]).magnitude / GameConstants.GAME_UNITS_PER_UA;
            flux += n.starLuminosity / Mathf.Max(au * au, 1e-4f);
        }
        return flux;
    }

    public void JumpToNewSystem()
    {
        string id = SystemFactory.GenerateSystemID(Random.Range(0, 640000));
        Debug.Log($"ID du système généré : {id}");
        JumpToSystem(id);
    }

    public void JumpToSystem(string targetID)
    {
        if (!SystemFactory.IsValidSystemID(targetID)) { Debug.LogError("ID de système invalide !"); return; }

        ClearCurrentSystem();
        if (Game.State != null) Game.State.Tracks.Clear(); // bearings from the old system mean nothing here
        CurrentData = SystemFactory.Generate(BaseSeed, targetID);
        Spawn(CurrentData);
        ApplyPositions(Game.Clock != null ? Game.Clock.SimSeconds : 0.0);
        PlacePlayerShip(CurrentData);

        Debug.Log($"[SystemManager] {targetID} : {CurrentData.StarCount} étoile(s), "
                + $"{GetComponentsInChildren<CelestialBody>().Length} corps actifs, "
                + $"vaisseau actif={PlayerShip != null && PlayerShip.activeInHierarchy}");
    }

    public void SetBaseSeed(int newSeed)
    {
        if (Game.State != null) Game.State.WorldSeed = newSeed;
        PlayerPrefs.SetInt("BaseSeed", newSeed);
        PlayerPrefs.Save();
    }
    #endregion

    // =========================================================================
    #region Spawn
    private void Spawn(SystemData data)
    {
        var stars = new List<(int node, CelestialBody body)>();

        for (int i = 0; i < data.nodes.Count; i++)
        {
            NodeData  n      = data.nodes[i];
            Transform parent = n.parent >= 0 ? _nodeTransforms[n.parent] : transform;

            GameObject go = n.kind == NodeKind.Barycenter ? CreateBarycenter(n, parent) : SpawnBody(n, parent);
            _nodeTransforms.Add(go.transform);

            if (n.kind == NodeKind.Star) stars.Add((i, go.GetComponent<CelestialBody>()));
        }

        // Each barycenter lists the stars that orbit it (directly or through a nested barycenter).
        for (int i = 0; i < data.nodes.Count; i++)
        {
            if (data.nodes[i].kind != NodeKind.Barycenter) continue;

            Barycenter bc = _nodeTransforms[i].GetComponent<Barycenter>();
            bc.bodies = new List<CelestialBody>();
            foreach (var (starNode, body) in stars)
                for (int a = data.nodes[starNode].parent; a >= 0; a = data.nodes[a].parent)
                    if (a == i) { bc.bodies.Add(body); break; }
            bc.RecalculateMass();
        }
    }

    private GameObject CreateBarycenter(NodeData n, Transform parent)
    {
        var go = new GameObject(n.name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Barycenter>();
        if (n.hasOrbit) go.AddComponent<OrbitalComponent>().elements = n.orbit;
        return go;
    }

    private GameObject SpawnBody(NodeData n, Transform parent)
    {
        Transform t = _pool.Get();
        if (t == null) // pool at capacity: fall back to a plain instance
            t = Instantiate(planetPrefab.transform, transform);

        GameObject go = t.gameObject;
        go.GetComponent<CelestialBody>()?.Reset();
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * n.radiusGame;
        go.name = n.name;
        go.tag  = n.kind == NodeKind.Star ? "Star" : "Planet";

        SphereCollider col = go.GetComponent<SphereCollider>();
        if (col != null) col.radius = n.radiusGame;

        CelestialBody body = go.GetComponent<CelestialBody>();
        if (body == null) body = go.AddComponent<CelestialBody>();
        body.bodyName            = n.name;
        body.bodyType            = n.bodyType;
        body.temperature         = n.temperature;
        body.mass                = n.mass;
        body.density             = n.density;
        body.radius              = n.radiusGame;
        body.solRadius           = n.radiusSol;
        body.starLuminosity      = n.starLuminosity;
        body.albedo              = n.albedo;
        body.chemicalComposition = n.composition;
        body.spectrum            = n.spectrum;
        body.atmosphere          = n.atmosphere;
        body.surfaceTemperature  = n.surfaceTemperature;
        body.surfaceClass        = n.surfaceClass;
        body.metallicity         = n.metallicity;
        body.ageGyr              = n.ageGyr;

        OrbitalComponent orb = go.GetComponent<OrbitalComponent>();
        if (orb == null) orb = go.AddComponent<OrbitalComponent>();
        orb.elements = n.orbit;

        return go;
    }
    #endregion

    /// <summary>The spawned transform of node `index` of CurrentData (null if out of range). Dev tools only:
    /// gameplay code must go through tracks and SensorSight, never through the node list.</summary>
    public Transform NodeTransform(int index)
    {
        return index >= 0 && index < _nodeTransforms.Count ? _nodeTransforms[index] : null;
    }

    // =========================================================================
    #region Runtime
    /// <summary>Sets every node's world position for the given simulated time (parents first).</summary>
    private void ApplyPositions(double simSeconds)
    {
        int count = CurrentData.nodes.Count;
        if (_positions.Length < count) _positions = new Vector3[count];

        CurrentData.EvaluatePositions(simSeconds, _positions);

        Vector3 origin = transform.position;
        for (int i = 0; i < count && i < _nodeTransforms.Count; i++)
            _nodeTransforms[i].position = origin + _positions[i];
    }

    private void PlacePlayerShip(SystemData data)
    {
        if (PlayerShip == null) { Debug.LogError("Vaisseau joueur non assigné !"); return; }

        PlayerShip.SetActive(true);
        PlayerShip.transform.SetParent(transform, false);

        // The probe arrives facing the system origin, already in a stable circular parking orbit around
        // whatever it lands nearest to - under real gravity (see ShipOrbit) a ship placed at rest would
        // just fall straight into it, so "at rest" no longer makes sense once orbits are simulated for real.
        if (data.startNode >= 0 && Game.State != null) { PlaceInParkingOrbit(data); MoveShip(); return; }

        Vector3 o = data.ShipOffset;
        double heading = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
        if (Game.State != null)
        {
            Game.State.Ship.Place(o.x, o.y, o.z, heading);
            double simNow = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
            Vector3 vel = OrbitalMechanics.CircularVelocityKmS(data, o, simNow);
            Game.State.Ship.vx = vel.x; Game.State.Ship.vy = vel.y; Game.State.Ship.vz = vel.z;
            Game.State.ShipOrbit.Reset(); // don't propagate across the jump on the next MoveShip() tick
        }
        MoveShip();
    }

    /// <summary>
    /// Home-system start: a circular parking orbit of startOrbitRadiusGame around startNode (Earth), placed on
    /// the night side (away from the system origin) in the ecliptic plane, moving prograde, facing the Sun.
    /// The ship's state is built in double precision from the body's analytic state.
    /// </summary>
    private void PlaceInParkingOrbit(SystemData data)
    {
        double simNow = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        OrbitalMechanics.NodeState(data, data.startNode, simNow, out Vec3d bodyPos, out Vec3d bodyVel);

        Vec3d outward = new Vec3d(bodyPos.x, 0.0, bodyPos.z);
        outward = outward.Magnitude > 1e-9 ? outward.Normalized : new Vec3d(1, 0, 0);
        double r = data.startOrbitRadiusGame;
        Vec3d pos = bodyPos + outward * r;

        double mu = OrbitalMechanics.Mu(OrbitalMechanics.MassOf(data, data.startNode));
        double speed = System.Math.Sqrt(mu / r);
        // Same sense as CircularVelocityKmS: Cross(up, radial).
        Vec3d tangent = Vec3d.Cross(new Vec3d(0, 1, 0), outward).Normalized * speed;
        Vec3d vel = (bodyVel + tangent) * ShipState.KmPerUnit;

        double heading = System.Math.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg;
        ShipState ship = Game.State.Ship;
        ship.Place(pos.x, pos.y, pos.z, heading);
        ship.vx = vel.x; ship.vy = vel.y; ship.vz = vel.z;
        Game.State.ShipOrbit.Reset();
    }

    /// <summary>Advances the ship along its real orbit (see ShipOrbit) for this frame's simulated time,
    /// then copies its state to the transform.</summary>
    private void MoveShip()
    {
        if (PlayerShip == null || Game.State == null) return;

        ShipState s = Game.State.Ship;
        if (Game.Clock != null && CurrentData != null)
            Game.State.ShipOrbit.Advance(CurrentData, s, Game.Clock.SimSeconds);

        PlayerShip.transform.position = transform.position + new Vector3((float)s.x, (float)s.y, (float)s.z);
        PlayerShip.transform.rotation = Quaternion.Euler(0f, (float)s.headingDeg, 0f);
    }

    private void ClearCurrentSystem()
    {
        // Bodies first (back to the pool), then plain objects such as barycenters.
        var toDestroy = new List<GameObject>();
        foreach (Transform t in _nodeTransforms)
        {
            if (t == null) continue;

            CelestialBody body = t.GetComponent<CelestialBody>();
            if (body != null)
            {
                body.Reset();
                t.SetParent(transform, false);
                _pool.ReturnToPool(t);
            }
            else
            {
                toDestroy.Add(t.gameObject);
            }
        }
        foreach (GameObject go in toDestroy) Destroy(go);

        _nodeTransforms.Clear();
        CurrentData = null;
    }
    #endregion
}
