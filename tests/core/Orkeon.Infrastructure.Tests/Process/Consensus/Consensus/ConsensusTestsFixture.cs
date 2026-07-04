using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ToolUsage = Orkeon.Domain.Tools.ToolUsage;

namespace Orkeon.Infrastructure.Tests;

public class ConsensusTestsFixture
{
    private readonly List<string> _messages = new();

    public ConsensusTestsFixture()
    {
    }

    public ConsensusTestsFixture WithMessages(List<string> value)
    {
        // Configure _messages as needed
        return this;
    }

    public List<string> GetMessages() => _messages;

}
