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

## Structure du projet

```
src/
├── core/
│   ├── Orkeon.Domain/          # Core domain entities
│   ├── Orkeon.Application/     # Application services
│   └── Orkeon.Infrastructure/  # External integrations
├── tools/
│   ├── Orkeon.Tools.Abstractions/  # Tool base classes & interfaces
│   ├── Orkeon.Tools.Code/          # Code-related tools
│   ├── Orkeon.Tools.Data/          # Data manipulation tools
│   ├── Orkeon.Tools.FileSystem/    # File system tools
│   └── Orkeon.Tools.Web/           # Web/HTTP tools
├── plugins/
│   └── Orkeon.Plugins/        # Plugin system
└── apps/
    └── Orkeon.ConsoleApp/     # Console application

tests/
├── core/
│   ├── Orkeon.Domain.Tests/
│   ├── Orkeon.Application.Tests/
│   └── Orkeon.Infrastructure.Tests/
├── plugins/
│   └── Orkeon.Plugins.Tests/
└── shared/
    └── Orkeon.Tests.Shared/   # Common test fixtures

examples/                 # Example projects
docs/                    # Documentation
```

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
Il n'est pas encore câblé en CI (le câblage est conditionné à la résorption préalable de la
dette de miroirs existante). Le script vérifie l'existence des fichiers, pas l'équivalence du
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
- [ ] Implémentation du fournisseur de mémoire ChromaDB
- [ ] Fournisseur de mémoire Pinecone
- [ ] Fournisseurs LLM supplémentaires (Anthropic, Cohere)
- [ ] Optimisations de performance
- [ ] Améliorations de la documentation

### Priorité moyenne
- [ ] Outils supplémentaires (Slack, Discord, Email)
- [ ] Support du streaming
- [ ] Communication entre agents en temps réel
- [ ] Interface web de gestion des crews

### Bonnes premières issues
- [ ] Ajouter davantage d'exemples
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

1. Mettre à jour les numéros de version
2. Mettre à jour CHANGELOG.md
3. Basculer les entrées `PublicAPI.Unshipped.txt` dans `PublicAPI.Shipped.txt`
4. Rédiger les notes de release
5. Taguer la release
6. Construire et publier les paquets NuGet

## Des questions ?

N'hésitez pas à ouvrir une issue avec votre question ou à nous contacter sur Discord.

## Licence

En contribuant, vous acceptez que vos contributions soient publiées sous licence MIT.
