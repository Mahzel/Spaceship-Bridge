using System.Collections.Generic;
using UnityEngine;

public enum NodeKind { Barycenter, Star, Planet }

/// <summary>
/// One body of a system (or a barycenter). Pure data: no GameObjects.
/// Nodes are stored parents-first, so positions can be evaluated in a single forward pass.
/// </summary>
public sealed class NodeData
{
    public int      index;
    public int      parent = -1;      // index in SystemData.nodes, -1 for the root
    public NodeKind kind;
    public string   name;

    public bool          hasOrbit;    // false for the root
    public OrbitElements orbit;       // relative to the parent node

    // Physical properties (stars and planets)
    public string bodyType;
    public float  temperature, mass, radiusGame, radiusSol, density, albedo;
    public float  starLuminosity;     // 0 for planets

    public List<ChemicalComposition> composition;
    public Spectrum spectrum;

    // Atmosphere / surface (planets and moons; default for stars and barycenters)
    public Atmosphere atmosphere;
    public float  surfaceTemperature; // greenhouse-adjusted actual temperature - `temperature` above stays
                                       // the equilibrium blackbody figure the two are meant to be compared against
    public string surfaceClass;       // e.g. "Temperate", "Hellscape", "Frozen", "Gas Giant" - see SystemFactory.DetermineSurfaceClass

    // Star diversity (stars only)
    public float metallicity;         // dex, [Fe/H]-style: 0 = solar, negative = metal-poor, positive = metal-rich
    public float ageGyr;              // billions of years
}

/// <summary>
/// A complete star system, deterministic from (world seed, system ID). Pre-scan and the atlas can
/// read a system without instantiating anything.
/// </summary>
public sealed class SystemData
{
    public readonly string id;
    public readonly int    seed;
    public readonly List<NodeData> nodes = new();

    // Where the probe arrives, relative to the system origin.
    public float shipDistance;
    public float shipAzimuthRad;

    /// <summary>If >= 0, the probe instead starts in a circular parking orbit of startOrbitRadiusGame around this
    /// node (the home system: Earth). See SystemManager.PlacePlayerShip.</summary>
    public int   startNode = -1;
    public float startOrbitRadiusGame;

    public SystemData(string id, int seed)
    {
        this.id   = id;
        this.seed = seed;
    }

    public Vector3 ShipOffset =>
        new Vector3(shipDistance * Mathf.Cos(shipAzimuthRad), 0f, shipDistance * Mathf.Sin(shipAzimuthRad));

    /// <summary>Positions of every node relative to the system origin at the given simulated time.</summary>
    public void EvaluatePositions(double simSeconds, Vector3[] result)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            NodeData n = nodes[i];
            Vector3 p = n.parent >= 0 ? result[n.parent] : Vector3.zero;
            if (n.hasOrbit) p += KeplerOrbit.OffsetAt(n.orbit, simSeconds);
            result[i] = p;
        }
    }

    /// <summary>Position of a single node (walks up its parent chain).</summary>
    public Vector3 PositionOf(int index, double simSeconds)
    {
        Vector3 p = Vector3.zero;
        for (int i = index; i >= 0; i = nodes[i].parent)
            if (nodes[i].hasOrbit) p += KeplerOrbit.OffsetAt(nodes[i].orbit, simSeconds);
        return p;
    }

    public int StarCount
    {
        get { int c = 0; foreach (var n in nodes) if (n.kind == NodeKind.Star) c++; return c; }
    }
}
