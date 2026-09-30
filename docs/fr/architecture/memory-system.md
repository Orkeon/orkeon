> 🇬🇧 [English version](../../architecture/memory-system.md)

# Système de mémoire

## Interface et types

`IMemoryProvider` (`Orkeon.Domain.Memory`) définit le contrat avec sept méthodes : `StoreAsync`, `GetAsync`, `SearchAsync`, `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` et `SearchSimilarAsync` (recherche vectorielle par similarité cosinus). Les deux dernières ont un corps par défaut dans l'interface (celui de `SearchSimilarAsync` ne renvoie rien) ; tous les providers du dépôt dérivent donc de `MemoryProviderBase` (`Orkeon.Infrastructure.Memory.Base`), qui redéclare `SearchSimilarAsync` **abstraite** — sans quoi un provider qui aurait oublié la recherche vectorielle renverrait silencieusement zéro résultat via l'interface. La classe de base ajoute aussi `UpdateAsync`, `CountAsync`, `ListKeysAsync` et `InitializeAsync(MemoryProviderConfig)`.

Cinq types de mémoire sont définis par `MemoryType` (`Orkeon.Domain.Memory`) : `ShortTerm` (contexte immédiat), `LongTerm` (informations persistantes), `Episodic` (séquences d'événements), `Entity` (informations sur des entités spécifiques), `Procedural` (compétences apprises).

### Capacités optionnelles

Au-delà du contrat de base, un provider adopte des interfaces de capacité (`Orkeon.Domain.Memory`) quand son backend les honore nativement :

| Capacité | Membres | Implémentée par |
|---|---|---|
| `IScoredVectorSearch` | `SearchSimilarWithScoresAsync` (clé + score par résultat) | In-Memory, SQLite, ChromaDB, Pinecone, LanceDB |
| `IBatchUpsert` | `UpsertBatchAsync` | In-Memory, SQLite |
| `IHybridSearchCapable` | `HybridSearchAsync` (vectoriel + plein texte dans le store) | LanceDB |
| `ICollectionAwareMemory` | store / upsert / recherche par collection, `DeleteByFilterAsync`, `DropCollectionAsync` | ChromaDB (collections), Pinecone (namespaces), LanceDB (tables) |

On les découvre avec `provider.TryGetCapability<TCapability>(out var capability)` (`MemoryCapabilityExtensions`), pas avec un simple test `is` : un décorateur comme `EncryptedMemoryProviderDecorator` implémente statiquement toutes les capacités et signale, via `IMemoryCapabilityProbe`, celles que son provider enveloppé possède réellement. Les consommateurs se replient quand une capacité manque — le sous-système RAG, par exemple, utilise des clés préfixées (`rag:{collection}:…`) sur un provider sans `ICollectionAwareMemory` et sa fusion BM25 + RRF in-process sur un provider sans `IHybridSearchCapable`.

## Implémentations

| Provider | Classe | Caractéristiques |
|----------|--------|-----------------|
| In-Memory | `InMemoryProvider` | `ConcurrentDictionary`, recherche vectorielle cosinus, développement/tests |
| Redis | `RedisMemoryProvider` | Clés préfixées `orkeon:memory:` par défaut, politique de retry Polly (`ResiliencePolicies.GetRedisRetryPolicy`), sérialisation JSON camelCase. **Doit être initialisé** : la connexion s'ouvre dans `InitializeAsync` (chaîne de connexion tirée de `MemoryProviderConfig.ConnectionString`, défaut `localhost:6379`) ; tout autre appel avant lève `InvalidOperationException` |
| SQLite | `SqliteMemoryProvider` | `Microsoft.Data.Sqlite`, persistance locale (fichier ou `:memory:`, défaut `Data Source=:memory:`), embeddings en BLOB, recherche plein texte `LIKE` + recherche vectorielle cosinus (scan en mémoire), identifiants et métadonnées préservés à la relecture. Une `Data Source` fichier est un **chemin virtuel** résolu par le VFS et doit se trouver sur un montage accessible en écriture (ex. `Data Source=/output/orkeon-memory.db`), sinon le constructeur lève `FileAccessDeniedException` |
| ChromaDB | `ChromaDbMemoryProvider` | API REST v2 (routes tenant/database), base vectorielle |
| Pinecone | `PineconeMemoryProvider` | Base vectorielle cloud — en-tête `Api-Key` ; l'hôte de l'index est dérivé en `https://{IndexName}-{Environment}.svc.{Environment}.pinecone.io`, sauf si le `HttpClient` injecté a déjà une `BaseAddress` |
| LanceDB | `LanceDbMemoryProvider` | Serveur distant LanceDB Cloud/Enterprise — REST + Arrow IPC, recherche vectorielle et plein-texte **côté serveur** |

## LanceDB (intégration distante réelle)

`LanceDbMemoryProvider` cible un serveur **LanceDB Cloud / Enterprise** via le
protocole [Lance REST Namespace](https://docs.lancedb.com/api-reference/rest/)
(`HttpClient` brut, aucun SDK). Il n'existe **plus de store local** : l'ancienne
implémentation « fichier JSON (+gzip) sur le VFS avec scoring cosinus local » a été
remplacée (décision R4.11 — implémenter LanceDB réellement).

### Protocole

| Opération provider | Endpoint REST | Payload |
|---|---|---|
| `InitializeAsync` (table auto-créée au premier usage) | `POST /v1/table/{t}/exists`, `POST /v1/table/{t}/create`, `POST /v1/table/{t}/create_index` (FTS) | JSON / Arrow IPC stream |
| `StoreAsync` / `UpdateAsync` (upsert) | `POST /v1/table/{t}/merge_insert?on=id&when_matched_update_all=true&when_not_matched_insert_all=true` | Arrow IPC stream |
| `GetAsync` / `ListKeysAsync` | `POST /v1/table/{t}/query` (filtre SQL / pagination `k`+`offset`) | JSON → Arrow IPC file |
| `SearchAsync` (plein-texte BM25) | `POST /v1/table/{t}/query` avec `full_text_query` | JSON → Arrow IPC file |
| `SearchSimilarAsync` (vectoriel) | `POST /v1/table/{t}/query` avec `vector.single_vector` + `distance_type` | JSON → Arrow IPC file |
| `DeleteAsync` / `ClearAsync` | `POST /v1/table/{t}/delete` (prédicat SQL) | JSON |
| `CountAsync` | `POST /v1/table/{t}/count_rows` | JSON → entier brut |

L'authentification utilise l'en-tête `x-api-key` (+ `x-lancedb-database` optionnel).
Les payloads de données sont encodés/décodés en **Arrow IPC** via le paquet
`Apache.Arrow` (Apache-2.0, cf. `THIRD-PARTY-NOTICES.md`). Le schéma de table est
fixe : `id`, `content`, `vector` (`FixedSizeList<float32>[dim]`), `importance`,
`source`, `tags` (JSON), `created_at`, `metadata_json`.

### Configuration

```json
{
  "Orkeon": {
    "LanceDb": {
      "Endpoint": "https://my-deployment.us-east-1.api.lancedb.com",
      "ApiKey": "…",
      "Database": "optionnel",
      "TableName": "orkeon_memories",
      "EmbeddingDimension": 1536,
      "DistanceType": "cosine",
      "CreateFullTextIndexOnInit": true
    }
  }
}
```

Autres clés : `DefaultTopK` (10), `MinSimilarityScore` (0), `VectorWeight` / `FullTextWeight`
(poids de la fusion hybride, 0,7 / 0,3). Valeurs par défaut des clés montrées : `TableName`
`orkeon_memories`, `EmbeddingDimension` 1536, `DistanceType` `cosine`, `CreateFullTextIndexOnInit` `true`.

Enregistrement : `services.AddOrkeonLanceDb(configuration)` (section `Orkeon:LanceDb`) enregistre
`LanceDbMemoryProvider` (et `LanceDbMigrationService`) comme singletons concrets — on les injecte
par leur classe ; l'extension ne réassocie pas `IMemoryProvider`. Sans `Endpoint`, la résolution
du provider lève `InvalidOperationException` (pas de repli local) ; via `MemoryProviderFactory`,
un endpoint absent journalise un avertissement et retombe sur In-Memory (voir plus bas).

### Limites connues

- **Plein-texte** : `SearchAsync` exige un index FTS sur `content`. Le provider tente
  de le créer à la création de la table (`CreateFullTextIndexOnInit`) ; si la création
  échoue, un avertissement est journalisé et l'erreur serveur est propagée telle quelle
  aux appels `SearchAsync` suivants (aucune simulation locale).
- **Hybride** : l'endpoint `query` n'exécute pas la fusion vectoriel+plein-texte en un
  appel ; `HybridSearchAsync` émet **deux requêtes serveur** (classements `_distance`
  et `_score` calculés côté serveur) puis fusionne localement les deux listes classées
  avec `VectorWeight`/`FullTextWeight` — même approche que les rerankers des SDK
  officiels LanceDB.
- **Scores** : la similarité retournée vaut `1 − _distance`, pertinente pour la
  métrique `cosine` (défaut) ; pour `l2`/`dot` l'échelle diffère.
- **`DeleteAsync`/`UpdateAsync`** : l'API delete renvoie une version de commit, pas un
  compteur — le provider vérifie d'abord l'existence de la clé (1 requête `query`
  supplémentaire) pour préserver le contrat booléen.
- **Filtres métadonnées** : `source` se traduit en égalité SQL ; `tag`/`tags` et les
  clés custom en `LIKE '%…%'` sur les colonnes JSON (sémantique de sous-chaîne — les
  caractères jokers SQL `%`/`_` dans les valeurs matchent largement).
- **Dimension fixe** : un embedding dont la taille diffère de `EmbeddingDimension`
  provoque une `InvalidOperationException` explicite (le schéma serveur est figé).

## ChromaDB — version supportée et configuration

`ChromaDbMemoryProvider` cible l'**API REST v2** de ChromaDB
(`/api/v2/tenants/{tenant}/databases/{database}/collections/...`), c'est-à-dire les
**serveurs ChromaDB ≥ 0.6.x, y compris la série 1.x**. Les routes historiques
`/api/v1` ont été retirées côté serveur (réponse HTTP 410) et ne sont plus utilisées
par le provider — les serveurs ≤ 0.5.x (v1 uniquement) ne sont donc **pas supportés**.

Le tenant et la base de données ciblés sont configurables via `ChromaDbOptions`
(section de configuration `Orkeon:ChromaDb`) et valent par défaut
`default_tenant`/`default_database`, les valeurs créées d'office par un serveur
ChromaDB mono-tenant. Un tenant ou une base non par défaut doit déjà exister sur
le serveur (le provider ne les crée pas ; seule la collection est créée à la volée
via `get_or_create`).

```json
{
  "Orkeon": {
    "ChromaDb": {
      "BaseUrl": "http://localhost:8000",
      "Tenant": "default_tenant",
      "Database": "default_database",
      "CollectionName": "orkeon_memories",
      "DefaultTopK": 10
    }
  }
}
```

Le provider expose en outre `HeartbeatAsync()` qui sonde la vivacité du serveur via
`GET /api/v2/heartbeat` et renvoie `false` (sans lever) si le serveur est injoignable
ou répond en erreur.

## Sélection par configuration

### Le provider de l'application

`AddOrkeonInfrastructure()` enregistre `IMemoryProvider` comme singleton construit par
`MemoryProviderFactory` à partir de deux clés de configuration racine : `Memory:Provider` (type,
voir ci-dessous ; absent → In-Memory) et `Memory:ConnectionString`. La méthode `Create` de la
factory ne fait que **construire** le provider ; `CreateAndInitializeAsync` mappe en plus le DTO
vers le `MemoryProviderConfig` du Domain (options `RetentionPeriod`, `MaxItems`, `KeyPrefix`) et
appelle `InitializeAsync`. L'enregistrement DI utilise `Create` — c'est pourquoi Redis, le seul
provider qui refuse de fonctionner sans initialisation, se câble autrement (`AddOrkeonRedisMemory`,
puis l'initialiser soi-même).

`MemoryProviderFactory` (port `IMemoryProviderFactory`) résout le provider depuis
`MemoryProviderConfigDto.Type` (insensible à la casse) :

| Type | Alias | `ConnectionString` | Clés d'`Options` |
|---|---|---|---|
| `inmemory` | `in-memory`, vide | — | — |
| `redis` | | Chaîne de connexion Redis (lue par `InitializeAsync`) | `KeyPrefix` (via `CreateAndInitializeAsync`) |
| `sqlite` | | Chaîne de connexion SQLite, `Data Source` virtuelle (ex. `Data Source=/output/orkeon-memory.db`) | `TableName` (identifiant validé contre l'injection SQL) |
| `chromadb` | `chroma` | URL de base du serveur (défaut `http://localhost:8000`) | — (tenant/base/collection gardent leurs défauts) |
| `pinecone` | | — | `ApiKey`, `Environment`, `IndexName` (défaut `orkeon-memories`), `Namespace` (défaut `default`) |
| `lancedb` | `lance` | Endpoint REST LanceDB Cloud/Enterprise (**obligatoire** : sans lui, avertissement explicite et repli In-Memory) | `ApiKey`, `TableName`, `Database` |

Un type inconnu retombe sur In-Memory avec un avertissement explicite.

### Extensions d'injection de dépendances

| Extension | Enregistre |
|---|---|
| `AddOrkeonInfrastructure()` | `IMemoryProviderFactory` + le singleton `IMemoryProvider` ci-dessus |
| `AddOrkeonRedisMemory(configuration)` | `RedisMemoryProvider`, et **réassocie** `IMemoryProvider` à lui (appeler `InitializeAsync` avant usage) |
| `AddOrkeonChromaDb(configuration)` | `ChromaDbOptions` lié à `Orkeon:ChromaDb` + `ChromaDbMemoryProvider` en singleton concret (son constructeur prend un `HttpClient` que l'hôte enregistre). Appelée automatiquement par `AddOrkeonInfrastructure(configuration)` quand la section existe |
| `AddOrkeonPinecone(configuration)` | Même forme sur `Orkeon:Pinecone` (`ApiKey`, `Environment`, `IndexName`, `Namespace`) |
| `AddOrkeonLanceDb(configuration)` | `LanceDbOptions` sur `Orkeon:LanceDb`, `LanceDbMemoryProvider` (son propre `HttpClient` issu d'`IHttpClientFactory`) et `LanceDbMigrationService` (`MigrateToLanceDbAsync`, copie un provider existant dans la table LanceDB) |
| `AddOrkeonMemoryMigration()` | `MemoryMigrationService` — `MigrateAsync` copie toutes les entrées d'un provider vers un autre. Non enregistrée par `AddOrkeonInfrastructure()` : l'appeler quand on déplace un store (p. ex. In-Memory ou SQLite vers une base vectorielle), résoudre le service et lui passer les providers source et cible (deux instances de `MemoryProviderBase` — la source est parcourue clé par clé) |

Seule `AddOrkeonRedisMemory` remplace l'`IMemoryProvider` de l'application ; les trois extensions de
bases vectorielles rendent le provider injectable par sa classe, à côté de celui que `Memory:Provider`
a sélectionné.

### Sélection du provider par crew

Une crew peut déclarer son propre provider via `memoryProvider` en YAML (ou `CrewBuilder.WithMemoryProvider`).
La sélection voyage jusqu'au run au lieu d'être figée globalement par la configuration `Memory:Provider` :

1. `memoryProvider` est mappé dans `CrewConfiguration.MemoryProvider`, que `CrewFactory` reporte sur
   l'agrégat de domaine `Crew` (`Crew.MemoryProvider`).
2. Au kickoff, l'orchestrateur enregistre `Crew.Id → Crew.MemoryProvider` dans le singleton
   `CrewMemoryProviderRegistry` (indexé par crew, donc les sélections ne fuient jamais d'une crew à l'autre).
3. Quand `MemoryService` matérialise le système de mémoire de cette crew, il résout la chaîne enregistrée
   en un `IMemoryProvider` concret via `MemoryProviderFactory` et adosse la mémoire **long terme** de la
   crew à ce provider (la mémoire court terme reste une fenêtre glissante in-process). Les types
   inconnus/indisponibles conservent le repli In-Memory-avec-avertissement de la factory.

Seul le **type** voyage : la factory est appelée avec une chaîne de connexion vide et sans options,
si bien que chaque provider tourne sur ses défauts — `sqlite` est une base `:memory:` in-process,
`chromadb` vise `http://localhost:8000`, `lancedb` (sans endpoint) retombe sur In-Memory avec un
avertissement, et `redis` n'est jamais initialisé : sa première lecture ou écriture lève. Une crew
qui a besoin d'une vraie connexion utilise plutôt le provider de l'application.

Une crew qui ne déclare aucun `memoryProvider` utilise le store in-process par défaut — le comportement est inchangé.

## Chiffrement au repos

`EncryptedMemoryProviderDecorator` enveloppe n'importe quel `IMemoryProvider` (SQLite,
Redis, In-Memory…) : le contenu est chiffré via `IEncryptionProvider` avant stockage et
déchiffré à la lecture. Les embeddings et les métadonnées restent en clair (nécessaires à
l'indexation) ; la recherche vectorielle (`SearchSimilarAsync`) est déléguée au provider
interne et le contenu des résultats est déchiffré au retour. La recherche plein texte
(`SearchAsync`) sur un store chiffré ne matche que le texte chiffré — utilisez la recherche
vectorielle dans ce cas.

Le décorateur n'est jamais appliqué automatiquement : l'hôte enveloppe le provider qu'il veut
protéger (`new EncryptedMemoryProviderDecorator(inner, encryptionProvider, logger)`), avec
l'`IEncryptionProvider` AES-256-GCM enregistré par `AddOrkeonInfrastructure()`
(`Orkeon:Encryption`). Rechiffrer un store existant sous une nouvelle clé relève de l'opt-in
`AddOrkeonKeyRotation()` — voir [Sous-systèmes opt-in](../reference/opt-in-subsystems.md).

## Mémoire cognitive

Le sous-système cognitif (`Orkeon.Infrastructure.Memory.Cognitive`, opt-in
`AddOrkeonCognitiveMemory(configuration)`) superpose `ICognitiveMemoryService` — un `IMemoryService`
enrichi de `RememberAsync`, `RecallAsync`, `ConsolidateAsync`, `AnalyzeAsync` et
`CheckContradictionsAsync` — au provider de mémoire. Il requiert un `ILlmProvider` (analyse) et un
`IEmbeddingProvider` (rappel). Ses types de résultat vivent dans le Domain (`CognitiveMemoryTypes.cs`) :

- `MemoryAnalysis` — importance notée par le LLM (0,0-1,0), catégorisation, extraction d'entités (`MemoryAnalyzer`) ;
- `ContradictionCheck` + `ConflictResolution` — détection de conflits entre mémoires (`ContradictionDetector`) ;
- `ScoredMemory` — score de rappel composite calculé par `CompositeScorer` : similarité sémantique 0,5 +
  récence 0,3 + importance 0,2 par défaut (`RecallOptions.SemanticWeight`/`RecencyWeight`/`ImportanceWeight`,
  plus `TopK` 10 et `MinScore` 0,1) ; la récence décroît avec une demi-vie de `RecencyHalfLifeHours` ;
- `ConsolidationResult` — consolidation, élagage et résolution de conflits (`MemoryConsolidator`).

Options (`Orkeon:CognitiveMemory`, `CognitiveMemoryOptions`) : `EnableLlmAnalysis` (`true`),
`EnableContradictionDetection` (`true`), `ContradictionCandidateCount` (10), `AnalysisModel`
(null = le modèle du provider), `AnalysisTemperature` (0,1), `PruningThreshold` (0,1),
`PruningMinAgeDays` (30), `RecencyHalfLifeHours` (69), `DefaultRecallOptions`.

---

> **Voir aussi** : [Fournisseurs LLM](./llm-providers.md) · [Sécurité](./security.md) · [Référence de configuration](../reference/configuration.md) · [Retour à l'index](../INDEX.md)
