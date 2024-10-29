using System.Diagnostics.CodeAnalysis;
using static WowAHaha.GameDataApi.Http.GameDataDynamicNameSpace;

namespace WowAHaha.GameDataApi.Http;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum GameDataDynamicNameSpace : sbyte
{
    None = 0,
    US = 1 << 0,
    EU = 1 << 1,
    TW = 1 << 2,
    CN = 1 << 3,
    KR = 1 << 4
}

public static class GameDataDynamicNameSpaceExtensions
{
    public static string ToDynamicName(this GameDataDynamicNameSpace @this)
    {
        return $"dynamic-{@this.ToString().ToLowerInvariant()}";
    }

    public static Uri ToHostUri(this GameDataDynamicNameSpace @this)
    {
        return @this switch
        {
            US => new Uri("https://us.api.blizzard.com/"),
            EU => new Uri("https://eu.api.blizzard.com/"),
            TW => new Uri("https://tw.api.blizzard.com/"),
            CN => new Uri("https://api.battlenet.com.cn/"),
            KR => new Uri("https://kr.api.blizzard.com/"),
            None => throw new ArgumentOutOfRangeException(nameof(@this), @this, null),
            _ => throw new ArgumentOutOfRangeException(nameof(@this), @this, null)
        };
    }

    public static string GetLocale(this GameDataDynamicNameSpace @this)
    {
        return @this switch
        {
            US => "en_US",
            EU => "en_GB",
            TW => "zh_TW",
            CN => "zh_CN",
            KR => "ko_KR",
            None => throw new ArgumentOutOfRangeException(nameof(@this), @this, null),
            _ => throw new ArgumentOutOfRangeException(nameof(@this), @this, null)
        };
    }

    public static string ToCommonName(this GameDataDynamicNameSpace @this)
    {
        return @this switch
        {
            US => "United States",
            EU => "Europe",
            TW => "Taiwan",
            CN => "China",
            KR => "Korea",
            None => nameof(None),
            _ => throw new ArgumentOutOfRangeException(nameof(@this), @this, null)
        };
    }
}