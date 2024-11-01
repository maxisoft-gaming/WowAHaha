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

            if (read > 0 && TryRead(buffer, read, nameSpace, out WowTokenPriceWithNamespace price, out position))
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

    [MustUseReturnValue]
    // ReSharper disable once RedundantNullableFlowAttribute
    private bool TryRead(ReadOnlySpan<byte> buffer, int read, GameDataDynamicNameSpace nameSpace, [NotNullWhen(true)] out WowTokenPriceWithNamespace price, out long safePosition)
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