using System.Reflection;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// CrewAI attributes with no reader are gone from the public domain types.
/// </summary>
public class InertAttributesRemovedTests
{
    private const BindingFlags Surface =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    [Trait("BR", "CH-05")]
    public void ShouldExposeNoInertCrewAiAttribute_WhenAgentTypesAreInspected()
    {
        // Arrange
        var types = new List<Type>
        {
            typeof(DomainAgent),
            typeof(Orkeon.Domain.Agent.AgentBuilder),
            typeof(Orkeon.Domain.Agent.AgentCreateOptions),
            typeof(Orkeon.Domain.Agent.AgentSpawnRequest),
            typeof(Orkeon.Domain.Agent.AgentSpawnRequestBuilder),
        };

        // Act
        var offenders = FindOffenders(types, ["CacheEnabled", "MaxRetryLimit"]);

        // Assert
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void ShouldExposeNoInertCrewAiAttribute_WhenCrewTypesAreInspected()
    {
        // Arrange
        var types = new List<Type>
        {
            typeof(DomainCrew),
            typeof(Orkeon.Domain.Crew.CrewBuilder),
            typeof(Orkeon.Domain.Crew.CrewCreateOptions),
        };

        // Act
        var offenders = FindOffenders(types, ["ShareCrew", "OutputLogFile", "FullOutput", "Language"]);
        offenders.AddRange(typeof(Orkeon.Domain.Constants.Crew.CrewDefaults)
            .GetMembers(Surface)
            .Where(m => m.Name == "DefaultLanguage")
            .Select(m => $"CrewDefaults.{m.Name}"));

        // Assert
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void ShouldHaveNoLanguageCodeType_WhenDomainAssemblyIsInspected()
    {
        // Arrange
        var assembly = typeof(DomainAgent).Assembly;

        // Act
        var offenders = assembly.GetTypes()
            .Where(t => t.Name == "LanguageCode")
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        // Assert
        Assert.Empty(offenders);
    }

    private static List<string> FindOffenders(IEnumerable<Type> types, string[] names)
    {
        var offenders = new List<string>();
        foreach (var type in types)
        {
            foreach (var member in type.GetMembers(Surface))
            {
                if (names.Contains(member.Name, StringComparer.OrdinalIgnoreCase))
                {
                    offenders.Add($"{type.Name}.{member.Name}");
                }
            }

            var methods = type.GetMethods(Surface).Cast<MethodBase>().Concat(type.GetConstructors());
            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name is not null && names.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{type.Name}.{method.Name}({parameter.Name})");
                    }
                }
            }
        }

        return offenders;
    }
}
