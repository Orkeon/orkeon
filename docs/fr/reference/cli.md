> 🇬🇧 [English version](../../reference/cli.md)

# Référence du CLI `orkeon`

L'outil en ligne de commande `orkeon` est le point d'entrée principal du framework : il exécute les crews YAML et les scripts TypeScript (`.ork.ts`), génère une configuration, sonde les fournisseurs LLM, pilote le sous-système RAG, cherche dans les cas d'usage d'exemple et diagnostique une installation. Il est construit depuis `src/scripting/Orkeon.Scripting.Cli` et se packe comme dotnet tool `orkeon` :

```bash
dotnet tool install --global Orkeon.Scripting.Cli --prerelease
orkeon doctor
```

Les binaires de release et les installeurs (zip/MSI Windows, paquet Debian, tarballs macOS) embarquent le même CLI, autonome — aucun SDK .NET requis. Voir [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md).

**Codes de sortie** (stables) : `0` OK · `1` erreur de script/config (fichier manquant, script invalide, échec de validation) · `2` le run a échoué — une erreur runtime inattendue, un service que le host n'a pas pu construire au démarrage, ou une équipe qui a tourné sans réussir (une tâche sans réponse finale, un disjoncteur déclenché, un consensus non atteint) · `130` annulé par Ctrl+C. Sur un code `2`, la **dernière ligne de stderr** est `ERROR: <raison>` — la phrase qui dit pourquoi ; le type de l'exception et sa trace de pile ne sont journalisés qu'en `--verbose 2` (ou `ORKEON_DEBUG=1`).

## `orkeon run`

```bash
orkeon run <crew.ork.ts | crew.yaml | répertoire-crew/> [options]
```

Exécute une définition de crew et imprime son résultat sur stdout. Le dispatch dépend de la cible : `.ork.ts`/`.js` part vers l'hôte de scripting (transpilation esbuild + Jint) ; `.yaml`/`.yml` — ou un répertoire contenant un crew YAML multi-fichiers (`config.yaml` + `agents/` + `tasks/`, ou le triplet plat `crew.yaml`/`agents.yaml`/`tasks.yaml`) — part vers le runner YAML one-shot partagé.

| Option | Description |
|---|---|
| `-s, --settings <chemin>` | Chemin vers `appsettings.json`. Sans elle, une chaîne de repli s'applique (ci-dessous). |
| `-m, --mount <spec>` | Montage VFS, format Docker `<physique>:<virtuel>:<droits>[;sous-chemin:droits]`. Plusieurs montages se passent **séparés par des espaces derrière un seul flag** (`--mount a:/x:ro b:/y:rw`) — le parseur rejette un `--mount` répété. Une lettre de lecteur Windows ne demande rien de particulier (`C:\src:/workspace:ro`) ; un chemin que la forme nue ne peut pas porter — contenant un `:` ou un `;`, ou finissant par une barre oblique inverse — se met **entre guillemets** : `"/data/odd:name":/data:ro`, `"C:\src\":/workspace:ro`. Ces guillemets appartiennent à la grammaire des *montages*, donc votre shell ne doit pas les manger : en bash/zsh, entourez toute la spec de guillemets simples (`--mount '"/data/odd:name":/data:ro'`) ; en PowerShell, doublez-les (`--mount '""/data/odd:name"":/data:ro'`). La barre oblique inverse n'est jamais un caractère d'échappement. Le chemin virtuel est toujours un nom commençant par `/` — jamais un chemin disque ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)), et `/crew`, `/script`, `/llm-logs` et `/sandbox` sont réservés par le runner (`RunnerVirtualRoots.All`) : un mount qui en revendique un est refusé avec le code 1, et refusé de la même façon qu'il ait été écrit ici ou déclaré dans le fichier de settings — le garde lit les deux, et nomme les racines que *cette* commande réserve plutôt qu'une liste figée. `orkeon forge` ne réserve que `/sandbox` : il n'accepte aucun `--mount`, monte lui-même `/workspace`, `/forge` et `/output`, et place ces trois racines face aux settings exactement comme un `--mount` l'est (phrase suivante) — une entrée des settings sur l'une d'elles est remplacée pour l'essai, si bien qu'un fichier de settings nommant `/output`, mount ordinaire d'un run normal et nom que Studio donne au dossier d'écriture d'une équipe, forge sans rien changer. **Face au fichier de settings, un `--mount` est placé par racine virtuelle** : sur une racine que `Orkeon:FileSystem:Mounts` déclare déjà (fichier de settings ou environnement `ORKEON_`), le `--mount` **remplace toutes les entrées des settings de cette racine pour ce run** — il est écrit à l'index de la première entrée, les autres sont retirées, et le journal dit `mount /x: --mount replaces the settings entry` ; sur une racine neuve, il est **ajouté** après toutes les entrées déclarées. Les entrées des settings qu'aucun `--mount` ne nomme restent en vigueur. Une racine n'est en double que lorsqu'une même *source* la revendique deux fois sans que rien ne distingue les revendications : deux `--mount` sur la même racine (`--mount a:/x:ro b:/x:rw`), ou un fichier de settings qui déclare deux fois une racine avec une entrée sans identifiant (ligne suivante), sont refusés avec le code 1 et une seule ligne (`ERROR: '/x' is mounted twice on the command line: … Keep one.` / `… declared twice in <settings> (…) and '<entrée>' has no id. Give every entry an id …`), avant qu'aucun host ne soit construit. |
| `--mount-id <ulid>` | Sélectionne, parmi plusieurs entrées des settings déclarant **une même racine virtuelle**, celle que ce run garde (VFS-90). Une entrée des settings peut porter un identifiant — l'ULID de 26 caractères devant son `|`, `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` ; Orkeon Studio en écrit un à chaque enregistrement — et deux entrées ne partagent une racine que si toutes deux en portent un. Plusieurs identifiants se passent **séparés par des espaces derrière un seul flag**, comme `--mount`. L'entrée désignée est gardée telle que déclarée (dossier, droits) ; les autres entrées de sa racine sont **retirées pour le run** — pas montées, pas en liste blanche, leur dossier pas sondé. Sans l'option, le bloc `mounts:` de la crew (`<ulid>|/output`, voir le [schéma YAML](../architecture/yaml-schema.md)) sélectionne de la même façon ; sans l'un ni l'autre, une racine déclarée plusieurs fois est refusée avec le code 1 et une ligne qui nomme chaque identifiant (`ERROR: '/output' is declared twice in <settings> (<idA>: <dossierA>, <idB>: <dossierB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`). Un identifiant qu'aucune entrée ne porte, ou mal formé, est refusé de même ; un `--mount` sur la même racine l'emporte sur l'option, avec une ligne `WARNING:` ; une racine que la crew exige et que rien ne fournit est refusée aussi (`the crew requires '/output' … pass --mount <folder>:/output:rw`). Les agents ne voient jamais un identifiant : `list_mounts` et les messages de refus d'accès ne nomment que des chemins virtuels. |
| `--allow-external-mounts` | Autorise les arguments `--mount` hors du répertoire de travail (ou `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`) : le chemin de base de chaque `--mount` est ajouté à la liste blanche `PathSecurity:AdditionalAllowedDirectories` contre laquelle `PathValidator` vérifie les chemins résolus, à la suite de ce que les settings y listent déjà. Un montage **déclaré dans le fichier de settings** (ou via des variables `ORKEON_`) n'a besoin d'aucun drapeau : son chemin de base est toujours mis en liste blanche, parce qu'un dossier déclaré est l'intention explicite du propriétaire de la machine — jusque-là un tel dossier était monté et chaque accès refusé comme « outside the allowed workspace directory », sans qu'aucun drapeau puisse y remédier. |
| `-v, --verbose <0-2>` | `0` silencieux, `1` échanges LLM & outils, `2` debug complet. |
| `--llm-log` / `--llm-log-path <rép>` | Journalise les échanges LLM complets en JSONL (répertoire par défaut `./llm-logs`). |
| `--inputs <json>` / `--inputs-file <chemin>` | Entrées structurées pour les **scripts** (variable globale `inputs`). |
| `-V, --var CLE=VALEUR` | Variable pour le `CrewInput` d'un **crew YAML** (gabarits de tâche `{CLE}`). Plusieurs variables séparées par des espaces derrière un seul `-V` (un flag répété est rejeté). |
| `--initial-context <texte>` | Contexte initial passé au `CrewInput` d'un **crew YAML**. |
| `--memory-limit-mb <n>` | Plafond mémoire Jint pour cette exécution (`0` le désactive). |
| `--validate` | Dry run : résout les settings, construit l'hôte, charge le crew avec résolution stricte des outils — aucun appel LLM, aucun kickoff. Imprime `VALIDATION OK/FAILED: …`. |
| `--list-tools` | Construit l'hôte, imprime le registre trié des outils runtime, puis sort. Aucun chemin de crew requis. |
| `--events jsonl` | Émet le protocole d'événements versionné sur stdout au lieu du rendu texte, et lit des commandes sur stdin. C'est ainsi qu'Orkeon Studio observe un run. Voir [Le bus d'événements du run](../architecture/run-event-bus.md). |
| `--stream` | Avec `--events`, émet aussi les `llm.delta` jeton par jeton. Verbeux par nature : désactivé sauf demande. |
| `--client <nom>` | Avec `--events`, le nom auquel le processus observateur répond sur le hub du run (`client://<nom>`, défaut `studio`). Les agents peuvent lui écrire à cette adresse ; le bloc `links:` d'une crew l'autorise. |

```bash
orkeon run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount ./out:/output:rw -v 1
```

**Résolution des settings** — sans `--settings`, le CLI suit une chaîne de repli : `appsettings.json` à côté du fichier de crew, puis un `appsettings/appsettings.json` trouvé en remontant les répertoires parents, puis le fichier global par utilisateur écrit par `orkeon init`, puis les variables d'environnement `ORKEON_*` seules. Détails et matrice de profils prêts à l'emploi : [Exécuter votre premier exemple](../getting-started/run-your-first-example.md).

## `orkeon forge`

```bash
orkeon forge "résumer chaque matin les nouvelles offres de mon fournisseur"   # partir d'un besoin
orkeon forge                                   # ouvrir sur l'entretien
orkeon forge list                              # lister les sessions du workspace
orkeon forge resume <slug>                     # reprendre une session exactement là où elle s'est arrêtée
orkeon forge resume <slug> --read <dir>        # l'essayer sur les documents de <dir>
orkeon forge resume <slug> --adopt             # garder l'équipe telle quelle, sans essai
orkeon forge promote <slug> --to <dir>         # sortir une session prête en dossier ordinaire
orkeon forge reopen <dossier-equipe>           # retrouver — ou reconstruire depuis crew/ — la session liée à une équipe promue
```

L'Atelier : un parcours guidé du besoin en langage naturel à l'équipe déployable. Un assistant vous interroge et capte un brief structuré — objectif, entrées, **critères d'acceptation**, un exemple d'entrée — puis propose un plan d'équipe, le rend, le valide, **l'essaie en bac à sable sur votre exemple**, et juge le résultat **contre vos propres critères**. Non conforme ? Le diagnostic alimente une boucle de correction, bornée par un budget dur (itérations, jetons, temps). Chaque session vit sous `.orkeon/forge/<slug>/` — reprenable, diffable entre tentatives, auditable.

Démarrer ou reprendre un cycle exige un LLM configuré (`orkeon init`) : la forge refuse d'ouvrir l'entretien sans lui (`FORGE-LLM-UNAVAILABLE`) plutôt que de dégrader en silence. `list`, `promote` et `reopen` sont entièrement hors ligne.

`reopen <dossier-equipe>` rend une équipe promue à nouveau modifiable, quoi qu'il soit advenu de sa session. Une seule règle décide de la session à laquelle un dossier est lié (la règle R) : chaque session porte un identifiant stable — annoncé par `session.started` — que sa promotion recopie dans le `forge.json` du dossier, et la session qui porte cet identifiant est liée quand son `promotedTo` nomme le dossier, ou quand le dossier qu'il nomme a disparu ou ne porte plus l'identifiant : l'équipe a été déplacée ou renommée, et la session est pointée vers sa nouvelle place. Quand `promotedTo` nomme un autre dossier existant qui porte le même identifiant, ce dossier-ci est une **copie**, liée à rien ; un dossier sans identifiant, ou avec un identifiant qu'aucune session ne porte, n'est lié à rien non plus. Une session liée est seulement nommée : reprenez-la. Sinon — session supprimée, dossier forgé sur une autre machine ou importé, copie, dossier sans identifiant — une session est **reconstruite** depuis le dossier lui-même : le plan est relu depuis `crew/` (`config.yaml` + `agents/` + `tasks/` par entité, ou un seul `crew.yaml`), le brief vient du `forge.json` que chaque promotion écrit à côté de `FORGE.md` — ou est dérivé du plan, et la commande le dit — et le crew est copié tel quel. La session reconstruite reçoit un nouvel identifiant, écrit dans le `forge.json` du dossier (créé sans brief quand le dossier n'en avait pas ; celui d'une copie est réécrit, si bien qu'elle ne peut plus atteindre la session de l'original, et sa session porte le nom du dossier de la copie), et se pose à la pause `--dry`, en pointant vers le dossier : le `reopen` suivant la retrouve, `resume --edit --dry`, `resume`, `resume --adopt` suivent comme d'habitude, et un `promote --to` vers le même dossier le met à jour en place. Un dossier sans crew YAML (crew script, disposition étrangère, fichiers qui ne décrivent pas un plan valide) est refusé avec `FORGE-TEAM-UNREADABLE` et les raisons ; le verbe n'accepte aucune option sauf `--events`. C'est ainsi que fonctionne le « Modifier » d'Orkeon Studio, sur toute équipe.

| Option | Description |
|---|---|
| `--format yaml\|script` | Format rendu (défaut `yaml`). `script` rend un `crew.ork.ts` éditable et exige esbuild — absent, une nouvelle session retombe sur YAML avec `FORGE-ESBUILD-MISSING`. Le format d'une session ne change jamais en reprise. |
| `--events jsonl` | Émet le protocole d'événements versionné sur stdout au lieu du rendu terminal ; les réponses descendent sur stdin (c'est ainsi qu'Orkeon Studio pilote la forge). |
| `--auto` | Arbitre les verdicts non conformes sans humain, dans les limites du budget. |
| `--dry` | S'arrête après la validation — génère et valide, n'exécute jamais. Reprenez sans `--dry` pour essayer. |
| `--edit` | *(resume)* Amende le blueprint d'une session en pause avant son essai : le JSON amendé passe par le canal (`blueprint.edited` sur stdin en mode `--events`, une ligne collée dans le terminal), est validé intégralement, puis re-rendu de façon déterministe — zéro token LLM, même itération. Avec `--dry`, la session se remet en pause à la même frontière. À l'arbitrage, utilisez plutôt la décision `edit`. |
| `--adopt` | *(resume)* Garde l'équipe telle qu'elle a été générée, sans essai : une session mise en pause par `--dry` passe directement à Ready. Entièrement hors ligne — pas d'hôte, pas de LLM, pas de dossier d'exécution, zéro jeton. Ce qui est sauté, ce sont les **preuves** que produit un essai, jamais un contrôle : à cette pause le crew est rendu et validé, et la promotion n'a jamais consommé d'artefact d'essai (`verdict.json` est facultatif et `FORGE.md` écrit « aucun verdict enregistré »). Refusé partout ailleurs, avec `FORGE-INVALID-STATE`. |
| `--max-iterations <n>` / `--max-tokens <n>` / `--max-seconds <n>` | Le budget (défaut 3 itérations ; `0` = jetons/temps illimités). Une reprise peut le relever ; la consommation est toujours reportée. |
| `--settings <path>` | Mêmes sémantiques qu'`orkeon run` — **forme longue uniquement** : le parseur du forge est artisanal et ne définit aucun alias court. |
| `--read <dir>` | *(nouvelle session, resume)* Le dossier que l'essai lit en `/workspace`, à la place du répertoire de travail. Le répertoire de travail garde tous ses autres rôles — la session vit toujours sous son `.orkeon/forge/<slug>/`, les settings se résolvent toujours à côté : `--read` déplace les documents, pas l'atelier. Un dossier inexistant est refusé avec le code 1 avant toute création de session (`--read names no directory`) ; `promote` refuse l'option, puisqu'il ne monte rien. Un dossier de lecture hors du répertoire de travail est automatiquement mis en liste blanche pour les outils fichiers, comme `orkeon run` le fait pour le dossier de son script — les montages du forge sont ses trois racines à lui, il n'y a donc pas de `--allow-external-mounts` ici. C'est ainsi qu'Orkeon Studio essaie une équipe sur le dossier choisi à sa première étape. |
| `--pack <dir>` | Surcharge le pack de prompts embarqué. |
| `--to <dir>` | *(promote)* Dossier de destination ; doit être inexistant ou vide — sauf s'il s'agit du dossier auquel la session est liée (celui vers lequel elle a promu, ou ce dossier déplacé ou renommé depuis ; jamais une copie, refusée avec la raison), qui est alors mis à jour en place. |
| `--schedule daily@HH:mm\|hourly` | *(promote)* Génère les artefacts de planification sous `schedule/` — XML de tâche Windows, timer systemd, ligne cron. La commande d'installation est **affichée, jamais exécutée** : Orkeon n'a pas d'ordonnanceur. |
| `--with-settings` | *(promote)* Copie le fichier de settings résolu dans le dossier. Off par défaut — un settings porte souvent des clés API et le dossier est fait pour être partagé. |

Le bac à sable : l'essai tourne in-process avec les écritures confinées au dossier de la session (`/output` pour les livrables, `/forge` pour ses fichiers de travail), le répertoire de travail — ou le dossier `--read` — monté en lecture seule en `/workspace`, et `shell_command`/`code_interpreter` retirés du catalogue d'outils — le plan d'équipe ne peut nommer que des outils que la validation acceptera.

Le dossier promu est ordinaire : `crew/` (ou `crew/crew.ork.ts`), `run.sh`/`run.cmd` composés contre la grammaire d'`orkeon run` avec vos entrées d'exemple pré-remplies, `FORGE.md` — la carte d'identité de l'équipe (objectif, critères d'acceptation, verdict, version), écrite dans la langue de l'entretien — et `forge.json`, son jumeau lisible par la machine (identifiant de la session, slug, titre, format, instant de promotion, brief) que `forge reopen` lit — l'identifiant est ce qui relie le dossier à sa session où qu'aille le dossier. `orkeon run <dir>/crew` le lance — depuis l'intérieur de `<dir>`, et sans les `--mount` que fournit `run.sh`, si bien qu'une équipe qui produit des livrables n'écrit rien par cette voie ; le lanceur Studio détecte le dossier et pose les montages lui-même.

## `orkeon usecases`

```bash
orkeon usecases search "je veux un résumé de mes mails chaque matin"  # les cas d'usage les plus proches
orkeon usecases search "summarize my emails every morning" --top 3
orkeon usecases list --category finance-trading --process parallel   # le catalogue, filtré
orkeon usecases show 03-email-pipeline --crew                         # une fiche, et son fichier de crew
```

Le catalogue des cas d'usage d'exemple : les 105 exemples numérotés d'`examples/`, chacun décrit par une fiche écrite en cinq langues ([usecases.json](../../../examples/usecases.json)). L'outil embarque le catalogue lui-même — le manifeste, le fichier de crew de chaque exemple et son dossier `data/` — si bien que les trois sous-commandes fonctionnent hors ligne, ne lisent rien sur le disque et n'appellent aucun LLM. Les exemples finance sont **référence seule** : on peut les chercher et les lire, pas les importer, car leurs crews dépendent d'un dossier `_tools/` partagé que l'outil n'embarque pas.

**`search <texte>`** classe le catalogue selon un besoin écrit en langage naturel, en français, anglais, espagnol, allemand ou chinois simplifié.

- **Par les termes** : BM25 sur le titre et le problème de chaque fiche dans les cinq langues, ses tags, ses outils et sa catégorie. La requête et les fiches sont normalisées de la même façon — minuscules, accents repliés (`resume` trouve `résumé`), chinois découpé en bigrammes de caractères —, si bien qu'une requête trouve ses termes dans la langue où elle est tapée.
- **Par le sens** : le modèle d'embeddings local (BGE-micro-v2, sur la machine) compare la requête au texte anglais de chaque fiche, et son classement est fusionné avec celui des termes par RRF. Le modèle ne lit que l'anglais (voir [Limites connues](./limitations.md)) : le sens n'est donc ajouté que pour les langues où le jeu d'or ([usecases.golden.yaml](../../../examples/usecases.golden.yaml)) a mesuré un gain — aujourd'hui l'anglais seul (mesure du 2026-09-24 sur les textes en cinq langues : les termes seuls atteignent un rappel@5 de 1,00 dans chaque langue, et le sens n'améliore le classement qu'en anglais) ; le français, l'espagnol, l'allemand et le chinois sont cherchés par les termes. Le modèle se charge à la première recherche qui en a besoin — environ une seconde —, puis chaque recherche prend quelques millisecondes.
- **Sans le modèle** (ses fichiers vont dans `LocalEmbeddingsModel/default/` à côté du binaire ; `orkeon doctor` les vérifie), la recherche passe par les termes seuls et chaque réponse le dit. Elle ne se dégrade jamais en silence.

| Option | Description |
|---|---|
| `--top <n>` | Nombre de cas d'usage renvoyés (défaut 5). |
| `--lang fr\|en\|es\|de\|zh-Hans` | La langue de la requête (`zh` est accepté pour `zh-Hans`). Omise, elle est lue dans le texte — mots grammaticaux, lettres accentuées, caractères chinois — et des mots-clés qui ne trahissent rien comptent comme de l'anglais. Elle choisit le mode de recherche et les titres affichés. |
| `--events jsonl` | Répond par une ligne `usecases.results` sur stdout au lieu du rendu texte. **Sans texte, mode session** : une requête par ligne de stdin, une réponse par requête, le modèle chargé une seule fois pour toutes, et la fin de stdin termine le processus avec le code 0. |

Chaque résultat porte l'`id` du cas d'usage, son `rank`, son `score` (BM25 par les termes, RRF en mode hybride — comparable au sein d'une même réponse seulement), la raison de la correspondance `reason` (`terms`, `meaning` ou `terms+meaning`), les termes trouvés `terms`, sa similarité `similarity` avec la requête en mode hybride, et son titre dans la langue de la requête. La réponse porte le mode employé `mode` (`bm25` ou `hybrid`), la langue et la façon dont elle a été établie (`langSource` : `option`, `detected` ou `default`), et `degraded`, la raison, quand le sens a été abandonné.

Le mode session est ce qui permet à Orkeon Studio de suggérer des cas d'usage pendant la frappe : le processus s'ouvre sur une ligne `usecases.ready` (taille du catalogue, langues, mode de chacune), puis répond à chaque requête avec le `correlationId` de la requête dans l'enveloppe.

```text
→ {"kind":"usecases.query","correlationId":"q1","text":"relancer les factures impayées","lang":"fr","top":5}
← {"v":2,"seq":2,"ts":"…","kind":"usecases.results","correlationId":"q1","query":"relancer les factures impayées","lang":"fr","langSource":"option","mode":"bm25","results":[{"rank":1,"id":"40-invoice-processing","score":7.8412,"reason":"terms","terms":["factures"],"title":"…"}]}
```

Une requête sans `top` ni `lang` prend le `--top` et la `--lang` de la ligne de commande. Une ligne qui n'est pas une requête est ignorée. Une requête qui ne peut pas s'exécuter — pas de `text`, une `lang` hors des cinq, un `top` inférieur à 1 — reçoit une ligne `error` portant son `correlationId` et le code `USECASES-QUERY-INVALID`, et la session continue. Les types d'événement sont déclarés une seule fois, dans `Orkeon.Constants.Protocol.UseCaseEventKinds`.

**`list`** imprime le catalogue : id, processus, titre, et les indicateurs `data` (données d'exemple), `web` (a besoin du réseau), `keys` (a besoin d'une clé tierce) et `reference only`. Les filtres se combinent : `--category` (`03-finance-trading`, ou `finance-trading`), `--process` (`sequential`, `hierarchical`, `parallel`, `consensual`, `graph`, `autonomous`), `--tag`. Une catégorie ou un processus inconnu est refusé, avec la liste des valeurs valides. `--lang` choisit les titres (défaut `en`). `--events jsonl` émet une ligne `usecases.catalog` qui contient chaque fiche en entier, sous les noms de champ du manifeste.

**`show <id>`** imprime une fiche : sa catégorie, son processus, ses agents et tâches, ses outils, ses tags, ce dont elle a besoin (réseau, clés), ses montages, si elle est importable, les fichiers que l'outil embarque pour elle, et son titre et son problème dans chaque langue écrite (`--lang` pour une seule). `--crew` ajoute le fichier de crew. `--events jsonl` émet une ligne `usecases.sheet`, avec `crew` sur demande. Un id inconnu sort avec le code 1 et `USECASES-UNKNOWN-ID` (une ligne `error` en mode `--events`).

Codes de sortie : `0` réponse donnée (réponse vide comprise), `1` refus (id inconnu, option invalide), `2` erreur inattendue, `130` Ctrl+C.

## `orkeon init`

Assistant de configuration. Génère un `appsettings.json` valide au chemin global par utilisateur (`%APPDATA%\Orkeon\appsettings.json` sous Windows, `$XDG_CONFIG_HOME/Orkeon/appsettings.json` — sinon `~/.config/Orkeon/appsettings.json` — sous Linux **et** macOS, qui n'utilise volontairement pas `~/Library/Application Support`) via un assistant interactif à 5 choix — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` — ou en mode non interactif par flags, puis sonde l'endpoint (sauf `--no-probe`).

| Option | Description |
|---|---|
| `-p, --provider <preset>` | `ollama` \| `docker-model-runner` \| `openai` \| `custom` \| `none`. |
| `-u, --base-url <url>` / `-m, --model <id>` | Endpoint et modèle. Requis pour `custom` ; les presets ont leurs défauts. |
| `-k, --api-key-env <nom>` / `--api-key <valeur>` | La variable que lit la sonde de `init` — elle n'est **pas** écrite dans le fichier généré, et `init` imprime que le runtime lit `ORKEON_Llm__ApiKey` nativement — ou une clé stockée en clair dans le fichier (déconseillé). Voir [la variable par fournisseur](./llm-providers-comparison.md). |
| `--path <fichier>` | Écrire ailleurs qu'au chemin global par utilisateur. |
| `-f, --force` | Écraser un fichier existant. |
| `--no-probe` | Sauter la sonde de l'endpoint. |

```bash
orkeon init --provider ollama --model llama3.2 --no-probe
```

## `orkeon llm`

Deux verbes contre un endpoint fournisseur réel.

**`orkeon llm probe`** — déroule le protocole de test LLM contre un fournisseur et archive éventuellement la trace de campagne. Options clés : `-p, --provider` (requis : `openai | anthropic | ollama | azure | together | qwen | deepseek | kimi | mistral | huggingface | zai | gemini | grok | minimax | openrouter | mammouth`), `-m, --model`, `-u, --base-url` (requis pour Azure), `--api-version` (mode déploiement Azure), `--workspace-id` (clés à portée de workspace — les clés identity-linked d'Anthropic l'exigent), `-k, --api-key-env` (défaut `ORKEON_LLM_API_KEY` — la clé elle-même n'est jamais acceptée sur la ligne de commande), `--modes` (séparés par des virgules, ex. `M1,M2,M8` ; défaut : tous), `--archive <rép>`, `--format md|json`, `--commit`, `--timeout` (secondes, défaut 180), `--temperature` (défaut 0), `--thinking-effort` (effort de raisonnement de base, ex. `none` — certains modèles refusent les function tools en raisonnant), `--m7-effort` (effort utilisé par la sonde thinking M7, défaut `low` — pour les modèles dont l'ensemble supporté l'exclut).

**`orkeon llm models`** — liste les modèles servis par un fournisseur. Options : `-p, --provider` (requis), `-u, --base-url`, `-k, --api-key-env`, `-f, --filter` (glob shell), `--json`.

```bash
ORKEON_LLM_API_KEY=... orkeon llm probe -p deepseek --modes M1,M2 --format json
orkeon llm models -p ollama --filter 'llama*'
```

## `orkeon rag`

Trois verbes sur le sous-système RAG (`ingest`, `search`, `eval`). Tous partagent les options d'hôte de `run` : `-s/--settings`, `-m/--mount`, `--allow-external-mounts`, `-v/--verbose`. Les sources relatives se résolvent contre un montage automatique `{cwd} → /workspace:ro` ; l'état atterrit dans `{cwd}/.orkeon → /output:rw`.

**`orkeon rag ingest`** — ingestion incrémentale (les sources inchangées sont sautées) : `-c, --collection` (requis), `--source <chemin|glob>` (requis ; plusieurs sources séparées par des espaces derrière un seul flag), `--chunking recursive|sentence|structural|semantic`, `--reindex` (réindexation complète — seule issue après un changement de modèle/dimension d'embedding).

**`orkeon rag search`** — pose une question, imprime la réponse fondée avec citations et scores : `<question>` positionnelle, `-c, --collection` (requis), `--top-n` (défaut 5).

**`orkeon rag eval`** — évalue une collection contre un jeu de données de référence (recall@k, MRR, groundedness) et écrit des rapports markdown/JSON : `-d, --dataset` (requis), `-c, --collection`, `--profile fast|balanced|quality|adaptive|corrective|default` (défaut `default` = le `Orkeon:Rag:Profile` configuré) ou `--compare fast,balanced,…`, `-k` (défaut 5), `--llm-judge`, `--offline` (zéro réseau : stub extractif déterministe, aucune clé LLM nécessaire), `--no-ingest`, `--reindex`, `--min-recall` / `--min-mrr` (portes anti-régression, sortie 1 sous le seuil), `--output` (défaut `/output/rag/eval`).

```bash
orkeon rag eval --dataset examples/rag/eval/golden.yaml \
  --compare fast,balanced,quality,corrective,adaptive --offline
```

## `orkeon doctor`

Diagnostic d'installation : dit en moins de 15 secondes ce qui fonctionne et ce qui manque, en table ✅/⚠️/❌ ou en `--json` (schéma stable `{check, status, detail}` pour la CI). Neuf vérifications : `dotnet-runtime`, `appsettings`, `llm-config`, `llm-reachability`, `esbuild`, `local-embeddings`, `onnx-reranker`, `tree-sitter`, `workspace-write`. Codes de sortie : `0` tout vert ou avertissements seuls, `1` au moins une vérification en échec.

```bash
orkeon doctor --json
```

## `orkeon-repl` — la console interactive séparée

`orkeon-repl` est un **outil distinct** construit depuis `src/apps/Orkeon.ConsoleApp` (commande dotnet tool `orkeon-repl`) : un REPL interactif complet pour piloter agents, crews et outils depuis une console Terminal.Gui à deux volets (logs + REPL), avec toute la pile du framework câblée — outils intégrés, RAG, analyse de code, embeddings locaux — et des commandes scriptées en TypeScript. Il ne partage volontairement pas le nom d'assembly `orkeon`. Voir [Commandes CLI en TypeScript](../architecture/cli-ts-commands.md).

## Les autres binaires livrés

Les archives de release portent plus de lanceurs que les deux documentés ici : `orkeon-slim`
(la même CLI, framework-dependent), **`orkeon-host`** (le daemon de service longue durée —
voir [le service host](../architecture/service-host.md)), les deux TUIs Studio
(`orkeon-studio-config`, `orkeon-studio-run`) et l'app desktop Windows (`orkeon-studio`)
— voir [Orkeon Studio](../architecture/studio.md). La liste est complète. La
[matrice de publication](./publication-matrix.md) et
[Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) listent
exactement quelle archive porte quoi.

---

> **Voir aussi** : [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Exécuter votre premier exemple](../getting-started/run-your-first-example.md) ·
> [Retour à l'index](../INDEX.md)
