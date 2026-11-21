> 🇬🇧 [English version](../../adr/ADR-019-tool-bridge.md)

> **Voir aussi** : [ADR-017](./ADR-017-transcript-display-model.md) · [ADR-018](./ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md) · [Retour à l'index](../INDEX.md)

# ADR-019 — Un pont d'outils : certains outils d'une exécution tournent sur une autre machine, par un protocole propre

**Statut** : Accepté · **Date** : 2026-11-21
· **Périmètre** : `Orkeon.Domain` (`Attributes/ToolContractAttribute.cs`, `Tools/ToolContracts.cs`), `Orkeon.Hosting` (`ToolBridge/`), `Orkeon.Host` (`ToolBridgeConnectionService.cs`), `Orkeon.Scripting` (`ScriptToolCatalogue.cs`). Issue #445.

## Contexte

Un Orkeon qui tourne dans un conteneur n'a pas accès à ce qui vit sur la machine de l'utilisateur : le navigateur où
l'utilisateur est connecté, un client de messagerie de bureau, un périphérique local. Les outils qui pilotent ces
choses doivent tourner là où elles sont, tandis que l'exécution, ses appels au modèle, son pipeline et son espace de
travail restent dans le conteneur. MCP pourrait porter les appels, mais un serveur MCP est un fournisseur d'outils que
l'appelant ne connaît pas d'avance : ses outils arrivent avec leur propre schéma et sans classe d'accès, et le pipeline
de l'appelant ne peut pas les juger comme il juge un outil intégré. Les outils à déplacer sont des outils intégrés, que
les deux côtés connaissent.

## Décision

1. **Un protocole propre, JSON sur HTTP, pas MCP.** La version 1 se tient sous `/orkeon-bridge/v1/` : `GET tools` (les
   outils servis, chacun avec une empreinte de contrat), `POST call` (un appel : un identifiant, l'outil, ses
   arguments, son contexte ; la réponse brute de l'outil en retour), `POST cancel` (l'annulation d'un appel en
   cours), `POST end-run` (la fin d'une exécution de l'appelant) et `POST heartbeat` (un appelant qui a des exécutions
   en cours est toujours là). Chaque requête porte le jeton en `Authorization: Bearer`. Une version ultérieure ajoute
   des champs et des points d'accès à côté de ceux-ci ; un champ qu'un côté ne connaît pas est ignoré.
2. **Les deux côtés sélectionnent.** La section de l'appelant `Orkeon:Tools:Bridge` (`Address`, `TokenEnvVar`, `Allow`,
   `Deny`) et celle du serveur `Orkeon:Bridge` (`Prefix`, `TokenEnvVar`, `Allow`, `Deny`, `MaxRequestBytes`) appliquent
   une seule règle : sans `Allow` rien n'est sélectionné, le `*` seul sélectionne tous les outils, `Deny` l'emporte,
   seuls des noms et le `*` seul sont acceptés, et un nom qui n'est pas un outil du registre de ce côté refuse le
   démarrage. Un outil est ponté quand les deux côtés le sélectionnent. Un outil de délégation entre collègues n'est
   jamais sélectionné : il représente l'équipe de l'appelant.
3. **L'espace de travail reste chez lui.** Un outil qui agit sur l'espace de travail (fichiers, shell, index du code)
   déclare `[ToolContract(..., ActsOnWorkspace = true)]`, lu à travers les enveloppes par
   `ToolContracts.ActsOnWorkspace`. Le `*` ne sélectionne jamais un tel outil, d'un côté comme de l'autre ; le nommer le
   ponte, et chaque côté journalise un avertissement qui le nomme.
4. **Pas de démarrage sans le pont.** L'appelant refuse de démarrer quand le pont est injoignable, quand il refuse le
   jeton, quand un outil que nomme `Allow` n'est pas servi, ou quand une empreinte de contrat diffère. Avec `*`,
   l'appelant ponte ce qu'il a sélectionné et que le pont sert. L'étape est partagée par `orkeon` (run, validate,
   scripts, eval) et `orkeon-host` (un service hébergé juste après les serveurs MCP, code de sortie 78 sur un refus).
5. **L'empreinte de contrat** est le SHA-256 du schéma d'entrée, canonique et sans ses descriptions (de la prose, qui
   peut nommer ce que détient un hôte), et de la table d'accès : la classe sans argument et la classe de chaque valeur
   de chaque paramètre énuméré de premier niveau. `ToolContract.Version` est facultatif et la plupart des outils n'en
   fixent aucune.
6. **L'appelant continue de juger.** Le mandataire enveloppe l'instance de l'appelant : nom, description, schéma,
   `Access`, `AccessFor` et `UsesFileSystem` restent locaux, si bien que la barrière, le gardien, la troncature et le
   nettoyeur de l'appelant jugent l'appel et son résultat. Seul l'appel est distant. Le pont exécute l'outil tel quel et
   consigne l'appel dans son propre audit sous l'appelant `bridge`.
7. **Un pont perdu est un appel échoué.** Une fois démarré, un appel auquel le pont ne peut répondre échoue avec la
   raison ; l'appel suivant réessaie. Un outil ponté ne tourne jamais localement. Annuler un appel interrompt sa requête
   et l'annule sur le pont.
8. **Le serveur est gardé.** Il écoute en boucle locale par défaut ; tout autre hôte exige https. Le jeton vient de la
   variable d'environnement que nomme `TokenEnvVar`, jamais d'un fichier, et se compare en temps constant. Une requête
   qui porte un en-tête `Origin` est refusée, comme un corps au-delà de `MaxRequestBytes`.
9. **Une exécution de l'appelant est une exécution sur le pont.** Un appel porte l'identifiant de l'appelant et celui
   de son exécution d'agent ; le pont garde un `AgentRunScope` par exécution de l'appelant et y exécute chaque appel de
   cette exécution, si bien qu'« un navigateur par exécution » signifie ce qu'il signifie en local. L'appelant termine
   cette portée par `POST end-run` quand sa propre portée se termine, et bat tous les tiers de
   `Orkeon:Bridge:HeartbeatWindow` (30 s) tant qu'il a des exécutions en cours ; un appelant silencieux au-delà de la
   fenêtre perd ses exécutions, et leurs navigateurs, aussitôt. Un appel hors de toute exécution ne porte pas
   d'identifiant d'exécution.
10. **Un chemin reste celui de l'appelant.** Un appel d'un outil qui utilise le système de fichiers (et n'agit pas sur
    l'espace de travail) porte les montages de l'appelant visibles des agents et les fichiers que nomment ses
    arguments texte. Le pont les présente dans un dossier temporaire aux chemins virtuels de l'appelant le temps de
    l'appel (un dossier que son validateur de chemins autorise, comme il autorise celui du bac à sable), renvoie ce
    que l'outil y a écrit, puis le supprime ; l'appelant écrit ces fichiers par son propre système de fichiers, sous
    les droits de son exécution. `MaxFileBytes` (16 Mio) borne chaque sens.
11. **Le pont est une commande.** `orkeon bridge serve` construit l'hôte que construit `orkeon mcp serve` et sert
    jusqu'à Ctrl+C ou SIGTERM ; aucun réglage ne le démarre, et `orkeon-host` n'est qu'un appelant.

## Conséquences

- Une équipe dans un conteneur peut piloter le navigateur ou le client de messagerie de l'utilisateur avec le pipeline
  qu'elle appliquerait à un outil local.
- Les deux machines doivent exécuter la même version de chaque outil ponté ; une divergence refuse le démarrage plutôt
  que de laisser l'appelant juger un appel selon un schéma que le pont n'applique pas.
- Les réglages d'un outil ponté sont ceux du pont : l'appelant signale une section qui lui est propre
  (`Orkeon:Tools:<Outil>`) et qui ne s'applique plus.
- Un résultat que renvoie le pont voyage en JSON : un résultat typé atteint le pipeline de l'appelant sous son rendu JSON
  plutôt que son `ToString()`.
- Une connexion perdue en cours d'exécution termine l'exécution sur le pont dans la fenêtre, sans délai de grâce : un
  appelant qui revient ouvre de nouvelles portées, et les pages de son navigateur ont disparu.
- Comment mettre les deux côtés en place : le guide [Orkeon dans un conteneur, le navigateur sur le bureau](../guides/tool-bridge.md).
