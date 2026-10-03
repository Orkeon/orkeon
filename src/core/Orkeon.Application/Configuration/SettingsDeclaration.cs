namespace Orkeon.Application.Configuration;

/// <summary>
/// One section of the settings a registration reads, declared by that registration (GAP-40): where
/// it is, which keys it may carry, and — for an options section — how to evaluate it. A shipped host
/// judges every declared section at its start, whether the run uses it or not: each options section
/// is evaluated (the binder's conversions, its <c>Validate</c> rules, the names it holds), then every
/// key under a declared path is checked against the shapes declared there, the readers of one section
/// together, and a key no declaration knows is refused, naming it.
/// <para>
/// Declared through <see cref="SettingsDeclarationExtensions.DeclareSettings{TOptions}"/> for an
/// options section, which also validates it when a host starts (<c>ValidateOnStart</c>), and through
/// <see cref="SettingsDeclarationExtensions.DeclareSettingsShape"/> for a section read raw.
/// </para>
/// </summary>
public sealed class SettingsDeclaration
{
    /// <summary>Declares a section read raw: its keys are checked against <paramref name="shape"/>, nothing is evaluated.</summary>
    /// <param name="path">The section's configuration path, such as <c>Orkeon:Guardian</c>.</param>
    /// <param name="shape">The type whose public properties name the section's keys, recursively.</param>
    public SettingsDeclaration(string path, Type shape)
        : this(path, shape, evaluate: null)
    {
    }

    /// <summary>Declares a section, with what evaluates its options.</summary>
    /// <param name="path">The section's configuration path, such as <c>Orkeon:Guardian</c>.</param>
    /// <param name="shape">The type whose public properties name the section's keys, recursively.</param>
    /// <param name="evaluate">
    /// Creates the section's options from the container — binder, <c>Validate</c> rules — or null for a
    /// section read raw. It throws what the binder or a rule refuses.
    /// </param>
    public SettingsDeclaration(string path, Type shape, Action<IServiceProvider>? evaluate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(shape);
        Path = path;
        Shape = shape;
        Evaluate = evaluate;
    }

    /// <summary>The section's configuration path.</summary>
    public string Path { get; }

    /// <summary>The type whose public properties name the section's keys, recursively.</summary>
    public Type Shape { get; }

    /// <summary>Creates the section's options from the container, or null for a section read raw.</summary>
    public Action<IServiceProvider>? Evaluate { get; }
}
