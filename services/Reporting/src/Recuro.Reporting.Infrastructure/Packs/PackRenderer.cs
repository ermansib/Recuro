using System.Globalization;
using System.Text;
using Recuro.BuildingBlocks.Infrastructure.Documents;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Reports;

namespace Recuro.Reporting.Infrastructure.Packs;

/// <summary>
/// RCU-RPT-003 pack files: a one-document PDF (BuildingBlocks' licence-free writer) and an RFC 4180 CSV
/// of the register and the source mix. The register is already masked for the recipient role. Tenant
/// branding is applied by the email Notification sends; the files themselves carry no tenant name.
/// </summary>
internal sealed class PackRenderer : IPackRenderer
{
    public RenderedPack Render(KpiRegisterDto register, string recipientRole)
    {
        ArgumentNullException.ThrowIfNull(register);
        return new RenderedPack(SimplePdf.Render($"Recruitment KPI pack — {register.PeriodLabel}", PdfLines(register, recipientRole)), Csv(register));
    }

    private static IEnumerable<string> PdfLines(KpiRegisterDto register, string recipientRole)
    {
        yield return Invariant($"Period: {register.PeriodLabel} ({register.From[..10]} to {register.To[..10]}, {register.Cadence.ToLowerInvariant()})");
        yield return Invariant($"Prepared for: {recipientRole} · {register.OnTrack} metrics on track · {register.Hires} hires");
        yield return Invariant($"Definitions v{register.DefinitionsVersion} · computed {register.ComputedAt[..16].Replace('T', ' ')} UTC");
        yield return string.Empty;
        yield return "Full metric register (FRD §12)";
        foreach (var kpi in register.Kpis)
        {
            yield return Invariant($"• {kpi.Name}: {kpi.Value} (target {kpi.Target}) — {kpi.Status}");
        }

        if (register.SourceMix.Count > 0)
        {
            yield return string.Empty;
            yield return "Source mix & cost effectiveness";
            foreach (var share in register.SourceMix)
            {
                var cph = share.CostPerHire is { } value ? " · CPH " + ReportFormat.Money(value, register.Currency) : string.Empty;
                yield return Invariant($"• {share.Source}: {share.Hires} hires ({ReportFormat.Percent(share.Percent)}){cph}");
            }
        }

        yield return string.Empty;
        yield return "Definitions";
        foreach (var kpi in register.Kpis)
        {
            yield return Invariant($"{kpi.Name}: {kpi.Definition}");
        }
    }

    private static string Csv(KpiRegisterDto register)
    {
        var csv = new StringBuilder();
        Row(csv, "period", "metric", "value", "actual", "unit", "target", "target_value", "status", "sample");
        foreach (var kpi in register.Kpis)
        {
            Row(csv, register.Period, kpi.Name, kpi.Value, Number(kpi.Actual), kpi.Unit, kpi.Target, Number(kpi.TargetValue), kpi.Status, kpi.Sample.ToString(CultureInfo.InvariantCulture));
        }

        csv.Append("\r\n");
        Row(csv, "period", "source", "hires", "percent", "cost", "cost_per_hire", "currency");
        foreach (var share in register.SourceMix)
        {
            Row(csv, register.Period, share.Source, share.Hires.ToString(CultureInfo.InvariantCulture), Number(share.Percent), Number(share.Cost), Number(share.CostPerHire), register.Currency ?? string.Empty);
        }

        return csv.ToString();
    }

    private static void Row(StringBuilder csv, params string[] cells)
    {
        csv.AppendJoin(',', cells.Select(Escape));
        csv.Append("\r\n");
    }

    /// <summary>Quotes cells and defuses spreadsheet formulas (CSV injection).</summary>
    private static string Escape(string cell)
    {
        var value = cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@' && !decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            ? "'" + cell
            : cell;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
