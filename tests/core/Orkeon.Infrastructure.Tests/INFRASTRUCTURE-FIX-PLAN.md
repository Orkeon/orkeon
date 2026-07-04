# Plan de Correction Complet - Tests Infrastructure

## Problèmes Identifiés

### 1. Tests Redis (Timeout - Critique)
- **Fichier**: `Memory/RedisMemoryProviderTests.cs`
- **Problème**: Tentatives de connexion à Redis réel
- **Solution**: Utiliser InMemoryRedisMemoryProvider mock partout

### 2. Tests LLM Providers (Timeout - Critique)
- **Fichiers**:
  - `LLMs/OpenAILlmProviderTests.cs`
  - `LLMs/OllamaLlmProviderTests.cs`
- **Problème**: Appels API externes (OpenAI, Ollama)
- **Solution**: Mock HttpClient avec TestMessageHandler

### 3. Tests Async/Await (Warning - Important)
- **Fichiers concernés**:
  - `Resilience/ResiliencePoliciesTests.cs` (10+ méthodes)
  - `Services/OpenAIContextWindowManagerTests.cs`
  - `Memory/InMemoryProviderTests.cs`
- **Problème**: `async` sans `await`
- **Solution**: Ajouter `await Task.CompletedTask` ou retirer `async`

### 4. Tests avec Null References (Warning - Moyen)
- **Fichiers**:
  - `Tools/AskQuestionToolTests.cs`
  - `Tools/DelegateWorkToolTests.cs`
  - `Parsing/MarkdownParserTests.cs`
- **Solution**: Ajouter null checks ou utiliser `!` operator

### 5. Tests de Stratégies (Échec - Important)
- **Fichier**: `Strategies/ParallelProcessStrategyTests.cs`
- **Problème**: Distribution round-robin incorrecte
- **Solution**: Corriger la logique de distribution

## Plan d'Exécution

### Phase 1: Isolation des Dépendances Externes (Priorité: CRITIQUE)

#### 1.1 Redis Tests
```csharp
// Remplacer toutes les utilisations de RedisMemoryProvider par:
var provider = new InMemoryRedisMemoryProvider(logger);
```

#### 1.2 HTTP/API Tests
```csharp
// Créer un TestMessageHandler générique:
public class TestMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_responseFactory(request));
    }
}
```

### Phase 2: Correction des Patterns Async (Priorité: HAUTE)

#### 2.1 Pattern de correction
```csharp
// AVANT:
public async Task TestMethod()
{
    // Code synchrone
}

// APRÈS:
public Task TestMethod()
{
    // Code synchrone
    return Task.CompletedTask;
}
```

### Phase 3: Null Safety (Priorité: MOYENNE)

#### 3.1 Pattern de correction
```csharp
// AVANT:
var result = possiblyNull.Property;

// APRÈS:
var result = possiblyNull?.Property ?? defaultValue;
// OU
var result = possiblyNull!.Property; // si certain non-null
```

### Phase 4: Logique Métier (Priorité: HAUTE)

#### 4.1 Parallel Strategy Tests
- Vérifier la distribution round-robin
- Corriger les assertions de comptage

## Fichiers à Modifier

### Priorité 1 (Bloquants - Timeout)
1. `/Memory/RedisMemoryProviderTests.cs`
2. `/LLMs/OpenAILlmProviderTests.cs`
3. `/LLMs/OllamaLlmProviderTests.cs`
4. `/Services/ConsoleHumanInputProviderTests.cs`

### Priorité 2 (Échecs de Tests)
1. `/Memory/InMemoryVectorStoreTests.cs`
2. `/Services/JsonToolCallParserTests.cs`
3. `/Tools/AskQuestionToolTests.cs`
4. `/Parsing/MarkdownParserTests.cs`
5. `/Strategies/ParallelProcessStrategyTests.cs`
6. `/Resilience/ResiliencePoliciesTests.cs`

### Priorité 3 (Warnings)
1. Tous les fichiers avec warnings CS1998 (async sans await)
2. Tous les fichiers avec warnings CS8602 (possible null reference)

## Méthodologie de Correction

1. **Isolation**: Chaque test doit être autonome, sans dépendances externes
2. **Mocking**: Utiliser des mocks pour tous les services externes
3. **Timeouts**: Aucun test ne doit dépasser 100ms
4. **Assertions**: Vérifier que chaque assertion est correcte
5. **Cleanup**: Nettoyer les ressources après chaque test

## Commandes de Validation

```bash
# Test par catégorie
dotnet test --filter "FullyQualifiedName~Memory" --no-build
dotnet test --filter "FullyQualifiedName~LLMs" --no-build
dotnet test --filter "FullyQualifiedName~Strategies" --no-build

# Test complet avec timeout court
timeout 60s dotnet test tests/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --no-build

# Validation finale
dotnet test tests/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --verbosity minimal
```

## Résultat Attendu

- ✅ 0 tests en timeout
- ✅ 100% des tests passants
- ✅ Temps d'exécution < 30 secondes
- ✅ 0 warnings de compilation