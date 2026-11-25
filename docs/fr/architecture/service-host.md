> 🇬🇧 [English version](../../architecture/service-host.md)

# Le host de service et la passerelle de chat

**Périmètre** : `orkeon-host` — le daemon qui héberge des crews, la passerelle qui permet de les atteindre depuis un canal de chat, et le serveur A2A qui permet à d'autres agents de les lancer.
**Public** : qui installe et exploite Orkeon sur un serveur.

Lancé depuis un terminal ou embarqué dans un programme, Orkeon suppose un humain devant un écran, sur la même machine, le temps d'un processus. Le host de service lève les trois hypothèses ; les canaux Discord et Microsoft Teams donnent des endroits où l'on peut lui parler depuis là où l'on est déjà.

---

## 1. Ce que c'est, et ce que ce n'est pas

**C'est** un processus long-vivant qui héberge une ou plusieurs crews, isole chaque run, borne la concurrence, répond à un canal de chat et — quand la configuration expose des crews — à des pairs A2A, et s'arrête sans abandonner le travail en vol.

**Ce n'est pas un ordonnanceur.** Orkeon n'en livre aucun, délibérément. Une crew qui doit tourner chaque matin est lancée par le système, à partir de l'artefact que produit `orkeon forge promote --schedule` — tâche Windows, timer systemd ou ligne cron —, que `orkeon forge schedule` installe et que `orkeon forge unschedule` retire. Le host en pose la fondation ; il ne prétend pas l'être, et aucune partie de ce document ne doit se lire autrement.

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

La clé du modèle n'est pas dans le fichier non plus : elle vient de `ORKEON_Llm__ApiKey`, posée dans l'environnement du service (voir *L'installer*), ou de la variable que le fichier nomme à la place (`Llm:ApiKeyEnvVar`, et `Llm:Profiles:<nom>:ApiKeyEnvVar` par profil — voir la [configuration](../reference/configuration.md#la-clé-dapi-apikey-apikeyenvvar)), posée dans ce même environnement. Un hôte lit les variables du compte sous lequel il tourne — sous Windows sa portée Utilisateur aussi, jamais recopiée dans le processus : une clé que vous avez posée dans votre environnement utilisateur atteint un `orkeon-host` que vous lancez vous-même, pas un hôte qui tourne sous un compte de service. Toute clé se surcharge de la même façon — le préfixe `ORKEON_`, `__` pour `:` — si bien que `ORKEON_Orkeon__Host__RunTimeout=00:10:00` raccourcit l'échéance sans toucher au fichier.

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
| `--allow-external-mounts` | Autorise un `--mount` d'opérateur hors du dossier de travail — lu aussi depuis `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`. Rien ne l'implique : les dossiers des crews et les dossiers accordés aux crews sont des entrées du démon, lisibles où qu'ils soient, sans le drapeau. |
| `-h`, `--help` / `--version` | Affichent puis sortent avec `0` ; ils répondent en premier. |

Une valeur s'écrit `--flag valeur` ou `--flag=valeur` ; une option courte ne prend sa valeur que dans l'argument suivant. Le démon refuse avec `78`, avant tout démarrage : un argument inconnu (`orkeon-host: unknown argument '<arg>'; see orkeon-host --help`), `-h=x` ou `-s=x` compris ; `--help`, `--version` et `--allow-external-mounts` écrits avec `=` (`orkeon-host: <flag> takes no value.`) ; une valeur attachée vide (`orkeon-host: <flag>= has an empty value.`) ; `--settings` (ou `-s`) ou `--working-dir` donné deux fois ; `--settings`, `-s` ou `--working-dir` donné en dernier, sans valeur (`orkeon-host: <flag> requires a path.`, `--settings` pour `-s`) ; et un `--mount` ou `-m` donné en dernier, sans valeur (`orkeon-host: <flag> requires a value.`).

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
de fusionner avec lui, donc le registre composé reporte les mounts du boot — les internes (`/llm-logs`,
`/sandbox`, `/credentials`, `/credential-files`) et ceux que voit l'agent, moins tout chemin que les mounts propres du crew revendiquent — sans cela, la journalisation des échanges et les bacs à sable de code tomberaient
pour toute la durée du run, et le contrôle qui les empêche de gagner une seconde adresse joignable
par l'agent tomberait avec eux. Un dossier accordé est une entrée du démon : il est lisible où qu'il soit, sans
`--allow-external-mounts`. Au démarrage, un dossier accordé à une crew mal formé ou absent refuse le démarrage avec `78`
et une ligne qui nomme la crew (`orkeon-host: crew '<nom>' is granted a folder that does not exist: '<mount>'.`), et un `--mount`
hors du dossier de travail, sans le drapeau, est refusé avec `78` et la ligne du run, préfixée `orkeon-host: ` :
`The mount <racine> (<physique>) is outside the directories this run may read: ...`.

`Path` accepte ce qu'accepte `orkeon run` : un fichier YAML, un dossier de crew multi-fichiers, ou un script `.ork.ts`. Le host le charge par le même chemin de code, donc **une crew hébergée est exactement la crew qu'un terminal lance**. Le dossier de chaque crew est **monté automatiquement dans le VFS, en lecture seule, sous un nom** — `/crews`, puis `/crews-1`, `/crews-2`, … pour chaque dossier supplémentaire — et la crew est chargée par cette orthographe virtuelle (`/crews/support.yaml` pour un fichier, `/crews-1` pour un dossier). Le loader lit par le système de fichiers virtuel comme tout le reste du framework, et un chemin qui n'existerait que sur le disque physique passerait la sonde de démarrage puis échouerait à chaque message. Le montage n'est délibérément **pas** identité : un agent qui appelle `list_mounts`, ou qui lit un message de refus d'accès, ne doit jamais recevoir l'organisation disque de l'opérateur ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). Un `--mount` à vous qui revendique `/crews*` est refusé au démarrage avec le code de sortie 78. Une réserve accompagne la forme script : transpiler du `.ork.ts` demande esbuild sur la machine, et ni l'image de conteneur ni une installation service nue ne l'embarquent — une crew hébergée en daemon est une crew YAML, sauf à installer esbuild soi-même.

### `Files` — les dossiers d'une conversation

Éteint par défaut. Avec `Orkeon:Host:Files:Enabled`, chaque run d'une crew hébergée trouve deux dossiers : `/inbox`, en
lecture seule, ce que la conversation a reçu, et `/outbox`, en lecture-écriture, ce que ses runs rendent. Les deux durent
autant que la conversation : le message suivant les retrouve tels que le run précédent les a laissés. Aucun run n'est monté
sur les deux dossiers d'une autre conversation, même sur une crew à la fois ; ce que deux conversations d'un hôte partagent
par ailleurs est dit sous **Partagé**, plus bas. Un run demandé par A2A ou par l'API de l'hôte n'a
pas de conversation : il reçoit deux dossiers vides à lui, supprimés à sa fin. À la fin d'un run, les fichiers de `/outbox`
qu'il a ajoutés ou modifiés sont listés, fichiers cachés et liens exclus. Discord remplit `/inbox` ([ce qu'il prend](#ce-que-le-canal-discord-écoute)) ;
[les commandes de Discord](#les-commandes-de-fichiers) listent et envoient ce que contient `/outbox`. Rien n'est envoyé d'office,
sauf ce qu'un agent envoie par [`chat_attach`](#les-fichiers-quenvoie-un-agent).

```json
"Files": {
  "Enabled": true,
  "Root": "conversations",
  "MaxFileBytes": 10485760,
  "MaxFilesPerMessage": 10,
  "MaxConversationBytes": 104857600,
  "MaxConversationFiles": 1000,
  "MaxTotalBytes": 2147483648,
  "MaxAttachedFilesPerRun": 10,
  "MaxAttachedMessagesPerRun": 5,
  "MaxAttachedBytesPerRun": 52428800,
  "Retention": "7.00:00:00",
  "AllowedExtensions": [ "pdf", "csv" ]
}
```

- **Où.** `Root` (relatif au dossier de travail du service) tient un dossier par conversation, nommé par un hachage de son
  canal et de son identifiant, ouvert au seul compte du service sous Unix. Le réglage autorise ce dossier à l'hôte lui-même
  où qu'il soit ; aucun agent ne l'atteint autrement que par ses deux mounts.
- **Refusé au démarrage (`78`).** Un `Root` qui est un dossier que monte l'hôte, qui en contient un ou qui se trouve
  dessous — un `--mount`, un mount des settings, le dossier d'une crew, les `Mounts` d'une crew —, liens suivis des deux
  côtés ; un mount sur `/inbox` ou `/outbox`, ou sur un chemin dessous, où qu'il soit déclaré ; une limite qui n'est pas
  positive ; une extension vide. Les dossiers sont comparés sans tenir compte de la casse sous Windows et macOS. Un montage
  bind mène là où aucun lien ne se voit, et n'est pas détecté : ne liez pas `Root` ainsi.
- **Contrôlé à nouveau.** Une crew qui écrit dans un dossier peut y remplacer un sous-dossier par un lien : la comparaison
  est refaite à chaque run, qui est refusé, et à chaque accès, si bien qu'un lien posé pendant un run n'ouvre rien non plus.
- **Partagé.** L'index de code, les bases documentaires et le journal d'`edit_batch` servent tout le processus. Dans le run
  d'une conversation, `index_codebase`, `incremental_reindex`, `rag_ingest`, `edit_batch` et les recherches qui ingèrent ce
  qu'elles lisent (`directory_search`, `txt_search`, `pdf_search`, `mdx_search`) refusent `/inbox`, `/outbox`, ce qui est
  dessous, et un dossier ou un motif qui les contient. `file_read`, `grep` et les autres outils de fichiers les lisent. Ce
  qu'un agent écrit lui-même dans un endroit partagé — un dossier déclaré comme `/output`, `session_store`, le hub
  d'événements — est lu par les runs qui le partagent.
- **Limites.** Un fichier au-delà d'une limite, ou d'une extension qu'`AllowedExtensions` ne prend pas, est refusé seul. Un
  run écrit librement tant qu'il tourne : les limites sont contrôlées à l'arrivée d'un fichier et au démarrage d'un run, et
  une conversation au-delà de `MaxConversationBytes`, ou un hôte au-delà de `MaxTotalBytes`, ne démarre aucun run avant que
  la rétention ou l'opérateur ne fasse de la place. Un fichier déposé dans un dossier d'une crew (`/put`) compte dans sa
  conversation, en octets et pour `MaxConversationFiles`, jusqu'à ce que la rétention la supprime, même une fois que la crew
  l'a déplacé ou supprimé ; le fichier, lui, est à la crew, et reste. Une conversation dont un dossier ne peut être listé compte comme au-delà
  de sa propre limite, et n'en arrête aucune autre.
- **Rétention.** Une conversation est supprimée `Retention` après sa dernière activité ; le contrôle passe au démarrage,
  puis toutes les heures. Celle dont un run est en cours, ou suspendu sur une question, est gardée. Un run suspendu se
  reprend hors de l'hôte (`orkeon run --resume`), sans conversation.
- **Le shell et le code.** Un `shell_command` non confiné, ou du code exécuté sur l'hôte
  (`Orkeon:CodeSandbox:AllowHostExecution`), n'est pas tenu par les mounts et lit toutes les conversations : l'hôte le dit
  au démarrage, et [le confinement](security.md#confinement-du-shell) est la réponse. Les serveurs MCP stdio et le processus
  du fournisseur `agent-cli` tournent aussi sous le compte du service : voir [les limites](../reference/limitations.md).
  Le bac à sable du code, `/sandbox`, est une seule racine pour tous les runs : aucun agent ne l'atteint par un chemin, et
  seuls ces processus, ou un shell qui reçoit un mount contenant le dossier du bac à sable, passent par là d'un run à l'autre.
- **Hors de l'hôte.** Les deux racines sont réservées dans `orkeon-host` avec `Files` allumé, et nulle part ailleurs. Une
  crew qui s'en sert tourne dans un terminal avec `--mount ./in:/inbox:ro --mount ./out:/outbox:rw`.

#### `files:` — les dossiers qu'une crew ouvre

Une crew nomme les dossiers accessibles en plus de `/outbox`, toujours ouvert au téléchargement. Elle choisit **dans** ce
que l'opérateur a monté pour elle, et n'ouvre rien par défaut. [Les commandes de fichiers](#les-commandes-de-fichiers) les lisent.

```yaml
files:
  - path: /output
    download: true
  - path: /output/drop
    upload: true
```

- **Où l'écrire.** Dans le YAML de la crew, ou le `config.yaml` (sinon `crew.yaml`) d'un dossier de crew. Un script l'a
  dans un fichier voisin, `support.files.yaml` pour `support.ork.ts`, qui ne contient que cette clé et se lit sans exécuter
  le script ; un script sans ce fichier n'ouvre aucun dossier hors `/outbox`. Le voisin d'une crew YAML n'est pas lu.
- **Quand il est lu.** À chaque question, dans le fichier tel qu'il est : un changement ne demande pas de redémarrage.
- **Refusés.** Un chemin hors des mounts de la crew, sous un mount interne, sous `/crews*`, sur `/inbox` ou `/outbox` ; un
  téléchargement là où le mount n'accorde pas la lecture, un dépôt là où il n'accorde pas l'écriture et la création ; un
  dossier qui est ou contient celui d'une définition de crew, ou fait partie d'un dossier de crew, liens suivis. La casse
  ne contourne rien : un chemin est comparé aux mounts tel qu'il est écrit, et à ce qu'il ne doit pas atteindre sans égard
  à la casse.
- **Une déclaration invalide** ferme les dossiers de cette crew, tous, et d'aucune autre. Le journal dit pourquoi, une fois.
  Un bloc mal formé en lui-même fait aussi échouer le chargement de sa crew ; un dossier que l'hôte refuse, non.
- **`orkeon run --validate`** refuse un bloc mal formé, celui d'un fichier voisin compris. Il ne connaît pas les mounts de
  l'hôte.

La configuration est **validée au démarrage** : aucune crew sous `Orkeon:Host:Crews`, une crew sans `Name` ou sans `Path`, deux crews de même nom (sans tenir compte de la casse), un chemin de crew qui n'existe pas, un `MaxConcurrentRuns` inférieur à 1, un `RunTimeout` nul, négatif ou au-delà d'environ 24,8 jours, un `ShutdownGracePeriod` négatif, une entrée de `LlmProfiles` qui nomme un profil que `Llm:Profiles` ne définit pas, et — pour un canal activé — une liste d'autorisation vide, une variable de jeton absente, un `ProgressInterval` nul ou négatif, une entrée de `GuildIds` ou une clé de `Routes` qui n'est pas un nombre, ou une entrée de `Routes` ou un `DefaultCrew` qui nomme une crew que l'hôte ne déclare pas ; pour un serveur A2A activé, aucune crew exposée, une crew exposée que l'hôte ne déclare pas, un `Host` ou un `Port` mal formé, ou une écoute au-delà de la boucle locale sans authentification, et — quel que soit l'interrupteur A2A — une clé `A2A:EnableServer`, `A2A:Host` ou `A2A:Port`, que le démon ne lit pas (voir [Autres agents (A2A)](#5-autres-agents-a2a)), et une écoute que le système refuse — une URL que HTTP.sys n'a réservée pour personne, un port qu'un autre processus tient (voir [Windows](#windows)) ; et, avant tous ceux-là, un réglage que refuse l'hôte des runners — une valeur que le lieur de configuration ne convertit pas (`"RunTimeout": "abc"`, `"Orkeon:Guardian:Enabled": "oui"`) ou qu'une règle de sa section refuse, une clé qu'aucune section ne porte (`Orkeon:Host:RunTimeoutt`, ou une clé qui n'est plus un réglage, `RaggableTree:Exclude`), un nom de section ou de composant qu'il ne connaît pas (`Orkeon:Guardain`, un fournisseur de mémoire, un reranker RAG), un niveau de journal qui n'en est pas un, un profil LLM qu'il ne sait pas construire, un fichier de réglages qu'il ne peut pas lire, une adresse qui n'en est pas une (`RaggableTree:Embedding:BaseUrl`, `Telemetry:OtlpEndpoint`), refusent tous le démarrage avec le code de sortie 78 — avant que le service ne se déclare prêt — plutôt que d'être découverts un run raté à la fois. Tout réglage que lit le démon est jugé à ce moment, qu'un run s'en serve ou non ; les comptes e-mail sont la seule exception — un compte illisible est mis de côté et signalé quand un run le nomme ([quand un réglage est refusé](../reference/configuration.md#quand-un-réglage-est-refusé)).

Le démarrage refuse aussi une référence `${NOM}` ou `%NOM%` dans un réglage que l'hôte transmet tel qu'il est écrit, en nommant le chemin et jamais la valeur, avec le code 78 et avant la connexion d'un serveur MCP : Orkeon ne développe jamais une variable ([la liste](../reference/configuration.md#une-référence-de-variable-nest-jamais-développée)).

Toute la séquence qui précède le démarrage passe sous cette barrière : la configuration d'amorçage du démon, ses propres sections — `Orkeon:Host` avec son `A2A`, `Orkeon:Host:Discord`, `Orkeon:Host:Teams`, et la section `A2A` —, **lues une fois, au démarrage**, et l'hôte des runners lui-même. Chaque refus tient en une ligne qui nomme la clé — ou le fichier, avec la ligne et la position de ce que son JSON a de faux — sur stderr et, sous le SCM de Windows, dans le journal d'événements Application.

Les crews sont lues **une seule fois**, au démarrage : ni balayage de dossier, ni rechargement. Ajouter une crew, c'est modifier le fichier et redémarrer.

### Aucun secret n'est jamais écrit ici

`TokenEnvironmentVariable` porte le **nom** d'une variable d'environnement. Le jeton lui-même ne touche jamais le fichier de configuration, un commit, ni une couche d'image de conteneur — où il resterait aussi longtemps que l'image existe, y compris après que quelqu'un l'a « supprimé » dans une couche ultérieure. C'est la règle que suivent déjà les fournisseurs LLM, et un jeton de bot, qui peut lire tous les messages d'un serveur, n'y fait pas exception.

### Le profil : un seul axe, à dessein

| Axe | Défaut | Pourquoi |
|---|---|---|
| `MaxConcurrentRuns` | `4` | Un daemon qui accepte toutes les requêtes qui arrivent meurt à sa première rafale, et un canal de chat rend les rafales triviales. |

Le profil ne porte délibérément rien d'autre : une surface de configuration qui ne fait rien
est pire qu'absente.

Une requête au-delà du plafond est **refusée avec une réponse**, pas mise en file : « on est occupé, réessayez » est quelque chose qu'un canal relaie à une personne ; une file d'attente invisible ne l'est pas. Le plafond est celui de la crew, quel que soit le chemin par lequel les runs arrivent : conversations de chat et tâches A2A le partagent.

---

## 3. L'isolation

Chaque run obtient son propre scope d'injection de dépendances, et l'état par crew d'un run est libéré quand il se termine. À eux deux, ils portent la défense contre le risque que la conception de la passerelle désigne comme le plus sérieux : de l'état qui fuit entre conversations.

Deux mécanismes, énoncés précisément. Le **scope** isole les services scoped — le repository de crews avant tout : la crew d'une conversation n'est jamais résoluble depuis le run d'une autre. La **libération** tient les services process-wide honnêtes : chaque message charge une crew fraîche avec un id frais, et le service de mémoire comme le registre de fournisseurs abandonnent leur entrée à la fin du run — sans quoi un daemon en accumule une par conversation, pour toujours. Une mémoire qui survivrait à un run hébergé est impossible par construction — il n'y a pas de drapeau à se tromper.

Chaque run porte aussi son échéance (`RunTimeout`). Un daemon n'a personne pour appuyer sur Ctrl-C : un run sans délai est un daemon bloqué à attendre un modèle qui ne répondra pas.

---

## 4. La passerelle

Un message devient un run dans un ordre fixe : **autoriser, router, accuser réception, travailler.**

**Autoriser d'abord.** Un expéditeur absent de la liste n'atteint jamais une crew, ne coûte jamais un jeton, et n'apparaît jamais dans un journal comme une requête acceptée. Il en est informé, car le silence ressemble à un bot cassé.

> **Une liste d'autorisation vide refuse tout le monde**, et le canal refuse de démarrer plutôt que de ne répondre à personne en silence. Le défaut inverse est la façon dont un bot invité sur un serveur public finit par dépenser le budget d'API de quelqu'un pour des inconnus.

**Router.** Le host livre une seule stratégie : **un thread est un run**, et **le salon choisit la crew**. Un fil ouvert dans un salon Discord listé sous `Discord:Routes` (identifiant de salon → nom de crew) démarre cette crew ; un fil ouvert ailleurs démarre `Discord:DefaultCrew` — la première crew de `Orkeon:Host:Crews` s'il est absent. Dans l'exemple ci-dessus, les fils du salon `234567890123456789` atteignent `veille`, et ceux de tout autre salon `support`. Une crew hébergée qu'aucun salon n'atteint est nommée par un avertissement au démarrage. Les identifiants de salon sont ceux qu'affiche Discord en mode développeur (clic droit sur le salon → Copier l'identifiant du salon) ; un fil d'un salon forum suit la route du forum. Quand le host ne connaît pas encore le salon d'un fil, il le demande à Discord ; sans réponse, le message est refusé par une phrase et ne démarre aucune crew. C'est la correspondance qu'une personne peut prédire sans qu'on la lui explique — `#facturation` répond facturation, et ce qui se passe dans ce fil est un travail — et elle donne le parallélisme sans inventer une notion de session que quiconque doive apprendre. Un second message dans un fil qui tourne est refusé avec une explication, plutôt que de lancer un second run dont personne ne saurait distinguer les réponses.

**Accuser réception.** La fenêtre de réponse de toute plateforme de chat se mesure en secondes ; une crew se mesure en minutes. L'accusé de réception part avec l'**admission** : à l'instant où la place du run est réservée — toujours avant tout travail de crew, et porteur du bouton d'arrêt, pour qu'un run soit interruptible dès sa première seconde. Un refus (crew inconnue, saturée) est répondu sans accusé : « je m'y mets » plus un bouton Stop, suivi de « on est occupé », serait une promesse rétractée par sa propre ligne suivante — avec un bouton accroché à rien.

**Travailler**, en rapportant au fil de l'eau. La réponse finale est postée dans le fil, coupée aux 2 000 caractères de Discord (avec la marque `…(truncated)`) ; une réponse vide s'affiche `(no output)`. Avec [`Files`](#files--les-dossiers-dune-conversation) allumé, la réponse se termine par les fichiers de `/outbox` que le run a ajoutés ou modifiés et n'a pas envoyés tels quels, dont la place est réservée avant la coupe du texte, et une réponse coupée est gardée entière dans `/outbox/reply-<id du run>.md`, avant que la conversation soit libre pour le message suivant ; quand une limite la refuse, la réponse le dit. Un succès, un échec, un arrêt et un run qui attend une entrée finissent de même. Les fichiers sont nommés, jamais envoyés sans demande par l'hôte. La progression est **throttlée** (`ProgressInterval`, 2 secondes par défaut) : un run émet un événement par pensée d'agent et par appel d'outil, et relayer chacun épuiserait la limite de débit par canal de Discord à l'intérieur d'une seule crew. La dernière mise à jour supprimée est vidée juste avant la réponse finale, pour qu'un run ne se termine pas sur une vue vieille de plusieurs étapes.

### Ce que le canal Discord écoute

Les messages **à l'intérieur d'un fil**, écrits par des personnes : un message posté dans un canal ordinaire est ignoré, tout comme chaque message d'un bot et chaque message que Discord écrit lui-même (un épinglage, une arrivée, un renommage). Le client se connecte avec les intents `Guilds`, `GuildMessages` et `MessageContent` — `MessageContent` est un intent privilégié, à activer pour le bot dans le portail développeur de Discord, faute de quoi chaque message arrive vide. `AllowedUserIds` est le seul contrôle d'accès : `GuildIds` choisit où les slash-commands sont enregistrées, il ne restreint pas qui peut parler au bot. Les messages du bot ne mentionnent personne : un `@everyone` dans une réponse est du texte.

**Fichiers.** Sans [`Files`](#files--les-dossiers-dune-conversation), aucun n'est pris : un message avec des pièces jointes tourne sur son seul texte, et l'accusé de réception nomme les fichiers laissés de côté. Un message sans texte et sans fichier pris ne démarre aucun agent et reçoit une phrase en réponse. Avec `Files` allumé :

- **Réception.** Les fichiers sont téléchargés une fois le run admis et acquitté, sous son échéance : `/stop` arrête aussi un téléchargement. Ils viennent de `cdn.discordapp.com` seul, en HTTPS, sans redirection et sans le jeton du bot ; leur adresse signée n'est ni gardée ni journalisée. La taille annoncée fait refuser tôt, et les octets sont comptés à mesure qu'ils arrivent. Un fichier déjà rangé, d'après son identifiant Discord, n'est pas rangé deux fois.
- **Refus.** Un fichier au-delà d'une limite, au-delà de `MaxFilesPerMessage`, d'une extension non prise, ou illisible est laissé seul de côté ; la réponse nomme chacun avec sa raison. Le run continue avec le reste. Quand rien n'est arrivé et que le message n'a pas de texte, aucun agent ne tourne.
- **Ce que lit la crew.** Un bloc suit le texte de la personne : le chemin, le type et la taille des fichiers du message, les 20 derniers fichiers antérieurs, et `/outbox`. Un message de fichiers sans texte demande à la crew de les traiter. Il n'y a aucune variable de prompt et le contenu n'est jamais inséré ; les noms sont assainis, cités et bornés, si bien qu'aucun ne forge une ligne du prompt, et le bloc dit au modèle que ce sont des données. Le contenu d'un fichier vaut ce que vaut la personne qui l'envoie : un agent qui le lit peut y trouver des instructions, ne donnez donc à une crew ouverte à des inconnus aucun outil que vous ne leur donneriez pas.
- **Images.** Les fichiers png, jpeg, webp et gif du message sont montrés à chaque tâche du run, quand le modèle de l'agent est connu pour lire les images ([la règle et ses bornes](../guides/multimodal.md#les-images-dun-run)) ; ceux des messages précédents sont nommés par leur seul chemin. La réponse finit par les images qu'un agent n'a pas vues, avec qui et pourquoi.
- **Portées.** Un agent dont le [`filesystem:`](vfs-compliance.md#périmètre-de-fichiers-dun-agent) laisse `/inbox` de côté ne lit pas les fichiers ; le journal le dit à chaque run.
- **Laissés de côté.** Les autocollants et les messages transférés sont ignorés : on poste le fichier lui-même.

### Le canal Microsoft Teams

`Orkeon:Host:Teams` (éteint par défaut) tourne à côté de Discord ou seul ; chaque canal a sa liste d'autorisation, ses routes et sa crew par défaut. Teams appelle le bot, donc ce canal **écoute** : `POST /api/messages` sur son propre préfixe.

```json
"Orkeon": {
  "Host": {
    "Teams": {
      "Enabled": true,
      "AppId": "11111111-2222-3333-4444-555555555555",
      "TenantId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      "SecretEnvironmentVariable": "ORKEON_TEAMS_APP_SECRET",
      "AllowedUserIds": [ "99999999-8888-7777-6666-555555555555" ],
      "Prefix": "http://localhost:3978/",
      "Routes": [ { "Channel": "19:abc123@thread.tacv2", "Crew": "veille" } ]
    }
  }
}
```

**Ce que vous préparez chez Microsoft.** Une inscription d'application mono-tenant avec un secret client, un Azure Bot qui l'utilise, avec le canal Microsoft Teams activé et `https://<votre hôte>/api/messages` comme point de terminaison de messagerie, et une application Teams portant le bot, installée dans l'équipe ou la conversation. `AllowedUserIds` contient des **identifiants d'objet** Entra. `Routes` est une liste parce qu'un identifiant de canal Teams contient `:`, qu'une clé de configuration ne peut pas porter ; `Channel` est l'identifiant seul, sans le `;messageid=…` d'un fil.

**TLS se termine devant le host.** Microsoft n'appelle qu'en HTTPS. Placez devant le préfixe de boucle locale un reverse proxy qui tient le certificat et ne relayez que `/api/messages`, avec `Host: localhost:3978` — l'écouteur ne répond qu'au nom d'hôte de son préfixe. En conteneur, ou quand le proxy garde le `Host` d'origine, utilisez `http://+:3978/` ; le démarrage avertit alors que seul le proxy doit atteindre ce port. Un préfixe `https://` n'est accepté que sous Windows, avec un certificat lié au port (`netsh http add sslcert`). Un préfixe qui ne peut pas être pris refuse le démarrage. L'écouteur borne les requêtes simultanées (64) et le temps que met un corps (10 secondes), pas les connexions ni leurs en-têtes : laissez le proxy les limiter.

**Chaque requête est prouvée avant que son corps soit lu.** Le jeton porteur doit être signé (RS256) par une clé des métadonnées OpenID du Bot Framework, émis par `https://api.botframework.com`, pour `AppId`, dans sa durée de vie (cinq minutes de tolérance), et porter une seule claim `serviceurl` ; sinon `401`. Les clés de signature sont lues à une adresse fixe, gardées douze heures et crues vingt-quatre au plus ; sans clé, rien n'est accepté (`503`). Viennent ensuite le type de contenu et la taille (256 Ko), puis le corps est tenu à ce que dit le jeton : le même `serviceUrl`, une clé de signature habilitée pour `msteams`, et `TenantId` — sinon `403`. Rien ne débraye ces contrôles.

**Les réponses ne vont qu'au service validé.** La claim `serviceurl` doit être en `https` sur un hôte que Microsoft publie pour Teams, ou sur un hôte nommé par `ServiceHosts` ; les redirections ne sont pas suivies. Le jeton du bot est obtenu par les identifiants client, gardé jusqu'à peu avant son expiration, et ni lui ni le secret ne sont journalisés. Les clouds nationaux (GCC High, DoD, 21Vianet) ont d'autres émetteurs et d'autres adresses de connexion et ne sont pas pris en charge.

**Un fil est un run** ici aussi : dans un canal d'équipe, le fil ; dans une conversation personnelle ou de groupe, la conversation entière. Dans un canal, Teams ne livre que les messages qui **mentionnent le bot** (sauf si l'application a reçu davantage), et la mention est retirée. Un message sans identifiant d'objet Entra, d'un autre bot, ou vide est ignoré, tandis qu'un fichier envoyé seul reçoit une réponse comme sur Discord — sauf dans un canal d'équipe, où Teams ne donne un fichier que sous forme de lien dans la copie HTML du message, qui n'est pas lue ; une activité livrée deux fois tourne une fois. La requête reçoit sa réponse aussitôt, et le run continue. Les réponses sont coupées à 20 000 caractères. Le HTML d'une réponse est échappé hors du code : `List<string>` s'affiche tel quel et aucune balise d'image n'est rendue ; une image Markdown devient un simple lien. Une réponse finale est retentée quand la connexion échoue, quand la connexion du bot à son service d'identité échoue pour une raison passagère, et sur HTTP 412, 429, 502, 503 et 504.

#### Les fichiers dans Teams

[`Files`](#files--les-dossiers-dune-conversation) allumé, les fichiers passent **dans la conversation personnelle avec le bot seulement** : Teams n'en donne à un bot nulle part ailleurs. Dans un canal ou une conversation de groupe, `/files`, `/get` et `/put` reçoivent cette phrase, de même qu'un message dont Teams annonce les fichiers ; le `chat_attach` d'un agent y est refusé avec elle. `Files` éteint, les commandes répondent qu'elles sont éteintes sur cet hôte, et ceux d'un message sont nommés comme laissés de côté.

```json
"Files": { "Enabled": true },
"Teams": {
  "SharePointNames": [ "contoso" ],
  "CertificatePathEnvironmentVariable": "ORKEON_TEAMS_CERTIFICATE_PATH"
}
```

**Le manifeste de l'application Teams** (les entrées pour les fichiers, à côté des vôtres) :

```json
{
  "bots": [ { "botId": "11111111-2222-3333-4444-555555555555", "scopes": [ "personal" ], "supportsFiles": true } ],
  "authorization": {
    "permissions": {
      "resourceSpecific": [ { "name": "File.ReadWriteSelected.User", "type": "Application" } ]
    }
  }
}
```

`supportsFiles` permet d'envoyer des fichiers au bot. La permission propre à la ressource est celle que Microsoft annonce pour le jeton d'application : les manifestes l'acceptent à partir du **2027-01-15** (avant, Teams refuse le manifeste), et l'application doit être publiée avec elle au plus tard le **2027-03-01**. `File.ReadSelected.User` ne fait que recevoir ; `File.ReadWriteSelected.User` envoie et couvre la réception. Elle ne donne aucun accès à elle seule : uniquement à un fichier qu'une personne a envoyé au bot ou accepté.

**N'accordez rien d'autre à l'application sur SharePoint.** Elle n'a besoin d'aucune permission `Sites.*` ni `Files.*`, de Graph ou de SharePoint. Avec l'une d'elles, son jeton ouvrirait tous les fichiers que cette permission couvre, et l'adresse que lit l'hôte vient de ce qu'a envoyé le client d'une personne : qui pourrait écrire cette adresse ferait lire à l'hôte, pour la crew, un fichier que personne ne lui a partagé. La règle des chemins ci-dessous réduit cela à des fichiers ; elle ne sait pas distinguer un fichier d'un autre.

**Le certificat.** SharePoint prend le jeton propre de l'application, obtenu par un certificat ; Microsoft y refuse un secret client (le bot garde le sien pour ses réponses). `CertificatePathEnvironmentVariable` nomme la variable d'environnement qui contient le chemin absolu d'un fichier PEM : le certificat et sa clé privée RSA non chiffrée, la moitié publique étant inscrite sur l'application `AppId`. Gardez-le dans un dossier à lui : l'hôte monte ce dossier en lecture seule là où aucun agent ne le voit ([fichiers d'identifiant](vfs-compliance.md#racines-internes-et-accès-privilégié)), et dit au démarrage quand il ne le peut pas. Il est relu pour chaque jeton. Une variable qui ne contient pas de chemin absolu refuse le démarrage.

**Sans certificat.** Jusqu'au 2027-03-01 (UTC), les adresses données par Teams qui portent un paramètre `tempauth` marchent sans jeton, et l'hôte s'en sert ainsi ; le démarrage avertit. Après cette date, chaque fichier de SharePoint est refusé avec la raison, et le texte des messages tourne encore ; une image collée dans le message vient du Bot Connector et ne demande aucun certificat. Les mêmes adresses servent quand le certificat n'arrive pas à se connecter.

**D'où viennent et où vont les fichiers.** Les pages de Microsoft ne donnent pas la liste des hôtes : c'est donc la configuration qui les nomme. `SharePointNames` `contoso` vaut `contoso.sharepoint.com` et `contoso-my.sharepoint.com`, et aucun autre. Vide, aucun fichier n'est échangé et le démarrage avertit. Les images d'un message sont montrées à la crew comme [sur Discord](#ce-que-le-canal-discord-écoute). Une image collée dans un message vient du Bot Connector d'où vient le message, sous `/v3/attachments/`, avec le jeton du bot. Toujours HTTPS sur le port par défaut, sans redirection, l'adresse exactement comme Teams l'a écrite, un jeton demandé pour ce seul hôte et envoyé à lui seul, un fichier du seul `TenantId`, les octets comptés à mesure ; ni adresse ni jeton ne sont journalisés. Teams n'annonce pas de taille : un fichier trop gros est refusé pendant qu'il arrive.

**Quels chemins.** Microsoft ne documente aucun chemin et demande d'utiliser l'adresse exactement comme reçue : la règle est donc la plus étroite qui prenne encore les formes vues. Une réception est le chemin propre d'un fichier, `_layouts/15/download.aspx`, ou `_api/v2.x/drive(s)/…/content` ; un envoi est `_api/v2.x/drive(s)/…/uploadSession`, sur le seul hôte `-my`. Les autres pages et interfaces de SharePoint (`_api`, `_layouts`, `_vti_*`, `_admin`, `_catalogs` et semblables), un segment vide ou fait de points, un séparateur encodé ou un double encodage sont refusés, sans requête et sans jeton demandé. Si Microsoft employait une autre forme, le fichier serait refusé avec la raison.

**Commandes.** Tapées comme des messages : `/files [folder]`, `/get <path>`, et `/put <folder>` sur un message qui porte un fichier. La réponse est un message de la conversation.

**Envoi.** Tout fichier, de `/get` comme de `chat_attach`, est d'abord **proposé par une carte de consentement** ; `chat_attach` reçoit `awaiting_consent` et le run n'attend pas. Accepté, le fichier est déposé dans le OneDrive de la personne à l'adresse que donne Teams (contrôlée comme les autres), puis montré par une carte de fichier. Une offre vaut une heure, pour la personne à qui elle est faite, dans la conversation où elle est faite, et n'est envoyée qu'une fois : une réponse rejouée, forgée, tardive ou venue de quelqu'un d'autre est refusée et c'est dit. Au plus 10 offres attendent dans une conversation et 256 sur l'hôte. Elles sont gardées en mémoire : **un redémarrage les perd**, et la carte le dit. Au plus 25 Mio par fichier, borne propre à l'hôte ; aucun fichier vide. Un fichier changé depuis son offre (taille ou date) n'est pas envoyé : redemandez-le. Quand la connexion, SharePoint ou l'attente des fichiers des autres échoue, la carte peut être acceptée de nouveau pendant ce qui reste de son heure. Le contenu tient en mémoire pendant l'envoi : un fichier à la fois par personne, quatre sur l'hôte, chacun attendant son tour une minute au plus.

**Ce que les pages de Microsoft laissent inconnu** : la liste complète des hôtes, la plus grande taille de fichier, la durée de vie d'une adresse. Le SharePoint d'un cloud national n'est pas pris en charge. L'hôte n'appelle pas Graph et ne lit pas les fichiers des canaux ([ADR-021](../adr/ADR-021-chat-tool-family.md)).

### Commandes

`/status` et `/stop` sont, sur Discord, des **slash-commands enregistrées** — le client Discord les autocomplète, et la réponse est **éphémère** : un coup d'œil au statut ou un refus regarde celui qui invoque, pas une ligne de plus dans le thread de tout le monde. Elles sont enregistrées à la connexion : globalement quand `GuildIds` est vide (aucune configuration, mais Discord met les commandes globales en cache jusqu'à une heure), ou par serveur nommé (disponibles immédiatement — la boucle de dev). Sur Discord, la passerelle ne parse pas le texte des messages pour elles : un `/stop` littéral tapé comme texte est un prompt comme un autre. Sur Teams, un message qui est `/status` ou `/stop` et rien d'autre (la mention à part) est la commande, avec la réponse dans le fil ; il n'y a pas de bouton Stop.

| Commande | Effet |
|---|---|
| `/status` | Ce que cette conversation exécute, et depuis quand. |
| `/stop` | Arrête le run de cette conversation. Le **bouton Stop** est la même invocation avec un autre doigt — même contrôle d'allow-list, même réponse. |
| `/files [folder]` | `Files` allumé : les fichiers des dossiers de la conversation et de ceux que la crew ouvre. |
| `/get <path>` | `Files` allumé : envoie un fichier (sur Teams, le propose). |
| `/put <file> <folder>` | `Files` allumé : range un fichier dans un dossier que la crew ouvre au dépôt. |

Chaque chemin est gardé par le `AllowedUserIds` de son canal. Bouton compris : un clic de quelqu'un hors liste est refusé en éphémère au lieu d'arrêter le run.

#### Les commandes de fichiers

Enregistrées sur Discord quand [`Files`](#files--les-dossiers-dune-conversation) est allumé, et pas autrement. Sur Teams elles se tapent comme des messages, dans la conversation personnelle ([Les fichiers dans Teams](#les-fichiers-dans-teams)). Sur Discord elles répondent en éphémère : ce que `/get` envoie n'est vu que de la personne qui le demande.

- **Où.** Dans un fil : son canal, demandé à Discord après la première réponse, choisit la crew. Ailleurs, ou quand le canal ne peut pas être connu, la commande est refusée, et la crew par défaut n'en tient jamais lieu.
- **Ce qui est ouvert.** `/outbox` est toujours listé et envoyé. `/inbox` est listé et n'est pas renvoyé : la personne a ce qui a été envoyé, et un fichier retiré du canal ne doit pas en ressortir par le bot. Le reste est ce que déclare le [`files:`](#files--les-dossiers-quune-crew-ouvre) de la crew, lu à chaque commande. Les fichiers cachés, les liens et les autres mounts sous un dossier ouvert sont laissés de côté. Une liste lit au plus 1 000 fichiers par dossier.
- **Chemins.** Un chemin se tape entier, depuis `/`, et n'a qu'une lecture : `..`, `.`, un segment vide, une barre oblique inverse ou un caractère de contrôle est refusé. Il est tenu à la déclaration et au système de fichiers virtuel ; un chemin interdit reçoit la même réponse qu'un chemin absent. Le client complète avec au plus 25 chemins de 100 caractères ; un chemin plus long se tape à la main. Rien n'est complété pour une personne hors liste.
- **Tailles.** La limite de Discord du moment, et 24 Mio au plus par requête, dix fichiers par message ; un fichier plus gros est nommé et n'est pas envoyé. Un `/get` à la fois par personne, et quatre pour l'hôte : le suivant est prié de réessayer. Dans un fil, le bot a besoin de la permission **Attach Files**.
- **Pendant un run.** Les trois commandes marchent. Le run ne retient pas les fichiers de la conversation : une commande n'attend qu'une autre commande, jamais un fichier en train d'arriver. `/get` envoie une copie lue d'un trait, après avoir jugé de nouveau le chemin ; un fichier qui grossit entre-temps est refusé, et un fichier réécrit à taille égale peut partir tel qu'il était à mi-chemin.
- **`/put`** ne démarre aucun run. Le fichier est rangé sous les limites de tout fichier reçu, jamais par-dessus un autre, et pas deux fois dans un dossier pour un même fichier Discord ; il a deux minutes pour arriver, et le dossier est jugé de nouveau une fois qu'il est là : celui que la crew a fermé entre-temps, ou remplacé par un lien, ne prend rien. Un agent le voit quand il regarde dans ce dossier.
- **Qui voit.** Un dossier déclaré comme `/output` appartient à la crew : ce qu'un fil y laisse, les autres fils de la crew le lisent.

#### Les fichiers qu'envoie un agent

`Files` allumé, un agent qui liste `chat_attach` envoie des fichiers dans le fil auquel son run répond, visibles de tous ceux qui y sont ([ADR-023](../adr/ADR-023-chat-attach-tool.md)).

- **Quoi.** Exactement ce que `/get` enverrait, par les mêmes contrôles : `/outbox` et les dossiers que la crew déclare en `download`. Jamais `/inbox`. Un fichier interdit reçoit la même réponse qu'un fichier absent. Un agent dont la portée `filesystem:` exclut un dossier n'en envoie rien.
- **Où.** La conversation du run et aucune autre : l'outil ne prend pas d'adresse. Les agents que le run démarre (sous-agents, agents créés, travail en arrière-plan) la partagent, avec ses comptes, jusqu'à la fin du run. Sans conversation, ou après le run, l'appel échoue et le run continue.
- **Combien.** `MaxAttachedFilesPerRun`, `MaxAttachedMessagesPerRun` et `MaxAttachedBytesPerRun` bornent un run, tous ses agents ensemble ; les limites du canal s'y ajoutent. Ce qui dépasse un plafond est refusé et nommé avec son réglage.
- **Ce qui revient.** Par fichier : `sent`, `too_large`, `refused` ou `awaiting_consent`, avec la raison. Sur Teams, un fichier de la conversation personnelle est `awaiting_consent` ([Les fichiers dans Teams](#les-fichiers-dans-teams)).
- **Quand cela s'arrête.** `/stop` et l'échéance du run mettent fin à l'envoi : le reste n'est pas envoyé. Ce qui a été posté le reste.

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

**Sécurité.** La section `A2A:Security` du même fichier s'applique telle qu'écrite — `ApiKey` (les clés acceptées nommées par `ApiKeySecretNames`, lues dans `ORKEON_<NOM>`), `Bearer` (Azure AD ou OIDC), mTLS — voir [Sécurité](security.md#tls-mutuel-a2a). La carte reste publique ; les points de tâche exigent l'identifiant. Sans schéma `ApiKey` ni `Bearer` (le défaut sur la boucle locale, ou le mTLS seul, dont un navigateur présente le certificat de lui-même) le listener refuse ce qu'une page web pourrait forger : un `POST` sans `Content-Type: application/json` reçoit `415`, un en-tête `Host` qui ne nomme pas le listener (rebond DNS) `421`. Le démarrage est refusé avec le code de sortie 78 quand la section n'expose aucune crew, ou une crew que `Orkeon:Host:Crews` ne déclare pas ; quand `Host` n'est pas un nom d'hôte `http://` ou `https://` ou que `Port` sort de 1–65535 ; et quand l'hôte écoute au-delà de la boucle locale (tout sauf `localhost`, une adresse `127.x.x.x` ou `[::1]`) alors que `A2A:Security` ne déclare ni schéma d'authentification ni mTLS. `A2A:EnableServer`, `A2A:Host` et `A2A:Port` — les interrupteurs des hôtes C# — sont refusés aussi, en nommant leur remplaçant sous `Orkeon:Host:A2A` : le démon ne lit que la sienne.

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

Les sondes de santé de l'[API du host](../reference/host-api.md) (`GET /healthz`, `GET /readyz`, éteintes sauf `Orkeon:Host:Api:Enabled`) sont ce qu'appelle un `HEALTHCHECK` Docker ou une sonde Kubernetes ; systemd garde `Type=notify` comme disponibilité propre.

Il n'y a délibérément **pas de `WatchdogSec`** : l'intégration systemd de .NET envoie `READY=1` et `STOPPING=1` et aucun battement de watchdog — en armer un ferait tuer par systemd un host sain à son premier battement manqué, jamais envoyé.

`TimeoutStopSec` est délibérément plus long que `ShutdownGracePeriod`, pour que les runs en vol aient leur grâce avant que systemd ne perde patience. **Augmenter l'un sans l'autre rend celui qui reste en arrière dépourvu de sens.** À l'arrêt, le host ferme d'abord l'admission : **dès cet instant, plus aucun run ne démarre** — un message de chat ou une tâche A2A arrivé pendant la grâce reçoit « The host is stopping: this run was not started. Send it again once the host is back. », sans accusé de réception ni bouton Stop, et un pair A2A le lit comme `Failed` ; rien n'est chargé, aucun modèle n'est appelé. Il attend ensuite jusqu'à `ShutdownGracePeriod` les runs en vol — le canal et le serveur A2A restent debout pendant ce temps, pour livrer leurs réponses, et `/status` et `/stop` répondent toujours —, puis les arrête tous et leur laisse cinq secondes de plus pour se terminer ; le budget d'arrêt du host générique est fixé à la période de grâce plus dix secondes pour couvrir les deux.

Le journal est discret par défaut : l'hôte des runners journalise au niveau **Warning**, si bien que les lignes Information — chaque crew hébergée au démarrage, chaque run lancé et terminé, la connexion Discord — n'apparaissent qu'une fois le niveau relevé dans les settings, par exemple `"Logging": { "LogLevel": { "Orkeon": "Information" } }`. Un niveau vaut `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` ou `None`, sans tenir compte de la casse : un autre mot refuse le démarrage avec le code de sortie 78, en nommant sa clé.

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

Le MSI **n'est pas signé**. Windows affiche *« Windows a protégé votre ordinateur »* avec un
éditeur inconnu au double-clic — **Informations complémentaires** → **Exécuter quand
même** — et l'invite d'élévation ne nomme pas d'éditeur non plus. Vérifiez d'abord le
fichier, contre `SHA256SUMS.msi` et son attestation de build :
[Vérifier ce que vous installez](../guides/verify-what-you-install.md).

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

[`deploy/Dockerfile.host`](https://github.com/orkeon/orkeon/blob/main/deploy/Dockerfile.host). Le jeton est passé par nom à l'exécution, jamais gravé dans une couche. L'image garde la licence et les notices tierces de ce qu'elle redistribue sous `/usr/share/doc/orkeon/` (`LICENSE.md`, `THIRD-PARTY-NOTICES.md`) ; le runtime .NET est celui de l'image de base, qui porte les siennes. L'image n'expose aucun port et ne déclare aucun `HEALTHCHECK` : les surfaces HTTP du daemon, le serveur A2A et l'[API du host](../reference/host-api.md), sont éteintes par défaut. Avec `Orkeon:Host:Api:Enabled`, un `HEALTHCHECK` sur `/healthz` et la sonde de disponibilité de l'orchestrateur sur `/readyz` sont ce à quoi l'API sert (la vivacité ne dépend jamais du LLM ; la page dit ce que la disponibilité vérifie). Pour exposer des crews depuis un conteneur, réglez `Orkeon:Host:A2A:Host` sur `http://+` — la boucle locale d'un conteneur est injoignable de l'extérieur, donc `A2A:Security` doit déclarer un schéma d'authentification — et publiez le port (`-p 5002:5002`).

---

## 7. Ce qui est livré, et ce qui ne l'est pas

**Livré** : le host et son cycle de vie, le registre de crews avec isolation par run et plafond de concurrence, les ports de la passerelle, l'autorisation par liste, le routage thread-est-run, le répondeur throttlé, le canal Discord avec les slash-commands enregistrées `/status`, `/stop`, `/files`, `/get` et `/put` et le bouton d'arrêt — un seul chemin autorisé pour les trois —, le canal Microsoft Teams, avec les fichiers en conversation personnelle, et le serveur A2A opt-in, une compétence par crew exposée.

**Non livré**, et sous-entendu nulle part : un ordonnanceur, le rechargement à chaud de la configuration, l'ajout ou le retrait de crews hébergées pendant que le démon tourne, tout canal de chat autre que Discord et Teams, et toute surface HTTP au-delà du point d'entrée Teams, du serveur A2A et de l'[API du host](../reference/host-api.md) (désactivée par défaut : sondes de santé, routes de run et `POST /runs/{id}/input`) — ni enregistrement de tâche A2A (`GET /a2a/tasks/{id}` répond `501`). Une fonction des runners reste aussi hors du daemon : l'outil `semantic_search` n'est pas enregistré (les serveurs MCP de la section `MCP` sont connectés au démarrage, comme pour un run — voir [Intégration MCP](mcp.md)). Les ports sont écrits de sorte que le protocole JSONL du bus d'événements en soit une implémentation légitime — le modèle ne se referme pas sur le chat — mais ce canal-là n'est pas écrit.

**Une chose ne peut pas être vérifiée en CI** : le critère de succès de la spec elle-même — lancer une crew depuis un vrai fil Discord, voir la progression, l'interrompre par bouton, avec le service en daemon systemd. Cela exige un compte Discord et un serveur, donc une action propriétaire. Le canal Teams en est au même point : il a été essayé contre des formes relevées et des clés générées, jamais contre un vrai tenant. Ce que la CI tient, c'est tout ce qui borde la socket : la traduction des messages, les deux limites de la plateforme, l'autorisation, le routage, le throttling et l'isolation.

---

## 8. Dans le processus, ou sous `orkeon-host`

Une application .NET peut exécuter un crew dans son propre processus : l'`ICrewScriptRunner` du
paquet `Orkeon.Scripting` ([Hosting — exécuter un crew dans le processus](../reference/hosting.md#exécuter-un-crew-dans-le-processus))
construit l'hôte de runner d'`orkeon run` pour une exécution, rend la sortie du crew et le libère.
Cela couvre un crew que l'application appelle et attend : un gestionnaire de requête, un traitement
par lots, un test.
Ses chemins relatifs — le crew, le fichier de settings, les montages — se résolvent contre le
répertoire de travail du processus, comme pour `orkeon run` ; une exécution annulée lève
`OperationCanceledException`.

Un crew a besoin d'`orkeon-host` à la place quand il doit survivre à un appel :

- **Il répond à un canal de chat.** La passerelle, sa liste d'autorisation, le routage
  thread-est-run, le répondeur throttlé et les commandes `/status` / `/stop` ne vivent que dans le
  démon.
- **D'autres agents l'appellent.** Le serveur A2A, sa carte d'agent et son authentification sont
  ceux du démon.
- **Plusieurs crews partagent un service.** Le registre de crews, son plafond de concurrence, les
  espaces de noms de mounts par crew et l'isolation par run sont ce qu'`orkeon-host` ajoute à une
  exécution unique.
- **Il est supervisé comme un service.** systemd, un service Windows ou l'image conteneur
  redémarrent et journalisent le démon ; une exécution in-process vit et meurt avec son appelant.

Un `.ork.ts` procédural — qui pilote des agents lui-même au lieu de remettre un crew par
`globalThis.crew` — n'est ni l'un ni l'autre : il s'exécute avec `orkeon run`.

---

## 9. Voir aussi

- [Le bus d'événements du run](run-event-bus.md) — le protocole qu'un processus observateur lit, et la forme sur laquelle les ports de la passerelle ont été écrits.
- [EventHub et cycle de vie des crews](event-hub-and-crew-lifecycle.md) — le messaging inter-crews et son ACL.
- [Matrice de publication](../reference/publication-matrix.md) — où `orkeon-host` est livré.
