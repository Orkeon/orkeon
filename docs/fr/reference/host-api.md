# L'API du host et les sondes de santé

`orkeon-host` peut servir une petite API HTTP sur un listener à lui : deux sondes de santé pour systemd, Docker et Kubernetes, et les routes pour lancer une crew hébergée, la suivre et l'arrêter. Elle est **désactivée par défaut** : un daemon ne doit pas ouvrir un port que personne n'a demandé. (Le serveur A2A est un autre listener, activé à part — voir [le host de service](../architecture/service-host.md).)

```json
{
  "Orkeon": {
    "Host": {
      "Api": {
        "Enabled": true,
        "Prefix": "http://localhost:5190/",
        "Security": { "AllowedAuthSchemes": ["ApiKey"], "ApiKeySecretNames": ["ORKEON_HOST_API_KEY"] },
        "ReadinessTags": [],
        "IdempotencyTtl": "24:00:00",
        "IdempotencyMaxKeys": 10000
      }
    }
  }
}
```

| Clé | Défaut | Sens |
|---|---|---|
| `Enabled` | `false` | Sert l'API. Désactivée, le host n'écoute aucun port pour elle. |
| `Prefix` | `http://localhost:5190/` | Le préfixe du listener : schéma, hôte, port et une barre oblique finale. `http://+:5190/` écoute sur toutes les interfaces. |
| `Security` | aucune | Ce qu'un appelant présente sur toute route sauf les deux sondes : `AllowedAuthSchemes` (`Bearer`, `ApiKey`), `ApiKeySecretNames`, `RequireMutualTls`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints` — les règles de `A2A:Security`, appliquées par le même code (`HttpRequestAuthenticator`). `ApiKey` lit ses clés par le fournisseur de secrets (`ORKEON_<NOM>`), à chaque requête. `Bearer` demande un validateur : `A2A:Security:AzureAD` ou `A2A:Security:Oidc`, ou un `IAuthenticationProvider` que le host enregistre. |
| `ReadinessTags` | `[]` | Tags de health checks ajoutés à la disponibilité. `llm` ajoute le contrôle du fournisseur LLM (voir plus bas). |
| `SseKeepAliveInterval` | `00:00:15` | Combien de temps un flux d'événements silencieux attend avant d'écrire une trame de maintien. |
| `IdempotencyTtl` | `24:00:00` | Combien de temps une clé d'idempotence est retenue. |
| `IdempotencyMaxKeys` | `10000` | Le plus de clés tenues en mémoire. |

Un préfixe au-delà de la boucle locale (`http://+:…`) sans schéma d'authentification ni `RequireMutualTls` est **refusé au démarrage** (code 78) : il lancerait des crews pour quiconque peut le joindre. Le sont aussi un préfixe sans barre oblique finale, un schéma `Bearer` sans validateur, `RequireMutualTls` sans ancre de confiance et un port qu'un autre processus tient. Sous Windows le listener est HTTP.sys : un préfixe autre que la boucle locale demande une réservation d'URL pour le compte du service (`netsh http add urlacl url=http://+:5190/ user=<compte>`) ; sans elle le démarrage est refusé, avec le même code et l'indication.

## Sondes

Les deux répondent sans authentification — un orchestrateur ne détient aucun identifiant — avec un corps JSON `{ "status", "checks": [ { "name", "status", "description" } ] }`. L'exception d'un contrôle en échec va au journal, jamais au corps.

### `GET /healthz` — vivacité

Exécute les contrôles de tag `live` : **`system_resources`** (la mémoire du processus contre `Telemetry:MaxMemoryMB` ; au-delà, `degraded`, qui répond encore 200). Il lit le processus et rien d'autre : **la vivacité ne dépend jamais du LLM, du magasin d'état, de l'historique de runs ni d'aucun autre service**, si bien qu'une panne de fournisseur ne fait jamais redémarrer par un orchestrateur un daemon qui va bien. `200`, ou `503` dès que le host s'arrête (voir plus bas).

### `GET /readyz` — disponibilité

Exécute les contrôles de tag `ready`, plus ceux dont `ReadinessTags` liste le tag. `200` quand aucun n'est en échec, `503` sinon.

| Contrôle | Ce qu'il vérifie | Atteint |
|---|---|---|
| `crew_registry` | Au moins une crew est hébergée et le host admet encore des runs. | la mémoire |
| `state_store` | Le magasin de checkpoints répond à une lecture (`IStateStore`). Sain quand aucun n'est enregistré. | le magasin |
| `run_history` | L'historique de runs répond à une requête (`IRunHistoryReader`). Sain quand l'historique est désactivé. | l'historique |
| `memory_provider` | Le fournisseur de mémoire répond (`IMemoryProvider`). | le fournisseur |
| `llm_provider` | **Seulement quand `ReadinessTags` contient `llm`.** Le fournisseur LLM répond. | le service du modèle |

Le contrôle du LLM est hors disponibilité par défaut : la panne d'un fournisseur fait tomber tous les runs mais pas le daemon, et un orchestrateur qui sortirait le host de la rotation transformerait une panne partielle en panne totale. Ajoutez `"llm"` à `ReadinessTags` quand un répartiteur de charge doit s'écarter d'un host qui ne joint pas son modèle.

**Pendant l'arrêt du host**, les deux sondes répondent `503` avec `"status": "stopping"` dès la fermeture de l'admission — toute la `ShutdownGracePeriod` — pour qu'un répartiteur cesse d'envoyer du trafic pendant que les runs en cours se terminent.

```bash
curl -s localhost:5190/healthz
curl -s -o /dev/null -w '%{http_code}\n' localhost:5190/readyz
```

systemd n'a pas de sonde HTTP : utilisez `ExecStartPost` pour attendre la disponibilité si un script en a besoin (`curl --retry 10 --retry-connrefused -f localhost:5190/readyz`), et laissez `Type=notify` comme disponibilité propre de l'unité. Docker et Kubernetes :

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s CMD curl -fs http://localhost:5190/healthz || exit 1
```

```yaml
livenessProbe:  { httpGet: { path: /healthz, port: 5190 } }
readinessProbe: { httpGet: { path: /readyz,  port: 5190 } }
```

(le préfixe du conteneur est `http://+:5190/`, avec un schéma d'authentification ; les sondes restent ouvertes.)

## Runs

Toute route ci-dessous demande les identifiants de `Security`. Les corps et réponses sont du JSON, aux noms en snake_case ; une erreur est `{ "error": "<code>", "message": "…" }`.

### Un seul id de bout en bout

L'id d'un run est celui que le daemon frappe quand il admet le run (une ULID, un `ExecutionId`). C'est l'identité du run partout : le `run_id` de cette API, le run d'une tâche A2A, `orkeon runs` et l'historique de runs, la session de checkpoint (`--resume`), le `correlationId` de chaque événement. `run.started` le répète sous `hostRunId`.

### `POST /runs`

```json
{ "crew": "support", "prompt": "What changed?", "variables": { "topic": "elections" }, "idempotency_key": "order-8841" }
```

`crew` et `prompt` sont obligatoires ; `variables` ne contient que des chaînes ; un champ inconnu est refusé. Le corps est plafonné à 1 Mio.

| Réponse | Quand |
|---|---|
| `202 { "run_id", "idempotent_replay": false }` | Le run est admis ; il continue en arrière-plan. |
| `200 { "run_id", "idempotent_replay": true }` | L'`idempotency_key` avait été vue avec la même requête : le run de la première. |
| `409 idempotency_conflict` | La clé avait été vue avec une requête différente. |
| `400 invalid_request` | Un corps mal formé. `413` au-delà de 1 Mio. |
| `404 unknown_crew` | Aucune crew hébergée de ce nom. |
| `429 at_limit` + `Retry-After: 5` | La crew est à son `MaxConcurrentRuns`. |
| `503 host_stopping` | Le host s'arrête : rien ne démarre. |

### `GET /runs`, `GET /runs/{id}`, `POST /runs/{id}/cancel`

`GET /runs?crew=&limit=` liste les runs enregistrés, du plus récent au plus ancien (`limit` de 1 à 500, 50 par défaut) ; `GET /runs/{id}` en lit un : `run_id`, `crew`, `status` (`running`, `succeeded`, `failed`, `orphaned`, `waiting_for_input`), `started_at`, `finished_at`, `error`. `POST /runs/{id}/cancel` répond `202` et arrête un run en cours (le run se lit alors `failed` : son `run.finished` porte le code de sortie 130), `409 run_not_running` pour un run terminé, `404 unknown_run` pour un id inconnu. Les routes de lecture lisent l'historique de runs : avec `Orkeon:RunHistory:Enabled` désactivé elles répondent `501 run_history_disabled` (le daemon l'active par défaut).

### `POST /runs/{id}/input`

Répond à la question qu'un run attend (FWK-23 ; demande `Orkeon:Checkpointing:Enabled`). Corps : `{"request_id": "...", "answer": "..."}` ; `request_id` est facultatif (la question attendue par le run). `answer` est le texte (une confirmation vaut `yes` ou `no`, plusieurs choix un tableau JSON, une approbation d'outil `allow`, `allow_for_session` ou `deny`). Le run qui attend la question dans ce processus continue : `200 {"status": "delivered"}`. Un run suspendu (délai d'attente écoulé, `Orkeon:HumanInput:Pause: Suspend`) voit la réponse enregistrée : `202 {"status": "recorded"}`, et `orkeon run <crew> --resume <id>` la reprend (le démon ne reprend pas un run de lui-même). La première réponse gagne : `409 no_pending_input` pour la suivante, `409 request_mismatch` pour une autre question que celle posée, `400 invalid_request` sans réponse, `404` pour un run inconnu, `409 input_unavailable` quand l'hôte ne garde aucun checkpoint. La route passe par la même autorisation que les autres routes `/runs`. `Orkeon:HumanInput:Pause` vaut `Hold` par défaut dans le démon, avec un `HoldTimeout` de 30 minutes ; l'historique du run montre `input.needed` (avec `requestId`) et `input.given`, et un run suspendu finit par `run.finished` `status: waiting_for_input`, `exitCode: 75`.

### `GET /runs/{id}/events`

Les événements du run, depuis l'historique, puis en direct jusqu'à `run.finished`. Avec `Accept: text/event-stream` c'est du SSE : `id: <seq>`, `event: <kind>`, `data: <l'événement, dans la forme fil du bus d'événements de run>`. Sinon c'est du NDJSON (`application/x-ndjson`), un événement par ligne. `?from=<seq>` démarre à ce seq (inclus) ; l'en-tête `Last-Event-ID`, qu'un client SSE envoie en se reconnectant, reprend **après** le seq qu'il nomme et l'emporte sur `from`. `seq` croît strictement dans un run, y compris à travers un `--resume`. Tant qu'un run est silencieux, le flux écrit une trame de maintien toutes les `SseKeepAliveInterval` (un commentaire SSE `: keep-alive`, une ligne vide en NDJSON) : c'est l'écriture qui remarque un client parti, si bien qu'un flux abandonné s'achève au lieu de durer autant que le run. Un client qui se déconnecte achève son flux sans bruit (journal au niveau Debug).

## Idempotence

`idempotency_key` (de 1 à 255 caractères) rend `POST /runs` rejouable sans risque.

- **Ce qui est comparé.** La clé, et un SHA-256 de la requête (la crew telle que le host la nomme, le prompt, les variables triées par nom). Même clé et même hash : l'id du premier run, `200`. Même clé, autre hash : `409`.
- **Concurrence.** Deux requêtes de même clé qui arrivent en même temps démarrent **un** run : la première réserve la clé sous un verrou avant de rien démarrer, la seconde attend ce démarrage et répond avec l'id du run de la première. Une tentative refusée (`429`, `503`, `404`) libère la clé : le client peut la retenter.
- **Où vivent les clés.** En mémoire, pendant `IdempotencyTtl` (24 heures par défaut, comptées depuis le démarrage du run), au plus `IdempotencyMaxKeys` — les plus anciennes terminées partent d'abord. La clé et le hash sont aussi écrits dans l'événement `run.started` du run, dans l'historique.
- **À travers un redémarrage.** Au démarrage de l'API, les clés des runs que l'historique détient sur le dernier `IdempotencyTtl` (les 1000 plus récents) sont relues : un rejeu après redémarrage répond donc encore `200` sur le même run. Cela dure autant que l'historique garde le run (rétention de `Orkeon:RunHistory`) et que le TTL n'est pas écoulé. **Avec l'historique désactivé, un redémarrage oublie toutes les clés**, et un rejeu démarre un nouveau run. Un historique illisible au démarrage coûte les clés d'avant le redémarrage, pas le démarrage ; un avertissement le dit.
- Un run envoyé sans clé n'est jamais dédupliqué.

## Hors de cette API

Reprendre un run suspendu par l'API n'y est pas : la réponse est enregistrée et la CLI le reprend. Le flux des jetons LLM du run non plus : les événements portent la progression des tâches et des outils que publie le bus d'événements.
