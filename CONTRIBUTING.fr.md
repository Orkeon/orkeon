> 🇬🇧 [English version](CONTRIBUTING.md)

# Contribuer à Orkeon

Avant tout, merci d'envisager de contribuer à Orkeon ! Ce sont des personnes comme vous qui font d'Orkeon un outil aussi formidable.

## Code de conduite

Ce projet et toutes les personnes qui y participent sont régis par notre [Code de conduite](CODE_OF_CONDUCT.fr.md). En participant, vous vous engagez à le respecter.

## Comment puis-je contribuer ?

### Signaler des bugs

Avant de créer un rapport de bug, vérifiez les issues existantes : vous pourriez découvrir qu'il n'est pas nécessaire d'en créer un. Lorsque vous créez un rapport de bug, merci d'inclure autant de détails que possible :

* **Utilisez un titre clair et descriptif**
* **Décrivez les étapes exactes qui reproduisent le problème**
* **Fournissez des exemples concrets pour illustrer les étapes**
* **Décrivez le comportement observé après avoir suivi les étapes**
* **Expliquez le comportement que vous attendiez à la place, et pourquoi**
* **Incluez des extraits de code et des stack traces le cas échéant**

### Proposer des améliorations

Les suggestions d'amélioration sont suivies via les issues GitHub. Lorsque vous créez une suggestion d'amélioration, merci d'inclure :

* **Utilisez un titre clair et descriptif**
* **Fournissez une description pas à pas de l'amélioration proposée**
* **Fournissez des exemples concrets pour illustrer les étapes**
* **Décrivez le comportement actuel et expliquez le comportement que vous attendiez à la place**
* **Expliquez en quoi cette amélioration serait utile**

### Pull requests

1. Forkez le dépôt et créez votre branche à partir de `main`
2. Si vous avez ajouté du code qui devrait être testé, ajoutez des tests
3. Si vous avez modifié des APIs, mettez à jour la documentation
4. Assurez-vous que la suite de tests passe
5. Vérifiez que votre code suit le style de code existant
6. Soumettez cette pull request !
7. Sur votre première pull request, acceptez l'[accord de licence de contribution](CLA.fr.md) :
   une vérification poste la phrase à répondre, et reste rouge tant que ce n'est pas
   fait. Une fois par compte GitHub ; vous gardez votre droit d'auteur, le projet obtient
   une licence qu'il peut transmettre à une entité successeur. (Le texte est un gabarit
   en attente de relecture juridique — il le dit dès sa première ligne — et la
   vérification documente le processus voulu en attendant.)

## Mise en place de l'environnement de développement

```bash
# Clone your fork
git clone https://github.com/your-username/orkeon.git
cd orkeon

# Add upstream remote
git remote add upstream https://github.com/Orkeon/orkeon.git

# Install dependencies (once, and again only when a package reference changes)
dotnet restore Orkeon.sln

# Build (CI builds -c Release -warnaserror: the solution is kept at zero warnings)
dotnet build Orkeon.sln --no-restore

# Run tests (the set CI runs)
dotnet test Orkeon.sln --no-build --filter "Category!=Integration&Category!=Slow"
```

> **Pourquoi ce filtre, et pas un `dotnet test Orkeon.sln` nu** : `Category=Integration`
> couvre les tests Testcontainers, qui exigent Docker et téléchargent des gigaoctets
> d'images de bases de données, et `Category=Slow` les tests longs (les vrais modèles
> ONNX, les exécutions de bout en bout). `ci.yml` exécute exactement la commande filtrée
> ci-dessus, puis les tests ONNX du projet d'embeddings locaux dans une étape dédiée, pour
> qu'une défaillance du runtime natif se signale sous son nom — tout code de sortie non
> nul fait échouer cette étape. Les deux catégories tournent sur toute la solution dans
> le `integration.yml` nocturne ; pour les lancer en local (Docker requis) :
> `dotnet test Orkeon.sln --no-build --filter "Category=Integration|Category=Slow" -- --ignore-exit-code 8`
> — `--ignore-exit-code 8` parce que la plupart des modules ne contiennent aucun test de
> ces deux catégories.

> **Note** — ce dépôt déclare des **sous-modules privés des mainteneurs** :
> clonez **sans** `--recursive` (comme ci-dessus). Le build, les tests et tout le flux de
> contribution s'en passent ; un échec de `git submodule update` sur ces chemins est
> attendu et sans conséquence.

> **Le premier build touche le réseau une fois** : la couche scripting provisionne
> une petite toolchain esbuild (`npm ci` sous `tools/scripting-esbuild/`, strictement
> depuis le lockfile commité). Pour l'éviter (CI, machines sans npm) :
> `dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true` — le build réussit quand
> même et esbuild est résolu depuis le `PATH` au runtime.

> **Les tests tournent sur Microsoft.Testing.Platform**, activé par `global.json` — le SDK
> .NET 10 refuse purement et simplement d'exécuter ces projets via VSTest. Les commandes
> courantes ne changent pas, mais deux choses mordent dès qu'on restreint une exécution :
> un `--filter` qui ne correspond à **aucun** test dans un module y est une erreur (code 8),
> pas un succès vide — une exécution sur toute la solution ne doit donc jamais exclure un
> projet par son nom ; et les options propres à VSTest (`--collect`, `--logger`, `--blame`)
> sont rejetées comme arguments inconnus.

## Structure du projet

La solution compte **48 projets src répartis en 14 zones** et **36 projets de tests**.
Trente-quatre projets src ont un projet de tests miroir ; `tests/e2e` et `tests/shared`
forment les deux autres. Quatorze projets src n'ont volontairement pas de miroir : les cinq
satellites `Orkeon.Constants.*` et `Orkeon.Rag.Onnx.Model` ne portent que des constantes et
des ressources embarquées, `Orkeon.Analysis.Abstractions` est exercé via
`Orkeon.Analysis.Tests`, `Orkeon.Generators` est couvert par la compilation des projets qui
le consomment, et les six projets `src/packaging/` sont de pur empaquetage :

```
src/
├── core/        # Orkeon.Domain, Orkeon.Application, Orkeon.Infrastructure (cœur Clean Architecture)
├── tools/       # 10 packs d'outils : Abstractions, Analysis (RaggableTree), Code, Data, Email,
│                #   Embeddings.Local, EventHub, FileSystem, Rag, Web
├── rag/         # Sous-système RAG : Rag.Abstractions, Rag, Rag.Onnx, Rag.Onnx.Model
├── analysis/    # Moteur RaggableTree : Analysis.Abstractions, Analysis
├── scripting/   # Orkeon.Scripting (DSL .ork.ts) + Orkeon.Scripting.Cli (le tool `orkeon`)
├── cli/         # Cli.Abstractions, Cli, Cli.Commands.Scripting, Cli.TerminalGui
├── constants/   # Satellites sans dépendance de constantes PARTAGÉES (ADR-009) :
│                #   Constants.Llm, Constants.FileSystem, Constants.Configuration, Constants.Protocol, Constants.Cli
├── hosting/     # Orkeon.Hosting (RunnerHost) + Orkeon.Host (le daemon `orkeon-host`)
│                #   + Orkeon.Hosting.Aspire (intégration AppHost .NET Aspire, ADR-011)
├── plugins/     # Orkeon.Plugins (chargement de plugins au runtime)
├── interop/     # Orkeon.Interop.AgentFramework (pont Microsoft Agent Framework, ADR-010)
├── generators/  # Orkeon.Generators (générateurs de source)
├── analyzers/   # Orkeon.Compliance.Vfs (analyseur Roslyn VFS-only)
├── packaging/   # Projets d'empaquetage NuGet (PUB-25) : Orkeon (le framework en un nupkg), Orkeon.Tools,
│                #   + les wrappers Rag.Onnx / Tools.Embeddings.Local / Interop.AgentFramework /
│                #   Hosting.Aspire dépendant de l'ombrelle Orkeon
└── apps/        # Orkeon.ConsoleApp (orkeon-repl) + Orkeon.Studio.{Config,Core,Run,Wpf}

examples/        # 105 exemples embarqués (9 catégories + vitrines) — solution dédiée
docs/            # Documentation, EN + miroir docs/fr (gate de parité CI)
```

L'arborescence annotée complète vit dans
[docs/fr/getting-started/overview.md](docs/fr/getting-started/overview.md#structure-du-projet).

## Standards de codage

### Guide de style C#

* Utilisez le PascalCase pour les membres publics
* Utilisez le camelCase pour les champs privés
* Préfixez les interfaces par « I »
* Utilisez des noms de variables significatifs
* Gardez des méthodes courtes et focalisées
* Utilisez async/await pour les opérations asynchrones

### Exemple :
```csharp
public interface IAgentService
{
    Task<Agent> CreateAgentAsync(string role, string goal);
}

public class AgentService : IAgentService
{
    private readonly ILogger<AgentService> _logger;
    
    public async Task<Agent> CreateAgentAsync(string role, string goal)
    {
        // Implementation
    }
}
```

### Les règles que le build fait respecter

* **Pas de `System.IO` direct dans le code du framework.** Fichiers et répertoires passent
  par `IFileSystemService` et des chemins virtuels (`/workspace/...`, `/output/...`) ;
  l'analyseur `Orkeon.Compliance.Vfs` fait échouer le build sinon. Les exceptions admises
  (l'implémentation du VFS elle-même, le code d'amorçage marqué `// EXCEPTION-BOOTSTRAP`,
  la détection système marquée `// OUT-OF-SCOPE`, les tests) sont listées dans
  [Conformité VFS](docs/fr/architecture/vfs-compliance.md).
* **`Orkeon.Domain.Task` masque `System.Threading.Tasks.Task`.** Dans un fichier qui
  importe les deux espaces de noms, écrivez `System.Threading.Tasks.Task` en entier.
* **Une nouvelle API publique se déclare**, voir [Versionnement et stabilité API](#versionnement-et-stabilité-api).

### Les commentaires sont en anglais, et jamais accentués

Un invariant, tenu par `scripts/check-comment-accents.py` en CI : tout commentaire est en
anglais et ne porte aucune lettre accentuée. L'interface de Studio étant en français, le
piège est le commentaire qui cite un libellé : **traduisez le libellé, n'enlevez pas ses
accents** — un commentaire citant `"Modele d'IA"` nomme quelque chose que le produit
n'affiche jamais. Nommez plutôt le rôle (`the model-settings tab`). Une phrase française
désaccentuée reste du français, en pire.

Seules les lignes de commentaire sont concernées. Les chaînes visibles par l'utilisateur
gardent leurs accents, tout comme la typographie employée partout dans le dépôt — tirets
cadratins, points de suspension, flèches, guillemets : ce ne sont pas des lettres accentuées.

### Documentation

* Ajoutez une documentation XML à toutes les APIs publiques
* Incluez des exemples dans la documentation lorsque c'est utile
* Mettez à jour README.md si vous ajoutez de nouvelles fonctionnalités

#### Documentation bilingue (obligatoire)

La documentation est maintenue en anglais et en français en parallèle. Toute PR qui ajoute,
renomme ou supprime un fichier sous `docs/**.md` (hors `docs/fr/`) **doit** appliquer le même
changement à son miroir français sous `docs/fr/`, et toute modification d'un fichier
communautaire racine (`README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
`SUPPORT.md`, ainsi que la page d'accueil du site `index.md`)
**doit** mettre à jour son miroir `*.fr.md`. Le script `scripts/check-docs-parity.sh` le
vérifie : il échoue dès qu'un miroir manque — exécutez-le localement avant d'ouvrir la PR.
**C'est un gate CI** : `ci.yml` exécute le script à chaque push et pull request, un miroir
manquant fait donc échouer le build. Le script vérifie l'existence des fichiers, pas l'équivalence du
contenu — la synchronisation reste à votre charge ; si vous ne pouvez pas traduire
immédiatement, ajoutez un miroir minimal et signalez-le pour traduction.

Les instantanés d'audit datés (p. ex. les rapports GO/NO-GO de publication) sont des
archives de gouvernance, pas de la documentation vivante : ils vivent dans le dépôt de
gouvernance privé des mainteneurs, hors de `docs/`, si bien que le contrat de parité
s'applique à tout l'arbre documentaire sans exception.

### Tests

* Écrivez des tests unitaires pour les nouvelles fonctionnalités
* Maintenez ou améliorez la couverture de code
* Utilisez des noms de tests descriptifs
* Suivez le pattern AAA (Arrange, Act, Assert)
* Utilisez xUnit et ses assertions natives uniquement — pas de framework de mock (Moq,
  NSubstitute, FakeItEasy) ni de bibliothèque d'assertions fluentes (FluentAssertions,
  Shouldly)

```csharp
[Fact]
public void ShouldTransitionFromIdleToBusy_WhenStartingTask()
{
    // Arrange
    var agent = new AgentBuilder().Role("Researcher").Goal("Find information").Build();
    var taskId = TaskId.Create();
    agent.AssignTask(taskId);

    // Act
    agent.StartTask(taskId);

    // Assert
    Assert.Equal(AgentStatus.Busy, agent.Status);
    Assert.Equal(taskId, agent.CurrentTask);
}
```

Les doublures de test sont **écrites à la main**, par choix : une classe simple nommée
d'après l'interface qu'elle remplace, préfixée `Mock`, `Fake` ou `Stub` (`MockTaskRepository`
implémente `ITaskRepository`), placée dans un dossier `Doubles/` (ou `Fakes/`) du projet de
tests qui la consomme, et exposant de simples champs ou propriétés pour configurer les
réponses et inspecter les appels. La référence est
`tests/core/Orkeon.Infrastructure.Tests/Doubles/MockTaskRepository.cs`.

### Scripts et commits

* Un script `.sh`, ou tout script à ligne shebang destiné à être exécuté directement, est
  commité **exécutable** (mode `100755`) : `git update-index --chmod=+x path/to/script.sh`.
  La gate `file-modes.yml` lit le mode dans l'index git — le seul qu'un clone macOS/Linux
  restaure —, si bien qu'un arbre de travail sur un montage NTFS où tout fichier paraît
  exécutable ne prouve rien.
* Un changement de paquet — une version de `Directory.Packages.props`, un paquet ajouté à
  un projet que livrent les outils, les installeurs ou les images de conteneur, ou retiré —
  change ce que ces binaires redistribuent : après la restauration, lancez
  `python3 scripts/third-party-notices.py` et commitez `THIRD-PARTY-NOTICES.md` avec. Le
  `--check` de la CI échoue tant que l'inventaire de ce fichier ne liste pas la nouvelle
  fermeture, tant qu'une version qu'une de ses sections manuscrites déclare n'est pas
  celle livrée, et tant qu'un paquet livré se résout sous la version qu'épingle
  `Directory.Packages.props`. Un épinglage ne vaut que pour une référence directe : un
  paquet qu'une application ne reçoit que transitivement, sous son épinglage, demande une
  référence directe dans un projet sur lequel elle repose (`Orkeon.Cli.TerminalGui` porte
  celles que `Terminal.Gui` apporte aux TUI de Studio).
* Un `Dockerfile` qui publie une application copie `LICENSE.md` et `THIRD-PARTY-NOTICES.md`
  dans l'image qu'il produit, sous `/usr/share/doc/orkeon/`, et `.dockerignore` garde les
  deux dans le contexte de build : sinon, le `--check` échoue. Les installeurs copient les
  notices du runtime .NET qu'ils embarquent au moment de l'empaquetage
  (`scripts/third-party-notices.py --runtime-notices`, qu'appellent
  `scripts/package-installers.sh` et son miroir `.ps1`, qui demandent donc Python 3).
* Les messages de commit sont en anglais et suivent la forme `type(scope): summary` de
  l'historique (`feat`, `fix`, `docs`, `test`, `chore`…).

## Domaines de contribution

### Le périmètre est gelé

Orkeon embarque déjà 16 fournisseurs LLM (quatorze vendeurs et deux
agrégateurs), 91 outils intégrés, 6 stores de mémoire, deux pipelines RAG,
RaggableTree, un DSL de scripting, des plugins, MCP, A2A et un Studio —
maintenus par une seule personne. Tant que de vrais utilisateurs n'en
demandent pas davantage, **la surface fonctionnelle ne grandit pas** :

- pas de 17ᵉ fournisseur LLM — la base compatible OpenAI couvre tout endpoint
  qui parle ce dialecte ; pointez `Orkeon:Llm:BaseUrl` dessus. Les deux
  agrégateurs (OpenRouter, Mammouth AI) sont la première exception motivée, actée
  par le propriétaire le 2026-09-18 : un agrégateur déclare ses propres
  capacités, écrit des champs que le socle ne lit pas (`reasoning`,
  `usage.cost`) et doit être reconnu par l'outillage — rien de ce qu'une
  `BaseUrl` apporte. La règle vaut pour tout le reste ;
- pas de nouvel outil intégré — écrivez le vôtre dans un script `.ork.ts` ou
  un plugin, tous deux de premier rang et sans modification ici. La famille
  e-mail (`Orkeon.Tools.Email`, treize outils) est la deuxième exception
  motivée, actée par le propriétaire le 2026-09-26 : un script `.ork.ts` ne
  peut pas ouvrir de socket, et un plugin ne mettrait pas l'e-mail dans Orkeon
  lui-même, ce qui était demandé
  ([ADR-012](docs/fr/adr/ADR-012-email-tool-family.md)). La règle vaut pour
  tout le reste ;
- pas de nouveau store de mémoire, adaptateur de langage ou mode d'orchestration.

Une pull request qui ajoute l'un d'eux sera fermée avec un lien vers cette
section, quelle que soit sa qualité. Ce qui *est* bienvenu, c'est tout ce qui
abaisse le coût d'essayer Orkeon ou de lui faire confiance : bugs, tests,
documentation, interopérabilité avec ce que les gens utilisent déjà (Microsoft
Agent Framework, OpenTelemetry, .NET Aspire), et performance.

### Priorité haute
- [ ] Bugs trouvés en exécutant les exemples sur un modèle local
- [ ] Tests d'interop MCP contre les serveurs de référence (MCP Inspector)
- [ ] Optimisations de performance
- [ ] Améliorations de la documentation (voir le contrat de parité EN/FR ci-dessus)

### Bonnes premières issues
- [ ] Ajouter davantage d'exemples (suivre [le gabarit de README d'exemple](docs/fr/templates/example-readme.md))
- [ ] Améliorer les messages d'erreur
- [ ] Ajouter de la documentation XML
- [ ] Corriger les fautes de frappe dans la documentation

## Versionnement et stabilité API

Orkeon suit le [Semantic Versioning 2.0](https://semver.org/). L'API publique n'est pas
une affaire d'opinion — elle est **consignée dans le dépôt** et vérifiée au build :

- Chaque projet packable porte `PublicAPI.Shipped.txt` (la surface gelée, publiée) et
  `PublicAPI.Unshipped.txt` (les ajouts depuis la dernière release), contrôlés par
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Un changement d'API publique non déclaré
  fait échouer le build (`RS0016`/`RS0017` promus en erreurs).
- **Ajouter** une API publique : la déclarer dans `PublicAPI.Unshipped.txt` (le code
  fix de l'analyseur le fait pour vous — `dotnet format analyzers --diagnostics RS0016`
  sur le projet). À la release, les entrées `Unshipped` passent dans `Shipped`.
- **Un breaking change est toute édition ou suppression d'une ligne de
  `PublicAPI.Shipped.txt`.** Il exige une version majeure (une mineure n'est acceptable
  qu'avant la 1.0), une entrée `*REMOVED*` dans le fichier d'API, et une entrée
  CHANGELOG qui le dit sans détour.
- **Fenêtre de dépréciation** : rien de public n'est retiré sans avoir livré
  `[Obsolete]` pendant au moins une version mineure, avec le remplaçant nommé dans le
  message.
- **Les surfaces `[Experimental]` sont hors de cet engagement.** A2A, l'orchestration
  Autonomous, le RAG correctif et l'intégration MCP portent des diagnostics
  `[Experimental("ORKEXP00x")]` : les référencer est une erreur de compilation à
  supprimer explicitement — c'est votre opt-in à une surface qui peut changer dans
  n'importe quelle version. Voir
  [docs/fr/reference/experimental-apis.md](docs/fr/reference/experimental-apis.md).
- **Engagement de stabilité pour la fenêtre 1.x** : une fois la 1.0 publiée, aucun
  breaking change sur une API livrée non expérimentale avant la 2.0. D'ici là — la
  ligne `1.0.0-rc.*` sur laquelle `main` se trouve aujourd'hui — des breaking changes
  peuvent encore arriver d'une release candidate à l'autre, mais sont toujours annoncés
  dans le CHANGELOG et les notes de migration.

## Processus de release

1. Bump de `VersionPrefix`/`VersionSuffix` dans `src/Directory.Build.props` — la
   source unique de vérité. Le workflow de publication **refuse un tag `v*` qui ne
   lui correspond pas**.
2. Couper la section `[Unreleased]` de `CHANGELOG.md` en section versionnée datée.
3. Basculer les entrées `PublicAPI.Unshipped.txt` dans `PublicAPI.Shipped.txt`.
4. Basculer de même les règles d'analyseur : toute entrée en attente dans
   `src/analyzers/Orkeon.Compliance.Vfs/AnalyzerReleases.Unshipped.md` (les règles de
   conformité VFS `ORKVFS00x`) passe dans `AnalyzerReleases.Shipped.md` sous un titre
   `## Release <version>`, ne laissant que son en-tête au fichier `Unshipped`.
   L'analyseur de suivi des releases lit ce titre comme un simple numéro
   `Majeur.Mineur.Correctif` et refuse un suffixe de pré-release (RS2007) : le titre de la
   ligne 1.0.0 est donc `## Release 1.0.0` — il est déjà là, et les tags `rc.*` n'y ajoutent
   rien.
5. Vérifier que la CI est verte sur le commit que vous taguez. Au-delà du build
   `-warnaserror` et des suites de tests, les gates qui doivent passer sont
   `scripts/check-docs-parity.sh`, `scripts/check-doc-claims.py` (qui compare aussi les
   copies manuscrites du lineup NuGet et exécute la moitié « sources » de
   `scripts/check-package-closure.py` ; son énumération des fichiers est testée par
   `scripts/test-check-doc-claims.py`), `scripts/check-comment-accents.py`, la gate des
   notices tierces (`scripts/third-party-notices.py --check`, dont les règles sont testées
   par `scripts/test-third-party-notices.py`), la vérification des typings de scripting
   (`scripts/check-scripting-typings.sh`), le test de la purge du canal dev
   (`scripts/test-prune-dev-packages.sh`), les gates des
   exemples (`scripts/generate-examples-index.sh --check`,
   `scripts/lint-example-configs.py`, `scripts/lint-example-readmes.py`,
   `scripts/test-examples-catalog.py`, la solution d'exemples compilée en `-warnaserror`,
   `scripts/validate-all-examples.sh`), la gate des bits exécutables (`file-modes.yml`),
   le scan de secrets (`secret-scan.yml`), CodeQL, la revue de dépendances et le build
   docfx strict — plus `rag-eval.yml` et `quickstart.yml` quand leurs chemins ont changé.
6. Lancer `python3 scripts/check-release-readiness.py`. `publish.yml` l'exécute sur le
   tag et refuse une release dont la section `[Unreleased]` de `CHANGELOG.md` n'est pas
   vide ou dont les fichiers `PublicAPI.Unshipped.txt` ne sont pas réduits à leur
   en-tête ; après le pack, il exécute aussi `scripts/check-package-closure.py` sur les
   nupkgs eux-mêmes.
7. Taguer `v<version>` et pousser le tag. Cela déclenche : `publish.yml` (packe tout ;
   pousse **les neuf paquets de la gamme v1 sur NuGet.org** — `Orkeon`, `Orkeon.Tools`,
   `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model`, `Orkeon.Tools.Embeddings.Local`,
   `Orkeon.Scripting.Cli`, `Orkeon.Compliance.Vfs`, `Orkeon.Interop.AgentFramework`,
   `Orkeon.Hosting.Aspire`, l'ombrelle en premier puisque les autres en dépendent, via
   Trusted Publishing — et chaque projet packable sur GitHub Packages, les paquets et un
   SBOM CycloneDX couverts par une attestation de provenance de build ; voir
   [la matrice de publication](docs/fr/reference/publication-matrix.md)),
   `release.yml` (les paquets CLI par plateforme et les archives multi-apps pour chaque
   RID, le MSI Windows per-user et le MSI de service `orkeon-host`, les tarballs macOS, le
   paquet Debian, le SBOM et les fichiers de sommes `SHA256SUMS` / `SHA256SUMS.msi` —
   chacun smoke-testé sur un vrai runner, puis attesté, avant publication de la Release —
   plus l'image conteneur `orkeon-runners` poussée sur GHCR) et `docs.yml`
   (déploie le site de documentation sur GitHub Pages, à l'adresse <https://orkeon.github.io/orkeon/>).
   Une fois la Release publiée, `release-verify.yml` rejoue les smokes d'installation sur
   les assets tels que téléchargés depuis la page de la Release.

Entre deux tags, chaque push sur `main` dont la CI est verte est aussi packé en
`<version>.dev.<numéro de run>` et poussé sur GitHub Packages uniquement — jamais sur
NuGet.org —, sans attestation ni SBOM ; les builds dev plus anciens sont purgés.

## Des questions ?

N'hésitez pas à ouvrir une issue, ou à lancer un fil dans les [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions).

## Licence

En contribuant, vous acceptez que vos contributions soient publiées sous licence MIT.
