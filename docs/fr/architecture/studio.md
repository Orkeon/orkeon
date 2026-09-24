> 🇬🇧 [English version](../../architecture/studio.md)

# Orkeon Studio

Orkeon Studio est la famille de front-ends graphiques et terminaux au-dessus des workflows du CLI `orkeon` : éditer et valider le `appsettings.json` — celui-là même qu'écrit `orkeon init` — puis choisir un crew et le lancer en exécutant le binaire `orkeon` co-installé. C'est un front-end, pas un second produit — tout ce que fait Studio peut se faire au terminal, et tout ce qu'il écrit est lisible par le CLI : on passe de l'un à l'autre à tout moment.

## Les quatre projets

Les quatre vivent sous `src/apps/` et aucun n'est publié sur NuGet (`IsPackable=false` dans chaque csproj — ils sont livrés par les installeurs de release).

| Projet | Cible | Sortie | Rôle |
|---|---|---|---|
| `Orkeon.Studio.Core` | `net10.0` | bibliothèque | Cœur agnostique de l'UI : tout ce que les écrans affichent vit ici. |
| `Orkeon.Studio.Config` | `net10.0` | exe (`orkeon-studio-config`) | Éditeur de settings Terminal.Gui v2 : formulaires de sections typés, éditeur de montages VFS, presets LLM, validation, `orkeon doctor`. |
| `Orkeon.Studio.Run` | `net10.0` | exe (`orkeon-studio-run`) | Lanceur de crews Terminal.Gui v2 : pointe le CLI `orkeon` co-installé sur un crew et diffuse sa sortie. |
| `Orkeon.Studio.Wpf` | `net10.0-windows` | WinExe, `AssemblyName=Orkeon.Studio` (`orkeon-studio`) | Application de bureau WPF combinant les deux workflows dans une fenêtre. |

### `Orkeon.Studio.Core` — le cœur partagé

Une bibliothèque sans UI et sans point d'entrée, consommée uniquement par les trois front-ends. Ses dossiers recouvrent les comportements :

- **Configuration/** — un modèle d'édition sans perte de `appsettings.json` (`AppSettingsDocument`) plus les sections typées (`LlmSection`, `LlmLoggingSection`, `LoggingSection`, `MountsSection`, `RagSection`, `RateLimitingSection`, et depuis STUDIO-21 `McpSection` et `ShellToolsSection`), `LlmProviderDetector`, et le catalogue des outils (`Tools/ToolCatalog` : les outils qu'une exécution expose, par famille, avec ce que chacun demande).
- **FileSystem/** — édition des montages : `MountDefinition`, `MountRights`, `MountValidator`, parcours de répertoires.
- **Presets/ & Llm/** — le catalogue de presets LLM (`LlmPresets`, `OrkeonCliDefaults`) et la sonde d'endpoint (`ILlmEndpointProbe`/`HttpLlmEndpointProbe`, `LlmApiKeyResolver`).
- **Targets/** — détection de la cible d'exécution (`RunTargetDetector`) : un `config.yaml`, un répertoire de crew multi-fichiers, ou un script `.ork.ts`.
- **Launch/ & Process/** — construction de la ligne de commande `orkeon run` (`RunArgumentsBuilder`, `RunLaunchOptions`), localisation du binaire (`OrkeonBinaryLocator` — dans l'ordre : l'argument `--cli-dir`, à côté de l'exécutable, la variable d'environnement `ORKEON_CLI_DIR`, le `PATH`, puis le checkout de développement), exécution et flux de sortie (`OrkeonProcessRunner`, `IProcessLauncher`), interprétation des codes de sortie (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), et le rapport `orkeon doctor` (`DoctorReport`).
- **Forge/** — le client typé d'`orkeon forge --events jsonl` (le moteur derrière l'assistant de création) : un parseur de lignes tolérant, épinglé contre les lignes d'or du protocole côté CLI, la projection de session que tous les fronts lisent (`ForgeSessionModel`, correspondance des jalons, les règles de la checklist ✔/✘), le pilote de processus enfant avec le canal de réponse stdin (`ForgeClient`, dont la requête de démarrage porte des surcharges d'environnement — c'est ainsi que le profil de l'assistant de Studio atteint le moteur), le catalogue de sessions sur disque et l'hydrateur de reprise. Le processus Studio ne touche jamais à un LLM — il ne voit que des lignes JSON.
- **Profiles/** — les réglages de modèle nommés du design v3 (`ModelProfile`, `ModelProfileSet`, `ModelProfileFileStore` → `studio-model-profiles.json` à côté du fichier de settings) : des « réglages de modèle » réutilisables, une élection par défaut reflétée dans la section `Llm`, le profil sur lequel tourne l'assistant de Studio, et des surcharges d'environnement `ORKEON_Llm__*` par profil pour les lancements (modèle, endpoint, température, délai et le budget de réponse `MaxTokens` — vide laisse le plafond au moteur, qui envoie le maximum documenté du modèle, et l'indication sous le champ dit ce que c'est pour le modèle choisi, ou que le modèle est inconnu du catalogue et reçoit 4096 sauf épinglage — LLM-10 ; l'interrupteur de réflexion — défaut du fournisseur / activée / désactivée — et l'indication d'effort de raisonnement, `ORKEON_Llm__Thinking__{Enabled,Effort}`, et un délai pré-rempli à 600 s quand le modèle par défaut du fournisseur choisi réfléchit avant de répondre — Kimi, DeepSeek, Z.AI, MiniMax — LLM-11). L'éditeur de profil offre le catalogue complet des fournisseurs (`LlmPresets.ProviderCatalogFor` — les deux runtimes locaux plus chaque cloud dont le framework livre un provider, endpoint/modèle pré-remplis depuis les défauts runtime épinglés par drift), et un novice colle sa clé d'API directement dans l'éditeur : elle atterrit dans une **variable d'environnement utilisateur** (`IApiKeyStore`/`EnvironmentApiKeyStore`, le nom conventionnel du fournisseur comme `DEEPSEEK_API_KEY`) — le fichier du store ne porte jamais que le *nom* de cette variable (`ModelProfile.KeyEnvName`), et les lancements posent la valeur résolue sur le processus enfant en `ORKEON_Llm__ApiKey`. La clé elle-même n'entre dans aucun fichier. Le store lit son fichier sans tenir compte de la casse des propriétés — il s'édite à la main, et `"profiles"` est ce que les gens tapent — et un fichier qui existe mais ne se lit pas est signalé sur l'écran des réglages au lieu de se charger comme un ensemble vide, indiscernable d'un premier lancement.
- **Teams/** — le dossier des équipes (`TeamCatalog`, défaut `~/Orkeon/teams`) : chaque équipe adoptée est un dossier ordinaire — listé, dupliqué, supprimé, importé (avec un scan des secrets en clair, et refusé avant toute copie quand le détecteur du lanceur ne le résout pas en une définition d'équipe, avec le message du détecteur) — plus le sidecar `studio-team.json` qui note ce que la définition de crew ne peut pas dire (nom, besoin, profil, planification affichée). Le profil noté n'est pas décoratif : lancer une équipe adoptée le résout dans le store de profils et le pose sur le run en `ORKEON_Llm__*`.
- **Run/** — le client typé d'un `orkeon run --events jsonl` **observé** (BUS-06) : `RunClient`, frère de `ForgeClient` et délibérément son jumeau — même lanceur, même localisateur, même parseur d'enveloppe — et `RunProgressModel`, qui plie le flux vers ce qu'un écran affiche (tâches en cours et chaque outil au travail, délégations en cours, tâches terminées, coût, question en attente — l'état complet est décrit sous *L'état de progression d'un run*, plus bas). Le client porte aussi le siège que le hub du run donne à un processus observateur : écrire à un agent, publier, s'abonner, répondre — et l'écran « Lancer » occupe désormais ce siège lui aussi : le `send` d'un agent (marqué `expectsReply`) apparaît comme un panneau de demande, et la réponse saisie repart par stdin. Voir [Le bus d'événements du run](run-event-bus.md).
- **UseCases/** — le client typé d'`orkeon usecases` (STUDIO-39) : `UseCaseClient`, jumeau de `ForgeClient` — `usecases list` exécuté jusqu'au bout pour le catalogue de la galerie, une session `usecases search` gardée ouverte pour les suggestions sous le besoin, chaque requête appariée à sa réponse par l'identifiant de corrélation, les échecs typés plutôt que levés — ainsi que les lecteurs du catalogue et d'une réponse (`UseCaseCatalog`, `UseCaseAnswer`), la règle qui décide des réponses assez proches pour être suggérées (`UseCaseSuggestions`), qui lit dans la réponse elle-même combien de cas d'usage portent un terme — Studio ne normalise jamais une orthographe de son côté.
- **Storage/ & History/** — emplacements des settings et chaîne de résolution (`SettingsLocations`, `AppSettingsFile`), historique des lancements (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — le port `IStudioStrings` (ci-dessous).

La liste de dépendances de Core est volontairement mince : seulement `Orkeon.Domain` (montages, `LlmDefaults`) et `Orkeon.Rag.Abstractions` (`RagProfilePresets`, la liste fermée des noms de profils RAG proposés par les UIs). Core étant référencé par trois front-ends self-contained, chaque dépendance transitive se paie trois fois sur disque — les références plus lourdes ont été supprimées, et les quelques constantes dupliquées sont épinglées contre les originales par des tests de dérive dans `Orkeon.Studio.Core.Tests`.

### Les front-ends

- **`orkeon-studio-config`** (TUI) — éditeur plein écran du fichier de settings : presets fournisseur, modèle et endpoint, logging, rate limiting, profil RAG, la table des montages VFS, une vue JSON brut, et un écran de diagnostic exécutant `orkeon doctor`. Rendu uniquement : chaque comportement vient de Core.
- **`orkeon-studio-run`** (TUI) — choisir une cible, régler les options d'exécution (dont `--validate` pour un dry run), suivre la sortie en direct, annuler au besoin. `--version` et `--help` sont répondus en mode headless avant l'initialisation de Terminal.Gui, ce qui garde les deux TUIs scriptables et vérifiables en CI.
- **`orkeon-studio`** (WPF, Windows) — une fenêtre de bureau au design v3 « volets » : une barre latérale en ordre de cycle de vie — **Équipes d'agents** (Créer une équipe, Mes équipes, Importer), **Travail** (Tester, Exécuter, Historique), **Environnement** (Réglages, Diagnostic — le rapport du doctor se copie en texte brut) — sous une bascule globale **Novice/Expert**. Novice explique chaque étape, montre l'aide contextuelle et cache la machinerie ; Expert montre tout : lignes de commande, JSON brut, journal technique, et l'écran Tester réservé aux experts. La fenêtre s'ouvre sur un écran de démarrage (Kama, la mascotte, clic pour passer), porte un panneau À propos, une visite guidée en cinq étapes, les thèmes clair/sombre et la bascule à chaud entre cinq langues ; elle tient un minimum de 1024×768 et chaque contrôle est stylé — aucun chrome Windows natif. Les écrans novices suivent de près la maquette v3 : **Exécuter** est une carte équipe (nom et méta issus du sidecar), une carte de progression en langage clair avec badge d'état et action « Ouvrir le résultat », et un journal technique replié par défaut ; **Historique** est une liste de cartes avec durée par exécution et phrase de résultat localisée ; **Diagnostic** s'ouvre sur une carte bilan nourrie par un premier doctor silencieux au démarrage, avec des noms de vérifications en clair ; **Réglages** s'ouvre sur le fichier utilisateur (`%APPDATA%\Orkeon\appsettings.json`, celui qu'écrit `orkeon init`) : les dossiers qu'il déclare, sa section `Llm` et son JSON brut sont à l'écran dès la première image ; il s'enregistre à chaque modification en novice (le cycle explicite Valider/Enregistrer est celui de l'expert), en réécrivant ce fichier avec ses autres clés intactes, et les dossiers autorisés sont une carte par montage, « Autoriser un dossier » ouvrant directement le sélecteur de dossier du système (STUDIO-19) ; l'onglet expert des limites montre la valeur par défaut du moteur dans chaque champ vide, en filigrane porté par le thème, et ses interrupteurs sont des booléens à un clic qui retirent leur clé quand on les remet à la valeur par défaut (STUDIO-22). Il ne référence que `Orkeon.Studio.Core`. Outre `--smoke-exit`, il accepte `--cli-dir <dir>` (nomme le dossier du CLI, prioritaire sur toute autre recherche) et `--capture-screens <dir>` : une campagne de captures sans intervention — la référence de remédiation de fidélité face à la maquette. Elle monte la fenêtre sur un **scénario amorcé** (trois équipes adoptées, sept exécutions passées, quatre sessions forge, quatre réglages de modèle, un doctor avec un avertissement et un échec, plus une machine de premier lancement vide et sans CLI), puis parcourt chaque écran et chaque état visuel distinct — les quatre étapes du wizard, les cinq modales, la visite guidée, l'assistant, les listes vides à côté des listes peuplées — dans les deux modes et **les deux thèmes**, plus un balayage des langues sur les écrans les plus denses. Environ 250 images sur huit passes, un PNG par arrêt sous `<langue>/<thème>/<mode>/`, au même chemin relatif dans chaque passe pour qu'en comparer deux soit un diff de répertoires, et un `manifest.json` portant la raison d'être de chaque image, son SHA-256 et le motif de chaque arrêt en échec. Le scénario vit dans un répertoire temporaire jetable : la campagne ne lit ni n'écrit jamais les équipes, l'historique, les réglages ou les préférences de l'opérateur. Sortie 0, ou 1 avec chaque arrêt fautif nommé sur stderr.

L'assistant de création est la doctrine en actes : « Créer une équipe » déroule Décrire ▸ Composer ▸ Essayer ▸ Adopter au-dessus d'`orkeon forge --events jsonl` lancé en processus enfant — la composition tourne avec `--dry` : le moteur génère et valide puis **s'arrête à l'étape Composer** ; l'essai est le clic « Essayer l'équipe » de l'utilisateur, qui reprend la session sans dry (une session rouverte depuis « Mes équipes » à cette pause retombe sur Composer de la même façon) — le stepper est une projection des jalons du moteur, les blocs « consigne + questions » de chaque étape voyagent par le canal ordinaire `user.message`, les boutons d'arbitrage sont générés depuis les options `decision.needed` du moteur lui-même, et l'adoption promeut directement dans le dossier des équipes avec la vraie grammaire de planification du moteur (à la demande, `daily@HH:mm`, `hourly`), puis rend l'assistant à une étape 1 vierge avec une ligne disant que l'équipe est dans Mes équipes (STUDIO-20). Une capacité absente du flux n'existe pas à l'écran — c'est exactement ce qui empêche le `orkeon forge` du terminal et l'assistant WPF de diverger. L'assistant est verrouillé tant que celui de Studio n'a pas de profil de modèle ; l'écran Réglages unifié (onglet Modèle d'IA avec les profils nommés, Dossiers autorisés, l'onglet Outils, et les onglets experts limites, MCP et fichier brut) est là où vit cette élection. Quand le clic échoue — pas de binaire `orkeon` sur la machine, une configuration que le moteur refuse, une sortie non nulle avec ou sans stderr, une promotion refusée à l'étape 4 — une carte sous la ligne de statut le dit dans la langue de l'utilisateur, garde le texte du moteur brut (enveloppé, borné, défilant, jamais traduit) et propose « Copier le rapport » (ligne de commande, code de sortie, erreur moteur, tout le stderr et le journal), « Réessayer » et, selon la famille, le diagnostic ou les réglages ; elle s'affiche dans les deux modes, s'efface à la composition suivante, et « Arrêter » n'en produit jamais.

### La galerie des cas d'usage (STUDIO-39)

L'étape 1 de l'assistant ne s'arrête plus à quatre exemples codés en dur. Ces quatre restent en tête, comme suggestions rapides, et un lien en dessous — « Parcourir les cas d'usage (105) », le nombre lu dans le catalogue — ouvre un panneau latéral par-dessus la fenêtre : tout le catalogue d'exemples que l'outil `orkeon` embarque (STUDIO-36 à 38), cherché à la frappe (chaque mot doit commencer un mot de la carte, accents et casse mis à part) et filtré par catégorie (neuf puces), par l'organisation de l'équipe (le processus, en mots simples), sans accès web, et sans clé tierce. Une carte porte le titre et le problème dans la langue de la fenêtre, un badge pour le processus, et « Référence seule » sur les cas finance dont les crews dépendent de fichiers que le CLI n'embarque pas : ils restent consultables et utilisables comme référence. L'expert voit aussi l'identifiant de chaque cas et l'orthographe moteur de son processus.

Choisir une carte écrit son problème dans le besoin, dans la langue de la fenêtre — le sélecteur de langue de Studio dit `zh` là où le catalogue dit `zh-Hans` — et attache le cas comme **référence** de la création : une puce « Inspiré de : <titre> » sous le besoin, dont la croix retire la référence et laisse les mots. La référence est un état de l'assistant (`CreateTeamViewModel.ReferenceUseCaseId`), effacé quand la création se termine (Recommencer, adoption, reprise, réouverture) ; STUDIO-40 la transmet au moteur sous la forme `forge --reference <id>`.

Pendant la frappe, l'assistant propose. Après une pause de 500 ms dans la zone du besoin, il interroge la session de recherche du CLI — `orkeon usecases search --events jsonl` en mode session : un seul processus pour toute la vie de la fenêtre, démarré par la première requête, son entrée standard fermée à la fermeture de la fenêtre — et affiche « N cas proches » sous la zone ; le lien ouvre la galerie sur ces cas, le meilleur en tête. Quelles réponses comptent, c'est une règle de Studio, mesurée sur le vrai catalogue : une correspondance compte quand elle partage avec le besoin au moins un terme **distinctif** — un terme rare, que contiennent au plus 3 % des cas d'usage (3 sur 105), et assez long pour être un mot du besoin plutôt que de la phrase : quatre caractères au moins dans une phrase, trois dans une recherche d'un ou deux mots-clés (`kyc`, `etl`), toute longueur en chinois, dont les termes sont des paires de caractères. Une correspondance par le sens seul ne compte jamais — en mode hybride, une requête absurde en obtient quand même cinq — et partager des termes ne suffit pas non plus : une phrase française ordinaire partage `de`, `un` ou `mes` avec presque toutes les fiches, et le catalogue contient `est` dans trois d'entre elles. Combien de cas d'usage portent un terme se lit dans la réponse elle-même : l'assistant demande à la session tout le catalogue (`top` = sa taille), et une réponse par termes liste chaque fiche qui partage un terme avec le besoin, avec les termes qu'elle partage, écrits par la normalisation du CLI lui-même — Studio ne normalise jamais une orthographe de son côté, et sa zone de recherche compare avec la collation de la plateforme, accents et casse mis à part. En anglais, cherché en mode hybride, la moitié « termes » de la fusion couvre les vingt meilleures fiches, là où se classent de toute façon les fiches d'un terme rare. Quand aucune correspondance ne se qualifie, rien ne s'affiche : pas d'indication plutôt que du bruit. Un besoin écrit par un cas choisi n'est pas recherché.

Il n'y a pas de repli côté Studio : le catalogue vient du CLI comme toutes les autres réponses de l'assistant, lu une fois au démarrage par le même exécuteur que le diagnostic et le lanceur, si bien que la galerie ne peut jamais montrer le catalogue d'un autre binaire que celui qui exécute les équipes. Sans CLI, le panneau affiche la carte « moteur introuvable » de l'assistant — les mots du localisateur, un nouvel essai, le chemin vers le diagnostic — et rien n'est suggéré. Une session terminée sans s'être jamais annoncée (un moteur antérieur à STUDIO-38) n'est pas rouverte à chaque touche ; un catalogue qui se recharge lève cette limite.

### L'écran « Lancer » n'est plus un terminal

C'était une liste de vingt mille lignes : honnête, et un terminal avec un thème. La personne qui lance une crew depuis Studio veut deux choses que le défilement ne donne pas — est-ce que ça avance, et est-ce que ça m'attend — donc l'écran observe désormais le run par le protocole que le wizard de création utilise déjà (`--events jsonl`, activé par défaut ; décocher l'option rend l'argv nu).

Ce qu'il montre : les tâches terminées avec leur agent, leur durée et leurs jetons ; une ligne de coût ; et **la question du run, posée à l'écran**. Auparavant, une tâche déclarée `humanInput: true` était approuvée dans le dos de l'utilisateur — un repli défendable pour un run non surveillé, et la mauvaise réponse dès qu'un écran regarde.

Le journal brut est **rétrogradé, pas supprimé**. Une ligne que le panneau ne sait pas lire y retombe plutôt que dans le vide, la règle que le lanceur en terminal suivait déjà.

Trois refus tiennent le panneau honnête, chacun épinglé par un test. Un run muet dit « rien de rapporté » plutôt qu'une progression implicite. Une question sans identifiant de corrélation n'est pas affichée comme en attente, car répondre exige une adresse. Et une réponse qui n'a pas pu partir laisse la question ouverte au lieu de prétendre qu'elle est arrivée.

La première version ne montrait que ce qui était **terminé**. Pendant une tâche de sept minutes, la carte gardait un badge « en cours » immobile et les lignes des tâches déjà faites, et rien ne distinguait un run qui travaille d'un run qui s'est figé — les mots du propriétaire : « on ne sait pas si le processus est en cours ». Le moteur annonce désormais chaque tâche à son départ (`task.started`, dans les six modes), et la carte porte une ligne pour **ce qui tourne maintenant** — son agent, un glyphe qui tourne, « depuis HH:mm:ss » à l'horloge du run — et, dessous, l'outil au travail, tiré de `tool.called` / `tool.returned`. Le badge pulse tant que le processus enfant vit et s'arrête à l'instant où il sort. Chaque ligne terminée dit aussi combien d'outils la tâche a appelés : une tâche censée écrire un fichier qui en rapporte zéro, c'est le diagnostic qu'un run vert cache. Deux choses plus petites que la même recette demandait : chaque ligne du journal commence par l'heure à laquelle Studio l'a lue, à l'écran comme dans le texte copié ; et chaque lancement repart d'un écran propre — le journal et le verdict du run précédent s'en vont, pour que rien à l'écran ne puisse passer pour le run qui va commencer. « Copier » est la façon dont un journal survit au clic suivant ; « validation à blanc d'abord » garde son essai à blanc et son vrai run dans un même journal, parce que la remise à neuf est par clic, pas par passe.

### L'état de progression d'un run (STUDIO-30)

`RunProgressModel` (Core, `Run/`) tient tout l'état vivant d'un run observé : un écran qui en a besoin — une barre d'état, par exemple — lit le modèle et jamais les lignes brutes. Chaque lanceur alimente le sien : l'onglet Lancer (`Launch.Progress`) et l'écran Tester (`Test.Launcher.Progress`, qui plie les mêmes événements ; sa propre vue ne les affiche pas, la barre d'état si — STUDIO-34). Le lanceur en terminal, `orkeon-studio-run`, n'utilise pas ce modèle.

| État | Lu dans |
|---|---|
| `ActiveTools` — chaque outil au travail, du plus ancien au plus récent, avec son nom et `StartedAt` (le `ts` de l'enveloppe). `ActiveToolName`, le plus récent d'entre eux, est celui que nomme la ligne d'activité de la carte Lancer | `tool.called`, clos par le `tool.returned` de même identifiant de corrélation |
| `ToolCallCount`, `SucceededToolCalls`, `FailedToolCalls` | `tool.called` ; le `success` de `tool.returned` |
| `RunningTasks` | `task.started`, jusqu'à son `task.completed` |
| `ActiveDelegations` — le rôle à qui le travail est confié, et depuis quand | `delegation.started`, jusqu'à son `tool.returned` |
| `SpawnedAgents` — rôle, raison, heure | `agent.spawned` |
| `IsWaitingForAnswer` — une question à un humain ou la requête d'un agent attend (`PendingQuestion`, `PendingAgentRequest`) | `input.needed` ; `hub.message` marqué `expectsReply` |
| `Cost` — ↑ `PromptTokens`, ↓ `CompletionTokens`, la paire de cache, `EstimatedTokens` (la part que le runtime a estimée), le `Amount` et la `Currency` du fournisseur lui-même, et le `Model` et le `Provider` des appels des agents | `cost.updated` (STUDIO-29) ; son `operation` dit à qui revient l'appel |
| `StartedAt`, `Elapsed` | `run.started` ; le `durationMs` de `run.finished` |
| `UnfinishedTools`, `UnfinishedDelegations` | les appels encore ouverts à `run.finished` |

Un retour clôt l'appel que nomme son identifiant de corrélation : des appels parallèles — deux du même outil compris — se closent dans l'ordre où ils reviennent. Une délégation est un appel d'outil par-dessous, mais le CLI la rapporte *à la place* de son `tool.called` et la clôt par le `tool.returned` de cet appel (il n'existe pas de `delegation.finished`) : elle est donc en cours jusque-là, et n'est pas comptée parmi les appels d'outils. Un agent engendré reste compté quoi que renvoie son appel de création : un spawn qui attend son agent rapporte l'échec de cet agent lui-même. `spawn_agent` n'est attaché à aucun agent par défaut, cette liste est donc le plus souvent vide.

La fin du run vide ce qui est au travail, et un appel encore ouvert à ce moment-là **n'est jamais revenu** : il passe dans `UnfinishedTools` (ou `UnfinishedDelegations`) et n'est jamais compté comme réussi. Chaque appel d'outil annoncé est à une seule place — au travail, réussi, échoué ou non terminé.

Ce qui n'a pas été mesuré reste absent. Pas de `cost.updated`, pas de `Cost` — même quand la clôture porte un total de tokens — et un champ que le compteur n'a pas porté reste nul, jamais un zéro. `Elapsed` est la seule lecture de l'horloge locale que fait le modèle : pendant le run, le temps écoulé depuis le départ que le run a horodaté, qui avance entre deux événements — un écran le rafraîchit donc à son propre rythme ; une fois la fin rapportée, le temps mur du run lui-même, figé. Pendant le run, un départ dont le `ts` ne se lit pas ne donne aucune durée plutôt qu'une durée devinée.

Une estimation se dit comme telle. Quand un fournisseur n'a rien compté pour un appel, le runtime l'estime, et `EstimatedTokens` (en direct) et `FinalEstimatedTokens` (à la clôture) disent quelle part du total est cette estimation — nuls tant que chaque appel a été compté. Comme chaque appel d'un run est au compteur (STUDIO-42), une lecture peut aussi venir d'un juge, d'un pipeline RAG, du manager ou du planificateur : une telle lecture fait bouger le compteur et laisse `Model` et `Provider`, qui nomment le modèle sur lequel travaillent **les agents** — le tour d'un agent (`operation: agent`), ou l'appel `ctx.llm.*` d'un script, qui nomme sa méthode. Une lecture qui ne nomme aucun type de travail les laisse aussi : rien ne dit qu'un agent a fait l'appel. Tant que l'appel d'aucun agent n'a répondu, le compteur peut bouger sans qu'aucun modèle soit nommé.

`Changed` se déclenche une fois par événement qui a fait bouger l'état et en nomme le type (`RunProgressChangedEventArgs.Kind` ; nul pour une réponse, à une question ou à un agent, acceptée de ce côté-ci), pour qu'un écran qui n'affiche pas le texte généré puisse ignorer les `llm.delta`, un par token.

### La barre d'état (STUDIO-34)

Une troisième ligne, au pied de la fenêtre, dit ce qui tourne, ce que ça consomme et quels outils sont au travail, sans changer d'écran. `StatusBarViewModel` (`ViewModels/Shell/`) l'alimente ; il ne contient aucune logique de vue, et tout son comportement est vérifié dans `Orkeon.Studio.Wpf.Tests`.

Trois activités peuvent tourner en même temps, chacune avec son moteur, et chacune a **son propre groupe** pendant qu'elle tourne — rien n'est caché, rien n'est fusionné (DD-2) :

| Groupe | Sur la barre tant que | Lu dans |
|---|---|---|
| **Exécuter** | le processus de l'écran Exécuter vit (`Launch.IsRunning`) | `Launch.Progress.Model`, le `RunProgressModel` du run (STUDIO-30) |
| **Tester** | le lanceur propre à l'écran Tester tourne (`Test.Launcher.IsRunning`) | `Test.Launcher.Progress.Model` |
| **Créer une équipe** — l'assistant | le moteur forge vit, au travail ou en attente de l'utilisateur | la carte de progression de l'assistant, `CreateTeam.Progress` |

Le groupe d'un lanceur dit l'équipe (lue au départ du run, et gardée si le lanceur est pointé entre-temps sur une autre équipe), l'état — en cours, **en attente d'une réponse**, puis réussi ou échoué entre la fin que le run rapporte et la sortie du processus —, la tâche en cours, la durée écoulée, ↑ et ↓ (marqués « ≈ » quand le runtime en a estimé une part), la puce de cache, le coût réel facturé par le fournisseur quand il y en a un, les outils au travail (leur nombre et le premier nom ; la liste entière, chacun avec l'heure du run à son appel, au survol), les délégations en cours, et le fournisseur et le modèle **des appels des agents**, tels que le compteur les rapporte — la lecture d'un juge, d'un pipeline RAG ou du manager ne les renomme jamais. Pendant un run, la barre ne nomme jamais un profil supposé : chaque équipe choisit le sien. Le groupe de l'assistant dit l'étape dans les mots mêmes de la carte, ↑ et ↓ (marqués « ≈ » quand ils sont estimés), et ce qu'il reste de l'allocation de jetons de la session (`budgetRemaining`, porté par la carte sous le nom `TokensRemaining`). Au repos, la barre montre le fournisseur et le modèle du profil **par défaut** — il n'existe pas de profil « actif ».

Un segment que rien n'a mesuré est absent, jamais un zéro. Le novice lit l'état et les compteurs de chaque groupe, et le Solde ; l'expert lit tout, les autres segments d'un lanceur formant une ligne que la barre tronque sur une fenêtre étroite et montre en entier au survol (D-03). Les groupes actifs se partagent la largeur : une fenêtre étroite tronque leur ligne experte et ne pousse jamais un groupe hors de la barre. Un clic sur un groupe ouvre l'écran de son activité — Exécuter, Tester ou Créer une équipe ; en mode novice, qui n'a pas d'écran Tester, le groupe Tester ne répond pas au clic (D-04). Les runs lancés hors de Studio sont hors périmètre (D-05).

La durée écoulée avance entre deux événements : la barre la rafraîchit donc à son propre rythme, une seconde — `IUiTicker` : un `DispatcherTimer` dans l'application, un rythme qui ne bat jamais dans les tests ni dans la campagne de captures —, tenu seulement tant qu'un groupe de lanceur est sur la barre. La campagne fige l'horloge sur laquelle la durée est lue (`StudioServices.Clock`), et sa capture du run en cours arrête le run scripté avant la ligne qui en rapporte la fin : la capture est bien celle d'un run encore en cours.

Le segment **Solde**, à droite, est un emplacement réservé, `StatusBar.Balance` : STUDIO-35 le remplit, et rien ici n'interroge un fournisseur.

### Les dossiers d'équipe de bout en bout (remédiation v2)

Les dossiers d'une équipe adoptée font partie de l'équipe : le sidecar
`studio-team.json` les enregistre en mount-strings (`mounts`), à côté du nom,
du réglage et de la programmation. Les cartes de « Mes équipes » les montrent en
chips ; « Changer les dossiers » les édite dans la modale des dossiers d'équipe.
Une équipe associe un dossier que les réglages déclarent : « Autoriser un autre
dossier… » sur une équipe adoptée, et le bloc « Dossiers de cette équipe » du
wizard sous la politique « Plus tard », ouvrent le sélecteur « Ajouter un dossier
autorisé », une liste à cocher des dossiers tenus dans « Réglages › Dossiers
autorisés » (`Orkeon:FileSystem:Mounts`). Les entrées choisies sont reportées
telles quelles, **droits compris** : les réglages sont le seul endroit où un
dossier et ses droits se décident, et une équipe capable de les élargir ferait de
cette déclaration une suggestion. Une ligne que l'équipe porte déjà, ou dont la
racine virtuelle est déjà prise par un autre dossier, le dit et ne peut pas être
choisie — la liste d'une équipe, comme celle des réglages, nomme chaque racine une
seule fois. Le seul geste qui déclare depuis le wizard est le choix disque de
« Des dossiers existants » ci-dessous, et il déclare dans les réglages au passage
— les réglages restent la source des droits.

**Une équipe est un dossier qu'on emporte.** Le sidecar enregistre les dossiers
propres de l'équipe relativement à elle : un segment physique qui commence par
`./` — `./input:/workspace:ro`, `./output:/output:rw`, `./rapports:/rapports:rw` —
nomme un dossier dans le dossier de l'équipe ; un seul segment, `/` sur les deux
OS, jamais `..`, jamais cité. Une entrée absolue est un dossier de l'utilisateur,
hors équipe, et reste ce qu'elle est ; une entrée illisible passe inchangée, dans
les deux sens. Le runtime résout un chemin physique relatif contre le cwd du
processus et rien d'autre, aussi `TeamCatalog` est-il le seul endroit qui connaît
la convention (`TeamMountPaths`, dans Core, en est l'unique aide) : il résout les
entrées relatives en chemins absolus quand il décrit une équipe (`Describe`,
`DescribeTarget`, donc `List` — le brut reste sur `Metadata.Mounts`), et relativise
à l'entrée (`SaveMetadata`, `SaveMounts`), en créant chaque dossier relatif à ce
moment — l'unique point où un dossier d'équipe est matérialisé, à l'adoption comme
à chaque modification ultérieure. Les cartes, le lanceur et la modale des dossiers
reçoivent des chemins absolus comme avant et n'en savent rien. Une duplication, un
export et un import copient les entrées telles quelles et les résolvent sous la
copie ; un ancien sidecar qui portait des chemins absolus sous son propre dossier
est réécrit relatif au passage — plus d'étape de « rebase », parce qu'une copie est
une sauvegarde, pas une couche de compatibilité. `DeclaredMounts.IsInsideTeam`
répond de toute entrée `./` avant même que le dossier de l'équipe existe (relatif
*est* dans l'équipe, par construction), et `MountValidator` saute le test
d'existence d'une telle entrée tant qu'on ne lui dit pas sous quel dossier d'équipe
regarder.

**Où vivent les dossiers se demande à l'étape 1.** Une quatrième question,
« Où sont vos dossiers ? », répond aux deux racines qu'une équipe peut adresser
avant d'avoir un blueprint — `/workspace` en lecture (« Vos documents ») et
`/output` en écriture (« Les résultats »), les seules que `DeriveMounts` puisse
produire sans lui — et composer n'attend jamais cette réponse (`FolderPolicy`,
`Later` par défaut). « Des dossiers existants » affiche les deux lignes ;
« Choisir le dossier… » sur l'une ou l'autre ouvre le **sélecteur disque** sur
les droits de la ligne (lecture seule pour les documents, lecture et écriture
pour les résultats), et le dossier choisi est déclaré dans « Réglages › Dossiers
autorisés » sauf si les réglages le tiennent déjà, sauvegardé, puis lié derrière
la ligne avec les droits de la ligne — un seul geste, la ligne d'état dit
laquelle des deux choses s'est produite, et une sauvegarde refusée lie quand même
(`MainWindowViewModel.DeclareAndBindAsync`). Un dossier choisi dans l'équipe
rouverte elle-même est lié et jamais déclaré : c'est le sien, et la sauvegarde le
relativise. « Créés dans l'équipe » répond aux deux lignes relativement à
l'équipe, sur-le-champ — `./input:/workspace:ro`, `./output:/output:rw`, lues
« dans l'équipe : input / output » — et rien n'est créé sur le disque avant
l'adoption. « Plus tard » se comporte comme avant : pas de lignes, l'étape
Composer demande. La puce déplace les deux racines canoniques et rien d'autre ;
composer les garde (`KeepOnlyStepOneMounts` — le reste appartenait au blueprint
remplacé), « Recommencer », une reprise et « Modifier » sur une carte les
oublient, politique comprise, pour qu'un choix d'étape 1 périmé ne fuie jamais
dans le sidecar d'une autre équipe.
Les deux lignes sont un point de départ, pas une limite (relecture du propriétaire
du 2026-09-19) : « Ajouter le dossier » nomme autant de points de montage que le
besoin en appelle — un nom que les agents utiliseront (`/factures`, `/archives`),
en lecture ou en écriture — et chacun devient une ligne comme les canoniques,
répondue des deux mêmes façons (« Créés dans l'équipe » répond sur-le-champ à une
nouvelle), listée à l'étape Composer à côté des racines du blueprint, conservée
d'une composition à l'autre, et créée dans l'équipe à l'adoption si elle reste
sans réponse. Lesquelles les agents adressent est l'affaire du blueprint : le
besoin doit les nommer. Un nom est normalisé comme le sélecteur le dérive d'un
dossier (minuscules, un seul segment) ; un nom que le runner réserve ou qu'une
ligne porte déjà ne peut pas être ajouté.

Le bloc du wizard à l'étape Composer est **une ligne par point de montage**
(lot 3) : le nom que les agents adressent, qui l'adresse — provenance, jamais
permission — et le dossier derrière, ou deux boutons quand il n'y en a pas
encore. « Choisir le dossier… » ouvre le sélecteur **ciblé sur ce chemin
virtuel** (le sélecteur disque sous « Des dossiers existants ») : l'entrée retenue
garde son dossier et ses droits, et seul le nom que les agents lui donnent
revient à l'équipe. Une ouverture ciblée prend UN dossier et juge ses lignes là
où le choix va **atterrir**, pas sur la racine que les réglages ont déclarée —
sans quoi chaque dossier déjà employé ailleurs se refuserait lui-même. « Créer
dans l'équipe » est l'autre réponse, par ligne, et « Créer tous les dossiers dans
l'équipe » répond d'un coup à toutes les lignes sans réponse ; sous la politique
« Créés dans l'équipe », une racine qu'un blueprint ultérieur ajoute —
`/rapports` à côté de `/output`, que l'étape 1 ne pouvait pas deviner — reçoit la
même réponse dès qu'elle apparaît (`AnswerNewDerivedRoots`), sauf si
l'utilisateur l'a retirée. Une ligne dans l'équipe montre le libellé, jamais un
chemin disque, et **ne lit jamais rouge** : `DeclaredMounts.IsVouchedFor` —
déclaré dans les réglages, ou propre à l'équipe — est la règle du wizard, des
cartes « Mes équipes » et du lanceur à la fois. Sans ces gestes, une racine
impliquée par les agents ne pouvait être répondue qu'à l'adoption, par un dossier
créé dans l'équipe et laissé vide : c'est pourquoi la carte dit aussi, tant qu'il
est temps, qu'une équipe qui lit sans dossier choisi — ou avec une réponse « dans
l'équipe », dont l'`input/` est créé tout aussi vide — recevra son propre
`input/` vide et que rien n'y copiera vos documents.

**L'essai lit là où sont les documents.** `orkeon forge … --read <dir>` monte
`<dir>` en `/workspace`, en lecture seule, à la place du dossier de travail et ne
change rien d'autre — la session reste sous le forge home, les réglages se
résolvent toujours à côté. Le wizard passe le dossier lié derrière `/workspace` à
chaque invocation du moteur (`ForgeStartRequest.ReadDirectory`, les sept sites ;
la promotion garde le workspace seul) : un dossier réel tel quel, une réponse
relative à l'équipe résolue sous le dossier de l'équipe dès qu'il existe — une
équipe rouverte ré-essaie sur son propre `input/`, rempli depuis — et rien avant
que l'équipe existe, où l'argv est exactement celui d'avant l'option et où
l'étape 3 dit que l'essai tourne sur un dossier vide. Un moteur antérieur à
`--read` ne le rencontre que quand un dossier est connu, et le refuse alors à
voix haute sur la carte d'échec.

**Les dossiers propres d'une équipe sont autorisés du fait qu'ils vivent dans
l'équipe, et ne sont jamais écrits dans les réglages globaux.** Deux listes
d'autorisation, c'est une de trop : déclarer l'`output/` d'une équipe dans
`Orkeon:FileSystem:Mounts` dupliquerait le sidecar dans un fichier partagé entre
toutes les équipes, et mettrait les dossiers privés d'une équipe dans la liste où
toutes les autres piochent. La règle est donc implicite —
`DeclaredMounts.IsVouchedFor` : déclaré dans les réglages, *ou* dans l'équipe — et
c'est celle que le lanceur applique (`BlockingFolders` ne compte jamais le `/output`
ou le `/workspace` propre d'une équipe). L'écran des réglages doit pourtant montrer
ces dossiers, sans quoi le seul écran qui prétend lister ce que les agents peuvent
voir ignorerait les dossiers dans lesquels toute équipe adoptée écrit. Aussi
« Réglages › Dossiers autorisés » se termine-t-il, dans les deux modes, par une
section « Dossiers des équipes » en lecture seule (`TeamFoldersViewModel`) : une
ligne par dossier d'équipe de chaque équipe adoptée — « Veille concurrentielle ·
/output → output » avec le badge de droits en un mot — lue dans les entrées brutes
des sidecars, les `./` comme la graphie absolue sous l'équipe d'un ancien sidecar,
jamais un dossier hors équipe, jamais un chemin disque. Aucune commande, aucune
croix, rien d'écrit : son aide dit que ces dossiers appartiennent à leurs équipes et
se changent depuis « Mes équipes » › « Changer les dossiers ». La section suit la
liste des équipes — une adoption, un import, une suppression ou une duplication
depuis une carte, un enregistrement de la modale des dossiers finissent tous par
reconstruire les cartes, et elle relit les sidecars sur ce signal puis à chaque
ouverture de l'onglet des dossiers — de sorte qu'elle ne montre jamais une équipe
disparue, ni n'oublie une équipe adoptée il y a une minute.

Déclarer reste le geste des réglages, et « Déclarer un nouveau dossier… » est
une porte vers eux : le sélecteur se ferme et l'écran bascule sur
« Réglages › Dossiers autorisés », sur cet onglet et pas seulement sur cet
écran. Une seule porte, pour qu'un dossier ne puisse pas être déclaré à deux
endroits et diverger entre eux. Il n'y a plus de sélecteur intégré
(STUDIO-19) : « Autoriser un dossier » sur la carte novice des réglages ouvre
le sélecteur de dossier du système, et le dossier choisi est monté en lecture
seule sous un nom virtuel dérivé de son propre nom
(`MountDefinition.SuggestVirtualPath`, première suggestion libre si ce nom est
pris ou inutilisable) — le badge de droits de la carte le bascule en lecture et
écriture. Les lignes « Des dossiers existants » du wizard ouvrent le même
sélecteur avec les droits de la ligne et déclarent le dossier choisi **sous la
racine de la ligne**, avec un identifiant à lui, puis relient cette entrée même
(VFS-90, D-01) — une seconde entrée sur une racine qu'un autre dossier occupe
déjà, distinguée par son identifiant, jamais un renommage en `/docs` ; un dossier
supplémentaire en écriture à l'étape Composer passe par une racine nommée.

Un dossier d'équipe dont rien ne répond — ni déclaré dans les réglages, ni
propre à l'équipe — apparaît en rouge, sur les lignes du wizard, les cartes de
« Mes équipes » et la modale des dossiers d'équipe. Le `./input` et le `./output`
d'une équipe sont les siens : créés dans l'équipe à l'adoption, jamais déclarés,
jamais rouges. Le rouge est la seule chose qu'une ligne ne peut pas dire en
nommant un chemin virtuel, et une équipe qui sort des dossiers autorisés de la
machine ne devrait pas se découvrir en lisant un sidecar. Déclarer un dossier dans
les réglages le sort du rouge aussitôt : les cartes et l'écran Exécuter recalculent
leurs verdicts à chaque modification de la liste déclarée, et la fenêtre lit cette
liste dans le fichier utilisateur avant de les construire (STUDIO-18).

Une équipe qui sort des réglages ne se lance pas. « Exécuter » refuse une équipe
portant un dossier qu'aucune entrée des réglages n'autorise : le bouton de
lancement est désactivé et la carte nomme les dossiers ainsi que les deux
issues (les déclarer, ou les retirer de l'équipe), avec un bouton vers
« Réglages › Dossiers autorisés ». Découvrir ce refus par un run qui échoue à
mi-course, sa raison enfouie dans un journal, est le résultat que cela remplace.
La règle vit dans `Orkeon.Studio.Core` (`DeclaredMounts.BlockingFolders`) et non
dans les écrans WPF, pour que le lanceur TUI ne puisse pas y répondre
autrement — et le `/output` et le `/input` d'une équipe ne la bloquent jamais :
c'est sa plomberie, et les compter rendrait toute équipe adoptée impossible à
lancer.

Les dossiers déduits du blueprint sont supprimables comme les autres. C'étaient
des puces informatives sans croix — « modifiez un agent pour les changer » — ce
qui laissait une équipe porter une racine dont son propriétaire ne voulait pas,
sans moyen de le dire. Une suppression tient désormais : `SidecarMounts` ne la
réajoute plus, exactement l'annulation silencieuse que cette méthode existe pour
empêcher. L'écran avertit et nomme les racines abandonnées, parce que rien ne
leur sera associé et que les agents qui y écrivent échoueront ; un « Rétablir »
unique est le chemin de retour après une croix de trop. Ce que `SidecarMounts`
enregistre est relatif à l'équipe pour chaque réponse « dans l'équipe » et chaque
racine que le blueprint adresse sans réponse (`./output:/output:rw`,
`./input:/workspace:ro`) ; la sauvegarde crée les dossiers. Au lancement, le catalogue lit le sidecar
face aux réglages (`TeamMountResolution.Resolve`) et `LaunchMountPlan.For` dit ce
qui atteint la ligne de commande : une déclaration des réglages que l'équipe nomme
part en `--mount-id <ulid>` — sans chemin, l'entrée de la machine telle qu'elle
est aujourd'hui ; les dossiers propres à l'équipe et toute copie que les réglages
ne tiennent pas telle qu'enregistrée partent en `--mount`, devant ceux du
lancement, pour que les chips et la commande ne puissent pas diverger ; un
identifiant que cette machine ne déclare pas bloque le lancement (ci-dessous). Le
drapeau `--allow-external-mounts` suit lui aussi le sidecar : un dossier d'équipe
hors de l'équipe l'allume, une équipe dont tous les dossiers se résolvent sous
elle — le dossier de travail du lancement — n'en a pas besoin, et la case expert
reste pour les montages du lancement. Le moteur place chaque `--mount` **par
racine virtuelle** (`RunnerHost`) : un dossier d'équipe sous un nom que les
réglages dépensent pour un autre dossier remplace toutes les entrées des réglages
de cette racine pour le run, et un dossier sous un nom neuf s'ajoute après les
entrées déclarées. La table des montages effectifs de l'écran Exécuter prédit
exactement cela — une ligne par entrée des réglages, origine *appsettings* pour ce
que les réglages fournissent, `--mount (remplace « … »)` pour un remplacement,
*sélectionné par identifiant parmi N* / *non monté pour cette exécution* pour les
entrées d'une racine partagée — et la phrase au-dessus énonce la règle
(`MountOverrideSemantics`). Avant cela, la copie conforme du sélecteur rencontrait
sa jumelle des réglages et toute équipe adoptée utilisant un dossier autorisé
échouait au démarrage sur « Duplicate virtual paths » ; les dossiers déclarés dans
les réglages sont aussi mis en liste blanche pour `PathValidator` sans aucun
drapeau, si bien qu'une équipe lisant un dossier autorisé hors de son propre
dossier n'est plus refusée fichier par fichier. Limite assumée : un `orkeon run`
nu en terminal ne lit pas le sidecar — c'est le bloc `mounts:` de la crew qu'il
lit (VFS-90) ; comme le champ `profile`, le sidecar est le confort de Studio, pas
le contrat du moteur.

### Un montage a une identité (VFS-90)

Chaque entrée de « Réglages › Dossiers autorisés » porte un **identifiant** —
l'ULID de 26 caractères devant le `|` de sa chaîne de montage,
`01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data\out:/output:rw` — attribué par l'éditeur au
chargement à une entrée qui n'en a pas et écrit à l'enregistrement suivant,
affiché sur la ligne avec une copie en un clic pour le bloc `mounts:` d'une crew
écrite à la main. L'identifiant est ce par quoi le sidecar d'une équipe nomme ses
déclarations, et c'est ce qui permet désormais à deux entrées de déclarer une même
racine : la machine du propriétaire garde côte à côte le `…\09\output:/output:rw`
de l'expérience 09 et le `…\10\output:/output:rw` de l'expérience 10, et chaque
équipe nomme le sien. Les conséquences, écran par écran :

- **L'entrée fait foi** (D-01). Le sélecteur ouvert pour un point de montage
  n'offre que les entrées déclarées sous cette racine — une entrée déclarée comme
  `/docs` lit « déclaré comme /docs — ce point de montage est /output » et ne peut
  pas être choisie — et enregistre le choix tel quel, identifiant compris. Le
  sélecteur disque déclare le dossier **sous la racine de la ligne** (une seconde
  entrée `/output` quand une existe) et relie cette entrée ; une entrée égale —
  même dossier, racine et droits — est réutilisée, jamais déclarée deux fois. Le
  renommage en `/docs` pour esquiver une racine prise disparaît avec.
- **Le sidecar garde une copie** (D-02) : `<ulid>|<dossier>:/racine:droits`,
  portable, l'identifiant l'emportant sur la copie sur la machine qui l'a. Le
  `./output` propre à une équipe ne porte pas d'identifiant (D-07). Un sidecar
  écrit avant les identifiants résout ses copies par dossier, racine et droits, et
  « Changer les dossiers » met à niveau une copie exacte vers l'entrée elle-même à
  l'enregistrement.
- **Les identifiants voyagent avec l'équipe** (D-06). « Dupliquer », « Exporter »
  et « Importer » les copient tels quels. Une équipe nommant une déclaration que
  cette machine n'a pas — un import, ou une entrée retirée depuis — lit « la
  déclaration … manque sur cette machine » sur ses lignes et dans l'onglet des
  dossiers, et « Exécuter » la refuse en nommant les identifiants ; la revue
  d'import propose « Les autoriser tels qu'enregistrés », qui déclare les copies
  **sous les mêmes identifiants** (un nouvel identifiant seulement quand cette
  machine le dépense déjà pour autre chose).
- **Les réglages disent qui dépend d'une entrée** : chaque ligne lit « Utilisé
  par … » depuis les sidecars, et retirer une entrée qu'une équipe nomme demande
  d'abord, en nommant les équipes — elles cessent de démarrer dès qu'elle
  disparaît. `MountValidator` classe deux entrées sur une racine en information
  (`STUDIO-MOUNT-SHARED`), une racine partagée avec une entrée sans identifiant en
  l'erreur que le moteur lèverait, et un identifiant sur deux entrées en
  `STUDIO-MOUNT-ID`.
- **Les agents ne voient jamais un identifiant** : `list_mounts`, la table de
  montages du prompt et les messages de refus d'accès ne nomment que des chemins
  virtuels.

Un dossier d'équipe peut contenir une **définition mono-fichier**. Chaque exemple
d'`examples/` — et chaque définition écrite à la main — est un seul `config.yaml`
ou `crew.yaml` portant `agents:` et `tasks:` en ligne, pas le layout promu
`crew/config.yaml` + `crew/agents/*.yaml` + `crew/tasks/*.yaml` qu'écrit
`forge promote`. Le détecteur de cible (`RunTargetDetector`) résout un dossier sans
marqueur de layout ni script mais avec un seul fichier `*.yaml`/`*.yml` comme ce
fichier : le chemin exécuté est le fichier, le chemin sélectionné reste le dossier,
donc le sidecar à côté et le dossier de lancement fonctionnent exactement comme
pour une équipe promue, à la racine du dossier ou sous son sous-dossier `crew/`
indifféremment. Plusieurs fichiers YAML se résolvent en `crew.yaml`, puis
`config.yaml`, et sont sinon proposés comme candidats, comme les scripts. C'est ce
qui rend tout le catalogue d'exemples importable : « Importer » exécute ce
détecteur sur la source avant de copier quoi que ce soit, et un dossier qu'il ne
résout pas est refusé avec le message du détecteur dans la ligne d'état, au lieu
d'atterrir dans « Mes équipes » comme une carte que rien ne peut lancer.

**« Ouvrir le dossier », à toute étape, dans les deux modes.** L'en-tête du
wizard l'offre dès que le moteur a répondu : la session de travail — qui contient
le `crew/` généré — avant l'adoption, l'équipe adoptée (ou rouverte) ensuite, et
l'infobulle dit laquelle. Même port `IShellOpener` que les cartes d'équipe,
gardé de la même façon : pas d'ouvreur câblé, pas de bouton.

### Le blueprint, édité à la main

L'arbitrage `edit` du protocole forge est réel : le mode interactif arbitre
chaque verdict (un verdict conforme coûte un clic « accepter »), et
`decision.made {edit}` suivi de `blueprint.edited {blueprint}` re-rend l'équipe
de façon déterministe — zéro jeton LLM — puis regagne son verdict par le chemin
inchangé validate/test/diagnose ; le moteur revalide tout ce qu'il reçoit
(parse, compilation, catalogue d'outils) et répond à une édition invalide par un
`FORGE-BLUEPRINT-INVALID` récupérable, en rouvrant l'arbitrage. Côté Studio,
l'étape Composer affiche une carte par agent du blueprint avec « Modifier » qui
ouvre l'éditeur d'agent — le nom correspond au `role` du blueprint, « ce qu'il
fait » à son `goal`, les chips de capacités à ses `tools` ; « Retirer de
l'équipe » et « Ajouter un agent » empruntent le même chemin. Les boutons sont
actionnables aux deux points d'édition du moteur : pendant qu'il attend à son
arbitrage (le canal vivant — décision `edit`, puis le blueprint amendé), et à la
pause `--dry` de l'étape Composer, où le moteur est éteint — là, l'application
est un processus enfant `forge resume --edit --dry` qui porte le blueprint
amendé en première ligne stdin : le moteur le valide intégralement, re-rend de
façon déterministe (zéro jeton LLM, même itération) et se remet en pause à la
même frontière, si bien que le Composer se repeint avec l'équipe amendée.
Pendant que l'assistant compose ou qu'un essai tourne, les boutons attendent
avec le moteur.

La même pause « dry » porte une seconde réponse à côté d'« Essayer l'équipe » :
« Adopter sans essayer » (`forge resume --adopt`), qui fait passer la session
directement à Ready — hors ligne, sans dossier d'exécution, zéro jeton. `Ready`
n'avait qu'un seul prédécesseur, un verdict accepté : garder l'équipe telle
qu'elle avait été générée obligeait donc à subir une exécution que rien en aval
ne consomme — `verdict.json` est facultatif à la promotion et le `FORGE.md`
généré sait déjà écrire « aucun verdict enregistré ». Ce qu'un essai achète,
ce sont des *preuves*, pas une permission : l'infobulle du bouton le dit
exactement, et la transition reçoit son propre déclencheur (`TrialSkipped`)
pour que l'historique de la session ne se lise jamais comme un verdict qui n'a
jamais été gagné.

Quel moteur a répondu est également à l'écran, à côté du nom de l'assistant
(«&nbsp;moteur 1.0.0-rc.2&nbsp;», depuis `session.started`). Studio n'embarque
pas le CLI — il lance le premier `orkeon` que son localisateur trouve :
co-installé, sur le `PATH`, ou construit depuis le dépôt — de sorte que sans
cette ligne, une session pilotée par un binaire périmé est indiscernable d'une
session qui fonctionne et n'a simplement rien à rapporter.

### Ce qu'un run a coûté, à l'écran (remédiation v3)

Partout où un essai ou une exécution se termine, Studio affiche ce que cela a
coûté — en chips mono, une seule recette (`UsageMetricsFormatter`) : le total de
tokens, le cache de prompt (`cache 62 % · 7 980 tokens` — la paire hit/miss est
une *partition* des tokens de prompt, jamais une addition), et le temps mur. La
carte verdict du wizard lit le `verdict.ready` enrichi (les chiffres du dernier
essai lui-même, distincts du compteur cumulatif `cost.updated`) ; la ligne de
fin d'Exécuter lit le `run.finished` enrichi ; l'historique enregistre tokens et
paire de cache par entrée (schéma tolérant — les anciens fichiers se chargent)
et les montre dans la ligne méta. Une métrique non mesurée ne produit **aucune
chip** — jamais un zéro.

### Modifier, ré-essayer, ré-adopter (remédiation v3)

L'adoption n'est plus une porte à sens unique. « Modifier » sur une carte
d'équipe rouvre le wizard à l'étape Composer avec tout le stepper accessible : le
moteur reprend la session de l'équipe dans un arbitrage rouvert (le verdict stocké
est ré-annoncé d'abord), les agents sont
donc à nouveau éditables, une nouvelle décision `retry` re-exécute l'essai tel
quel (zéro jeton de composition, une itération de budget), et la ré-adoption
**met à jour le même dossier d'équipe** — les fichiers générés (`crew/`,
lanceurs, `FORGE.md`, `schedule/`) sont régénérés, le sidecar et les fichiers de
l'utilisateur survivent, renommer l'équipe ne change que son nom d'affichage.
Après une adoption, l'assistant est de nouveau une étape 1 vierge (STUDIO-20) :
modifier une équipe adoptée passe par « Modifier » sur sa carte, et l'arbitrage
rouvert propose `retry`. La session à laquelle une équipe est liée est la réponse
du moteur, jamais celle de Studio (STUDIO-25) : « Modifier » lance toujours
`forge reopen <dossier-equipe>`. Une session porte un identifiant stable, annoncé
par `session.started` et recopié par sa promotion dans le `forge.json` de l'équipe,
et une seule règle décide du lien — la règle R,
`Orkeon.Domain.FileSystem.TeamSessionLink`, la même pour le CLI et Studio : la
session qui porte l'identifiant du dossier lui est liée quand son `promotedTo`
désigne ce dossier, ou quand le dossier qu'il désigne a disparu ou ne porte plus
l'identifiant — l'équipe a été déplacée ou renommée, et la session la suit ; quand
`promotedTo` désigne un autre dossier existant qui porte le même identifiant, ce
dossier-ci est une copie, liée à rien. Aucun chemin ne décide seul du lien. Une
équipe à laquelle aucune session n'est liée — importée, session supprimée,
dupliquée, ou sans identifiant — se modifie aussi (FORGE-09) : `forge reopen`
reconstruit une session depuis le `crew/` de l'équipe (brief tiré du `forge.json` de
la promotion, dérivé du plan sinon), écrit l'identifiant de la nouvelle session dans
le `forge.json` du dossier — une équipe dupliquée devient ainsi indépendante et ne
peut plus atteindre la session de l'original — et la pose à la pause sèche ; le
wizard lit la session sur `session.started` / `team.reopened` et ouvre le Composer
sans moteur, comme après `--dry` — amender un agent, essayer l'équipe ou la garder
telle quelle, puis ré-adopter sur le même dossier. La carte ne fait que garder le
bouton : « Modifier » est proposé quand le `forge.json` de l'équipe nomme une session
(`TeamSummary.ForgeSessionId`) ou que sa crew YAML peut être relue
(`TeamSummary.HasYamlCrew`) ; une équipe sans l'un ni l'autre (crew script,
disposition étrangère, pas d'enregistrement) le garde désactivé, la raison en
infobulle ; l'infobulle dit aussi quand la réouverture passe par une session
reconstruite. Supprimer une session sous « Sessions
en cours » pendant que l'assistant est ouvert dessus termine aussi cette création :
l'assistant revient à l'étape 1 vierge de « Recommencer » (un moteur en marche est
arrêté d'abord) plutôt que de garder un Composer au-dessus d'un dossier qui n'existe
plus ; une session sur laquelle il n'est pas ouvert le laisse intact. « Modifier » amène
l'assistant au premier plan dès le clic, avant que le moteur ait répondu — la
réouverture, une reconstruction quand aucune session n'est liée, prend un moment, et
l'écran ne bougeait qu'une fois celle-ci finie (les « deux clics » du propriétaire,
2026-09-21) ; la réouverture se voit comme le moteur au travail. Un clic sur « Modifier » ou
« Reprendre » pendant que le moteur est occupé sur une autre création est refusé en toutes
lettres sur la ligne de statut de l'assistant, sans rien arrêter, au lieu d'être ignoré.
La tâche d'un run forge ne se termine qu'une fois son épilogue posé sur le thread UI, et
avec lui chaque événement posté avant : WPF reprend un await commencé dans un gestionnaire
de saisie à la priorité Send, au-dessus de la priorité Normal des posts du thread lecteur,
et la reconstruction lisait la session sur un modèle que les événements n'avaient pas
encore atteint — étape 1, la session sur disque pour le second clic. Le disque est le
repli quand le flux n'annonce rien — la session que nomme l'identifiant laissé par le
moteur dans le `forge.json` de l'équipe, retenue seulement si la règle R la lie à ce
dossier même, si bien qu'une copie n'atterrit jamais sur la session de l'original — et une
carte le dit quand ni l'un ni l'autre ne l'a.

### Adopter sous le nom de l'équipe (STUDIO-26)

L'adoption transmet au moteur le nom de l'équipe : `forge promote --name` devient le titre de
`FORGE.md`, de `forge.json` et de la session, et une fois la promotion écrite le moteur renomme
le dossier de session d'après le dossier d'équipe (`-2` quand une autre session porte déjà ce
nom) et l'annonce par `session.renamed`, qui déplace avec lui le slug et le dossier de session de
l'assistant ; les artefacts de planification et les lanceurs portent eux aussi le nom du dossier
d'équipe. Un renommage que le disque refuse laisse l'adoption debout : le `warning` du moteur
rejoint la ligne que l'assistant laisse après une adoption, et l'identifiant garde le lien.
Avant de promouvoir une **nouvelle** équipe, l'assistant regarde d'abord le dossier : quand
quelque chose l'occupe déjà — une équipe, un dossier qui n'en contient aucune, un fichier
(`TeamCatalog.OccupantOf`) — l'étape 4 dit quoi, propose un nom libre dont le dossier est celui
qui est pris suffixé `-2`… (`TeamCatalog.FreeSibling`) et, quand une équipe l'occupe, « Ouvrir
l'équipe existante », qui amène Mes équipes au premier plan. Le moteur n'est sollicité qu'une
fois le nom libre, si bien que son refus d'une destination non vide n'atteint jamais l'écran.
Une ré-adoption écrit dans le dossier de sa propre équipe, ce qui n'est pas une collision.

### Outils et MCP dans les réglages (STUDIO-21)

Deux onglets qui manquaient à l'écran Réglages. **Outils**, ouvert aux deux modes, tient en
trois cartes. Les clés des outils : une ligne par clé qu'un outil demande — la clé Tavily de
`web_search` (`ORKEON_TAVILY_API_KEY`, l'orthographe que lit la chaîne de secrets) et la clé
Brave de `brave_search` (`BRAVE_API_KEY`, lue telle quelle par l'hôte des runners) — sur les
mêmes lignes et le même magasin que les clés API de l'onglet Modèle (`SecretRowViewModel`,
`IApiKeyStore` : la valeur va dans l'environnement utilisateur, jamais dans un fichier, et la
ligne dit où obtenir une clé). Le catalogue : tous les outils qu'`orkeon run` enregistre, par
famille, chacun en chip, et sous chaque famille une ligne par outil qui demande quelque chose
— une clé mémorisée au-dessus, un outil présent seulement une fois sa clé en place
(`brave_search`), une clé fournie à l'appel par l'agent (`image_generation`), des paramètres
de connexion fournis à l'appel (les outils de bases de données et de graphes), un réglage
expert plus bas (`shell_command`). Le catalogue est déclaré dans Core (`ToolCatalog`) : le
framework ne porte aucune métadonnée « réglages requis » et son registre ne liste que des
noms, donc la liste est la colonne `orkeon run` de la matrice de disponibilité de
`docs/tools/inventory.md`, et un test pinne chaque nom contre ce fichier. La carte expert :
la liste d'autorisation de `shell_command` (`Orkeon:Tools:Shell`), l'interrupteur des
interpréteurs et les deux listes de commandes, une commande par ligne — jamais écrites
comme un tableau vide, que le moteur lirait comme « bloquer toute commande ».

**MCP**, expert seulement, est la section `MCP` : l'interrupteur, et une carte par serveur
sous `MCP:Servers` — identifiant, transport (`Stdio` ou `Sse`, l'orthographe du moteur),
commande, arguments et environnement pour un serveur stdio, URL pour un serveur HTTP. Chaque
frappe écrit en place à travers `McpSection`, donc une clé que Studio ne modélise pas survit à
l'édition du serveur qui la porte, et un renommage déplace le nœud entier. Chaque ligne dit
son propre problème comme le validateur refusera l'enregistrement (`STUDIO-MCP-*`) : un
identifiant que le binder mutilerait, un serveur stdio sans commande, un serveur HTTP sans URL
http(s) absolue ; une valeur du bloc d'environnement qui ressemble à un secret est signalée
en information, puisque le fichier est en clair et que le serveur hérite de l'environnement
utilisateur. La section est honorée par le runner : `orkeon run` connecte les serveurs
déclarés avant le chargement de la crew (voir [Intégration MCP](./mcp.md)), ce qui est ce qui
donne son sens à l'onglet — jusque-là personne ne la lisait.

## Localisation : le port `IStudioStrings`

`Orkeon.Studio.Core` définit un port de localisation, `IStudioStrings` (`Localization/StudioStrings.cs`) : un indexeur par clé plus un événement `CultureChanged` pour que les ViewModels ré-émettent leurs bindings au changement de langue. Les valeurs anglaises par défaut dans `EnglishStudioStrings` font office de registre de clés de référence. Chaque front choisit sa langue : l'application WPF ponte le port sur son service `I18n` adossé aux resx (`I18nStudioStrings`, `Strings.resx` plus les satellites `fr`, `es`, `de` et `zh-Hans`) avec une **bascule à chaud entre cinq langues** relayée via `CultureChanged`, pilotée par `LanguageSelectorViewModel` — la langue du système est détectée et volontairement jamais persistée, seul un choix explicite est enregistré ; les TUIs gardent l'anglais par défaut. Volontairement non traduits, par politique de contrat CLI : les détails des vérifications d'`orkeon doctor`, les résultats de sonde LLM, les descriptions de codes de sortie et les verdicts `VALIDATION OK/FAILED` — traduire la copie de Studio la désynchroniserait de ce que le CLI imprime dans un terminal. Les messages du validateur et les noms des vérifications doctor suivent une répartition révisée : la ligne anglaise brute reste le détail expert (infobulle ou libellé mono), et une surcouche en langage clair par code est ce que les listes montrent d'abord — `Studio.Diagnostics.Code.<code>` pour les messages du validateur, `Studio.Diagnostics.Check.<name>` pour les vérifications doctor, toutes deux sous l'unique convention de clés `Studio.<Ecran>.<Libelle>` que les tests de parité imposent.

## Où Studio range les choses

Deux racines, une règle : **l'état applicatif** vit dans le répertoire de configuration
par utilisateur, **les documents** vivent dans le profil utilisateur. Rien n'est jamais
généré dans le répertoire courant, et les clés d'API ne vivent dans aucun des deux —
elles restent dans les variables d'environnement de l'utilisateur, jamais dans un fichier.

**La clé ne se saisit qu'à un seul endroit.** La ligne où l'on colle une clé (`IApiKeyStore` /
`EnvironmentApiKeyStore`, partagée par l'onglet modèle et les clés d'outils de STUDIO-21)
n'existe que dans l'**application WPF** : la valeur part dans l'environnement utilisateur
(`EnvironmentVariableTarget.User` plus le processus, pour que la session et chacun de ses
enfants la voient tout de suite), ce qui persiste entre sessions sous Windows et est un no-op
documenté sous Unix — là où l'application WPF ne tourne pas. Les deux TUI ne portent aucun
champ de clé : sous Linux et macOS, la variable est posée par le shell de l'opérateur. Quelle
variable, par fournisseur, et les trois noms qu'on confond avec elle :
[Clés d'API : la variable par fournisseur](../reference/llm-providers-comparison.md).

**`%APPDATA%\Orkeon\`** (`$XDG_CONFIG_HOME/Orkeon/` ailleurs) — l'état applicatif :

| Entrée | Ce que c'est |
|---|---|
| `appsettings.json` | les réglages globaux par utilisateur — la base durable sur laquelle chaque lancement compose |
| `studio-model-profiles.json` | les réglages de modèles nommés (fournisseur, modèle, URL, température, budget de réponse, timeout, interrupteur de réflexion et effort, **nom de variable d'environnement de la clé seulement**) |
| `studio-history.json` | l'historique des lancements que lisent l'écran Historique et les cartes d'équipe |
| `.orkeon\forge\<slug>\` | les **sessions d'atelier** — des chantiers reprenables (brief, blueprint, rendu `crew/` provisoire, `runs/` d'essai), pas les crews adoptées. Le nom à point est la convention d'état de workspace du moteur (SPEC §4.1, comme `.git`) : Studio donne `%APPDATA%\Orkeon` au moteur comme workspace de forge, donc `forge resume <slug>` fonctionne à l'identique depuis un terminal et depuis Studio |

**`%LOCALAPPDATA%\Orkeon\Studio\ui-preferences.json`** — le confort de la fenêtre :
mode, langue, thème. À part des deux racines ci-dessus : il n'est écrit que par
l'application WPF (Windows seulement), dans les données applicatives locales
(**non itinérantes**), et chaque accès disque est tolérant — un fichier absent, corrompu
ou non inscriptible retombe sur les valeurs par défaut, jamais sur un plantage.

**`%USERPROFILE%\Orkeon\teams\<slug>\`** — les documents : les équipes adoptées.
Chacune est un dossier ordinaire et autonome (définition de la crew, `run.cmd`/`run.sh`,
le sidecar `studio-team.json` avec nom, besoin, réglage, programmation et dossiers) —
copiable, partageable, supprimable, exécutable avec `orkeon run <dossier>` seul. Le
`<slug>` est le nom de l'équipe passé par la règle de nommage de dossier dont le moteur
nomme aussi ses sessions — une seule implémentation, `FolderSlug` dans
`Orkeon.Domain.FileSystem` : minuscules ASCII, accents retirés, un tiret entre les mots,
coupé au mot sous 64 caractères ; un nom qui ne garde aucune lettre ni aucun chiffre
ASCII (écrit en chinois, par exemple) donne `equipe`.
L'adoption *déplace* le résultat d'une session de la racine d'état vers la racine des
documents ; c'est la frontière entre un brouillon et un livrable. Dans le sidecar,
`name` est normalisé à l'écriture (une ligne, balisage Markdown retiré, coupé au mot
sous 64 caractères — le plafond du slug ; l'import applique la même règle à la copie
qu'il fait) tandis que `description` est le besoin entier, intact : le résumé d'un
paragraphe qu'affichent les cartes est dérivé à la lecture (`TeamCatalog.Summarize`)
et jamais stocké.

## Comment Studio est livré

Studio s'installe **à côté du CLI** via les paquets de release — voir la [matrice de publication](../reference/publication-matrix.md) pour les artefacts exacts et [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) pour le pas-à-pas :

- **Windows** — le `orkeon-cli-<version>-win-x64.zip` et le MSI par utilisateur embarquent l'application de bureau WPF (`orkeon-studio`, self-contained) ; le MSI enregistre aussi un raccourci « Orkeon Studio » dans le menu Démarrer.
- **Debian/Ubuntu** — le `.deb` et les archives multi-app Linux embarquent les deux applications terminal (`orkeon-studio-config`, `orkeon-studio-run`), self-contained.
- **macOS** — CLI seul sur le canal d'onboarding en V1 ; les archives multi-app `osx-*` embarquent bien les deux TUIs (seule l'app WPF a un filtre de RID), non testées sur macOS en V1.

Les deux TUIs ciblent `net10.0` simple et sont donc des builds multi-plateformes ; seul `Orkeon.Studio.Wpf` est lié à Windows (`net10.0-windows`, WPF).

## Ce que Studio n'est pas

- **Pas un paquet NuGet** — les quatre projets posent `IsPackable=false` ; le seul canal de distribution est celui des installeurs de release.
- **Pas un moteur séparé** — Studio ne ré-implémente jamais un workflow : il édite le fichier de settings du CLI et lance le CLI lui-même (`OrkeonProcessRunner`), donc ses résultats sont exactement ceux d'`orkeon run`.

---

> **Voir aussi** : [Matrice de publication](../reference/publication-matrix.md) ·
> [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Retour à l'index](../INDEX.md)
