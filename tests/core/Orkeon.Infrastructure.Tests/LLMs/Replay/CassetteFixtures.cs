using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

internal static class CassetteFixtures
{
    internal const string CassettePath = "/llm-cassette/run.jsonl";

    internal static FakeFileSystemService NewFileSystem() => new FakeFileSystemService().AddMount("/llm-cassette");

    internal static LlmMessage[] User(string text) => [new LlmMessage { Role = "user", Content = text }];

    internal static async Task<string> ReadAsync(FakeFileSystemService fs) =>
        await fs.TryReadAllTextAsync(CassettePath, TestContext.Current.CancellationToken) ?? string.Empty;

    internal static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
