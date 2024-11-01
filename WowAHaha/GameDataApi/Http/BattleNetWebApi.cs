using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JasperFx.Core.Reflection;
using Maxisoft.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WowAHaha.GameDataApi.Models;
using WowAHaha.GameDataApi.Models.Serializers;
using WowAHaha.GameDataApi.Statistics;
using WowAHaha.Utils;

namespace WowAHaha.GameDataApi.Http;

public class BattleNetWebApiConfiguration
{
    public string OAuthTokenUri { get; set; } = "https://oauth.battle.net/token";
    public string OAuthGrantTypeKey { get; set; } = "grant_type";
    public string OAuthGrantTypeValue { get; set; } = "client_credentials";

    public bool CorrectTokenExpiration { get; set; } = true;
}

public interface IBattleNetWebApi
{
    Task<RunningAuctionStatsBag> GetCommoditiesAuctions(GameDataDynamicNameSpace dynamicNameSpace, string locale = "auto",
        Action<(DateTimeOffset? Date, DateTimeOffset? LastModified)>? onDateAndLastModifiedHook = null,
        CancellationToken cancellationToken = default);

    Task<WowTokenPrice?> GetWowTokenPrice(GameDataDynamicNameSpace dynamicNameSpace, string locale = "auto", CancellationToken cancellationToken = default);
}

public class BattleNetWebApi(
    HttpClient client,
    IConfiguration configurationProvider,
    ILogger<BattleNetWebApi> logger,
    IUrlRewriter urlRewriter
    ) : IBattleNetWebApi
    
{
    private readonly BattleNetWebApiAccessToken _accessToken = new();

    private readonly SemaphoreSlim _accessTokenSemaphore = new(1, 1);

    private readonly Lazy<NetworkCredential> _credentials =
        new(() => ReadCredentials(GetConfigurationSection(configurationProvider), logger));

    private readonly object _lockObject = new();
    private BattleNetWebApiConfiguration? _configuration;

    private IConfigurationSection Configuration { get; } = GetConfigurationSection(configurationProvider);

    public async Task<WowTokenPrice?> GetWowTokenPrice(GameDataDynamicNameSpace dynamicNameSpace, string locale = "auto", CancellationToken cancellationToken = default)
    {
        Uri requestUri = BuildWowTokenUri(dynamicNameSpace, locale);
        BattleNetWebApiAccessToken accessToken = await GetAccessToken(cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.AccessToken);

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{response.StatusCode} {response.ReasonPhrase} {error}");
        }
        
        return await response.Content.ReadFromJsonAsync(typeof(WowTokenPrice), WowTokenPriceSourceGenerationContext.Default, cancellationToken) as WowTokenPrice;
    }

    public async Task<RunningAuctionStatsBag> GetCommoditiesAuctions(GameDataDynamicNameSpace dynamicNameSpace, string locale = "auto",
        Action<(DateTimeOffset? Date, DateTimeOffset? LastModified)>? onDateAndLastModifiedHook = null,
        CancellationToken cancellationToken = default)
    {
        Uri requestUri = BuildAuctionsCommoditiesUri(dynamicNameSpace, locale);

        BattleNetWebApiAccessToken accessToken = await GetAccessToken(cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.AccessToken);

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (onDateAndLastModifiedHook is not null)
        {
            (DateTimeOffset? date, DateTimeOffset? lastModified) = GetDateAndLastModified(response);
            onDateAndLastModifiedHook((date, lastModified));
        }


        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{response.StatusCode} {response.ReasonPhrase} {error}");
        }

        response.EnsureSuccessStatusCode();
        long defaultBufferSize = -1;
        if (response.Content.Headers.ContentLength is > 1 << 20)
        {
            defaultBufferSize = 1L << checked((int)(Math2.Log2(response.Content.Headers.ContentLength.Value) + 1));
        }


        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new RunningAuctionStatsBagJsonConverter());
        if (defaultBufferSize > 1024)
        {
            options.DefaultBufferSize = checked((int)defaultBufferSize);
        }

        var res = await response.Content.ReadFromJsonAsync<RunningAuctionStatsBag>(options, cancellationToken);
        return res ?? new RunningAuctionStatsBag();
    }

    private BattleNetWebApiConfiguration GetApiConfiguration()
    {
        // ReSharper disable InvertIf
        if (_configuration is null)
        {
            lock (_lockObject)
            {
                if (_configuration is null)
                {
                    _configuration ??= new BattleNetWebApiConfiguration();
                    Configuration.Bind(_configuration);
                }
            }
        }
        // ReSharper restore InvertIf

        return _configuration;
    }

    internal static IConfigurationSection GetConfigurationSection(IConfiguration configuration)
    {
        return configuration.GetSection(typeof(BattleNetWebApi).NameInCode());
    }

    private static NetworkCredential ReadCredentials(IConfigurationSection configuration, ILogger logger)
    {
        string? clientId;
        clientId = configuration.GetValue<string?>(nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                $"No ClientId provided. Please set environment variable or configuration value for {configuration.Key}:{nameof(clientId)}.");
        }

        string? clientSecret;
        clientSecret = configuration.GetValue<string?>(nameof(clientSecret));
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                $"No ClientSecret provided. Please set environment variable or configuration value for {configuration.Key}:{nameof(clientSecret)}.");
        }

        var credentialKey = configuration.GetValue<string>("CredentialEncryptionKey", "");
        if (!string.IsNullOrEmpty(credentialKey))
        {
            credentialKey = credentialKey.Trim();
            clientId = DumbDumbEncryption.Decrypt(clientId, credentialKey);
            clientSecret = DumbDumbEncryption.Decrypt(clientSecret, credentialKey);
            logger.LogDebug("Credential encryption key provided. Decrypting credentials...");
        }
        else
        {
            logger.LogWarning("No credential encryption key provided. Using unencrypted credentials.");
        }

        return new NetworkCredential(clientId, clientSecret);
    }

    private async Task<AuthTokenResponse> GetAccessTokenAsync(NetworkCredential credential,
        CancellationToken cancellationToken)
    {
        BattleNetWebApiConfiguration config = GetApiConfiguration();
        var tokenRequestUri = urlRewriter[config.OAuthTokenUri];

        var request = new HttpRequestMessage(HttpMethod.Post, tokenRequestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credential.UserName}:{credential.Password}")));
        request.Content = new FormUrlEncodedContent(new[]
        {
            KeyValuePair.Create(config.OAuthGrantTypeKey, config.OAuthGrantTypeValue)
        });

        DateTimeOffset requestStartDate = DateTimeOffset.Now;

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        response.EnsureSuccessStatusCode();

        var authToken =
            await response.Content.ReadFromJsonAsync<AuthTokenResponse>(options,
                cancellationToken);
        DateTimeOffset requestEndDate = response.Headers.Date ?? DateTimeOffset.Now;
        if (authToken is null)
        {
            throw new Exception("Failed to retrieve access token");
        }

        if (config.CorrectTokenExpiration)
        {
            checked
            {
                authToken.ExpiresIn -= (long)(requestEndDate - requestStartDate).TotalSeconds;
            }
        }


        return authToken;
    }

    private async ValueTask<BattleNetWebApiAccessToken> GetAccessToken(CancellationToken cancellationToken)
    {
        if (!_accessToken.TryGetAccessToken(out var accessToken))
        {
            await _accessTokenSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (!_accessToken.TryGetAccessToken(out accessToken))
                {
                    _accessToken.Response = await GetAccessTokenAsync(_credentials.Value, cancellationToken);
                }
            }
            finally
            {
                _accessTokenSemaphore.Release();
            }
        }

        if (string.IsNullOrEmpty(accessToken) && !_accessToken.TryGetAccessToken(out accessToken))
        {
            throw new Exception("Failed to retrieve access token");
        }

        return _accessToken;
    }

    private Uri BuildAuctionsCommoditiesUri(GameDataDynamicNameSpace dynamicNameSpace, string locale)
    {
        Uri requestUri = urlRewriter[dynamicNameSpace.ToHostUri()];
        var nameSpaceDynamic = dynamicNameSpace.ToDynamicName();
        var requestUriString = urlRewriter[$"{requestUri}/data/wow/auctions/commodities"]
                               + $"?namespace={nameSpaceDynamic}";

        if (!string.IsNullOrEmpty(locale))
        {
            if (locale == "auto")
            {
                locale = dynamicNameSpace.GetLocale();
            }

            requestUriString += $"&locale={locale}";
        }

        return new Uri(urlRewriter[requestUriString]);
    }
    
    private Uri BuildWowTokenUri(GameDataDynamicNameSpace dynamicNameSpace, string locale)
    {
        Uri requestUri = urlRewriter[dynamicNameSpace.ToHostUri()];
        var nameSpaceDynamic = dynamicNameSpace.ToDynamicName();
        var requestUriString = urlRewriter[$"{requestUri}/data/wow/token/index"]
                               + $"?namespace={nameSpaceDynamic}";

        if (!string.IsNullOrEmpty(locale))
        {
            if (locale == "auto")
            {
                locale = dynamicNameSpace.GetLocale();
            }

            requestUriString += $"&locale={locale}";
        }

        return new Uri(urlRewriter[requestUriString]);
    }

    private (DateTimeOffset? Date, DateTimeOffset? LastModified) GetDateAndLastModified(HttpResponseMessage response)
    {
        DateTimeOffset? date = response.Headers.Date;
        DateTimeOffset? lastModified = response.Content.Headers.LastModified;

        if (date is null)
        {
            if (TryParseDate(response.Headers, out DateTimeOffset tmp) || TryParseDate(response.Content.Headers, out tmp))
            {
                date = tmp;
            }

            date ??= lastModified;
        }

        // ReSharper disable once InvertIf
        if (lastModified is null)
        {
            if (TryParseDate(response.Content.Headers, out DateTimeOffset tmp, "Last-Modified") || TryParseDate(response.Headers, out tmp, "Last-Modified"))
            {
                lastModified = tmp;
            }
        }

        return (date, lastModified);
    }

    private bool TryParseDate<T>(T headers, out DateTimeOffset result, string name = "Date") where T : HttpHeaders
    {
        if (headers.TryGetValues(name, out IEnumerable<string>? dates))
        {
            foreach (var dateStr in dates)
            {
                if (dateStr.Length < 1)
                {
                    continue;
                }

                if (long.TryParse(dateStr, out var longDate))
                {
                    result = DateTimeOffset.FromUnixTimeSeconds(longDate);
                    return true;
                }


                try
                {
                    if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    logger.LogDebug("Failed to parse Date: {DateStr} {e}", dateStr, e);
                }

                try
                {
                    result = DateTimeOffset.Parse(dateStr, CultureInfo.CurrentCulture);
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogDebug("Failed to parse Date: {DateStr} {e}", dateStr, e);
                }
            }
        }

        result = default;
        return false;
    }
}