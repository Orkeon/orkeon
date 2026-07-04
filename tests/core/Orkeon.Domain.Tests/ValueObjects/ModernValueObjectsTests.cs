using Orkeon.Domain.SharedKernel.ValueObjects;
using CrewVersion = Orkeon.Domain.SharedKernel.ValueObjects.SemanticVersion;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ModernValueObjectsTests
{
    #region EmailAddress Tests

    [Fact]
    public void ShouldCreate_WhenUsingEmailAddressWithValidEmail()
    {
        // Act
        var email = EmailAddress.From("user@example.com");

        // Assert
        Assert.Equal("user@example.com", email.Value);
        Assert.Equal("example.com", email.Domain);
        Assert.Equal("user", email.LocalPart);
    }

    [Fact]
    public void ShouldNormalize_WhenUsingEmailAddressUppercaseEmail()
    {
        // Act
        var email = EmailAddress.From("USER@EXAMPLE.COM");

        // Assert
        Assert.Equal("user@example.com", email.Value);
    }

    [Fact]
    public void ShouldTrim_WhenUsingEmailAddressWithSpaces()
    {
        // Act
        var email = EmailAddress.From("  user@example.com  ");

        // Assert
        Assert.Equal("user@example.com", email.Value);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingEmailAddressWithInvalidFormat()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailAddress.From("invalid"));
        Assert.Throws<ArgumentException>(() => EmailAddress.From("@example.com"));
        Assert.Throws<ArgumentException>(() => EmailAddress.From("user@"));
        Assert.Throws<ArgumentException>(() => EmailAddress.From("user @example.com"));
        Assert.Throws<ArgumentException>(() => EmailAddress.From("user@example"));
    }

    [Fact]
    public void ShouldThrowException_WhenUsingEmailAddressWithEmptyOrNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailAddress.From(""));
        Assert.Throws<ArgumentException>(() => EmailAddress.From(null!));
    }

    [Fact]
    public void ShouldThrowException_WhenUsingEmailAddressTooLong()
    {
        // Arrange
        var longEmail = new string('a', 250) + "@example.com";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => EmailAddress.From(longEmail));
        Assert.Contains("too long", exception.Message);
    }

    [Fact]
    public void ShouldCreate_WhenUsingEmailAddressStaticFrom()
    {
        // Act
        var email = EmailAddress.From("test@example.com");

        // Assert
        Assert.Equal("test@example.com", email.Value);
    }

    [Fact]
    public void ShouldWork_WhenUsingEmailAddressImplicitStringConversion()
    {
        // Arrange
        var email = EmailAddress.From("user@example.com");

        // Act
        string value = email;

        // Assert
        Assert.Equal("user@example.com", value);
    }

    #endregion

    #region Url Tests

    [Fact]
    public void ShouldCreate_WhenUsingUrlWithValidUrl()
    {
        // Act
        var url = Url.From("https://example.com/path?query=value");

        // Assert
        Assert.Equal("https://example.com/path?query=value", url.Value);
        Assert.Equal("https", url.Scheme);
        Assert.Equal("example.com", url.Host);
        Assert.Equal(443, url.Port);
        Assert.Equal("/path", url.Path);
        Assert.True(url.IsHttps);
        Assert.True(url.IsSecure);
    }

    [Fact]
    public void ShouldNotBeSecure_WhenUsingUrlHttpUrl()
    {
        // Act
        var url = Url.From("http://example.com:8080");

        // Assert
        Assert.Equal("http", url.Scheme);
        Assert.Equal(8080, url.Port);
        Assert.False(url.IsHttps);
        Assert.False(url.IsSecure);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingUrlWithInvalidFormat()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Url.From("not a url"));
        Assert.Throws<ArgumentException>(() => Url.From("//example.com"));
        Assert.Throws<ArgumentException>(() => Url.From("example.com"));
    }

    [Fact]
    public void ShouldThrowException_WhenUsingUrlWithEmptyOrNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Url.From(""));
        Assert.Throws<ArgumentException>(() => Url.From(null!));
        Assert.Throws<ArgumentException>(() => Url.From("   "));
    }

    [Fact]
    public void ShouldWork_WhenUsingUrlImplicitConversions()
    {
        // Arrange
        var url = Url.From(TestBaseUrl);

        // Act
        string stringValue = url;
        Uri uriValue = url;

        // Assert
        Assert.Equal(TestBaseUrl, stringValue);
        Assert.Equal(new Uri(TestBaseUrl), uriValue);
    }

    [Fact]
    public void ShouldCreate_WhenUsingUrlStaticFrom()
    {
        // Act
        var url = Url.From(TestBaseUrl);

        // Assert
        Assert.Equal(TestBaseUrl, url.Value);
    }

    #endregion

    #region Money Tests

    [Fact]
    public void ShouldCreate_WhenUsingMoneyWithValidAmount()
    {
        // Act
        var money = Money.Create(100.50m, "USD");

        // Assert
        Assert.Equal(100.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void ShouldBeUSD_WhenUsingMoneyWithDefaultCurrency()
    {
        // Act
        var money = Money.Create(50m);

        // Assert
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingMoneyWithNegativeAmount()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => Money.Create(-10m));
        Assert.Contains("negative", exception.Message);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingMoneyWithInvalidCurrency()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Money.Create(100m, ""));
        Assert.Throws<ArgumentException>(() => Money.Create(100m, null!));
        Assert.Throws<ArgumentException>(() => Money.Create(100m, "US"));
        Assert.Throws<ArgumentException>(() => Money.Create(100m, "USDD"));
        Assert.Throws<ArgumentException>(() => Money.Create(100m, "123"));
    }

    [Fact]
    public void ShouldNormalize_WhenUsingMoneyLowercaseCurrency()
    {
        // Act
        var money = Money.Create(100m, "eur");

        // Assert
        Assert.Equal("EUR", money.Currency);
    }

    [Fact]
    public void ShouldWork_WhenUsingMoneyAddSameCurrency()
    {
        // Arrange
        var money1 = Money.Create(100m, "USD");
        var money2 = Money.Create(50.50m, "USD");

        // Act
        var result = money1.Add(money2);

        // Assert
        Assert.Equal(150.50m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingMoneyAddDifferentCurrency()
    {
        // Arrange
        var usd = Money.Create(100m, "USD");
        var eur = Money.Create(50m, "EUR");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => usd.Add(eur));
        Assert.Contains("different currencies", exception.Message);
    }

    [Fact]
    public void ShouldWork_WhenUsingMoneySubtractSameCurrency()
    {
        // Arrange
        var money1 = Money.Create(100m, "USD");
        var money2 = Money.Create(30m, "USD");

        // Act
        var result = money1.Subtract(money2);

        // Assert
        Assert.Equal(70m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ShouldWork_WhenUsingMoneyMultiply()
    {
        // Arrange
        var money = Money.Create(100m, "USD");

        // Act
        var result = money.Multiply(1.5m);

        // Assert
        Assert.Equal(150m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ShouldWork_WhenUsingMoneyDivide()
    {
        // Arrange
        var money = Money.Create(100m, "USD");

        // Act
        var result = money.Divide(4m);

        // Assert
        Assert.Equal(25m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void ShouldWork_WhenUsingMoneyStaticFactories()
    {
        // Act
        var zero = Money.Zero("EUR");
        var usd = Money.Usd(99.99m);
        var eur = Money.Eur(49.99m);

        // Assert
        Assert.Equal(0m, zero.Amount);
        Assert.Equal("EUR", zero.Currency);
        Assert.Equal(99.99m, usd.Amount);
        Assert.Equal("USD", usd.Currency);
        Assert.Equal(49.99m, eur.Amount);
        Assert.Equal("EUR", eur.Currency);
    }

    #endregion

    #region Percentage Tests

    [Fact]
    public void ShouldCreate_WhenUsingPercentageWithValidValue()
    {
        // Act
        var percentage = Percentage.From(75.5);

        // Assert
        Assert.Equal(75.5, percentage.Value);
        Assert.Equal(0.755, percentage.AsDecimal);
        Assert.Equal(0.755, percentage.AsRatio);
    }

    [Fact]
    public void ShouldRoundToTwoDecimalPlaces_WhenUsingPercentageValueRounding()
    {
        // Act
        var percentage = Percentage.From(75.556);

        // Assert
        Assert.Equal(75.56, percentage.Value);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingPercentageWithNegativeValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => Percentage.From(-1));
        Assert.Contains("negative", exception.Message);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingPercentageOverHundred()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => Percentage.From(100.1));
        Assert.Contains("exceed 100", exception.Message);
    }

    [Fact]
    public void ShouldWork_WhenUsingPercentageBoundaryValues()
    {
        // Act
        var zero = Percentage.From(0);
        var hundred = Percentage.From(100);

        // Assert
        Assert.Equal(0, zero.Value);
        Assert.Equal(0, zero.AsDecimal);
        Assert.Equal(100, hundred.Value);
        Assert.Equal(1.0, hundred.AsDecimal);
    }

    [Fact]
    public void ShouldConvert_WhenUsingPercentageFromDecimal()
    {
        // Act
        var percentage = Percentage.FromDecimal(0.85);

        // Assert
        Assert.Equal(85, percentage.Value);
    }

    [Fact]
    public void ShouldWork_WhenUsingPercentageImplicitConversion()
    {
        // Arrange
        var percentage = Percentage.From(65.5);

        // Act
        double value = percentage;

        // Assert
        Assert.Equal(65.5, value);
    }

    [Fact]
    public void ShouldFormat_WhenUsingPercentageToString()
    {
        // Act
        var percentage = Percentage.From(75.5);

        // Assert
        Assert.Equal("75.5%", percentage.ToString());
    }

    #endregion

    #region Version Tests

    [Fact]
    public void ShouldCreate_WhenUsingVersionWithValidValues()
    {
        // Act
        var version = CrewVersion.Create(1, 2, 3, "beta");

        // Assert
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("beta", version.PreRelease);
        Assert.True(version.IsPreRelease);
        Assert.False(version.IsStable);
    }

    [Fact]
    public void ShouldBeStable_WhenUsingVersionWithoutPreRelease()
    {
        // Act
        var version = CrewVersion.Create(1, 0, 0);

        // Assert
        Assert.Null(version.PreRelease);
        Assert.False(version.IsPreRelease);
        Assert.True(version.IsStable);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingVersionWithNegativeValues()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => CrewVersion.Create(-1, 0, 0));
        Assert.Throws<ArgumentException>(() => CrewVersion.Create(1, -1, 0));
        Assert.Throws<ArgumentException>(() => CrewVersion.Create(1, 0, -1));
    }

    [Fact]
    public void ShouldWork_WhenUsingVersionParseValidString()
    {
        // Act
        var version1 = CrewVersion.Parse("1.2.3");
        var version2 = CrewVersion.Parse("2.0.0-alpha");

        // Assert
        Assert.Equal(1, version1.Major);
        Assert.Equal(2, version1.Minor);
        Assert.Equal(3, version1.Patch);
        Assert.Null(version1.PreRelease);

        Assert.Equal(2, version2.Major);
        Assert.Equal(0, version2.Minor);
        Assert.Equal(0, version2.Patch);
        Assert.Equal("alpha", version2.PreRelease);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingVersionParseInvalidString()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => CrewVersion.Parse(""));
        Assert.Throws<ArgumentException>(() => CrewVersion.Parse("1.2"));
        Assert.Throws<ArgumentException>(() => CrewVersion.Parse("1.2.3.4"));
        Assert.Throws<ArgumentException>(() => CrewVersion.Parse("a.b.c"));
    }

    [Fact]
    public void ShouldWork_WhenUsingVersionIncrement()
    {
        // Arrange
        var version = CrewVersion.Create(1, 2, 3, "beta");

        // Act
        var major = version.IncrementMajor();
        var minor = version.IncrementMinor();
        var patch = version.IncrementPatch();

        // Assert
        Assert.Equal("2.0.0", major.ToString());
        Assert.Equal("1.3.0", minor.ToString());
        Assert.Equal("1.2.4", patch.ToString());

        // Pre-release is removed after increment
        Assert.Null(major.PreRelease);
        Assert.Null(minor.PreRelease);
        Assert.Null(patch.PreRelease);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingVersionCompareTo()
    {
        // Arrange
        var v1 = CrewVersion.Create(1, 0, 0);
        var v2 = CrewVersion.Create(1, 0, 1);
        var v3 = CrewVersion.Create(1, 1, 0);
        var v4 = CrewVersion.Create(2, 0, 0);
        var v5 = CrewVersion.Create(1, 0, 0, "alpha");

        // Assert
        Assert.True(v1.CompareTo(v2) < 0);
        Assert.True(v2.CompareTo(v3) < 0);
        Assert.True(v3.CompareTo(v4) < 0);
        Assert.True(v5.CompareTo(v1) < 0); // Pre-release is less than stable
        Assert.True(v1.CompareTo(null) > 0);
        Assert.Equal(0, v1.CompareTo(v1));
    }

    [Fact]
    public void ShouldWork_WhenUsingVersionComparisonOperators()
    {
        // Arrange
        var v1 = CrewVersion.Create(1, 0, 0);
        var v2 = CrewVersion.Create(2, 0, 0);

        // Assert
        Assert.True(v1 < v2);
        Assert.True(v2 > v1);
        Assert.True(v1 <= v2);
        Assert.True(v2 >= v1);
#pragma warning disable CS1718 // Comparison made to same variable
        Assert.True(v1 <= v1); // Self-comparison is intentional for testing reflexivity
        Assert.True(v1 >= v1); // Self-comparison is intentional for testing reflexivity
#pragma warning restore CS1718
    }

    [Fact]
    public void ShouldFormat_WhenUsingVersionToString()
    {
        // Assert
        Assert.Equal("1.2.3", CrewVersion.Create(1, 2, 3).ToString());
        Assert.Equal("1.2.3-beta", CrewVersion.Create(1, 2, 3, "beta").ToString());
    }

    #endregion

    #region ExecutionMetrics Tests

    [Fact]
    public void ShouldCreate_WhenUsingExecutionMetricsWithValidValues()
    {
        // Arrange
        var duration = TimeoutStandard;
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow;

        // Act
        var metrics = ExecutionMetrics.Create(duration, 10, 2, startTime, endTime);

        // Assert
        Assert.Equal(duration, metrics.Duration);
        Assert.Equal(10, metrics.SuccessCount);
        Assert.Equal(2, metrics.FailureCount);
        Assert.Equal(startTime, metrics.StartTime);
        Assert.Equal(endTime, metrics.EndTime);
        Assert.Equal(12, metrics.TotalCount);
        Assert.Equal(10.0 / 12.0, metrics.SuccessRate, 10); // Use 10 decimal places precision
        Assert.Equal(2.0 / 12.0, metrics.FailureRate, 10); // Use 10 decimal places precision
        Assert.True(metrics.IsCompleted);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingExecutionMetricsWithNegativeValues()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ExecutionMetrics.Create(TimeSpan.FromSeconds(-1), 0, 0, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => ExecutionMetrics.Create(TimeSpan.Zero, -1, 0, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => ExecutionMetrics.Create(TimeSpan.Zero, 0, -1, DateTime.UtcNow));
    }

    [Fact]
    public void ShouldCreateInitialMetrics_WhenUsingExecutionMetricsUsingStarted()
    {
        // Arrange
        var startTime = DateTime.UtcNow;

        // Act
        var metrics = ExecutionMetrics.Started(startTime);

        // Assert
        Assert.Equal(TimeSpan.Zero, metrics.Duration);
        Assert.Equal(0, metrics.SuccessCount);
        Assert.Equal(0, metrics.FailureCount);
        Assert.Equal(startTime, metrics.StartTime);
        Assert.Null(metrics.EndTime);
        Assert.False(metrics.IsCompleted);
    }

    [Fact]
    public void ShouldIncrement_WhenUsingExecutionMetricsAddingSuccess()
    {
        // Arrange
        var metrics = ExecutionMetrics.Started(DateTime.UtcNow);

        // Act
        var updated = metrics.AddSuccess().AddSuccess().AddSuccess();

        // Assert
        Assert.Equal(3, updated.SuccessCount);
        Assert.Equal(0, updated.FailureCount);
    }

    [Fact]
    public void ShouldIncrement_WhenUsingExecutionMetricsAddingFailure()
    {
        // Arrange
        var metrics = ExecutionMetrics.Started(DateTime.UtcNow);

        // Act
        var updated = metrics.AddFailure().AddFailure();

        // Assert
        Assert.Equal(0, updated.SuccessCount);
        Assert.Equal(2, updated.FailureCount);
    }

    [Fact]
    public void ShouldSetEndTimeAndDuration_WhenUsingExecutionMetricsWithComplete()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddSeconds(-30);
        var metrics = ExecutionMetrics.Started(startTime);
        var endTime = DateTime.UtcNow;

        // Act
        var completed = metrics.Complete(endTime);

        // Assert
        Assert.Equal(endTime, completed.EndTime);
        Assert.Equal(endTime - startTime, completed.Duration);
        Assert.True(completed.IsCompleted);
    }

    [Fact]
    public void ShouldBeZero_WhenUsingExecutionMetricsUsingRatesWithNoOperations()
    {
        // Arrange
        var metrics = ExecutionMetrics.Started(DateTime.UtcNow);

        // Assert
        Assert.Equal(0, metrics.TotalCount);
        Assert.Equal(0.0, metrics.SuccessRate);
        Assert.Equal(1.0, metrics.FailureRate);
    }

    #endregion

    #region ConfigurationKey Tests

    [Fact]
    public void ShouldCreate_WhenUsingConfigurationKeyWithValidKey()
    {
        // Act
        var key = ConfigurationKey.From("app.timeout", typeof(int));

        // Assert
        Assert.Equal("app.timeout", key.Value);
        Assert.Equal(typeof(int), key.ValueType);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingConfigurationKeyWithEmptyKey()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ConfigurationKey.From("", typeof(string)));
        Assert.Throws<ArgumentException>(() => ConfigurationKey.From(null!, typeof(string)));
        Assert.Throws<ArgumentException>(() => ConfigurationKey.From("   ", typeof(string)));
    }

    [Fact]
    public void ShouldThrowException_WhenUsingConfigurationKeyWithNullType()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ConfigurationKey.From("key", null!));
    }

    [Fact]
    public void ShouldWork_WhenUsingConfigurationKeyWithGenericCreate()
    {
        // Act
        var key = ConfigurationKey.Create<string>("app.name");

        // Assert
        Assert.Equal("app.name", key.Value);
        Assert.Equal(typeof(string), key.ValueType);
    }

    [Fact]
    public void ShouldFormat_WhenUsingConfigurationKeyToString()
    {
        // Act
        var key = ConfigurationKey.From("app.timeout", typeof(int));

        // Assert
        Assert.Equal("app.timeout (Int32)", key.ToString());
    }

    [Fact]
    public void ShouldWork_WhenUsingConfigurationKeyGenericImplicitConversion()
    {
        // Arrange
        var key = ConfigurationKey<int>.From("app.timeout");

        // Act
        string value = key;

        // Assert
        Assert.Equal("app.timeout", value);
    }

    #endregion

    #region ResourceUsage Tests

    [Fact]
    public void ShouldCreate_WhenUsingResourceUsageWithValidValues()
    {
        // Arrange
        var measuredAt = DateTime.UtcNow;

        // Act
        var usage = ResourceUsage.Create(1048576, TimeSpan.FromSeconds(10), 5, measuredAt);

        // Assert
        Assert.Equal(1048576, usage.MemoryBytes);
        Assert.Equal(TimeSpan.FromSeconds(10), usage.CpuTime);
        Assert.Equal(5, usage.ThreadCount);
        Assert.Equal(measuredAt, usage.MeasuredAt);
        Assert.Equal(1.0, usage.MemoryMB);
        Assert.Equal(1.0 / 1024.0, usage.MemoryGB);
    }

    [Fact]
    public void ShouldThrowException_WhenUsingResourceUsageWithNegativeValues()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ResourceUsage.Create(-1, TimeSpan.Zero, 0));
        Assert.Throws<ArgumentException>(() => ResourceUsage.Create(0, TimeSpan.FromSeconds(-1), 0));
        Assert.Throws<ArgumentException>(() => ResourceUsage.Create(0, TimeSpan.Zero, -1));
    }

    [Fact]
    public void ShouldUseUtcNow_WhenUsingResourceUsageWithDefaultMeasuredAt()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var usage = ResourceUsage.Create(0, TimeSpan.Zero, 0);

        // Assert
        var after = DateTime.UtcNow;
        Assert.True(usage.MeasuredAt >= before);
        Assert.True(usage.MeasuredAt <= after);
    }

    [Fact]
    public void ShouldReturnCurrentMetrics_WhenUsingResourceUsageCurrent()
    {
        // Act
        var usage = ResourceUsage.Current();

        // Assert
        Assert.True(usage.MemoryBytes > 0);
        Assert.True(usage.CpuTime >= TimeSpan.Zero);
        Assert.True(usage.ThreadCount > 0);
        Assert.True(usage.MeasuredAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldBeCorrect_WhenUsingResourceUsageMemoryConversions()
    {
        // Arrange
        var usage = ResourceUsage.Create(
            1073741824, // 1 GB in bytes
            TimeSpan.Zero,
            0);

        // Assert
        Assert.Equal(1024.0, usage.MemoryMB);
        Assert.Equal(1.0, usage.MemoryGB);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldEmailValidation_WhenUsingComplexScenario()
    {
        // Various valid email formats
        var validEmails = new[]
        {
            "simple@example.com",
            "user.name@example.com",
            "user+tag@example.co.uk",
            "user_name@sub.example.com",
            "123@example.com",
            "a@b.c"
        };

        foreach (var email in validEmails)
        {
            var emailAddress = EmailAddress.From(email);
            Assert.NotNull(emailAddress);
            Assert.Contains("@", emailAddress.Value);
        }
    }

    [Fact]
    public void ShouldMoneyOperations_WhenUsingComplexScenario()
    {
        // Shopping cart scenario
        var items = new[]
        {
            Money.Usd(19.99m),
            Money.Usd(35.50m),
            Money.Usd(9.99m)
        };

        var subtotal = items.Aggregate(Money.Zero("USD"), (sum, item) => sum.Add(item));
        var tax = subtotal.Multiply(0.08m); // 8% tax
        var total = subtotal.Add(tax);

        Assert.Equal(65.48m, subtotal.Amount);
        Assert.Equal(5.2384m, tax.Amount);
        Assert.Equal(70.7184m, total.Amount);
    }

    [Fact]
    public void ShouldVersionManagement_WhenUsingComplexScenario()
    {
        // Version progression
        var versions = new[]
        {
            CrewVersion.Parse("1.0.0-alpha"),
            CrewVersion.Parse("1.0.0-beta"),
            CrewVersion.Parse("1.0.0"),
            CrewVersion.Parse("1.0.1"),
            CrewVersion.Parse("1.1.0"),
            CrewVersion.Parse("2.0.0")
        };

        // Verify sorted order
        for (int i = 0; i < versions.Length - 1; i++)
        {
            Assert.True(versions[i] < versions[i + 1]);
        }

        // Find latest stable version
        var latestStable = versions.Where(v => v.IsStable).Max();
        Assert.Equal("2.0.0", latestStable?.ToString());
    }

    [Fact]
    public void ShouldMetricsTracking_WhenUsingComplexScenario()
    {
        // Simulate operation execution
        var metrics = ExecutionMetrics.Started(DateTime.UtcNow);

        // Process 100 items
        for (int i = 0; i < 100; i++)
        {
            if (i % 10 == 9) // 10% failure rate
                metrics = metrics.AddFailure();
            else
                metrics = metrics.AddSuccess();
        }

        metrics = metrics.Complete(DateTime.UtcNow.AddSeconds(30));

        Assert.Equal(90, metrics.SuccessCount);
        Assert.Equal(10, metrics.FailureCount);
        Assert.Equal(0.9, metrics.SuccessRate, 2);
        Assert.True(metrics.Duration >= TimeoutQuick);
    }

    #endregion
}
