using Orkeon.Application.Common;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Engine for rendering templates with parameter substitution.
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Renders a template with the provided parameters.
    /// </summary>
    System.Threading.Tasks.Task<string> RenderAsync(
        string templateContent,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a template with inheritance support.
    /// </summary>
    System.Threading.Tasks.Task<string> RenderWithInheritanceAsync(
        string templateContent,
        string? baseTemplate,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts parameter placeholders from a template.
    /// </summary>
    System.Threading.Tasks.Task<IEnumerable<string>> ExtractParametersAsync(
        string templateContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates template syntax.
    /// </summary>
    System.Threading.Tasks.Task<TemplateValidation> ValidateTemplateAsync(
        string templateContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a custom function for use in templates.
    /// </summary>
    void RegisterFunction(string name, Func<object[], object> func);

    /// <summary>
    /// Registers a custom filter for use in templates.
    /// </summary>
    void RegisterFilter(string name, Func<object, object[], object> filter);
}

/// <summary>
/// Template validation result.
/// </summary>
public class TemplateValidation
{
    /// <summary>
    /// Gets whether the template is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the extracted parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>
    /// Gets syntax warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static TemplateValidation Valid(params string[] parameters)
    {
        return new TemplateValidation
        {
            IsValid = true,
            Parameters = parameters.ToList()
        };
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    public static TemplateValidation Invalid(params string[] errors)
    {
        return new TemplateValidation
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}
