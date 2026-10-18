using Microsoft.Extensions.Options;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Accounts;

/// <summary>Which account a call gets, and what it is told when there is none.</summary>
public class DriveAccountRegistryTests
{
    [Fact]
    public void Should_refuse_every_call_when_no_account_is_configured()
    {
        var error = Assert.Throws<DriveToolException>(() => Registry().Resolve(null));

        Assert.Equal(DriveErrorCode.NotConfigured, error.Code);
        Assert.Contains("Orkeon:Tools:Drive:Accounts", error.Message, StringComparison.Ordinal);
        Assert.Contains("docs/guides/drive.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_use_the_only_account_by_default_and_ask_which_one_among_several()
    {
        Assert.Equal("perso", Registry(("perso", TestDriveAccounts.Google())).Resolve(null).Name);

        var error = Assert.Throws<DriveToolException>(() => Registry(("a", TestDriveAccounts.Google()), ("b", TestDriveAccounts.OneDrive())).Resolve(" "));
        Assert.Equal(DriveErrorCode.UnknownAccount, error.Code);
        Assert.Contains("a, b", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_name_the_problems_of_a_misconfigured_account()
    {
        var error = Assert.Throws<DriveToolException>(() => Registry(("broken", new DriveAccountOptions())).Resolve("BROKEN"));

        Assert.Equal(DriveErrorCode.InvalidConfiguration, error.Code);
        Assert.Contains("Provider is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_an_unknown_account_with_the_configured_ones()
    {
        var error = Assert.Throws<DriveToolException>(() => Registry(("perso", TestDriveAccounts.Google())).Resolve("pro"));

        Assert.Equal(DriveErrorCode.UnknownAccount, error.Code);
        Assert.Contains("Configured: perso", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Demand_should_name_only_the_missing_rights_and_where_to_add_them()
    {
        var account = TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google(DriveRights.Read));

        var error = Assert.Throws<DriveToolException>(() => DriveAccess.Demand(account, DriveRights.Read | DriveRights.Delete | DriveRights.Purge));

        Assert.Equal(DriveErrorCode.RightDenied, error.Code);
        Assert.Contains("the Delete, Purge rights (it grants: Read)", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Drive:Accounts:perso:Rights", error.Message, StringComparison.Ordinal);
    }

    internal static DriveAccountRegistry Registry(params (string Name, DriveAccountOptions Options)[] accounts)
    {
        var options = new DriveToolsOptions();
        foreach (var (name, declared) in accounts)
            options.Accounts[name] = declared;
        return new DriveAccountRegistry(Options.Create(options));
    }
}
