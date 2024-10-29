using Microsoft.Extensions.Configuration;

namespace WowAHaha.GameDataApi.Http;

public interface IUrlRewriter
{
    string Rewrite(string url);
    Uri Rewrite(Uri url);
    
    public string this[string key] => Rewrite(key);
    public Uri this[Uri key] => Rewrite(key);
}

public sealed class UrlRewriter : IUrlRewriter
{
    private readonly IConfigurationSection _configuration;

    internal UrlRewriter(IConfigurationSection configuration)
    {
        _configuration = configuration;
    }

    // ReSharper disable once UnusedMember.Global
    public UrlRewriter(IConfiguration configuration) : this(configuration.GetSection(nameof(UrlRewriter)))
    {
        
    }

    public string this[string key] => Rewrite(key);
    public Uri this[Uri key] => Rewrite(key);

    public string Rewrite(string url)
    {
        return _configuration.GetValue(url, url) ?? url;
    }

    public Uri Rewrite(Uri url)
    {
        return new Uri(Rewrite(url.AbsoluteUri), UriKind.Absolute);
    }
}