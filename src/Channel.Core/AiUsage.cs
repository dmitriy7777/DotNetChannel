namespace Channel.Core;

public record AiUsageReport(string? ResponseId, string Status, long? InputTokens, long? OutputTokens, long? TotalTokens,
    long? CachedInputTokens = null, long? ReasoningTokens = null, long? RateLimitRemainingTokens = null, string? RateLimitReset = null);
public record AiUsageEntry(string Id, long PostId, int Revision, string Model, DateTimeOffset CreatedUtc, AiUsageReport Report);
public record PostTokenUsage(int Attempts, int RecordedAttempts, long InputTokens, long OutputTokens, long TotalTokens, long CachedInputTokens, long ReasoningTokens);
public record TokenUsageOverview(IReadOnlyList<AiUsageEntry> Entries, long TodayTokens, long AllTimeTokens, int TodayUnrecordedAttempts, int AllTimeUnrecordedAttempts = 0);
