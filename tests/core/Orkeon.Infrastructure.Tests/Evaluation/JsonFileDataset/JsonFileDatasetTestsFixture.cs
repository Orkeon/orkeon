using Orkeon.Infrastructure.Evaluation;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class JsonFileDatasetTestsFixture
{
    public static JsonFileDataset CreateDataset(string name, string virtualPath, FakeFileSystemService fs)
        => new(fs, name, virtualPath);
}
