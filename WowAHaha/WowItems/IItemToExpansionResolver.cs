using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WowAHaha.GameDataApi.Models;

namespace WowAHaha.WowItems;

public interface IItemToExpansionResolver
{
    /// <summary>
    ///     Get the wow expansion of the given item
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public WowExpansion GetExpansion(ApiItemIdentifier itemId);
}

public class ItemToExpansionResolverConfigurations
{
    public int MaxCacheSize { get; set; } = -1;
    
    public string ItemToExpansionFileName { get; set; } = "wow_item_to_expansion.json";
}

// ReSharper disable once UnusedType.Global
public sealed class ItemToExpansionResolver : IItemToExpansionResolver
{
    private readonly ConcurrentDictionary<long, WowExpansion> _cache = new();
    private readonly Lazy<ArrayList<long>> _itemIds;
    private readonly Lazy<Dictionary<long, WowExpansion>> _itemToExpansions;
    private readonly ILogger<ItemToExpansionResolver> _logger;
    private readonly ItemToExpansionResolverConfigurations _options = new();

    public ItemToExpansionResolver(ILogger<ItemToExpansionResolver> logger, Boxed<CancellationToken> cancellationToken, IConfiguration configuration)
    {
        _logger = logger;
        configuration.GetSection(nameof(ItemToExpansionResolver)).Bind(_options);
        _itemToExpansions = new Lazy<Dictionary<long, WowExpansion>>(() =>
        {
            Dictionary<long, WowExpansion> res = LoadItemToExpansion(cancellationToken).GetAwaiter().GetResult();
            return res;
        });

        _itemIds = new Lazy<ArrayList<long>>(() =>
        {
            ArrayList<long> keys = _itemToExpansions.Value.Keys.ToArrayList();
            keys.Sort();
            return keys;
        });
    }

    private string ItemToExpansionFileName => _options.ItemToExpansionFileName;

    public WowExpansion GetExpansion(ApiItemIdentifier itemId)
    {
        return GetExpansionFromULong(itemId.ItemId);
    }

    private WowExpansion GetExpansionFromULong(ulong itemId)
    {
        var itemIdLong = (long)itemId;
        if (itemIdLong < 0)
        {
            _logger.LogWarning("ItemId {itemId} not valid", itemId);
            return WowExpansion.Unknown;
        }

        if (_cache.TryGetValue(itemIdLong, out WowExpansion expansion))
        {
            return expansion;
        }

        if (_itemToExpansions.Value.TryGetValue(itemIdLong, out expansion))
        {
            return expansion;
        }

        ReadOnlySpan<long> itemIds = _itemIds.Value.AsSpan();
        var index = itemIds.BinarySearch(itemIdLong);
        if (index < 0)
        {
            index = -index;
        }

        if (index >= itemIds.Length)
        {
            return WowExpansion.Latest; // TODO fix when a new expansion is released
        }

        index = Math.Max(0, index);
        index = Math.Min(itemIds.Length - 1, index);
        var left = itemIds[index];

        expansion = _itemToExpansions.Value.GetValueOrDefault(left, expansion);

        // ReSharper disable once InvertIf
        if (expansion is not WowExpansion.Unknown)
        {
            AddToCache(itemIdLong, ref expansion);
        }

        return expansion;
    }

    private bool AddToCache(long itemId, ref WowExpansion expansion)
    {
        if (_cache.Count >= _options.MaxCacheSize)
        {
            return false;
        }

        if (_cache.TryAdd(itemId, expansion))
        {
            return true;
        }

        if (_cache.TryGetValue(itemId, out WowExpansion tmp))
        {
            expansion = tmp;
        }

        return false;
    }

    /// <summary>
    /// Loads the item to expansion dictionary from a file named "ItemToExpansion.json" first in the current path, then
    /// loads the embedded one if not found
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    private async Task<Dictionary<long, WowExpansion>> LoadItemToExpansion(CancellationToken cancellationToken)
    {
        var jsonSerializerOptions = new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals };

        // Try to read from a file wow_item_to_expansion.json
        var fileInfo = new FileInfo(ItemToExpansionFileName);
        if (fileInfo.Exists)
        {
            await using FileStream stream = fileInfo.OpenRead();
            try
            {
                var res = await JsonSerializer.DeserializeAsync<Dictionary<long, WowExpansion>>(stream, jsonSerializerOptions, cancellationToken);
                if (res != null)
                {
                    return res;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while reading file {File}", fileInfo.FullName);
            }
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(ItemToExpansionFileName))
            {
                continue;
            }

            await using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogError("Resource {ResourceName} not found", resourceName);
                continue;
            }

            try
            {
                var res = await JsonSerializer.DeserializeAsync<Dictionary<long, WowExpansion>>(stream, jsonSerializerOptions,
                    cancellationToken);
                if (res != null)
                {
                    return res;
                }
            }
            catch (JsonException e)
            {
                _logger.LogError(e, "Error while deserializing {ResourceName}", resourceName);
            }
        }

        throw new FileNotFoundException("Neither file nor resource found for wow_item_to_expansion.json");
    }
}