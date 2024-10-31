using System.Diagnostics;
using JetBrains.Annotations;
using MathNet.Numerics.Statistics;
using Maxisoft.Utils.Collections.Queues.Specialized;
using TDigestNet;
using WowAHaha.GameDataApi.Models;

namespace WowAHaha.GameDataApi.Statistics;

public sealed class RunningAuctionStats
{
    public enum OutlierResult
    {
        Unknown, // meaning that we don't know as there's not enough data
        Outlier,
        NotOutlier
    }

    private readonly CircularDeque<ApiPartialAuction> _queue = new(16);
    public readonly ApiItemIdentifier ItemId;
    private long _skippedCounter;

    private RunningWeightedStatistics? _stats;
    private TDigest? _tdigest;
    private long _totalQuantity;
    private long _updateCounter;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RunningAuctionStats(ApiItemIdentifier itemId)
    {
        ItemId = itemId;
    }

    public RunningWeightedStatistics RunningStats
    {
        get
        {
            Complete();
            return _stats ?? new RunningWeightedStatistics();
        }
    }

    // ReSharper disable once InconsistentNaming
    public TDigest TDigest
    {
        get
        {
            Complete();
            return _tdigest ?? new TDigest();
        }
    }

    public long Count => ProcessedCount + _skippedCounter;

    public long ProcessedCount => RunningStats.Count;

    public double TotalWeight => RunningStats.TotalWeight;

    public long TotalQuantity
    {
        get
        {
            Complete();
            return _totalQuantity;
        }
    }

    public long UpdateCounter
    {
        get
        {
            Complete();
            return _updateCounter;
        }
    }

    internal void Complete()
    {
        DrainQueueWithoutOutlier();
    }

    private int DrainQueue()
    {
        var res = 0;
        while (_queue.TryPopBack(out ApiPartialAuction item))
        {
            if (!TryPush(item, false))
            {
                throw new InvalidOperationException();
            }

            res++;
        }

        return res;
    }

    private bool DrainQueueIfFull()
    {
        if (!_queue.IsFull)
        {
            return false;
        }

        DrainQueueWithoutOutlier();
        return true;
    }

    private void DrainQueueWithoutOutlier()
    {
        if (_stats is null || _stats.Count < Math.Max(_queue.Count, 6))
        {
            var runningStats = new RunningWeightedStatistics();
            var runningStats2 = new RunningWeightedStatistics();
            foreach (ApiPartialAuction auction in _queue)
            {
                runningStats.Push(1, auction.UnitPrice);
                runningStats2.Push(auction.Quantity, auction.UnitPrice);
            }

            while (_queue.TryPopBack(out ApiPartialAuction item))
            {
                if (IsOutlierCore(runningStats, runningStats.Count, item))
                {
                    Interlocked.Increment(ref _skippedCounter);
                    continue; // drop
                }

                if (IsOutlierCore(runningStats2, runningStats2.Count, item, 6, 4))
                {
                    Interlocked.Increment(ref _skippedCounter);
                    continue; // drop
                }

                if (!TryPush(item, false))
                {
                    throw new InvalidOperationException("Can't push");
                }
            }
        }
        else
        {
            DrainQueue();
        }
    }

    private static bool IsOutlierCore(RunningWeightedStatistics stats, long updateCounter, ApiPartialAuction auction, double maxDeviation = 5, double minDeviation = 3,
        double negWeight = 5)
    {
        Debug.Assert(minDeviation < maxDeviation, "minDeviation < maxDeviation");
        var mean = stats.Mean;
        var std = stats.PopulationStandardDeviation;
        var mult = maxDeviation - Math.Log2(updateCounter) / minDeviation;
        mult = double.Clamp(mult, minDeviation, maxDeviation);
        var range = mult * std;
        return auction.UnitPrice > mean + range || auction.UnitPrice < mean - negWeight * range;
    }

    [MustUseReturnValue]
    public OutlierResult IsOutlier(ApiPartialAuction auction)
    {
        DrainQueueIfFull();

        if (_updateCounter >= _queue.Capacity && _stats is { Count: > 3 })
        {
            DrainQueue();
            var outlier = IsOutlierCore(_stats, _updateCounter, auction);

            return outlier ? OutlierResult.Outlier : OutlierResult.NotOutlier;
        }

        return OutlierResult.Unknown;
    }

    [MustUseReturnValue]
    public bool TryPush(ApiPartialAuction auction)
    {
        return TryPush(auction, true);
    }


    [MustUseReturnValue]
    private bool TryPush(ApiPartialAuction auction, bool checkOutlier)
    {
        var counter = Interlocked.Read(ref _updateCounter);
        if (unchecked((long)(ulong)auction.ItemId <= 0))
        {
            return false;
        }

        if (auction.Quantity <= 0)
        {
            return false;
        }

        if (auction.UnitPrice <= 0)
        {
            return false;
        }

        _stats ??= new RunningWeightedStatistics();

        if (checkOutlier)
        {
            switch (IsOutlier(auction))
            {
                case OutlierResult.Outlier:
                    Interlocked.Increment(ref _skippedCounter);
                    return false;
                case OutlierResult.NotOutlier:
                    break;
                case OutlierResult.Unknown:
                    DrainQueueIfFull();
                    _queue.PushBack(auction);
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        var weight = auction.TimeLeft switch
        {
            ApiAuctionTimeLeft.VeryShort => 0.8,
            ApiAuctionTimeLeft.Short => 0.9,
            ApiAuctionTimeLeft.Medium => 0.95,
            ApiAuctionTimeLeft.Long => 0.99,
            ApiAuctionTimeLeft.VeryLong => 1,
            _ => 1
        };

        weight *= auction.Quantity;

        _stats.Push(weight, auction.UnitPrice);

        _tdigest ??= new TDigest();
        _tdigest.Add(auction.UnitPrice, weight);
        Interlocked.Add(ref _totalQuantity, auction.Quantity);
        var newCounter = Interlocked.Increment(ref _updateCounter);
        if (Interlocked.Read(ref counter) + 1 != newCounter)
        {
            throw new InvalidOperationException("Concurrent access occurred");
        }

        return true;
    }
}