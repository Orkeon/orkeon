using Orkeon.Application.Interfaces;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;

namespace Orkeon.ConsoleApp.Tests.Fakes;

public sealed class FakeCrewFactory : ICrewFactory
{
    public List<string> PathsLoaded { get; } = new();
    public Func<string, Crew>? CrewFromFile { get; set; }

    public System.Threading.Tasks.Task<Crew> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default)
        => throw new NotImplementedException();

    public System.Threading.Tasks.Task<Crew> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default)
    {
        PathsLoaded.Add(yamlFilePath);
        var crew = CrewFromFile?.Invoke(yamlFilePath)
            ?? Crew.Create(goal: "g");
        return System.Threading.Tasks.Task.FromResult(crew);
    }

    public System.Threading.Tasks.Task<Crew> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default)
        => throw new NotImplementedException();
}
