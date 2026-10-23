namespace Jellyfin.Api.Constants;

/// <summary>
/// Names of the rate limiting policies used by the API.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Login, quick connect initiation and password reset attempts, per client address.
    /// </summary>
    public const string Authentication = "Authentication";

    /// <summary>
    /// Quick connect state polling, per client address; clients poll every few seconds while waiting.
    /// </summary>
    public const string QuickConnectPoll = "QuickConnectPoll";
}
