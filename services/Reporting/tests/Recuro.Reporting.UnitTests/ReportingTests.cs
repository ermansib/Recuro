using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.UnitTests;

public class ReportPeriodTests
{
    [Theory]
    [InlineData("2026-08", Cadence.Monthly, "2026-08-01", "2026-09-01", "Aug 2026")]
    [InlineData("2026-Q3", Cadence.Quarterly, "2026-07-01", "2026-10-01", "Q3 2026")]
    [InlineData("2026-12", Cadence.Monthly, "2026-12-01", "2027-01-01", "Dec 2026")]
    public void Parses_months_and_quarters(string key, Cadence cadence, string first, string end, string label)
    {
        Assert.True(ReportPeriod.TryParse(key, out var period));
        Assert.Equal(cadence, period.Cadence);
        Assert.Equal(DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture), period.FirstDay);
        Assert.Equal(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), period.EndExclusive);
        Assert.Equal(label, period.Label);
        Assert.Equal(key, period.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-Q5")]
    [InlineData("26-08")]
    [InlineData("2026/08")]
    public void Rejects_bad_periods(string? key) => Assert.False(ReportPeriod.TryParse(key, out _));

    [Fact]
    public void Trailing_periods_cross_year_boundaries()
    {
        var keys = ReportPeriod.Month(2026, 2).Trailing(4).Select(p => p.Key);
        Assert.Equal(["2025-11", "2025-12", "2026-01", "2026-02"], keys);
        Assert.Equal("2025-Q4", ReportPeriod.Quarter(2026, 1).Previous().Key);
    }

    [Fact]
    public void Window_starts_at_local_midnight_in_the_tenant_zone()
    {
        var zone = TimeZones.Find("Asia/Kolkata")!;
        var window = ReportPeriod.Month(2026, 8).InZone(zone);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 18, 30, 0, TimeSpan.Zero), window.From);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 18, 30, 0, TimeSpan.Zero), window.To);
    }
}

public class ProjectionFoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Application_folds_are_order_independent_and_idempotent()
    {
        var inOrder = ApplicationFact.Start("APP-1");
        inOrder.Identify("REQ-1", "CAN-1", "referral");
        inOrder.Created(T0);
        inOrder.Reached(FunnelStage.Screened, T0.AddDays(1));
        inOrder.Reached(FunnelStage.Interviewed, T0.AddDays(3));
        inOrder.OfferAccepted(T0.AddDays(10), new DateOnly(2026, 9, 1));

        var shuffled = ApplicationFact.Start("APP-1");
        shuffled.OfferAccepted(T0.AddDays(10), new DateOnly(2026, 9, 1));
        shuffled.Reached(FunnelStage.Interviewed, T0.AddDays(3));
        shuffled.Identify("REQ-1", "CAN-1", "referral");
        shuffled.Reached(FunnelStage.Screened, T0.AddDays(1));
        shuffled.Created(T0);
        shuffled.Created(T0); // redelivered
        shuffled.OfferAccepted(T0.AddDays(10), new DateOnly(2026, 9, 1));

        Assert.Equal(inOrder.CreatedAt, shuffled.CreatedAt);
        Assert.Equal(inOrder.ScreenedAt, shuffled.ScreenedAt);
        Assert.Equal(inOrder.OfferAcceptedAt, shuffled.OfferAcceptedAt);
        Assert.Equal(FunnelStage.Offered, shuffled.FurthestStage);
        Assert.True(shuffled.IsHire);
    }

    [Fact]
    public void Withdrawn_accepted_offer_is_no_longer_a_hire()
    {
        var fact = ApplicationFact.Start("APP-2");
        fact.OfferAccepted(T0, null);
        fact.OfferWithdrawn(T0.AddDays(2));
        Assert.False(fact.IsHire);
    }

    [Fact]
    public void Resubmitted_mrf_measures_from_the_last_submission_before_approval()
    {
        var req = RequisitionFact.Start("REQ-1");
        req.Submitted("M1", "in", T0);
        req.Submitted("M1", "in", T0.AddDays(5));
        req.Approved(T0.AddDays(6));
        req.Submitted("M1", "in", T0.AddDays(9)); // after approval: ignored
        Assert.Equal(T0.AddDays(5), req.SubmittedAt);
    }

    [Fact]
    public void Feedback_overdue_before_submission_counts_as_late()
    {
        var fact = FeedbackFact.Start("INT-1", "u-panel", "APP-1");
        fact.Overdue(T0);
        fact.Submitted(T0.AddHours(5), withinSla: true);
        Assert.False(fact.OnTime);
        Assert.Equal(T0.AddHours(5), fact.CountsAt);
    }

    [Fact]
    public void Funnel_counts_how_far_each_application_got()
    {
        var a = ApplicationFact.Start("A");
        a.Created(T0);
        a.Reached(FunnelStage.Bgv, T0.AddDays(5));
        var b = ApplicationFact.Start("B");
        b.Created(T0);
        b.Reached(FunnelStage.Screened, T0.AddDays(1));

        var steps = Funnel.Count([a, b], new ReportWindow(T0.AddDays(-1), T0.AddDays(30)));

        Assert.Equal([2, 2, 1, 1, 0, 0], steps.Select(s => s.Count));
        Assert.Equal(50m, steps[2].ConversionPercent);
        Assert.Null(steps[0].ConversionPercent);
    }
}

public class KpiFormulaTests
{
    private static readonly ReportPeriod August = ReportPeriod.Month(2026, 8);
    private static readonly ReportWindow Window = August.InZone(TimeZoneInfo.Utc);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TatTargets Targets = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["E"] = 20, ["M1"] = 35 },
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["mrf-approval"] = 2, ["overall-managerial"] = 35 },
        "tat:v1;doa:v1");

    private static MetricDefinition Definition(string key) => DefaultMetricDefinitions.All.Single(d => d.Key == key);

    private static (ApplicationFact App, RequisitionFact Req) Hire(string id, string source, string grade, int approvedDaysBefore, int appliedDaysBefore, DateTimeOffset acceptedAt, bool joined)
    {
        var req = RequisitionFact.Start("REQ-" + id);
        req.Submitted(grade, "in", acceptedAt.AddDays(-approvedDaysBefore - 1));
        req.Approved(acceptedAt.AddDays(-approvedDaysBefore));
        var app = ApplicationFact.Start("APP-" + id);
        app.Identify(req.ReqId, "CAN-" + id, source);
        app.Created(acceptedAt.AddDays(-appliedDaysBefore));
        app.OfferAccepted(acceptedAt, DateOnly.FromDateTime(acceptedAt.AddDays(14).UtcDateTime));
        if (joined)
        {
            app.Reached(FunnelStage.Joined, acceptedAt.AddDays(14));
        }

        return (app, req);
    }

    private static KpiInputs Inputs(IReadOnlyList<(ApplicationFact App, RequisitionFact Req)> hires, IReadOnlyList<FeedbackFact>? feedback = null, IReadOnlyList<RecruitmentCost>? costs = null) =>
        new(
            August,
            Window,
            Now,
            hires.Select(h => h.App).ToList(),
            hires.ToDictionary(h => h.Req.ReqId, h => h.Req, StringComparer.Ordinal),
            feedback ?? [],
            costs ?? [],
            Targets);

    private static readonly DateTimeOffset Mid = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Time_to_fill_and_hire_average_hires_and_take_targets_from_Config()
    {
        var inputs = Inputs([Hire("1", "referral", "M1", 20, 10, Mid, true), Hire("2", "portal", "E", 30, 20, Mid, false)]);

        var fill = KpiFormulas.Measure(inputs, Definition(MetricKeys.TimeToFill));
        Assert.Equal(25m, fill.Actual);
        Assert.Equal(2, fill.Sample);
        Assert.Equal(TatTargets.ToCalendarDays(27.5m), fill.Target); // average of M1 35 and E 20 working days
        Assert.Equal(KpiStatus.OnTrack, KpiFormulas.Evaluate(Definition(MetricKeys.TimeToFill), fill));

        var hire = KpiFormulas.Measure(inputs, Definition(MetricKeys.TimeToHire));
        Assert.Equal(15m, hire.Actual);
        Assert.Equal(TatTargets.ToCalendarDays(27.5m * 0.8m), hire.Target);
    }

    [Fact]
    public void Hires_outside_the_period_do_not_count()
    {
        var inputs = Inputs([Hire("1", "referral", "M1", 20, 10, Mid.AddMonths(1), true)]);
        Assert.Equal(KpiStatus.NoData, KpiFormulas.Evaluate(Definition(MetricKeys.TimeToFill), KpiFormulas.Measure(inputs, Definition(MetricKeys.TimeToFill))));
    }

    [Fact]
    public void Offer_to_join_counts_offers_whose_joining_date_has_passed()
    {
        var joined = Hire("1", "referral", "M1", 20, 10, Mid, true);
        var dropped = Hire("2", "portal", "M1", 20, 10, Mid, false);
        var measurement = KpiFormulas.Measure(Inputs([joined, dropped]), Definition(MetricKeys.OfferToJoin));
        Assert.Equal(50m, measurement.Actual);
        Assert.Equal(KpiStatus.OffTrack, KpiFormulas.Evaluate(Definition(MetricKeys.OfferToJoin), measurement));
    }

    [Fact]
    public void Feedback_tat_is_the_on_time_share_including_overdue_unsubmitted()
    {
        var onTime = FeedbackFact.Start("INT-1", "a", "APP-1");
        onTime.Submitted(Mid, withinSla: true);
        var late = FeedbackFact.Start("INT-1", "b", "APP-1");
        late.Submitted(Mid, withinSla: false);
        var missing = FeedbackFact.Start("INT-2", "c", "APP-2");
        missing.Overdue(Mid);
        var alsoOnTime = FeedbackFact.Start("INT-3", "d", "APP-3");
        alsoOnTime.Submitted(Mid, withinSla: true);

        var measurement = KpiFormulas.Measure(Inputs([], [onTime, late, missing, alsoOnTime]), Definition(MetricKeys.FeedbackTat));

        Assert.Equal(50m, measurement.Actual);
        Assert.Equal(4, measurement.Sample);
    }

    [Fact]
    public void Mrf_tat_uses_the_tat_matrix_stage()
    {
        var measurement = KpiFormulas.Measure(Inputs([Hire("1", "referral", "M1", 20, 10, Mid.AddDays(25), true)]), Definition(MetricKeys.MrfTat));
        Assert.Equal(1m, measurement.Actual);
        Assert.Equal(TatTargets.ToCalendarDays(2m), measurement.Target);
    }

    [Fact]
    public void Cost_per_hire_and_source_mix_split_spend_by_channel()
    {
        var hires = new[] { Hire("1", "Referral", "M1", 20, 10, Mid, true), Hire("2", "referral", "M1", 20, 10, Mid, true), Hire("3", "consultant", "M1", 20, 10, Mid, true) };
        var costs = new[]
        {
            RecruitmentCost.Record(new DateOnly(2026, 8, 1), "referral", 20_000m, "INR", null, "K. Iyer", Now),
            RecruitmentCost.Record(new DateOnly(2026, 8, 1), "consultant", 130_000m, "INR", null, "K. Iyer", Now),
            RecruitmentCost.Record(new DateOnly(2026, 7, 1), "consultant", 999_999m, "INR", null, "K. Iyer", Now),
        };
        var inputs = Inputs(hires, costs: costs);

        var cph = KpiFormulas.Measure(inputs, Definition(MetricKeys.CostPerHire));
        Assert.Equal(50_000m, cph.Actual);
        Assert.Equal("INR", cph.Currency);
        Assert.Equal(KpiStatus.Review, KpiFormulas.Evaluate(Definition(MetricKeys.CostPerHire), cph));

        var mix = KpiFormulas.Measure(inputs, Definition(MetricKeys.SourceMix)).Mix!;
        Assert.Equal("referral", mix[0].Source);
        Assert.Equal(2, mix[0].Hires);
        Assert.Equal(66.7m, mix[0].Percent);
        Assert.Equal(10_000m, mix[0].CostPerHire);
        Assert.Equal(130_000m, mix[1].CostPerHire);
    }

    [Fact]
    public void Metrics_waiting_for_a_feed_report_no_data()
    {
        var inputs = Inputs([Hire("1", "referral", "M1", 20, 10, Mid, true)]);
        foreach (var key in new[] { MetricKeys.QualityOfHire, MetricKeys.EarlyAttrition })
        {
            Assert.Equal(KpiStatus.NoData, KpiFormulas.Evaluate(Definition(key), KpiFormulas.Measure(inputs, Definition(key))));
        }
    }

    [Theory]
    [InlineData(TargetDirection.AtLeast, 90, 92, KpiStatus.OnTrack)]
    [InlineData(TargetDirection.AtLeast, 90, 86, KpiStatus.Near)]
    [InlineData(TargetDirection.AtLeast, 90, 70, KpiStatus.OffTrack)]
    [InlineData(TargetDirection.AtMost, 10, 8, KpiStatus.OnTrack)]
    [InlineData(TargetDirection.AtMost, 10, 10.5, KpiStatus.Near)]
    [InlineData(TargetDirection.AtMost, 10, 14, KpiStatus.OffTrack)]
    public void Status_compares_to_target_with_a_near_band(TargetDirection direction, double target, double actual, KpiStatus expected)
    {
        var definition = new MetricDefinition("x", "X", "x", MetricUnit.Percent, direction, "t", (decimal)target, NearPercent: 10m);
        Assert.Equal(expected, KpiFormulas.Evaluate(definition, new KpiMeasurement((decimal)actual, 5, (decimal)target)));
    }
}

public class ReportPresentationTests
{
    [Fact]
    public void Formats_like_the_dashboard_mock()
    {
        Assert.Equal("87%", ReportFormat.Percent(87m));
        Assert.Equal("4.2%", ReportFormat.Percent(4.2m));
        Assert.Equal("21 d", ReportFormat.Days(21.3m));
        Assert.Equal("1.4 d", ReportFormat.Days(1.4m));
        Assert.Equal("₹42K", ReportFormat.Money(42_000m, "INR"));
        Assert.Equal("$1.2M", ReportFormat.Money(1_200_000m, "USD"));
        Assert.Equal(("✓", "green"), ReportFormat.Status(KpiStatus.OnTrack));
    }

    private static KpiRegisterDto Register() => new(
        "2026-08",
        "Aug 2026",
        "Monthly",
        "2026-08-01T00:00:00.0000000Z",
        "2026-09-01T00:00:00.0000000Z",
        0,
        null,
        "1 / 2",
        [
            new KpiDto(MetricKeys.CostPerHire, "Cost per Hire", "Quarterly budget", "₹42K", "review", "slate", [], "d", "money", 42_000m, null, 3, "INR"),
            new KpiDto(MetricKeys.OfferToJoin, "Offer-to-Join Ratio", "Target ≥ 85%", "87%", "✓", "green", [], "d", "percent", 87m, 85m, 30, null),
        ],
        3,
        "INR",
        [new MixShareDto("referral", 3, 100m, 126_000m, 42_000m)],
        10,
        "2026-09-01T00:00:00.0000000Z",
        null);

    [Fact]
    public void Hr_ta_team_view_hides_channel_spend_but_keeps_the_register()
    {
        var hidden = ReportMasking.HiddenFields([ReportMasking.LocalFallback("hrta")]);
        var masked = ReportMasking.Apply(Register(), hidden);
        Assert.Equal(2, masked.Kpis.Count);
        Assert.Null(masked.SourceMix[0].Cost);
        Assert.Null(masked.SourceMix[0].CostPerHire);
    }

    [Fact]
    public void No_map_fails_closed_and_any_open_role_wins()
    {
        var closed = ReportMasking.Apply(Register(), ReportMasking.HiddenFields([null]));
        Assert.DoesNotContain(closed.Kpis, k => k.Key == MetricKeys.CostPerHire);

        var open = ReportMasking.HiddenFields([ReportMasking.LocalFallback("hrta"), ReportMasking.LocalFallback("hrhead")]);
        Assert.Empty(open);
    }

    [Fact]
    public void Snapshot_hash_is_stable_for_the_same_register()
    {
        var a = Register();
        var b = Register() with { Snapshot = new SnapshotRefDto(Guid.NewGuid(), 2, "x", "y"), ComputedAt = "2026-09-02T00:00:00.0000000Z" };
        Assert.Equal(SnapshotPayload.Hash(a), SnapshotPayload.Hash(b));
        Assert.Equal(64, SnapshotPayload.Hash(a).Length);
        Assert.NotEqual(SnapshotPayload.Hash(a), SnapshotPayload.Hash(a with { Hires = 4 }));
    }
}
