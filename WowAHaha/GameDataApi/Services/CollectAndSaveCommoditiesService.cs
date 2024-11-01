using Microsoft.Extensions.Logging;
using WowAHaha.GameDataApi.Http;
using WowAHaha.GameDataApi.Models.Serializers;
using WowAHaha.GameDataApi.Statistics;

namespace WowAHaha.GameDataApi.Services;

public class TimeOutOfSyncOrApiError(string message) : Exception(message);

internal class SkipContentProcessingException(string message) : Exception(message);

public class CollectAndSaveCommoditiesService(
    IBattleNetWebApi api,
    ProgramConfigurations programConfig,
    ILogger<CollectAndSaveCommoditiesService> logger,
    ICommodityAuctionSerializer serializer,
    ProgramWorkerSemaphore semaphore) : IRunnableService
{
    public async Task Run(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // ReSharper disable once VariableHidesOuterVariable
        await Parallel.ForEachAsync(Enum.GetValues<GameDataDynamicNameSpace>(), cts.Token, async (nameSpace, cancellationToken) =>
        {
#if DEBUG
                await Task.Delay(((int)nameSpace + 1) * 20, cancellationToken); // easy way to process dynamic namespace in order

#endif


            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessSpace(nameSpace);
            }
            finally
            {
                semaphore.Release();
            }

            return;

            // ReSharper disable once VariableHidesOuterVariable
            async ValueTask ProcessSpace(GameDataDynamicNameSpace space)
            {
                if (space is GameDataDynamicNameSpace.None)
                {
                    return;
                }

                if (programConfig.IgnoreChina && space is GameDataDynamicNameSpace.CN)
                {
                    return;
                }

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromMinutes(5));
                    CollectAndSaveCommoditiesExecutionSummary? previousSummary = await serializer.LoadPreviousSummary(space, cts.Token).ConfigureAwait(false);
                    RunningAuctionStatsBag res;

                    DateTimeOffset lastModified = DateTimeOffset.Now;
                    try
                    {
                        res = await api.GetCommoditiesAuctions(space, onDateAndLastModifiedHook: OnDateAndLastModifiedHook, cancellationToken: cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (SkipContentProcessingException e)
                    {
                        logger.LogDebug("Skipping content processing for {Space} because of: {Message}", space, e.Message);
                        return;
                    }

                    void OnDateAndLastModifiedHook((DateTimeOffset? Date, DateTimeOffset? LastModified) obj)
                    {
                        if (obj.Date is not null && (now - obj.Date.Value).Duration() < TimeSpan.FromMinutes(5))
                        {
                            now = obj.Date.Value;
                        }

                        if (obj.LastModified is null)
                        {
                            lastModified = now;
                            return;
                        }

                        lastModified = obj.LastModified.Value;
                        if ((lastModified - now).Duration() > TimeSpan.FromHours(12))
                        {
                            throw new TimeOutOfSyncOrApiError("Time out of sync or remote api error");
                        }

                        if (previousSummary is not null)
                        {
                            if ((lastModified - previousSummary.DataTimestamp).Duration() < TimeSpan.FromMinutes(1))
                            {
                                throw new SkipContentProcessingException("Same data as previous run");
                            }
                        }
                    }

                    if (res.Count <= 0)
                    {
                        logger.LogWarning("No commodities for {Space}", space);
                        return;
                    }

                    logger.LogInformation("Got {Count} commodities for {Space}", res.AuctionsStats.Count, space);
                    await serializer.WriteToFiles(res, space, lastModified, cts.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to get auctions for {Space}", space);
                }
            }
        });
    }

    public string Name => GetType().Name;
}