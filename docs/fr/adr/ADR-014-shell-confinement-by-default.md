> 🇬🇧 [English version](../../adr/ADR-014-shell-confinement-by-default.md)

> **Voir aussi** : [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [Configuration](../reference/configuration.md) · [Limitations connues](../reference/limitations.md) · [Retour à l'index](../INDEX.md)

# ADR-014 — Les commandes shell sont confinées par le système d'exploitation, par défaut

**Statut** : Accepté · **Date** : 2026-10-18
· **Portée** : `src/tools/Orkeon.Tools.Code` (`ShellCommandTool`, `Confinement/`, `Sessions/`), `AgentRunScope` (`Orkeon.Domain`) et ses deux points d'entrée (`AgentExecutionService`, le runtime de scripting), les images, le paquet Debian

## Contexte

`shell_command` vérifiait auprès du VFS les chemins que nomme sa ligne de commande, et rien
d'autre : un script qu'il lançait lisait et écrivait partout où l'utilisateur de l'hôte le pouvait,
montages en lecture seule compris, et atteignait le réseau. Depuis la 1.0.0-rc.4, un opérateur
pouvait choisir bubblewrap sous Linux (`Confinement:Mode = Bubblewrap`) ; personne n'y était tenu,
et macOS comme Windows n'avaient rien. Chaque commande était aussi son propre processus : une vue
confinée aurait été reconstruite à chaque appel, et l'annulation de l'appelant laissait la commande
tourner.

## Décision

1. **Le confinement est actif par défaut.** `Orkeon:Tools:Shell:Confinement:Mode` reçoit `Auto`, le
   nouveau défaut, qui choisit le moteur de la plateforme :
   - **Linux** : bubblewrap (`bwrap`), chaque montage exposé aux agents lié à son chemin virtuel ;
   - **macOS** : Seatbelt (`sandbox-exec` et un profil généré) ;
   - **Windows** : un AppContainer dans un Job Object, les montages lui étant accordés par ACL.
   Dans chaque moteur, le processus voit les montages de l'agent (en lecture seule sans Write), les
   répertoires système en lecture seule, rien d'autre de l'hôte, et pas de réseau sauf
   `AllowNetwork`.
2. **Coupé dans un conteneur.** Un hôte dans un conteneur (`/.dockerenv`, `/run/.containerenv`,
   `DOTNET_RUNNING_IN_CONTAINER=true`) est déjà confiné par lui, et un conteneur non privilégié
   refuse en général les espaces de noms utilisateur dont bubblewrap a besoin : `Auto` y vaut `None`
   et le journalise une fois. Les images fixent aussi `Mode=None` explicitement. Un mode explicite
   n'est jamais remplacé.
3. **Échec franc.** Un moteur qui ne peut pas tourner (absent, refusé par le noyau, un mode d'une
   autre plateforme) fait échouer l'appel avec la raison et la façon de couper le confinement
   (`Mode = None`). Une commande ne se rabat jamais sur une exécution hors confinement.
4. **Une session shell par exécution d'agent.** La première commande d'un agent ouvre un shell
   persistant (`/bin/sh -s`, dans le moteur), lié à l'`AgentRunScope` de l'exécution ; les commandes
   suivantes de la même exécution le réutilisent, si bien que la vue confinée est construite une
   fois et rouverte seulement quand les montages changent. La session et tous les processus
   qu'elle a lancés sont tués quand l'exécution se termine, échoue ou est annulée (la portée se
   ferme), et quand l'hôte meurt (`--die-with-parent` et un espace de noms PID sous bubblewrap, un
   chien de garde hors confinement, le Job Object sous Windows). L'annulation de l'appelant tue la
   commande en cours.
5. **La ligne de commande reste sous liste d'autorisation.** La session est un vrai shell, mais
   l'outil analyse et vérifie toujours chaque commande contre sa liste d'autorisation et ses
   opérateurs interdits avant de l'écrire dans la session.

## Alternatives étudiées

- **WSL2 et bubblewrap sous Windows** : un seul moteur partout, mais WSL2 devient obligatoire sur
  l'hôte, chaque chemin est traduit deux fois et les chaînes d'outils Windows natives restent
  dehors. Écarté.
- **Un jeton restreint sous Windows** : plus simple qu'un AppContainer, mais il ne coupe pas le
  réseau et un jeton restreint lit encore l'essentiel du disque. Écarté au profit de
  l'AppContainer, qui refuse par défaut.
- **Windows Sandbox** : une machine virtuelle complète par session, des secondes au démarrage et
  absente des éditions Famille. Écarté.
- **Pas de confinement sous macOS** : `sandbox-exec` est déprécié mais toujours livré et utilisé
  par les outils de la plateforme ; laisser macOS sans confinement ferait dépendre le défaut de la
  plateforme. Écarté.
- **Une liste d'autorisation sans vrai shell** : ce que faisait l'outil. Chaque commande un
  nouveau processus, aucun état entre les commandes, et une vue confinée reconstruite à chaque
  fois. Écarté au profit d'un shell confiné par exécution, la liste d'autorisation gardée devant.

## Conséquences

- **Pas de réseau par défaut.** Une commande qui télécharge (un gestionnaire de paquets,
  `git fetch`) échoue tant que `Confinement:AllowNetwork = true` n'est pas posé.
- **Les chaînes d'outils hors des répertoires système** (`/opt/node`, `~/.dotnet`, `nvm`) sont
  invisibles tant que leur dossier n'est pas listé dans `Confinement:ReadOnlyPaths`.
- **Chemins** : sous bubblewrap, le processus travaille sur des chemins virtuels ; sous Seatbelt et
  AppContainer, sur des chemins physiques, traduits à l'aller et au retour comme pour une commande
  hors confinement.
- **ACL Windows** : l'AppContainer n'atteint un montage que par une ACE ajoutée pour lui ; les ACE
  sont enregistrées et retirées à la fermeture de la session, et les restes d'un hôte tombé sont
  nettoyés au démarrage suivant.
- **Changement cassant** : un hôte qui comptait sur l'ancien défaut (`None`) reçoit le
  confinement ; la sortie est `Mode = None` (`CHANGELOG.md`, tableau de migration). Le paquet
  Debian dépend de `bubblewrap` ; sous Ubuntu 24.04 et suivantes, le profil AppArmor de
  `/usr/bin/bwrap` lève pour bubblewrap seul la restriction des espaces de noms utilisateur non
  privilégiés.
