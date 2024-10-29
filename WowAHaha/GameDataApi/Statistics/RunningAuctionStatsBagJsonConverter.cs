using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Dictionaries;
using WowAHaha.GameDataApi.Models;

namespace WowAHaha.GameDataApi.Statistics;

/// <summary>
///     This converter updates the <see cref="RunningAuctionStatsBag" /> as soon as a valid auction item is found in the
///     JSON. This eliminates the need for an temporary full representation of each auction in memory.
///     <remarks>
///         The <c>System.Text.Json</c> parser internal does buffer the entire HTTP response data in memory (this takes about
///         10-20MB of memory).
///     </remarks>
/// </summary>
public class RunningAuctionStatsBagJsonConverter : JsonConverter<RunningAuctionStatsBag>
{
    private static readonly HashSet<string> SkippedKnownProperties = ["id", "_links"];

    private static void ReadAuctions(ref Utf8JsonReader reader, ref RunningAuctionStatsBag bag)
    {
        ulong itemId = 0;
        long quantity = 0;
        long unitPrice = 0;
        var timeLeft = ApiAuctionTimeLeft.Unknown;

        var startDepth = reader.CurrentDepth;
        var itemStartDepth = -1;
        var cleanValues = true;

        while (reader.Read())
        {
            if (reader.CurrentDepth < startDepth)
            {
                return;
            }

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (reader.TokenType == JsonTokenType.StartObject && (reader.CurrentDepth - 1 == startDepth || cleanValues))
            {
                itemId = 0;
                quantity = 0;
                unitPrice = 0;
                timeLeft = ApiAuctionTimeLeft.Unknown;
                itemStartDepth = -1;
                cleanValues = false;
                continue;
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (itemId > 0 && quantity > 0 && unitPrice > 0)
                {
                    var auction = new ApiPartialAuction(itemId, quantity, unitPrice,
                        timeLeft);
                    try
                    {
                        RunningAuctionStats stats = bag.AuctionsStats.GetOrAdd(auction.ItemId,
                            () => new RunningAuctionStats(auction.ItemId));
                        if (!stats.TryPush(auction))
                        {
                            throw new Exception($"Failed to push auction {auction.ItemId}");
                        }

                        bag.IncrementCount();
                        cleanValues = true;
                    }
                    catch (Exception e)
                    {
                        bag.IncrementSkippedCount();
                        cleanValues = true;
#if DEBUG
                        Debug.WriteLine(
                            $"Error while adding auction {auction.ItemId} at position {reader.BytesConsumed}.");
                        Debug.WriteLine($"Exception: {e.Message}\nStackTrace: {e.StackTrace}");
#endif
                    }
                }
                else if (reader.CurrentDepth < itemStartDepth)
                {
                    bag.IncrementSkippedCount();
                    cleanValues = true;
#if DEBUG
                    Debug.WriteLine(
                        $"Discarding auction at position {reader.BytesConsumed} because it is missing data: itemId={itemId}, quantity={quantity}, unitPrice={unitPrice}, timeLeft={timeLeft}");
#endif
                }

                continue;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            ReadOnlySpan<byte> propertyName =
                reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;

            if (propertyName.SequenceEqual("item"u8))
            {
                if (itemStartDepth < 0)
                {
                    itemStartDepth = reader.CurrentDepth;
                }

                var readCount = 0;
                while (itemId <= 0)
                {
                    if (reader.CurrentDepth < itemStartDepth)
                    {
                        break;
                    }

                    if (!reader.Read())
                    {
                        break;
                    }

                    if (readCount == 0 && reader.TokenType is JsonTokenType.Number or JsonTokenType.String)
                    {
                        itemId = ReadUlong(ref reader);
                        continue;
                    }

                    readCount++;

                    if (reader.CurrentDepth < itemStartDepth)
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        ReadOnlySpan<byte> itemPropertyName = reader.HasValueSequence
                            ? reader.ValueSequence.ToArray()
                            : reader.ValueSpan;

                        if (itemPropertyName.SequenceEqual("id"u8))
                        {
                            if (!reader.Read())
                            {
                                break;
                            }

                            try
                            {
                                itemId = ReadUlong(ref reader);
                            }
                            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
                            {
                                continue;
                            }

                            reader.TrySkip();
                        }
                        else
                        {
#if DEBUG
                            Debug.WriteLine($"Unknown property at position {reader.BytesConsumed}: {Encoding.UTF8.GetString(itemPropertyName)}");
#endif
                            reader.TrySkip();
                        }
                    }
                }
            }

            else if (propertyName.SequenceEqual("quantity"u8))
            {
                if (!reader.Read())
                {
                    break;
                }

                quantity = ReadLong(ref reader);
            }
            else if (propertyName.SequenceEqual("unit_price"u8))
            {
                if (!reader.Read())
                {
                    break;
                }

                unitPrice = ReadLong(ref reader);
            }
            else if (propertyName.SequenceEqual("time_left"u8))
            {
                if (!reader.Read())
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    ReadOnlySpan<byte> timeLeftString =
                        reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;

                    timeLeft = ParseTimeLeft(timeLeftString);
                }
            }
            else
            {
#if DEBUG
                var propertyNameString = Encoding.UTF8.GetString(propertyName);
                if (!SkippedKnownProperties.Contains(propertyNameString))
                {
                    Debug.WriteLine($"Unknown property at position {reader.BytesConsumed}: {propertyNameString}");
                }
#endif
                // Skip unknown properties
                reader.TrySkip();
            }
        }
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    public override RunningAuctionStatsBag Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var res = new RunningAuctionStatsBag();

        // Read the start of the object
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expecting a non-empty Object at position {reader.BytesConsumed}", null,
                null, reader.BytesConsumed);
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                ReadOnlySpan<byte> propertyName =
                    reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;

                if (propertyName.SequenceEqual("auctions"u8))
                {
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        ReadAuctions(ref reader, ref res);
                    }
                    else
                    {
                        throw new JsonException($"Expecting a non-empty Array at position {reader.BytesConsumed}", null,
                            null, reader.BytesConsumed);
                    }
                }
                else
                {
                    #if DEBUG
                    var propertyNameString = Encoding.UTF8.GetString(propertyName);
                    if (!SkippedKnownProperties.Contains(propertyNameString))
                    {
                        Debug.WriteLine($"Unknown property at position {reader.BytesConsumed}: {propertyNameString}");
                    }
                    #endif
                }
            }
        }

        return res;
    }

    public override void Write(Utf8JsonWriter writer, RunningAuctionStatsBag value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    private static ulong ReadUlong(ref Utf8JsonReader reader, string? path = null, long? lineNumber = null)
    {
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetUInt64();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (ulong.TryParse(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan,
                    out var parsedId))
            {
                return parsedId;
            }
        }

        throw new JsonException($"Expecting a number or string at position {reader.BytesConsumed}", path,
            lineNumber, reader.BytesConsumed);
    }

    private static long ReadLong(ref Utf8JsonReader reader, string? path = null, long? lineNumber = null)
    {
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (long.TryParse(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan,
                    out var parsedId))
            {
                return parsedId;
            }

            throw new JsonException($"Expecting a number or string at position {reader.BytesConsumed}",
                path, lineNumber, reader.BytesConsumed);
        }

        throw new JsonException($"Expecting a number or string at position {reader.BytesConsumed}", path,
            lineNumber, reader.BytesConsumed);
    }

    private static ApiAuctionTimeLeft ParseTimeLeft(ReadOnlySpan<byte> timeLeftString)
    {
        if (timeLeftString.SequenceEqual("VERY_SHORT"u8))
        {
            return ApiAuctionTimeLeft.VeryShort;
        }

        if (timeLeftString.SequenceEqual("SHORT"u8))
        {
            return ApiAuctionTimeLeft.Short;
        }

        if (timeLeftString.SequenceEqual("MEDIUM"u8))
        {
            return ApiAuctionTimeLeft.Medium;
        }

        if (timeLeftString.SequenceEqual("LONG"u8))
        {
            return ApiAuctionTimeLeft.Long;
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (timeLeftString.SequenceEqual("VERY_LONG"u8))
        {
            return ApiAuctionTimeLeft.VeryLong;
        }

        return ApiAuctionTimeLeft.Unknown;
    }
}