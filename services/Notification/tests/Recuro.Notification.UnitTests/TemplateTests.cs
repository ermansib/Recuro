using Recuro.Notification.Application.Templates;
using Recuro.Notification.Domain.Templates;

namespace Recuro.Notification.UnitTests;

public class TemplateTests
{
    private static readonly Dictionary<string, string> Values = new() { ["reqId"] = "REQ-2026-0156", ["reason"] = "Budget" };

    [Fact]
    public void Placeholders_are_filled_and_missing_ones_show_a_dash()
    {
        Assert.Equal("MRF REQ-2026-0156 — no —", TemplateText.Fill("MRF {reqId} — no {other}", Values));
    }

    [Fact]
    public void Unclosed_braces_are_left_as_text()
    {
        Assert.Equal("Odd { brace", TemplateText.Fill("Odd { brace", Values));
    }

    [Fact]
    public void Filled_values_are_not_expanded_again()
    {
        var values = new Dictionary<string, string> { ["a"] = "{b}", ["b"] = "x" };

        Assert.Equal("{b}", TemplateText.Fill("{a}", values));
    }

    [Fact]
    public async Task The_rejection_template_quotes_the_reason()
    {
        var template = await new DefaultTemplates().GetAsync("mrf.rejected", CancellationToken.None);

        var email = template!.RenderEmail(Values);

        Assert.Equal("MRF rejected — REQ-2026-0156", email.Subject);
        Assert.Contains("Reason given: Budget", email.Paragraphs);
        Assert.Equal("/mrf", email.Link);
    }

    [Fact]
    public async Task Unknown_template_keys_return_null()
    {
        Assert.Null(await new DefaultTemplates().GetAsync("nope", CancellationToken.None));
    }
}
