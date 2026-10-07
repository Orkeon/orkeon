using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-65 — the typed view over <c>Orkeon:Tools:Email</c>: accounts keyed by name, every write
/// in place, keys Studio does not model kept whole, and nothing written the engine would read as
/// an empty value (an empty string, an empty object, an empty list).
/// </summary>
public sealed class EmailSectionTests
{
    // The three account blocks of docs/guides/email.md, each wrapped in the section that carries it.
    private const string GuideQuickStart = """
        {
          "Orkeon": {
            "Tools": {
              "Email": {
                "Accounts": {
                  "gmail": {
                    "Provider": "Gmail",
                    "Address": "you@gmail.com",
                    "Rights": "Read, Organize, Draft",
                    "Auth": { "Method": "Password", "PasswordEnvVar": "GMAIL_APP_PASSWORD" }
                  }
                }
              }
            }
          }
        }
        """;

    private const string GuideOutlook = """
        {
          "Orkeon": { "Tools": { "Email": { "Accounts": {
            "hotmail": {
              "Provider": "Outlook",
              "Address": "you@hotmail.com",
              "Rights": "Read, Organize, Draft, Send",
              "Auth": { "ClientId": "00000000-0000-0000-0000-000000000000" },
              "Send": { "AllowedRecipients": [ "you@gmail.com", "*@example.com" ] }
            }
          } } } }
        }
        """;

    private const string GuideOwnServer = """
        {
          "Orkeon": { "Tools": { "Email": { "Accounts": {
            "work": {
              "Provider": "Custom",
              "Address": "me@example.com",
              "Rights": "Read, Organize, Draft, Send, Delete",
              "Incoming": { "Protocol": "Imap", "Host": "imap.example.com" },
              "Outgoing": { "Host": "smtp.example.com", "Port": 587, "Security": "StartTls" },
              "Auth": { "Username": "me", "PasswordEnvVar": "WORK_MAIL_PASSWORD" },
              "Send": { "AllowedRecipients": [ "*@example.com" ], "MaxRecipients": 5, "MaxPerHour": 20 }
            }
          } } } }
        }
        """;

    public static TheoryData<string, string> LoadableDocuments => new()
    {
        { "guide: Gmail quick start", GuideQuickStart },
        { "guide: Outlook account", GuideOutlook },
        { "guide: own server", GuideOwnServer },
        {
            "examples/scripting/13-email-triage.appsettings.json",
            File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "scripting", "13-email-triage.appsettings.json"))
        },
    };

    [Theory]
    [MemberData(nameof(LoadableDocuments))]
    public void A_document_written_by_hand_goes_through_a_load_and_a_save_unchanged(string label, string json)
    {
        var document = AppSettingsDocument.Parse(json);
        Assert.True(document.Email.AccountNames.Count > 0, label);

        foreach (var name in document.Email.AccountNames)
            document.Email.SetAccount(document.Email.GetAccount(name)!);

        Assert.Equal(AppSettingsDocument.Parse(json).ToJson(), document.ToJson());
    }

    [Fact]
    public void A_key_studio_does_not_model_stays_where_it_was_under_the_section_an_account_and_Incoming()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Orkeon": { "Tools": { "Email": {
                "Note": 1,
                "Accounts": {
                  "a": {
                    "Provider": "Custom",
                    "Label": "kept",
                    "Address": "a@x.org",
                    "Incoming": { "Host": "h", "Hint": true, "Port": 143 },
                    "Auth": { "Extra": "kept", "PasswordEnvVar": "P" }
                  }
                }
              } } }
            }
            """);

        var account = document.Email.GetAccount("a")!;
        document.Email.SetAccount(account with { Address = "b@x.org", IncomingHost = "h2", PasswordEnvVar = null });

        Assert.Equal(AppSettingsDocument.Parse("""
            {
              "Orkeon": { "Tools": { "Email": {
                "Note": 1,
                "Accounts": {
                  "a": {
                    "Provider": "Custom",
                    "Label": "kept",
                    "Address": "b@x.org",
                    "Incoming": { "Host": "h2", "Hint": true, "Port": 143 },
                    "Auth": { "Extra": "kept" }
                  }
                }
              } } }
            }
            """).ToJson(), document.ToJson());
    }

    [Fact]
    public void A_cleared_field_removes_its_key_and_is_never_written_as_an_empty_string()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(FullAccount("a"));

        document.Email.SetAccount(new EmailAccountDefinition
        {
            Name = "a",
            Provider = "",
            Address = "a@x.org",
            DisplayName = "   ",
            IncomingHost = "",
            OutgoingHost = "",
            Username = "",
            PasswordEnvVar = "",
            ClientId = "",
            ClientSecretEnvVar = "",
            Tenant = "",
        });

        Assert.Equal(AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "a": { "Address": "a@x.org" } } } } } }
            """).ToJson(), document.ToJson());
    }

    [Fact]
    public void An_emptied_Incoming_Outgoing_Auth_and_Send_are_removed_and_never_written_as_empty_objects()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(FullAccount("a"));

        document.Email.SetAccount(document.Email.GetAccount("a")! with
        {
            IncomingProtocol = null, IncomingHost = null, IncomingPort = null, IncomingSecurity = null,
            OutgoingProtocol = null, OutgoingHost = null, OutgoingPort = null, OutgoingSecurity = null,
            AuthMethod = null, Username = null, PasswordEnvVar = null, ClientId = null, ClientSecretEnvVar = null, Tenant = null,
            AllowedRecipients = [], MaxRecipients = null, MaxPerHour = null,
        });

        var account = Assert.IsType<System.Text.Json.Nodes.JsonObject>(document.GetNode("Orkeon:Tools:Email:Accounts:a"));
        Assert.DoesNotContain(account, property => property.Key is "Incoming" or "Outgoing" or "Auth" or "Send");
        Assert.DoesNotContain("{}", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_emptied_recipient_list_removes_AllowedRecipients_and_never_writes_an_empty_array()
    {
        var document = AppSettingsDocument.Parse(GuideOwnServer);

        document.Email.SetAccount(document.Email.GetAccount("work")! with { AllowedRecipients = [] });

        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts:work:Send:AllowedRecipients"));
        Assert.Equal(5, document.GetInt32("Orkeon:Tools:Email:Accounts:work:Send:MaxRecipients"));
        Assert.DoesNotContain("[]", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Send_block_holding_only_an_emptied_list_goes_with_it()
    {
        var document = AppSettingsDocument.Parse(GuideOutlook);

        document.Email.SetAccount(document.Email.GetAccount("hotmail")! with { AllowedRecipients = [] });

        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts:hotmail:Send"));
    }

    [Fact]
    public void Rights_are_written_in_the_engine_order_as_one_comma_separated_text()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Email.SetAccount(new EmailAccountDefinition
        {
            Name = "a",
            Rights = EmailRight.Draft | EmailRight.Read | EmailRight.Organize,
        });

        Assert.Equal("Read, Organize, Draft", document.GetString("Orkeon:Tools:Email:Accounts:a:Rights"));
        Assert.Equal(System.Text.Json.JsonValueKind.String, document.GetNode("Orkeon:Tools:Email:Accounts:a:Rights")!.GetValueKind());
    }

    [Theory]
    [InlineData("Read, Organize, Draft", EmailRight.Read | EmailRight.Organize | EmailRight.Draft)]
    [InlineData("read,SEND", EmailRight.Read | EmailRight.Send)]
    [InlineData("3", EmailRight.Read | EmailRight.Organize)]
    [InlineData("Purge", EmailRight.Purge)]
    public void Rights_are_read_without_regard_to_case_and_in_their_numeric_form(string written, EmailRight expected)
    {
        var document = AppSettingsDocument.Parse(
            $$"""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "a": { "Rights": "{{written}}" } } } } } }""");

        var account = document.Email.GetAccount("a")!;

        Assert.Equal(expected, account.Rights);
        Assert.Null(account.RightsRaw);
    }

    [Theory]
    [InlineData("Reed")]
    [InlineData("Read, Bogus")]
    [InlineData("64")]
    [InlineData("None")]
    public void A_text_the_engine_cannot_read_as_rights_is_kept_as_written_until_a_right_is_chosen(string written)
    {
        var document = AppSettingsDocument.Parse(
            $$"""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "a": { "Rights": "{{written}}" } } } } } }""");

        var account = document.Email.GetAccount("a")!;
        Assert.Equal(EmailRight.None, account.Rights);
        Assert.Equal(written, account.RightsRaw);

        document.Email.SetAccount(account);
        Assert.Equal(written, document.GetString("Orkeon:Tools:Email:Accounts:a:Rights"));

        document.Email.SetAccount(account with { Rights = EmailRight.Read });
        Assert.Equal("Read", document.GetString("Orkeon:Tools:Email:Accounts:a:Rights"));
        Assert.Null(document.Email.GetAccount("a")!.RightsRaw);
    }

    [Fact]
    public void Rights_cleared_remove_the_key_and_never_write_an_empty_string()
    {
        var document = AppSettingsDocument.Parse(GuideQuickStart);

        document.Email.SetAccount(document.Email.GetAccount("gmail")! with { Rights = EmailRight.None });

        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts:gmail:Rights"));
    }

    [Fact]
    public void A_value_the_user_did_not_change_keeps_the_spelling_the_file_had()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "a": {
              "Address": "a@x.org", "Rights": "read,SEND", "TimeoutSeconds": "30", "SaveSentCopy": "true",
              "Incoming": { "Port": "993" }
            } } } } } }
            """);

        document.Email.SetAccount(document.Email.GetAccount("a")! with { Address = "b@x.org" });

        Assert.Equal(AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "a": {
              "Address": "b@x.org", "Rights": "read,SEND", "TimeoutSeconds": "30", "SaveSentCopy": "true",
              "Incoming": { "Port": "993" }
            } } } } } }
            """).ToJson(), document.ToJson());
    }

    [Fact]
    public void Every_field_of_an_account_comes_back_as_it_was_written()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var written = FullAccount("work");

        document.Email.SetAccount(written);

        var read = Assert.IsType<EmailAccountDefinition>(AppSettingsDocument.Parse(document.ToJson()).Email.GetAccount("work"));
        Assert.Equal(written.AllowedRecipients, read.AllowedRecipients);
        Assert.Equal(written with { AllowedRecipients = [] }, read with { AllowedRecipients = [] });
        Assert.Equal(["work"], document.Email.AccountNames);
    }

    [Fact]
    public void An_account_with_nothing_set_stays_listed_as_an_empty_object()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Email.SetAccount(new EmailAccountDefinition { Name = "new" });

        Assert.Equal(["new"], document.Email.AccountNames);
        var read = Assert.IsType<EmailAccountDefinition>(document.Email.GetAccount("new"));
        Assert.Equal(new EmailAccountDefinition { Name = "new" } with { AllowedRecipients = [] }, read with { AllowedRecipients = [] });
        Assert.Empty(read.AllowedRecipients);
    }

    [Fact]
    public void An_account_that_is_not_declared_is_null_and_a_new_one_goes_last()
    {
        var document = AppSettingsDocument.Parse(GuideQuickStart);

        Assert.Null(document.Email.GetAccount("missing"));

        document.Email.SetAccount(new EmailAccountDefinition { Name = "second", Address = "s@x.org" });

        Assert.Equal(["gmail", "second"], document.Email.AccountNames);
    }

    [Fact]
    public void Renaming_keeps_the_account_where_it_stood_and_follows_the_default_account()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "DefaultAccount": "B", "Accounts": {
              "a": { "Address": "a@x.org" },
              "b": { "Address": "b@x.org", "Extra": 1 },
              "c": { "Address": "c@x.org" }
            } } } } }
            """);

        document.Email.RenameAccount("b", "beta");

        Assert.Equal(["a", "beta", "c"], document.Email.AccountNames);
        Assert.Equal("beta", document.Email.DefaultAccount);
        Assert.Equal(1, document.GetInt32("Orkeon:Tools:Email:Accounts:beta:Extra"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts:b"));
    }

    [Fact]
    public void Renaming_leaves_the_default_account_alone_when_it_names_another_account()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "DefaultAccount": "c", "Accounts": { "a": { "Address": "a@x.org" }, "c": {} } } } } }
            """);

        document.Email.RenameAccount("a", "alpha");

        Assert.Equal("c", document.Email.DefaultAccount);
        Assert.Equal(["alpha", "c"], document.Email.AccountNames);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("C")]
    public void Renaming_to_a_name_another_account_holds_is_refused_whatever_the_case(string taken)
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "a": { "Address": "a@x.org" }, "c": {} } } } } }
            """);
        var before = document.ToJson();

        Assert.Throws<ArgumentException>(() => document.Email.RenameAccount("a", taken));

        Assert.Equal(before, document.ToJson());
    }

    [Fact]
    public void Renaming_an_account_to_its_own_name_in_another_case_is_allowed()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "DefaultAccount": "perso", "Accounts": { "perso": { "Address": "p@x.org" } } } } } }
            """);

        document.Email.RenameAccount("perso", "Perso");

        Assert.Equal(["Perso"], document.Email.AccountNames);
        Assert.Equal("Perso", document.Email.DefaultAccount);
    }

    [Fact]
    public void Removing_the_default_account_removes_DefaultAccount_and_the_last_account_drops_the_dictionary()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "DefaultAccount": "A", "Accounts": { "a": {}, "b": {} } } } } }
            """);

        document.Email.RemoveAccount("b");
        Assert.Equal("A", document.Email.DefaultAccount);

        document.Email.RemoveAccount("a");

        Assert.Null(document.Email.DefaultAccount);
        Assert.Null(document.GetNode("Orkeon:Tools:Email:DefaultAccount"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts"));
        Assert.Empty(document.Email.AccountNames);
    }

    [Fact]
    public void Account_names_are_compared_without_regard_to_case_as_the_engine_does()
    {
        var document = AppSettingsDocument.Parse(GuideQuickStart);

        var found = Assert.IsType<EmailAccountDefinition>(document.Email.GetAccount("GMAIL"));

        Assert.Equal("gmail", found.Name);
        Assert.Throws<ArgumentException>(() => document.Email.SetAccount(found with { Name = "Gmail" }));
        Assert.Equal(["gmail"], document.Email.AccountNames);
    }

    [Fact]
    public void Two_keys_a_file_holds_that_differ_only_by_case_are_both_listed_and_the_exact_one_wins()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Accounts": { "a": { "Address": "lower@x.org" }, "A": { "Address": "upper@x.org" } } } } } }
            """);

        Assert.Equal(["a", "A"], document.Email.AccountNames);
        Assert.Equal("upper@x.org", document.Email.GetAccount("A")!.Address);
        Assert.Equal("lower@x.org", document.Email.GetAccount("a")!.Address);

        document.Email.SetAccount(document.Email.GetAccount("A")! with { Address = "changed@x.org" });

        Assert.Equal("changed@x.org", document.GetString("Orkeon:Tools:Email:Accounts:A:Address"));
        Assert.Equal("lower@x.org", document.GetString("Orkeon:Tools:Email:Accounts:a:Address"));
    }

    [Fact]
    public void An_account_name_holding_a_colon_or_blanks_does_not_split_into_a_path()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Email.SetAccount(new EmailAccountDefinition { Name = "a:b", Address = "a@x.org" });

        Assert.Equal(["a:b"], document.Email.AccountNames);
        Assert.Equal("a@x.org", document.Email.GetAccount("a:b")!.Address);
        Assert.Null(document.GetNode("Orkeon:Tools:Email:Accounts:a"));
    }

    [Fact]
    public void The_default_account_the_credentials_directory_and_the_screening_switch_are_read_written_and_removed()
    {
        var document = AppSettingsDocument.CreateEmpty();
        Assert.Null(document.Email.DefaultAccount);
        Assert.Null(document.Email.WithholdRejected);
        Assert.Null(document.Email.CredentialsDirectory);

        document.Email.DefaultAccount = "perso";
        document.Email.WithholdRejected = true;
        document.Email.CredentialsDirectory = "/srv/orkeon/credentials";

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        Assert.Equal("perso", reparsed.Email.DefaultAccount);
        Assert.True(reparsed.Email.WithholdRejected);
        Assert.Equal("/srv/orkeon/credentials", reparsed.Email.CredentialsDirectory);
        Assert.Equal("true", reparsed.GetString("Orkeon:Tools:Email:Screening:WithholdRejected"));

        document.Email.DefaultAccount = "";
        document.Email.WithholdRejected = null;
        document.Email.CredentialsDirectory = null;

        Assert.Null(document.GetNode("Orkeon:Tools:Email:DefaultAccount"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:Screening"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:CredentialsDirectory"));
    }

    [Fact]
    public void Clearing_the_screening_switch_keeps_a_key_studio_does_not_model_under_Screening()
    {
        var document = AppSettingsDocument.Parse("""
            { "Orkeon": { "Tools": { "Email": { "Screening": { "WithholdRejected": false, "Future": 1 } } } } }
            """);

        document.Email.WithholdRejected = null;

        Assert.Equal(1, document.GetInt32("Orkeon:Tools:Email:Screening:Future"));
        Assert.Null(document.GetNode("Orkeon:Tools:Email:Screening:WithholdRejected"));
    }

    // A section written by hand with every key in lower case: the configuration compares keys
    // without regard to case, so the engine reads it whole.
    private const string LowerCaseKeys = """
        { "Orkeon": { "Tools": { "Email": {
            "defaultaccount": "work",
            "credentialsdirectory": "/srv/credentials",
            "screening": { "withholdrejected": true },
            "accounts": {
              "work": {
                "provider": "Gmail", "address": "me@gmail.com", "displayname": "Me", "rights": "Read, Send",
                "timeoutseconds": 30, "savesentcopy": true,
                "incoming": { "protocol": "Imap", "host": "imap.example.com", "port": 1993, "security": "StartTls" },
                "outgoing": { "host": "smtp.example.com", "port": 2525 },
                "auth": { "method": "Password", "username": "me", "passwordenvvar": "GMAIL_APP_PASSWORD" },
                "send": { "allowedrecipients": ["*@example.com"], "maxrecipients": 5, "maxperhour": 20 }
              }
            }
        } } } }
        """;

    [Fact]
    public void Keys_written_in_another_case_are_read_as_the_engine_reads_them()
    {
        var email = AppSettingsDocument.Parse(LowerCaseKeys).Email;

        Assert.Equal("work", email.DefaultAccount);
        Assert.Equal("/srv/credentials", email.CredentialsDirectory);
        Assert.True(email.WithholdRejected);
        Assert.Equal(["work"], email.AccountNames);
        Assert.Equal(
            new EmailAccountDefinition
            {
                Name = "work",
                Provider = "Gmail",
                Address = "me@gmail.com",
                DisplayName = "Me",
                Rights = EmailRight.Read | EmailRight.Send,
                TimeoutSeconds = 30,
                SaveSentCopy = true,
                IncomingProtocol = "Imap",
                IncomingHost = "imap.example.com",
                IncomingPort = 1993,
                IncomingSecurity = "StartTls",
                OutgoingHost = "smtp.example.com",
                OutgoingPort = 2525,
                AuthMethod = "Password",
                Username = "me",
                PasswordEnvVar = "GMAIL_APP_PASSWORD",
                MaxRecipients = 5,
                MaxPerHour = 20,
            },
            email.GetAccount("work")! with { AllowedRecipients = [] });
        Assert.Equal(["*@example.com"], email.GetAccount("work")!.AllowedRecipients);
    }

    [Fact]
    public void A_field_changed_under_keys_in_another_case_lands_on_the_key_the_file_holds_and_never_beside_it()
    {
        var document = AppSettingsDocument.Parse(LowerCaseKeys);
        var email = document.Email;

        email.DefaultAccount = "Work";
        email.CredentialsDirectory = "/srv/other";
        email.WithholdRejected = false;
        email.SetAccount(email.GetAccount("work")! with
        {
            Address = "you@gmail.com",
            Rights = EmailRight.Read,
            IncomingPort = 993,
            OutgoingSecurity = "StartTls",
            PasswordEnvVar = "OTHER_PASSWORD",
            AllowedRecipients = ["you@example.com"],
            MaxPerHour = 10,
            SaveSentCopy = false,
        });

        // No object holds two keys equal but for the case, which no run could read.
        AssertNoTwinKeys(document.Root);

        // The spelling of the file stays; a key it did not hold comes in the engine's spelling.
        var section = (JsonObject)document.GetNode(EmailSection.SectionPath)!;
        var account = (JsonObject)section["accounts"]!["work"]!;
        Assert.Equal(["defaultaccount", "credentialsdirectory", "screening", "accounts"], section.Select(property => property.Key));
        Assert.Equal(["withholdrejected"], ((JsonObject)section["screening"]!).Select(property => property.Key));
        Assert.Equal(
            ["provider", "address", "displayname", "rights", "timeoutseconds", "savesentcopy", "incoming", "outgoing", "auth", "send"],
            account.Select(property => property.Key));
        Assert.Equal(["host", "port", "Security"], ((JsonObject)account["outgoing"]!).Select(property => property.Key));
        Assert.Equal(["method", "username", "passwordenvvar"], ((JsonObject)account["auth"]!).Select(property => property.Key));
        Assert.Equal(["allowedrecipients", "maxrecipients", "maxperhour"], ((JsonObject)account["send"]!).Select(property => property.Key));

        // And the engine binds what was written.
        var options = Bind(document);
        var bound = Assert.Single(options.Accounts).Value;
        Assert.Empty(options.SectionProblems);
        Assert.Empty(options.AccountProblems);
        Assert.Equal("Work", options.DefaultAccount);
        Assert.Equal("/srv/other", options.CredentialsDirectory);
        Assert.False(options.Screening.WithholdRejected);
        Assert.Equal("you@gmail.com", bound.Address);
        Assert.Equal(EmailRights.Read, bound.Rights);
        Assert.Equal(993, bound.Incoming.Port);
        Assert.Equal(TransportSecurity.StartTls, bound.Outgoing.Security);
        Assert.Equal("OTHER_PASSWORD", bound.Auth.PasswordEnvVar);
        Assert.Equal(["you@example.com"], bound.Send.AllowedRecipients);
        Assert.Equal(10, bound.Send.MaxPerHour);
        Assert.False(bound.SaveSentCopy);
    }

    [Fact]
    public void A_field_cleared_removes_the_key_the_file_holds_whatever_its_case()
    {
        var document = AppSettingsDocument.Parse(LowerCaseKeys);
        var email = document.Email;

        email.DefaultAccount = null;
        email.CredentialsDirectory = " ";
        email.WithholdRejected = null;
        email.SetAccount(email.GetAccount("work")! with
        {
            Address = null,
            DisplayName = null,
            Rights = EmailRight.None,
            TimeoutSeconds = null,
            SaveSentCopy = null,
            IncomingProtocol = null,
            IncomingHost = null,
            IncomingPort = null,
            IncomingSecurity = null,
            OutgoingPort = null,
            Username = null,
            AllowedRecipients = [],
            MaxRecipients = null,
            MaxPerHour = null,
        });

        var section = (JsonObject)document.GetNode(EmailSection.SectionPath)!;
        var account = (JsonObject)section["accounts"]!["work"]!;
        Assert.Equal(["accounts"], section.Select(property => property.Key));
        Assert.Equal(["provider", "outgoing", "auth"], account.Select(property => property.Key));
        Assert.Equal(["host"], ((JsonObject)account["outgoing"]!).Select(property => property.Key));
        Assert.Equal(["method", "passwordenvvar"], ((JsonObject)account["auth"]!).Select(property => property.Key));

        email.RemoveAccount("work");

        Assert.Empty(section);
    }

    [Fact]
    public void An_account_added_or_renamed_under_keys_in_another_case_stays_in_the_dictionary_the_file_holds()
    {
        var document = AppSettingsDocument.Parse(LowerCaseKeys);
        var email = document.Email;

        email.RenameAccount("work", "job");
        email.SetAccount(new EmailAccountDefinition { Name = "perso", Provider = "Outlook" });

        var section = (JsonObject)document.GetNode(EmailSection.SectionPath)!;
        AssertNoTwinKeys(document.Root);
        Assert.Equal(["job", "perso"], ((JsonObject)section["accounts"]!).Select(property => property.Key));
        Assert.Equal(["job", "perso"], email.AccountNames);
        Assert.Equal("job", section["defaultaccount"]!.GetValue<string>());

        email.RemoveAccount("job");

        Assert.Null(email.DefaultAccount);
        Assert.Equal(["credentialsdirectory", "screening", "accounts"], section.Select(property => property.Key));
    }

    private static void AssertNoTwinKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject container:
                Assert.Equal(container.Count, container.Select(property => property.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                foreach (var (_, value) in container)
                    AssertNoTwinKeys(value);
                break;
            case JsonArray array:
                foreach (var item in array)
                    AssertNoTwinKeys(item);
                break;
        }
    }

    private static EmailToolsOptions Bind(AppSettingsDocument document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new EmailToolsOptions();

        EmailOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsEmail), options);
        return options;
    }

    /// <summary>Every field set to a value the engine reads back as written.</summary>
    private static EmailAccountDefinition FullAccount(string name) => new()
    {
        Name = name,
        Provider = "Custom",
        Address = "me@example.com",
        DisplayName = "Me",
        Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft | EmailRight.Send | EmailRight.Delete | EmailRight.Purge,
        TimeoutSeconds = 30,
        SaveSentCopy = false,
        IncomingProtocol = "Imap",
        IncomingHost = "imap.example.com",
        IncomingPort = 993,
        IncomingSecurity = "SslOnConnect",
        OutgoingProtocol = "Smtp",
        OutgoingHost = "smtp.example.com",
        OutgoingPort = 587,
        OutgoingSecurity = "StartTls",
        AuthMethod = "Password",
        Username = "me",
        PasswordEnvVar = "WORK_MAIL_PASSWORD",
        ClientId = "client-id",
        ClientSecretEnvVar = "CLIENT_SECRET",
        Tenant = "consumers",
        AllowedRecipients = ["*@example.com", "you@gmail.com"],
        MaxRecipients = 5,
        MaxPerHour = 20,
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }
}
