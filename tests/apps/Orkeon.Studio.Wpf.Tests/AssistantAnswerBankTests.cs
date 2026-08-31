using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Runtime.CompilerServices;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The assistant's keyword bank, in every language it ships in (30/08 mock, T-06).
/// <para>
/// The patterns are catalogued per culture because they cannot be shared: someone asking
/// about cost types «cuesta» in Spanish, «kostet» in German and 多少钱 in Chinese, and none
/// of those contains the English word. The first version of this catalogue was translated
/// word for word from the English list and matched the dictionary form nobody types — which
/// failed silently, since a question that matches nothing still gets the generic answer.
/// That is exactly why this suite exists: a bank that quietly stops matching looks, from the
/// outside, like a bank that is working.
/// </para>
/// </summary>
public sealed class AssistantAnswerBankTests
{
    private static readonly string[] Rules =
    [
        "Studio.Chat.RuleFolders", "Studio.Chat.RuleCost", "Studio.Chat.RuleDuration",
        "Studio.Chat.RuleError", "Studio.Chat.RuleSchedule", "Studio.Chat.RulePrivacy",
    ];

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static Dictionary<string, string> Bank(string culture)
    {
        var path = Path.Combine(WpfSourceRoot(), "Resources", culture);
        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => Rules.Contains((string?)d.Attribute("name")))
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    /// <summary>The first rule that matches — the order the bank itself resolves in.</summary>
    private static string? FirstMatch(Dictionary<string, string> bank, string question) =>
        Rules.FirstOrDefault(rule => Regex.IsMatch(
            question, bank[rule], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200)));

    public static TheoryData<string, string, string> Questions()
    {
        var data = new TheoryData<string, string, string>();

        void Add(string culture, string rule, params string[] questions)
        {
            foreach (var question in questions)
                data.Add(culture, "Studio.Chat." + rule, question);
        }

        Add("Strings.resx", "RuleFolders", "which folder will it read?", "does it have write permission?", "can it change my files?");
        Add("Strings.resx", "RuleCost", "how much does this cost?", "is this paid?", "what is the price?", "is it expensive?");
        Add("Strings.resx", "RuleDuration", "how long does it take?", "is it slow?", "how many minutes?");
        Add("Strings.resx", "RuleError", "what if it fails?", "is there an error?", "it crashed");
        Add("Strings.resx", "RuleSchedule", "can I schedule it?", "every morning at 8", "does it run automatically?");
        Add("Strings.resx", "RulePrivacy", "does my data leave the machine?", "is it secure?", "confidential information", "gdpr");

        Add("Strings.fr.resx", "RuleFolders", "quel dossier va-t-elle lire ?", "a-t-elle le droit d'écrire ?", "peut-elle modifier mes fichiers ?");
        Add("Strings.fr.resx", "RuleCost", "combien ça coûte ?", "c'est payant ?", "quel est le prix ?", "c'est cher ?");
        Add("Strings.fr.resx", "RuleDuration", "combien de temps ça prend ?", "c'est lent ?", "combien de minutes ?");
        Add("Strings.fr.resx", "RuleError", "que se passe-t-il si ça échoue ?", "y a-t-il une erreur ?", "ça a planté");
        Add("Strings.fr.resx", "RuleSchedule", "puis-je la programmer ?", "chaque matin à 8 h", "ça s'exécute automatiquement ?");
        Add("Strings.fr.resx", "RulePrivacy", "mes données sortent-elles ?", "est-ce sécurisé ?", "informations confidentielles", "rgpd");

        Add("Strings.es.resx", "RuleFolders", "¿qué carpeta va a leer?", "¿tiene permiso de escritura?", "¿puede modificar mis ficheros?");
        Add("Strings.es.resx", "RuleCost", "¿cuánto cuesta esto?", "¿esto es de pago?", "¿cuánto me va a costar?", "¿es caro?");
        Add("Strings.es.resx", "RuleDuration", "¿cuánto tiempo tarda?", "¿es lento?", "¿cuántos minutos?");
        Add("Strings.es.resx", "RuleError", "¿qué pasa si falla?", "¿y si hay un error?", "se ha caído");
        Add("Strings.es.resx", "RuleSchedule", "¿puedo programarlo?", "cada mañana a las 8", "¿se ejecuta automáticamente?");
        Add("Strings.es.resx", "RulePrivacy", "¿mis datos salen de aquí?", "¿es seguro?", "información confidencial", "rgpd");

        Add("Strings.de.resx", "RuleFolders", "welchen Ordner liest sie?", "darf sie schreiben?", "kann sie meine Dateien ändern?");
        Add("Strings.de.resx", "RuleCost", "was kostet das?", "ist das kostenpflichtig?", "wie teuer wird das?", "der Preis?");
        Add("Strings.de.resx", "RuleDuration", "wie lange dauert das?", "ist das langsam?", "wie viele Minuten?");
        Add("Strings.de.resx", "RuleError", "was wenn es fehlschlägt?", "gibt es einen Fehler?", "es ist abgestürzt");
        Add("Strings.de.resx", "RuleSchedule", "kann ich das planen?", "jeden Morgen um 8", "läuft das automatisch?");
        Add("Strings.de.resx", "RulePrivacy", "verlassen meine Daten den Rechner?", "ist das sicher?", "vertrauliche Informationen", "DSGVO");

        Add("Strings.zh-Hans.resx", "RuleFolders", "它会读哪个文件夹？", "有写入权限吗？", "会修改我的文件吗？");
        Add("Strings.zh-Hans.resx", "RuleCost", "这要多少钱？", "会收费吗？", "贵不贵？", "价格如何");
        Add("Strings.zh-Hans.resx", "RuleDuration", "要多久？", "会不会很慢？", "大概几分钟");
        Add("Strings.zh-Hans.resx", "RuleError", "出错了怎么办？", "如果失败呢？", "程序崩溃了");
        Add("Strings.zh-Hans.resx", "RuleSchedule", "可以定时运行吗？", "每天早上自动跑", "能排程吗");
        Add("Strings.zh-Hans.resx", "RulePrivacy", "我的数据会外传吗？", "安全吗？", "涉及机密信息", "隐私");

        return data;
    }

    [Theory]
    [MemberData(nameof(Questions))]
    public void A_question_reaches_the_rule_it_is_about(string culture, string expectedRule, string question)
    {
        var matched = FirstMatch(Bank(culture), question);

        Assert.True(
            matched == expectedRule,
            $"[{culture}] «{question}» should reach {expectedRule}, reached {matched ?? "no rule at all"}.");
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.fr.resx")]
    [InlineData("Strings.es.resx")]
    [InlineData("Strings.de.resx")]
    [InlineData("Strings.zh-Hans.resx")]
    public void Every_pattern_is_a_valid_regex_that_cannot_run_away(string culture)
    {
        var bank = Bank(culture);
        Assert.Equal(Rules.Length, bank.Count);

        foreach (var rule in Rules)
        {
            var pattern = bank[rule];
            Assert.False(string.IsNullOrWhiteSpace(pattern), $"{culture}: {rule} is empty");

            // Compiles, and finishes on a long hostile input — these come from a catalogue,
            // which is data, and data is where a runaway pattern comes from.
            var exception = Record.Exception(() => Regex.IsMatch(
                new string('a', 4_000) + "?", pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200)));

            Assert.True(exception is null, $"{culture}: {rule} — {exception?.Message}");
        }
    }
}
