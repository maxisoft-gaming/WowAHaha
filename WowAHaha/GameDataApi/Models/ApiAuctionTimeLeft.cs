namespace WowAHaha.GameDataApi.Models;

public enum ApiAuctionTimeLeft : sbyte
{
    Unknown = 0,
    VeryShort = 1 << 0,
    Short = 1 << 1,
    Medium = 1 << 3,
    Long = 1 << 5,
    VeryLong = 1 << 6
}