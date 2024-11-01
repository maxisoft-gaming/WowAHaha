using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WowAHaha.GameDataApi.Http;
using WowAHaha.GameDataApi.Models;
using WowAHaha.GameDataApi.Models.Serializers;

namespace WowAHaha.GameDataApi.Services;

public class CollectAndSaveWowTokenPricesService(
    IBattleNetWebApi api,
    ProgramConfigurations programConfig,
    ILogger<CollectAndSaveCommoditiesService> logger,
    IWowTokenPriceSerializer serializer,
    ProgramWorkerSemaphore semaphore) : IRunnableService
{
    public async Task Run(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // ReSharper disable once VariableHidesOuterVariable
        var prices = new ConcurrentDictionary<GameDataDynamicNameSpace, WowTokenPriceWithNamespace>();
        await Parallel.ForEachAsync(Enum.GetValues<GameDataDynamicNameSpace>(), cts.Token, async (nameSpace, cancellationToken) =>
        {
#if DEBUG
            await Task.Delay(((int)nameSpace + 1) * 20, cancellationToken); // easy way to process dynamic namespace in order
#endif


            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessWowToken(nameSpace);
            }
            finally
            {
                semaphore.Release();
            }

            return;

            // ReSharper disable once VariableHidesOuterVariable
            async ValueTask ProcessWowToken(GameDataDynamicNameSpace space)
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
                    cts.CancelAfter(TimeSpan.FromMinutes(1));
                    Task<WowTokenPriceWithNamespace?> previousPriceTask = serializer.LoadPreviousPrice(space, cts.Token);


                    WowTokenPrice? res = await api.GetWowTokenPrice(space, cancellationToken: cts.Token)
                        .ConfigureAwait(false);

                    if (res is null)
                    {
                        logger.LogWarning("No token price for {Space}", space);
                        return;
                    }


                    WowTokenPriceWithNamespace? previous = await previousPriceTask.ConfigureAwait(false);
                    if (previous is not null && res.LastUpdatedTimestamp <= previous.LastUpdatedTimestamp)
                    {
                        logger.LogInformation("Skipping update for {Space}", space);
                        return;
                    }

                    if ((DateTimeOffset.FromUnixTimeMilliseconds(res.LastUpdatedTimestamp) - now).Duration() > TimeSpan.FromHours(24))
                    {
                        logger.LogWarning("Skipping update for {Space} as it is more than 24 hours old", space);
                        return;
                    }

                    logger.LogInformation("Got token price of {TokenPrice} for {Space}", res.Price / 1e4, space);
                    prices.TryAdd(space, new WowTokenPriceWithNamespace(res.LastUpdatedTimestamp, nameSpace, res.Price));
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to get wow token price for {Space}", space);
                }
            }
        });

        if (prices.IsEmpty)
        {
            return;
        }

        WowTokenPriceWithNamespace[] pricesArray = prices.Values.ToArray();
        Array.Sort(pricesArray);
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await serializer.SavePrice(cancellationToken, pricesArray).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public string Name => GetType().Name;

    public int Priority => 80;
}