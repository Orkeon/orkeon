using System.Text.Json;
using System.Text.Json.Nodes;

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
    // ── the use-case catalogue (STUDIO-39): what `orkeon usecases` answers ─────────────────

    /// <summary>
    /// The <c>usecases.catalog</c> line <c>usecases list</c> prints: a slice of the real catalogue
    /// (STUDIO-36/37) — every category, the finance cases that are reference only, the one case that
    /// needs a third-party key — in the five languages, so the language sweep photographs real text.
    /// </summary>
    /// <remarks>Built on each read, never in a static initializer: the sheets are declared below it.</remarks>
    public static string UseCaseCatalog => BuildUseCaseCatalog();

    /// <summary>The line a search session opens with.</summary>
    public static string UseCaseReady =>
        $$$"""{"v":2,"seq":1,"ts":"2026-08-28T07:12:00Z","kind":"usecases.ready","count":{{{UseCaseSheets.Length}}},"languages":["fr","en","es","de","zh-Hans"],"modes":{"fr":"bm25","en":"hybrid","es":"bm25","de":"bm25","zh-Hans":"bm25"}}""";

    /// <summary>
    /// The answer to any query of the session, as the CLI spells one: the competitor watch and the
    /// adaptive summary on the terms of <see cref="StudioFixture.Need"/> that only they carry, the
    /// email triage on words every sentence shares — which the wizard does not count as close.
    /// </summary>
    public static IEnumerable<string> UseCaseAnswer(string queryLine)
    {
        var query = JsonNode.Parse(queryLine);
        if (query?["kind"]?.GetValue<string>() != "usecases.query")
            return [];

        var answer = new JsonObject
        {
            ["v"] = 2,
            ["seq"] = 2,
            ["ts"] = "2026-08-28T07:12:01Z",
            ["kind"] = "usecases.results",
            ["correlationId"] = query["correlationId"]?.GetValue<string>(),
            ["query"] = query["text"]?.GetValue<string>(),
            ["lang"] = "fr",
            ["langSource"] = "detected",
            ["mode"] = "bm25",
            ["results"] = new JsonArray(
                Result(1, "06-competitive-intelligence", "concurrents", "mes", "de"),
                Result(2, "30-adaptive-summary", "resumer", "de"),
                Result(3, "03-email-pipeline", "je", "mes")),
        };
        return [answer.ToJsonString(WireOptions)];

        static JsonObject Result(int rank, string id, params string[] terms) => new()
        {
            ["rank"] = rank,
            ["id"] = id,
            ["score"] = 4.0 / rank,
            ["reason"] = "terms",
            ["terms"] = new JsonArray([.. terms.Select(term => (JsonNode?)term)]),
        };
    }

    /// <summary>The CLI's own escaping on the wire: an accented letter stays that letter.</summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] UseCaseLanguages = ["fr", "en", "es", "de", "zh-Hans"];

    private static string BuildUseCaseCatalog()
    {
        var sheets = new JsonArray();
        foreach (var sheet in UseCaseSheets)
        {
            sheets.Add(new JsonObject
            {
                ["agents"] = 3,
                ["category"] = sheet.Category,
                ["format"] = "yaml",
                ["hasSampleData"] = false,
                ["id"] = sheet.Id,
                ["importable"] = sheet.Importable,
                ["mounts"] = new JsonArray("./output:/output:rw"),
                ["number"] = int.Parse(sheet.Id[..sheet.Id.IndexOf('-', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture),
                ["problem"] = Texts(sheet.Problems),
                ["process"] = sheet.Process,
                ["requiresKeys"] = new JsonArray([.. sheet.Keys.Select(key => (JsonNode?)key)]),
                ["requiresNetwork"] = sheet.Network,
                ["tags"] = new JsonArray(),
                ["tasks"] = 3,
                ["title"] = Texts(sheet.Titles),
                ["tools"] = new JsonArray("file_read", "file_write"),
            });
        }

        return new JsonObject
        {
            ["v"] = 2,
            ["seq"] = 1,
            ["ts"] = "2026-08-28T07:12:00Z",
            ["kind"] = "usecases.catalog",
            ["count"] = UseCaseSheets.Length,
            ["languages"] = new JsonArray([.. UseCaseLanguages.Select(language => (JsonNode?)language)]),
            ["useCases"] = sheets,
        }.ToJsonString(WireOptions);

        static JsonObject Texts(string[] texts)
        {
            var node = new JsonObject();
            for (var i = 0; i < UseCaseLanguages.Length; i++)
                node[UseCaseLanguages[i]] = texts[i];
            return node;
        }
    }

    /// <summary>One sheet of the slice; <paramref name="Titles"/> and <paramref name="Problems"/> in the manifest's language order.</summary>
    private sealed record UseCaseSheet(
        string Id,
        string Category,
        string Process,
        bool Network,
        string[] Keys,
        bool Importable,
        string[] Titles,
        string[] Problems);

    private static readonly UseCaseSheet[] UseCaseSheets =
    [
        new("06-competitive-intelligence", "01-enterprise", "parallel", Network: true, Keys: [], Importable: true,
            Titles: ["Veille concurrentielle", "Competitor monitoring", "Vigilancia de la competencia", "Wettbewerbsbeobachtung", "竞争对手监测"],
            Problems:
            [
                "Suivre les prix, annonces et brevets de mes concurrents et repérer ce qui change",
                "Track my competitors' prices, announcements and patents, and spot what changes",
                "Seguir los precios, anuncios y patentes de mis competidores y detectar qué cambia",
                "Preise, Ankündigungen und Patente meiner Wettbewerber verfolgen und Veränderungen erkennen",
                "跟踪竞争对手的价格、公告和专利，找出其中的变化",
            ]),
        new("03-email-pipeline", "01-enterprise", "sequential", Network: false, Keys: [], Importable: true,
            Titles: ["Tri et réponse aux e-mails", "Email triage and replies", "Clasificación y respuesta de correos", "E-Mail-Sortierung mit Antwortentwürfen", "邮件分类与回复"],
            Problems:
            [
                "Trier mes e-mails par urgence et préparer des réponses que je valide avant envoi",
                "Sort my emails by urgency and draft replies for me to approve before sending",
                "Ordenar mis correos por urgencia y preparar respuestas que apruebo antes de enviarlas",
                "Meine E-Mails nach Dringlichkeit sortieren und Antworten zur Freigabe vorbereiten",
                "按紧急程度整理我的邮件，起草回复，经我确认后再发送",
            ]),
        new("16-interactive-qa", "01-enterprise", "sequential", Network: true, Keys: ["ORKEON_TAVILY_API_KEY"], Importable: true,
            Titles: ["Réponses avec sources", "Answers with sources", "Respuestas con fuentes", "Antworten mit Quellen", "附出处的问答"],
            Problems:
            [
                "Poser une question et obtenir une réponse claire, dans ma langue, avec ses sources",
                "Ask a question and get a clear answer, in my language, with its sources",
                "Hacer una pregunta y obtener una respuesta clara, en mi idioma, con sus fuentes",
                "Eine Frage stellen und eine klare Antwort in meiner Sprache samt Quellen erhalten",
                "提出一个问题，用我的语言得到清晰的回答，并附上出处",
            ]),
        new("30-adaptive-summary", "02-science-research", "sequential", Network: false, Keys: [], Importable: true,
            Titles: ["Résumé adapté au lecteur", "Summary tailored to the reader", "Resumen adaptado al lector", "Zusammenfassung nach Kenntnisstand", "按读者水平定制的摘要"],
            Problems:
            [
                "Résumer un document à mon niveau, du plus technique au plus pédagogique",
                "Summarize a document at my level, from highly technical to beginner-friendly",
                "Resumir un documento a mi nivel, de lo más técnico a lo más didáctico",
                "Ein Dokument auf meinem Niveau zusammenfassen, von sehr fachlich bis einsteigerfreundlich",
                "按我的水平总结一份文档，可以很专业，也可以通俗易懂",
            ]),
        new("16-prisma-meta-analysis", "02-science-research", "sequential", Network: true, Keys: [], Importable: true,
            Titles: ["Revue systématique de la littérature", "Systematic literature review", "Revisión sistemática de la literatura", "Systematische Literaturübersicht", "系统性文献综述"],
            Problems:
            [
                "Synthétiser les études sur une question, en justifiant chaque étude retenue ou écartée",
                "Synthesize the studies on a question, justifying each study kept or left out",
                "Sintetizar los estudios sobre una pregunta, justificando cada estudio incluido o descartado",
                "Die Studien zu einer Frage auswerten und jede Aufnahme oder jeden Ausschluss begründen",
                "综合分析某个问题的相关研究，并说明每项研究纳入或排除的理由",
            ]),
        new("31-algo-trading", "03-finance-trading", "hierarchical", Network: true, Keys: [], Importable: false,
            Titles: ["Salle des marchés simulée", "Simulated trading desk", "Mesa de operaciones simulada", "Simulierter Handelsraum", "模拟交易室"],
            Problems:
            [
                "Simuler une stratégie de trading complète, de l'analyse du marché aux achats et ventes, sans argent réel",
                "Simulate a complete trading strategy, from market analysis to buying and selling, without real money",
                "Simular una estrategia de trading completa, del análisis del mercado a la compra y venta, sin dinero real",
                "Eine vollständige Handelsstrategie von der Marktanalyse bis zum Kauf und Verkauf simulieren, ohne echtes Geld",
                "完整模拟一套交易策略，从市场分析到买卖操作，全程不动用真实资金",
            ]),
        new("40-invoice-processing", "03-finance-trading", "sequential", Network: false, Keys: [], Importable: false,
            Titles: ["Contrôle des factures fournisseurs", "Supplier invoice checks", "Control de facturas de proveedores", "Prüfung von Lieferantenrechnungen", "供应商发票核对"],
            Problems:
            [
                "Vérifier que chaque facture fournisseur correspond bien à une commande et à une livraison",
                "Check that every supplier invoice matches an order and a delivery",
                "Comprobar que cada factura de proveedor corresponde a un pedido y a una entrega",
                "Prüfen, ob jede Lieferantenrechnung zu einer Bestellung und einer Lieferung passt",
                "核实每张供应商发票都能对应到一笔订单和一次收货",
            ]),
        new("46-diagnostic-assistant", "04-health-wellness", "sequential", Network: true, Keys: [], Importable: true,
            Titles: ["Pistes de diagnostic pour le médecin", "Diagnostic leads for doctors", "Pistas diagnósticas para la consulta", "Diagnoseansätze für die Arztpraxis", "供医生参考的诊断思路"],
            Problems:
            [
                "Organiser les symptômes de mes patients et les pistes à vérifier, en me soumettant chaque étape",
                "Organize my patients' symptoms and the leads to check, submitting each step for my approval",
                "Ordenar los síntomas de mis pacientes y las pistas por verificar, sometiendo cada paso a mi aprobación",
                "Die Symptome meiner Patienten und die zu prüfenden Diagnoseansätze ordnen und mir jeden Schritt zur Freigabe vorlegen",
                "整理我接诊患者的症状和待核实的诊断思路，每一步都交由我确认",
            ]),
        new("56-adaptive-tutor", "05-education", "sequential", Network: false, Keys: [], Importable: true,
            Titles: ["Tutorat personnalisé", "Personalized tutoring", "Tutoría personalizada", "Persönliche Lernbegleitung", "个性化辅导"],
            Problems:
            [
                "Proposer des explications et des exercices adaptés au niveau et à la façon d'apprendre d'un élève",
                "Provide explanations and exercises matched to a learner's level and way of learning",
                "Proponer explicaciones y ejercicios adaptados al nivel y a la forma de aprender de un alumno",
                "Erklärungen und Übungen anbieten, die zu Niveau und Lernstil der Lernenden passen",
                "根据学习者的水平和学习方式，提供量身定制的讲解和练习",
            ]),
        new("66-cicd-pipeline", "06-engineering-devops", "sequential", Network: true, Keys: [], Importable: true,
            Titles: ["Mise en ligne de code contrôlée", "Controlled code release", "Publicación controlada de código", "Kontrollierte Code-Veröffentlichung", "代码上线把关"],
            Problems:
            [
                "Contrôler une modification de code avant sa mise en ligne, et revenir en arrière si elle échoue",
                "Check a code change before it goes live, and roll it back if it fails",
                "Revisar un cambio de código antes de publicarlo y deshacerlo si falla",
                "Eine Codeänderung vor der Veröffentlichung prüfen und bei einem Fehler zurücknehmen",
                "上线前检查代码改动，出问题时退回原版本",
            ]),
        new("76-narrative-studio", "07-creative-media", "sequential", Network: false, Keys: [], Importable: true,
            Titles: ["Studio d'écriture", "Story writing studio", "Estudio de escritura", "Schreibstudio", "故事写作工作室"],
            Problems:
            [
                "Écrire un chapitre de mon histoire, de l'intrigue aux dialogues, et en vérifier la cohérence",
                "Write a chapter of my story, from plot to dialogue, and check that it holds together",
                "Escribir un capítulo de mi historia, de la trama a los diálogos, y comprobar su coherencia",
                "Ein Kapitel meiner Geschichte schreiben, von der Handlung bis zu den Dialogen, und seine Stimmigkeit prüfen",
                "为我的故事写一章，从情节到对白，并检查是否前后连贯",
            ]),
        new("86-smart-home-a2a", "08-iot-smart-systems", "parallel", Network: true, Keys: [], Importable: true,
            Titles: ["Maison connectée coordonnée", "Coordinated smart home", "Casa inteligente coordinada", "Abgestimmtes Smart Home", "协同智能家居"],
            Problems:
            [
                "Accorder chauffage, éclairage, alarme et dépense d'énergie de ma maison en un seul plan",
                "Reconcile my home's heating, lighting, alarm and energy use in a single plan",
                "Conciliar calefacción, iluminación, alarma y consumo de energía de mi casa en un único plan",
                "Heizung, Licht, Alarmanlage und Energieverbrauch meines Hauses in einem einzigen Plan abstimmen",
                "把我家的供暖、照明、安防和用电统一到一份方案中",
            ]),
        new("96-self-adaptive-crew", "09-experimental", "sequential", Network: true, Keys: [], Importable: true,
            Titles: ["Équipe qui s'auto-évalue", "Self-assessing team", "Equipo que se autoevalúa", "Team, das sich selbst bewertet", "自我评估的团队"],
            Problems:
            [
                "Évaluer le coût et la qualité d'une équipe automatisée, puis proposer une meilleure organisation",
                "Assess the cost and quality of an automated team, then propose a better setup",
                "Evaluar el coste y la calidad de un equipo automatizado y proponer una organización mejor",
                "Kosten und Qualität eines automatisierten Teams bewerten und dann eine bessere Aufstellung vorschlagen",
                "评估一个自动化团队的成本与质量，再提出更好的组织方式",
            ]),
    ];
}
