using JetBrains.Annotations;

namespace WowAHaha.GameDataApi.Http;

public class BattleNetWebApiAccessToken
{
    private AuthTokenResponse _response = new();

    internal AuthTokenResponse Response
    {
        get => _response;
        set
        {
            _response = value;
            Expiration = DateTimeOffset.UtcNow.AddSeconds(value.ExpiresIn);
        }
    }

    private DateTimeOffset Expiration { get; set; } = DateTimeOffset.MinValue;

    public string AccessToken => Response.AccessToken;

    public string TokenType => Response.TokenType;

    public bool IsExpired()
    {
        return DateTimeOffset.UtcNow > Expiration;
    }

    [MustUseReturnValue]
    public bool TryGetAccessToken(out string accessToken)
    {
        accessToken = Response.AccessToken;
        return !IsExpired() && !string.IsNullOrEmpty(accessToken);
    }
}