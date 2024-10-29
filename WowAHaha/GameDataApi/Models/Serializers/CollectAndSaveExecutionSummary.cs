using WowAHaha.GameDataApi.Http;

namespace WowAHaha.GameDataApi.Models.Serializers;

public class CollectAndSaveCommoditiesExecutionSummary
{
    public DateTimeOffset DataTimestamp { get; set; }
    public Guid Hash { get; set; }
    public GameDataDynamicNameSpace? NameSpace { get; set; }
    public long Count { get; set; }
    public long SkippedCount { get; set; }

    public int CodeVersion { get; set; } = 1;

    public DateTimeOffset ExecutionTimestamp { get; set; } = GetDefaultExecutionTimestamp();

    private static DateTimeOffset GetDefaultExecutionTimestamp()
    {
        var res = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds((res >> 1) << 1);
    }
}