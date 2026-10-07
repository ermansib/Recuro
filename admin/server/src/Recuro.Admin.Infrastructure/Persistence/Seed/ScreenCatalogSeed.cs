using Recuro.Admin.Domain.Screens;

namespace Recuro.Admin.Infrastructure.Persistence.Seed;

/// <summary>
/// The main portal's screens (prototype ids S-01..S-17) and their configurable fields.
/// Field keys match the property names of the main app's DTOs (frontend/src/domain/types.ts).
/// Locked fields drive a rule or state machine (DOA routing, offer matrix, BGV gate) and cannot be hidden.
/// </summary>
internal static class ScreenCatalogSeed
{
    public static IEnumerable<ScreenDefinition> All()
    {
        var order = 0;
        yield return Screen("dashboard", "S-01", "Dashboard", "Dashboard", "Your hiring at a glance", canDisable: false, ++order);
        yield return Screen("mrf", "S-02", "Requisitions", "New manpower requisition", "Raise approved demand to unlock sourcing", canDisable: false, ++order,
            Field("department", "Department", FieldDataType.Select, required: true, locked: true),
            Field("designation", "Designation", FieldDataType.Text, required: true, locked: true),
            Field("grade", "Grade", FieldDataType.Select, required: true, locked: true),
            Field("location", "Location", FieldDataType.Select, required: true),
            Field("positions", "Number of positions", FieldDataType.Number, required: true, locked: true),
            Field("reportingManager", "Reporting manager", FieldDataType.Text, required: true),
            Field("employmentType", "Employment type", FieldDataType.Select, required: true),
            Field("nature", "Nature of requisition", FieldDataType.Select, required: true),
            Field("replacementReason", "Replacement reason", FieldDataType.LongText),
            Field("joiningDate", "Target joining date", FieldDataType.Date, required: true),
            Field("band", "Salary band", FieldDataType.Select),
            Field("outOfBudget", "Out of budget", FieldDataType.Select, required: true, locked: true),
            Field("oobJustification", "Out-of-budget justification", FieldDataType.LongText),
            Field("qualifications", "Qualifications", FieldDataType.LongText),
            Field("sourcingChannels", "Sourcing channels", FieldDataType.Select));
        yield return Screen("approvals", "S-04", "Requisitions", "Approvals", "Requisitions and offers waiting for you", canDisable: false, ++order,
            Field("comment", "Comment", FieldDataType.LongText));
        yield return Screen("jd", "S-05", "Job descriptions", "Job description builder", "Draft, version and approve job descriptions", canDisable: true, ++order,
            Field("purpose", "Purpose of the role", FieldDataType.LongText, required: true),
            Field("responsibilities", "Key responsibilities", FieldDataType.LongText, required: true),
            Field("reportsTo", "Reports to", FieldDataType.Text),
            Field("teamSize", "Team size", FieldDataType.Text),
            Field("location", "Location", FieldDataType.Text),
            Field("minQualification", "Minimum qualification", FieldDataType.Text, required: true),
            Field("experience", "Experience", FieldDataType.Text, required: true),
            Field("competencies", "Competencies", FieldDataType.LongText),
            Field("assessments", "Assessments", FieldDataType.LongText),
            Field("benchmark", "Benchmark", FieldDataType.Text));
        yield return Screen("pipeline", "S-06", "Candidates", "Candidate pipeline", "Move candidates through each stage", canDisable: false, ++order,
            Field("experienceYears", "Experience (years)", FieldDataType.Number),
            Field("currentCtc", "Current CTC", FieldDataType.Currency),
            Field("expectedCtc", "Expected CTC", FieldDataType.Currency),
            Field("noticeDays", "Notice period (days)", FieldDataType.Number),
            Field("source", "Source", FieldDataType.Select, required: true, locked: true));
        yield return Screen("assessment", "S-09", "Interviews", "Interview assessment", "Schedule interviews and record Annexure B scores", canDisable: false, ++order,
            Field("ratings", "Competency ratings", FieldDataType.Number, required: true, locked: true),
            Field("recommendation", "Recommendation", FieldDataType.Select, required: true, locked: true),
            Field("strengths", "Strengths", FieldDataType.LongText),
            Field("concerns", "Areas of concern", FieldDataType.LongText),
            Field("coiDeclared", "Conflict-of-interest declaration", FieldDataType.Select, required: true, locked: true));
        yield return Screen("bgv", "S-11", "Background verification", "Background verification", "Track checks with empanelled vendors", canDisable: true, ++order,
            Field("vendor", "Vendor", FieldDataType.Select, required: true),
            Field("checks", "Checks", FieldDataType.Select, required: true, locked: true),
            Field("remarks", "Remarks", FieldDataType.LongText));
        yield return Screen("offer", "S-12", "Offers", "Offer management", "Build, approve and send offers", canDisable: false, ++order,
            Field("designation", "Designation", FieldDataType.Text, required: true),
            Field("location", "Location", FieldDataType.Text, required: true),
            Field("reportingManager", "Reporting manager", FieldDataType.Text, required: true),
            Field("joiningDate", "Joining date", FieldDataType.Date, required: true, locked: true),
            Field("probationMonths", "Probation (months)", FieldDataType.Number, required: true),
            Field("fixed", "Fixed pay", FieldDataType.Currency, required: true, locked: true),
            Field("variable", "Variable pay", FieldDataType.Currency),
            Field("benefits", "Benefits", FieldDataType.Currency));
        yield return Screen("onboarding", "S-13", "Onboarding", "Onboarding", "Pre-boarding, onboarding and probation", canDisable: true, ++order);
        yield return Screen("vendors", "S-14", "Vendors", "Vendors and consultants", "Empanelment, agreements and performance", canDisable: true, ++order);
        yield return Screen("reports", "S-15", "Reports", "Reports and KPIs", "Hiring funnel, TAT and cost", canDisable: true, ++order);
        yield return Screen("internalCareers", "S-16", "Employee portal", "Internal careers", "Internal job postings and referrals", canDisable: true, ++order);
        yield return Screen("careers", "S-17", "Careers site", "Careers", "Grow your career with us", canDisable: true, ++order,
            Field("name", "Full name", FieldDataType.Text, required: true, locked: true),
            Field("email", "Email", FieldDataType.Email, required: true, locked: true),
            Field("phone", "Phone", FieldDataType.Phone, required: true),
            Field("resume", "Resume", FieldDataType.File, required: true, locked: true),
            Field("experienceYears", "Total experience (years)", FieldDataType.Number),
            Field("currentCtc", "Current CTC", FieldDataType.Currency),
            Field("expectedCtc", "Expected CTC", FieldDataType.Currency),
            Field("noticeDays", "Notice period (days)", FieldDataType.Number),
            Field("summary", "Cover note", FieldDataType.LongText));
    }

    private static ScreenDefinition Screen(
        string key, string code, string module, string title, string subtitle, bool canDisable, int order, params FieldSpec[] fields) =>
        ScreenDefinition.Create(
            key,
            code,
            module,
            title,
            subtitle,
            canDisable,
            order,
            fields.Select((spec, index) => new FieldDefinition(spec.Key, spec.Label, spec.Type, spec.Required, spec.Locked, (index + 1) * 10)));

    private static FieldSpec Field(string key, string label, FieldDataType type, bool required = false, bool locked = false) =>
        new(key, label, type, required, locked);

    private sealed record FieldSpec(string Key, string Label, FieldDataType Type, bool Required, bool Locked);
}
