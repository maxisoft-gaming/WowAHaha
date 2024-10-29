using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using WowAHaha.GameDataApi.Models;

namespace WowAHaha.GameDataApi.Statistics;

/// <summary>
///     This class stores the auction stats for all items in a dictionary
/// </summary>
public class RunningAuctionStatsBag
{
    private long _count;
    private long _skippedCount;

    public Dictionary<ApiItemIdentifier, RunningAuctionStats> AuctionsStats { get; init; } = new();

    public long Count
    {
        get => _count;
        private set => _count = value;
    }

    public long SkippedCount
    {
        get => _skippedCount;
        private set => _skippedCount = value;
    }

    public void IncrementCount()
    {
        Interlocked.Increment(ref _count);
    }

    public void IncrementSkippedCount()
    {
        Interlocked.Increment(ref _skippedCount);
    }

    public Guid ComputeHash(DateTimeOffset dateTimeOffset)
    {
        return RunningAuctionStatsBagHasher.GetHash(this, dateTimeOffset);
    }
}

internal static class RunningAuctionStatsBagHasher
{
    internal static Guid GetHash(RunningAuctionStatsBag value, DateTimeOffset dateTimeOffset)
    {
        // Note that the hash algo has to use xor every element so that it is commutative
        if (dateTimeOffset == default)
        {
            dateTimeOffset = DateTimeOffset.UtcNow;
        }
        var date = unchecked((ushort)(~(ushort)((dateTimeOffset.ToUnixTimeSeconds() >> 16) & 0xffff) + 1));
        Span<Guid> hashGuid = stackalloc Guid[1];
        Span<ulong> hashLongs = MemoryMarshal.Cast<Guid, ulong>(hashGuid);
        Debug.Assert(hashLongs.Length == 2);
        ulong h1 = date;
        var h2 = unchecked((ulong)(value.Count ^ value.SkippedCount));
        Vector128<ulong> v = Vector128.Create(h1, h2);

        // the following code can be typically optimized even more with avx instructions (even more with non-backward compatible changes)
        unchecked
        {
            foreach ((ApiItemIdentifier key, RunningAuctionStats stats) in value.AuctionsStats)
            {
                stats.Complete();
                v ^= GetHash(Vector128.Create(
                    (uint)key.ItemId,
                    (uint)stats.Count,
                    (uint)stats.TotalQuantity,
                    0)
                ).AsUInt64();

                v ^= Vector128.Create(0U, (uint)(stats.TotalQuantity >> 32), (uint)(key.ItemId >> 32), (uint)(stats.Count >> 32)).AsUInt64();

                if (stats.Count <= 0)
                {
                    continue;
                }

                v ^= GetHashPrice(stats.TotalWeight);
                v ^= GetHashPrice(stats.RunningStats.Mean);
                v ^= GetHashPrice(stats.RunningStats.Variance);
                v ^= GetHashPrice(stats.RunningStats.Maximum);
                v ^= GetHashPrice(stats.RunningStats.Minimum);
                v ^= GetHashPrice(stats.RunningStats.Kurtosis);
                v ^= GetHashPrice(stats.RunningStats.Skewness);
            }

            hashLongs[0] ^= v[0];
            hashLongs[1] ^= v[1];
        }

        return hashGuid[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static Vector128<uint> GetHash(Vector128<uint> key)
    {
        unchecked
        {
            key = ~key + (key << 15);
            key = key ^ (key >> 12);
            key = ~key + (key << 2);
            key = key ^ (key >> 4);
            key = ~key + (key << 1);
            return key;
        }
    }

    /// <summary>
    ///     This method use the logarithm of the price to compute a hash. This is intended to be imprecise
    ///     and is used to reduce the impact of the price and any related metrics micro changes on the hash of the auction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Vector128<ulong> GetHashPrice(double key)
    {
        if (key <= 0) // as a price should not be negative
        {
            return Vector128<ulong>.Zero;
        }

        if (!double.IsFinite(key))
        {
            return Vector128<ulong>.AllBitsSet;
        }

        var vi = (ulong)BitConverter.DoubleToInt64Bits(key);
        // extract exponent and mantissa part from a number IEEE754 / IEC 60559:1989 format
        // here we just need the bits to hash them (not a full IEEE754 exact representation)
        var exponent = (vi >> 52) & 0x7ffUL;
        var mantissa = vi & 0xfffffffffffffUL;

        var log2 = (ulong)BitOperations.Log2((ulong)key);

        return GetHash((Vector128.Create(exponent >> 3, mantissa >> 5) ^ Vector128.Create(log2, ~log2)).AsUInt32()).AsUInt64();
    }
}