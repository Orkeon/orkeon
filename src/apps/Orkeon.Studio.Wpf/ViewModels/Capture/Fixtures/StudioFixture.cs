using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;

/// <summary>
/// The scenario the whole collection tells: a small consulting practice that watches its market,
/// writes a weekly note and sorts its invoices.
/// <para>
/// Invented once, here, so forty screenshots agree with each other. Every awkward state a reviewer
/// needs to see is built into the data rather than arranged by a stop: a team that names a folder
/// nobody declared, a folder that does not exist, a trial that failed, a profile whose key was
/// never stored, a doctor with one warning and one failure.
/// </para>
/// </summary>
internal static class StudioFixture
{
    /// <summary>
    /// The code the scripted device sign-in shows (STUDIO-70): made up, and recognisably so. A
    /// real device code is a secret while it lives; none ever enters a campaign.
    /// </summary>
    public const string DeviceSignInCode = "DEMO-1234";

    /// <summary>Name of the profile the assistant runs on; the wizard's gate keys on it.</summary>
    public const string AssistantProfile = "Studio";

    /// <summary>The variable the assistant's DeepSeek key is remembered under.</summary>
    public const string DeepSeekKeyVariable = "DEEPSEEK_API_KEY";

    /// <summary>
    /// The session parked at the dry pause: a blueprint and no verdict.
    /// <para>
    /// The absence of a verdict is the point, not an omission — <c>--dry</c> stops BEFORE the
    /// trial. It is also what keeps the milestone at Propose, so a resume lands on the Composer
    /// rather than skipping ahead to an arbitration that never happened.
    /// </para>
    /// </summary>
    public const string DryPauseSessionSlug = "assistant-support";

    /// <summary>The session that was tried and passed.</summary>
    public const string PassingSessionSlug = "veille-matinale";

    /// <summary>The session whose trial failed, with suggestions to show.</summary>
    public const string FailingSessionSlug = "tri-courrier";

    /// <summary>The session that adopted a team — the one «Modifier» reopens.</summary>
    public const string PromotedSessionSlug = "veille-adoptee";

    /// <summary>The team whose sidecar names a folder the settings do not declare.</summary>
    public const string BlockedTeamSlug = "tri-factures";

    /// <summary>
    /// The team nobody launched for a month and a half (STUDIO-32): younger than the archive
    /// suggestion's sixty days, so My teams proposes nothing by default — the stop that shows the
    /// proposal lowers the threshold, the way a user does in Settings › Studio.
    /// </summary>
    public const string StaleTeamSlug = "inventaire-stock";

    /// <summary>The archived team (STUDIO-31, STUDIO-32): what the Archives view lists.</summary>
    public const string ArchivedTeamSlug = "veille-salon";

    /// <summary>
    /// The team whose sidecar carries a pasted README (STUDIO-16): a name of several lines,
    /// headings and code included, and a need forty lines long — what a team created from a
    /// long brief really looks like on disk. The run card, the my-teams card and the history
    /// have to show it on one line, and the description folded.
    /// </summary>
    public const string PastedReadmeTeamSlug = "extraction-factures";

    /// <summary>The sidecar name of <see cref="PastedReadmeTeamSlug"/>: a whole README.</summary>
    public const string PastedReadmeName = """
        # Extraction des factures fournisseurs déposées en `docs/`, classées par mois et par fournisseur, avec un **contrôle** des doublons

        > Un README entier collé dans le champ « Nom de l'équipe ».

        ## Ce que fait l'équipe

        - lit chaque PDF déposé
        - en extrait le fournisseur, la date et le montant
        """;

    /// <summary>The sidecar need of <see cref="PastedReadmeTeamSlug"/>: forty lines of README.</summary>
    public const string PastedReadmeDescription = """
        # Extraction des factures fournisseurs

        Chaque matin, les factures déposées dans `docs/` sont lues une à une : le fournisseur,
        la date, le montant hors taxes et la TVA sont extraits, puis chaque facture est classée
        dans `sortie/` par mois et par fournisseur, avec un contrôle des doublons.

        ## Fonctionnement

        1. Un **lecteur** parcourt `docs/` et ne retient que les PDF non encore traités.
        2. Un **extracteur** lit chaque document et remplit une fiche structurée.
        3. Un **vérificateur** compare la fiche aux factures déjà classées.
        4. Un **archiviste** range le fichier et tient le journal du jour.

        ## Fiche produite

        ```json
        {
          "fournisseur": "Papeterie Lemoine",
          "date": "2026-08-27",
          "montant_ht": 184.20,
          "tva": 36.84,
          "doublon": false
        }
        ```

        ## Règles

        - Une facture sans date lisible est mise de côté dans `sortie/a-verifier/`.
        - Un doublon n'est jamais écrasé : le second exemplaire est signalé dans le journal.
        - Les montants sont relus deux fois quand la TVA ne tombe pas juste.
        - Rien n'est supprimé de `docs/` : l'archiviste copie, il ne déplace pas.

        ## Dossiers

        | Dossier | Rôle |
        |---|---|
        | `docs/` | les factures déposées, en lecture |
        | `sortie/` | le classement produit, en écriture |

        ## Planification

        L'équipe tourne à sept heures ; un lancement à la main reste possible depuis Studio.
        Le journal de la veille est relu avant de commencer, pour reprendre ce qui a été mis
        de côté.
        """;

    /// <summary>The wizard's need, worded once so several stops agree.</summary>
    public const string Need = "Je veux résumer chaque matin les nouveautés de mes concurrents.";

    /// <summary>What the assistant has said, so a thread with turns in it is worth reading.</summary>
    public static IReadOnlyList<string> AssistantTurns { get; } =
    [
        "Bonjour. Décrivez-moi ce que vous voulez obtenir, avec vos mots — je m'occupe de la forme.",
        "D'accord : une note quotidienne à partir de ce que vos concurrents publient. "
        + "Dans quel dossier vos sources arrivent-elles ?",
        "Parfait. Je compose une équipe de trois : un veilleur qui lit, un analyste qui trie, "
        + "un rédacteur qui écrit la note.",
    ];

    /// <summary>
    /// Pinned so two runs of the campaign produce the same dates — and the same elapsed time for
    /// a run in flight, whose clock stops here (STUDIO-34).
    /// </summary>
    internal static readonly DateTimeOffset Now = new(2026, 8, 28, 7, 12, 0, TimeSpan.Zero);

    /// <summary>The populated machine.</summary>
    public static CaptureWorldPlan Seeded { get; } = new()
    {
        Name = "seeded",
        // STUDIO-39: a slice of the use-case catalogue, and a search session that finds close cases.
        UseCaseCatalog = CaptureScripts.UseCaseCatalog,
        UseCaseReady = CaptureScripts.UseCaseReady,
        UseCaseAnswer = CaptureScripts.UseCaseAnswer,
        // «disparu» is deliberately absent: one declared folder must be genuinely unreadable, so
        // the red row in the settings is a real verdict rather than a simulated one.
        DataFolders = ["docs", "sortie", "archives"],
        DeclaredMounts =
        [
            new("docs", "/docs", "ro"),
            new("sortie", "/output", "rw"),
            new("archives", "/archives", "ro"),
            new("disparu", "/perdu", "ro"),
        ],
        LlmJson = """{ "Provider": "deepseek", "Model": "deepseek-chat" }""",
        // STUDIO-67: the E-mail tab photographs a preset account and a custom one — the first
        // shows what a novice declares, the second every field of the expert form. Both are
        // accounts the run would use: no shot carries a warning the seed did not mean.
        EmailJson = """{ "DefaultAccount": "perso", "Accounts": { "perso": { "Provider": "Gmail", "Address": "camille.durand@gmail.com", "DisplayName": "Camille Durand", "Rights": "Read, Organize, Draft", "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } }, "hotmail": { "Provider": "Outlook", "Address": "camille.durand@outlook.com", "Rights": "Read, Organize", "Auth": { "ClientId": "11111111-2222-3333-4444-555555555555" } }, "bureau": { "Provider": "Custom", "Address": "camille@atelier-durand.example", "Rights": "Read, Organize, Draft, Send", "Incoming": { "Host": "imap.atelier-durand.example" }, "Outgoing": { "Host": "smtp.atelier-durand.example", "Security": "StartTls" }, "Auth": { "PasswordEnvVar": "BUREAU_MAIL_PASSWORD" }, "Send": { "AllowedRecipients": [ "*@atelier-durand.example", "comptable@cabinet-martin.example" ], "MaxPerHour": 20 } } } }""",
        ExtraSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RateLimiting"] = """{ "RequestsPerMinute": 60, "MaxConcurrent": 4 }""",
            ["Logging"] = """{ "LogLevel": { "Default": "Information" } }""",
            // STUDIO-21: the MCP tab photographs both transports at once.
            ["MCP"] = """{ "Servers": { "fichiers": { "Command": "npx", "Args": ["-y", "@modelcontextprotocol/server-filesystem", "/data/docs"], "Env": { "NODE_ENV": "production" } }, "distant": { "Transport": "Sse", "Url": "https://mcp.example.com/rpc" } } }""",
        },
        Teams =
        [
            new(
                "veille-concurrentielle",
                new StudioTeamMetadata
                {
                    Name = "Veille concurrentielle",
                    Description = "Relit la presse du secteur chaque matin et résume ce qui a bougé.",
                    Profile = AssistantProfile,
                    Schedule = "daily@07:00",
                },
                [new("docs", "/docs", "ro"), new("sortie", "/output", "rw")],
                ["veilleur.yaml", "analyste.yaml", "redacteur.yaml", "relecteur.yaml"],
                ["collecter.yaml", "analyser.yaml", "rediger.yaml"]),
            new(
                "rapport-hebdo",
                new StudioTeamMetadata
                {
                    Name = "Rapport hebdomadaire",
                    Description = "Assemble la note de la semaine à partir des archives.",
                    Profile = "Local",
                },
                // Writes into its own output/ (STUDIO-14): the sidecar carries the team-relative
                // entry, and Settings › Authorized folders lists it in the read-only
                // « Team folders » section — the seed is what the stop photographs.
                [new("archives", "/archives", "ro"), MountSeed.InTeam("/output", "rw")],
                ["lecteur.yaml", "synthetiseur.yaml", "editeur.yaml"],
                ["lire.yaml", "synthetiser.yaml"]),
            new(
                BlockedTeamSlug,
                new StudioTeamMetadata
                {
                    Name = "Tri des factures",
                    Description = "Classe les factures entrantes par fournisseur et par mois.",
                },
                // Names a folder the settings never declared: the red chips and the refusal to
                // launch come out of the data, not out of a stop that arranges them.
                [new("comptabilite", "/factures", "rw")],
                ["trieur.yaml", "verificateur.yaml"],
                ["classer.yaml"]),
            new(
                PastedReadmeTeamSlug,
                new StudioTeamMetadata
                {
                    // Written raw on purpose: this is the sidecar a long brief left behind
                    // before adoption normalized the name, and the display has to cope with it.
                    Name = PastedReadmeName,
                    Description = PastedReadmeDescription,
                    Profile = "Local",
                    Schedule = "daily@07:00",
                    // Run from Studio early this morning (STUDIO-31, D-05): the list goes by last
                    // activity (STUDIO-32), and this card stays high enough to be seen folded.
                    LastRunAt = Now.AddHours(-3),
                },
                [new("docs", "/docs", "ro"), new("sortie", "/output", "rw")],
                ["lecteur.yaml", "extracteur.yaml", "verificateur.yaml", "archiviste.yaml"],
                ["lire.yaml", "extraire.yaml", "verifier.yaml", "classer.yaml"]),
            new(
                StaleTeamSlug,
                new StudioTeamMetadata
                {
                    Name = "Inventaire du stock",
                    Description = "Recense chaque trimestre les fournitures du cabinet et signale ce qu'il faut recommander.",
                    // Its last run from Studio, older than the history the fixture keeps.
                    LastRunAt = Now.AddDays(-45),
                },
                [new("archives", "/archives", "ro")],
                ["compteur.yaml", "acheteur.yaml"],
                ["recenser.yaml"]),
            new(
                ArchivedTeamSlug,
                new StudioTeamMetadata
                {
                    Name = "Veille du salon 2025",
                    Description = "Suivait les annonces des exposants avant le salon professionnel de juin.",
                    LastRunAt = Now.AddDays(-110),
                    Archived = true,
                    ArchivedAt = Now.AddDays(-20),
                },
                [new("docs", "/docs", "ro")],
                ["veilleur.yaml", "redacteur.yaml"],
                ["collecter.yaml"]),
        ],
        History =
        [
            new("veille-concurrentielle", Now.AddHours(-2), 0, RunOutcome.Success, 74.2, 18_430, 11_200),
            new("rapport-hebdo", Now.AddDays(-1), 0, RunOutcome.Success, 212.8, 46_100, 9_800),
            new("veille-concurrentielle", Now.AddDays(-1).AddHours(-2), 2, RunOutcome.RuntimeError, 31.4, 6_020, 0),
            new("veille-concurrentielle", Now.AddDays(-2).AddHours(-2), 0, RunOutcome.Success, 68.9, 17_940, 12_600),
            new("rapport-hebdo", Now.AddDays(-3), 130, RunOutcome.Cancelled, 12.1, null, null),
            new("veille-concurrentielle", Now.AddDays(-3).AddHours(-2), 0, RunOutcome.Success, 71.5, 18_020, 10_400),
            new("tri-factures", Now.AddDays(-5), 1, RunOutcome.ScriptError, 3.2, null, null),
        ],
        Profiles = new ModelProfileSet
        {
            Profiles =
            [
                // On its real endpoint, with its key remembered (below): the default and the
                // assistant's account, whose balance the status bar reads at startup (STUDIO-35).
                new()
                {
                    Name = AssistantProfile,
                    Provider = LlmPresets.DeepSeek,
                    Model = "deepseek-chat",
                    BaseUrl = LlmProviderEndpoints.DeepSeek,
                    KeyEnvName = DeepSeekKeyVariable,
                },
                new() { Name = "Local", Provider = LlmPresets.Ollama, Model = "qwen3:8b", BaseUrl = "http://localhost:11434/v1" },
                // No key stored: the orange chip on the API-keys card is what a first-run cloud
                // profile actually looks like.
                new() { Name = "Cloud", Provider = LlmPresets.OpenAI, Model = "gpt-4.1", KeyEnvName = "ORKEON_Llm__ApiKey" },
                new() { Name = "Éco", Provider = LlmPresets.Ollama, Model = "phi4", BaseUrl = "http://localhost:11434/v1" },
            ],
            DefaultProfile = AssistantProfile,
            StudioProfile = AssistantProfile,
        },
        ApiKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DeepSeekKeyVariable] = "capture-only-deepseek-key",
            // STUDIO-69: the Gmail account's app password is kept, the custom account's is not —
            // which is exactly what the scripted `email accounts` below says of the two.
            ["GMAIL_APP_PASSWORD"] = "capture-only-gmail-app-password",
        },
        // The engine lists the accounts sorted by name, with its own sentence for the one that
        // lacks its password and for the one that was never signed in (STUDIO-70): one ready, two
        // not, the states of the tab worth a pixel.
        EmailAccountsJson = """
            [
              { "name": "bureau", "address": "camille@atelier-durand.example", "provider": "Custom", "reads": "Imap", "sends": "Smtp", "rights": "Read, Organize, Draft, Send", "auth": "Password", "default": false, "ready": false, "problem": "The password of e-mail account 'bureau' is read from the environment variable BUREAU_MAIL_PASSWORD, which is set neither in the process environment nor in the user's." },
              { "name": "hotmail", "address": "camille.durand@outlook.com", "provider": "Outlook", "reads": "Graph", "sends": "Graph", "rights": "Read, Organize", "auth": "OAuth2", "default": false, "ready": false, "problem": "E-mail account 'hotmail' needs an OAuth sign-in: run `orkeon email login hotmail` in a terminal." },
              { "name": "perso", "address": "camille.durand@gmail.com", "provider": "Gmail", "reads": "Imap", "sends": "Smtp", "rights": "Read, Organize, Draft", "auth": "Password", "default": true, "ready": true, "problem": null }
            ]
            """,
        // STUDIO-70: what `orkeon email login --events jsonl` says of the Outlook account — the
        // page and a made-up code, a quarter of an hour to type it — before it waits.
        EmailLoginStream =
        [
            $$"""{"v":2,"seq":1,"ts":"2026-09-24T08:00:00Z","kind":"email.login.device_code","verification_uri":"https://microsoft.com/devicelogin","user_code":"{{DeviceSignInCode}}","expires_in":900}""",
        ],
        DoctorJson = DoctorWithIssues,
        Sessions = [DryPauseSession, PassingSession, FailingSession, PromotedSession],
        RunStream = CaptureScripts.RunToSuccess,
        ForgeStream = CaptureScripts.ForgeComposeToFolders,
    };

    /// <summary>
    /// The first-run machine: nothing anywhere, and no CLI installed.
    /// <para>
    /// A world rather than a teardown. "Empty" then becomes something a stop declares, instead of
    /// something it has to un-populate — and it delivers the assistant gate, the missing-CLI
    /// banner and every empty card for free.
    /// </para>
    /// </summary>
    public static CaptureWorldPlan Pristine { get; } = new()
    {
        Name = "pristine",
        CliInstalled = false,
        LlmJson = "{ }",
        DoctorJson = "[]",
    };

    /// <summary>Six checks with one warning and one failure — the shape worth photographing.</summary>
    private const string DoctorWithIssues = """
        [
          { "check": "cli", "status": "ok", "detail": "orkeon 1.0.0-rc.2" },
          { "check": "appsettings", "status": "ok", "detail": "chargé depuis le dossier de configuration" },
          { "check": "mounts", "status": "warn", "detail": "un dossier déclaré est introuvable : /perdu" },
          { "check": "llm-provider", "status": "ok", "detail": "deepseek / deepseek-chat" },
          { "check": "llm-reachability", "status": "fail", "detail": "le point de terminaison a refusé la connexion" },
          { "check": "workspace", "status": "ok", "detail": "accessible en écriture" }
        ]
        """;

    /// <summary>Composed, never tried: the state "Adopt without trying" was invented for.</summary>
    private static SessionSeed DryPauseSession => new()
    {
        Slug = DryPauseSessionSlug,
        Id = Guid.Parse("3c6a1f0e-2d4b-4a8c-9e71-5b0d2f6a8c41"),
        Title = "Assistant de support",
        State = "Test",
        Status = "Active",
        UpdatedAt = "2026-08-28T06:41:00Z",
        Transcript = CaptureScripts.SupportTranscript,
        Brief = CaptureScripts.SupportBrief,
        Blueprint = CaptureScripts.SupportBlueprint,
    };

    /// <summary>
    /// Tried, and it passed. Kept at state <c>Test</c> WITH a verdict on disk: that is the
    /// looped-back session the engine really produces, and the one shape a resume can rebuild
    /// without starting a process.
    /// </summary>
    private static SessionSeed PassingSession => new()
    {
        Slug = PassingSessionSlug,
        Id = Guid.Parse("8e2d5b7a-1c3f-4e69-a0b4-7d9c2e5f1a36"),
        Title = "Veille matinale",
        State = "Test",
        Status = "Active",
        UpdatedAt = "2026-08-28T06:20:00Z",
        Transcript = CaptureScripts.SupportTranscript,
        Brief = CaptureScripts.SupportBrief,
        Blueprint = CaptureScripts.SupportBlueprint,
        Verdict = CaptureScripts.PassingVerdict,
        LastRun = CaptureScripts.LastRun,
    };

    /// <summary>The session behind an adopted team — what «Modifier» on a team card reopens.</summary>
    private static SessionSeed PromotedSession => new()
    {
        Slug = PromotedSessionSlug,
        Id = Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"),
        Title = "Veille concurrentielle",
        State = "Verdict",
        Status = "Promoted",
        UpdatedAt = "2026-08-26T09:00:00Z",
        PromotedToTeamSlug = "veille-concurrentielle",
        Transcript = CaptureScripts.SupportTranscript,
        Brief = CaptureScripts.SupportBrief,
        Blueprint = CaptureScripts.SupportBlueprint,
        Verdict = CaptureScripts.PassingVerdict,
        LastRun = CaptureScripts.LastRun,
    };

    private static SessionSeed FailingSession => new()
    {
        Slug = FailingSessionSlug,
        Id = Guid.Parse("b47e0c29-5a1d-4f38-8c6e-2a9f0d7b3e15"),
        Title = "Tri du courrier",
        State = "Verdict",
        Status = "Active",
        UpdatedAt = "2026-08-27T15:02:00Z",
        Transcript = CaptureScripts.MailTranscript,
        Brief = CaptureScripts.MailBrief,
        Blueprint = CaptureScripts.MailBlueprint,
        Verdict = CaptureScripts.FailingVerdict,
        LastRun = CaptureScripts.LastRun,
    };
}
