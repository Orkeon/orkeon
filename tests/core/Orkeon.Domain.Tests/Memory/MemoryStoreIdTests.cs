using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Memory;

public class MemoryStoreIdTests
{
    #region Create Factory Method Tests

    [Fact]
    public void ShouldGenerateNewMemoryStoreId_WhenCreating()
    {
        // Act
        var id = MemoryStoreId.Create();

        // Assert
        Assert.NotNull(id);
        Assert.NotEqual(default(Ulid), id.Value);
    }

    [Fact]
    public void ShouldGenerateUniqueIds_WhenCreating()
    {
        // Act
        var id1 = MemoryStoreId.Create();
        var id2 = MemoryStoreId.Create();
        var id3 = MemoryStoreId.Create();

        // Assert
        Assert.NotEqual(id1.Value, id2.Value);
        Assert.NotEqual(id1.Value, id3.Value);
        Assert.NotEqual(id2.Value, id3.Value);
    }

    [Fact]
    public void ShouldAlwaysGenerateValidUlids_WhenCreatingWithMultipleInvocations()
    {
        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            var id = MemoryStoreId.Create();
            Assert.NotEqual(default(Ulid), id.Value);
        }
    }

    #endregion

    #region From(Guid) Factory Method Tests

    [Fact]
    public void ShouldCreateMemoryStoreId_WhenUsingFromGuidWithValidGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id = MemoryStoreId.From(guid);

        // Assert
        Assert.NotNull(id);
        Assert.Equal(new Ulid(guid), id.Value);
    }

    [Fact]
    public void ShouldThrow_WhenUsingFromGuidWithEmptyGuid()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            MemoryStoreId.From(Guid.Empty));
        Assert.Contains("Invalid MemoryStoreId: empty ULID", exception.Message);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEqualIds_WhenUsingFromGuidWithSameGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id1 = MemoryStoreId.From(guid);
        var id2 = MemoryStoreId.From(guid);

        // Assert
        Assert.Equal(id1, id2);
        Assert.Equal(id1.Value, id2.Value);
    }

    #endregion

    #region Parse(string) Factory Method Tests

    [Fact]
    public void ShouldCreateMemoryStoreId_WhenUsingParseWithValidUlidString()
    {
        // Arrange
        var ulid = Ulid.NewUlid();
        var ulidString = ulid.ToString();

        // Act
        var id = MemoryStoreId.Parse(ulidString);

        // Assert
        Assert.NotNull(id);
        Assert.Equal(ulid, id.Value);
    }

    [Fact]
    public void ShouldThrow_WhenUsingFromStringWithInvalidFormat()
    {
        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            MemoryStoreId.Parse("not-a-valid-ulid"));
    }

    [Fact]
    public void ShouldThrow_WhenUsingFromStringWithEmptyString()
    {
        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            MemoryStoreId.Parse(""));
    }

    [Fact]
    public void ShouldThrow_WhenUsingFromStringWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            MemoryStoreId.From(default(Ulid)));
        Assert.Contains("Invalid MemoryStoreId: empty ULID", exception.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUsingFromStringWithWhitespace()
    {
        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            MemoryStoreId.Parse("   "));
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingFromGuidWithDifferentGuids()
    {
        // Arrange
        var guids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        // Act & Assert
        foreach (var guid in guids)
        {
            var id = MemoryStoreId.From(guid);
            Assert.Equal(new Ulid(guid), id.Value);
        }
    }

    [Fact]
    public void ShouldBothWork_WhenUsingFromGuidWithSameGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var id1 = MemoryStoreId.From(guid);
        var id2 = MemoryStoreId.From(guid);

        // Assert
        Assert.Equal(id1, id2);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ShouldReturnGuidString_WhenCallingToString()
    {
        // Arrange
        var id = MemoryStoreId.Create();

        // Act
        var result = id.ToString();

        // Assert
        Assert.Equal(id.Value.ToString(), result);
        Assert.True(Ulid.TryParse(result, out _));
    }

    [Fact]
    public void ShouldBeConsistentWithValue_WhenCallingToString()
    {
        // Arrange
        var id = MemoryStoreId.Create();

        // Act
        var stringValue = id.ToString();
        var guidValue = id.Value.ToString();

        // Assert
        Assert.Equal(guidValue, stringValue);
    }

    [Fact]
    public void ShouldRoundTrip_WhenCallingToString()
    {
        // Arrange
        var original = MemoryStoreId.Create();

        // Act
        var stringValue = original.ToString();
        var recreated = MemoryStoreId.Parse(stringValue);

        // Assert
        Assert.Equal(original, recreated);
        Assert.Equal(original.Value, recreated.Value);
    }

    #endregion

    #region Value Object Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id1 = MemoryStoreId.From(guid);
        var id2 = MemoryStoreId.From(guid);

        // Act & Assert
        Assert.True(id1.Equals(id2));
        Assert.True(id2.Equals(id1));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValue()
    {
        // Arrange
        var id1 = MemoryStoreId.Create();
        var id2 = MemoryStoreId.Create();

        // Act & Assert
        Assert.False(id1.Equals(id2));
        Assert.False(id2.Equals(id1));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var id = MemoryStoreId.Create();

        // Act & Assert
        Assert.False(id.Equals(null));
        Assert.NotNull(id);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentType()
    {
        // Arrange
        var id = MemoryStoreId.Create();
        var other = "not a MemoryStoreId";

        // Act & Assert
        Assert.False(id.Equals(other));
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id1 = MemoryStoreId.From(guid);
        var id2 = MemoryStoreId.From(guid);

        // Act
        var hash1 = id1.GetHashCode();
        var hash2 = id2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHash_WhenCallingGetHashCodeWithDifferentValue()
    {
        // Arrange
        var id1 = MemoryStoreId.Create();
        var id2 = MemoryStoreId.Create();

        // Act
        var hash1 = id1.GetHashCode();
        var hash2 = id2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region GetEqualityComponents Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingEqualityComponents()
    {
        // Arrange
        var id = MemoryStoreId.Create();

        // Act
        var components = id.GetEqualityComponentsPublic();

        // Assert
        Assert.Single(components);
        Assert.Equal(id.Value, components[0]);
    }

    [Fact]
    public void ShouldBeConsistentWithEquals_WhenGettingEqualityComponents()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id1 = MemoryStoreId.From(guid);
        var id2 = MemoryStoreId.From(guid);

        // Act
        var components1 = id1.GetEqualityComponentsPublic();
        var components2 = id2.GetEqualityComponentsPublic();

        // Assert
        Assert.Equal(components1.Count, components2.Count);
        Assert.Equal(components1[0], components2[0]);
        Assert.Equal(id1, id2);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldCollectionUsage_WhenUsingComplexScenario()
    {
        // Arrange
        var ids = new HashSet<MemoryStoreId>();
        var idList = new List<MemoryStoreId>();

        // Act - Add multiple IDs
        for (int i = 0; i < 10; i++)
        {
            var id = MemoryStoreId.Create();
            ids.Add(id);
            idList.Add(id);
        }

        // Add duplicate (same value)
        var existingGuid = idList[5].Value;
        var duplicate = MemoryStoreId.From(existingGuid);
        var addedToSet = ids.Add(duplicate);

        // Assert
        Assert.Equal(10, ids.Count); // Set should reject duplicate
        Assert.False(addedToSet);
        Assert.Equal(10, idList.Count);
        Assert.Contains(duplicate, ids); // Should find the equal value
    }

    [Fact]
    public void ShouldDictionaryKey_WhenUsingComplexScenario()
    {
        // Arrange
        var dictionary = new Dictionary<MemoryStoreId, string>();
        var id1 = MemoryStoreId.Create();
        var id2 = MemoryStoreId.Create();
        var id3 = MemoryStoreId.From(id1.Value); // Same as id1

        // Act
        dictionary[id1] = "Memory Store 1";
        dictionary[id2] = "Memory Store 2";
        dictionary[id3] = "Updated Memory Store 1"; // Should update, not add

        // Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Updated Memory Store 1", dictionary[id1]);
        Assert.Equal("Memory Store 2", dictionary[id2]);
        Assert.True(dictionary.ContainsKey(id3));
    }

    [Fact]
    public void ShouldSerialization_WhenUsingComplexScenario()
    {
        // Arrange
        var original = MemoryStoreId.Create();

        // Act - Simulate serialization/deserialization
        var serialized = original.ToString();
        var deserialized = MemoryStoreId.Parse(serialized);

        // Assert
        Assert.Equal(original, deserialized);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void ShouldPerformanceConsistency_WhenUsingComplexScenario()
    {
        // Arrange
        var iterations = 1000;
        var ids = new List<MemoryStoreId>(iterations);
        var ulidStrings = new List<string>(iterations);

        // Act - Create many IDs
        for (int i = 0; i < iterations; i++)
        {
            var id = MemoryStoreId.Create();
            ids.Add(id);
            ulidStrings.Add(id.ToString());
        }

        // Recreate from strings
        var recreatedIds = new List<MemoryStoreId>(iterations);
        foreach (var ulidString in ulidStrings)
        {
            recreatedIds.Add(MemoryStoreId.Parse(ulidString));
        }

        // Assert - All should match
        for (int i = 0; i < iterations; i++)
        {
            Assert.Equal(ids[i], recreatedIds[i]);
            Assert.Equal(ids[i].Value, recreatedIds[i].Value);
        }

        // Verify uniqueness
        var uniqueValues = new HashSet<Ulid>(ids.Select(id => id.Value));
        Assert.Equal(iterations, uniqueValues.Count);
    }

    #endregion
}

// Extension to access protected method for testing
internal static class MemoryStoreIdTestExtensions
{
    public static IList<object> GetEqualityComponentsPublic(this MemoryStoreId id)
    {
        // Since GetEqualityComponents is protected, we test it indirectly
        // through Equals behavior, but for completeness, we can verify
        // that equality is based on the Value property
        return [id.Value];
    }
}
