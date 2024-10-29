using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace WowAHaha.GameDataApi.Http;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class AuthTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "";

    [JsonPropertyName("expires_in")] // in seconds
    public long ExpiresIn { get; set; } = 0;

    public string Sub { get; set; } = "";
}