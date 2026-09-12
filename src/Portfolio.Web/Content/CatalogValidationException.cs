namespace Portfolio.Web.Content;

/// <summary>
/// Reports all catalog validation errors together so invalid content blocks startup
/// </summary>
/// <param name="catalogName">Identifies the invalid catalog.</param>
/// <param name="errors">Contains the validation failures.</param>
public sealed class CatalogValidationException(string catalogName, IReadOnlyList<string> errors)
    : InvalidOperationException($"{catalogName} is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}")
{
    /// <summary>Gets the individual validation failures for diagnostics and tests.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}
