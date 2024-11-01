using System.Text.Json.Serialization;

namespace WowAHaha.GameDataApi.Models.Serializers;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WowTokenPrice))]
internal partial class WowTokenPriceSourceGenerationContext : JsonSerializerContext
{
}