> 🇬🇧 [English version](../../architecture/memory-system.md)

# Système de mémoire

## Interface et types

`IMemoryProvider` (`Orkeon.Domain.Memory`) définit le contrat avec sept méthodes : `StoreAsync`, `GetAsync`, `SearchAsync`, `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` et `SearchSimilarAsync` (recherche vectorielle par similarité cosinus).

Cinq types de mémoire sont définis par `MemoryType` : `ShortTerm` (contexte immédiat), `LongTerm` (informations persistantes), `Episodic` (séquences d'événements), `Entity` (informations sur des entités spécifiques), `Procedural` (compétences apprises).

## Implémentations

| Provider | Classe | Caractéristiques |
|----------|--------|-----------------|
| In-Memory | `InMemoryProvider` | `ConcurrentDictionary`, recherche vectorielle cosinus, développement/tests |
| Redis | `RedisMemoryProvider` | Clés préfixées, politiques Polly, sérialisation JSON camelCase |
| SQLite | `SqliteMemoryProvider` | `Microsoft.Data.Sqlite`, persistance locale (fichier ou `:memory:`), embeddings en BLOB, recherche plein texte `LIKE` + recherche vectorielle cosinus (scan en mémoire), identifiants et métadonnées préservés à la relecture |
| ChromaDB | `ChromaDbMemoryProvider` | API REST v2 (routes tenant/database), base vectorielle |
| Pinecone | `PineconeMemoryProvider` | Cloud vector database |
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
fixe : `id`, `content`, `vector` (FixedSizeList<float32>[dim]), `importance`,
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

Enregistrement : `services.AddOrkeonLanceDb(configuration)` (section `Orkeon:LanceDb`).
Sans `Endpoint`, la résolution du provider échoue explicitement (pas de repli local).

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

`MemoryProviderFactory` (port `IMemoryProviderFactory`) résout le provider depuis
`MemoryProviderConfigDto.Type` : `inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`.
Pour SQLite, `ConnectionString` est la chaîne de connexion SQLite
(ex. `Data Source=orkeon-memory.db`) et `Options["TableName"]` permet de changer la table
(identifiant validé contre l'injection SQL). Pour LanceDB, `ConnectionString` est l'endpoint
REST LanceDB Cloud/Enterprise et `Options` peut porter `ApiKey`, `TableName`, `Database`
(sans endpoint : avertissement explicite et repli In-Memory ; câblage DI via
`AddOrkeonLanceDb`). Un type inconnu retombe sur In-Memory avec un avertissement explicite.

## Chiffrement au repos

`EncryptedMemoryProviderDecorator` enveloppe n'importe quel `IMemoryProvider` (SQLite,
Redis, In-Memory…) : le contenu est chiffré via `IEncryptionProvider` avant stockage et
déchiffré à la lecture. Les embeddings et les métadonnées restent en clair (nécessaires à
l'indexation) ; la recherche vectorielle (`SearchSimilarAsync`) est déléguée au provider
interne et le contenu des résultats est déchiffré au retour. La recherche plein texte
(`SearchAsync`) sur un store chiffré ne matche que le texte chiffré — utilisez la recherche
vectorielle dans ce cas.

## Mémoire cognitive

Le sous-système cognitif dans `Orkeon.Infrastructure.Memory.Cognitive` ajoute des capacités avancées : `MemoryAnalysis` (scoring d'importance 0.0-1.0, catégorisation, extraction d'entités), `ContradictionCheck` (détection de conflits entre mémoires), `ScoredMemory` (scoring composite : similarité sémantique 0.5 + récence 0.3 + importance 0.2), et `MemoryConsolidator` (consolidation et résolution de conflits).

---

> **Voir aussi** : [Fournisseurs LLM](./llm-providers.md) · [Sécurité](../architecture/security.md) · [Retour à l'index](../INDEX.md)
