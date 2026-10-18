using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Validation;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using StudioDriveRights = Orkeon.Studio.Core.Configuration.DriveRights;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// What Studio says of a drive account is what the run will say, on the model of
/// <see cref="EmailValidationOracleTests"/>. Studio Core spells the engine's rules again
/// (<see cref="DriveAccountRules"/>) instead of referencing <c>Orkeon.Tools.Drive</c>, so every row
/// here is handed to the engine's own binder, registry and resolver: the account is usable or set
/// aside on both sides, and each family of problems counts the same and opens on the same
/// sentence. A rule the engine gains turns its row red, and the rule is then added in Core.
/// </summary>
public sealed class DriveValidationOracleTests
{
    private const string Name = "work";

    /// <summary>A Google Drive account the engine resolves: the base most rows change one field of.</summary>
    private static readonly DriveAccountDefinition Google = new()
    {
        Name = Name,
        Provider = "GoogleDrive",
        Rights = StudioDriveRights.Read | StudioDriveRights.Write,
        Root = "Orkeon/agents",
        ClientId = "id.apps.googleusercontent.com",
        ClientSecretEnvVar = "DRIVE_WORK_CLIENT_SECRET",
    };

    /// <summary>A OneDrive account the engine resolves: a client id, the default tenant.</summary>
    private static readonly DriveAccountDefinition OneDrive = new()
    {
        Name = Name,
        Provider = "OneDrive",
        Rights = StudioDriveRights.Read,
        ClientId = "00000000-0000-0000-0000-000000000000",
    };

    /// <summary>
    /// The accounts built with <see cref="DriveAccountDefinition"/> and written by
    /// <see cref="DriveSection"/>, each with the codes Studio raises on it, in order.
    /// </summary>
    private static readonly Dictionary<string, (DriveAccountDefinition Account, string[] Codes)> Definitions = new(StringComparer.Ordinal)
    {
        ["google drive"] = (Google, []),
        ["onedrive"] = (OneDrive, []),
        ["onedrive for work, its client secret named, every right"] = (OneDrive with
        {
            Tenant = "organizations",
            ClientSecretEnvVar = "WORK_SECRET",
            Rights = StudioDriveRights.Read | StudioDriveRights.Write | StudioDriveRights.Organize | StudioDriveRights.Share
                     | StudioDriveRights.Delete | StudioDriveRights.Purge,
            AllowedRecipients = ["*", "*@example.com", "me@example.com"],
            AllowPublicLinks = true,
            MaxUploadMegabytes = 10240,
            TimeoutSeconds = 30,
        }, []),
        ["a name the engine refuses"] = (Google with { Name = "-work" }, [ValidationCodes.DriveName]),
        ["no provider"] = (Google with { Provider = null }, [ValidationCodes.DriveProvider]),
        ["no rights"] = (Google with { Rights = StudioDriveRights.None }, [ValidationCodes.DriveRights]),
        ["rights written none"] = (Google with { Rights = StudioDriveRights.None, RightsRaw = "None" }, [ValidationCodes.DriveRights]),
        ["rights the engine cannot read"] = (Google with { Rights = StudioDriveRights.None, RightsRaw = "Read, Rite" }, [ValidationCodes.DriveValue]),
        ["a provider the engine does not know"] = (Google with { Provider = "Dropbox" }, [ValidationCodes.DriveValue]),
        ["a root that climbs"] = (Google with { Root = "Orkeon/../secrets" }, [ValidationCodes.DriveRoot]),
        ["a root with a colon"] = (Google with { Root = "C:/agents" }, [ValidationCodes.DriveRoot]),
        ["a root with backslashes"] = (Google with { Root = "Orkeon\\agents" }, []),
        ["google without a client id"] = (Google with { ClientId = null }, [ValidationCodes.DriveAuth]),
        ["google without a client secret"] = (Google with { ClientSecretEnvVar = null }, [ValidationCodes.DriveAuth]),
        ["google with a tenant"] = (Google with { Tenant = "organizations" }, [ValidationCodes.DriveAuth]),
        ["onedrive without a client id"] = (OneDrive with { ClientId = null, Tenant = "my tenant" }, [ValidationCodes.DriveAuth]),
        ["onedrive with a tenant that is no alias"] = (OneDrive with { Tenant = "my tenant" }, [ValidationCodes.DriveAuth]),
        ["recipients that are no pattern"] = (Google with { AllowedRecipients = ["me", "*.example.com", "*@", "a@b@c"] },
            [ValidationCodes.DriveShare, ValidationCodes.DriveShare, ValidationCodes.DriveShare, ValidationCodes.DriveShare]),
        ["an upload limit of nothing"] = (Google with { MaxUploadMegabytes = 0 }, [ValidationCodes.DriveLimits]),
        ["an upload limit beyond the ceiling"] = (Google with { MaxUploadMegabytes = 10241 }, [ValidationCodes.DriveLimits]),
        ["a timeout of nothing"] = (Google with { TimeoutSeconds = 0 }, [ValidationCodes.DriveLimits]),
        ["everything wrong at once"] = (new DriveAccountDefinition
        {
            Name = "_bad",
            Provider = "OneDrive",
            Root = "a/./b",
            Tenant = "x y",
            AllowedRecipients = ["nobody"],
            MaxUploadMegabytes = -1,
            TimeoutSeconds = -5,
        }, [ValidationCodes.DriveName, ValidationCodes.DriveRights, ValidationCodes.DriveRoot, ValidationCodes.DriveAuth,
            ValidationCodes.DriveShare, ValidationCodes.DriveLimits, ValidationCodes.DriveLimits]),
    };

    /// <summary>
    /// The accounts written by hand, as <c>Orkeon:Tools:Drive</c> sections holding the account
    /// <c>work</c>: what a file may carry that <see cref="DriveSection"/> never writes.
    /// </summary>
    private static readonly Dictionary<string, (string Section, string[] Codes)> Files = new(StringComparer.Ordinal)
    {
        ["an account that is no object"] = ("""{ "Accounts": { "work": null } }""",
            [ValidationCodes.DriveProvider, ValidationCodes.DriveRights]),
        ["values the binder cannot convert"] = ("""
            { "Accounts": { "work": { "Provider": "Box", "Rights": "Read, Rite", "MaxUploadMegabytes": "lots",
              "Share": { "AllowPublicLinks": "sometimes" }, "TimeoutSeconds": "30s" } } }
            """, [ValidationCodes.DriveValue, ValidationCodes.DriveValue, ValidationCodes.DriveValue, ValidationCodes.DriveValue, ValidationCodes.DriveValue]),
        ["keys no account carries"] = ("""
            { "Accounts": { "work": { "Provider": "GoogleDrive", "Roots": "Orkeon", "Auth": { "ClientId": "c", "Secret": "s" },
              "Rights": "Write, Bogus" } } }
            """, [ValidationCodes.DriveKey, ValidationCodes.DriveKey, ValidationCodes.DriveValue]),
        ["every key in another case"] = ("""
            { "screening": { "withholdrejected": false },
              "accounts": { "work": { "provider": "onedrive", "rights": "read,WRITE", "root": "/Orkeon/agents/",
                "auth": { "clientid": "client", "tenant": "organizations" },
                "share": { "allowedrecipients": ["*@example.com"], "allowpubliclinks": "True" },
                "maxuploadmegabytes": "250", "timeoutseconds": 60 } } }
            """, []),
        ["numbers for the provider and the rights"] = ("""
            { "Accounts": { "work": { "Provider": 1, "Rights": "63", "Auth": { "ClientId": "c" } } } }
            """, []),
        ["a number with a bit no right has"] = ("""
            { "Accounts": { "work": { "Provider": "OneDrive", "Rights": "64", "Auth": { "ClientId": "c" } } } }
            """, [ValidationCodes.DriveValue]),
        ["an empty provider and empty numbers read as absent"] = ("""
            { "Accounts": { "work": { "Provider": "", "Rights": "Read", "MaxUploadMegabytes": "", "Auth": { "ClientId": "c" } } } }
            """, [ValidationCodes.DriveProvider]),
        ["empty rights"] = ("""
            { "Accounts": { "work": { "Provider": "OneDrive", "Rights": "", "Auth": { "ClientId": "c" } } } }
            """, [ValidationCodes.DriveValue]),
        ["a screening switch that is neither"] = ("""
            { "Screening": { "WithholdRejected": "maybe" },
              "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }
            """, [ValidationCodes.DriveScreening]),
        ["a screening switch and a key at once"] = ("""
            { "Screening": { "WithholdRejected": 1 },
              "Accounts": { "work": { "Provider": "OneDrive", "Note": "mine" } } }
            """, [ValidationCodes.DriveScreening, ValidationCodes.DriveKey]),
        ["a screening switch with blanks around it"] = ("""
            { "Screening": { "WithholdRejected": " True " },
              "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }
            """, []),
    };

    public static TheoryData<string> DefinitionRows() => new(Definitions.Keys);

    public static TheoryData<string> FileRows() => new(Files.Keys);

    [Theory]
    [MemberData(nameof(DefinitionRows))]
    public void Studio_and_the_engine_say_the_same_of_an_account_studio_writes(string row)
    {
        var (definition, codes) = Definitions[row];
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(definition);

        var findings = DriveAccountRules.Check(definition.Name, definition);

        Assert.Equal(codes, findings.Select(finding => finding.Code));
        AssertSameAsTheEngine(document, definition.Name, findings);

        // The file Studio wrote reads back as the account it was given.
        Assert.Equal(findings, DriveAccountRules.Check(document, definition.Name));
    }

    [Theory]
    [MemberData(nameof(FileRows))]
    public void Studio_and_the_engine_say_the_same_of_an_account_written_by_hand(string row)
    {
        var (section, codes) = Files[row];
        var document = AppSettingsDocument.Parse($$"""{ "Orkeon": { "Tools": { "Drive": {{section}} } } }""");

        var findings = DriveAccountRules.Check(document, Name);

        Assert.Equal(codes, findings.Select(finding => finding.Code));
        AssertSameAsTheEngine(document, Name, findings);
    }

    /// <summary>
    /// Studio's own finding, and the only one the engine does not make: the secret pasted in the
    /// field that names its variable. The engine resolves the account and fails when it reads it.
    /// </summary>
    [Fact]
    public void A_secret_pasted_where_its_variable_is_named_is_said_by_studio_alone()
    {
        var definition = Google with { ClientSecretEnvVar = "GOCSPX-abc def" };

        var finding = Assert.Single(DriveAccountRules.Check(definition.Name, definition));

        Assert.Equal(ValidationCodes.DriveAuth, finding.Code);
        Assert.Equal($"{DriveSection.AccountsPath}:{Name}:Auth:ClientSecretEnvVar", finding.Path);
        Assert.DoesNotContain("GOCSPX", finding.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_finding_on_an_account_is_a_warning_at_a_path_under_the_account()
    {
        foreach (var (definition, _) in Definitions.Values)
        {
            foreach (var finding in DriveAccountRules.Check(definition.Name, definition))
            {
                Assert.Equal(ValidationSeverity.Warning, finding.Severity);
                Assert.StartsWith($"{DriveSection.AccountsPath}:{definition.Name}", finding.Path, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void An_account_the_file_does_not_hold_has_no_finding()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(Google with { Provider = null });

        Assert.Empty(DriveAccountRules.Check(document, "other"));
        Assert.Empty(DriveAccountRules.Check(AppSettingsDocument.CreateEmpty(), Name));
    }

    [Fact]
    public void A_recipient_pattern_is_judged_as_the_engine_judges_it()
    {
        foreach (var pattern in new[] { "*", "*@example.com", "me@example.com", "a.b+c@sub.example.org", "me", "*.example.com", "*@", "*@a@b", "a@b@c", "Me <me@example.com>", "*@*" })
        {
            Assert.Equal(
                Orkeon.Tools.Drive.Security.RecipientPatterns.IsValidPattern(pattern),
                DriveAccountRules.IsRecipientPattern(pattern));
        }
    }

    [Fact]
    public void An_account_name_is_judged_as_the_engine_judges_it()
    {
        foreach (var name in new[] { "work", "Work.2", "a_b-c", "-work", "_work", "", "w o", new string('a', 64), new string('a', 65), "é" })
            Assert.Equal(DriveAccountResolver.IsValidName(name), DriveAccountRules.IsValidName(name));
    }

    /// <summary>The verdict and the problems by family, against what the engine's registry returns once its binder has read <paramref name="document"/>.</summary>
    private static void AssertSameAsTheEngine(AppSettingsDocument document, string name, IReadOnlyList<ValidationMessage> findings)
    {
        var options = Bind(document);
        var resolution = new DriveAccountRegistry(Options.Create(options)).Inspect(name);

        // Usable or set aside.
        Assert.Equal(resolution.Account is null, findings.Count > 0);

        // The same number of problems in each family, and the same first sentence.
        var engine = resolution.Problems.ToLookup(FamilyOf);
        var studio = findings.ToLookup(finding => finding.Code, finding => finding.Text);
        Assert.Equal(engine.Select(family => family.Key).Order(StringComparer.Ordinal), studio.Select(family => family.Key).Order(StringComparer.Ordinal));
        foreach (var family in engine)
        {
            Assert.Equal(family.Count(), studio[family.Key].Count());
            Assert.Equal(family.First(), studio[family.Key].First());
        }
    }

    /// <summary>The code Studio files an engine sentence under: the drive families of <see cref="ValidationCodes"/>.</summary>
    private static string FamilyOf(string problem) => problem switch
    {
        _ when problem.Contains(" is not an account setting: ", StringComparison.Ordinal) => ValidationCodes.DriveKey,
        _ when problem.StartsWith(ConfigurationKeys.ToolsDrive + ":Screening:", StringComparison.Ordinal) => ValidationCodes.DriveScreening,
        _ when problem.Contains("' is not one of ", StringComparison.Ordinal)
            || problem.Contains("' is not a list of ", StringComparison.Ordinal)
            || problem.EndsWith("' is neither true nor false", StringComparison.Ordinal)
            || problem.EndsWith("' is not a whole number", StringComparison.Ordinal) => ValidationCodes.DriveValue,
        _ when problem.StartsWith("the account name ", StringComparison.Ordinal) => ValidationCodes.DriveName,
        _ when problem.StartsWith("Provider ", StringComparison.Ordinal) => ValidationCodes.DriveProvider,
        _ when problem.StartsWith("Rights ", StringComparison.Ordinal) => ValidationCodes.DriveRights,
        _ when problem.StartsWith("Root ", StringComparison.Ordinal) => ValidationCodes.DriveRoot,
        _ when problem.StartsWith("Auth:", StringComparison.Ordinal) => ValidationCodes.DriveAuth,
        _ when problem.StartsWith("Share:", StringComparison.Ordinal) => ValidationCodes.DriveShare,
        _ when problem.StartsWith("MaxUploadMegabytes ", StringComparison.Ordinal)
            || problem.StartsWith("TimeoutSeconds ", StringComparison.Ordinal) => ValidationCodes.DriveLimits,
        _ => "unclassified: " + problem,
    };

    private static DriveToolsOptions Bind(AppSettingsDocument document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsDrive), options);
        return options;
    }
}
