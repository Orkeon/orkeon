using Orkeon.Domain.Memory;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Memory;

public class EntityMemoryTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateEntityMemory_WhenConstructingWithValidParameters()
    {
        // Arrange
        var name = "John Doe";
        var type = "person";
        var description = "Software engineer";
        var confidence = 0.9f;

        // Act
        var entity = new EntityMemory(name, type, description, confidence);

        // Assert
        Assert.Equal(name, entity.Name);
        Assert.Equal(type, entity.Type);
        Assert.Equal(description, entity.Description);
        Assert.Equal(confidence, entity.Confidence);
        Assert.NotNull(entity.Attributes);
        Assert.Empty(entity.Attributes);
        Assert.NotNull(entity.Relationships);
        Assert.Empty(entity.Relationships);
        Assert.Equal(entity.FirstEncountered, entity.LastUpdated);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var name = "Test Entity";
        var type = "concept";

        // Act
        var entity = new EntityMemory(name, type);

        // Assert
        Assert.Equal(name, entity.Name);
        Assert.Equal(type, entity.Type);
        Assert.Equal(string.Empty, entity.Description);
        Assert.Equal(0.8f, entity.Confidence);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithEmptyName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityMemory("", "type"));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithWhitespaceName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityMemory("   ", "type"));
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullType()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityMemory("Name", null!));
        Assert.Equal("type", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithConfidenceBelowZero()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EntityMemory("Name", "type", "", -0.1f));
        Assert.Contains("Confidence must be between 0 and 1", exception.Message);
        Assert.Equal("confidence", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithConfidenceAboveOne()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EntityMemory("Name", "type", "", 1.1f));
        Assert.Contains("Confidence must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void ShouldSetEmptyString_WhenConstructingWithNullDescription()
    {
        // Act
        var entity = new EntityMemory("Name", "type", null!);

        // Assert
        Assert.Equal(string.Empty, entity.Description);
    }

    [Fact]
    public void ShouldSetTimestampsCorrectly_WhenConstructing()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var entity = new EntityMemory("Name", "type");
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(entity.FirstEncountered >= before);
        Assert.True(entity.FirstEncountered <= after);
        Assert.Equal(entity.FirstEncountered, entity.LastUpdated);
    }

    #endregion

    #region UpdateDescription Tests

    [Fact]
    public void ShouldUpdate_WhenUpdatingDescriptionWithValidDescription()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type", "Initial description");
        var originalLastUpdated = entity.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(originalLastUpdated); // Ensure time difference (R5.6)

        // Act
        entity.UpdateDescription("Updated description");

        // Assert
        Assert.Equal("Updated description", entity.Description);
        Assert.True(entity.LastUpdated > originalLastUpdated);
        Assert.Equal(entity.FirstEncountered, originalLastUpdated);
    }

    [Fact]
    public void ShouldSetEmptyString_WhenUpdatingDescriptionWithNullDescription()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type", "Initial");

        // Act
        entity.UpdateDescription(null!);

        // Assert
        Assert.Equal(string.Empty, entity.Description);
    }

    [Fact]
    public void ShouldAlwaysUpdateTimestamp_WhenUpdatingDescription()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");
        var timestamps = new List<DateTime>();

        // Act
        for (int i = 0; i < 3; i++)
        {
            ClockAdvance.UntilStrictlyAfter(entity.LastUpdated);
            entity.UpdateDescription($"Description {i}");
            timestamps.Add(entity.LastUpdated);
        }

        // Assert
        for (int i = 1; i < timestamps.Count; i++)
        {
            Assert.True(timestamps[i] > timestamps[i - 1]);
        }
    }

    #endregion

    #region SetAttribute Tests

    [Fact]
    public void ShouldAdd_WhenSettingAttributeWithNewAttribute()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act
        entity.SetAttribute("key1", "value1");

        // Assert
        Assert.Single(entity.Attributes);
        Assert.Equal("value1", entity.Attributes["key1"]);
    }

    [Fact]
    public void ShouldUpdate_WhenSettingAttributeWithExistingAttribute()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");
        entity.SetAttribute("key", "initial");

        // Act
        entity.SetAttribute("key", "updated");

        // Assert
        Assert.Single(entity.Attributes);
        Assert.Equal("updated", entity.Attributes["key"]);
    }

    [Fact]
    public void ShouldThrow_WhenSettingAttributeWithEmptyKey()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            entity.SetAttribute("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldSetEmptyString_WhenSettingAttributeWithNullValue()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act
        entity.SetAttribute("key", null!);

        // Assert
        Assert.Equal(string.Empty, entity.Attributes["key"]);
    }

    [Fact]
    public void ShouldUpdateLastUpdated_WhenSettingAttribute()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");
        var originalLastUpdated = entity.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(originalLastUpdated);

        // Act
        entity.SetAttribute("key", "value");

        // Assert
        Assert.True(entity.LastUpdated > originalLastUpdated);
    }

    [Fact]
    public void ShouldMaintainAll_WhenSettingAttributeWithMultipleAttributes()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act
        entity.SetAttribute("attr1", "value1");
        entity.SetAttribute("attr2", "value2");
        entity.SetAttribute("attr3", "value3");

        // Assert
        Assert.Equal(3, entity.Attributes.Count);
        Assert.Equal("value1", entity.Attributes["attr1"]);
        Assert.Equal("value2", entity.Attributes["attr2"]);
        Assert.Equal("value3", entity.Attributes["attr3"]);
    }

    #endregion

    #region AddRelationship Tests

    [Fact]
    public void ShouldAdd_WhenAddingRelationshipWithValidRelationship()
    {
        // Arrange
        var entity = new EntityMemory("Person1", "person");
        var relationship = new EntityRelationship("Person2", "knows");

        // Act
        entity.AddRelationship(relationship);

        // Assert
        Assert.Single(entity.Relationships);
        Assert.Contains(relationship, entity.Relationships);
    }

    [Fact]
    public void ShouldThrow_WhenAddingRelationshipWithNullRelationship()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            entity.AddRelationship(null!));
        Assert.Equal("relationship", exception.ParamName);
    }

    [Fact]
    public void ShouldUpdateLastUpdated_WhenAddingRelationship()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");
        var originalLastUpdated = entity.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(originalLastUpdated);
        var relationship = new EntityRelationship("Other", "related_to");

        // Act
        entity.AddRelationship(relationship);

        // Assert
        Assert.True(entity.LastUpdated > originalLastUpdated);
    }

    [Fact]
    public void ShouldMaintainAll_WhenAddingRelationshipWithMultipleRelationships()
    {
        // Arrange
        var entity = new EntityMemory("Company", "organization");
        var relationships = new[]
        {
            new EntityRelationship("CEO", "has_employee", "Chief Executive Officer"),
            new EntityRelationship("CTO", "has_employee", "Chief Technology Officer"),
            new EntityRelationship("Industry", "operates_in", "Technology sector")
        };

        // Act
        foreach (var rel in relationships)
        {
            entity.AddRelationship(rel);
        }

        // Assert
        Assert.Equal(3, entity.Relationships.Count);
        Assert.All(relationships, rel => Assert.Contains(rel, entity.Relationships));
    }

    #endregion

    #region UpdateConfidence Tests

    [Fact]
    public void ShouldUpdate_WhenUpdatingConfidenceWithValidValue()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type", confidence: 0.5f);

        // Act
        entity.UpdateConfidence(0.9f);

        // Assert
        Assert.Equal(0.9f, entity.Confidence);
    }

    [Fact]
    public void ShouldUpdate_WhenUpdatingConfidenceWithZero()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act
        entity.UpdateConfidence(0f);

        // Assert
        Assert.Equal(0f, entity.Confidence);
    }

    [Fact]
    public void ShouldUpdate_WhenUpdatingConfidenceWithOne()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act
        entity.UpdateConfidence(1f);

        // Assert
        Assert.Equal(1f, entity.Confidence);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingConfidenceWithNegativeValue()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            entity.UpdateConfidence(-0.1f));
        Assert.Contains("Confidence must be between 0 and 1", exception.Message);
        Assert.Equal("confidence", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingConfidenceWithValueGreaterThanOne()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            entity.UpdateConfidence(1.01f));
        Assert.Contains("Confidence must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void ShouldUpdateLastUpdated_WhenUpdatingConfidence()
    {
        // Arrange
        var entity = new EntityMemory("Test", "type");
        var originalLastUpdated = entity.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(originalLastUpdated);

        // Act
        entity.UpdateConfidence(0.95f);

        // Assert
        Assert.True(entity.LastUpdated > originalLastUpdated);
    }

    #endregion

    #region EntityRelationship Tests

    [Fact]
    public void ShouldCreate_WhenUsingEntityRelationshipConstructorWithValidParameters()
    {
        // Arrange
        var relatedEntity = "Related Entity";
        var relationType = "works_with";
        var context = "Collaborated on Project X";

        // Act
        var relationship = new EntityRelationship(relatedEntity, relationType, context);

        // Assert
        Assert.Equal(relatedEntity, relationship.RelatedEntityName);
        Assert.Equal(relationType, relationship.RelationType);
        Assert.Equal(context, relationship.Context);
        Assert.True(relationship.EstablishedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldCreateWithNullContext_WhenUsingEntityRelationshipConstructorWithoutContext()
    {
        // Act
        var relationship = new EntityRelationship("Entity", "type");

        // Assert
        Assert.Null(relationship.Context);
    }

    [Fact]
    public void ShouldThrow_WhenUsingEntityRelationshipConstructorWithEmptyRelatedEntityName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityRelationship("", "type"));
        Assert.Equal("relatedEntityName", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenUsingEntityRelationshipConstructorWithWhitespaceRelatedEntityName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityRelationship("   ", "type"));
    }

    [Fact]
    public void ShouldThrow_WhenUsingEntityRelationshipConstructorWithEmptyRelationType()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new EntityRelationship("Entity", ""));
        Assert.Equal("relationType", exception.ParamName);
    }

    [Fact]
    public void ShouldSetEstablishedAtToCurrentTime_WhenUsingEntityRelationshipConstructor()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var relationship = new EntityRelationship("Entity", "type");
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(relationship.EstablishedAt >= before);
        Assert.True(relationship.EstablishedAt <= after);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldPersonEntityWithFullProfile_WhenUsingComplexScenario()
    {
        // Arrange & Act
        var person = new EntityMemory("Alice Johnson", "person", "Senior Software Engineer at TechCorp", 0.95f);

        // Add personal attributes
        person.SetAttribute("email", "alice.johnson@techcorp.com");
        person.SetAttribute("phone", "+1-555-0123");
        person.SetAttribute("location", "San Francisco, CA");
        person.SetAttribute("department", "Engineering");
        person.SetAttribute("expertise", "Machine Learning, Python, Cloud Architecture");
        person.SetAttribute("years_experience", "10");

        // Add relationships
        person.AddRelationship(new EntityRelationship("Bob Smith", "reports_to", "Direct manager"));
        person.AddRelationship(new EntityRelationship("TechCorp", "works_for", "Full-time employee since 2019"));
        person.AddRelationship(new EntityRelationship("ML Team", "member_of", "Lead ML Engineer"));
        person.AddRelationship(new EntityRelationship("Charlie Davis", "mentors", "Junior developer mentorship"));

        // Update confidence after verification
        person.UpdateConfidence(0.99f);

        // Assert
        Assert.Equal("Alice Johnson", person.Name);
        Assert.Equal("person", person.Type);
        Assert.Equal(6, person.Attributes.Count);
        Assert.Equal(4, person.Relationships.Count);
        Assert.Equal(0.99f, person.Confidence);

        // Verify specific details
        Assert.Equal("alice.johnson@techcorp.com", person.Attributes["email"]);
        Assert.Equal("10", person.Attributes["years_experience"]);

        var managerRel = person.Relationships.First(r => r.RelationType == "reports_to");
        Assert.Equal("Bob Smith", managerRel.RelatedEntityName);
        Assert.Equal("Direct manager", managerRel.Context);
    }

    [Fact]
    public void ShouldOrganizationEntityEvolution_WhenUsingComplexScenario()
    {
        // Arrange - Create startup
        var startup = new EntityMemory("InnovateTech", "organization", "AI startup focused on NLP", 0.7f);
        startup.SetAttribute("founded", "2020");
        startup.SetAttribute("employees", "5");
        startup.SetAttribute("funding_stage", "Pre-seed");
        startup.SetAttribute("location", "Garage in Palo Alto");

        var initialTimestamp = startup.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(initialTimestamp);

        // Act - Series A funding
        startup.UpdateDescription("AI startup specializing in enterprise NLP solutions");
        startup.SetAttribute("employees", "25");
        startup.SetAttribute("funding_stage", "Series A");
        startup.SetAttribute("funding_amount", "$5M");
        startup.SetAttribute("location", "Office in San Francisco");
        startup.AddRelationship(new EntityRelationship("VentureCapital Inc", "funded_by", "$5M Series A"));
        startup.UpdateConfidence(0.85f);

        var seriesATimestamp = startup.LastUpdated;
        ClockAdvance.UntilStrictlyAfter(seriesATimestamp);

        // Act - Growth phase
        startup.SetAttribute("employees", "120");
        startup.SetAttribute("funding_stage", "Series B");
        startup.SetAttribute("funding_amount", "$25M");
        startup.SetAttribute("revenue", "$10M ARR");
        startup.AddRelationship(new EntityRelationship("BigTech Corp", "partnership_with", "Strategic technology partner"));
        startup.AddRelationship(new EntityRelationship("Enterprise Client 1", "customer_of", "Major enterprise deployment"));
        startup.UpdateConfidence(0.95f);

        // Assert
        Assert.Equal("InnovateTech", startup.Name);
        Assert.Equal("120", startup.Attributes["employees"]);
        Assert.Equal("Series B", startup.Attributes["funding_stage"]);
        Assert.Equal(3, startup.Relationships.Count);
        Assert.Equal(0.95f, startup.Confidence);

        // Verify timestamps show progression
        Assert.True(seriesATimestamp > initialTimestamp);
        Assert.True(startup.LastUpdated > seriesATimestamp);
        // FirstEncountered is captured once at construction and must never drift
        // forward as the entity evolves: it stays at/just-before the initial phase
        // and strictly before every later phase. (A sub-millisecond wall-clock
        // tolerance against initialTimestamp was flaky: the 4 SetAttribute calls
        // between construction and initialTimestamp can straddle a ms boundary.)
        Assert.True(startup.FirstEncountered <= initialTimestamp);
        Assert.True(startup.FirstEncountered < seriesATimestamp);
    }

    [Fact]
    public void ShouldConceptEntityWithHierarchy_WhenUsingComplexScenario()
    {
        // Arrange & Act
        var mlConcept = new EntityMemory("Machine Learning", "concept",
            "Branch of AI that enables systems to learn from data", 0.9f);

        // Add attributes defining the concept
        mlConcept.SetAttribute("category", "Artificial Intelligence");
        mlConcept.SetAttribute("complexity", "High");
        mlConcept.SetAttribute("prerequisites", "Statistics, Linear Algebra, Calculus");
        mlConcept.SetAttribute("applications", "Computer Vision, NLP, Recommendation Systems");
        mlConcept.SetAttribute("key_algorithms", "Neural Networks, SVM, Random Forests, Gradient Boosting");

        // Add relationships to related concepts
        mlConcept.AddRelationship(new EntityRelationship("Artificial Intelligence", "subset_of",
            "ML is a subset of AI"));
        mlConcept.AddRelationship(new EntityRelationship("Deep Learning", "parent_of",
            "DL is a subset of ML"));
        mlConcept.AddRelationship(new EntityRelationship("Statistics", "depends_on",
            "Statistical foundations required"));
        mlConcept.AddRelationship(new EntityRelationship("Data Science", "used_in",
            "Core technique in data science"));
        mlConcept.AddRelationship(new EntityRelationship("Neural Networks", "includes",
            "Type of ML algorithm"));

        // Assert
        Assert.Equal("Machine Learning", mlConcept.Name);
        Assert.Equal("concept", mlConcept.Type);
        Assert.Equal(5, mlConcept.Attributes.Count);
        Assert.Equal(5, mlConcept.Relationships.Count);

        // Verify concept hierarchy
        var parentRel = mlConcept.Relationships.First(r => r.RelationType == "subset_of");
        Assert.Equal("Artificial Intelligence", parentRel.RelatedEntityName);

        var childRel = mlConcept.Relationships.First(r => r.RelationType == "parent_of");
        Assert.Equal("Deep Learning", childRel.RelatedEntityName);
    }

    #endregion
}
