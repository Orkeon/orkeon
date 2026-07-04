using Orkeon.Domain.Configuration;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;
namespace Orkeon.Domain.Tests.Configuration;

public class RollbackOptionsTests
{
    private static readonly string[] ApiWebWorkerServices = ["api", "web", "worker"];
    private static readonly string[] RollbackSteps = ["Stop services", "Backup current", "Apply rollback", "Start services"];
    private static readonly string[] ApiWorkerSchedulerServices = ["api", "worker", "scheduler"];
    private static readonly string[] CustomerTickets = ["SUP-123", "SUP-124", "SUP-125"];
    private static readonly string[] ApprovalChain = ["dev-lead", "ops-manager", "cto"];

    #region RollbackOptions Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingRollbackOptionsWithDefaultConstructor()
    {
        var options = new RollbackOptions();
        Assert.NotNull(options.TargetVersionId);
        Assert.True(options.CreateBackup);
        Assert.True(options.ValidateBeforeRollback);
        Assert.Null(options.Comment);
        Assert.NotNull(options.Metadata);
        Assert.Empty(options.Metadata);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRollbackOptionsUsingProperties()
    {
        var metadata = new Dictionary<string, object>
        {
            { "reason", "Bug in production" },
            { "authorizedBy", "admin@company.com" },
            { "ticketNumber", "JIRA-1234" }
        };

        var options = new RollbackOptions
        {
            TargetVersionId = ConfigurationVersionId.Create(),
            CreateBackup = false,
            ValidateBeforeRollback = false,
            Comment = "Rolling back due to critical bug",
            Metadata = metadata
        };

        Assert.NotNull(options.TargetVersionId);
        Assert.False(options.CreateBackup);
        Assert.False(options.ValidateBeforeRollback);
        Assert.Equal("Rolling back due to critical bug", options.Comment);
        Assert.Equal(metadata, options.Metadata);
        Assert.Equal(3, options.Metadata.Count);
    }

    [Fact]
    public void ShouldAcceptTypedId_WhenUsingRollbackOptionsUsingTargetVersionId()
    {
        var versionId = ConfigurationVersionId.Create();
        var options = new RollbackOptions { TargetVersionId = versionId };
        Assert.Equal(versionId, options.TargetVersionId);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldAcceptValues_WhenUsingRollbackOptionsUsingBooleanFlags(bool createBackup, bool validate)
    {
        var options = new RollbackOptions { CreateBackup = createBackup, ValidateBeforeRollback = validate };
        Assert.Equal(createBackup, options.CreateBackup);
        Assert.Equal(validate, options.ValidateBeforeRollback);
    }

    #endregion

    #region RollbackOptions Metadata Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingRollbackOptionsUsingMetadata()
    {
        var options = new RollbackOptions
        {
            Metadata = new Dictionary<string, object>
            {
                { "environment", "production" },
                { "priority", "high" },
                { "timestamp", DateTime.UtcNow },
                { "affectedServices", ApiWebWorkerServices }
            }
        };
        Assert.Equal(4, options.Metadata.Count);
        Assert.Equal("production", options.Metadata["environment"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingRollbackOptionsUsingMetadataWithComplexObjects()
    {
        var rollbackPlan = new { Steps = RollbackSteps, EstimatedDuration = TimeoutLong, RequiredApprovals = 2 };
        var options = new RollbackOptions
        {
            Metadata = new Dictionary<string, object>
            {
                { "plan", rollbackPlan },
                { "riskLevel", 3 },
                { "automaticRollback", false }
            }
        };
        Assert.Equal(3, options.Metadata.Count);
        Assert.Equal(rollbackPlan, options.Metadata["plan"]);
    }

    #endregion

    #region RollbackResult Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingRollbackResultWithDefaultConstructor()
    {
        var beforeCreation = DateTime.UtcNow;
        var result = new RollbackResult();
        var afterCreation = DateTime.UtcNow;
        Assert.False(result.Success);
        Assert.Null(result.Error);
        Assert.Null(result.BackupVersionId);
        Assert.NotNull(result.CurrentVersionId);
        Assert.NotNull(result.PreviousVersionId);
        Assert.True(result.RolledBackAt >= beforeCreation);
        Assert.True(result.RolledBackAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.RolledBackAt.Kind);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingRollbackResultUsingProperties()
    {
        var rolledBackAt = DateTime.UtcNow.AddMinutes(-5);
        var result = new RollbackResult
        {
            Success = true,
            Error = "Some error occurred",
            BackupVersionId = ConfigurationVersionId.Create(),
            CurrentVersionId = ConfigurationVersionId.Create(),
            PreviousVersionId = ConfigurationVersionId.Create(),
            RolledBackAt = rolledBackAt
        };
        Assert.True(result.Success);
        Assert.Equal("Some error occurred", result.Error);
        Assert.NotNull(result.BackupVersionId);
        Assert.NotNull(result.CurrentVersionId);
        Assert.NotNull(result.PreviousVersionId);
        Assert.Equal(rolledBackAt, result.RolledBackAt);
    }

    #endregion

    #region RollbackResult Static Factory Methods Tests

    [Fact]
    public void ShouldCreateSuccessResult_WhenUsingRollbackResultCreatingSuccessWithBackup()
    {
        var currentId = ConfigurationVersionId.Create();
        var previousId = ConfigurationVersionId.Create();
        var backupId = ConfigurationVersionId.Create();
        var result = RollbackResult.CreateSuccess(currentId, previousId, backupId);
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(currentId, result.CurrentVersionId);
        Assert.Equal(previousId, result.PreviousVersionId);
        Assert.Equal(backupId, result.BackupVersionId);
    }

    [Fact]
    public void ShouldCreateSuccessResult_WhenUsingRollbackResultCreatingSuccessWithoutBackup()
    {
        var result = RollbackResult.CreateSuccess(ConfigurationVersionId.Create(), ConfigurationVersionId.Create());
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Null(result.BackupVersionId);
    }

    [Fact]
    public void ShouldCreateFailureResult_WhenUsingRollbackResultCreatingFailure()
    {
        var result = RollbackResult.CreateFailure("Unable to connect to configuration server");
        Assert.False(result.Success);
        Assert.Equal("Unable to connect to configuration server", result.Error);
        Assert.NotNull(result.CurrentVersionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple error")]
    [InlineData("Detailed error: Configuration file not found at /etc/orkeon/config.json")]
    [InlineData("Multi-line\nerror\nmessage")]
    public void ShouldAcceptAll_WhenUsingRollbackResultCreatingFailureWithVariousErrors(string error)
    {
        var result = RollbackResult.CreateFailure(error);
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
    }

    #endregion

    #region RollbackResult DateTime Tests

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingRollbackResultUsingRolledBackAt()
    {
        var beforeCreation = DateTime.UtcNow;
        var result = new RollbackResult();
        var afterCreation = DateTime.UtcNow;
        Assert.True(result.RolledBackAt >= beforeCreation);
        Assert.True(result.RolledBackAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.RolledBackAt.Kind);
    }

    [Fact]
    public void ShouldHaveDifferentTimestamps_WhenUsingRollbackResultWithMultipleInstances()
    {
        var result1 = new RollbackResult();
        ClockAdvance.UntilStrictlyAfter(result1.RolledBackAt);
        var result2 = new RollbackResult();
        Assert.True(result2.RolledBackAt >= result1.RolledBackAt);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldSuccessfulRollbackWithBackup_WhenUsingRollbackScenario()
    {
        var targetVersionId = ConfigurationVersionId.Create();
        var options = new RollbackOptions
        {
            TargetVersionId = targetVersionId,
            CreateBackup = true,
            ValidateBeforeRollback = true,
            Comment = "Rolling back due to performance issues",
            Metadata = new Dictionary<string, object> { { "issueId", "PERF-789" }, { "approvedBy", "tech-lead@company.com" } }
        };
        var result = RollbackResult.CreateSuccess(targetVersionId, ConfigurationVersionId.Create(), ConfigurationVersionId.Create());
        Assert.True(options.CreateBackup);
        Assert.True(result.Success);
        Assert.Equal(options.TargetVersionId, result.CurrentVersionId);
    }

    [Fact]
    public void ShouldFailedRollbackDueToValidation_WhenUsingRollbackScenario()
    {
        var options = new RollbackOptions { TargetVersionId = ConfigurationVersionId.Create(), CreateBackup = true, ValidateBeforeRollback = true, Comment = "Emergency rollback" };
        var result = RollbackResult.CreateFailure("Validation failed: Target version v1.0.0 is missing required configuration keys");
        Assert.True(options.ValidateBeforeRollback);
        Assert.False(result.Success);
        Assert.Contains("Validation failed", result.Error);
    }

    [Fact]
    public void ShouldQuickRollbackWithoutBackup_WhenUsingRollbackScenario()
    {
        var options = new RollbackOptions { TargetVersionId = ConfigurationVersionId.Create(), CreateBackup = false, ValidateBeforeRollback = false, Comment = "Emergency rollback - system down" };
        var result = RollbackResult.CreateSuccess(ConfigurationVersionId.Create(), ConfigurationVersionId.Create());
        Assert.False(options.CreateBackup);
        Assert.True(result.Success);
        Assert.Null(result.BackupVersionId);
    }

    [Fact]
    public void ShouldContainAllNecessaryInfo_WhenUsingRollbackOptionsWithCompleteConfiguration()
    {
        var options = new RollbackOptions
        {
            TargetVersionId = ConfigurationVersionId.Create(),
            CreateBackup = true,
            ValidateBeforeRollback = true,
            Comment = "Rolling back due to customer-reported issues with new feature X",
            Metadata = new Dictionary<string, object>
            {
                { "environment", "production" }, { "region", "us-east-1" },
                { "affectedServices", ApiWorkerSchedulerServices }, { "customerTickets", CustomerTickets },
                { "rollbackWindow", new { start = "02:00", end = "04:00", timezone = "UTC" } },
                { "approvalChain", ApprovalChain }
            }
        };
        Assert.NotNull(options.TargetVersionId);
        Assert.Equal(6, options.Metadata.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingRollbackOptionsWithNullMetadata()
    {
        var options = new RollbackOptions { Metadata = null! };
        Assert.Null(options.Metadata);
    }

    [Fact]
    public void ShouldAccept_WhenUsingRollbackOptionsWithNullTargetVersionId()
    {
        var options = new RollbackOptions { TargetVersionId = null! };
        Assert.Null(options.TargetVersionId);
    }

    [Fact]
    public void ShouldAccept_WhenUsingRollbackResultWithNullVersionIds()
    {
        var result = new RollbackResult { CurrentVersionId = null!, PreviousVersionId = null! };
        Assert.Null(result.CurrentVersionId);
        Assert.Null(result.PreviousVersionId);
    }

    [Fact]
    public void ShouldAccept_WhenUsingRollbackResultWithPastDateTime()
    {
        var pastDate = DateTime.UtcNow.AddDays(-30);
        var result = new RollbackResult { RolledBackAt = pastDate };
        Assert.Equal(pastDate, result.RolledBackAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingRollbackOptionsWithUnicodeContent()
    {
        var options = new RollbackOptions
        {
            TargetVersionId = ConfigurationVersionId.Create(),
            Comment = "回滚到稳定版本",
            Metadata = new Dictionary<string, object> { { "原因", "性能问题" } }
        };
        Assert.NotNull(options.TargetVersionId);
    }

    [Fact]
    public void ShouldAccept_WhenUsingRollbackResultCreatingFailureWithNullError()
    {
        var result = RollbackResult.CreateFailure(null!);
        Assert.False(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingRollbackOptionsToString()
    {
        var options = new RollbackOptions { TargetVersionId = ConfigurationVersionId.Create() };
        var s = options.ToString();
        Assert.NotNull(s);
        Assert.NotEmpty(s);
        Assert.Contains("RollbackOptions", s);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingRollbackResultToString()
    {
        var result = RollbackResult.CreateSuccess(ConfigurationVersionId.Create(), ConfigurationVersionId.Create());
        var s = result.ToString();
        Assert.NotNull(s);
        Assert.NotEmpty(s);
        Assert.Contains("RollbackResult", s);
    }

    #endregion
}
