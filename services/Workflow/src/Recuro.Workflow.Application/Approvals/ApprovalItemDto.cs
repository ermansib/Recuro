using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Approvals;

/// <summary>The frontend's <c>ApprovalItem</c> (frontend/src/domain/types.ts), field for field.</summary>
public sealed record ApprovalItemDto(
    string Id,
    string AssigneeRole,
    string Kind,
    string Tone,
    string Title,
    ChipDto Chip,
    string Meta,
    string Route,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SensitiveDto? Sensitive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EntityRefDto? Entity,
    IReadOnlyList<ActionDto> Actions,
    string CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsNew,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DecisionDto? Decision);

public sealed record ChipDto(string Text, string Tone);

public sealed record SensitiveDto(string Label, string Value);

public sealed record EntityRefDto(string Type, string Id);

public sealed record ActionDto(
    string Id,
    string Label,
    string Style,
    string Effect,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultText);

public sealed record DecisionDto(
    string Text,
    string By,
    string At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

/// <summary>Builds inbox items. Server-side masking and chips live here, not in the browser.</summary>
public static class ApprovalItemMapper
{
    /// <summary>Roles that may see CTC and band values (FRD §3.2 <c>candidate.viewSensitive</c>). Others get a mask.</summary>
    public static readonly IReadOnlySet<string> SensitiveViewers = new HashSet<string>(StringComparer.Ordinal) { "hrta", "hrhead" };

    public const string Masked = "••••";

    /// <summary>Items created this recently, still open, carry the frontend's "new" tag.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromHours(24);

    public static ApprovalItemDto From(WorkflowInstance instance, ApprovalTask task, IReadOnlyCollection<string> viewerRoles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(viewerRoles);
        var p = instance.Presentation;
        var canSeeSensitive = viewerRoles.Any(SensitiveViewers.Contains);
        return new ApprovalItemDto(
            task.Id.ToString(),
            task.AssigneeRole,
            p.Kind,
            p.Tone,
            p.Title,
            ChipFor(task, p, now),
            p.Meta,
            p.Route,
            p.Sensitive is null ? null : new SensitiveDto(p.Sensitive.Label, canSeeSensitive ? p.Sensitive.Value : Masked),
            new EntityRefDto(instance.SubjectType, instance.SubjectId),
            p.Actions.Select(a => new ActionDto(a.Id, a.Label, a.Style, EffectName(a.Effect), a.ResultText)).ToList(),
            Iso(task.CreatedAt),
            task.IsOpen && now - task.CreatedAt < NewFor ? true : null,
            task.Decision is { } d ? new DecisionDto(d.Text, d.ByName, Iso(d.At), d.Reason) : null);
    }

    public static string EffectName(ActionEffect effect) => effect switch
    {
        ActionEffect.Resolve => "resolve",
        ActionEffect.Reject => "reject",
        ActionEffect.Query => "query",
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };

    public static ActionEffect? ParseEffect(string? effect) => effect switch
    {
        "resolve" => ActionEffect.Resolve,
        "reject" => ActionEffect.Reject,
        "query" => ActionEffect.Query,
        _ => null,
    };

    private static ChipDto ChipFor(ApprovalTask task, Presentation p, DateTimeOffset now)
    {
        if (task.Status == ApprovalTaskStatus.Completed)
        {
            return new ChipDto("✓ Decided", "green");
        }

        if (task.Status == ApprovalTaskStatus.Cancelled)
        {
            return new ChipDto("Withdrawn", "slate");
        }

        if (task.PausedAt is not null)
        {
            return new ChipDto("⏸ Query open · SLA paused", "slate");
        }

        if (task.EscalationLevel > 0)
        {
            return new ChipDto(string.Create(CultureInfo.InvariantCulture, $"⚑ Escalated L{task.EscalationLevel}"), "red");
        }

        if (task.DueAt is { } due && task.SlaWorkingDays is { } sla)
        {
            return due <= now
                ? new ChipDto(string.Create(CultureInfo.InvariantCulture, $"⏱ Overdue · {sla}d SLA"), "red")
                : new ChipDto(string.Create(CultureInfo.InvariantCulture, $"⏱ SLA {sla}d"), "amber");
        }

        return p.Chip is null ? new ChipDto(p.Kind, "navy") : new ChipDto(p.Chip.Text, p.Chip.Tone);
    }

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
