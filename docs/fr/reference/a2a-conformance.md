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
| Send Message | `POST /a2a/tasks/send` | 🟡 Partiel | Forme d'ère 0.x (`A2ATaskRequest` : id/skillId/input/inputMode/metadata), pas le modèle `Message`/`Part` v1.0. |
| Send Streaming Message | `POST /a2a/tasks/sendSubscribe` (SSE) | 🟡 Partiel | Working → update final → `[DONE]` ; pas d'événements typés `TaskStatusUpdateEvent`/`TaskArtifactUpdateEvent`. |
| Get Task | `GET /a2a/tasks/{id}` | 🟡 Partiel | **Réel depuis PUB-08** quand la persistance des tâches est activée (`AddOrkeonA2ATaskPersistence()` sur un `IStateStore` de checkpointing) : 200 avec l'état enregistré, 404 pour un id inconnu. Sans l'opt-in : `501` explicite (jamais d'état fabriqué). |
| List Tasks | — | 🔴 Absent | |
| Cancel Task | `DELETE /a2a/tasks/{id}` | 🟡 Partiel | Avec persistance : 404 pour les ids inconnus et l'enregistrement passe à `Cancelled`. **Consultatif seulement** : le travail en cours n'est pas interrompu. Sans persistance : accusé de réception historique. |
| Subscribe to (existing) Task | — | 🔴 Absent | Le streaming n'existe qu'à la soumission. |
| Push Notification Configs (create/get/list/delete) | — | 🔴 Absent | Pas de livraison webhook. |
| Get Extended Agent Card | — | 🔴 Absent | Carte publique unique. |

## Modèle de données

| Concept v1.0 | Orkeon | Statut |
|---|---|---|
| États de tâche (9 : dont `SUBMITTED`, `INPUT_REQUIRED`, `AUTH_REQUIRED`, `REJECTED`) | 5 états (`Pending`, `Working`, `Completed`, `Failed`, `Cancelled`) | 🟡 Les états terminaux se mappent 1-1 ; les états interrompus n'ont pas d'équivalent. |
| `Message` / `Part` (parts texte, fichier, données) | chaîne `input` + indice MIME `inputMode` | 🟡 Text-first ; pas de payloads multi-parts. |
| Artifacts | chaîne `output` | 🟡 Sortie textuelle unique. |
| AgentCard | `GET /.well-known/agent.json` | 🟡 Servie avec name/description/skills ; champs v1.0 (`capabilities`, `securitySchemes`, `agentInterfaces`, `signature`) absents. |
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
| Vérification de l'identité du client | mTLS (fail-closed : `RequireMutualTls` refuse de démarrer sans ancre de confiance ; chaîne CA ou empreintes épinglées) + `AllowedAuthSchemes` (401) | 🟢 Pour le modèle de déploiement mTLS. |
| Déclaration `securitySchemes` dans l'AgentCard | — | 🔴 Les schémas sont imposés mais pas annoncés. |
| Révocation de certificats | Non vérifiée | 🟡 **Décision (PUB-08 T3) : préférer les certificats à courte durée de vie à CRL/OCSP.** Le modèle de confiance mTLS d'A2A vise des CA privées, où les endpoints CRL/OCSP existent rarement et où OCSP ajoute une dépendance de disponibilité ; une durée de vie de 24–72 h borne la fenêtre d'exposition sans nouvelle dépendance runtime, et la rotation s'inscrit dans les options existantes (une nouvelle instance de client recharge le PFX). Le support CRL reste hors périmètre tant qu'un déploiement n'en prouve pas le besoin. |
| Auth des webhooks de push notification | — | 🔴 Pas de webhooks. |

## Ce que cela signifie pour les consommateurs

- **Orkeon ↔ Orkeon** entre processus/hôtes : supporté (c'est pour cela que la
  surface existe), désormais avec un statut de tâche durable via l'opt-in de
  persistance.
- **Orkeon ↔ agents tiers v1.0** : pas encore — attendre (ou contribuer à)
  l'alignement sur le binding HTTP+JSON suivi par la suite de PUB-08.

Chaque écart ci-dessus est un périmètre assumé, pas un oubli : la surface est
marquée `[Experimental]` (`ORKEXP001`) précisément pour pouvoir être remodelée
vers la v1.0 sans dette de breaking change. Voir aussi
[APIs expérimentales](./experimental-apis.md) et
[Limites et contraintes](./limitations.md).
