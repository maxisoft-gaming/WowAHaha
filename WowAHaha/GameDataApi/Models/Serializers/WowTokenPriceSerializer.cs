using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using WowAHaha.GameDataApi.Http;
using WowAHaha.Utils;

namespace WowAHaha.GameDataApi.Models.Serializers;

public interface IWowTokenPriceSerializer
{
    Task SavePrice(CancellationToken cancellationToken, params WowTokenPriceWithNamespace[] prices);
    Task<WowTokenPriceWithNamespace?> LoadPreviousPrice(GameDataDynamicNameSpace nameSpace, CancellationToken cancellationToken);
}

// ReSharper disable once UnusedType.Global
public class WowTokenPriceSerializer(ILogger<WowTokenPriceSerializer> logger) : IWowTokenPriceSerializer
{
    private record CachedEntry(WowTokenPriceWithNamespace Price, string Path, long FileSize, DateTime LastModified, long Position);

    private readonly ConcurrentDictionary<GameDataDynamicNameSpace, CachedEntry> _cache = new();

    public async Task SavePrice(CancellationToken cancellationToken, params WowTokenPriceWithNamespace[] prices)
    {
        var path = GetFilePath();

        await using FileStream stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var position = stream.TrimEnd();
        if (position > 0)
        {
            stream.WriteByte((byte)'\n');
        }

        foreach (WowTokenPriceWithNamespace price in prices)
        {
            await JsonSerializer.SerializeAsync(stream, price, typeof(WowTokenPriceWithNamespace), WowTokenPriceWithNamespaceSourceGenerationContext.Default, cancellationToken);
            stream.WriteByte((byte)'\n');
        }

        _ = stream.TrimEnd();
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<WowTokenPriceWithNamespace?> LoadPreviousPrice(GameDataDynamicNameSpace nameSpace, CancellationToken cancellationToken)
    {
        var path = GetFilePath();
        if (!File.Exists(path))
        {
            return null;
        }

        DateTime lastModified;
        try
        {
            lastModified = File.GetLastWriteTime(path);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get last write time for {Path}", path);
            lastModified = DateTime.UnixEpoch;
        }

        var fileSize = new FileInfo(path).Length;

        if (fileSize > 0 && TryGetFromCache(nameSpace: nameSpace, lastModified: lastModified, fileSize: fileSize, out WowTokenPriceWithNamespace? entry))
        {
            return entry;
        }

        await using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(0, SeekOrigin.End);
        const int bufferSize = 1 << 10;
        var buffer = new byte[bufferSize];
        for (var position = stream.Position; position > 0;)
        {
            position -= buffer.Length;
            position = Math.Max(0, position);
            stream.Seek(position, SeekOrigin.Begin);
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read > 0 && TryRead(buffer, read, nameSpace, fileSize, lastModified, out WowTokenPriceWithNamespace price, out position))
            {
                return price;
            }

            if (position >= stream.Position)
            {
                // no progress made
                break;
            }
        }

        return null;
    }

    private void UpdateCache(GameDataDynamicNameSpace nameSpace, DateTime lastModified, long fileSize, long position, WowTokenPriceWithNamespace price)
    {
        Debug.Assert(price.Namespace == nameSpace, "price.Namespace == nameSpace");
        _cache.AddOrUpdate(nameSpace,
            _ => new CachedEntry(price, GetFilePath(), fileSize, lastModified, position),
            (space, cachedEntry) => cachedEntry.Price.LastUpdatedTimestamp > price.LastUpdatedTimestamp || space != price.Namespace
                ? cachedEntry
                : new CachedEntry(Price: price, Path: GetFilePath(), FileSize: fileSize, LastModified: lastModified, Position: position));
    }

    private bool TryGetFromCache(GameDataDynamicNameSpace nameSpace, DateTime lastModified, long fileSize, [NotNullWhen(true)] out WowTokenPriceWithNamespace? price)
    {
        if (!_cache.TryGetValue(nameSpace, out CachedEntry? entry))
        {
            price = null;
            return false;
        }

        if (entry.LastModified != lastModified || entry.FileSize != fileSize || entry.Price.Namespace != nameSpace)
        {
            price = null;
            return false;
        }

        price = entry.Price;
        return true;
    }

    [MustUseReturnValue]
    // ReSharper disable once RedundantNullableFlowAttribute
    private bool TryRead(ReadOnlySpan<byte> buffer, int read, GameDataDynamicNameSpace nameSpace, long fileSize, DateTime lastModified,
        [NotNullWhen(true)] out WowTokenPriceWithNamespace price, out long safePosition)
    {
        var end = read;
        for (var i = read - 1; i >= 0; i--)
        {
            if (buffer[i] != '\n' && i != 0)
            {
                continue;
            }

            ReadOnlySpan<byte> line = buffer[i..end];

            line = Trim(line);

            if (line.IsEmpty)
            {
                continue;
            }

            if (line[0] != '{' && line[0] != '[')
            {
                logger.LogWarning("Failed to deserialize line: {Line}", Encoding.UTF8.GetString(line));
                break;
            }

            if (line[^1] != '}' && line[^1] != '}')
            {
                logger.LogWarning("Failed to deserialize line: {Line}", Encoding.UTF8.GetString(line));
                break;
            }

            WowTokenPriceWithNamespace? item;
            try
            {
                item = JsonSerializer.Deserialize(line, typeof(WowTokenPriceWithNamespace), WowTokenPriceWithNamespaceSourceGenerationContext.Default) as
                    WowTokenPriceWithNamespace;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                item = null;
                logger.LogError(e, "Failed to deserialize line: {Line}", Encoding.UTF8.GetString(line));
            }

            if (item is not null)
            {
                UpdateCache(nameSpace: nameSpace, lastModified: lastModified, fileSize: fileSize, position: i, price: item);
            }

            if (item?.Namespace == nameSpace)
            {
                price = item;
                safePosition = i;
                return true;
            }

            end = i;
        }

        price = default!;
        safePosition = end;
        return false;
    }

    [MustUseReturnValue]
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        while (!span.IsEmpty)
        {
            var s = span[0];
            if (s == '\r' || s == '\n' || s == ' ' || s == '\t')
            {
                span = span[1..];
                continue;
            }

            s = span[^1];
            if (s == '\r' || s == '\n' || s == ' ' || s == '\t')
            {
                span = span[..^1];
                continue;
            }

            break;
        }

        return span;
    }

    private static string GetFilePath()
    {
        return "WowTokenPrice.jsonl";
    }
}