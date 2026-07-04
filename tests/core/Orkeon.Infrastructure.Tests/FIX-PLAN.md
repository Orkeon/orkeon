# 🔧 PLAN DE CORRECTION DES TESTS INFRASTRUCTURE

## 📊 Analyse des Problèmes Identifiés

### 1. **Tests Redis (Timeout ~38s)** 🔴 CRITIQUE
- `BasicRedisMemoryProviderTests` - Tous les tests timeout
- `RedisMemoryProviderTests` - Connexion Redis bloque
- **Solution**: Implémenter mocks complets pour Redis

### 2. **Tests Async/Await** ⚠️ IMPORTANT
- `HierarchicalProcessStrategyTests` - Problèmes async
- `ParallelProcessStrategyTests` - Race conditions
- **Solution**: Corriger patterns async et ajouter ConfigureAwait(false)

### 3. **Tests HTTP/JSON** ⚠️ MOYEN
- `HttpClientAdapterTests` - Problèmes de sérialisation JSON
- `OpenAIProviderTests` - Mocks HTTP incomplets
- **Solution**: Fixer les TestHttpMessageHandler

### 4. **Tests Token Counter** ✅ MINEUR
- `SimpleTokenCounterAdditionalTests` - Logique de comptage incorrecte
- **Solution**: Ajuster la logique de comptage

### 5. **Tests Memory Providers** ⚠️ IMPORTANT
- `InMemoryProviderTests` - Validation des paramètres
- `SqliteMemoryProviderTests` - Problèmes de connexion
- **Solution**: Améliorer la validation et les mocks

## 🎯 Ordre de Correction (Priorité)

### Phase 1: Isolation des Dépendances Externes (CRITIQUE)
1. **Mock Redis complet**
   - Créer `MockRedisConnectionMultiplexer`
   - Créer `MockRedisDatabase`
   - Remplacer toutes les connexions Redis par des mocks

2. **Mock SQLite**
   - Utiliser SQLite en mémoire `:memory:`
   - Éviter les fichiers physiques

3. **Mock HTTP Clients**
   - Corriger `TestHttpMessageHandler`
   - Implémenter réponses mock pour OpenAI/Ollama

### Phase 2: Correction des Patterns Async (IMPORTANT)
1. **Ajouter ConfigureAwait(false)**
   - Sur tous les await dans le code de production
   - Sur tous les await dans les tests

2. **Éliminer .Result et .Wait()**
   - Remplacer par async/await partout

3. **Gérer les CancellationToken**
   - Passer correctement les tokens
   - Gérer les timeouts explicitement

### Phase 3: Correction de la Logique (MOYEN)
1. **Token Counter**
   - Ajuster le comptage pour XML/JSON
   - Gérer les caractères spéciaux

2. **Validation des paramètres**
   - Ajouter les validations manquantes
   - Gérer les cas null/empty

3. **Sérialisation JSON**
   - Configurer correctement JsonSerializerOptions
   - Gérer l'échappement des caractères

## 📝 Implémentation Détaillée

### Étape 1: Créer les Mocks Redis
```csharp
public class MockRedisConnectionMultiplexer : IConnectionMultiplexer
{
    private readonly MockRedisDatabase _database = new();

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        return _database;
    }
    // ... autres méthodes
}

public class MockRedisDatabase : IDatabase
{
    private readonly Dictionary<string, RedisValue> _storage = new();

    public async Task<bool> StringSetAsync(RedisKey key, RedisValue value, ...)
    {
        _storage[key] = value;
        return await Task.FromResult(true);
    }
    // ... autres méthodes
}
```

### Étape 2: Modifier les Tests Redis
```csharp
[Fact]
public async Task StoreAsync_WithValidItem_ShouldSucceed()
{
    // Arrange
    var mockMultiplexer = new MockRedisConnectionMultiplexer();
    var provider = new BasicRedisMemoryProvider(mockMultiplexer, logger);

    // Act & Assert
    // ... test sans connexion réelle
}
```

### Étape 3: Corriger Async/Await
```csharp
// AVANT
var result = someTask.Result;

// APRÈS
var result = await someTask.ConfigureAwait(false);
```

### Étape 4: SQLite en Mémoire
```csharp
var connectionString = "Data Source=:memory:";
using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
// ... utiliser la connexion en mémoire
```

## 🚀 Script d'Exécution

```bash
# 1. Appliquer les corrections
dotnet build tests/Orkeon.Infrastructure.Tests

# 2. Tester par catégorie
dotnet test --filter "FullyQualifiedName~Memory" --no-build
dotnet test --filter "FullyQualifiedName~Http" --no-build
dotnet test --filter "FullyQualifiedName~Strategies" --no-build

# 3. Test complet
dotnet test tests/Orkeon.Infrastructure.Tests --no-build
```

## ✅ Critères de Succès
- ✅ Aucun test ne timeout
- ✅ Tous les tests passent en < 30 secondes
- ✅ Aucune connexion externe requise
- ✅ 100% des tests Infrastructure passent

## 🔄 Ordre d'Implémentation
1. Mock Redis (élimine les timeouts)
2. Mock HTTP (corrige les tests LLM)
3. SQLite mémoire (corrige persistence)
4. Async/Await (élimine deadlocks)
5. Validation/Logique (finalise les corrections)