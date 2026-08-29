> 🇬🇧 [English version](CONTRIBUTING.md)

# Contribuer à Orkeon

Avant tout, merci d'envisager de contribuer à Orkeon ! Ce sont des personnes comme vous qui font d'Orkeon un outil aussi formidable.

## Code de conduite

Ce projet et toutes les personnes qui y participent sont régis par notre Code de conduite. En participant, vous vous engagez à le respecter.

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

## Mise en place de l'environnement de développement

```bash
# Clone your fork
git clone https://github.com/your-username/orkeon.git
cd orkeon

# Add upstream remote
git remote add upstream https://github.com/Orkeon/orkeon.git

# Install dependencies
dotnet restore Orkeon.sln

# Build
dotnet build Orkeon.sln

# Run tests
dotnet test Orkeon.sln
```

> **Le premier build touche le réseau une fois** : la couche scripting provisionne
> une petite toolchain esbuild (`npm ci` sous `tools/scripting-esbuild/`, strictement
> depuis le lockfile commité). Pour l'éviter (CI, machines sans npm) :
> `dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true` — le build réussit quand
> même et esbuild est résolu depuis le `PATH` au runtime.

## Structure du projet

La solution compte **37 projets src répartis en 12 zones**, chacun reflété par un
projet de tests (plus `tests/e2e`, `tests/examples`, `tests/shared`) :

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

```
src/
├── core/        # Orkeon.Domain, Orkeon.Application, Orkeon.Infrastructure (cœur Clean Architecture)
├── tools/       # 9 packs d'outils : Abstractions, Analysis (RaggableTree), Code, Data,
│                #   Embeddings.Local, EventHub, FileSystem, Rag, Web
├── rag/         # Sous-système RAG : Rag.Abstractions, Rag, Rag.Onnx, Rag.Onnx.Model
├── analysis/    # Moteur RaggableTree : Analysis.Abstractions, Analysis
├── scripting/   # Orkeon.Scripting (DSL .ork.ts) + Orkeon.Scripting.Cli (le tool `orkeon`)
├── cli/         # Cli.Abstractions, Cli, Cli.Commands.Scripting, Cli.TerminalGui
├── hosting/     # Orkeon.Hosting (RunnerHost)
├── plugins/     # Orkeon.Plugins (chargement de plugins au runtime)
├── generators/  # Orkeon.Generators (générateurs de source)
├── analyzers/   # Orkeon.Compliance.Vfs (analyseur Roslyn VFS-only)
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

### Documentation

* Ajoutez une documentation XML à toutes les APIs publiques
* Incluez des exemples dans la documentation lorsque c'est utile
* Mettez à jour README.md si vous ajoutez de nouvelles fonctionnalités

#### Documentation bilingue (obligatoire)

La documentation est maintenue en anglais et en français en parallèle. Toute PR qui ajoute,
renomme ou supprime un fichier sous `docs/**.md` (hors `docs/fr/`) **doit** appliquer le même
changement à son miroir français sous `docs/fr/`, et toute modification d'un fichier
communautaire racine (`README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`)
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

```csharp
[Fact]
public async Task Agent_Should_Execute_Task_Successfully()
{
    // Arrange
    var agent = Agent.Create("Researcher", "Find information");
    var task = CrewTask.Create("Research AI", "Report");
    
    // Act
    var result = await agent.ExecuteTaskAsync(task);
    
    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.Output);
}
```

## Domaines de contribution

### Priorité haute
- [ ] Fournisseurs LLM supplémentaires (Cohere, Vertex AI / Bedrock via leurs SDKs)
- [ ] Adaptateurs de langage supplémentaires pour RaggableTree (`ILanguageAdapter` : Java, Ruby, PHP…)
- [ ] Tests d'interop MCP contre les serveurs de référence (MCP Inspector)
- [ ] Optimisations de performance
- [ ] Améliorations de la documentation (voir le contrat de parité EN/FR ci-dessous)

### Priorité moyenne
- [ ] Outils supplémentaires (calendrier, ticketing, messagerie au-delà de Slack/Email)
- [ ] Fournisseurs de mémoire supplémentaires (Qdrant, Weaviate, Milvus)
- [ ] Interface web de gestion des crews

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
  breaking change sur une API livrée non expérimentale avant la 2.0. D'ici là (0.x),
  des breaking changes peuvent arriver en version mineure mais sont toujours annoncés
  dans le CHANGELOG et les notes de migration.

## Processus de release

1. Bump de `VersionPrefix`/`VersionSuffix` dans `src/Directory.Build.props` — la
   source unique de vérité. Le workflow de publication **refuse un tag `v*` qui ne
   lui correspond pas**.
2. Couper la section `[Unreleased]` de `CHANGELOG.md` en section versionnée datée.
3. Basculer les entrées `PublicAPI.Unshipped.txt` dans `PublicAPI.Shipped.txt`.
4. Vérifier que la CI est verte : build `-warnaserror`, tests,
   `scripts/check-docs-parity.sh`, linters d'exemples, build docfx strict.
5. Taguer `v<version>` et pousser le tag. Cela déclenche : `publish.yml` (packe tout ;
   pousse **Domain/Application/Infrastructure sur NuGet.org**, chaque paquet sur
   GitHub Packages — voir [la matrice de publication](docs/fr/reference/publication-matrix.md)),
   `release.yml` (zip+MSI Windows, tarballs macOS, paquet Debian) et `docs.yml`
   (déploie le site de documentation sur GitHub Pages).

## Des questions ?

N'hésitez pas à ouvrir une issue, ou à lancer un fil dans les [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions).

## Licence

En contribuant, vous acceptez que vos contributions soient publiées sous licence MIT.
