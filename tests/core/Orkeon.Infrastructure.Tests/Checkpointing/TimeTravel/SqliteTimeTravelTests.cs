using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Checkpointing.TimeTravel;

/// <summary>
/// Time-travel tests using the <see cref="SqliteStateStore"/> implementation.
/// Uses an in-memory SQLite database for fast, isolated tests.
/// </summary>
public class SqliteTimeTravelTests : TimeTravelTestBase
{
    private SqliteStateStore? _sqliteStore;

    protected override IStateStore CreateStore()
    {
        _sqliteStore = new SqliteStateStore("Data Source=:memory:", new FakeFileSystemService());
        return _sqliteStore;
    }

    protected override Task CleanupAsync()
    {
        _sqliteStore?.Dispose();
        return Task.CompletedTask;
    }
}
