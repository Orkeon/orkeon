using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Declares the settings a registration reads (GAP-40): the section an options type is bound from, or
/// the shape of a section read raw. A shipped host evaluates every declared options section at its
/// start, then refuses a key no declaration knows; a C# host that starts (<c>StartAsync</c>) validates
/// the same options through <see cref="IStartupValidator"/>.
/// </summary>
public static class SettingsDeclarationExtensions
{
    /// <summary>
    /// Declares <paramref name="sectionPath"/> as the section <paramref name="builder"/>'s options are
    /// bound from, and validates them when a host starts (<c>ValidateOnStart</c>): a value the binder
    /// cannot convert, or that a <c>Validate</c> rule of the builder refuses, fails the start instead of
    /// the first run that reads it. Binds nothing — the caller's registration does. Declaring a section
    /// twice for one options type declares it once.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The options builder, already bound to the section.</param>
    /// <param name="sectionPath">The section's configuration path, such as <c>Orkeon:Guardian</c>.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static OptionsBuilder<TOptions> DeclareSettings<TOptions>(this OptionsBuilder<TOptions> builder, string sectionPath)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);

        var name = builder.Name;
        if (!IsDeclared(builder.Services, sectionPath, typeof(TOptions)))
        {
            builder.Services.AddSingleton(new SettingsDeclaration(
                sectionPath,
                typeof(TOptions),
                services => _ = services.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name)));
        }

        return builder.ValidateOnStart();
    }

    /// <summary>
    /// Adds a rule <paramref name="builder"/>'s options must keep, worded by the rule itself: each
    /// sentence <paramref name="problems"/> returns is a failure — naming the key, the value and what is
    /// accepted —, and no sentence is a pass. Run when the options are created, so at a host's start for
    /// a declared section (<see cref="DeclareSettings{TOptions}"/>); the container is there for the rules
    /// that check a name against what the host registered.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <param name="problems">What is wrong with the options, given the container; empty when nothing is.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static OptionsBuilder<TOptions> ValidateSettings<TOptions>(
        this OptionsBuilder<TOptions> builder,
        Func<TOptions, IServiceProvider, IEnumerable<string>> problems)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(problems);

        var name = builder.Name;
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(services => new SettingsRule<TOptions>(name, problems, services));
        return builder;
    }

    /// <summary>
    /// <see cref="ValidateSettings{TOptions}(OptionsBuilder{TOptions}, Func{TOptions, IServiceProvider, IEnumerable{string}})"/>
    /// for a rule that needs only the options: <paramref name="problem"/> returns the failure, or null.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <param name="problem">What is wrong with the options, or null when nothing is.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static OptionsBuilder<TOptions> ValidateSettings<TOptions>(
        this OptionsBuilder<TOptions> builder,
        Func<TOptions, string?> problem)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(problem);
        return builder.ValidateSettings((options, _) => problem(options) is { } failure ? [failure] : []);
    }

    /// <summary>
    /// Declares the shape of a section a registration reads raw — without an options type —, so a key
    /// it does not carry is refused at the host's start like a key of an options section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionPath">The section's configuration path, such as <c>RaggableTree</c>.</param>
    /// <param name="shape">The type whose public properties name the section's keys, recursively.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection DeclareSettingsShape(this IServiceCollection services, string sectionPath, Type shape)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentNullException.ThrowIfNull(shape);

        if (!IsDeclared(services, sectionPath, shape))
            services.AddSingleton(new SettingsDeclaration(sectionPath, shape));
        return services;
    }

    /// <summary>Whether <paramref name="sectionPath"/> is already declared with <paramref name="shape"/>.</summary>
    public static bool IsDeclared(this IServiceCollection services, string sectionPath, Type shape)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor =>
            descriptor.ServiceType == typeof(SettingsDeclaration)
            && !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is SettingsDeclaration declared
            && declared.Shape == shape
            && string.Equals(declared.Path, sectionPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One <see cref="ValidateSettings{TOptions}(OptionsBuilder{TOptions}, Func{TOptions, IServiceProvider, IEnumerable{string}})"/> rule.</summary>
    private sealed class SettingsRule<TOptions>(
        string? optionsName,
        Func<TOptions, IServiceProvider, IEnumerable<string>> problems,
        IServiceProvider services) : IValidateOptions<TOptions>
        where TOptions : class
    {
        public ValidateOptionsResult Validate(string? name, TOptions options)
        {
            if (optionsName is not null && !string.Equals(optionsName, name, StringComparison.Ordinal))
                return ValidateOptionsResult.Skip;

            var failures = problems(options, services).ToList();
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
