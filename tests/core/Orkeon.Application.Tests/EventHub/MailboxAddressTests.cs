using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.EventHub;

public sealed class MailboxAddressTests
{
    // Six valid URIs (Spec §16, supplementary unit tests).
    public static TheoryData<string, MailboxKind> ValidAddresses() =>
        new()
        {
            { $"agent://{CrewId.Create()}/{AgentId.Create()}", MailboxKind.Agent },
            { $"agent://{CrewId.Create()}/{AgentId.Create()}", MailboxKind.Agent },
            { $"crew://{CrewId.Create()}", MailboxKind.Crew },
            { $"crew://{CrewId.Create()}", MailboxKind.Crew },
            { "topic://order.received", MailboxKind.Topic },
            { "topic://_audit.cross-crew-1", MailboxKind.Topic },
        };

    [Theory]
    [MemberData(nameof(ValidAddresses))]
    public void Parse_returns_expected_kind_for_valid_uris(string uri, MailboxKind expectedKind)
    {
        var address = MailboxAddress.Parse(new Uri(uri));
        Assert.Equal(expectedKind, address.Kind);
        Assert.Equal(uri, address.Raw);
    }

    // Five invalid URIs.
    public static TheoryData<string> InvalidAddresses() =>
        new()
        {
            "",
            "http://example.com",
            "agent://",
            $"agent://{CrewId.Create()}",            // missing agent id
            "topic://invalid_space ",                // trailing space is an invalid topic char
        };

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public void Parse_throws_on_invalid_uris(string uri)
    {
        Assert.Throws<InvalidMailboxAddressException>(
            () => MailboxAddress.Parse(new Uri(uri, UriKind.RelativeOrAbsolute)));
    }

    [Fact]
    public void TryParse_returns_false_on_invalid_uri()
    {
        Assert.False(MailboxAddress.TryParse(new Uri("not-a-uri", UriKind.RelativeOrAbsolute), out var addr));
        Assert.Null(addr);
    }

    [Fact]
    public void Parsed_agent_address_exposes_crew_and_agent_ids()
    {
        var crewId = CrewId.Create();
        var agentId = AgentId.Create();
        var addr = MailboxAddress.Parse(new Uri($"agent://{crewId}/{agentId}"));
        Assert.Equal(MailboxKind.Agent, addr.Kind);
        Assert.Equal(crewId, addr.CrewId);
        Assert.Equal(agentId, addr.AgentId);
        Assert.Null(addr.Topic);
    }

    [Fact]
    public void Parsed_topic_address_exposes_topic_name()
    {
        var addr = MailboxAddress.Parse(new Uri("topic://order.received"));
        Assert.Equal(MailboxKind.Topic, addr.Kind);
        Assert.Equal("order.received", addr.Topic);
        Assert.Null(addr.CrewId);
        Assert.Null(addr.AgentId);
    }
}
