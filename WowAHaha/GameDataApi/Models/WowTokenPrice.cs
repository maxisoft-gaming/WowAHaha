using System.Text.Json.Serialization;

namespace WowAHaha.GameDataApi.Models;

public record WowTokenPrice(
    [property: JsonPropertyName("last_updated_timestamp")]
    long LastUpdatedTimestamp,
    [property: JsonPropertyName("price")] long Price
);