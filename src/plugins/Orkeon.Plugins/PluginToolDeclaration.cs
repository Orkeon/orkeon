using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;

namespace Orkeon.Plugins;

/// <summary>
/// Holds a plugin to the tools its manifest declares. A tool the plugin hands over as an instance is
/// checked at registration. A tool registered by type, factory or key is wrapped: the host's own provider
/// builds it, once, with the services the host registers whenever it likes, and the wrapper reads its name
/// then and refuses it when the manifest does not list it. No second construction, no scratch provider.
/// </summary>
internal static class PluginToolDeclaration
{
    /// <summary>
    /// Goes over the <see cref="IBaseTool"/> registrations the plugin added (the descriptors not in
    /// <paramref name="before"/>): checks an instance now, wraps the others.
    /// </summary>
    /// <exception cref="PluginManifestException">A tool given as an instance is not declared.</exception>
    public static void Guard(
        IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> before, PluginManifest manifest, string virtualPath)
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

            services[i] = descriptor.IsKeyedService
                ? new ServiceDescriptor(
                    typeof(IBaseTool),
                    descriptor.ServiceKey,
                    (sp, key) => guard.Build(() => CreateKeyed(descriptor, sp, key)),
                    descriptor.Lifetime)
                : ServiceDescriptor.Describe(
                    typeof(IBaseTool),
                    sp => guard.Build(() => Create(descriptor, sp)),
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
