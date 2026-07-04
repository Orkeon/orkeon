using System.Text.Json;
using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Infrastructure.Tests.A2A;

public class AgentCardTests
{
    [Fact]
    public void AgentCard_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var card = new AgentCard();

        // Assert
        Assert.Equal("", card.Name);
        Assert.Equal("", card.Description);
        Assert.Null(card.Url);
        Assert.Equal("1.0.0", card.Version);
        Assert.Null(card.Provider);
        Assert.Null(card.Authentication);
        Assert.Empty(card.Skills);
        Assert.Null(card.Extensions);
    }

    [Fact]
    public void AgentCard_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var card = new AgentCard
        {
            Name = "TestAgent",
            Description = "A test agent for unit testing",
            Url = new Uri("http://localhost:5002"),
            Version = "2.0.0",
            Provider = new AgentProvider
            {
                Organization = "TestOrg",
                ContactUrl = new Uri("https://test.org/contact")
            },
            Authentication = new AgentAuthentication
            {
                Schemes = ["Bearer", "ApiKey"],
                Credentials = "token-ref"
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = "skill-1",
                    Name = "Researcher",
                    Description = "Performs research tasks",
                    Tags = ["research", "analysis"],
                    InputModes = ["text/plain", "application/json"],
                    OutputModes = ["text/plain"]
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(card);
        var deserialized = JsonSerializer.Deserialize<AgentCard>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("TestAgent", deserialized!.Name);
        Assert.Equal("A test agent for unit testing", deserialized.Description);
        Assert.Equal(new Uri("http://localhost:5002"), deserialized.Url);
        Assert.Equal("2.0.0", deserialized.Version);
        Assert.NotNull(deserialized.Provider);
        Assert.Equal("TestOrg", deserialized.Provider!.Organization);
        Assert.Equal(new Uri("https://test.org/contact"), deserialized.Provider.ContactUrl);
        Assert.NotNull(deserialized.Authentication);
        Assert.Equal(2, deserialized.Authentication!.Schemes.Count);
        Assert.Equal("Bearer", deserialized.Authentication.Schemes[0]);
        Assert.Single(deserialized.Skills);
        Assert.Equal("skill-1", deserialized.Skills[0].Id);
        Assert.Equal("Researcher", deserialized.Skills[0].Name);
        Assert.Equal(2, deserialized.Skills[0].Tags.Count);
    }

    [Fact]
    public void AgentCard_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var card = new AgentCard
        {
            Name = "Test",
            Skills =
            [
                new AgentSkill
                {
                    Id = "s1",
                    InputModes = ["text/plain"],
                    OutputModes = ["application/json"]
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(card);

        // Assert — verify JSON property names (camelCase via attributes)
        Assert.Contains("\"name\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"url\":", json);
        Assert.Contains("\"version\":", json);
        Assert.Contains("\"skills\":", json);
        Assert.Contains("\"inputModes\":", json);
        Assert.Contains("\"outputModes\":", json);
    }

    [Fact]
    public void AgentSkill_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var skill = new AgentSkill();

        // Assert
        Assert.Equal("", skill.Id);
        Assert.Equal("", skill.Name);
        Assert.Equal("", skill.Description);
        Assert.Empty(skill.Tags);
        Assert.Single(skill.InputModes);
        Assert.Equal("text/plain", skill.InputModes[0]);
        Assert.Single(skill.OutputModes);
        Assert.Equal("text/plain", skill.OutputModes[0]);
    }

    [Fact]
    public void AgentCard_ShouldDeserializeFromWellKnownFormat()
    {
        // Arrange — simulate a well-known agent.json response
        var json = """
        {
            "name": "RemoteAgent",
            "description": "A remote A2A agent",
            "url": "https://remote.example.com",
            "version": "1.0.0",
            "provider": {
                "organization": "ExampleOrg",
                "contactUrl": "https://example.com"
            },
            "skills": [
                {
                    "id": "translate",
                    "name": "Translator",
                    "description": "Translates text between languages",
                    "tags": ["translation", "nlp"],
                    "inputModes": ["text/plain"],
                    "outputModes": ["text/plain"]
                },
                {
                    "id": "summarize",
                    "name": "Summarizer",
                    "description": "Summarizes long text",
                    "tags": ["summary"],
                    "inputModes": ["text/plain", "text/html"],
                    "outputModes": ["text/plain"]
                }
            ]
        }
        """;

        // Act
        var card = JsonSerializer.Deserialize<AgentCard>(json);

        // Assert
        Assert.NotNull(card);
        Assert.Equal("RemoteAgent", card!.Name);
        Assert.Equal(2, card.Skills.Count);
        Assert.Equal("translate", card.Skills[0].Id);
        Assert.Equal("summarize", card.Skills[1].Id);
        Assert.Equal(2, card.Skills[1].InputModes.Count);
    }

    [Fact]
    public void AgentAuthentication_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var auth = new AgentAuthentication();

        // Assert
        Assert.Empty(auth.Schemes);
        Assert.Null(auth.Credentials);
    }

    [Fact]
    public void AgentProvider_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var provider = new AgentProvider();

        // Assert
        Assert.Equal("", provider.Organization);
        Assert.Null(provider.ContactUrl);
    }
}
