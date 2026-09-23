using System;

/// <summary>
/// Local, deterministic RNG for world generation. Unlike UnityEngine.Random.InitState it never
/// touches global state, so generating a system cannot disturb noise or any other randomness.
/// </summary>
public sealed class SeededRandom
{
    private readonly Random _r;

    public SeededRandom(int seed) => _r = new Random(seed);

    /// <summary>Float in [0, 1).</summary>
    public float Value => (float)_r.NextDouble();

    /// <summary>Float in [min, max).</summary>
    public float Range(float min, float max) => min + (max - min) * (float)_r.NextDouble();

    /// <summary>Int in [min, max) — max exclusive, like UnityEngine.Random.Range.</summary>
    public int Range(int min, int max) => _r.Next(min, max);
}
