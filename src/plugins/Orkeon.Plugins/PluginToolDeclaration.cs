using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;

namespace Orkeon.Plugins;

/// <summary>
/// Holds a plugin to the tools its manifest declares. A tool the plugin hands over as an instance is
/// checked at registration. A tool registered by type, factory or key is wrapped: the host's own provider
/// builds it, once, with the services the host registers whenever it likes, and the wrapper reads its name
/// then and refuses it when the manifest does not list it. No second construction, no scratch provider.
/// Under <see cref="OrkeonPluginsOptions.ContinueOnError"/> a tool refused there (undeclared, or failing to
/// build) is dropped instead: it resolves to <c>null</c>, which every host consumer of the container's tools
/// skips, and the refusal is recorded once in <see cref="IPluginRegistry.Failures"/>.
/// </summary>
internal static class PluginToolDeclaration
{
    /// <summary>
    /// Goes over the <see cref="IBaseTool"/> registrations the plugin added (the descriptors not in
    /// <paramref name="before"/>): checks an instance now, wraps the others.
    /// </summary>
    /// <param name="services">The plugin's service collection.</param>
    /// <param name="before">The descriptors registered before the plugin ran.</param>
    /// <param name="manifest">The plugin's manifest, listing its declared tools.</param>
    /// <param name="virtualPath">The plugin's virtual path, for error messages.</param>
    /// <param name="failures">Where a tool refused at resolution is recorded and dropped; null to throw (fail fast).</param>
    /// <exception cref="PluginManifestException">A tool given as an instance is not declared.</exception>
    public static void Guard(
        IServiceCollection services,
        IReadOnlyCollection<ServiceDescriptor> before,
        PluginManifest manifest,
        string virtualPath,
        PluginRegistry? failures = null)
    {
        var known = new HashSet<ServiceDescriptor>(before, ReferenceEqualityComparer.Instance);
        var declared = new HashSet<string>(manifest.Tools, StringComparer.Ordinal);
        var guard = new ToolGuard(manifest.Name!, virtualPath, declared);

        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IBaseTool) || known.Contains(descriptor))
                continue;

            var instance = descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance;
            if (instance is not null)
            {
                guard.Check(instance);
                continue;
            }

            var slot = new ToolSlot(guard, failures, virtualPath);
            services[i] = descriptor.IsKeyedService
                ? new ServiceDescriptor(
                    typeof(IBaseTool),
                    descriptor.ServiceKey,
                    (sp, key) => slot.Build(() => CreateKeyed(descriptor, sp, key))!,
                    descriptor.Lifetime)
                : ServiceDescriptor.Describe(
                    typeof(IBaseTool),
                    sp => slot.Build(() => Create(descriptor, sp))!,
                    descriptor.Lifetime);
        }
    }

    private static object Create(ServiceDescriptor descriptor, IServiceProvider sp) =>
        descriptor.ImplementationFactory is { } factory
            ? factory(sp)
            : ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);

    private static object CreateKeyed(ServiceDescriptor descriptor, IServiceProvider sp, object? key) =>
        descriptor.KeyedImplementationFactory is { } factory
            ? factory(sp, key)
            : ActivatorUtilities.CreateInstance(sp, descriptor.KeyedImplementationType!);

    /// <summary>One lazily built plugin tool: refused, it throws (fail fast) or, under ContinueOnError, is dropped and recorded once.</summary>
    private sealed class ToolSlot(ToolGuard guard, PluginRegistry? failures, string virtualPath)
    {
        private int _recorded;

        public object? Build(Func<object> create)
        {
            if (failures is null)
                return guard.Build(create);

            try
            {
                return guard.Build(create);
            }
            catch (PluginLoadException ex)
            {
                if (Interlocked.Exchange(ref _recorded, 1) == 0)
                    failures.AddFailure(new PluginLoadFailure { VirtualPath = virtualPath, Reason = ex.Message });
                return null;
            }
        }
    }

    private sealed class ToolGuard(string plugin, string virtualPath, HashSet<string> declared)
    {
        public object Build(Func<object> create)
        {
            object tool;
            try
            {
                tool = create();
            }
            catch (PluginLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PluginLoadException(
                    $"Plugin '{plugin}' ({virtualPath}) failed to build one of its tools ({ex.GetType().Name}). See inner exception for details.",
                    virtualPath,
                    ex);
            }

            try
            {
                Check(tool);
            }
            catch (PluginManifestException)
            {
                (tool as IDisposable)?.Dispose();
                throw;
            }

            return tool;
        }

        public void Check(object tool)
        {
            string name;
            try
            {
                name = ((IBaseTool)tool).Name;
            }
            catch (Exception ex) when (ex is InvalidCastException or NullReferenceException or InvalidOperationException)
            {
                throw new PluginManifestException(
                    $"Plugin '{plugin}' ({virtualPath}) registers a tool whose name cannot be read ({ex.GetType().Name}). See inner exception for details.",
                    virtualPath,
                    ex);
            }

            if (!declared.Contains(name))
            {
                throw new PluginManifestException(
                    $"Plugin '{plugin}' ({virtualPath}) registers the tool '{name}', which its manifest does not declare under 'tools'.",
                    virtualPath);
            }
        }
    }
}
