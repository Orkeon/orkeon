using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Infrastructure.Tests.Checkpointing.TimeTravel;

/// <summary>
/// Time-travel tests using the <see cref="InMemoryStateStore"/> implementation.
/// </summary>
public class InMemoryTimeTravelTests : TimeTravelTestBase
{
    protected override IStateStore CreateStore() => new InMemoryStateStore();
}
