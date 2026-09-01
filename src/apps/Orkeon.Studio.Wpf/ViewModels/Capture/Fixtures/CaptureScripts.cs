namespace Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;

/// <summary>
/// The bodies the seeded world is made of: session artefacts on disk and event streams on the
/// wire. Held apart from <see cref="StudioFixture"/> because these are protocol shapes, and a
/// protocol shape that drifts should be findable in one file.
/// </summary>
internal static class CaptureScripts
{
    // ── the support session: composed, tried, passed ────────────────────────────────────────

    /// <summary>The conversation that produced the support brief.</summary>
    public static IReadOnlyList<string> SupportTranscript { get; } =
    [
        """{"role":"user","text":"J'aimerais que les questions de mes clients reçoivent une première réponse tout de suite."}""",
        """{"role":"assistant","text":"Entendu. Vos clients écrivent-ils par courriel, ou déposent-ils leurs demandes dans un dossier ?"}""",
        """{"role":"user","text":"Elles arrivent en fichiers texte dans docs/, une par demande."}""",
        """{"role":"assistant","text":"Et la réponse : un brouillon à relire, ou un envoi direct ?"}""",
        """{"role":"user","text":"Un brouillon. Je veux garder la main sur ce qui part."}""",
        """{"role":"assistant","text":"Je propose une équipe de trois : un lecteur qui classe la demande, un rédacteur qui rédige le brouillon, un relecteur qui vérifie le ton et les faits."}""",
    ];

    /// <summary>The structured brief the conversation settled on.</summary>
    public const string SupportBrief = """
        {
          "goal": "Rédiger un premier brouillon de réponse pour chaque demande client déposée dans docs/.",
          "acceptance": [
            { "id": "A1", "kind": "must", "statement": "Chaque demande reçoit un brouillon dans /output." },
            { "id": "A2", "kind": "must", "statement": "Le brouillon cite le passage de la demande auquel il répond." },
            { "id": "A3", "kind": "should", "statement": "Le ton reste celui d'une réponse professionnelle en français." }
          ]
        }
        """;

    /// <summary>The team the engine proposed.</summary>
    public const string SupportBlueprint = """
        {
          "crew": { "name": "assistant-support" },
          "rationale": "Trois rôles, parce que classer, rédiger et relire sont trois jugements différents : un seul agent qui ferait les trois relirait sa propre copie.",
          "agents": [
            { "key": "lecteur", "role": "Lecteur des demandes", "goal": "Comprendre ce que le client demande et le classer.", "tools": ["file_read", "directory_read"] },
            { "key": "redacteur", "role": "Rédacteur", "goal": "Écrire un brouillon de réponse qui cite la demande.", "tools": ["file_read", "file_write"] },
            { "key": "relecteur", "role": "Relecteur", "goal": "Vérifier le ton et les faits avant que le brouillon ne soit proposé.", "tools": ["file_read"] }
          ],
          "tasks": [
            { "key": "classer", "description": "Lire la demande et en dégager la question réelle.", "agent": "lecteur" },
            { "key": "rediger", "description": "Rédiger le brouillon de réponse.", "agent": "redacteur", "deliverable": "/output/reponses.md" },
            { "key": "relire", "description": "Relire le brouillon et signaler ce qui cloche.", "agent": "relecteur" }
          ]
        }
        """;

    /// <summary>A trial that passed, with one observation that is not a failure.</summary>
    public const string PassingVerdict = """
        {
          "score": 0.86,
          "passing": true,
          "judge": "llm",
          "findings": [
            { "id": "F1", "severity": "info", "acceptance": "A1", "statement": "Chaque demande a produit un brouillon.", "evidence": "3 fichiers écrits dans /output." },
            { "id": "F2", "severity": "info", "acceptance": "A2", "statement": "Les brouillons citent la demande.", "evidence": "Citation présente dans les 3 brouillons." },
            { "id": "F3", "severity": "minor", "acceptance": "A3", "statement": "Un brouillon tutoie le client.", "evidence": "reponses.md, deuxième paragraphe." }
          ],
          "suggestions": []
        }
        """;

    // ── the mail session: composed, tried, failed ───────────────────────────────────────────

    /// <summary>The conversation behind the mail-sorting session.</summary>
    public static IReadOnlyList<string> MailTranscript { get; } =
    [
        """{"role":"user","text":"Je reçois trop de courrier et je veux qu'il soit trié tout seul."}""",
        """{"role":"assistant","text":"Trié selon quoi — l'expéditeur, l'urgence, le sujet ?"}""",
        """{"role":"user","text":"L'urgence surtout. Ce qui attend peut attendre."}""",
    ];

    /// <summary>The brief, deliberately thinner than the support one.</summary>
    public const string MailBrief = """
        {
          "goal": "Trier le courrier entrant par urgence et en faire une liste quotidienne.",
          "acceptance": [
            { "id": "A1", "kind": "must", "statement": "Chaque message reçoit un niveau d'urgence." },
            { "id": "A2", "kind": "must", "statement": "La liste du jour tient sur un écran." }
          ]
        }
        """;

    /// <summary>A two-agent team — the trial found it too thin.</summary>
    public const string MailBlueprint = """
        {
          "crew": { "name": "tri-courrier" },
          "rationale": "Deux rôles suffisent : un qui lit et note l'urgence, un qui assemble la liste.",
          "agents": [
            { "key": "trieur", "role": "Trieur", "goal": "Attribuer un niveau d'urgence à chaque message.", "tools": ["file_read"] },
            { "key": "listeur", "role": "Listeur", "goal": "Assembler la liste du jour.", "tools": ["file_write"] }
          ],
          "tasks": [
            { "key": "trier", "description": "Lire chaque message et noter son urgence.", "agent": "trieur" },
            { "key": "lister", "description": "Écrire la liste du jour, la plus urgente en tête.", "agent": "listeur", "deliverable": "/output/courrier-du-jour.md" }
          ]
        }
        """;

    /// <summary>A trial that failed, with the suggestions that follow from it.</summary>
    public const string FailingVerdict = """
        {
          "score": 0.41,
          "passing": false,
          "judge": "llm",
          "findings": [
            { "id": "F1", "severity": "major", "acceptance": "A1", "statement": "Onze messages sur trente n'ont reçu aucune urgence.", "evidence": "Le trieur s'est arrêté à la limite d'itérations." },
            { "id": "F2", "severity": "minor", "acceptance": "A2", "statement": "La liste tient sur deux écrans.", "evidence": "courrier-du-jour.md, 68 lignes." }
          ],
          "suggestions": [
            { "target": "trieur", "change": "Traiter le courrier par lots de dix plutôt qu'en une passe.", "reason": "La limite d'itérations est atteinte avant la fin de la boîte." },
            { "target": "listeur", "change": "Ne lister que les deux niveaux d'urgence les plus hauts.", "reason": "La liste doit tenir sur un écran, ce que trente entrées ne permettent pas." }
          ]
        }
        """;

    /// <summary>The trial's own cost, folded into the verdict card's chips.</summary>
    public const string LastRun = """
        { "durationMs": 48210, "tokens": 12840, "cacheHitTokens": 5120, "cacheMissTokens": 7720 }
        """;

    // ── the wire ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A compose that reaches the dry pause: the proposal arrives, and the stream closes on
    /// <c>paused</c> — the state where the wizard offers both the try-the-team and the
    /// adopt-without-trial buttons.
    /// </summary>
    public static IReadOnlyList<string> ForgeComposeToDryPause { get; } =
    [
        """{"v":2,"seq":1,"ts":"2026-08-28T06:40:00Z","kind":"session.started","slug":"assistant-support","dir":"","format":"yaml","resumed":false,"engine":"1.0.0-rc.2"}""",
        """{"v":2,"seq":2,"ts":"2026-08-28T06:40:02Z","kind":"stage.entered","stage":"brief"}""",
        """{"v":2,"seq":3,"ts":"2026-08-28T06:40:05Z","kind":"assistant.message","text":"Je regarde ce que vous avez décrit et je compose une équipe."}""",
        """{"v":2,"seq":4,"ts":"2026-08-28T06:40:31Z","kind":"cost.updated","promptTokens":1840,"completionTokens":620,"estimatedTokens":2460}""",
        """{"v":2,"seq":5,"ts":"2026-08-28T06:40:44Z","kind":"stage.entered","stage":"blueprint"}""",
    ];

    /// <summary>A run that goes all the way through, for the Run screen.</summary>
    public static IReadOnlyList<string> RunToSuccess { get; } =
    [
        """{"v":2,"seq":1,"ts":"2026-08-28T07:10:00Z","kind":"run.started","crew":"veille-concurrentielle","tasks":3}""",
        """{"v":2,"seq":2,"ts":"2026-08-28T07:10:14Z","kind":"task.completed","task":"collecter","agent":"Veilleur","ok":true}""",
        """{"v":2,"seq":3,"ts":"2026-08-28T07:10:41Z","kind":"cost.updated","promptTokens":9200,"completionTokens":2100,"estimatedTokens":11300}""",
        """{"v":2,"seq":4,"ts":"2026-08-28T07:10:52Z","kind":"task.completed","task":"analyser","agent":"Analyste","ok":true}""",
        """{"v":2,"seq":5,"ts":"2026-08-28T07:11:08Z","kind":"task.completed","task":"rediger","agent":"Rédacteur","ok":true}""",
        """{"v":2,"seq":6,"ts":"2026-08-28T07:11:14Z","kind":"run.finished","status":"ok","exitCode":0}""",
    ];
}
