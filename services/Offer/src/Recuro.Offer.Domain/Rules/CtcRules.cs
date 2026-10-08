using System.Globalization;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Domain.Rules;

/// <summary>A broken CTC rule (RCU-OFR-001): the rule id, the field it concerns and a readable message.</summary>
public sealed record CtcViolation(string RuleId, string Field, string Message);

/// <summary>One Finance rule on a CTC breakup. Tenants combine rules through configuration, not code.</summary>
public interface ICtcRule
{
    string Id { get; }

    CtcViolation? Check(CtcComponents components);
}

/// <summary>Every component is zero or more and the total is positive.</summary>
public sealed class NonNegativeComponentsRule : ICtcRule
{
    public string Id => "ctc.non-negative";

    public CtcViolation? Check(CtcComponents components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Fixed < 0 || components.Variable < 0 || components.Benefits < 0)
        {
            return new CtcViolation(Id, "components", "CTC components must be zero or more.");
        }

        return components.Total <= 0 ? new CtcViolation(Id, "components", "The total CTC must be more than zero.") : null;
    }
}

/// <summary>A component's share of the total must stay within a bound, e.g. fixed ≥ 50% or variable ≤ 30%.</summary>
public sealed class ComponentShareRule(string id, string component, decimal? minPercent, decimal? maxPercent) : ICtcRule
{
    public const string FixedComponent = "fixed";
    public const string VariableComponent = "variable";
    public const string BenefitsComponent = "benefits";

    public string Id => id;

    public string Component => component;

    public decimal? MinPercent => minPercent;

    public decimal? MaxPercent => maxPercent;

    public CtcViolation? Check(CtcComponents components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var total = components.Fixed + components.Variable + components.Benefits;
        if (total <= 0)
        {
            return null;
        }

        var value = component switch
        {
            FixedComponent => components.Fixed,
            VariableComponent => components.Variable,
            BenefitsComponent => components.Benefits,
            _ => throw new InvalidOperationException($"Unknown CTC component {component}."),
        };
        var share = Math.Round(value * 100 / total, 1, MidpointRounding.AwayFromZero);
        if (minPercent is { } min && share < min)
        {
            return new CtcViolation(id, $"components.{component}", Format($"The {component} component is {share}% of CTC; the minimum is {min}%."));
        }

        if (maxPercent is { } max && share > max)
        {
            return new CtcViolation(id, $"components.{component}", Format($"The {component} component is {share}% of CTC; the maximum is {max}%."));
        }

        return null;
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The rule-set applied to every CTC breakup (RCU-OFR-001). An empty result means compliant.</summary>
public sealed class CtcRuleSet(IReadOnlyList<ICtcRule> rules)
{
    public IReadOnlyList<ICtcRule> Rules => rules;

    /// <summary>
    /// Seed values until a tenant's Config offer matrix carries <c>ctcRules</c>: non-negative components,
    /// fixed at least 50%, variable at most 30%, benefits at most 20% of CTC.
    /// </summary>
    public static CtcRuleSet Default { get; } = new(
    [
        new NonNegativeComponentsRule(),
        new ComponentShareRule("ctc.fixed-min", ComponentShareRule.FixedComponent, 50, null),
        new ComponentShareRule("ctc.variable-max", ComponentShareRule.VariableComponent, null, 30),
        new ComponentShareRule("ctc.benefits-max", ComponentShareRule.BenefitsComponent, null, 20),
    ]);

    public IReadOnlyList<CtcViolation> Validate(CtcComponents components)
    {
        var violations = new List<CtcViolation>();
        foreach (var rule in rules)
        {
            if (rule.Check(components) is { } violation)
            {
                violations.Add(violation);

                // Shares mean nothing when the amounts themselves are invalid.
                if (rule is NonNegativeComponentsRule)
                {
                    break;
                }
            }
        }

        return violations;
    }
}
