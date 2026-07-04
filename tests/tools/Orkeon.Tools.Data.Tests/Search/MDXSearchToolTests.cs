using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Search;
using Orkeon.Tools.Data.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Search;

public sealed class MDXSearchToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockEmbeddingService _mockEmbeddingService;
    private readonly MdxSearchTool _tool;

    public MDXSearchToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdx_search_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockEmbeddingService = new MockEmbeddingService();
        _tool = new MdxSearchTool(_mockEmbeddingService, new PassThroughFileSystemService());
    }

    [Fact]
    public async Task ShouldStripFrontmatter_WhenMarkdownHasYamlHeader()
    {
        // Arrange: markdown file with YAML frontmatter
        var content = """
            ---
            title: My Document
            author: Test
            date: 2026-01-01
            ---

            This is the actual content about configuration.

            Configuration options are listed below.
            """;

        var filePath = CreateFile("doc.md", content);

        // The query and frontmatter content get different embeddings so frontmatter won't match
        _mockEmbeddingService.SetEmbeddingFactory(text =>
            text.Contains("configuration", StringComparison.OrdinalIgnoreCase)
                ? [1.0f, 0.0f, 0.0f]
                : [0.0f, 1.0f, 0.0f]);

        var request = new ToolCallRequest(
            ToolName: "mdx_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "configuration options",
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount > 0, "Expected results from content after frontmatter stripping");

        // Verify frontmatter fields are NOT in any result
        var results = dict["results"] as IEnumerable<object>;
        Assert.NotNull(results);
        foreach (var r in results)
        {
            if (r is Dictionary<string, object?> resultDict)
            {
                var resultContent = resultDict["content"]?.ToString() ?? "";
                Assert.DoesNotContain("title:", resultContent);
                Assert.DoesNotContain("author:", resultContent);
            }
        }
    }

    [Fact]
    public async Task ShouldHandleMdAndMdxExtensions()
    {
        // Arrange: create both .md and .mdx files
        CreateFile("readme.md", "Installation guide.\n\nFollow these steps to install.");
        CreateFile("component.mdx", "React component docs.\n\nUsage example below.");

        _mockEmbeddingService.SetEmbeddingResult([0.5f, 0.5f, 0.5f]);

        var request = new ToolCallRequest(
            ToolName: "mdx_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = _tempDir,
                [ParamQuery] = "installation guide",
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True(Convert.ToInt32(dict["files_processed"]) >= 2,
            "Both .md and .mdx files should be processed");
        Assert.True(Convert.ToInt32(dict["result_count"]) > 0);
    }

    [Fact]
    public async Task ShouldStripJsxImports_WhenMdxHasImports()
    {
        // Arrange: MDX file with JSX imports and exports
        var content = """
            import { Button } from '@components/Button'
            import Layout from '../layouts/Default'
            export const meta = { title: 'Test' }
            export default function Page() {}

            # Getting Started

            This is a guide about getting started with the framework.

            Read more about configuration.
            """;

        var filePath = CreateFile("guide.mdx", content);

        _mockEmbeddingService.SetEmbeddingFactory(text =>
            text.Contains("getting started", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("framework", StringComparison.OrdinalIgnoreCase)
                ? [1.0f, 0.0f, 0.0f]
                : [0.0f, 1.0f, 0.0f]);

        var request = new ToolCallRequest(
            ToolName: "mdx_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "getting started with the framework",
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount > 0);

        // Verify import/export statements are NOT in any result content
        var results = dict["results"] as IEnumerable<object>;
        Assert.NotNull(results);
        foreach (var r in results)
        {
            if (r is Dictionary<string, object?> resultDict)
            {
                var resultContent = resultDict["content"]?.ToString() ?? "";
                Assert.DoesNotContain("import {", resultContent);
                Assert.DoesNotContain("export const", resultContent);
                Assert.DoesNotContain("export default", resultContent);
            }
        }
    }

    [Fact]
    public async Task ShouldChunkByHeadings_WhenMarkdownHasSections()
    {
        // Arrange: markdown file with distinct heading sections
        var content = """
            # Introduction

            This section describes the project overview.

            ## Installation

            Run npm install to install dependencies.

            ## Configuration

            Set the API_KEY environment variable to configure the tool.

            ## Usage

            Call the run command to start the application.
            """;

        var filePath = CreateFile("sections.md", content);

        // Make the "Configuration" heading section match the query closely
        _mockEmbeddingService.SetEmbeddingFactory(text =>
            text.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("environment variable", StringComparison.OrdinalIgnoreCase)
                ? [1.0f, 0.0f, 0.0f]
                : [0.0f, 1.0f, 0.0f]);

        var request = new ToolCallRequest(
            ToolName: "mdx_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "environment variable configuration",
                ["top_k"] = 1,
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount > 0, "Expected at least one heading-level chunk result");

        // The top result should contain the Configuration heading content
        var results = dict["results"] as IEnumerable<object>;
        Assert.NotNull(results);
        var topResult = results.Cast<Dictionary<string, object?>>().First();
        var topContent = topResult["content"]?.ToString() ?? "";
        Assert.Contains("API_KEY", topContent);
    }

    [Fact]
    public void ChunkByHeadings_ShouldSplitAtHeadingBoundaries()
    {
        var markdown = "# Intro\n\nSome text.\n\n## Section A\n\nContent A.\n\n## Section B\n\nContent B.";
        var chunks = MdxSearchTool.ChunkByHeadings(markdown, maxChunkSize: 5000);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 heading-based chunks, got {chunks.Count}");
        Assert.Contains("# Intro", chunks[0].Text);
        Assert.Contains("## Section A", chunks[1].Text);
        Assert.Contains("## Section B", chunks[2].Text);
    }

    [Fact]
    public void ChunkByHeadings_ShouldSubSplitLargeSections()
    {
        // Build a single heading section that exceeds the chunk size
        var longBody = string.Join(". ",
            Enumerable.Range(1, 40).Select(i => $"Sentence number {i} in this long section"));
        var markdown = $"## Large Section\n\n{longBody}";

        var chunks = MdxSearchTool.ChunkByHeadings(markdown, maxChunkSize: 200);

        Assert.True(chunks.Count > 1, $"Expected sub-splitting of large section, got {chunks.Count} chunk(s)");
    }

    // ── Helper methods ───────────────────────────────────────────────

    private string CreateFile(string fileName, string content)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore cleanup errors */ }
        _tool.Dispose();
    }
}
