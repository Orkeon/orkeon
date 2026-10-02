> 🇬🇧 [English version](../../architecture/memory-system.md)

# Système de mémoire

## Interface et types

`IMemoryProvider` (`Orkeon.Domain.Memory`) définit le contrat avec sept méthodes : `StoreAsync`, `GetAsync`, `SearchAsync` (recherche textuelle), `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` et `SearchSimilarAsync` (recherche vectorielle par similarité cosinus). Les deux dernières ont un corps par défaut dans l'interface (celui de `SearchSimilarAsync` ne renvoie rien) ; tous les providers du dépôt dérivent donc de `MemoryProviderBase` (`Orkeon.Infrastructure.Memory.Base`), qui redéclare `SearchSimilarAsync` **abstraite** — sans quoi un provider qui aurait oublié la recherche vectorielle renverrait silencieusement zéro résultat via l'interface. La classe de base ajoute aussi `UpdateAsync`, `CountAsync` et `ListKeysAsync`. Il n'y a pas d'étape d'initialisation : un provider reçoit ses options dans son constructeur et ouvre sa connexion éventuelle à son premier appel.

### Filtre de métadonnées

`SearchAsync(query, limit, filter, cancellationToken)` et `SearchSimilarAsync` prennent le même `filter`
optionnel (`Dictionary<string, object>`) : `source` est une égalité sur la source de l'entrée,
`tag`/`tags` une appartenance à ses étiquettes, et toute autre clé une égalité sur la **propriété
personnalisée** de ce nom. Chaque provider l'applique **avant** sa limite — une recherche filtrée rend
toutes les correspondances que le magasin contient, jusqu'à la limite, jamais moins parce que des entrées
que le filtre rejette auraient rempli la page :

| Provider | Comment le filtre est appliqué |
|---|---|
| In-Memory | En mémoire, sur chaque entrée, avant la limite ; valeurs comparées sans tenir compte de la casse |
| Redis | Sur chaque entrée du parcours de l'espace de clés, qui s'arrête à la limite ; valeurs comparées sans tenir compte de la casse |
| SQLite | Lignes correspondantes lues de la plus récente à la plus ancienne et vérifiées une à une jusqu'à la limite ; valeurs comparées sans tenir compte de la casse |
| ChromaDB | La clause `where` de la requête (un `$eq` par clé, sous un seul `$and`) ; valeurs exactes |
| Pinecone | Le filtre de métadonnées de la requête (un `$eq` par clé, avec la condition textuelle, sous un seul `$and`) ; valeurs exactes |
| LanceDB | Le prédicat SQL de la requête, en préfiltre ; une propriété personnalisée correspond par sa paire `"clé":"valeur"` dans `metadata_json` (exacte, les jokers `LIKE` d'une valeur matchent largement) |
| `EncryptedMemoryProviderDecorator` | Transmis au provider enveloppé : les métadonnées sont stockées en clair |

ChromaDB et Pinecone gardent les propriétés personnalisées d'une entrée dans ses métadonnées — dans la
collection ou l'espace de noms par défaut comme dans les nommés — et les restituent à la lecture : un
filtre sur une propriété personnalisée la trouve.

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
| Redis | `RedisMemoryProvider` | Clés préfixées `orkeon:memory:` par défaut, politique de retry Polly (`ResiliencePolicies.GetRedisRetryPolicy`), sérialisation JSON camelCase. Options `RedisMemoryOptions` (`Orkeon:Redis` : `ConnectionString`, défaut `localhost:6379` ; `KeyPrefix`). La connexion s'ouvre au premier appel, une seule fois quel que soit le nombre d'appelants concurrents ; un serveur injoignable remonte en `RedisConnectionException` sur cet appel, et l'appel suivant réessaie |
| SQLite | `SqliteMemoryProvider` | `Microsoft.Data.Sqlite`, persistance locale (fichier ou `:memory:`, défaut `Data Source=:memory:`), embeddings en BLOB, recherche plein texte `LIKE` + recherche vectorielle cosinus (scan en mémoire), identifiants et métadonnées préservés à la relecture. Une `Data Source` fichier est un **chemin virtuel** résolu par le VFS et doit se trouver sur un montage accessible en écriture (ex. `Data Source=/output/orkeon-memory.db`), sinon le constructeur lève `FileAccessDeniedException` |
| ChromaDB | `ChromaDbMemoryProvider` | API REST v2 (routes tenant/database), base vectorielle |
| Pinecone | `PineconeMemoryProvider` | Base vectorielle cloud — en-tête `Api-Key`. Les requêtes visent l'hôte de l'index : `Orkeon:Pinecone:Host` s'il est renseigné, sinon le `host` que Pinecone renvoie pour `IndexName` à un unique appel `describe_index` (`GET https://api.pinecone.io/indexes/{IndexName}`) fait au premier usage |
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
| Premier appel (table auto-créée) | `POST /v1/table/{t}/exists`, `POST /v1/table/{t}/create`, `POST /v1/table/{t}/create_index` (FTS) | JSON / Arrow IPC stream |
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

Tout chemin qui sélectionne `lancedb` lit cette section (voir [Sélection par configuration](#sélection-par-configuration)).
`services.AddOrkeonLanceDb(configuration)` expose en plus le `LanceDbMemoryProvider` partagé par sa
classe, avec `LanceDbMigrationService` ; l'extension ne réassocie pas `IMemoryProvider`. Sans
`Endpoint`, résoudre cette classe lève `InvalidOperationException` ; sélectionner `lancedb` par type
journalise un avertissement et retombe sur In-Memory (voir plus bas).

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
- **Filtres métadonnées** : `source` se traduit en égalité SQL ; `tag`/`tags` en
  `LIKE '%…%'` sur le JSON des étiquettes ; une clé personnalisée en
  `LIKE '%"clé":"valeur"%'` sur `metadata_json`, la paire écrite comme l'encodeur JSON du
  provider l'a écrite — la clé et la valeur entière, casse comprise. Les caractères jokers
  SQL `%`/`_` d'une valeur matchent encore largement.
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

### Une section par provider

L'hôte configure chaque provider une fois, dans sa propre section ; tout le reste nomme un **type** :

| Type | Alias | Section | Clés |
|---|---|---|---|
| `inmemory` | `in-memory`, vide | — | — |
| `redis` | | `Orkeon:Redis` | `ConnectionString` (défaut `localhost:6379`, toute chaîne StackExchange.Redis), `KeyPrefix` (défaut `orkeon:memory:`) |
| `sqlite` | | `Orkeon:Sqlite` | `ConnectionString` (défaut `Data Source=:memory:` ; une `Data Source` fichier est un chemin virtuel sur un montage inscriptible, ex. `Data Source=/output/orkeon-memory.db`), `TableName` (identifiant validé contre l'injection SQL), `DefaultTopK`, `MinSimilarityScore` |
| `chromadb` | `chroma` | `Orkeon:ChromaDb` | `BaseUrl` (défaut `http://localhost:8000`), `Tenant`, `Database`, `CollectionName`, `DefaultTopK` |
| `pinecone` | | `Orkeon:Pinecone` | `ApiKey`, `IndexName` (défaut `orkeon-memories`), `Host` (facultatif, voir le tableau plus haut), `Namespace` (défaut `default`) |
| `lancedb` | `lance` | `Orkeon:LanceDb` | `Endpoint` (**obligatoire** : sans lui, avertissement explicite et repli In-Memory), `ApiKey`, `TableName`, `Database`, … ([plus haut](#configuration)) |

```json
{
  "Memory": { "Provider": "redis" },
  "Orkeon": {
    "Redis": { "ConnectionString": "redis.internal:6379,password=…", "KeyPrefix": "team-a:" },
    "Sqlite": { "ConnectionString": "Data Source=/output/orkeon-memory.db" }
  }
}
```

Les secrets restent côté hôte : un fichier de crew ne porte jamais de chaîne de connexion ni de clé d'API.

`MemoryProviderFactory` (port `IMemoryProviderFactory`, `GetProvider(type)`, insensible à la casse)
distribue **une instance par type** : le provider de l'application, chaque crew qui nomme ce type et
le store RAG de ce type la partagent — une connexion Redis, un client HTTP, une connexion SQLite. La
factory possède ces instances et les libère avec le conteneur. Créer un provider ne connecte jamais ;
la connexion s'ouvre à son premier appel. Un type inconnu retombe sur In-Memory avec un avertissement
explicite ; `SupportedTypes` liste les alias.

### Le provider de l'application

`AddOrkeonInfrastructure()` lie les cinq sections et enregistre `IMemoryProviderFactory` et le
singleton `IMemoryProvider`, dont le type est `Memory:Provider` (absent → In-Memory). `Memory:Provider`
ne porte que le type ; la connexion est la section du provider choisi.

### Extensions d'injection de dépendances

| Extension | Enregistre |
|---|---|
| `AddOrkeonInfrastructure()` | Les cinq sections, `IMemoryProviderFactory` + le singleton `IMemoryProvider` ci-dessus |
| `AddOrkeonRedisMemory(configuration)` | Lie `Orkeon:Redis` depuis `configuration`, expose le `RedisMemoryProvider` partagé par sa classe et **réassocie** `IMemoryProvider` à lui — comme `Memory:Provider = redis` |
| `AddOrkeonChromaDb(configuration)` | Lie `Orkeon:ChromaDb` et expose le `ChromaDbMemoryProvider` partagé par sa classe (client HTTP issu d'`IHttpClientFactory`). Appelée par `AddOrkeonInfrastructure(configuration)` quand la section existe |
| `AddOrkeonPinecone(configuration)` | Même forme sur `Orkeon:Pinecone` (`ApiKey`, `IndexName`, `Host`, `Namespace`) |
| `AddOrkeonLanceDb(configuration)` | Même forme sur `Orkeon:LanceDb`, plus `LanceDbMigrationService` (`MigrateToLanceDbAsync`, copie un provider existant dans la table LanceDB) |
| `AddOrkeonMemoryMigration()` | `MemoryMigrationService` — `MigrateAsync` copie toutes les entrées d'un provider vers un autre. Non enregistrée par `AddOrkeonInfrastructure()` : l'appeler quand on déplace un store (p. ex. In-Memory ou SQLite vers une base vectorielle), résoudre le service et lui passer les providers source et cible (deux instances de `MemoryProviderBase` — la source est parcourue clé par clé) |

Seule `AddOrkeonRedisMemory` remplace l'`IMemoryProvider` de l'application ; les trois extensions de
bases vectorielles rendent le provider partagé injectable par sa classe, à côté de celui que
`Memory:Provider` a sélectionné.

### La mémoire d'une crew : provider et portée

Une crew peut déclarer son propre provider via `memoryProvider` en YAML (ou `CrewBuilder.WithMemoryProvider`).
La sélection voyage jusqu'au run au lieu d'être figée globalement par la configuration `Memory:Provider` :

1. `memoryProvider` est mappé dans `CrewConfiguration.MemoryProvider`, que `CrewFactory` reporte sur
   l'agrégat de domaine `Crew` (`Crew.MemoryProvider`), avec le nom de la crew (`Crew.Name` : le
   `name:` de sa configuration — fichier YAML, dossier de crew, crew `.ork.ts` — ou `CrewBuilder.Name`
   en C#).
2. Au kickoff, l'orchestrateur enregistre les deux dans le singleton `CrewMemoryProviderRegistry`
   (`Record(crewId, providerType, crewName)` ; indexé par crew, donc les sélections ne fuient jamais
   d'une crew à l'autre).
3. Quand `MemoryService` matérialise le système de mémoire de cette crew, il demande à
   `MemoryProviderFactory` le provider partagé de ce type — connecté depuis la section de l'hôte — et
   adosse la mémoire **long terme** de la crew à ce provider (la mémoire court terme reste une fenêtre
   glissante in-process). Les types inconnus conservent le repli In-Memory-avec-avertissement de la factory.

`memoryProvider:` est un type, rien de plus : `memoryProvider: "Redis"` se connecte avec `Orkeon:Redis`,
`"SQLite"` avec `Orkeon:Sqlite` (une base `:memory:` in-process quand cette section est absente).

**La portée est le nom de la crew.** L'instance du provider est partagée — par toutes les crews de ce
type, et par le magasin RAG de ce type : sans `Orkeon:Rag:Provider`, le magasin RAG est le provider
ambiant, dont les fragments, manifestes et registres vivent dans le même espace de clés. Chaque entrée
que range la mémoire long terme d'une crew porte deux propriétés personnalisées, `kind = crew-memory` et
`crew = <le nom de la crew>`, et chaque recherche dans cette mémoire les demande toutes deux au provider
([filtre de métadonnées](#filtre-de-métadonnées), appliqué avant la limite). Une crew lit donc ce
qu'elle a rangé, dans ce run et dans les runs précédents d'une crew de ce nom — c'est ce qui rend la
mémoire durable — et jamais les entrées d'une autre crew, ni un fragment RAG. Une crew sans nom (construite
en C# sans `CrewBuilder.Name`) a pour portée son id : sa mémoire dure un run. Deux crews d'un même nom
partagent leur mémoire — avec In-Memory, Redis et SQLite, deux noms qui ne diffèrent que par la casse
aussi. `MemoryCoordinator` étiquette en outre chaque souvenir qu'il range `crew:<nom>` (l'id de la crew
quand elle n'a pas de nom).

Vider la mémoire d'une crew (`IMemoryService.ClearMemoryAsync`) supprime les entrées que ce système de
mémoire a rangées — celles du run en cours —, jamais le reste du magasin partagé, et la libérer ne libère
jamais le provider partagé.

Une crew qui ne déclare aucun `memoryProvider` utilise un magasin in-process à elle, qui finit avec le run.

**Ce qui est rangé.** Après chaque tâche réussie, la sortie de l'agent, sous cet agent
(`IMemoryCoordinator.StoreTaskResultAsync`). En mode `Consensual`, seule la réponse retenue par le vote
est un résultat de tâche : les réponses candidates et les bulletins s'exécutent avec
`SimpleExecutionContext.StoreResultInMemory` à faux, et la stratégie range la réponse retenue une fois,
sous l'agent qui l'a écrite ([Types de processus](../orchestration/process-types.md#4-consensual--vote-et-consensus)).
Aucun run livré ne relit cette mémoire dans un prompt — un hôte C# la lit via `IMemoryService`
(voir [Limites connues](../reference/limitations.md)).

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
