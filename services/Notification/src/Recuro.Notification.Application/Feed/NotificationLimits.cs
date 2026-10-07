namespace Recuro.Notification.Application.Feed;

public static class NotificationLimits
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxIdsPerRead = 200;
    public const int StreamBatchSize = 100;
}

/// <summary>The <c>filter</c> query parameter of the bell and email centre (RCU-NTF-003).</summary>
public static class FeedFilters
{
    public const string All = "all";
    public const string Unread = "unread";

    public static bool IsKnown(string? filter) => filter is null or All or Unread;

    public static bool IsUnread(string? filter) => filter == Unread;
}
