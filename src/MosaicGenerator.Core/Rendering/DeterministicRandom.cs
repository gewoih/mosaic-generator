namespace MosaicGenerator.Core.Rendering;

/// <summary>
/// SplitMix64 seeded from the cell's own coordinates. Each cell draws from its own stream, so the
/// result does not depend on iteration order and regenerating with the same parameters reproduces
/// the previous layout exactly.
/// </summary>
public struct DeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public DeterministicRandom(ulong seed, int row, int column)
    {
        ulong coordinates = ((ulong)(uint)row << 32) | (uint)column;
        _state = Mix(seed ^ Mix(coordinates + GoldenGamma));
    }

    public ulong NextUInt64()
    {
        _state += GoldenGamma;
        return Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform in [-1, 1).</summary>
    public double NextSigned() => (NextDouble() * 2.0) - 1.0;

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
