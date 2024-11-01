using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Logging;
using TDigestNet;
using WowAHaha.GameDataApi.Http;
using WowAHaha.GameDataApi.Statistics;
using WowAHaha.Utils;
using WowAHaha.WowItems;

namespace WowAHaha.GameDataApi.Models.Serializers;

public interface ICommodityAuctionSerializer
{
    public Task WriteToFiles(RunningAuctionStatsBag bag, GameDataDynamicNameSpace nameSpace, DateTimeOffset modifiedTimestamp, CancellationToken cancellationToken);
    public Task<CollectAndSaveCommoditiesExecutionSummary?> LoadPreviousSummary(GameDataDynamicNameSpace nameSpace, CancellationToken cancellationToken);
}

// ReSharper disable once UnusedType.Global
public class CommodityAuctionSerializer(IItemToExpansionResolver itemToExpansionResolver, ILogger<CommodityAuctionSerializer> logger) : ICommodityAuctionSerializer
{
    private const string LatestSummaryFileName = "latest_summary.json";

    private static readonly Lazy<JsonSerializerOptions> LatestSummaryJsonSerializerOptions = new(() =>
        new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }
    );

    private static readonly Lazy<JsonSerializerOptions> SummaryHistoryJsonSerializerOptions = new(() =>
        new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }
    );

    private static readonly StandardFormat GeneralFormat = StandardFormat.Parse("G");
    private static readonly StandardFormat StatsFormat = StandardFormat.Parse("F3");

    private static readonly char Separator = 456.123f.ToString("G").Contains(',') ? ';' : ',';

    public async Task WriteToFiles(RunningAuctionStatsBag bag, GameDataDynamicNameSpace nameSpace, DateTimeOffset modifiedTimestamp, CancellationToken cancellationToken)
    {
        Guid? bagHash = null;
        var dirPath = GetDirectoryPath(nameSpace);
        if (Directory.Exists(dirPath))
        {
            bagHash = bag.ComputeHash(modifiedTimestamp);
            CollectAndSaveCommoditiesExecutionSummary? summary = await LoadPreviousSummary(nameSpace, cancellationToken);
            if (summary is not null && summary.Hash == bagHash && (summary.DataTimestamp - modifiedTimestamp).Duration() < TimeSpan.FromHours(6))
            {
                logger.LogDebug("Skipping {DirPath} as previous hash match current", dirPath);
                return;
            }
        }

        DirectoryInfo dir = Directory.CreateDirectory(dirPath);
        Debug.Assert(dir.Exists);

        var files = new Dictionary<string, FileStream>((int)WowExpansion.Latest);
        var filesSafeIndices = new Dictionary<string, long>();
        var completedSuccessfully = false;

        try
        {
            foreach ((ApiItemIdentifier itemId, RunningAuctionStats stats) in bag.AuctionsStats.OrderBy(pair => pair.Key))
            {
                Debug.Assert(itemId == stats.ItemId, "Item id mismatch");
                if (stats.ProcessedCount <= 0 || stats.TotalQuantity <= 0 || itemId != stats.ItemId)
                {
                    continue;
                }

                stats.Complete();
                WowExpansion expansion = itemToExpansionResolver.GetExpansion(itemId);
                var path = Path.Combine(dirPath, $"{(int)expansion:00}_{expansion}.csv");
                FileStream stream;

                if (!files.TryGetValue(path, out FileStream? previousStream))
                {
                    stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

                    try
                    {
                        var seekPosition = stream.TrimEnd();
                        Debug.Assert(seekPosition >= 0, "Failed to remove last line");
                        {
                            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                            writer.AutoFlush = true;
                            if (seekPosition == 0)
                            {
                                await WriteCsvHeader(writer, expansion, cancellationToken);
                            }
                            else
                            {
                                await writer.WriteLineAsync();
                            }
                        }

                        Debug.Assert(stream.Position > seekPosition);
                        files[path] = stream;
                    }
                    catch (Exception)
                    {
                        logger.LogDebug("Exception caught, will close stream");
                        await stream.DisposeAsync();
                        throw;
                    }
                }
                else
                {
                    stream = previousStream;
                }

                WriteCsvEntry(stream, expansion, stats, modifiedTimestamp);
                if (filesSafeIndices.TryGetValue(path, out var filesSafeIndex) && stream.Position - filesSafeIndex > 256)
                {
                    await stream.FlushAsync(cancellationToken);
                    filesSafeIndices[path] = stream.Position;
                }
            }

            completedSuccessfully = true;
        }
        finally
        {
            using CancellationTokenSource cts = cancellationToken.IsCancellationRequested
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Cancellation requested while writing to files in {Path}. Will try to finish up in 10 seconds", dirPath);
            }

            cts.CancelAfter(TimeSpan.FromSeconds(10));

            Task writeSummaryTask = WriteSummary(nameSpace, bag, modifiedTimestamp, cts.Token);
            foreach ((var path, FileStream stream) in files)
            {
                if (stream.CanWrite)
                {
                    _ = stream.TrimEnd();

                    if (!completedSuccessfully && filesSafeIndices.TryGetValue(path, out var filesSafeIndex) && filesSafeIndex > 0 && filesSafeIndex < stream.Position)
                    {
                        stream.SetLength(filesSafeIndex);
                        _ = stream.TrimEnd();
                    }
                }


                await stream.FlushAsync(cts.Token);

                await stream.DisposeAsync();
            }

            await writeSummaryTask.ConfigureAwait(false);
        }


        files.Clear();
    }

    public async Task<CollectAndSaveCommoditiesExecutionSummary?> LoadPreviousSummary(GameDataDynamicNameSpace nameSpace, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetDirectoryPath(nameSpace), LatestSummaryFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<CollectAndSaveCommoditiesExecutionSummary>(json, LatestSummaryJsonSerializerOptions.Value);
    }

    public string GetDirectoryPath(GameDataDynamicNameSpace nameSpace)
    {
        return $"commodities/{nameSpace.ToString().ToLower()}";
    }

    private async Task WriteSummary(GameDataDynamicNameSpace nameSpace, RunningAuctionStatsBag bag, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var dirPath = GetDirectoryPath(nameSpace);
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        var path = Path.Combine(dirPath, LatestSummaryFileName);
        var summary = new CollectAndSaveCommoditiesExecutionSummary
        {
            Hash = bag.ComputeHash(timestamp),
            DataTimestamp = timestamp,
            NameSpace = nameSpace,
            Count = bag.Count,
            SkippedCount = bag.SkippedCount
        };
        var json = JsonSerializer.Serialize(summary, LatestSummaryJsonSerializerOptions.Value);
        await File.WriteAllTextAsync(path, json, cancellationToken);


        path = Path.Combine(dirPath, "summaries.jsonl");

        await using FileStream stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var seekPosition = stream.TrimEnd();
        if (seekPosition > 0)
        {
            stream.WriteByte((byte)'\n');
        }

        await JsonSerializer.SerializeAsync(stream, summary, SummaryHistoryJsonSerializerOptions.Value, cancellationToken);
        _ = stream.TrimEnd();
    }

    private static async Task WriteCsvHeader(StreamWriter writer, WowExpansion expansion, CancellationToken cancellationToken)
    {
        var headerColumns = new[]
        {
            "timestamp", "id", "min", "qty", "n", "w", "mean", "std",
            "skew", "kurt", "q01", "q05", "q10", "q25", "q50", "q75"
        };
        var content = string.Join(Separator, headerColumns);
        await writer.WriteLineAsync(content);
        await writer.FlushAsync(cancellationToken);
    }

    // ReSharper disable once MemberCanBeMadeStatic.Local
    private void WriteCsvEntry<T>(T writer, WowExpansion expansion, RunningAuctionStats stats, DateTimeOffset time) where T : Stream
    {
        var separator = (byte)Separator;
        Debug.Assert(Separator == (char)separator);
        Span<byte> buffer = stackalloc byte[64];
        var position = 0;

        position = WriteLong(time.ToUnixTimeMilliseconds(), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)(ulong)stats.ItemId, writer, buffer, position, GeneralFormat, separator);

        RunningWeightedStatistics runningStats = stats.RunningStats;
        var minimum = stats.RunningStats.Minimum;
        TDigest quantiles = stats.TDigest;

        if (!double.IsNormal(minimum))
        {
            minimum = stats.TDigest.Min;
        }


        position = WriteLong((long)minimum, writer, buffer, position, GeneralFormat, separator);
        position = WriteLong(stats.TotalQuantity, writer, buffer, position, GeneralFormat, separator);
        position = WriteLong(stats.RunningStats.Count, writer, buffer, position, GeneralFormat, separator);
        position = WriteDouble(stats.TotalWeight, writer, buffer, position, StatsFormat, separator);
        position = WriteLong((long)runningStats.Mean, writer, buffer, position, GeneralFormat, separator);
        var std = runningStats.PopulationStandardDeviation;
        if (std <= 0 || !double.IsNormal(std))
        {
            std = runningStats.StandardDeviation;
        }

        position = WriteDouble(std, writer, buffer, position, StatsFormat, separator);
        position = WriteDouble(runningStats.PopulationSkewness, writer, buffer, position, StatsFormat, separator);
        position = WriteDouble(runningStats.PopulationKurtosis, writer, buffer, position, StatsFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.01), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.05), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.10), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.25), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.5), writer, buffer, position, GeneralFormat, separator);
        position = WriteLong((long)quantiles.Quantile(0.75), writer, buffer, position, GeneralFormat, (byte)'\n');

        if (position > 0)
        {
            writer.Write(buffer[..position]);
        }

        return;

        [MustUseReturnValue]
        static int WriteLong(long value, T writer, Span<byte> buffer, int position, StandardFormat priceFormat, byte separator)
        {
            if (value != long.MaxValue)
            {
                if (!Utf8Formatter.TryFormat(value, buffer[position..], out var bytesWritten, priceFormat))
                {
                    // most likely not enough space
                    if (position > 0)
                    {
                        writer.Write(buffer[..position]);
                        position = 0;
                    }

                    if (!Utf8Formatter.TryFormat(value, buffer[position..], out bytesWritten, priceFormat))
                    {
                        throw new InvalidOperationException("Failed to format long");
                    }
                }

                position += bytesWritten;
                if (position > (buffer.Length - 1) >> 1)
                {
                    writer.Write(buffer[..position]);
                    position = 0;
                    writer.WriteByte(separator);
                }
                else
                {
                    buffer[position++] = separator;
                }
            }
            else if (position > (buffer.Length - 1) >> 1)
            {
                writer.Write(buffer[..position]);
                position = 0;
                writer.WriteByte(separator);
            }
            else
            {
                buffer[position++] = separator;
            }

            return position;
        }

        [MustUseReturnValue]
        static int WriteDouble(double value, T writer, Span<byte> buffer, int position, StandardFormat priceFormat, byte separator)
        {
            if (double.IsNormal(value))
            {
                if (!Utf8Formatter.TryFormat(value, buffer[position..], out var bytesWritten, priceFormat))
                {
                    if (position > 0)
                    {
                        writer.Write(buffer[..position]);
                        position = 0;
                    }

                    if (!Utf8Formatter.TryFormat(value, buffer[position..], out bytesWritten, priceFormat))
                    {
                        throw new InvalidOperationException("Failed to format double");
                    }
                }

                position += bytesWritten;
                if (position > (buffer.Length - 1) >> 1)
                {
                    writer.Write(buffer[..position]);
                    position = 0;
                    writer.WriteByte(separator);
                }
                else
                {
                    buffer[position++] = separator;
                }
            }
            else if (position > (buffer.Length - 1) >> 1)
            {
                writer.Write(buffer[..position]);
                position = 0;
                writer.WriteByte(separator);
            }
            else if (separator != 0)
            {
                buffer[position++] = separator;
            }

            return position;
        }
    }
}