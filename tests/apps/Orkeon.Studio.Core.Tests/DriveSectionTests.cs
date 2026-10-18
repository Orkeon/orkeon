using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using StudioDriveRights = Orkeon.Studio.Core.Configuration.DriveRights;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// <see cref="DriveSection"/> edits <c>Orkeon:Tools:Drive</c> in place, on the model of
/// <see cref="EmailSectionTests"/>: an account reads back as written, a key Studio does not model
/// survives an edit, nothing is written as an empty string, object or list, keys are found
/// whatever their case and keep the spelling of the file, and the rest of the file is never touched.
/// </summary>
public sealed class DriveSectionTests
{
    private static readonly DriveAccountDefinition Full = new()
    {
        Name = "work",
        Provider = "GoogleDrive",
        Rights = StudioDriveRights.Read | StudioDriveRights.Write | StudioDriveRights.Share,
        Root = "Orkeon/agents",
        ClientId = "id.apps.googleusercontent.com",
        ClientSecretEnvVar = "DRIVE_WORK_CLIENT_SECRET",
        AllowedRecipients = ["*@example.com", "me@example.com"],
        AllowPublicLinks = true,
        MaxUploadMegabytes = 250,
        TimeoutSeconds = 30,
    };

    [Fact]
    public void An_account_reads_back_as_it_was_written_with_the_keys_the_engine_binds()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Drive.SetAccount(Full);

        Assert.Equal(Full, document.Drive.GetAccount("work"), DefinitionComparer.Instance);
        var account = Assert.IsType<JsonObject>(document.GetNode("Orkeon:Tools:Drive:Accounts:work"));
        Assert.Equal("GoogleDrive", account["Provider"]!.GetValue<string>());
        Assert.Equal("Read, Write, Share", account["Rights"]!.GetValue<string>());
        Assert.Equal("Orkeon/agents", account["Root"]!.GetValue<string>());
        Assert.Equal("DRIVE_WORK_CLIENT_SECRET", account["Auth"]!["ClientSecretEnvVar"]!.GetValue<string>());
        Assert.True(account["Share"]!["AllowPublicLinks"]!.GetValue<bool>());
        Assert.Equal(2, account["Share"]!["AllowedRecipients"]!.AsArray().Count);
        Assert.Equal(250, account["MaxUploadMegabytes"]!.GetValue<int>());
    }

    [Fact]
    public void Clearing_every_field_leaves_the_account_object_and_no_empty_value()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(Full);

        document.Drive.SetAccount(new DriveAccountDefinition { Name = "work", Root = "  ", ClientId = "" });

        var account = Assert.IsType<JsonObject>(document.GetNode("Orkeon:Tools:Drive:Accounts:work"));
        Assert.Empty(account);
        Assert.Equal(["work"], document.Drive.AccountNames);
    }

    [Fact]
    public void A_key_studio_does_not_model_and_the_rest_of_the_file_survive_an_edit()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": { "Provider": "Ollama" },
              "Orkeon": { "Tools": { "Drive": {
                "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Note": "mine",
                  "Auth": { "ClientId": "c", "Extra": 1 }, "Share": { "Later": true } } },
                "Future": { "Setting": 1 } } } }
            }
            """);

        var account = document.Drive.GetAccount("work")!;
        document.Drive.SetAccount(account with { Root = "Shared", Rights = StudioDriveRights.Read | StudioDriveRights.Organize });

        Assert.Equal("Ollama", document.GetString("Llm:Provider"));
        Assert.Equal("mine", document.GetString("Orkeon:Tools:Drive:Accounts:work:Note"));
        Assert.Equal(1, document.GetInt32("Orkeon:Tools:Drive:Accounts:work:Auth:Extra"));
        Assert.True(document.GetBoolean("Orkeon:Tools:Drive:Accounts:work:Share:Later"));
        Assert.Equal(1, document.GetInt32("Orkeon:Tools:Drive:Future:Setting"));
        Assert.Equal("Shared", document.GetString("Orkeon:Tools:Drive:Accounts:work:Root"));
        Assert.Equal("Read, Organize", document.GetString("Orkeon:Tools:Drive:Accounts:work:Rights"));
    }

    [Fact]
    public void Saving_an_account_the_user_did_not_change_changes_nothing_whatever_the_spelling()
    {
        const string Json = """
            { "Orkeon": { "Tools": { "Drive": { "accounts": { "work": {
              "provider": "onedrive", "rights": "read,WRITE", "maxuploadmegabytes": "100", "timeoutseconds": "abc",
              "auth": { "clientid": "c" }, "share": { "allowedrecipients": ["a@b.c", 42], "allowpubliclinks": "True" } } } } } } }
            """;
        var document = AppSettingsDocument.Parse(Json);
        var before = document.ToJson();

        document.Drive.SetAccount(document.Drive.GetAccount("WORK")!);

        Assert.Equal(before, document.ToJson());
    }

    [Fact]
    public void Rights_the_engine_cannot_read_are_written_back_as_they_were_until_a_right_is_chosen()
    {
        var document = AppSettingsDocument.Parse("""{ "Orkeon": { "Tools": { "Drive": { "Accounts": { "work": { "Rights": "Read, Rite" } } } } } }""");
        var account = document.Drive.GetAccount("work")!;

        Assert.Equal(StudioDriveRights.None, account.Rights);
        Assert.Equal("Read, Rite", account.RightsRaw);

        document.Drive.SetAccount(account);
        Assert.Equal("Read, Rite", document.GetString("Orkeon:Tools:Drive:Accounts:work:Rights"));

        document.Drive.SetAccount(account with { Rights = StudioDriveRights.Read, RightsRaw = null });
        Assert.Equal("Read", document.GetString("Orkeon:Tools:Drive:Accounts:work:Rights"));
    }

    [Fact]
    public void A_name_that_differs_only_by_case_is_the_same_account_and_cannot_be_set_as_another()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(Full);

        Assert.Throws<ArgumentException>(() => document.Drive.SetAccount(Full with { Name = "WORK" }));
        Assert.Equal("work", document.Drive.GetAccount("WORK")!.Name);
    }

    [Fact]
    public void Renaming_keeps_the_place_and_every_key_and_the_default_follows()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(Full with { Name = "a" });
        document.Drive.SetAccount(Full);
        document.Drive.SetAccount(Full with { Name = "z" });
        document.Drive.DefaultAccount = "WORK";

        document.Drive.RenameAccount("work", "office");

        Assert.Equal(["a", "office", "z"], document.Drive.AccountNames);
        Assert.Equal(Full with { Name = "office" }, document.Drive.GetAccount("office"), DefinitionComparer.Instance);
        Assert.Equal("office", document.Drive.DefaultAccount);
        Assert.Throws<ArgumentException>(() => document.Drive.RenameAccount("office", "A"));
    }

    [Fact]
    public void Removing_the_last_account_removes_the_dictionary_and_the_default_that_named_it()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Drive.SetAccount(Full);
        document.Drive.DefaultAccount = "work";
        document.Drive.CredentialsDirectory = "/srv/tokens";

        document.Drive.RemoveAccount("WORK");

        Assert.Empty(document.Drive.AccountNames);
        Assert.Null(document.GetNode("Orkeon:Tools:Drive:Accounts"));
        Assert.Null(document.Drive.DefaultAccount);
        Assert.Equal("/srv/tokens", document.Drive.CredentialsDirectory);
    }

    [Fact]
    public void The_screening_switch_is_written_under_its_object_and_clearing_it_removes_the_object()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Drive.WithholdRejected = true;
        Assert.True(document.GetBoolean("Orkeon:Tools:Drive:Screening:WithholdRejected"));

        document.Drive.WithholdRejected = null;
        Assert.Null(document.GetNode("Orkeon:Tools:Drive:Screening"));

        // Clearing what was never there creates nothing.
        var empty = AppSettingsDocument.CreateEmpty();
        empty.Drive.WithholdRejected = null;
        empty.Drive.DefaultAccount = null;
        Assert.Null(empty.GetNode("Orkeon:Tools:Drive"));
    }

    [Fact]
    public void A_value_that_is_no_object_is_an_account_with_no_field_until_one_is_set()
    {
        var document = AppSettingsDocument.Parse("""{ "Orkeon": { "Tools": { "Drive": { "Accounts": { "work": null } } } } }""");

        var account = document.Drive.GetAccount("work")!;
        document.Drive.SetAccount(account);
        Assert.Null(document.GetNode("Orkeon:Tools:Drive:Accounts:work"));
        Assert.Equal(["work"], document.Drive.AccountNames);

        document.Drive.SetAccount(account with { Provider = "OneDrive" });
        Assert.Equal("OneDrive", document.GetString("Orkeon:Tools:Drive:Accounts:work:Provider"));
    }

    [Fact]
    public void The_email_section_is_left_alone_by_every_drive_edit()
    {
        var document = AppSettingsDocument.Parse("""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "work": { "Provider": "Gmail" } } } } } }""");
        var before = document.GetNode("Orkeon:Tools:Email")!.ToJsonString();

        document.Drive.SetAccount(Full);
        document.Drive.RenameAccount("work", "office");
        document.Drive.RemoveAccount("office");

        Assert.Equal(before, document.GetNode("Orkeon:Tools:Email")!.ToJsonString());
        Assert.Equal(["work"], document.Email.AccountNames);
    }

    /// <summary>Two definitions are equal when every field is, the recipient list compared item by item.</summary>
    private sealed class DefinitionComparer : IEqualityComparer<DriveAccountDefinition?>
    {
        public static readonly DefinitionComparer Instance = new();

        public bool Equals(DriveAccountDefinition? x, DriveAccountDefinition? y) =>
            x is not null && y is not null
            && x with { AllowedRecipients = [] } == y with { AllowedRecipients = [] }
            && x.AllowedRecipients.SequenceEqual(y.AllowedRecipients, StringComparer.Ordinal);

        public int GetHashCode(DriveAccountDefinition? obj) => obj?.Name.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
