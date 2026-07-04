using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Adapters;

public static class LanguageAdapterFactory
{
    private static readonly ConcurrentDictionary<string, ILanguageAdapter> ByExtension =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<ILanguageAdapter> AllAdapters = [];

    private static readonly object RegistryLock = new();

    static LanguageAdapterFactory()
    {
        Register(new TypeScriptAdapter());
        Register(new PythonAdapter());
        Register(new CSharpAdapter());
        Register(new GoAdapter());
        Register(new RustAdapter());
    }

    public static void Register(ILanguageAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (RegistryLock)
        {
            if (!AllAdapters.Any(a => a.LanguageName == adapter.LanguageName))
            {
                AllAdapters.Add(adapter);
            }
            foreach (var ext in adapter.FileExtensions)
            {
                ByExtension[ext] = adapter;
            }
        }
    }

    public static ILanguageAdapter? GetByExtension(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension)) return null;
        return ByExtension.TryGetValue(fileExtension, out var adapter) ? adapter : null;
    }

    public static ILanguageAdapter GetByExtensionOrThrow(string fileExtension)
    {
        var adapter = GetByExtension(fileExtension);
        if (adapter is null)
            throw new NotSupportedException($"No language adapter registered for extension '{fileExtension}'.");
        return adapter;
    }

    public static IReadOnlyList<ILanguageAdapter> GetAll()
    {
        lock (RegistryLock)
        {
            return AllAdapters.ToList();
        }
    }
}
