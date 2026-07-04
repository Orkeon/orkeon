# PLAN DE CORRECTION COMPLET - TESTS INFRASTRUCTURE

## 📊 État Actuel: 84 tests échouent

## 🎯 Catégories de Tests à Corriger

### 1. **ConsoleHumanInputProviderTests** (PRIORITÉ 1)
**Problème:** Tests bloqués en attente d'input humain
**Solution:** Mocker Console.ReadLine() correctement
- GetInputAsync_WithEmptyInput_ReturnsDefaultValue
- GetChoiceAsync_WithEmptyOptions_ReturnsInput
- GetInputAsync_WithOnlyNewline_ReturnsDefaultValue
- GetInputAsync_WithWhitespaceInput_ReturnsDefaultValue

### 2. **OllamaLlmProviderTests** (25+ tests)
**Problème:** HttpClient non mocké, appels API réels
**Solution:** Utiliser TestHttpMessageHandler
- GenerateAsync tests
- ChatAsync tests
- ConfigureHttpClient tests

### 3. **ProcessStrategyFactoryTests** (12 tests)
**Problème:** ServiceProvider non configuré
**Solution:** Créer un ServiceProvider de test avec toutes les dépendances

### 4. **Memory Tests** (15+ tests)
- **InMemoryProviderTests:** Propriétés non implémentées
- **ContextualMemoryTests:** Scoring incorrect
- **EntityMemoryTests:** Thread safety
- **LongTermMemoryTests:** Concurrence

### 5. **Services Tests**
- **OpenAIContextWindowManagerTests:** Gestion null/zero
- **JsonToolCallParserTests:** Parsing JSON nested
- **SimpleEmbeddingServiceTests:** Cancellation
- **SimpleTokenCounterAdditionalTests:** XML counting

### 6. **Tools Tests**
- **AskQuestionToolTests:** Validation input
- **DelegateWorkToolTests:** Parameters

### 7. **Managers Tests**
- **LlmBasedManagerTests:** JSON parsing, fallback logic

### 8. **Strategies Tests**
- **ParallelProcessStrategyTests:** Task distribution
- **SequentialProcessStrategyTests:** Duplicate handling

### 9. **Cache Tests**
- **DistributedLlmCacheTests:** Expiration handling

### 10. **Parsing Tests**
- **MarkdownParserTests:** HTML rendering

## 🔧 Stratégie de Correction

### Phase 1: Corrections Critiques (Human Input)
1. Fixer ConsoleHumanInputProviderTests
2. Éliminer les blocages d'input

### Phase 2: Mocks et Dépendances
1. Mocker HttpClient pour OllamaLlmProviderTests
2. Configurer ServiceProvider pour ProcessStrategyFactoryTests

### Phase 3: Logique Métier
1. Corriger Memory providers
2. Fixer Services (parsing, token counting)
3. Corriger Tools validation
4. Fixer Managers logic

### Phase 4: Validation Finale
1. Exécuter tous les tests
2. Vérifier 100% de succès
3. Optimiser les performances

## 📝 Actions Immédiates

1. **ConsoleHumanInputProviderTests:** Mock Console I/O
2. **OllamaLlmProviderTests:** Mock HTTP responses
3. **ProcessStrategyFactoryTests:** Configure DI container
4. **Memory Tests:** Implement missing properties
5. **Services Tests:** Fix null handling
6. **Tools Tests:** Fix validation logic
7. **Managers Tests:** Fix JSON parsing
8. **Strategies Tests:** Fix task distribution

## ✅ Critères de Succès
- 0 tests échouent
- 0 tests timeout
- Tous les tests passent en < 30 secondes
- Aucune dépendance externe requise