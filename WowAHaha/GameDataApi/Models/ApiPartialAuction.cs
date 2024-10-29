namespace WowAHaha.GameDataApi.Models;

public readonly record struct ApiPartialAuction(
    ApiItemIdentifier ItemId,
    long Quantity,
    long UnitPrice,
    ApiAuctionTimeLeft TimeLeft);