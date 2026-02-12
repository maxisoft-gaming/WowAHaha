using System.Diagnostics.CodeAnalysis;

namespace WowAHaha.WowItems;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public enum WowExpansion
{
    Unknown = 0,
    Classic = 1,
    TheBurningCrusade = 2,
    WrathOfTheLichKing = 3,
    Cataclysm = 4,
    MistsOfPandaria = 5,
    WarlordsOfDraenor = 6,
    Legion = 7,
    BattleForAzeroth = 8,
    Shadowlands = 9,
    Dragonflight = 10,
    TheWarWithin = 11,
    Midnight = 12,

    Latest = 12 // point to latest expansion
}