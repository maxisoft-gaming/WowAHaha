using System.Text.Json.Serialization;
using WowAHaha.GameDataApi.Http;

namespace WowAHaha.GameDataApi.Models;

public record WowTokenPriceWithNamespace(
    [property: JsonPropertyName("t")] long LastUpdatedTimestamp,
    [property: JsonPropertyName("n")] GameDataDynamicNameSpace Namespace,
    [property: JsonPropertyName("p")] long Price
) : IComparable<WowTokenPriceWithNamespace>, IComparable
{
    public int CompareTo(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return 1;
        }

        if (ReferenceEquals(this, obj))
        {
            return 0;
        }

        return obj is WowTokenPriceWithNamespace other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(WowTokenPriceWithNamespace)}");
    }

    public int CompareTo(WowTokenPriceWithNamespace? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (ReferenceEquals(null, other))
        {
            return 1;
        }

        var lastUpdatedTimestampComparison = LastUpdatedTimestamp.CompareTo(other.LastUpdatedTimestamp);
        if (lastUpdatedTimestampComparison != 0)
        {
            return lastUpdatedTimestampComparison;
        }

        var namespaceComparison = Namespace.CompareTo(other.Namespace);
        if (namespaceComparison != 0)
        {
            return namespaceComparison;
        }

        return Price.CompareTo(other.Price);
    }
}