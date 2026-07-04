using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Migration;

namespace Orkeon.Infrastructure.Tests.Memory.Migration;

public class MemoryMigrationServiceTests
{
    private readonly MemoryMigrationServiceTestsFixture _fixture;

    public MemoryMigrationServiceTests()
    {
        _fixture = new MemoryMigrationServiceTestsFixture();
    }

    [Fact]
    public async Task ShouldCopyAllItems_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("Content 1"));
        source.Seed("key2", MemoryItem.Create("Content 2"));
        source.Seed("key3", MemoryItem.Create("Content 3"));

        // Act
        var result = await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.TotalItems);
        Assert.Equal(3, result.MigratedItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Equal(0, result.SkippedItems);
        Assert.Empty(result.Errors);
        Assert.Equal(3, target.Count);

        var item1 = await target.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(item1);
        Assert.Equal("Content 1", item1.Content);
    }

    [Fact]
    public async Task ShouldRespectBatchSize_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        for (var i = 0; i < 10; i++)
        {
            source.Seed($"key{i}", MemoryItem.Create($"Content {i}"));
        }

        var options = new MigrationOptions { BatchSize = 3 };

        // Act
        var result = await service.MigrateAsync(source, target, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, result.TotalItems);
        Assert.Equal(10, result.MigratedItems);
        Assert.Equal(10, target.Count);
    }

    [Fact]
    public async Task ShouldHandleFailuresGracefully_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        // Target that throws on specific keys
        var target = new MemoryMigrationServiceTestsFixture.FakeMemoryProvider(
            storeExceptionFactory: key =>
                key == "key2" ? new InvalidOperationException("Store failed for key2") : null);

        source.Seed("key1", MemoryItem.Create("Content 1"));
        source.Seed("key2", MemoryItem.Create("Content 2"));
        source.Seed("key3", MemoryItem.Create("Content 3"));

        // Act
        var result = await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.TotalItems);
        Assert.Equal(2, result.MigratedItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Single(result.Errors);
        Assert.Contains("key2", result.Errors[0]);
    }

    [Fact]
    public async Task ShouldDeleteFromSource_WhenMigrateAsyncWithDeleteFromSource()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("Content 1"));
        source.Seed("key2", MemoryItem.Create("Content 2"));

        var options = new MigrationOptions { DeleteFromSource = true };

        // Act
        var result = await service.MigrateAsync(source, target, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.MigratedItems);
        Assert.Equal(0, source.Count);
        Assert.Equal(2, target.Count);
    }

    [Fact]
    public async Task ShouldSkipExistingItems_WhenMigrateAsyncWithOverwriteExistingFalse()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("Source content 1"));
        source.Seed("key2", MemoryItem.Create("Source content 2"));
        source.Seed("key3", MemoryItem.Create("Source content 3"));

        // Pre-populate target with key2
        target.Seed("key2", MemoryItem.Create("Existing content 2"));

        var options = new MigrationOptions { OverwriteExisting = false };

        // Act
        var result = await service.MigrateAsync(source, target, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.TotalItems);
        Assert.Equal(2, result.MigratedItems);
        Assert.Equal(1, result.SkippedItems);
        Assert.Equal(0, result.FailedItems);

        // The existing item should not be overwritten
        var existingItem = await target.GetAsync("key2", TestContext.Current.CancellationToken);
        Assert.NotNull(existingItem);
        Assert.Equal("Existing content 2", existingItem.Content);
    }

    [Fact]
    public async Task ShouldReturnCorrectCounts_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("Content 1"));
        source.Seed("key2", MemoryItem.Create("Content 2"));
        source.Seed("key3", MemoryItem.Create("Content 3"));
        source.Seed("key4", MemoryItem.Create("Content 4"));
        source.Seed("key5", MemoryItem.Create("Content 5"));

        // Act
        var result = await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(5, result.MigratedItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Equal(0, result.SkippedItems);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ShouldReturnEmptyResult_WhenMigrateAsyncWithEmptySource()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        // Act
        var result = await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.TotalItems);
        Assert.Equal(0, result.MigratedItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Equal(0, result.SkippedItems);
    }

    [Fact]
    public async Task ShouldOverwriteExistingByDefault_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("New content"));
        target.Seed("key1", MemoryItem.Create("Old content"));

        // Act (default options: OverwriteExisting = true)
        var result = await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.MigratedItems);
        var item = await target.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(item);
        Assert.Equal("New content", item.Content);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSourceIsNull()
    {
        // Arrange
        var service = _fixture.CreateService();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.MigrateAsync(null!, target, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenTargetIsNull()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.MigrateAsync(source, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldNotDeleteFromSource_WhenDeleteFromSourceIsFalse()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        source.Seed("key1", MemoryItem.Create("Content 1"));
        source.Seed("key2", MemoryItem.Create("Content 2"));

        var options = new MigrationOptions { DeleteFromSource = false };

        // Act
        var result = await service.MigrateAsync(source, target, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.MigratedItems);
        Assert.Equal(2, source.Count); // Source should still have items
        Assert.Equal(2, target.Count);
    }

    [Fact]
    public async Task ShouldPreserveItemContent_WhenMigrateAsync()
    {
        // Arrange
        var service = _fixture.CreateService();
        var source = MemoryMigrationServiceTestsFixture.CreateFakeProvider();
        var target = MemoryMigrationServiceTestsFixture.CreateFakeProvider();

        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var item = MemoryItem.Create("Important content", embedding: embedding, importance: 0.9f, source: "research");
        source.Seed("key1", item);

        // Act
        await service.MigrateAsync(source, target, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var migrated = await target.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(migrated);
        Assert.Equal("Important content", migrated.Content);
        Assert.Equal(0.9f, migrated.Importance);
        Assert.Equal("research", migrated.Source);
        Assert.NotNull(migrated.Embedding);
        Assert.Equal(3, migrated.Embedding.Count);
    }
}
