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
| ChromaDB | La clause `where` de la requête (un `$eq` par clé, sous un seul `$and`) — de la requête vectorielle, et du `/get` qui sert une recherche textuelle ; valeurs exactes |
| Pinecone | Le filtre de métadonnées de la requête vectorielle (un `$eq` par clé) ; valeurs exactes. Pinecone n'a pas de recherche textuelle : son `SearchAsync` lève `NotSupportedException` |
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
| ChromaDB | `ChromaDbMemoryProvider` | API REST v2 (routes tenant/database), base vectorielle. Un enregistrement s'ajoute avec son embedding seulement — `StoreAsync` refuse une entrée qui n'en a pas, avant toute requête. Une recherche textuelle (`SearchAsync`) est un `/get` des documents qui contiennent la requête (`where_document` `$contains`, sensible à la casse comme le serveur la fait), dans le filtre ; une requête vide envoie le filtre seul. L'API HTTP n'a pas de requête textuelle : `query_texts` n'existe que dans les clients, qui calculent le vecteur eux-mêmes |
| Pinecone | `PineconeMemoryProvider` | Base vectorielle cloud — en-tête `Api-Key`. Les requêtes visent l'hôte de l'index : `Orkeon:Pinecone:Host` s'il est renseigné, sinon le `host` que Pinecone renvoie pour `IndexName` à un unique appel `describe_index` (`GET https://api.pinecone.io/indexes/{IndexName}`) fait au premier usage. Un vecteur exige des valeurs : `StoreAsync` refuse une entrée sans embedding, avant toute requête. On cherche par vecteur (`SearchSimilarAsync`) : `SearchAsync` lève `NotSupportedException` — il n'y a pas de recherche textuelle pour la servir |
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
ne porte que le type ; la connexion est la section du provider choisi. C'est aussi là que vit la mémoire
d'une crew nommée qui ne déclare aucun `memoryProvider:` ([plus bas](#la-mémoire-dune-crew--provider-et-portée)).

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

`memory:` décide si une crew se souvient — `memory: true` en YAML, `.memory(true)` en `.ork.ts`,
`CrewBuilder.EnableMemory()` en C# ; coupée par défaut dans les trois, comme chez CrewAI :

- **`memory: true`** : la crew range le résultat que chaque tâche garde, et rappelle ses souvenirs avant
  chaque tâche ([plus bas](#ce-que-rappelle-une-crew)).
- **`memory: false`, ou rien** : rien n'est rangé, rien n'est rappelé, et aucun système de mémoire n'est
  matérialisé pour la crew.

`memoryProvider:` dit **où** vit la mémoire d'une crew, et exige `memory: true` : un provider nommé pour une
crew sans mémoire est refusé — au chargement (`memoryProvider: 'sqlite' needs memory: true …`), et par
`Crew.Create(CrewCreateOptions)` / `CrewBuilder.Build()` en C#, tous deux avec le remède.

1. `memory` et `memoryProvider` sont mappés dans `CrewConfiguration.Memory` / `.MemoryProvider`, que
   `CrewFactory` reporte sur l'agrégat de domaine `Crew` (`Crew.MemoryEnabled`, `Crew.MemoryProvider`),
   avec le nom de la crew (`Crew.Name` : le `name:` de sa configuration — fichier YAML, dossier de crew,
   crew `.ork.ts` — ou `CrewBuilder.Name` en C#).
2. Au kickoff, l'orchestrateur enregistre les trois dans le singleton `CrewMemoryProviderRegistry`
   (`Record(crewId, providerType, crewName, memoryEnabled)` ; indexé par crew, donc les sélections ne
   fuient jamais d'une crew à l'autre ; `IsMemoryEnabled(crewId)` est ce que le run demande).
3. Quand `MemoryService` matérialise le système de mémoire de cette crew, il adosse sa mémoire **long
   terme** (la mémoire court terme reste une fenêtre glissante in-process) à :
   - le provider partagé du type que la crew a déclaré, demandé à `MemoryProviderFactory` — connecté depuis
     la section de l'hôte ; les types inconnus conservent le repli In-Memory-avec-avertissement de la factory ;
   - sinon, pour une crew **nommée**, le provider par défaut de l'hôte — l'`IMemoryProvider` de
     l'application, dont le type est `Memory:Provider` (In-Memory s'il est absent) ;
   - sinon — une crew construite en C# sans nom — un magasin in-process à elle, qui finit avec le run :
     sa portée serait un id qu'aucun run suivant ne porte, que nul ne relirait.

`memoryProvider:` est un type, rien de plus : `memoryProvider: "Redis"` se connecte avec `Orkeon:Redis`,
`"SQLite"` avec `Orkeon:Sqlite` (une base `:memory:` in-process quand cette section est absente).

Ce que cela veut dire pour les hôtes livrés : `orkeon-host` se souvient d'un message à l'autre — son
In-Memory vit avec le démon —, tandis qu'`orkeon run` ne se souvient d'un processus à l'autre qu'avec un
provider durable : `memoryProvider: sqlite` (ou Redis, une base vectorielle), ou un `Memory:Provider`
durable. Avec In-Memory, la mémoire d'une crew vit en RAM, le temps du processus.

**La portée est le nom de la crew.** L'instance du provider est partagée — par toutes les crews de ce
type, et par le magasin RAG de ce type : sans `Orkeon:Rag:Provider`, le magasin RAG est le provider
ambiant, dont les fragments, manifestes et registres vivent dans le même espace de clés. Chaque entrée
que range la mémoire long terme d'une crew porte deux propriétés personnalisées, `kind = crew-memory` et
`crew = <le nom de la crew>` (`CrewMemoryScope` : `Stamp(item, scope)`, `Filter(scope)`), et chaque
recherche dans cette mémoire les demande toutes deux au provider
([filtre de métadonnées](#filtre-de-métadonnées), appliqué avant la limite). Une crew lit donc ce
qu'elle a rangé, dans ce run et dans les runs précédents d'une crew de ce nom — c'est ce qui rend la
mémoire durable — et jamais les entrées d'une autre crew, ni un fragment RAG. Une crew sans nom (construite
en C# sans `CrewBuilder.Name`) a pour portée son id : sa mémoire dure un run. Deux crews d'un même nom
partagent leur mémoire — avec In-Memory, Redis et SQLite, deux noms qui ne diffèrent que par la casse
aussi. `MemoryCoordinator` étiquette en outre chaque souvenir qu'il range `crew:<nom>` (l'id de la crew
quand elle n'a pas de nom).

Vider la mémoire d'une crew (`IMemoryService.ClearMemoryAsync`) supprime les entrées que ce système de
mémoire a rangées — celles du run en cours —, jamais le reste du magasin partagé, et la libérer ne libère
jamais le provider partagé. Il n'y a pas de rétention : la mémoire d'une crew croît d'une entrée par tâche
réussie, run après run, et aucun verbe ne la remet à zéro.

**Ce qui est rangé.** Après chaque tâche réussie, sa sortie — exactement la sortie de la tâche, sous
l'agent qui l'a écrite (`IMemoryCoordinator.StoreTaskResultAsync`) —, plongée sur sa tâche (la
description, avec les variables du run) et le début de sa sortie (1 000 caractères), avec les propriétés
personnalisées `agent_id`, `agent_role`, `task_id`, `task_description` et `stored_at`. Seulement ce
qu'une tâche garde :

- en mode `Consensual`, la réponse retenue par le vote : les réponses candidates et les bulletins
  s'exécutent avec `SimpleExecutionContext.StoreResultInMemory` à faux, et la stratégie range la réponse
  retenue une fois, sous l'agent qui l'a écrite ([Types de processus](../orchestration/process-types.md#4-consensual--vote-et-consensus)) ;
- en mode `Hierarchical`, la sortie que le manager a acceptée : chaque essai s'exécute avec
  `StoreResultInMemory` à faux, et la stratégie range l'acceptée une fois, sous l'agent assigné — rien
  quand le manager rejette les trois essais, ni quand le travailleur échoue
  ([Types de processus](../orchestration/process-types.md#2-hierarchical--manager--workers)) ;
- jamais un bulletin, jamais un essai de forge ([Forge](../getting-started/forge-a-team-from-a-need.md)).

#### Ce que rappelle une crew

Avant chaque tâche, `AgentExecutionService` demande à `IMemoryCoordinator.RecallAsync` les souvenirs de la
crew les plus proches de la tâche, et le prompt utilisateur les porte :

- **La requête** est la tâche telle que la recherche de connaissance la voit : sa description et sa sortie
  attendue, avec les variables du run. Un souvenir est plongé sur sa propre tâche et sa sortie : la même
  tâche d'un run précédent remonte ainsi en tête.
- **La recherche** est vectorielle, dans la portée de la crew : le `SearchSimilarAsync` du provider, avec
  le filtre `kind` et `crew` (sur le magasin in-process d'une crew sans nom, un cosinus calculé en processus).
- **Ce qui est écarté** : un souvenir égal à l'une des sorties que le prompt porte déjà de ce run. Le rappel
  demande autant de résultats de plus qu'il y a de sorties précédentes, et rend donc tout de même
  `RecallLimit` souvenirs quand la crew en a autant d'autres ; un contenu rappelé deux fois est gardé une fois.
- **Les bornes** viennent de la section `Orkeon:CrewMemory` (`CrewMemoryOptions`, liée par
  `AddOrkeonInfrastructure()`) : `RecallLimit` 5 souvenirs, `MinScore` 0,6 (cosinus), `MaxChars` 4 000
  caractères de contenu au total — le dernier souvenir qui ne tient pas est tronqué, les suivants écartés.
  `MinScore` est à l'échelle de l'embedder ; 0,6 a été mesuré sur le modèle local (BGE-micro-v2), où la
  même tâche d'un run précédent obtient 0,72 et plus, une tâche anglaise sans rapport 0,53 et moins, et un
  travail voisin entre les deux (de 0,59 à 0,72). La requête porte les variables du run : une longue entrée
  qui partage le sujet du souvenir a porté une tâche sans rapport à 0,61. Ce modèle lit l'anglais : un
  texte français obtient un score élevé quoi qu'il dise (0,67 pour une paire sans rapport). Un autre
  embedder demande sa propre mesure.
- **Le format** : une section du prompt utilisateur, après les résultats des tâches précédentes et avant la
  connaissance récupérée — l'en-tête *From this crew's memory — earlier work, possibly outdated; use it
  only where it helps:*, puis pour chaque souvenir une ligne `--- 2026-09-30 · Analyst · Summarize the
  weekly news ---` (sa date, le rôle de l'agent, la tâche tronquée à 80 caractères) suivie de son contenu.
  Le Guardian la filtre avec le reste du prompt (phase d'entrée).
- **Qui rappelle** : toute exécution qui répond à une tâche — une candidate du consensuel, un essai du
  hiérarchique —, mais pas un bulletin (`AgentBallotCollector` l'exécute avec
  `SimpleExecutionContext.RecallFromMemory` à faux).

Une crew relancée sans être rechargée — la boucle `KickoffAsync` en C#, le `CrewAgent` à crew fixe —
retrouve ses tours précédents de la même façon : n'est écarté que ce que le prompt porte déjà.

#### L'embedder, et les échecs

Chaque souvenir est plongé à son rangement, et chaque rappel plonge sa requête, avec l'`IEmbeddingProvider`
de l'hôte — le port du RAG : le modèle local, qu'`orkeon run` et `orkeon-host` enregistrent par la
section `RaggableTree` (`RaggableTree:Enabled: false` le retire) et le REPL toujours, sinon la section
`Orkeon:Embeddings`.
Pinecone et ChromaDB reçoivent donc le vecteur qu'ils exigent.

Une crew `memory: true` est vérifiée **avant son premier appel LLM** : l'orchestrateur appelle
`IMemoryCoordinator.EnsureReadyAsync`, qui plonge un texte sonde puis cherche une fois dans la mémoire de la
crew. Un embedder absent, une clé refusée, un magasin injoignable ou un vecteur de mauvaise dimension y fait
échouer la crew, avec son nom et la cause dans son erreur. La dimension du magasin doit être celle de
l'embedder : LanceDB est créé en 1 536 par défaut (`Orkeon:LanceDb:EmbeddingDimension`) quand le modèle
local donne 384, et la dimension d'un index Pinecone est fixée à sa création.

Pendant le run, une mémoire qui échoue est un avertissement, jamais une tâche en échec : un rangement ou un
rappel qui lève est journalisé en `Warning`, avec la crew, la tâche, l'agent, le magasin et la cause ; la
tâche garde sa sortie, et un rappel en échec la laisse sans souvenirs. Une annulation n'est jamais avalée.
La règle vit dans `MemoryCoordinator`, seul point d'entrée de la mémoire du run.

Les appels d'embedding ne sont pas comptés par le compteur de jetons ([Limites connues](../reference/limitations.md)).

#### Ce qui n'est pas câblé

- **Le kickoff en streaming** (`KickoffStreamingAsync`, une API C# qu'aucun hôte livré n'appelle) ne
  rappelle ni ne range la mémoire d'une crew — il ignore aussi la connaissance attachée. Une crew
  `memory: true` reçoit un avertissement qui le dit ; `KickoffAsync` fait les deux.
- **Pas de LLM dans le rappel** : ni extraction de faits, ni rappel classé par LLM, ni score composite
  récence/importance (celui de CrewAI) : le rappel est vectoriel. La [mémoire cognitive](#mémoire-cognitive)
  est l'option C# qui analyse par LLM.

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
`CheckContradictionsAsync` — à la mémoire de la crew. Il requiert un `ILlmProvider` (analyse) et un
`IEmbeddingProvider` (chaque souvenir rangé, chaque requête de rappel et chaque souvenir fusionné est
plongé). Ses types de résultat vivent dans le Domain (`CognitiveMemoryTypes.cs`) :

- `MemoryAnalysis` — importance notée par le LLM (0,0-1,0), catégorisation, extraction d'entités (`MemoryAnalyzer`) ;
- `ContradictionCheck` + `ConflictResolution` — détection de conflits entre mémoires (`ContradictionDetector`) ;
- `ScoredMemory` — score de rappel composite calculé par `CompositeScorer` : similarité sémantique 0,5 +
  récence 0,3 + importance 0,2 par défaut (`RecallOptions.SemanticWeight`/`RecencyWeight`/`ImportanceWeight`,
  plus `TopK` 10 et `MinScore` 0,1) ; la récence décroît avec une demi-vie de `RecencyHalfLifeHours` ;
- `ConsolidationResult` — consolidation, élagage et résolution de conflits (`MemoryConsolidator`).

Il travaille dans **la mémoire de la crew elle-même** : la mémoire long terme qu'`IMemoryService`
matérialise pour la crew — son provider déclaré, sinon celui de l'hôte pour une crew nommée, avec son nom
pour portée ([plus haut](#la-mémoire-dune-crew--provider-et-portée)) ; un magasin in-process pour une crew
sans nom. Une crew a ainsi une seule mémoire : ce dont elle se souvient est estampillé `kind = crew-memory`
et `crew = <portée>` comme les souvenirs que rangent ses runs — ses runs le rappellent, et son rappel
retrouve les leurs —, et ni la mémoire d'une autre crew, ni un fragment RAG du même magasin ne remontent
jamais. La portée est celle que le kickoff de la crew a enregistrée : avant son premier kickoff, un id de
crew est une crew sans nom.

- `RememberAsync` range l'élément **une seule fois**, dans cette mémoire.
- `RecallAsync` et les candidates de contradiction la cherchent **par similarité**, dans la portée.
- Un conflit se résout **là d'où viennent ses candidates** : les souvenirs à garder, retirer ou fusionner
  sont les candidates montrées au détecteur, retirées de la mémoire de la crew par la clé qu'elle a rendue ;
  un souvenir fusionné y est plongé puis rangé.
- `ConsolidateAsync` trouve les souvenirs de la crew par similarité dans la portée, sans seuil de score
  (jusqu'à 1 000 — ce que Pinecone rend à une requête), les regroupe par cosinus, range chaque souvenir
  fusionné plongé et retire ceux qu'il remplace, dans la même mémoire. Un souvenir qui revient sans son
  vecteur (ChromaDB n'en rend pas avec un résultat de similarité) reste un groupe à lui seul.

Options (`Orkeon:CognitiveMemory`, `CognitiveMemoryOptions`) : `EnableLlmAnalysis` (`true`),
`EnableContradictionDetection` (`true`), `ContradictionCandidateCount` (10), `AnalysisModel`
(null = le modèle du provider), `AnalysisTemperature` (0,1), `PruningThreshold` (0,1),
`PruningMinAgeDays` (30), `RecencyHalfLifeHours` (69), `DefaultRecallOptions`.

---

> **Voir aussi** : [Fournisseurs LLM](./llm-providers.md) · [Sécurité](./security.md) · [Référence de configuration](../reference/configuration.md) · [Retour à l'index](../INDEX.md)
