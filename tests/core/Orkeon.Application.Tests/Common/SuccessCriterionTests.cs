using Orkeon.Application.Evaluation;

namespace Orkeon.Application.Tests.Common;

public class SuccessCriterionTests
{
    [Fact]
    public void ShouldSetAllValues_WhenConstructingWithAllParameters()
    {
        // Arrange
        var name = "Accuracy Criterion";
        var description = "Model accuracy must be above 95%";
        Func<object, bool> evaluator = result => (double)result > 0.95;
        var weight = 2.5;

        // Act
        var criterion = new SuccessCriterion(name, description, evaluator, weight);

        // Assert
        Assert.Equal("Accuracy Criterion", criterion.Name);
        Assert.Equal("Model accuracy must be above 95%", criterion.Description);
        Assert.NotNull(criterion.Evaluator);
        Assert.Equal(2.5, criterion.Weight);
    }

    [Fact]
    public void ShouldSetWeightToOne_WhenConstructingWithDefaultWeight()
    {
        // Arrange & Act
        var criterion = new SuccessCriterion(
            "Test Criterion",
            "Test description",
            result => true);

        // Assert
        Assert.Equal(1.0, criterion.Weight);
    }

    [Fact]
    public void ShouldCallEvaluator_WhenUsingIsMetWithValidInput()
    {
        // Arrange
        var evaluatorCalled = false;
        var criterion = new SuccessCriterion(
            "Test",
            "Description",
            result =>
            {
                evaluatorCalled = true;
                return result.Equals("success");
            });

        // Act
        var result1 = criterion.IsMet("success");
        var result2 = criterion.IsMet("failure");

        // Assert
        Assert.True(evaluatorCalled);
        Assert.True(result1);
        Assert.False(result2);
    }

    [Fact]
    public void ShouldCreateThresholdBasedCriterion_WhenUsingThreshold()
    {
        // Arrange & Act
        var criterion = SuccessCriterion.Threshold(
            "Performance",
            "Performance score must be at least 80%",
            0.8,
            result => (double)result);

        // Assert
        Assert.Equal("Performance", criterion.Name);
        Assert.Equal("Performance score must be at least 80%", criterion.Description);
        Assert.Equal(1.0, criterion.Weight);

        // Test the evaluator
        Assert.True(criterion.IsMet(0.85));
        Assert.True(criterion.IsMet(0.8));
        Assert.False(criterion.IsMet(0.79));
        Assert.False(criterion.IsMet(0.5));
    }

    [Fact]
    public void ShouldWork_WhenUsingThresholdWithComplexExtractor()
    {
        // Arrange
        var criterion = SuccessCriterion.Threshold(
            "Average Score",
            "Average must be >= 75",
            75.0,
            result =>
            {
                var scores = (List<double>)result;
                return scores.Average();
            });

        // Act & Assert
        Assert.True(criterion.IsMet(new List<double> { 80, 90, 70 })); // Avg = 80
        Assert.True(criterion.IsMet(new List<double> { 75, 75, 75 })); // Avg = 75
        Assert.False(criterion.IsMet(new List<double> { 60, 70, 80 })); // Avg = 70
    }

    [Fact]
    public void ShouldCreateBooleanCriterion_WhenUsingBoolean()
    {
        // Arrange & Act
        var criterion = SuccessCriterion.Boolean(
            "All Tests Pass",
            "All unit tests must pass",
            result =>
            {
                var testResults = (Dictionary<string, bool>)result;
                return testResults.All(kvp => kvp.Value);
            });

        // Assert
        Assert.Equal("All Tests Pass", criterion.Name);
        Assert.Equal("All unit tests must pass", criterion.Description);
        Assert.Equal(1.0, criterion.Weight);

        // Test the evaluator
        var allPass = new Dictionary<string, bool>
        {
            ["Test1"] = true,
            ["Test2"] = true,
            ["Test3"] = true
        };
        var somePass = new Dictionary<string, bool>
        {
            ["Test1"] = true,
            ["Test2"] = false,
            ["Test3"] = true
        };

        Assert.True(criterion.IsMet(allPass));
        Assert.False(criterion.IsMet(somePass));
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        Func<object, bool> evaluator = result => (int)result > 10;
        var criterion1 = new SuccessCriterion(
            "Count Check",
            "Count must be greater than 10",
            evaluator,
            1.5);
        var criterion2 = new SuccessCriterion(
            "Count Check",
            "Count must be greater than 10",
            evaluator,
            1.5);

        // Act & Assert
        Assert.Equal(criterion1, criterion2);
        Assert.True(criterion1 == criterion2);
        Assert.False(criterion1 != criterion2);
        Assert.Equal(criterion1.GetHashCode(), criterion2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var baseCriterion = new SuccessCriterion(
            "Base",
            "Base description",
            result => true,
            1.0);

        // Act & Assert - Different Name
        var differentName = new SuccessCriterion(
            "Different",
            "Base description",
            result => true,
            1.0);
        Assert.NotEqual(baseCriterion, differentName);

        // Different Description
        var differentDescription = new SuccessCriterion(
            "Base",
            "Different description",
            result => true,
            1.0);
        Assert.NotEqual(baseCriterion, differentDescription);

        // Different Evaluator
        var differentEvaluator = new SuccessCriterion(
            "Base",
            "Base description",
            result => false,
            1.0);
        Assert.NotEqual(baseCriterion, differentEvaluator);

        // Different Weight
        var differentWeight = new SuccessCriterion(
            "Base",
            "Base description",
            result => true,
            2.0);
        Assert.NotEqual(baseCriterion, differentWeight);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = new SuccessCriterion(
            "Original",
            "Original description",
            result => true,
            1.0);

        // Act
        var modified = original with
        {
            Name = "Modified",
            Weight = 2.0
        };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.Equal("Original", original.Name);
        Assert.Equal("Modified", modified.Name);
        Assert.Equal(1.0, original.Weight);
        Assert.Equal(2.0, modified.Weight);
        // Unchanged values
        Assert.Equal(original.Description, modified.Description);
        Assert.Equal(original.Evaluator, modified.Evaluator);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        Func<object, bool> evaluator = result => true;
        var criterion = new SuccessCriterion(
            "Test Name",
            "Test Description",
            evaluator,
            3.0);

        // Act
        var (name, description, eval, weight) = criterion;

        // Assert
        Assert.Equal("Test Name", name);
        Assert.Equal("Test Description", description);
        Assert.Equal(evaluator, eval);
        Assert.Equal(3.0, weight);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var criterion = new SuccessCriterion(
            "Completion Rate",
            "Task completion rate must be above 90%",
            result => true,
            1.5);

        // Act
        var result = criterion.ToString();

        // Assert
        Assert.Contains("Completion Rate", result);
        Assert.Contains("Task completion rate must be above 90%", result);
        Assert.Contains("1.5", result);
    }

    [Fact]
    public void ShouldMultipleCriteriaEvaluation_WhenUsingComplexScenario()
    {
        // Arrange - Define a set of criteria for a task evaluation
        var criteria = new List<SuccessCriterion>
        {
            SuccessCriterion.Threshold(
                "Accuracy",
                "Model accuracy >= 95%",
                0.95,
                result => ((TaskResult)result).Accuracy),

            SuccessCriterion.Boolean(
                "Speed",
                "Processing time <= 100ms",
                result => ((TaskResult)result).ProcessingTimeMs <= 100),

            SuccessCriterion.Boolean(
                "No Errors",
                "No errors during execution",
                result => !((TaskResult)result).HasErrors),

            new SuccessCriterion(
                "Data Quality",
                "Output data quality check",
                result => ((TaskResult)result).DataQualityScore > 0.8,
                2.0) // Higher weight for data quality
        };

        // Test case 1: All criteria met
        var successResult = new TaskResult
        {
            Accuracy = 0.96,
            ProcessingTimeMs = 80,
            HasErrors = false,
            DataQualityScore = 0.85
        };

        var metCriteria1 = criteria.Where(c => c.IsMet(successResult)).ToList();
        Assert.Equal(4, metCriteria1.Count);

        // Test case 2: Some criteria not met
        var partialResult = new TaskResult
        {
            Accuracy = 0.92,      // Below threshold
            ProcessingTimeMs = 120, // Above threshold
            HasErrors = false,
            DataQualityScore = 0.85
        };

        var metCriteria2 = criteria.Where(c => c.IsMet(partialResult)).ToList();
        Assert.Equal(2, metCriteria2.Count);
        Assert.Contains(metCriteria2, c => c.Name == "No Errors");
        Assert.Contains(metCriteria2, c => c.Name == "Data Quality");

        // Calculate weighted success score
        var totalWeight = criteria.Sum(c => c.Weight);
        var achievedWeight1 = metCriteria1.Sum(c => c.Weight);
        var achievedWeight2 = metCriteria2.Sum(c => c.Weight);

        var successScore1 = achievedWeight1 / totalWeight;
        var successScore2 = achievedWeight2 / totalWeight;

        Assert.Equal(1.0, successScore1); // 100% success
        Assert.True(successScore2 < 1.0); // Partial success
        Assert.True(successScore2 > 0.5); // But still > 50% due to higher weight on data quality
    }

    [Fact]
    public void ShouldNullAndExceptionHandling_WhenUsingEdgeCases()
    {
        // Arrange
        var criterion = new SuccessCriterion(
            "Null Safe",
            "Should handle null",
            result => result != null);

        // Act & Assert - Null handling
        Assert.False(criterion.IsMet(null!));
        Assert.True(criterion.IsMet("not null"));

        // Exception handling in evaluator
        var exceptionCriterion = new SuccessCriterion(
            "Exception Test",
            "May throw exception",
            result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return true;
            });

        Assert.Throws<ArgumentNullException>(() => exceptionCriterion.IsMet(null!));
        Assert.True(exceptionCriterion.IsMet("valid"));
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingChainedCriteria()
    {
        // Arrange - Create criteria that depend on each other
        var primaryCriterion = SuccessCriterion.Threshold(
            "Primary Score",
            "Primary score >= 70",
            70,
            result => ((ComplexResult)result).PrimaryScore);

        var secondaryCriterion = new SuccessCriterion(
            "Secondary Check",
            "Secondary check only if primary passes",
            result =>
            {
                var complexResult = (ComplexResult)result;
                // Only check secondary if primary would pass
                if (complexResult.PrimaryScore >= 70)
                    return complexResult.SecondaryScore >= 80;
                return true; // Don't fail on secondary if primary fails
            });

        // Test cases
        var highBothScores = new ComplexResult { PrimaryScore = 85, SecondaryScore = 90 };
        var highPrimaryLowSecondary = new ComplexResult { PrimaryScore = 75, SecondaryScore = 60 };
        var lowPrimaryHighSecondary = new ComplexResult { PrimaryScore = 65, SecondaryScore = 95 };

        // Assert
        Assert.True(primaryCriterion.IsMet(highBothScores));
        Assert.True(secondaryCriterion.IsMet(highBothScores));

        Assert.True(primaryCriterion.IsMet(highPrimaryLowSecondary));
        Assert.False(secondaryCriterion.IsMet(highPrimaryLowSecondary));

        Assert.False(primaryCriterion.IsMet(lowPrimaryHighSecondary));
        Assert.True(secondaryCriterion.IsMet(lowPrimaryHighSecondary)); // Passes because primary < 70
    }

    // Helper classes for complex scenarios
    private class TaskResult
    {
        public double Accuracy { get; set; }
        public int ProcessingTimeMs { get; set; }
        public bool HasErrors { get; set; }
        public double DataQualityScore { get; set; }
    }

    private class ComplexResult
    {
        public double PrimaryScore { get; set; }
        public double SecondaryScore { get; set; }
    }
}
