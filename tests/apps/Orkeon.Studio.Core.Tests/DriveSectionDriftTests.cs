using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Tools.Drive.Configuration;
using EngineDriveRights = Orkeon.Tools.Drive.Configuration.DriveRights;
using StudioDriveRights = Orkeon.Studio.Core.Configuration.DriveRights;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Studio Core re-spells the keys and the enumeration values of <c>Orkeon:Tools:Drive</c> instead
/// of referencing <c>Orkeon.Tools.Drive</c>, so the copies are held to the engine here, as
/// <see cref="EmailSectionDriftTests"/> holds the e-mail ones. The engine types are <c>internal</c>;
/// the engine opens them to this project only.
/// </summary>
public sealed class DriveSectionDriftTests
{
    /// <summary>The options classes the binder fills, with the keys of <see cref="DriveSection.Keys"/> each one carries.</summary>
    private static readonly (Type Options, string[] Keys)[] Groups =
    [
        (typeof(DriveToolsOptions), [DriveSection.Keys.DefaultAccount, DriveSection.Keys.CredentialsDirectory, DriveSection.Keys.Accounts, DriveSection.Keys.Screening]),
        (typeof(DriveScreeningOptions), [DriveSection.Keys.WithholdRejected]),
        (typeof(DriveAccountOptions),
        [
            DriveSection.Keys.Provider, DriveSection.Keys.Rights, DriveSection.Keys.Root, DriveSection.Keys.Auth,
            DriveSection.Keys.Share, DriveSection.Keys.MaxUploadMegabytes, DriveSection.Keys.TimeoutSeconds,
        ]),
        (typeof(DriveAuthOptions), [DriveSection.Keys.ClientId, DriveSection.Keys.ClientSecretEnvVar, DriveSection.Keys.Tenant]),
        (typeof(DriveShareOptions), [DriveSection.Keys.AllowedRecipients, DriveSection.Keys.AllowPublicLinks]),
    ];

    [Fact]
    public void The_section_path_is_the_one_the_runner_binds()
    {
        Assert.Equal(ConfigurationKeys.ToolsDrive, DriveSection.SectionPath);
        Assert.Equal(ConfigurationKeys.ToolsDrive + ":" + DriveSection.Keys.Accounts, DriveSection.AccountsPath);
    }

    [Fact]
    public void Every_public_property_of_an_options_class_has_its_key_and_every_key_is_a_property()
    {
        foreach (var (options, keys) in Groups)
        {
            var properties = options
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal);

            Assert.True(
                properties.SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                $"{options.Name} binds [{string.Join(", ", properties)}] but DriveSection.Keys models [{string.Join(", ", keys.Order(StringComparer.Ordinal))}] for it.");
        }
    }

    [Fact]
    public void Every_key_constant_belongs_to_an_options_class()
    {
        var constants = typeof(DriveSection.Keys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.Equal(
            Groups.SelectMany(group => group.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            constants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_provider_studio_offers_is_a_member_of_the_engine_enumeration_and_the_reverse()
    {
        Assert.Equal(Enum.GetNames<DriveProvider>().Order(StringComparer.Ordinal), DriveSection.Providers.Order(StringComparer.Ordinal));
        foreach (var provider in DriveSection.Providers)
            Assert.Equal(provider, BindOne(new DriveAccountDefinition { Name = "a", Provider = provider }).Provider.ToString());
    }

    [Fact]
    public void The_engine_lists_its_rights_in_the_order_studio_writes_them_and_with_the_same_values()
    {
        Assert.Equal(
            Enum.GetValues<EngineDriveRights>().Where(right => right != EngineDriveRights.None).Select(right => right.ToString()),
            DriveSection.RightNames);

        foreach (var right in Enum.GetValues<EngineDriveRights>())
            Assert.Equal((int)right, (int)Enum.Parse<StudioDriveRights>(right.ToString()));
        Assert.Equal(Enum.GetNames<EngineDriveRights>().Order(StringComparer.Ordinal), Enum.GetNames<StudioDriveRights>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_right_studio_writes_alone_is_bound_by_the_engine_as_that_right()
    {
        foreach (var right in Enum.GetValues<StudioDriveRights>().Where(right => right != StudioDriveRights.None))
            Assert.Equal(right.ToString(), BindOne(new DriveAccountDefinition { Name = "a", Rights = right }).Rights.ToString());
    }

    [Fact]
    public void An_account_filled_field_by_field_is_bound_by_the_engine_with_no_problem_and_the_values_written()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.DefaultAccount = "work";
        document.Drive.WithholdRejected = true;
        document.Drive.CredentialsDirectory = "/srv/orkeon/credentials";
        document.Drive.SetAccount(new DriveAccountDefinition
        {
            Name = "work",
            Provider = "OneDrive",
            Rights = StudioDriveRights.Read | StudioDriveRights.Write | StudioDriveRights.Organize | StudioDriveRights.Share
                     | StudioDriveRights.Delete | StudioDriveRights.Purge,
            Root = "Orkeon/agents",
            ClientId = "client-id",
            ClientSecretEnvVar = "CLIENT_SECRET",
            Tenant = "organizations",
            AllowedRecipients = ["*@example.com", "you@gmail.com"],
            AllowPublicLinks = true,
            MaxUploadMegabytes = 500,
            TimeoutSeconds = 30,
        });

        var options = Bind(document);

        Assert.Empty(options.SectionProblems);
        Assert.Empty(options.AccountProblems);
        Assert.Equal("work", options.DefaultAccount);
        Assert.True(options.Screening.WithholdRejected);
        Assert.Equal("/srv/orkeon/credentials", options.CredentialsDirectory);

        var account = Assert.Single(options.Accounts).Value;
        Assert.Equal(DriveProvider.OneDrive, account.Provider);
        Assert.Equal(
            EngineDriveRights.Read | EngineDriveRights.Write | EngineDriveRights.Organize | EngineDriveRights.Share
            | EngineDriveRights.Delete | EngineDriveRights.Purge,
            account.Rights);
        Assert.Equal("Orkeon/agents", account.Root);
        Assert.Equal("client-id", account.Auth.ClientId);
        Assert.Equal("CLIENT_SECRET", account.Auth.ClientSecretEnvVar);
        Assert.Equal("organizations", account.Auth.Tenant);
        Assert.Equal(["*@example.com", "you@gmail.com"], account.Share.AllowedRecipients);
        Assert.True(account.Share.AllowPublicLinks);
        Assert.Equal(500, account.MaxUploadMegabytes);
        Assert.Equal(30, account.TimeoutSeconds);
    }

    [Fact]
    public void A_coherent_account_written_by_studio_is_resolved_by_the_engine_with_no_problem()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(new DriveAccountDefinition
        {
            Name = "work",
            Provider = "GoogleDrive",
            Rights = StudioDriveRights.Read | StudioDriveRights.Share,
            Root = "Orkeon",
            ClientId = "id.apps.googleusercontent.com",
            ClientSecretEnvVar = DriveSecretNames.ClientSecretFor("work", []),
            AllowedRecipients = ["*@example.com"],
        });

        var options = Bind(document);
        var resolution = DriveAccountResolver.Resolve("work", options.Accounts["work"]);

        Assert.Empty(options.AccountProblems);
        Assert.Empty(resolution.Problems);
        Assert.NotNull(resolution.Account);
    }

    private static DriveAccountOptions BindOne(DriveAccountDefinition account)
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(account);

        var options = Bind(document);

        Assert.Empty(options.AccountProblems);
        return options.Accounts[account.Name];
    }

    private static DriveToolsOptions Bind(AppSettingsDocument document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsDrive), options);
        return options;
    }
}
