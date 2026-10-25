using System.Data.Common;

namespace Orkeon.Tools.Data.Tests.Doubles;

/// <summary>A <see cref="DbProviderFactory"/> handing out a configured connection.</summary>
public sealed class FakeDbProviderFactory : DbProviderFactory
{
    /// <summary>Connection returned by <see cref="CreateConnection"/>; null simulates a broken provider.</summary>
    public DbConnection? Connection { get; set; }

    public override DbConnection? CreateConnection() => Connection;
}
