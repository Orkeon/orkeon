> 🇬🇧 [English version](../../reference/a2a-conformance.md)

# Protocole A2A — matrice de conformité

Position honnête de l'implémentation A2A d'Orkeon (marquée `[Experimental]`,
diagnostic `ORKEXP001`) face à la **spécification A2A v1.0** (Linux
Foundation / a2aproject). En résumé : l'implémentation appartient à l'**ère
0.x** du protocole — une surface REST compacte plus du streaming SSE — et
couvre la boucle cœur send/stream/status/cancel ; le formalisme de bindings
v1.0 (mappings JSON-RPC, gRPC, HTTP+JSON du schéma proto canonique), les push
notifications et le cycle de vie de tâche enrichi ne sont pas implémentés.
Cette page est la référence de ce qui interopère et de ce qui n'interopère pas.

## Opérations abstraites (spec §core)

| Opération v1.0 | Endpoint Orkeon | Statut | Notes |
|---|---|---|---|
| Send Message | `POST /a2a/tasks/send` | 🟡 Partiel | Forme d'ère 0.x (`A2ATaskRequest` : id/skillId/input/inputMode/metadata), pas le modèle `Message`/`Part` v1.0. Le `skillId` est l'`id` d'une skill de la carte d'agent ; l'agent s'exécute et la réponse porte sa sortie — voir [Exécution des tâches](#exécution-des-tâches). |
| Send Streaming Message | `POST /a2a/tasks/sendSubscribe` (SSE) | 🟡 Partiel | Working → update final → `[DONE]` ; pas d'événements typés `TaskStatusUpdateEvent`/`TaskArtifactUpdateEvent`. |
| Get Task | `GET /a2a/tasks/{id}` | 🟡 Partiel | **Réel depuis PUB-08** quand la persistance des tâches est activée (`AddOrkeonA2ATaskPersistence()` sur un `IStateStore` de checkpointing) : 200 avec l'état enregistré, 404 pour un id inconnu. Sans l'opt-in : `501` explicite (jamais d'état fabriqué). |
| List Tasks | — | 🔴 Absent | |
| Cancel Task | `DELETE /a2a/tasks/{id}` | 🟡 Partiel | Une tâche sur laquelle l'agent travaille encore est **interrompue** : le jeton de son exécution est annulé et la requête qui l'a soumise répond `Cancelled`. Avec persistance, un enregistrement laissé `Working` par un serveur arrêté passe à `Cancelled` ; une tâche terminée répond 409 et garde son état. Un id inconnu (ou, sans persistance, toute tâche qui ne tourne pas) répond 404. |
| Subscribe to (existing) Task | — | 🔴 Absent | Le streaming n'existe qu'à la soumission. |
| Push Notification Configs (create/get/list/delete) | — | 🔴 Absent | Pas de livraison webhook. |
| Get Extended Agent Card | — | 🔴 Absent | Carte publique unique. |

## Modèle de données

| Concept v1.0 | Orkeon | Statut |
|---|---|---|
| États de tâche (9 : dont `SUBMITTED`, `INPUT_REQUIRED`, `AUTH_REQUIRED`, `REJECTED`) | 5 états (`Pending`, `Working`, `Completed`, `Failed`, `Cancelled`) | 🟡 Les états terminaux se mappent 1-1 ; les états interrompus n'ont pas d'équivalent. |
| `Message` / `Part` (parts texte, fichier, données) | chaîne `input` + indice MIME `inputMode` | 🟡 Text-first ; pas de payloads multi-parts. |
| Artifacts | chaîne `output` | 🟡 Sortie textuelle unique. |
| AgentCard | `GET /.well-known/agent.json` | 🟡 Servie avec name/description/skills ; champs v1.0 (`capabilities`, `securitySchemes`, `securityRequirements`, `supportedInterfaces`, `signatures`) absents. |
| Chemin de découverte de l'AgentCard (`/.well-known/agent-card.json`) | `/.well-known/agent.json` | 🔴 Le chemin well-known de la v1.0 n'est pas servi ; un client qui suit la convention obtient un 404 avant toute question de binding. |
| Paramètre de service `A2A-Version` | — | 🔴 Non lu ; aucune négociation de version. |

## Bindings

La v1.0 définit trois bindings canoniques (JSON-RPC 2.0, gRPC, HTTP+JSON)
mappés depuis le schéma proto. Orkeon parle **son propre dialecte REST d'ère
0.x** — aucun des trois bindings canoniques — un client strictement v1.0
n'interopérera donc pas sans adaptateur. L'alignement sur le binding HTTP+JSON
est la première marche naturelle quand cette surface sera promue.

## Sécurité

| Exigence | Orkeon | Statut |
|---|---|---|
| Vérification de l'identité du client | mTLS (fail-closed : `RequireMutualTls` refuse de démarrer sans ancre de confiance ; chaîne CA ou empreintes épinglées ; 403 en cas d'échec) + `AllowedAuthSchemes` (401) : un jeton `Bearer` validé par un `IAuthenticationProvider` (Azure AD, OIDC, ou celui de l'hôte), une `ApiKey` comparée en temps constant aux secrets nommés par `ApiKeySecretNames` ; un schéma déclaré sans validateur refuse de démarrer | 🟢 Les deux identifiants sont validés, pas seulement reconnus. `A2AClient` envoie le sien (`ClientAuthScheme` + `ClientCredentialSecretName`, plus bas). |
| Accès à la carte d'agent | `GET /.well-known/agent.json` | 🟢 Publique par conception : les contrôles de sécurité ne s'appliquent qu'aux endpoints de tâche. |
| Déclaration `securitySchemes` dans l'AgentCard | — | 🔴 Les schémas sont imposés mais pas annoncés. |
| Révocation de certificats | Non vérifiée | 🟡 **Décision (PUB-08 T3) : préférer les certificats à courte durée de vie à CRL/OCSP.** Le modèle de confiance mTLS d'A2A vise des CA privées, où les endpoints CRL/OCSP existent rarement et où OCSP ajoute une dépendance de disponibilité ; une durée de vie de 24–72 h borne la fenêtre d'exposition sans nouvelle dépendance runtime, et la rotation s'inscrit dans les options existantes (une nouvelle instance de client recharge le PFX). Le support CRL reste hors périmètre tant qu'un déploiement n'en prouve pas le besoin. |
| Auth des webhooks de push notification | — | 🔴 Pas de webhooks. |

## Exécution des tâches

Le routeur décide de ce que publie la carte : `GET /.well-known/agent.json` liste
les skills de l'`IA2ATaskRouter` enregistré (`GetSkillsAsync`), de sorte que la clé
qu'un pair lit est la clé que le routeur compare. `A2ATaskRouter`, le routeur que
l'opt-in enregistre, exécute l'agent que la requête désigne. Il liste une skill par
agent disponible (id → `id`, rôle → `name`, objectif → `description`, `text/plain`
en entrée et en sortie), et le `skillId` de la requête doit égaler **exactement**
l'un de ces ids — le rôle n'est pas une clé, et `writer` ne sélectionne jamais
`Ghostwriter`. L'agent
travaille alors sur une tâche ad hoc dont la description est l'`input` de la
requête (ses `metadata` deviennent les variables de la tâche), via
l'`IAgentExecutionService` de l'hôte, résolu dans le scope DI propre à la requête :

- l'agent réussit → `Completed`, `output` est ce qu'il a produit ;
- il échoue ou lève → `Failed`, `error` porte son erreur ;
- le jeton de la requête se déclenche (`DELETE /a2a/tasks/{id}`, ou l'arrêt du
  serveur) → `Cancelled` ;
- aucun agent ne publie cet id, l'entrée est vide, ou l'hôte n'a enregistré aucun
  service d'exécution (`AddOrkeonApplication()` le fait) → `Failed`, en disant lequel.

Rien ne répond `Completed` sans que l'agent ait tourné. Un hôte qui route
autrement enregistre son propre `IA2ATaskRouter` avant d'appeler `AddOrkeonA2A` :
le routeur par défaut n'est enregistré que si aucun ne l'est, et avec lui
l'annuaire d'agents où il les trouve (l'`IAgentRegistrationStore` partagé par tout
le processus, voir [Sous-systèmes opt-in](./opt-in-subsystems.md)) — un hôte doté
de son propre routeur garde son propre `IAgentRepository`. C'est ce que fait
`orkeon-host` : une skill par crew qu'il expose, une tâche étant un run de cette
crew — voir [Hôte de service](../architecture/service-host.md#5-autres-agents-a2a).
Les requêtes sont servies en parallèle : un `DELETE` atteint une tâche sur
laquelle l'agent travaille encore.

## Activation

`orkeon-host` active A2A pour les crews qu'expose `Orkeon:Host:A2A` — `Enabled`,
`Host`, `Port` et `Crews`, sa propre section ; une skill par crew exposée, une
tâche étant un run de cette crew sous ses montages et sa borne de concurrence —
voir [Hôte de service](../architecture/service-host.md#5-autres-agents-a2a).
`orkeon run` et le REPL ne le font pas : un run unique n'a rien à écouter.
Un hôte qui embarque s'y inscrit avec `AddOrkeonA2A(configuration)`
(sections `A2A` et `A2A:Security`) ou `AddOrkeonA2A(configure, configureSecurity)`,
plus `AddOrkeonA2ATaskPersistence()` pour des enregistrements de tâche durables.
Avec `EnableServer`, l'extension enregistre aussi un service hébergé : un hôte
générique démarre le serveur avec lui et l'arrête à l'extinction, sans code
d'amorçage de sa part ; un échec au démarrage (un schéma déclaré sans validateur,
le mTLS sans ancre de confiance) fait échouer le démarrage de l'hôte. Voir
[Sous-systèmes opt-in](./opt-in-subsystems.md).

| Clé `A2A` | Défaut | Effet |
|---|---|---|
| `EnableServer` | `false` | Enregistre `IA2AServer` (un `HttpListener` sur `Host:Port`) et le service hébergé qui le fait tourner. |
| `Host`, `Port` | `http://localhost`, `5002` | Le préfixe d'écoute. |
| `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl` | `Orkeon`, `Orkeon A2A Agent`, `1.0.0`, —, — | La carte d'agent. |
| `TimeoutSeconds` | `30` | Timeout des requêtes du client. |

`orkeon-host` prend son écoute dans `Orkeon:Host:A2A` et refuse `A2A:EnableServer`,
`A2A:Host` et `A2A:Port` au démarrage ; il lit le reste de la section — l'identité
de la carte, et `A2A:Security` — comme ci-dessus.

`A2A:Security` porte `ClientCertificatePath`/`ClientCertificatePassword` (le
certificat propre du client), `TrustedCertificateAuthorities`,
`TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` et
`ApiKeySecretNames`, ainsi que les validateurs bearer `A2A:Security:AzureAD` et
`A2A:Security:Oidc` (enregistrés par `AddOrkeonA2A(configuration)` quand ils sont renseignés).
Côté client, `ClientAuthScheme` (`Bearer` ou `ApiKey`) et
`ClientCredentialSecretName` font envoyer à `A2AClient`
`Authorization: <schéma> <secret>` à chaque appel de tâche, le secret étant lu via
`ISecretProvider` à chaque fois ; un appel dont l'identifiant ne peut être lu
échoue avant tout envoi. La découverte de carte (`IA2AAgentDiscovery`) reste anonyme.

`IA2AClient` et `IA2AAgentDiscovery` servent un hôte C# qui appelle un pair ;
aucun outil d'agent livré n'en appelle un.

## Ce que cela signifie pour les consommateurs

- **Orkeon ↔ Orkeon** entre processus/hôtes : le fil fonctionne de bout en bout —
  carte, envoi (l'agent s'exécute), streaming, statut, annulation (le travail en
  cours s'arrête), mTLS et identifiants bearer/clé d'API des deux côtés — avec un
  statut de tâche durable via l'opt-in de persistance. Un hôte C# l'active pour
  ses agents, `orkeon-host` pour les crews qu'il expose (sans enregistrement de
  tâche : son `GET` répond `501`).
- **Orkeon ↔ agents tiers v1.0** : pas encore — attendre (ou contribuer à)
  l'alignement sur le binding HTTP+JSON suivi par la suite de PUB-08.

Chaque écart ci-dessus est un périmètre assumé, pas un oubli : la surface est
marquée `[Experimental]` (`ORKEXP001`) précisément pour pouvoir être remodelée
vers la v1.0 sans dette de breaking change. Voir aussi
[APIs expérimentales](./experimental-apis.md) et
[Limites et contraintes](./limitations.md).
