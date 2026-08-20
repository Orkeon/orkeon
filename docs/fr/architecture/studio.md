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

- **Configuration/** — un modèle d'édition sans perte de `appsettings.json` (`AppSettingsDocument`) plus les sections typées (`LlmSection`, `LlmLoggingSection`, `LoggingSection`, `MountsSection`, `RagSection`, `RateLimitingSection`) et `LlmProviderDetector`.
- **FileSystem/** — édition des montages : `MountDefinition`, `MountRights`, `MountValidator`, parcours de répertoires.
- **Presets/ & Llm/** — le catalogue de presets LLM (`LlmPresets`, `OrkeonCliDefaults`) et la sonde d'endpoint (`ILlmEndpointProbe`/`HttpLlmEndpointProbe`, `LlmApiKeyResolver`).
- **Targets/** — détection de la cible d'exécution (`RunTargetDetector`) : un `config.yaml`, un répertoire de crew multi-fichiers, ou un script `.ork.ts`.
- **Launch/ & Process/** — construction de la ligne de commande `orkeon run` (`RunArgumentsBuilder`, `RunLaunchOptions`), localisation du binaire (`OrkeonBinaryLocator`), exécution et flux de sortie (`OrkeonProcessRunner`, `IProcessLauncher`), interprétation des codes de sortie (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), et le rapport `orkeon doctor` (`DoctorReport`).
- **Forge/** — le client typé d'`orkeon forge --events jsonl` (l'Atelier) : un parseur de lignes tolérant, épinglé contre les lignes d'or du protocole côté CLI, la projection de session que tous les fronts lisent (`ForgeSessionModel`, correspondance des jalons, les règles de la checklist ✔/✘), le pilote de processus enfant avec le canal de réponse stdin (`ForgeClient`), le catalogue de sessions sur disque (« Mes solutions ») et l'hydrateur de reprise. Le processus Studio ne touche jamais à un LLM — il ne voit que des lignes JSON.
- **Run/** — le client typé d'un `orkeon run --events jsonl` **observé** (BUS-06) : `RunClient`, frère de `ForgeClient` et délibérément son jumeau — même lanceur, même localisateur, même parseur d'enveloppe — et `RunProgressModel`, qui plie le flux vers ce qu'un écran affiche (tâches terminées, coût, question en attente). Le client porte aussi le siège que le hub du run donne à un processus observateur : écrire à un agent, publier, s'abonner, répondre. Voir [Le bus d'événements du run](run-event-bus.md).
- **Storage/ & History/** — emplacements des settings et chaîne de résolution (`SettingsLocations`, `AppSettingsFile`), historique des lancements (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — le port `IStudioStrings` (ci-dessous).

La liste de dépendances de Core est volontairement mince : seulement `Orkeon.Domain` (montages, `LlmDefaults`) et `Orkeon.Rag.Abstractions` (`RagProfilePresets`, la liste fermée des noms de profils RAG proposés par les UIs). Core étant référencé par trois front-ends self-contained, chaque dépendance transitive se paie trois fois sur disque — les références plus lourdes ont été supprimées, et les quelques constantes dupliquées sont épinglées contre les originales par des tests de dérive dans `Orkeon.Studio.Core.Tests`.

### Les front-ends

- **`orkeon-studio-config`** (TUI) — éditeur plein écran du fichier de settings : presets fournisseur, modèle et endpoint, logging, rate limiting, profil RAG, la table des montages VFS, une vue JSON brut, et un écran de diagnostic exécutant `orkeon doctor`. Rendu uniquement : chaque comportement vient de Core.
- **`orkeon-studio-run`** (TUI) — choisir une cible, régler les options d'exécution (dont `--validate` pour un dry run), suivre la sortie en direct, annuler au besoin. `--version` et `--help` sont répondus en mode headless avant l'initialisation de Terminal.Gui, ce qui garde les deux TUIs scriptables et vérifiables en CI.
- **`orkeon-studio`** (WPF, Windows) — une fenêtre de bureau avec une barre latérale d'écrans : le groupe **Résoudre** (l'Atelier : décrire un problème, voir l'équipe se forger, l'essayer, l'adopter — voir [Forger une équipe à partir d'un besoin](../getting-started/forge-a-team-from-a-need.md)), l'éditeur de settings (presets, sections, montages, JSON brut, diagnostic) et le lanceur de crews (exécution + historique), avec thème clair/sombre, bascule de langue EN/FR et visite guidée. Il ne référence que `Orkeon.Studio.Core`.

L'écran Résoudre est la doctrine en actes : Studio lance `orkeon forge --events jsonl` en processus enfant, rend son flux d'événements (conversation, jalons, checklist), et répond sur stdin. Une capacité absente du flux n'existe pas à l'écran — c'est exactement ce qui empêche le `orkeon forge` du terminal et l'écran WPF de diverger.

### L'écran « Lancer » n'est plus un terminal

C'était une liste de vingt mille lignes : honnête, et un terminal avec un thème. La personne qui lance une crew depuis Studio veut deux choses que le défilement ne donne pas — est-ce que ça avance, et est-ce que ça m'attend — donc l'écran observe désormais le run par le protocole que l'écran Résoudre utilise déjà (`--events jsonl`, activé par défaut ; décocher l'option rend l'argv nu).

Ce qu'il montre : les tâches terminées avec leur agent, leur durée et leurs jetons ; une ligne de coût ; et **la question du run, posée à l'écran**. Auparavant, une tâche déclarée `humanInput: true` était approuvée dans le dos de l'utilisateur — un repli défendable pour un run non surveillé, et la mauvaise réponse dès qu'un écran regarde.

Le journal brut est **rétrogradé, pas supprimé**. Une ligne que le panneau ne sait pas lire y retombe plutôt que dans le vide, la règle que le lanceur en terminal suivait déjà.

Trois refus tiennent le panneau honnête, chacun épinglé par un test. Un run muet dit « rien de rapporté » plutôt qu'une progression implicite. Une question sans identifiant de corrélation n'est pas affichée comme en attente, car répondre exige une adresse. Et une réponse qui n'a pas pu partir laisse la question ouverte au lieu de prétendre qu'elle est arrivée.

## Localisation : le port `IStudioStrings`

`Orkeon.Studio.Core` définit un port de localisation, `IStudioStrings` (`Localization/StudioStrings.cs`) : un indexeur par clé plus un événement `CultureChanged` pour que les ViewModels ré-émettent leurs bindings au changement de langue. Les valeurs anglaises par défaut dans `EnglishStudioStrings` font office de registre de clés de référence (89 clés après STUDIO-11). Chaque front choisit sa langue : l'application WPF ponte le port sur son service `I18n` adossé aux resx (`I18nStudioStrings`, `Strings.resx`/`Strings.fr.resx`) avec une **bascule EN/FR à chaud** relayée via `CultureChanged` ; les TUIs gardent l'anglais par défaut. Volontairement non traduits, par politique de contrat CLI : les noms et détails des vérifications d'`orkeon doctor`, les corps des messages du validateur, les résultats de sonde LLM, les descriptions de codes de sortie et les verdicts `VALIDATION OK/FAILED` — traduire la copie de Studio la désynchroniserait de ce que le CLI imprime dans un terminal.

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
