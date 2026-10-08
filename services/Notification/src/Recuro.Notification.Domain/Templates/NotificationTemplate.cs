using System.Text;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Domain.Templates;

/// <summary>
/// Text for one matrix rule in one language. Placeholders are <c>{name}</c>, filled from the event
/// payload's top-level fields plus <c>subjectId</c> and <c>actorName</c>. A tenant override (RCU-CFG-004)
/// is a template with the same key and its own <see cref="Version"/>.
/// </summary>
public sealed record NotificationTemplate(
    string Key,
    string Version,
    string Icon,
    string Title,
    string Body,
    string? Link,
    string EmailTag,
    string EmailSubject,
    IReadOnlyList<string> EmailParagraphs,
    string? EmailCta)
{
    public RenderedNotification RenderNotification(IReadOnlyDictionary<string, string> values) =>
        new(Icon, TemplateText.Fill(Title, values), TemplateText.Fill(Body, values), Link);

    public RenderedEmail RenderEmail(IReadOnlyDictionary<string, string> values) =>
        new(EmailTag, TemplateText.Fill(EmailSubject, values), EmailParagraphs.Select(p => TemplateText.Fill(p, values)).ToList(), EmailCta, Link);
}

/// <summary>Plain-text placeholder filling. Output is text, never HTML: the frontend renders it safely.</summary>
public static class TemplateText
{
    /// <summary>Shown for a placeholder the event did not supply.</summary>
    public const string Missing = "—";

    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        var output = new StringBuilder(template.Length);
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                output.Append(template, index, template.Length - index);
                break;
            }

            output.Append(template, index, open - index);
            var name = template[(open + 1)..close];
            output.Append(values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : Missing);
            index = close + 1;
        }

        return output.ToString();
    }
}
