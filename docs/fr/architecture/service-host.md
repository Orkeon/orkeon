> 🇬🇧 [English version](../../architecture/service-host.md)

# Le host de service et la passerelle de chat

**Périmètre** : `orkeon-host` — le daemon qui héberge des crews, la passerelle qui permet de les atteindre depuis un canal de chat, et le serveur A2A qui permet à d'autres agents de les lancer.
**Public** : qui installe et exploite Orkeon sur un serveur.

Jusqu'ici, Orkeon s'exécutait depuis un terminal ou s'embarquait dans un programme. Les deux supposent un humain devant un écran, sur la même machine, le temps d'un processus. Le host de service lève les trois hypothèses ; le canal Discord donne le premier endroit où l'on peut lui parler depuis là où l'on est déjà.

---

## 1. Ce que c'est, et ce que ce n'est pas

**C'est** un processus long-vivant qui héberge une ou plusieurs crews, isole chaque run, borne la concurrence, répond à un canal de chat et — quand la configuration expose des crews — à des pairs A2A, et s'arrête sans abandonner le travail en vol.

**Ce n'est pas un ordonnanceur.** Orkeon n'en livre aucun, délibérément. Une crew qui doit tourner chaque matin est lancée par le système, à partir de l'artefact que produit `orkeon forge promote --schedule` — tâche Windows, timer systemd ou ligne cron —, que `orkeon forge schedule` installe (Orkeon Studio demande d'abord son accord à l'utilisateur) et que `orkeon forge unschedule` retire. Le host en pose la fondation ; il ne prétend pas l'être, et aucune partie de ce document ne doit se lire autrement.

Le même binaire tourne de trois façons : en terminal, en unité systemd, en service Windows. `UseSystemd()` et `UseWindowsService()` sont inertes hors de leur superviseur, donc rien n'est construit différemment. **Un daemon qu'on ne peut pas lancer au premier plan est un daemon qu'on ne peut pas déboguer.**

---

## 2. Le configurer

```json
{
  "Llm": { "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-chat" },

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
          "Description": "Weekly technology watch: what changed, with sources.",
          "Mounts": [ "/srv/orkeon/out/veille:/output:rw" ]
        }
      ],

      "A2A": {
        "Enabled": true,
        "Port": 5002,
        "Crews": [ "veille" ]
      },

      "Discord": {
        "Enabled": true,
        "TokenEnvironmentVariable": "ORKEON_DISCORD_TOKEN",
        "AllowedUserIds": ["123456789012345678"],
        "ProgressInterval": "00:00:02",
        "GuildIds": [],
        "DefaultCrew": "support",
        "Routes": { "234567890123456789": "veille" }
      }
    }
  }
}
```

La clé du modèle n'est pas dans le fichier non plus : elle vient de `ORKEON_Llm__ApiKey`, posée dans l'environnement du service (voir *L'installer*), ou de la variable que le fichier nomme à la place (`Llm:ApiKeyEnvVar`, et `Llm:Profiles:<nom>:ApiKeyEnvVar` par profil — voir la [configuration](../reference/configuration.md#la-clé-dapi-apikey-apikeyenvvar)), posée dans ce même environnement. Un hôte lit les variables du compte sous lequel il tourne — sous Windows sa portée Utilisateur aussi, jamais recopiée dans le processus : une clé qu'Orkeon Studio a mémorisée pour vous atteint un `orkeon-host` que vous lancez vous-même, pas un hôte qui tourne sous un compte de service. Toute clé se surcharge de la même façon — le préfixe `ORKEON_`, `__` pour `:` — si bien que `ORKEON_Orkeon__Host__RunTimeout=00:10:00` raccourcit l'échéance sans toucher au fichier.

**Quels fournisseurs les crews hébergées peuvent utiliser.** Un hôte peut offrir plusieurs fournisseurs LLM sous forme de profils nommés (`Llm:Profiles:<nom>`, voir [Configuration](../reference/configuration.md#profils-nommés-llmprofiles)), et une crew en choisit un par agent, par son nom. Le démon exécute des crews qu'il ne contrôle pas : c'est donc lui qui décide quels noms répondent. `Orkeon:Host:LlmProfiles` est une **liste blanche**. Absente, tous les profils que la configuration définit sont offerts ; présente, seuls ceux qu'elle liste le sont — une crew qui en nomme un autre échoue au chargement, et le fil qui l'a lancée dit pourquoi. Le profil par défaut (la section `Llm`) est toujours offert : `"LlmProfiles": ["default"]` garde toutes les crews hébergées dessus. La liste vaut pour tout rôle auquel une crew donne un profil — ses agents, ses tâches, son manager hiérarchique (via le bloc `llm:` de l'agent manager) — et pour le `Orkeon:Rag:LlmProfile` du sous-système RAG aussi. Une entrée qui nomme un profil que `Llm:Profiles` ne définit pas, ou un profil RAG que la liste écarte, refuse le démarrage. Les lignes de démarrage suivent la liste : l'hôte nomme les profils qu'il offre (`LLM profiles offered to crews besides the default: …`), chacun avec l'origine de sa clé, et ceux qu'il cache sur une ligne à part (`LLM profiles hidden from crews by the host's allow-list: …`) — la liste se voit à l'œuvre —, et il signale une référence de clé qui ne résout rien, dans son journal et sur stderr, pour le défaut et les profils offerts seulement : une clé absente sur un profil qu'aucune crew ne peut nommer n'est pas une raison d'avertir. Ces lignes sont de niveau Information (voir le niveau du journal plus bas) ; sans liste, chaque profil est offert et nommé.

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash",
    "Profiles": { "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5" } }
  },
  "Orkeon": { "Host": { "LlmProfiles": [ "default" ] } }
}
```

### La ligne de commande

```
orkeon-host [--settings <file>] [--working-dir <dir>] [--mount <physical>:<virtual>:<ro|rw|rwnd>]... [--allow-external-mounts]
```

| Option | Effet |
|---|---|
| `-s`, `--settings <fichier>` | Le fichier de configuration. Par défaut `./appsettings.json`, résolu contre le dossier de travail ; un fichier nommé ici le remplace — les deux ne sont jamais posés l'un sur l'autre — et un fichier nommé qui n'existe pas refuse le démarrage. |
| `--working-dir <dossier>` | S'y déplace avant toute lecture, pour que les chemins relatifs des settings et des crews s'y résolvent. |
| `-m`, `--mount <spec>` | Un montage VFS supplémentaire, répétable. Les dossiers des crews sont montés sans lui (plus bas). |
| `--allow-external-mounts` | Autorise les montages hors du dossier de travail. Implicite dès qu'une crew est configurée, puisque chaque dossier de crew est lui-même monté depuis l'endroit où il se trouve. |
| `-h`, `--help` / `--version` | Affichent puis sortent avec `0`. |

Il n'y a pas de sous-commande. Les codes de sortie sont le contrat avec le superviseur : `0` pour un arrêt propre, `78` (EX_CONFIG) pour une configuration refusée au démarrage, `1` quand le canal de chat est mort.

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

La configuration est **validée au démarrage** : aucune crew sous `Orkeon:Host:Crews`, une crew sans `Name` ou sans `Path`, deux crews de même nom (sans tenir compte de la casse), un chemin de crew qui n'existe pas, un `MaxConcurrentRuns` inférieur à 1, un `RunTimeout` nul ou négatif, un `ShutdownGracePeriod` négatif, une entrée de `LlmProfiles` qui nomme un profil que `Llm:Profiles` ne définit pas, et — pour un canal activé — une liste d'autorisation vide, une variable de jeton absente, un `ProgressInterval` nul ou négatif, une entrée de `GuildIds` ou une clé de `Routes` qui n'est pas un nombre, ou une entrée de `Routes` ou un `DefaultCrew` qui nomme une crew que l'hôte ne déclare pas ; pour un serveur A2A activé, aucune crew exposée, une crew exposée que l'hôte ne déclare pas, un `Host` ou un `Port` mal formé, ou une écoute au-delà de la boucle locale sans authentification, et — quel que soit l'interrupteur A2A — une clé `A2A:EnableServer`, `A2A:Host` ou `A2A:Port`, que le démon ne lit pas (voir [Autres agents (A2A)](#5-autres-agents-a2a)), et une écoute que le système refuse — une URL que HTTP.sys n'a réservée pour personne, un port qu'un autre processus tient (voir [Windows](#windows)) ; et, avant tous ceux-là, un réglage que refuse l'hôte des runners — une clé qui n'est plus un réglage (`RaggableTree:Exclude`), un profil LLM qu'il ne sait pas construire, une valeur que le lieur de configuration ne convertit pas (`"RunTimeout": "abc"`), un fichier de réglages qu'il ne peut pas lire, une adresse qui n'en est pas une (`RaggableTree:Embedding:BaseUrl`, `Telemetry:OtlpEndpoint`), refusent tous le démarrage avec le code de sortie 78 — avant que le service ne se déclare prêt — plutôt que d'être découverts un run raté à la fois.

Toute la séquence qui précède le démarrage passe sous cette barrière : la configuration d'amorçage du démon, ses propres sections — `Orkeon:Host` avec son `A2A`, `Orkeon:Host:Discord`, et la section `A2A` —, **lues une fois, au démarrage**, et l'hôte des runners lui-même. Chaque refus tient en une ligne qui nomme la clé — ou le fichier, avec la ligne et la position de ce que son JSON a de faux — sur stderr et, sous le SCM de Windows, dans le journal d'événements Application. Une valeur que le lieur ne convertissait pas ne se révélait que plus tard, en plantage quand l'hôte construisait ses services ; une faute de frappe dans les réglages faisait planter le démon d'emblée, et systemd le relançait toutes les dix secondes.

Les crews sont lues **une seule fois**, au démarrage : ni balayage de dossier, ni rechargement. Ajouter une crew, c'est modifier le fichier et redémarrer.

### Aucun secret n'est jamais écrit ici

`TokenEnvironmentVariable` porte le **nom** d'une variable d'environnement. Le jeton lui-même ne touche jamais le fichier de configuration, un commit, ni une couche d'image de conteneur — où il resterait aussi longtemps que l'image existe, y compris après que quelqu'un l'a « supprimé » dans une couche ultérieure. C'est la règle que suivent déjà les fournisseurs LLM, et un jeton de bot, qui peut lire tous les messages d'un serveur, n'y fait pas exception.

### Le profil : un seul axe, à dessein

| Axe | Défaut | Pourquoi |
|---|---|---|
| `MaxConcurrentRuns` | `4` | Un daemon qui accepte toutes les requêtes qui arrivent meurt à sa première rafale, et un canal de chat rend les rafales triviales. |

Le profil ne porte délibérément rien d'autre. Des brouillons antérieurs esquissaient des
drapeaux `Interactive`, `Persistent` et `Chat` ; la révision les a trouvés liés à la
configuration et lus par personne — un exploitant pouvait les basculer sans rien changer du
tout. Une surface de configuration qui ne fait rien est pire qu'absente : le host livre le seul
bouton qui fonctionne.

Une requête au-delà du plafond est **refusée avec une réponse**, pas mise en file : « on est occupé, réessayez » est quelque chose qu'un canal relaie à une personne ; une file d'attente invisible ne l'est pas. Le plafond est celui de la crew, quel que soit le chemin par lequel les runs arrivent : conversations de chat et tâches A2A le partagent.

---

## 3. L'isolation

Chaque run obtient son propre scope d'injection de dépendances, et l'état par crew d'un run est libéré quand il se termine. À eux deux, ils portent la défense contre le risque que la conception de la passerelle désigne comme le plus sérieux : de l'état qui fuit entre conversations.

Deux mécanismes, énoncés précisément parce qu'une version antérieure de cette section surestimait ce qui les portait. Le **scope** isole les services scoped — le repository de crews avant tout : la crew d'une conversation n'est jamais résoluble depuis le run d'une autre. La **libération** tient les services process-wide honnêtes : chaque message charge une crew fraîche avec un id frais, et le service de mémoire comme le registre de fournisseurs abandonnent leur entrée à la fin du run — sans quoi un daemon en accumule une par conversation, pour toujours. Une mémoire qui survivrait à un run hébergé est impossible par construction — il n'y a pas de drapeau à se tromper.

Chaque run porte aussi son échéance (`RunTimeout`). Un daemon n'a personne pour appuyer sur Ctrl-C : un run sans délai est un daemon bloqué à attendre un modèle qui ne répondra pas.

---

## 4. La passerelle

Un message devient un run dans un ordre fixe : **autoriser, router, accuser réception, travailler.**

**Autoriser d'abord.** Un expéditeur absent de la liste n'atteint jamais une crew, ne coûte jamais un jeton, et n'apparaît jamais dans un journal comme une requête acceptée. Il en est informé, car le silence ressemble à un bot cassé.

> **Une liste d'autorisation vide refuse tout le monde**, et le canal refuse de démarrer plutôt que de ne répondre à personne en silence. Le défaut inverse est la façon dont un bot invité sur un serveur public finit par dépenser le budget d'API de quelqu'un pour des inconnus.

**Router.** Le host livre une seule stratégie : **un thread est un run**, et **le salon choisit la crew** (GAP-11). Un fil ouvert dans un salon Discord listé sous `Discord:Routes` (identifiant de salon → nom de crew) démarre cette crew ; un fil ouvert ailleurs démarre `Discord:DefaultCrew` — la première crew de `Orkeon:Host:Crews` s'il est absent. Dans l'exemple ci-dessus, les fils du salon `234567890123456789` atteignent `veille`, et ceux de tout autre salon `support`. Une crew hébergée qu'aucun salon n'atteint est nommée par un avertissement au démarrage. Les identifiants de salon sont ceux qu'affiche Discord en mode développeur (clic droit sur le salon → Copier l'identifiant du salon) ; un fil d'un salon forum suit la route du forum. C'est la correspondance qu'une personne peut prédire sans qu'on la lui explique — `#facturation` répond facturation, et ce qui se passe dans ce fil est un travail — et elle donne le parallélisme sans inventer une notion de session que quiconque doive apprendre. Un second message dans un fil qui tourne est refusé avec une explication, plutôt que de lancer un second run dont personne ne saurait distinguer les réponses.

**Accuser réception.** La fenêtre de réponse de toute plateforme de chat se mesure en secondes ; une crew se mesure en minutes. L'accusé de réception part avec l'**admission** : à l'instant où la place du run est réservée — toujours avant tout travail de crew, et porteur du bouton d'arrêt, pour qu'un run soit interruptible dès sa première seconde. Un refus (crew inconnue, saturée) est répondu sans accusé : « je m'y mets » plus un bouton Stop, suivi de « on est occupé », serait une promesse rétractée par sa propre ligne suivante — avec un bouton accroché à rien.

**Travailler**, en rapportant au fil de l'eau. La réponse finale est postée dans le fil, coupée aux 2 000 caractères de Discord (avec la marque `…(truncated)`) ; une réponse vide s'affiche `(no output)`. La progression est **throttlée** (`ProgressInterval`, 2 secondes par défaut) : un run émet un événement par pensée d'agent et par appel d'outil, et relayer chacun épuiserait la limite de débit par canal de Discord à l'intérieur d'une seule crew. La dernière mise à jour supprimée est vidée juste avant la réponse finale, pour qu'un run ne se termine pas sur une vue vieille de plusieurs étapes.

### Ce que le canal Discord écoute

Les messages **à l'intérieur d'un fil**, écrits par des personnes : un message posté dans un canal ordinaire est ignoré, tout comme chaque message d'un bot. Le client se connecte avec les intents `Guilds`, `GuildMessages` et `MessageContent` — `MessageContent` est un intent privilégié, à activer pour le bot dans le portail développeur de Discord, faute de quoi chaque message arrive vide. `AllowedUserIds` est le seul contrôle d'accès : `GuildIds` choisit où les slash-commands sont enregistrées, il ne restreint pas qui peut parler au bot.

### Commandes

`/status` et `/stop` sont des **slash-commands enregistrées** — le client Discord les autocomplète, et la réponse est **éphémère** : un coup d'œil au statut ou un refus regarde celui qui invoque, pas une ligne de plus dans le thread de tout le monde. Elles sont enregistrées à la connexion : globalement quand `GuildIds` est vide (aucune configuration, mais Discord met les commandes globales en cache jusqu'à une heure), ou par serveur nommé (disponibles immédiatement — la boucle de dev). La passerelle ne parse pas le texte des messages pour elles : un `/stop` littéral tapé comme texte est un prompt comme un autre.

| Commande | Effet |
|---|---|
| `/status` | Ce que cette conversation exécute, et depuis quand. |
| `/stop` | Arrête le run de cette conversation. Le **bouton Stop** est la même invocation avec un autre doigt — même contrôle d'allow-list, même réponse. |

Les deux chemins sont gardés par `AllowedUserIds`. Bouton compris : un clic de quelqu'un hors liste est refusé en éphémère au lieu d'arrêter le run.

---

## 5. Autres agents (A2A)

Le canal de chat est une entrée ; [A2A](../reference/a2a-conformance.md) est l'autre. Activé, l'hôte sert une carte d'agent et reçoit des tâches d'autres agents — un autre processus Orkeon, ou tout client du même dialecte REST — et **chaque crew qu'il expose est une compétence**. Une tâche est un **run de cette crew**, exactement ce qu'est un message de chat : l'`input` de la tâche est le besoin du run, et il passe par le même runner — sous les montages de la crew, dans son `MaxConcurrentRuns`, sous `RunTimeout`, journalisé avec son origine (`a2a:<id de tâche>`), drainé à l'arrêt.

| Clé `Orkeon:Host:A2A` | Défaut | Effet |
|---|---|---|
| `Enabled` | `false` | Sert les crews exposées en A2A. Éteint, l'hôte n'écoute sur aucun port. |
| `Host`, `Port` | `http://localhost`, `5002` | L'écoute. `http://+` écoute sur toutes les interfaces. |
| `Crews` | aucune | Les crews que d'autres agents peuvent lancer, par nom. Exposer est un choix par crew, comme une route de salon : une crew laissée de côté est invisible — absente de la carte, et une tâche qui la nomme échoue comme toute compétence inconnue. |

**Ce qu'un pair voit.** `GET /.well-known/agent.json` liste une compétence par crew exposée : son `id` et son `name` sont le `Name` de la crew tel que `Orkeon:Host:Crews` le déclare, sa `description` la clé `Description` de la crew quand la configuration en donne une. La carte vient de la configuration, pas de crews chargées : le démon charge une crew neuve à chaque run et n'en garde aucune entre deux, il n'y a donc pas d'annuaire d'agents à lire — et il n'en tient aucun. L'identité de la carte se lit dans `A2A`, comme pour tout serveur A2A Orkeon (`AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`).

**Ce que fait une tâche.** `POST /a2a/tasks/send` (ou `sendSubscribe`) dont le `skillId` est un id publié — exactement, casse comprise — lance cette crew sur `input` ; les `metadata` de la requête deviennent les variables du run. La réponse :

- le run se termine → `Completed`, `output` est la réponse de la crew ;
- le run échoue → `Failed`, `error` est la phrase qu'un fil de chat reçoit — l'id du run à chercher dans le journal de l'hôte, jamais le détail (chemins, endpoints) que le journal garde ;
- la crew est à son `MaxConcurrentRuns` — conversations de chat et tâches A2A comptées ensemble → `Failed`, en le disant ; une tâche n'est jamais mise en file ;
- `DELETE /a2a/tasks/{id}` arrête le run, et la tâche répond `Cancelled` ; un run au-delà de `RunTimeout` répond aussi `Cancelled`, en disant qu'il a expiré ;
- l'hôte s'arrête → `Failed`, `error` disant « The host is stopping: this run was not started. Send it again once the host is back. » — rien n'a été chargé, aucun modèle appelé ;
- un `skillId` que la carte ne publie pas → `Failed`, en nommant les compétences publiées ; un `input` vide → `Failed` aussi.

**Suivre une tâche.** `sendSubscribe` diffuse le run en événements server-sent : `Working`, puis une mise à jour `Working` par ligne que lit un fil de chat — `Running '<crew>'…`, puis `✔ <rôle> — step N done` à chaque tâche terminée —, la ligne dans son `message` (`partialOutput` reste la sortie), puis l'état final et `[DONE]`. Rien ne suit l'état final ; un pair qui s'en va en cours de run n'en lit plus rien, et le run va jusqu'à son terme.

`GET /a2a/tasks/{id}` répond `501` : le démon ne garde aucun enregistrement de tâche.

**Sécurité.** La section `A2A:Security` du même fichier s'applique telle qu'écrite — `ApiKey` (les clés acceptées nommées par `ApiKeySecretNames`, lues dans `ORKEON_<NOM>`), `Bearer` (Azure AD ou OIDC), mTLS — voir [Sécurité](security.md#tls-mutuel-a2a). La carte reste publique ; les points de tâche exigent l'identifiant. Le démarrage est refusé avec le code de sortie 78 quand la section n'expose aucune crew, ou une crew que `Orkeon:Host:Crews` ne déclare pas ; quand `Host` n'est pas un nom d'hôte `http://` ou `https://` ou que `Port` sort de 1–65535 ; et quand l'hôte écoute au-delà de la boucle locale (tout sauf `localhost`, une adresse `127.x.x.x` ou `[::1]`) alors que `A2A:Security` ne déclare ni schéma d'authentification ni mTLS. `A2A:EnableServer`, `A2A:Host` et `A2A:Port` — les interrupteurs des hôtes C# — sont refusés aussi, en nommant leur remplaçant sous `Orkeon:Host:A2A` : le démon ne lit que la sienne.

```json
{
  "A2A": {
    "Security": { "AllowedAuthSchemes": [ "ApiKey" ], "ApiKeySecretNames": [ "A2A_PEER_KEY" ] }
  },
  "Orkeon": { "Host": { "A2A": { "Enabled": true, "Host": "http://+", "Port": 5002, "Crews": [ "veille" ] } } }
}
```

Un pair envoie alors `Authorization: ApiKey <clé>`, la clé étant la valeur de `ORKEON_A2A_PEER_KEY` dans l'environnement du service.

**Ordre.** Le serveur A2A démarre après la connexion des serveurs MCP — une tâche charge une crew, dont les outils doivent être là — et avant le canal de chat, il s'arrête donc après le drainage : un run en vol pendant le délai de grâce livre encore sa réponse au pair qui l'a demandé, tandis qu'une tâche arrivée pendant ce délai est refusée — l'hôte s'arrête — et ne lance rien.

---

## 6. L'installer

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

`TimeoutStopSec` est délibérément plus long que `ShutdownGracePeriod`, pour que les runs en vol aient leur grâce avant que systemd ne perde patience. **Augmenter l'un sans l'autre rend celui qui reste en arrière dépourvu de sens.** À l'arrêt, le host ferme d'abord l'admission : **dès cet instant, plus aucun run ne démarre** — un message de chat ou une tâche A2A arrivé pendant la grâce reçoit « The host is stopping: this run was not started. Send it again once the host is back. », sans accusé de réception ni bouton Stop, et un pair A2A le lit comme `Failed` ; rien n'est chargé, aucun modèle n'est appelé. Il attend ensuite jusqu'à `ShutdownGracePeriod` les runs en vol — le canal et le serveur A2A restent debout pendant ce temps, pour livrer leurs réponses, et `/status` et `/stop` répondent toujours —, puis les arrête tous et leur laisse cinq secondes de plus pour se terminer ; le budget d'arrêt du host générique est fixé à la période de grâce plus dix secondes pour couvrir les deux.

Le journal est discret par défaut : l'hôte des runners journalise au niveau **Warning**, si bien que les lignes Information — chaque crew hébergée au démarrage, chaque run lancé et terminé, la connexion Discord — n'apparaissent qu'une fois le niveau relevé dans les settings, par exemple `"Logging": { "LogLevel": { "Orkeon": "Information" } }`.

Les secrets vont dans `/etc/orkeon/orkeon-host.env`, lisible du seul utilisateur du service. L'unité, elle, en reste vierge.

### Windows

L'archive **complète** (`orkeon-<version>-win-x64.zip` — pas le zip CLI) porte
le daemon et ses artefacts de déploiement. L'exécutable réel est
`libexec\orkeon-host\orkeon-host.exe` ; `bin\orkeon-host.cmd` est un wrapper
de terminal — ne jamais enregistrer le wrapper auprès du SCM. Le script
d'enregistrement est livré dans le dossier `deploy\windows\` de l'archive.

Deux canaux installent le même service. Le plus rapide est le **MSI
per-machine** dédié — `orkeon-host-<version>-win-x64.msi`, produit distinct du
MSI per-user du CLI — qui pose les mêmes chemins et enregistre le même service
déclarativement (double-clic, ou `msiexec /i ... /qn`). Tout ce qui suit sur le
compte, les chemins, les secrets, la récupération et le journal d'événements
vaut pour les deux canaux ; seule la mécanique d'enregistrement diffère.

Pour le canal script : extrayez l'archive sous `C:\Program Files\Orkeon`,
posez votre configuration sous `C:\ProgramData\Orkeon` (les miroirs de
`/opt/orkeon` et `/etc/orkeon`), puis lancez le script embarqué — ces chemins
sont ses défauts :

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

Aucun mot de passe n'est jamais passé pour ce compte, et c'est une exigence
plutôt qu'un confort : le SCM réclame un mot de passe *NULL* pour un compte
virtuel, et un mot de passe *vide* est une autre valeur. Donnez-lui le vide et
il valide le nom comme un compte ordinaire, puis le refuse avec l'erreur 1057,
*le nom de compte est invalide ou n'existe pas* — à propos d'un nom
parfaitement valide. Le canal MSI ne la rencontre jamais (sa table
`ServiceInstall` laisse le mot de passe à null) ; le script s'aligne dessus en
omettant complètement `password=`. L'enregistrement fixe aussi le type de SID
du service (`sc.exe sidtype Orkeon unrestricted`) ; `restricted` est l'étape de
durcissement suivante, non testée ici.

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

Avec A2A activé, l'écoute passe par HTTP.sys, qui ne laisse un compte non
administrateur écouter que sur un préfixe d'URL réservé pour lui : le préfixe que
décrit `Orkeon:Host:A2A` — `Host`, `Port` et une barre finale,
`http://localhost:5002/` par défaut, `http://+:5002/` pour toutes les interfaces.
Le script le réserve en enregistrant le service :
`install-service.ps1 -A2AUrlPrefix http://+:5002/` lance `netsh http add urlacl`
pour `NT SERVICE\Orkeon` (une réservation que le service tient déjà est gardée ;
une réservation tenue par un autre compte fait échouer l'enregistrement), note le
préfixe sous la clé du service, et `-Uninstall` — ou un nouvel enregistrement —
la retire. Relancez le script avec le nouveau préfixe après avoir changé `Host`
ou `Port`. Le MSI ne réserve rien : il s'installe avant toute configuration et ne
peut donc pas connaître le préfixe — son écran de fin donne l'étape, à lancer une
fois en administrateur :

```powershell
netsh http add urlacl url=http://+:5002/ user="NT SERVICE\Orkeon"
```

Un service démarré sans sa réservation ne plante pas : le démarrage est refusé
avec le code de sortie 78, et le journal d'événements Application porte la
commande exacte, préfixe et compte compris. Tout autre refus de l'écoute — un
port qu'un autre processus tient, un port sous 1024 sans le privilège — nomme le
préfixe et `Orkeon:Host:A2A:Port`.

### Conteneur

[`deploy/Dockerfile.host`](https://github.com/orkeon/orkeon/blob/main/deploy/Dockerfile.host). Le jeton est passé par nom à l'exécution, jamais gravé dans une couche. L'image garde la licence et les notices tierces de ce qu'elle redistribue sous `/usr/share/doc/orkeon/` (`LICENSE.md`, `THIRD-PARTY-NOTICES.md`) ; le runtime .NET est celui de l'image de base, qui porte les siennes. L'image n'expose aucun port et ne déclare aucun `HEALTHCHECK` : la seule surface HTTP du daemon est le serveur A2A, éteint par défaut. Pour exposer des crews depuis un conteneur, réglez `Orkeon:Host:A2A:Host` sur `http://+` — la boucle locale d'un conteneur est injoignable de l'extérieur, donc `A2A:Security` doit déclarer un schéma d'authentification — et publiez le port (`-p 5002:5002`).

---

## 7. Ce qui est livré, et ce qui ne l'est pas

**Livré** : le host et son cycle de vie, le registre de crews avec isolation par run et plafond de concurrence, les ports de la passerelle, l'autorisation par liste, le routage thread-est-run, le répondeur throttlé, le canal Discord avec les slash-commands enregistrées `/status` et `/stop` et le bouton d'arrêt — un seul chemin autorisé pour les trois — et le serveur A2A opt-in, une compétence par crew exposée.

**Non livré**, et sous-entendu nulle part : un ordonnanceur, le rechargement à chaud de la configuration, l'ajout ou le retrait de crews hébergées pendant que le démon tourne, tout canal de chat autre que Discord, et toute surface HTTP au-delà du serveur A2A — ni API, ni endpoint de santé (les contrôles de santé qu'enregistre la télémétrie ne sont exposés par rien), ni enregistrement de tâche A2A (`GET /a2a/tasks/{id}` répond `501`). Une fonction des runners reste aussi hors du daemon : l'outil `semantic_search` n'est pas enregistré (les serveurs MCP de la section `MCP` sont connectés au démarrage, comme pour un run — voir [Intégration MCP](mcp.md)). Les ports sont écrits de sorte que le protocole JSONL du bus d'événements en soit une implémentation légitime — le modèle ne se referme pas sur le chat — mais ce canal-là n'est pas écrit.

**Une chose ne peut pas être vérifiée en CI** : le critère de succès de la spec elle-même — lancer une crew depuis un vrai fil Discord, voir la progression, l'interrompre par bouton, avec le service en daemon systemd. Cela exige un compte Discord et un serveur, donc une action propriétaire. Ce que la CI tient, c'est tout ce qui borde la socket : la traduction des messages, les deux limites de la plateforme, l'autorisation, le routage, le throttling et l'isolation.

---

## 8. Voir aussi

- [Le bus d'événements du run](run-event-bus.md) — le protocole qu'un processus observateur lit, et la forme sur laquelle les ports de la passerelle ont été écrits.
- [EventHub et cycle de vie des crews](event-hub-and-crew-lifecycle.md) — le messaging inter-crews et son ACL.
- [Matrice de publication](../reference/publication-matrix.md) — où `orkeon-host` est livré.
