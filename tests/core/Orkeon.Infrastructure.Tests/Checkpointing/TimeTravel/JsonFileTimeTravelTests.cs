using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Checkpointing.TimeTravel;

/// <summary>
/// Time-travel tests using the <see cref="JsonFileStateStore"/> implementation backed by <see cref="FakeFileSystemService"/>.
/// </summary>
public class JsonFileTimeTravelTests : TimeTravelTestBase
{
    private const string BaseVirtualPath = "/state";

    protected override IStateStore CreateStore()
    {
        var fake = new FakeFileSystemService();
        fake.AddMount(BaseVirtualPath, FileAccessRights.ReadWrite);
        return new JsonFileStateStore(fake, BaseVirtualPath);
    }
}
