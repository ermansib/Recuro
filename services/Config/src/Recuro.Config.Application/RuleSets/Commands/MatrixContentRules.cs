using System.Text.Json;
using FluentValidation;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Commands;

/// <summary>Shared validation for a proposed matrix: shape first, then the matrix's business rules.</summary>
internal static class MatrixContentRules
{
    public static void ValidContent<T>(this AbstractValidator<T> validator, Func<T, MatrixType> type, Func<T, JsonElement> content)
    {
        validator.RuleFor(c => c).Custom((command, context) =>
        {
            var matrixType = type(command);
            var element = content(command);
            if (element.ValueKind != JsonValueKind.Undefined && element.GetRawText().Length > ConfigLimits.ContentBytes)
            {
                context.AddFailure("content", $"content must be at most {ConfigLimits.ContentBytes / 1024} KB.");
                return;
            }

            if (!MatrixJson.TryParse(matrixType, element, out var matrix, out var error))
            {
                context.AddFailure("content", error!);
                return;
            }

            foreach (var failure in MatrixValidators.Validate(matrixType, matrix!).Errors)
            {
                context.AddFailure("content." + CamelPath(failure.PropertyName), failure.ErrorMessage);
            }
        });
    }

    // "Routes[0].OverallTat.MinDays" → "routes[0].overallTat.minDays", like the JSON the client sent.
    private static string CamelPath(string path) =>
        string.Join('.', path.Split('.').Select(s => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..]));
}
