using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Notification.Api.Endpoints;

internal static class NotificationPolicies
{
    /// <summary>Every signed-in persona has a bell and an email centre (they only ever see their own).</summary>
    public const string Inbox = "notification.inbox";

    /// <summary>The delivery log is for HR staff and auditors (RCU-NTF-004).</summary>
    public const string DeliveryLog = "notification.delivery-log";

    /// <summary>Bounce reports come from the mail provider's adapter, a service account.</summary>
    public const string Bounces = "notification.bounces";

    public static IServiceCollection AddNotificationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Inbox, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.MdCeo, RecuroRoles.Employee, RecuroRoles.Candidate)
            .AddRolePolicy(DeliveryLog, RecuroRoles.HrStaff)
            .AddRolePolicy(Bounces, RecuroRoles.Service);
        return services;
    }
}
