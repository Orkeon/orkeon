using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Scripting.Cli.Commands.UseCases;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// A five-sheet catalogue with its texts written in the five languages, shaped exactly like
/// <c>examples/usecases.json</c>. The real manifest's titles and problems stay empty until
/// STUDIO-37 writes them, so the search is proven here; the fifth sheet keeps them empty on
/// purpose, like every sheet of the real manifest today.
/// </summary>
internal static class UseCaseFixtures
{
    public const string MailDigest = "01-daily-mail-digest";
    public const string InvoiceMatching = "02-invoice-matching";
    public const string FraudAlerts = "03-fraud-alerts";
    public const string Onboarding = "04-new-hire-onboarding";
    public const string Untitled = "05-untitled-example";

    public const string Manifest = """
        {
          "$comment": "Test fixture.",
          "languages": ["fr", "en", "es", "de", "zh-Hans"],
          "useCases": [
            {
              "agents": 2, "category": "01-enterprise", "format": "yaml", "hasSampleData": true,
              "id": "01-daily-mail-digest", "importable": true,
              "mounts": ["./data:/data:ro", "./output:/output:rw"], "number": 1,
              "problem": {
                "de": "Jeden Morgen die eingegangenen E-Mails lesen und kurz zusammenfassen.",
                "en": "Every morning, read the emails that came in and write a short summary.",
                "es": "Cada mañana, leer los correos recibidos y escribir un resumen breve.",
                "fr": "Chaque matin, lire les e-mails reçus et en faire un résumé court.",
                "zh-Hans": "每天早上阅读收到的邮件并写一份简短的摘要。"
              },
              "process": "sequential", "requiresKeys": [], "requiresNetwork": false,
              "tags": ["email", "summary"], "tasks": 2,
              "title": {
                "de": "Tägliche E-Mail-Zusammenfassung", "en": "Daily email digest",
                "es": "Resumen diario del correo", "fr": "Résumé quotidien des e-mails",
                "zh-Hans": "每日邮件摘要"
              },
              "tools": ["email_parser", "file_write"]
            },
            {
              "agents": 3, "category": "03-finance-trading", "format": "ork.ts", "hasSampleData": false,
              "id": "02-invoice-matching", "importable": false, "mounts": [], "number": 2,
              "problem": {
                "de": "Jede Lieferantenrechnung mit ihrer Bestellung abgleichen und Abweichungen melden.",
                "en": "Match every supplier invoice against its purchase order and flag the differences.",
                "es": "Cotejar cada factura de proveedor con su orden de compra y señalar las diferencias.",
                "fr": "Rapprocher chaque facture fournisseur de son bon de commande et signaler les écarts.",
                "zh-Hans": "将每张供应商发票与采购订单核对并标出差异。"
              },
              "process": "parallel", "requiresKeys": [], "requiresNetwork": false,
              "tags": ["finance"], "tasks": 3,
              "title": {
                "de": "Abgleich von Lieferantenrechnungen", "en": "Supplier invoice matching",
                "es": "Conciliación de facturas de proveedores", "fr": "Rapprochement des factures fournisseurs",
                "zh-Hans": "供应商发票核对"
              },
              "tools": ["csv_reader", "pdf_reader"]
            },
            {
              "agents": 4, "category": "03-finance-trading", "format": "yaml", "hasSampleData": false,
              "id": "03-fraud-alerts", "importable": true, "mounts": [], "number": 3,
              "problem": {
                "de": "Verdächtige Transaktionen in Echtzeit erkennen und das Compliance-Team warnen.",
                "en": "Spot suspicious transactions in real time and alert the compliance team.",
                "es": "Detectar transacciones sospechosas en tiempo real y alertar al equipo de cumplimiento.",
                "fr": "Repérer les transactions suspectes en temps réel et alerter l'équipe conformité.",
                "zh-Hans": "实时发现可疑交易并提醒合规团队。"
              },
              "process": "parallel", "requiresKeys": ["SERPER_API_KEY"], "requiresNetwork": true,
              "tags": ["finance", "security"], "tasks": 4,
              "title": {
                "de": "Erkennung von Bankbetrug", "en": "Bank fraud detection",
                "es": "Detección de fraude bancario", "fr": "Détection de fraude bancaire",
                "zh-Hans": "银行欺诈检测"
              },
              "tools": ["database_query", "web_search"]
            },
            {
              "agents": 3, "category": "01-enterprise", "format": "yaml", "hasSampleData": false,
              "id": "04-new-hire-onboarding", "importable": true, "mounts": [], "number": 4,
              "problem": {
                "de": "Die Einarbeitung eines neuen Mitarbeiters vorbereiten: Konten, Schulungen, erster Tag.",
                "en": "Prepare a new employee's onboarding: accounts, training, first day.",
                "es": "Preparar la incorporación de un nuevo empleado: cuentas, formación, primer día.",
                "fr": "Préparer le parcours d'intégration d'un nouveau salarié : comptes, formations, premier jour.",
                "zh-Hans": "为新员工准备入职流程：账号、培训和第一天安排。"
              },
              "process": "hierarchical", "requiresKeys": [], "requiresNetwork": false,
              "tags": ["hr"], "tasks": 5,
              "title": {
                "de": "Einarbeitung neuer Mitarbeiter", "en": "New hire onboarding",
                "es": "Incorporación de nuevos empleados", "fr": "Accueil des nouveaux salariés",
                "zh-Hans": "新员工入职"
              },
              "tools": ["file_read"]
            },
            {
              "agents": 1, "category": "09-experimental", "format": "yaml", "hasSampleData": false,
              "id": "05-untitled-example", "importable": true, "mounts": [], "number": 5,
              "problem": { "de": "", "en": "", "es": "", "fr": "", "zh-Hans": "" },
              "process": "graph", "requiresKeys": [], "requiresNetwork": false,
              "tags": [], "tasks": 1,
              "title": { "de": "", "en": "", "es": "", "fr": "", "zh-Hans": "" },
              "tools": ["json_tool"]
            }
          ]
        }
        """;

    /// <summary>The fixture catalogue, with the crew file of its first sheet and one data file.</summary>
    public static UseCaseCatalog Catalog() => UseCaseCatalog.Parse(Manifest, Files());

    /// <summary>The files the fixture catalogue embeds.</summary>
    public static FakeUseCaseFileSource Files() => new FakeUseCaseFileSource()
        .With("01-enterprise/01-daily-mail-digest/config.yaml", "name: \"daily-mail-digest\"\nprocess: \"sequential\"\n")
        .With("01-enterprise/01-daily-mail-digest/data/inbox.eml", "Subject: hello\n");

    /// <summary>A search over the fixture catalogue, every language in <paramref name="mode"/>.</summary>
    public static UseCaseSearchEngine Engine(
        UseCaseSearchMode mode = UseCaseSearchMode.Bm25, Func<IEmbeddingProvider>? loadModel = null) =>
        new(Catalog(), UseCaseSearchPolicy.Everywhere(mode), loadModel ?? NoModel);

    /// <summary>A model loader that must never run: a test using it expects terms only.</summary>
    public static IEmbeddingProvider NoModel() =>
        throw new InvalidOperationException("This test expected the search never to load the model.");
}
