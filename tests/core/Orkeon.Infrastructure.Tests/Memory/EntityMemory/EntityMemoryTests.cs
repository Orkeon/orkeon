using Orkeon.Domain.Memory;
using Infra = Orkeon.Infrastructure.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

public class EntityMemoryTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateEntityMemory_WhenConstructorWithDefaultParameters()
    {
        // Act
        var entityMemory = new Infra.EntityMemory();

        // Assert
        Assert.NotNull(entityMemory);
    }

    #endregion

    #region AddEntityAsync Tests

    [Fact]
    public async Task ShouldAddToMemory_WhenAddEntityAsyncWithValidEntity()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "role", "developer" }, { "experience", "5 years" } };

        // Act
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, attributes);

        // Assert
        var retrievedEntity = await entityMemory.GetEntityAsync("John Doe");
        Assert.NotNull(retrievedEntity);
        Assert.Equal("John Doe", retrievedEntity.Name);
        Assert.Equal(EntityType.Person, retrievedEntity.Type);
        Assert.Equal("developer", retrievedEntity.Attributes["role"]);
        Assert.Equal("5 years", retrievedEntity.Attributes["experience"]);
    }

    [Fact]
    public async Task ShouldAddEntityWithEmptyAttributes_WhenAddEntityAsyncWithEmptyAttributes()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string>();

        // Act
        await entityMemory.AddEntityAsync("Jane Smith", EntityType.Person, attributes);

        // Assert
        var retrievedEntity = await entityMemory.GetEntityAsync("Jane Smith");
        Assert.NotNull(retrievedEntity);
        Assert.Equal("Jane Smith", retrievedEntity.Name);
        Assert.Equal(EntityType.Person, retrievedEntity.Type);
        Assert.Empty(retrievedEntity.Attributes);
    }

    [Fact]
    public async Task ShouldAddAllTypes_WhenAddEntityAsyncWithDifferentEntityTypes()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();

        // Act
        await entityMemory.AddEntityAsync("Microsoft", EntityType.Organization, []);
        await entityMemory.AddEntityAsync("San Francisco", EntityType.Place, []);
        await entityMemory.AddEntityAsync("AI", EntityType.Concept, []);

        // Assert
        var microsoft = await entityMemory.GetEntityAsync("Microsoft");
        var sanFrancisco = await entityMemory.GetEntityAsync("San Francisco");
        var ai = await entityMemory.GetEntityAsync("AI");

        Assert.NotNull(microsoft);
        Assert.Equal(EntityType.Organization, microsoft.Type);
        Assert.NotNull(sanFrancisco);
        Assert.Equal(EntityType.Place, sanFrancisco.Type);
        Assert.NotNull(ai);
        Assert.Equal(EntityType.Concept, ai.Type);
    }

    [Fact]
    public async Task ShouldNormalizeNames_WhenAddEntityAsyncCaseInsensitive()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "role", "manager" } };

        // Act
        await entityMemory.AddEntityAsync("John DOE", EntityType.Person, attributes);

        // Assert - Should be retrievable with different casing
        var entity1 = await entityMemory.GetEntityAsync("john doe");
        var entity2 = await entityMemory.GetEntityAsync("JOHN DOE");
        var entity3 = await entityMemory.GetEntityAsync("John Doe");

        Assert.NotNull(entity1);
        Assert.NotNull(entity2);
        Assert.NotNull(entity3);
        Assert.Equal("John DOE", entity1.Name); // Original case preserved
        Assert.Equal("John DOE", entity2.Name);
        Assert.Equal("John DOE", entity3.Name);
    }

    [Fact]
    public async Task ShouldOverwriteEntity_WhenAddEntityAsyncDuplicateName()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string> { { "role", "developer" } };
        var newAttributes = new Dictionary<string, string> { { "role", "senior developer" } };

        // Act
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, originalAttributes);
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, newAttributes);

        // Assert
        var entity = await entityMemory.GetEntityAsync("John Doe");
        Assert.NotNull(entity);
        Assert.Equal("senior developer", entity.Attributes["role"]);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenAddEntityAsyncConcurrentAdds()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            int entityIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var attributes = new Dictionary<string, string> { { "id", entityIndex.ToString() } };
                await entityMemory.AddEntityAsync($"Entity{entityIndex}", EntityType.Person, attributes);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);
        Assert.Equal(50, people.Count);

        // Each entity should have unique ID
        var uniqueIds = people.Select(p => p.Attributes["id"]).Distinct().Count();
        Assert.Equal(50, uniqueIds);
    }

    #endregion

    #region GetEntityAsync Tests

    [Fact]
    public async Task ShouldReturnNull_WhenGetEntityAsyncNonExistentEntity()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();

        // Act
        var entity = await entityMemory.GetEntityAsync("Non-existent Entity");

        // Assert
        Assert.Null(entity);
    }

    [Fact]
    public async Task ShouldReturnEntity_WhenGetEntityAsyncExistingEntity()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "department", "Engineering" } };
        await entityMemory.AddEntityAsync("Alice Johnson", EntityType.Person, attributes);

        // Act
        var entity = await entityMemory.GetEntityAsync("Alice Johnson");

        // Assert
        Assert.NotNull(entity);
        Assert.Equal("Alice Johnson", entity.Name);
        Assert.Equal(EntityType.Person, entity.Type);
        Assert.Equal("Engineering", entity.Attributes["department"]);
    }

    [Fact]
    public async Task ShouldReturnEntity_WhenGetEntityAsyncCaseInsensitive()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "city", "New York" } };
        await entityMemory.AddEntityAsync("Central Park", EntityType.Place, attributes);

        // Act
        var entity1 = await entityMemory.GetEntityAsync("central park");
        var entity2 = await entityMemory.GetEntityAsync("CENTRAL PARK");
        var entity3 = await entityMemory.GetEntityAsync("Central Park");

        // Assert
        Assert.NotNull(entity1);
        Assert.NotNull(entity2);
        Assert.NotNull(entity3);
        Assert.Equal("Central Park", entity1.Name);
        Assert.Equal("Central Park", entity2.Name);
        Assert.Equal("Central Park", entity3.Name);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenGetEntityAsyncConcurrentGets()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "status", "active" } };
        await entityMemory.AddEntityAsync("Test Entity", EntityType.Organization, attributes);

        var tasks = new List<Task<MemoryEntity?>>();

        // Act
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await entityMemory.GetEntityAsync("Test Entity")));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.All(results, entity =>
        {
            Assert.NotNull(entity);
            Assert.Equal("Test Entity", entity.Name);
            Assert.Equal(EntityType.Organization, entity.Type);
            Assert.Equal("active", entity.Attributes["status"]);
        });
    }

    #endregion

    #region GetEntitiesByTypeAsync Tests

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetEntitiesByTypeAsyncEmptyMemory()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();

        // Act
        var entities = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);

        // Assert
        Assert.Empty(entities);
    }

    [Fact]
    public async Task ShouldReturnMatchingEntities_WhenGetEntitiesByTypeAsyncWithMatchingType()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Jane Smith", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Microsoft", EntityType.Organization, []);
        await entityMemory.AddEntityAsync("Google", EntityType.Organization, []);

        // Act
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);
        var organizations = await entityMemory.GetEntitiesByTypeAsync(EntityType.Organization);

        // Assert
        Assert.Equal(2, people.Count);
        Assert.All(people, entity => Assert.Equal(EntityType.Person, entity.Type));
        Assert.Contains(people, entity => entity.Name == "John Doe");
        Assert.Contains(people, entity => entity.Name == "Jane Smith");

        Assert.Equal(2, organizations.Count);
        Assert.All(organizations, entity => Assert.Equal(EntityType.Organization, entity.Type));
        Assert.Contains(organizations, entity => entity.Name == "Microsoft");
        Assert.Contains(organizations, entity => entity.Name == "Google");
    }

    [Fact]
    public async Task ShouldOrderByName_WhenGetEntitiesByTypeAsync()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        await entityMemory.AddEntityAsync("Charlie", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Alice", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Bob", EntityType.Person, []);

        // Act
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);

        // Assert
        Assert.Equal(3, people.Count);
        Assert.Equal("Alice", people[0].Name);
        Assert.Equal("Bob", people[1].Name);
        Assert.Equal("Charlie", people[2].Name);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetEntitiesByTypeAsyncNoMatchingType()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Microsoft", EntityType.Organization, []);

        // Act
        var places = await entityMemory.GetEntitiesByTypeAsync(EntityType.Place);

        // Assert
        Assert.Empty(places);
    }

    [Fact]
    public async Task ShouldReturnCorrectEntities_WhenGetEntitiesByTypeAsyncAllEntityTypes()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        await entityMemory.AddEntityAsync("John", EntityType.Person, []);
        await entityMemory.AddEntityAsync("Seattle", EntityType.Place, []);
        await entityMemory.AddEntityAsync("Microsoft", EntityType.Organization, []);
        await entityMemory.AddEntityAsync("Machine Learning", EntityType.Concept, []);
        await entityMemory.AddEntityAsync("VS Code", EntityType.Tool, []);
        await entityMemory.AddEntityAsync("Something Else", EntityType.Other, []);

        // Act & Assert
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);
        var places = await entityMemory.GetEntitiesByTypeAsync(EntityType.Place);
        var organizations = await entityMemory.GetEntitiesByTypeAsync(EntityType.Organization);
        var concepts = await entityMemory.GetEntitiesByTypeAsync(EntityType.Concept);
        var tools = await entityMemory.GetEntitiesByTypeAsync(EntityType.Tool);
        var others = await entityMemory.GetEntitiesByTypeAsync(EntityType.Other);

        Assert.Single(people);
        Assert.Equal("John", people[0].Name);
        Assert.Single(places);
        Assert.Equal("Seattle", places[0].Name);
        Assert.Single(organizations);
        Assert.Equal("Microsoft", organizations[0].Name);
        Assert.Single(concepts);
        Assert.Equal("Machine Learning", concepts[0].Name);
        Assert.Single(tools);
        Assert.Equal("VS Code", tools[0].Name);
        Assert.Single(others);
        Assert.Equal("Something Else", others[0].Name);
    }

    #endregion

    #region UpdateEntityAsync Tests

    [Fact]
    public async Task ShouldMergeAttributes_WhenUpdateEntityAsyncExistingEntity()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string>
        {
            { "role", "developer" },
            { "experience", "3 years" }
        };
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, originalAttributes);

        var updateAttributes = new Dictionary<string, string>
        {
            { "experience", "5 years" }, // Update existing
            { "department", "Engineering" } // Add new
        };

        // Act
        await entityMemory.UpdateEntityAsync("John Doe", updateAttributes);

        // Assert
        var entity = await entityMemory.GetEntityAsync("John Doe");
        Assert.NotNull(entity);
        Assert.Equal(3, entity.Attributes.Count);
        Assert.Equal("developer", entity.Attributes["role"]); // Original preserved
        Assert.Equal("5 years", entity.Attributes["experience"]); // Updated
        Assert.Equal("Engineering", entity.Attributes["department"]); // New added
    }

    [Fact]
    public async Task ShouldCreateNewEntity_WhenUpdateEntityAsyncNonExistentEntity()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var attributes = new Dictionary<string, string> { { "status", "new" } };

        // Act
        await entityMemory.UpdateEntityAsync("New Entity", attributes);

        // Assert
        var entity = await entityMemory.GetEntityAsync("New Entity");
        Assert.NotNull(entity);
        Assert.Equal("New Entity", entity.Name);
        Assert.Equal(EntityType.Other, entity.Type); // Default type for created entities
        Assert.Equal("new", entity.Attributes["status"]);
    }

    [Fact]
    public async Task ShouldUpdateCorrectEntity_WhenUpdateEntityAsyncCaseInsensitive()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string> { { "status", "active" } };
        await entityMemory.AddEntityAsync("Test Entity", EntityType.Organization, originalAttributes);

        var updateAttributes = new Dictionary<string, string> { { "status", "inactive" } };

        // Act
        await entityMemory.UpdateEntityAsync("test entity", updateAttributes);

        // Assert
        var entity = await entityMemory.GetEntityAsync("Test Entity");
        Assert.NotNull(entity);
        Assert.Equal("inactive", entity.Attributes["status"]);
    }

    [Fact]
    public async Task ShouldUpdateLastUpdatedTimestamp_WhenUpdateEntityAsync()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string> { { "version", "1.0" } };
        await entityMemory.AddEntityAsync("Test Entity", EntityType.Tool, originalAttributes);

        var originalEntity = await entityMemory.GetEntityAsync("Test Entity");
        var originalTimestamp = originalEntity!.LastUpdated;

        // Wait a small amount to ensure timestamp difference
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken);

        var updateAttributes = new Dictionary<string, string> { { "version", "2.0" } };

        // Act
        await entityMemory.UpdateEntityAsync("Test Entity", updateAttributes);

        // Assert
        var updatedEntity = await entityMemory.GetEntityAsync("Test Entity");
        Assert.NotNull(updatedEntity);
        Assert.True(updatedEntity.LastUpdated > originalTimestamp);
        Assert.Equal("2.0", updatedEntity.Attributes["version"]);
    }

    [Fact]
    public async Task ShouldNotChangeExistingAttributes_WhenUpdateEntityAsyncWithEmptyAttributes()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string>
        {
            { "role", "developer" },
            { "experience", "3 years" }
        };
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, originalAttributes);

        var emptyAttributes = new Dictionary<string, string>();

        // Act
        await entityMemory.UpdateEntityAsync("John Doe", emptyAttributes);

        // Assert
        var entity = await entityMemory.GetEntityAsync("John Doe");
        Assert.NotNull(entity);
        Assert.Equal(2, entity.Attributes.Count);
        Assert.Equal("developer", entity.Attributes["role"]);
        Assert.Equal("3 years", entity.Attributes["experience"]);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenUpdateEntityAsyncConcurrentUpdates()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var originalAttributes = new Dictionary<string, string> { { "counter", "0" } };
        await entityMemory.AddEntityAsync("Counter Entity", EntityType.Concept, originalAttributes);

        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 20; i++)
        {
            int updateIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var updateAttributes = new Dictionary<string, string> { { $"update_{updateIndex}", "done" } };
                await entityMemory.UpdateEntityAsync("Counter Entity", updateAttributes);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var entity = await entityMemory.GetEntityAsync("Counter Entity");
        Assert.NotNull(entity);
        Assert.True(entity.Attributes.Count >= 20); // At least 20 updates plus original counter

        // Should have all update attributes
        for (int i = 0; i < 20; i++)
        {
            Assert.True(entity.Attributes.ContainsKey($"update_{i}"));
            Assert.Equal("done", entity.Attributes[$"update_{i}"]);
        }
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestCompleteWorkflow()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();

        // Act & Assert - Add entities
        await entityMemory.AddEntityAsync("John Doe", EntityType.Person, new Dictionary<string, string>
        {
            { "role", "developer" },
            { "team", "backend" }
        });
        await entityMemory.AddEntityAsync("Jane Smith", EntityType.Person, new Dictionary<string, string>
        {
            { "role", "designer" },
            { "team", "frontend" }
        });
        await entityMemory.AddEntityAsync("Acme Corp", EntityType.Organization, new Dictionary<string, string>
        {
            { "industry", "technology" }
        });

        // Verify entities were added
        var john = await entityMemory.GetEntityAsync("John Doe");
        var jane = await entityMemory.GetEntityAsync("Jane Smith");
        var acme = await entityMemory.GetEntityAsync("Acme Corp");

        Assert.NotNull(john);
        Assert.NotNull(jane);
        Assert.NotNull(acme);

        // Get entities by type
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);
        var organizations = await entityMemory.GetEntitiesByTypeAsync(EntityType.Organization);

        Assert.Equal(2, people.Count);
        Assert.Single(organizations);

        // Update entity
        await entityMemory.UpdateEntityAsync("John Doe", new Dictionary<string, string>
        {
            { "role", "senior developer" },
            { "experience", "5 years" }
        });

        var updatedJohn = await entityMemory.GetEntityAsync("John Doe");
        Assert.NotNull(updatedJohn);
        Assert.Equal("senior developer", updatedJohn.Attributes["role"]);
        Assert.Equal("backend", updatedJohn.Attributes["team"]);
        Assert.Equal("5 years", updatedJohn.Attributes["experience"]);

        // Update non-existent entity should create it
        await entityMemory.UpdateEntityAsync("New Person", new Dictionary<string, string>
        {
            { "status", "created_via_update" }
        });

        var newPerson = await entityMemory.GetEntityAsync("New Person");
        Assert.NotNull(newPerson);
        Assert.Equal(EntityType.Other, newPerson.Type);
        Assert.Equal("created_via_update", newPerson.Attributes["status"]);
    }

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestCaseInsensitivityThroughoutWorkflow()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();

        // Act - Add with mixed case
        await entityMemory.AddEntityAsync("JOHN DOE", EntityType.Person, new Dictionary<string, string>
        {
            { "role", "developer" }
        });

        // Get with different case
        var entity1 = await entityMemory.GetEntityAsync("john doe");
        var entity2 = await entityMemory.GetEntityAsync("John Doe");
        var entity3 = await entityMemory.GetEntityAsync("JOHN DOE");

        // Update with different case
        await entityMemory.UpdateEntityAsync("John DOE", new Dictionary<string, string>
        {
            { "experience", "3 years" }
        });

        var updatedEntity = await entityMemory.GetEntityAsync("john doe");

        // Assert
        Assert.NotNull(entity1);
        Assert.NotNull(entity2);
        Assert.NotNull(entity3);
        Assert.NotNull(updatedEntity);

        // All references should be to the same entity (name preserved as originally entered)
        Assert.Equal("JOHN DOE", entity1.Name);
        Assert.Equal("JOHN DOE", entity2.Name);
        Assert.Equal("JOHN DOE", entity3.Name);
        Assert.Equal("JOHN DOE", updatedEntity.Name);

        // Should have both original and updated attributes
        Assert.Equal("developer", updatedEntity.Attributes["role"]);
        Assert.Equal("3 years", updatedEntity.Attributes["experience"]);
    }

    [Fact]
    public async Task ShouldMaintainConsistency_WhenIntegrationTestConcurrentOperations()
    {
        // Arrange
        var entityMemory = new Infra.EntityMemory();
        var tasks = new List<Task>();

        // Act - Concurrent adds
        for (int i = 0; i < 10; i++)
        {
            int entityIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await entityMemory.AddEntityAsync($"Person{entityIndex}", EntityType.Person,
                    new Dictionary<string, string> { { "id", entityIndex.ToString() } });
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);
        tasks.Clear();

        // Concurrent updates (after all adds completed)
        for (int i = 0; i < 10; i++)
        {
            int entityIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await entityMemory.UpdateEntityAsync($"Person{entityIndex}",
                    new Dictionary<string, string> { { "status", "active" } });
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var people = await entityMemory.GetEntitiesByTypeAsync(EntityType.Person);
        Assert.Equal(10, people.Count);

        foreach (var person in people)
        {
            Assert.Contains("id", person.Attributes.Keys);
            Assert.Contains("status", person.Attributes.Keys);
            Assert.Equal("active", person.Attributes["status"]);
        }
    }

    #endregion
}
