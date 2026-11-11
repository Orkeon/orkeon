> 🇬🇧 [English version](../../reference/stability.md)

> **Voir aussi** : [Retour à l'index](../INDEX.md)

# Politique de stabilité et de versionnement

Orkeon suit le [versionnement sémantique 2.0](https://semver.org/lang/fr/). La version vit
dans `src/Directory.Build.props` et nulle part ailleurs. Cette page dit ce que la ligne 1.x
garantit ; la procédure côté contributeur est dans
[CONTRIBUTING](../../../CONTRIBUTING.fr.md#versionnement-et-stabilité-api).

## Ce qu'une version peut changer

| Version | Peut changer | Ne peut pas changer |
|---|---|---|
| **Correctif** (`1.x.y`) | Corrections de bogues, documentation, code interne, performances. | Un membre public hors `[Experimental]` ; le comportement documenté. |
| **Mineure** (`1.x`) | Ajoute des membres publics, des outils, des fournisseurs, des options et des réglages ; déprécie des membres avec `[Obsolete]` ; modifie tout ce qui est hors engagement (ci-dessous). | Retire un membre de la surface figée ou en change la signature. |
| **Majeure** (`2.0`) | Retire ce qui était déprécié ; casse la surface figée. | — |

Tout changement cassant est annoncé dans le `CHANGELOG.md` avec une note de migration.

## La surface figée

L'engagement est consigné dans le dépôt, pas en prose : tout membre public listé dans le
`PublicAPI.Shipped.txt` d'un paquet publié est figé pour toute la ligne 1.x.
`Microsoft.CodeAnalysis.PublicApiAnalyzers` le vérifie à la compilation, et un changement
public non déclaré fait échouer le build (`RS0016` et `RS0017` sont des erreurs).

- **Un retrait ou un changement de signature attend la 2.0.** Modifier ou retirer une ligne
  de `PublicAPI.Shipped.txt` est, par définition, un changement cassant.
- **Les ajouts passent par `PublicAPI.Unshipped.txt`.** Un nouveau membre public y est
  déclaré ; à la publication, les entrées passent dans `PublicAPI.Shipped.txt` et sont dès
  lors figées. `scripts/check-release-readiness.py` refuse une publication tant qu'un
  fichier `Unshipped` contient une entrée ou que la section `[Unreleased]` du CHANGELOG
  n'est pas vide.

## Hors engagement

- **APIs expérimentales** : les types marqués `[Experimental]` peuvent changer ou
  disparaître dans n'importe quelle version, mineures comprises. Les référencer est une
  erreur de compilation portant leur id, à supprimer pour s'inscrire (voir
  [APIs expérimentales](./experimental-apis.md)).

  | ID | Domaine |
  |---|---|
  | `ORKEXP001` | A2A (protocole agent à agent) |
  | `ORKEXP002` | Orchestration autonome |
  | `ORKEXP003` | RAG correctif |
  | `ORKEXP004` | Intégration MCP |
  | `ORKEXP005` | Capacités par modèle |
  | `ORKEXP006` | Sécurité HTTP de l'hôte |
  | `ORKEXP007` | Ports d'historique des exécutions |

- **Types internes** : tout ce qui n'est listé dans aucun `PublicAPI.Shipped.txt`.
- **Le protocole d'événements d'exécution** (`--events jsonl`) est un protocole versionné :
  chaque ligne porte sa version de protocole dans `v` (voir
  [Le bus d'événements d'exécution](../architecture/run-event-bus.md)). Il est versionné
  à part, indépendamment des versions des paquets.

## Dépréciation

Un membre public appelé à disparaître est marqué `[Obsolete]`, avec son remplaçant nommé
dans le message, dans une version mineure, et n'est retiré au plus tôt qu'à la majeure
suivante.

## Versions prises en charge

La dernière mineure 1.x reçoit les correctifs. Les correctifs de sécurité suivent le
tableau de [SECURITY.fr.md](../../../SECURITY.fr.md).
