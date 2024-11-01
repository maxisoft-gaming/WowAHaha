using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowAHaha.GameDataApi.Models.Serializers;

[JsonSourceGenerationOptions(WriteIndented = false, MaxDepth = 2, UseStringEnumConverter = true, ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WowTokenPriceWithNamespace))]
internal partial class WowTokenPriceWithNamespaceSourceGenerationContext : JsonSerializerContext
{
}