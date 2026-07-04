using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public class OrkeonDiagnosticsTestsFixture
{
    public static string GetServiceName() => OrkeonDiagnostics.ServiceName;
    public static string GetServiceVersion() => OrkeonDiagnostics.ServiceVersion;
    public static string[] GetAllSourceNames() => OrkeonDiagnostics.AllSourceNames;

    public static (string Name, string? Version) GetCrewSource()
        => (OrkeonDiagnostics.CrewSource.Name, OrkeonDiagnostics.CrewSource.Version);

    public static (string Name, string? Version) GetAgentSource()
        => (OrkeonDiagnostics.AgentSource.Name, OrkeonDiagnostics.AgentSource.Version);

    public static (string Name, string? Version) GetTaskSource()
        => (OrkeonDiagnostics.TaskSource.Name, OrkeonDiagnostics.TaskSource.Version);

    public static (string Name, string? Version) GetLlmSource()
        => (OrkeonDiagnostics.LlmSource.Name, OrkeonDiagnostics.LlmSource.Version);

    public static (string Name, string? Version) GetToolSource()
        => (OrkeonDiagnostics.ToolSource.Name, OrkeonDiagnostics.ToolSource.Version);

    public static (string Name, string? Version) GetMemorySource()
        => (OrkeonDiagnostics.MemorySource.Name, OrkeonDiagnostics.MemorySource.Version);

    public static string? GetTagValue(string fieldName)
    {
        var field = typeof(OrkeonDiagnosticTags).GetField(fieldName);
        return field?.GetValue(null) as string;
    }

    public static System.Reflection.FieldInfo[] GetAllTagFields()
        => typeof(OrkeonDiagnosticTags)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
}
