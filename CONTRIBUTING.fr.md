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

Une exception : `docs/audit/` contient des instantanés d'audit datés (p. ex. les rapports
GO/NO-GO de publication). Ce sont des archives à date, pas de la documentation vivante —
ils restent en anglais uniquement et sont exclus du contrat de parité (décision consignée
dans le rapport GO/NO-GO 2026-08).

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

## Processus de release

1. Mettre à jour les numéros de version
2. Mettre à jour CHANGELOG.md
3. Rédiger les notes de release
4. Taguer la release
5. Construire et publier les paquets NuGet

## Des questions ?

N'hésitez pas à ouvrir une issue avec votre question ou à nous contacter sur Discord.

## Licence

En contribuant, vous acceptez que vos contributions soient publiées sous licence MIT.
