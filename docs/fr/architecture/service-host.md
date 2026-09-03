> 🇬🇧 [English version](../../architecture/service-host.md)

# Le host de service et la passerelle de chat

**Périmètre** : `orkeon-host` — le daemon qui héberge des crews, et la passerelle qui permet de les atteindre depuis un canal de chat.
**Public** : qui installe et exploite Orkeon sur un serveur.

Jusqu'ici, Orkeon s'exécutait depuis un terminal ou s'embarquait dans un programme. Les deux supposent un humain devant un écran, sur la même machine, le temps d'un processus. Le host de service lève les trois hypothèses ; le canal Discord donne le premier endroit où l'on peut lui parler depuis là où l'on est déjà.

---

## 1. Ce que c'est, et ce que ce n'est pas

**C'est** un processus long-vivant qui héberge une ou plusieurs crews, isole chaque run, borne la concurrence, répond à un canal de chat, et s'arrête sans abandonner le travail en vol.

**Ce n'est pas un ordonnanceur.** rc.2 n'en livre aucun, délibérément. Une crew qui doit tourner chaque matin a toujours besoin de l'artefact que produit `orkeon forge promote --schedule` — tâche Windows, timer systemd ou ligne cron — installé par une personne. Le host en pose la fondation ; il ne prétend pas l'être, et aucune partie de ce document ne doit se lire autrement.

Le même binaire tourne de trois façons : en terminal, en unité systemd, en service Windows. `UseSystemd()` et `UseWindowsService()` sont inertes hors de leur superviseur, donc rien n'est construit différemment. **Un daemon qu'on ne peut pas lancer au premier plan est un daemon qu'on ne peut pas déboguer.**

---

## 2. Le configurer

```json
{
  "Llm": { "Provider": "deepseek", "Model": "deepseek-chat" },

  "Orkeon": {
    "Host": {
      "RunTimeout": "00:30:00",
      "ShutdownGracePeriod": "00:00:20",

      "Crews": [
        {
          "Name": "support",
          "Path": "/srv/orkeon/crews/support",
          "Mounts": [ "/srv/orkeon/out/support:/output:rw" ],
          "Profile": {
            "MaxConcurrentRuns": 4
          }
        },
        {
          "Name": "veille",
          "Path": "/srv/orkeon/crews/veille",
          "Mounts": [ "/srv/orkeon/out/veille:/output:rw" ]
        }
      ],

      "Discord": {
        "Enabled": true,
        "TokenEnvironmentVariable": "ORKEON_DISCORD_TOKEN",
        "AllowedUserIds": ["123456789012345678"],
        "ProgressInterval": "00:00:02",
        "GuildIds": []
      }
    }
  }
}
```

### `Mounts` — un espace de noms de mounts par crew hébergé

Les deux crews ci-dessus adressent tous deux `/output`, sur deux dossiers différents. C'est
l'objet de la clé : les mounts d'une équipe sont son espace de noms à elle, pas une entrée dans
une table commune.

C'est l'hôte qui **accorde** ces dossiers ; le crew ne les déclare pas. `CrewRunner` entre un
`IFileSystemScope` ambiant pour la durée du run, sur un registre composé par
`ScopedMountComposition.ForExecution` : deux crews simultanés ne voient jamais les mounts l'un de
l'autre. Un crew sans `Mounts` garde les mounts de boot, inchangés.

Deux choses à savoir avant de s'en servir. Entrer un scope **remplace** le jeu de mounts au lieu
de fusionner avec lui, donc le registre composé reporte les mounts internes du boot (`/llm-logs`,
`/sandbox`) — sans cela, la journalisation des échanges et les bacs à sable de code tomberaient
pour toute la durée du run, et le contrôle qui les empêche de gagner une seconde adresse joignable
par l'agent tomberait avec eux. Et `IPathValidator` est un second portail, process-global, dont
les racines autorisées sont figées au boot : un dossier accordé hors de la racine du workspace
résout dans l'espace de noms puis se fait refuser là, sauf à élargir
`PathSecurity:AdditionalAllowedDirectories`.

`Path` accepte ce qu'accepte `orkeon run` : un fichier YAML, un dossier de crew multi-fichiers, ou un script `.ork.ts`. Le host le charge par le même chemin de code, donc **une crew hébergée est exactement la crew qu'un terminal lance**. Le dossier de chaque crew est **monté automatiquement dans le VFS, en lecture seule, sous un nom** — `/crews`, puis `/crews-1`, `/crews-2`, … pour chaque dossier supplémentaire — et la crew est chargée par cette orthographe virtuelle (`/crews/support.yaml` pour un fichier, `/crews-1` pour un dossier). Le loader lit par le système de fichiers virtuel comme tout le reste du framework, et un chemin qui n'existerait que sur le disque physique passerait la sonde de démarrage puis échouerait à chaque message. Le montage n'est délibérément **pas** identité : un agent qui appelle `list_mounts`, ou qui lit un message de refus d'accès, ne doit jamais recevoir l'organisation disque de l'opérateur ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). Un `--mount` à vous qui revendique `/crews*` est refusé au démarrage avec le code de sortie 78. Une réserve accompagne la forme script : transpiler du `.ork.ts` demande esbuild sur la machine, et ni l'image de conteneur ni une installation service nue ne l'embarquent — une crew hébergée en daemon est une crew YAML, sauf à installer esbuild soi-même.

La configuration est **validée au démarrage** : un chemin de crew manquant, un timeout à zéro, un canal activé avec une liste d'autorisation vide ou une variable de jeton absente refusent le démarrage avec le code de sortie 78 — avant que le service ne se déclare prêt — plutôt que d'être découverts un run raté à la fois.

### Aucun secret n'est jamais écrit ici

`TokenEnvironmentVariable` porte le **nom** d'une variable d'environnement. Le jeton lui-même ne touche jamais le fichier de configuration, un commit, ni une couche d'image de conteneur — où il resterait aussi longtemps que l'image existe, y compris après que quelqu'un l'a « supprimé » dans une couche ultérieure. C'est la règle que suivent déjà les fournisseurs LLM, et un jeton de bot, qui peut lire tous les messages d'un serveur, n'y fait pas exception.

### Le profil : un seul axe, à dessein

| Axe | Défaut | Pourquoi |
|---|---|---|
| `MaxConcurrentRuns` | `4` | Un daemon qui accepte toutes les requêtes qui arrivent meurt à sa première rafale, et un canal de chat rend les rafales triviales. |

Le profil ne porte délibérément rien d'autre. Des brouillons antérieurs esquissaient des
drapeaux `Interactive`, `Persistent` et `Chat` ; la révision les a trouvés liés à la
configuration et lus par personne — un exploitant pouvait les basculer sans rien changer du
tout. Une surface de configuration qui ne fait rien est pire qu'absente : rc.2 livre le seul
bouton qui fonctionne.

Une requête au-delà du plafond est **refusée avec une réponse**, pas mise en file : « on est occupé, réessayez » est quelque chose qu'un canal relaie à une personne ; une file d'attente invisible ne l'est pas.

---

## 3. L'isolation

Chaque run obtient son propre scope d'injection de dépendances, et l'état par crew d'un run est libéré quand il se termine. À eux deux, ils portent la défense contre le risque que la conception de la passerelle désigne comme le plus sérieux : de l'état qui fuit entre conversations.

Deux mécanismes, énoncés précisément parce qu'une version antérieure de cette section surestimait ce qui les portait. Le **scope** isole les services scoped — le repository de crews avant tout : la crew d'une conversation n'est jamais résoluble depuis le run d'une autre. La **libération** tient les services process-wide honnêtes : chaque message charge une crew fraîche avec un id frais, et le service de mémoire comme le registre de fournisseurs abandonnent leur entrée à la fin du run — sans quoi un daemon en accumule une par conversation, pour toujours. Une mémoire qui survivrait à un run hébergé est impossible par construction en rc.2 — il n'y a pas de drapeau à se tromper.

Chaque run porte aussi son échéance (`RunTimeout`). Un daemon n'a personne pour appuyer sur Ctrl-C : un run sans délai est un daemon bloqué à attendre un modèle qui ne répondra pas.

---

## 4. La passerelle

Un message devient un run dans un ordre fixe : **autoriser, router, accuser réception, travailler.**

**Autoriser d'abord.** Un expéditeur absent de la liste n'atteint jamais une crew, ne coûte jamais un jeton, et n'apparaît jamais dans un journal comme une requête acceptée. Il en est informé, car le silence ressemble à un bot cassé.

> **Une liste d'autorisation vide refuse tout le monde**, et le canal refuse de démarrer plutôt que de ne répondre à personne en silence. Le défaut inverse est la façon dont un bot invité sur un serveur public finit par dépenser le budget d'API de quelqu'un pour des inconnus.

**Router.** rc.2 livre une seule stratégie : **un thread est un run**. C'est la seule correspondance qu'une personne peut prédire sans qu'on la lui explique — ce qui se passe dans ce fil est un travail — et elle donne le parallélisme sans inventer une notion de session que quiconque doive apprendre. Un second message dans un fil qui tourne est refusé avec une explication, plutôt que de lancer un second run dont personne ne saurait distinguer les réponses.

**Accuser réception.** La fenêtre de réponse de toute plateforme de chat se mesure en secondes ; une crew se mesure en minutes. L'accusé de réception part avec l'**admission** : à l'instant où la place du run est réservée — toujours avant tout travail de crew, et porteur du bouton d'arrêt, pour qu'un run soit interruptible dès sa première seconde. Un refus (crew inconnue, saturée) est répondu sans accusé : « je m'y mets » plus un bouton Stop, suivi de « on est occupé », serait une promesse rétractée par sa propre ligne suivante — avec un bouton accroché à rien.

**Travailler**, en rapportant au fil de l'eau. La progression est **throttlée** (`ProgressInterval`, 2 secondes par défaut) : un run émet un événement par pensée d'agent et par appel d'outil, et relayer chacun épuiserait la limite de débit par canal de Discord à l'intérieur d'une seule crew. La dernière mise à jour supprimée est vidée juste avant la réponse finale, pour qu'un run ne se termine pas sur une vue vieille de trois étapes.

### Commandes

`/status` et `/stop` sont des **slash-commands enregistrées** — le client Discord les autocomplète, et la réponse est **éphémère** : un coup d'œil au statut ou un refus regarde celui qui invoque, pas une ligne de plus dans le thread de tout le monde. Elles sont enregistrées à la connexion : globalement quand `GuildIds` est vide (aucune configuration, mais Discord met les commandes globales en cache jusqu'à une heure), ou par serveur nommé (disponibles immédiatement — la boucle de dev). La passerelle ne parse pas le texte des messages pour elles : un `/stop` littéral tapé comme texte est un prompt comme un autre.

| Commande | Effet |
|---|---|
| `/status` | Ce que cette conversation exécute, et depuis quand. |
| `/stop` | Arrête le run de cette conversation. Le **bouton Stop** est la même invocation avec un autre doigt — même contrôle d'allow-list, même réponse. |

Les deux chemins sont gardés par `AllowedUserIds`. Bouton compris : un clic de quelqu'un hors liste est refusé en éphémère au lieu d'arrêter le run.

---

## 5. L'installer

### systemd

[`deploy/systemd/orkeon-host.service`](https://github.com/orkeon/orkeon/blob/main/deploy/systemd/orkeon-host.service).

```bash
sudo cp deploy/systemd/orkeon-host.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now orkeon-host
journalctl -u orkeon-host -f
```

`Type=notify`, parce que le host signale sa disponibilité une fois sa configuration acceptée — chaque chemin de crew sondé, chaque option validée — et non au démarrage du processus. Sinon systemd considérerait un host incapable de lire sa configuration comme « démarré » aussi longtemps qu'il met à sortir.

`Restart=on-failure`, pas `always`, et `RestartPreventExitStatus=78` : une configuration que le host refuse — pas de crew, un chemin inexistant, une liste d'autorisation vide, une variable de jeton absente — sort en 78 (EX_CONFIG) *avant* la disponibilité, et la redémarrer toutes les dix secondes enterrerait le seul message que l'exploitant doit lire. Un crash sort non-zéro et redémarre ; un canal qui meurt emporte le host avec le code 1, pour la même raison — un daemon qui existe pour être joignable ne doit pas survivre à sa propre surdité avec un statut propre.

Il n'y a délibérément **pas de `WatchdogSec`** : l'intégration systemd de .NET envoie `READY=1` et `STOPPING=1` et aucun battement de watchdog — en armer un ferait tuer par systemd un host sain à son premier battement manqué, jamais envoyé.

`TimeoutStopSec` est délibérément plus long que `ShutdownGracePeriod`, pour que les runs en vol aient leur grâce avant que systemd ne perde patience. **Augmenter l'un sans l'autre rend celui qui reste en arrière dépourvu de sens.**

Les secrets vont dans `/etc/orkeon/orkeon-host.env`, lisible du seul utilisateur du service. L'unité, elle, en reste vierge.

### Windows

L'archive **complète** (`orkeon-<version>-win-x64.zip` — pas le zip CLI) porte
le daemon et ses artefacts de déploiement. L'exécutable réel est
`libexec\orkeon-host\orkeon-host.exe` ; `bin\orkeon-host.cmd` est un wrapper
de terminal — ne jamais enregistrer le wrapper auprès du SCM. Le script
d'enregistrement est livré dans le dossier `deploy\windows\` de l'archive :

Extrayez l'archive sous `C:\Program Files\Orkeon`, posez votre configuration
sous `C:\ProgramData\Orkeon` (les miroirs de `/opt/orkeon` et `/etc/orkeon`),
puis lancez le script embarqué — ces chemins sont ses défauts :

```powershell
.\deploy\windows\install-service.ps1 -EnvironmentSecrets @{ ORKEON_DISCORD_TOKEN = '...' }
Start-Service -Name Orkeon
```

Le service tourne sous le compte virtuel `NT SERVICE\Orkeon` — le miroir du
`User=orkeon` de l'unité : aucun mot de passe à gérer, son propre SID, Modify
sur `ProgramData\Orkeon` et lecture seule partout ailleurs. L'enregistrement
passe `--working-dir C:\ProgramData\Orkeon` : les chemins relatifs du fichier
de settings — répertoires de crews compris — s'y résolvent, miroir de
`WorkingDirectory=/var/lib/orkeon`. (Un service Windows naît dans `System32` ;
le drapeau est ce qui l'en fait sortir.)

Les secrets vont dans la valeur `Environment` du service (`REG_MULTI_SZ` sous
sa clé) — `-EnvironmentSecrets` l'écrit — que seul le SCM lit et que seuls les
administrateurs ouvrent : le miroir le plus proche d'`EnvironmentFile`.
Redémarrez le service après un changement, même contrat que systemd.

Le redémarrage reflète la politique systemd d'aussi près que le SCM le permet :
deux redémarrages sur crash, puis l'arrêt. Le SCM ne sait pas filtrer les codes
de sortie — pas d'équivalent de `RestartPreventExitStatus=78` — mais une
configuration refusée se termine par un arrêt ordonné, que la récupération
crash-only ne redémarre jamais, et le refus est écrit dans le **journal
d'événements Application** (source `Orkeon`), le seul endroit qu'un exploitant
de service lit vraiment. `install-service.ps1 -Uninstall` retire le service et
vous laisse `ProgramData\Orkeon`.

### Conteneur

[`deploy/Dockerfile.host`](https://github.com/orkeon/orkeon/blob/main/deploy/Dockerfile.host). Le jeton est passé par nom à l'exécution, jamais gravé dans une couche.

---

## 6. Ce qui est livré, et ce qui ne l'est pas

**Livré** : le host et son cycle de vie, le registre de crews avec isolation par run et plafond de concurrence, les ports de la passerelle, l'autorisation par liste, le routage thread-est-run, le répondeur throttlé, et le canal Discord avec les slash-commands enregistrées `/status` et `/stop` et le bouton d'arrêt — un seul chemin autorisé pour les trois.

**Non livré**, et sous-entendu nulle part : un ordonnanceur, le rechargement à chaud de la configuration, l'hébergement multi-crew dynamique, et tout canal autre que Discord. Les ports sont écrits de sorte que le protocole JSONL du bus d'événements en soit une implémentation légitime — le modèle ne se referme pas sur le chat — mais ce canal-là n'est pas écrit.

**Une chose ne peut pas être vérifiée en CI** : le critère de succès de la spec elle-même — lancer une crew depuis un vrai fil Discord, voir la progression, l'interrompre par bouton, avec le service en daemon systemd. Cela exige un compte Discord et un serveur, donc une action propriétaire. Ce que la CI tient, c'est tout ce qui borde la socket : la traduction des messages, les deux limites de la plateforme, l'autorisation, le routage, le throttling et l'isolation.

---

## 7. Voir aussi

- [Le bus d'événements du run](run-event-bus.md) — le protocole qu'un processus observateur lit, et la forme sur laquelle les ports de la passerelle ont été écrits.
- [EventHub et cycle de vie des crews](event-hub-and-crew-lifecycle.md) — le messaging inter-crews et son ACL.
- [Matrice de publication](../reference/publication-matrix.md) — où `orkeon-host` est livré.
