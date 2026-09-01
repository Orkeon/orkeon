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
- **Launch/ & Process/** — construction de la ligne de commande `orkeon run` (`RunArgumentsBuilder`, `RunLaunchOptions`), localisation du binaire (`OrkeonBinaryLocator` — dans l'ordre : l'argument `--cli-dir`, à côté de l'exécutable, la variable d'environnement `ORKEON_CLI_DIR`, le `PATH`, puis le checkout de développement), exécution et flux de sortie (`OrkeonProcessRunner`, `IProcessLauncher`), interprétation des codes de sortie (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), et le rapport `orkeon doctor` (`DoctorReport`).
- **Forge/** — le client typé d'`orkeon forge --events jsonl` (le moteur derrière l'assistant de création) : un parseur de lignes tolérant, épinglé contre les lignes d'or du protocole côté CLI, la projection de session que tous les fronts lisent (`ForgeSessionModel`, correspondance des jalons, les règles de la checklist ✔/✘), le pilote de processus enfant avec le canal de réponse stdin (`ForgeClient`, dont la requête de démarrage porte des surcharges d'environnement — c'est ainsi que le profil de l'assistant de Studio atteint le moteur), le catalogue de sessions sur disque et l'hydrateur de reprise. Le processus Studio ne touche jamais à un LLM — il ne voit que des lignes JSON.
- **Profiles/** — les réglages de modèle nommés du design v3 (`ModelProfile`, `ModelProfileSet`, `ModelProfileFileStore` → `studio-model-profiles.json` à côté du fichier de settings) : des « réglages de modèle » réutilisables, une élection par défaut reflétée dans la section `Llm`, le profil sur lequel tourne l'assistant de Studio, et des surcharges d'environnement `ORKEON_Llm__*` par profil pour les lancements. L'éditeur de profil offre le catalogue complet des fournisseurs (`LlmPresets.ProviderCatalogFor` — les deux runtimes locaux plus chaque cloud dont le framework livre un provider, endpoint/modèle pré-remplis depuis les défauts runtime épinglés par drift), et un novice colle sa clé d'API directement dans l'éditeur : elle atterrit dans une **variable d'environnement utilisateur** (`IApiKeyStore`/`EnvironmentApiKeyStore`, le nom conventionnel du fournisseur comme `DEEPSEEK_API_KEY`) — le fichier du store ne porte jamais que le *nom* de cette variable (`ModelProfile.KeyEnvName`), et les lancements posent la valeur résolue sur le processus enfant en `ORKEON_Llm__ApiKey`. La clé elle-même n'entre dans aucun fichier.
- **Teams/** — le dossier des équipes (`TeamCatalog`, défaut `~/Orkeon/teams`) : chaque équipe adoptée est un dossier ordinaire — listé, dupliqué, supprimé, importé (avec un scan des secrets en clair) — plus le sidecar `studio-team.json` qui note ce que la définition de crew ne peut pas dire (nom, besoin, profil, planification affichée). Le profil noté n'est pas décoratif : lancer une équipe adoptée le résout dans le store de profils et le pose sur le run en `ORKEON_Llm__*`.
- **Run/** — le client typé d'un `orkeon run --events jsonl` **observé** (BUS-06) : `RunClient`, frère de `ForgeClient` et délibérément son jumeau — même lanceur, même localisateur, même parseur d'enveloppe — et `RunProgressModel`, qui plie le flux vers ce qu'un écran affiche (tâches terminées, coût, question en attente). Le client porte aussi le siège que le hub du run donne à un processus observateur : écrire à un agent, publier, s'abonner, répondre — et l'écran « Lancer » occupe désormais ce siège lui aussi : le `send` d'un agent (marqué `expectsReply`) apparaît comme un panneau de demande, et la réponse saisie repart par stdin. Voir [Le bus d'événements du run](run-event-bus.md).
- **Storage/ & History/** — emplacements des settings et chaîne de résolution (`SettingsLocations`, `AppSettingsFile`), historique des lancements (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — le port `IStudioStrings` (ci-dessous).

La liste de dépendances de Core est volontairement mince : seulement `Orkeon.Domain` (montages, `LlmDefaults`) et `Orkeon.Rag.Abstractions` (`RagProfilePresets`, la liste fermée des noms de profils RAG proposés par les UIs). Core étant référencé par trois front-ends self-contained, chaque dépendance transitive se paie trois fois sur disque — les références plus lourdes ont été supprimées, et les quelques constantes dupliquées sont épinglées contre les originales par des tests de dérive dans `Orkeon.Studio.Core.Tests`.

### Les front-ends

- **`orkeon-studio-config`** (TUI) — éditeur plein écran du fichier de settings : presets fournisseur, modèle et endpoint, logging, rate limiting, profil RAG, la table des montages VFS, une vue JSON brut, et un écran de diagnostic exécutant `orkeon doctor`. Rendu uniquement : chaque comportement vient de Core.
- **`orkeon-studio-run`** (TUI) — choisir une cible, régler les options d'exécution (dont `--validate` pour un dry run), suivre la sortie en direct, annuler au besoin. `--version` et `--help` sont répondus en mode headless avant l'initialisation de Terminal.Gui, ce qui garde les deux TUIs scriptables et vérifiables en CI.
- **`orkeon-studio`** (WPF, Windows) — une fenêtre de bureau au design v3 « volets » : une barre latérale en ordre de cycle de vie — **Équipes d'agents** (Créer une équipe, Mes équipes, Importer), **Travail** (Tester, Exécuter, Historique), **Environnement** (Réglages, Diagnostic — le rapport du doctor se copie en texte brut) — sous une bascule globale **Novice/Expert**. Novice explique chaque étape, montre l'aide contextuelle et cache la machinerie ; Expert montre tout : lignes de commande, JSON brut, journal technique, et l'écran Tester réservé aux experts. La fenêtre s'ouvre sur un écran de démarrage (Kama, la mascotte, clic pour passer), porte un panneau À propos, une visite guidée en cinq étapes, les thèmes clair/sombre et la bascule EN/FR à chaud ; elle tient un minimum de 1024×768 et chaque contrôle est stylé — aucun chrome Windows natif. Les écrans novices suivent de près la maquette v3 : **Exécuter** est une carte équipe (nom et méta issus du sidecar), une carte de progression en langage clair avec badge d'état et action « Ouvrir le résultat », et un journal technique replié par défaut ; **Historique** est une liste de cartes avec durée par exécution et phrase de résultat localisée ; **Diagnostic** s'ouvre sur une carte bilan nourrie par un premier doctor silencieux au démarrage, avec des noms de vérifications en clair ; **Réglages** s'enregistre à chaque modification en novice (le cycle explicite Valider/Enregistrer est celui de l'expert), et les dossiers autorisés sont une carte par montage avec un parcours « Autoriser un dossier ». Il ne référence que `Orkeon.Studio.Core`. Outre `--smoke-exit`, il accepte `--cli-dir <dir>` (nomme le dossier du CLI, prioritaire sur toute autre recherche) et `--capture-screens <dir>` : une campagne de captures sans intervention qui parcourt chaque écran dans les deux modes (plus les overlays éditeur de réglage et À propos) et écrit un PNG par arrêt — la référence de remédiation de fidélité face à la maquette.

L'assistant de création est la doctrine en actes : « Créer une équipe » déroule Décrire ▸ Composer ▸ Essayer ▸ Adopter au-dessus d'`orkeon forge --events jsonl` lancé en processus enfant — la composition tourne avec `--dry` : le moteur génère et valide puis **s'arrête à l'étape Composer** ; l'essai est le clic « Essayer l'équipe » de l'utilisateur, qui reprend la session sans dry (une session rouverte depuis « Mes équipes » à cette pause retombe sur Composer de la même façon) — le stepper est une projection des jalons du moteur, les blocs « consigne + questions » de chaque étape voyagent par le canal ordinaire `user.message`, les boutons d'arbitrage sont générés depuis les options `decision.needed` du moteur lui-même, et l'adoption promeut directement dans le dossier des équipes avec la vraie grammaire de planification du moteur (à la demande, `daily@HH:mm`, `hourly`). Une capacité absente du flux n'existe pas à l'écran — c'est exactement ce qui empêche le `orkeon forge` du terminal et l'assistant WPF de diverger. L'assistant est verrouillé tant que celui de Studio n'a pas de profil de modèle ; l'écran Réglages unifié (onglet Modèle d'IA avec les profils nommés, Dossiers autorisés, et les onglets experts limites/fichier brut) est là où vit cette élection.

### L'écran « Lancer » n'est plus un terminal

C'était une liste de vingt mille lignes : honnête, et un terminal avec un thème. La personne qui lance une crew depuis Studio veut deux choses que le défilement ne donne pas — est-ce que ça avance, et est-ce que ça m'attend — donc l'écran observe désormais le run par le protocole que le wizard de création utilise déjà (`--events jsonl`, activé par défaut ; décocher l'option rend l'argv nu).

Ce qu'il montre : les tâches terminées avec leur agent, leur durée et leurs jetons ; une ligne de coût ; et **la question du run, posée à l'écran**. Auparavant, une tâche déclarée `humanInput: true` était approuvée dans le dos de l'utilisateur — un repli défendable pour un run non surveillé, et la mauvaise réponse dès qu'un écran regarde.

Le journal brut est **rétrogradé, pas supprimé**. Une ligne que le panneau ne sait pas lire y retombe plutôt que dans le vide, la règle que le lanceur en terminal suivait déjà.

Trois refus tiennent le panneau honnête, chacun épinglé par un test. Un run muet dit « rien de rapporté » plutôt qu'une progression implicite. Une question sans identifiant de corrélation n'est pas affichée comme en attente, car répondre exige une adresse. Et une réponse qui n'a pas pu partir laisse la question ouverte au lieu de prétendre qu'elle est arrivée.

### Les dossiers d'équipe de bout en bout (remédiation v2)

Les dossiers d'une équipe adoptée font partie de l'équipe : le sidecar
`studio-team.json` les enregistre en mount-strings (`mounts`), à côté du nom,
du réglage et de la programmation. Les cartes de « Mes équipes » les montrent en
chips ; « Changer les dossiers » les édite dans la modale des dossiers d'équipe.
Une équipe ne déclare jamais un dossier, elle en associe un déjà déclaré : les
deux gestes côté équipe — le bloc « Dossiers de cette équipe » du wizard et
« Autoriser un autre dossier… » sur une équipe adoptée — ouvrent le sélecteur
« Ajouter un dossier autorisé », une liste à cocher des dossiers tenus dans
« Réglages › Dossiers autorisés » (`Orkeon:FileSystem:Mounts`). Les entrées
choisies sont reportées telles quelles, **droits compris** : les réglages sont
le seul endroit où un dossier et ses droits se décident, et une équipe capable
de les élargir ferait de cette déclaration une suggestion. Une ligne que
l'équipe porte déjà, ou dont la racine virtuelle est déjà prise par un autre
dossier, le dit et ne peut pas être choisie — deux montages sur une même racine
ne sont pas fusionnés par le runtime, l'un est perdu.

Le bloc du wizard est **une ligne par point de montage** (lot 3) : le nom que
les agents adressent, qui l'adresse — provenance, jamais permission — et le
dossier derrière, ou « Choisir le dossier… » quand il n'y en a pas encore. Ce
bouton ouvre le même sélecteur **ciblé sur ce chemin virtuel** : l'entrée
retenue garde son dossier et ses droits, et seul le nom que les agents lui
donnent revient à l'équipe. Une ouverture ciblée prend UN dossier et juge ses
lignes là où le choix va **atterrir**, pas sur la racine que les réglages ont
déclarée — sans quoi chaque dossier déjà employé ailleurs se refuserait
lui-même. Sans ce geste, une racine impliquée par les agents ne pouvait être
répondue qu'à l'adoption, par un dossier créé dans l'équipe et laissé vide :
c'est pourquoi la carte dit aussi, tant qu'il est temps, qu'une équipe qui lit
sans dossier choisi recevra son propre `input/` vide et que rien n'y copiera
vos documents.

Déclarer reste le geste des réglages, et « Déclarer un nouveau dossier… » est
une porte vers eux : le sélecteur se ferme et l'écran bascule sur
« Réglages › Dossiers autorisés », sur cet onglet et pas seulement sur cet
écran. Une seule porte, pour qu'un dossier ne puisse pas être déclaré à deux
endroits et diverger entre eux ; c'est sur la carte novice des réglages que
s'ouvre encore le sélecteur partagé « Autoriser un dossier » (chemin +
Parcourir, arborescence à un niveau avec la note « déjà autorisé », droits en
deux lignes radio, aperçu expert du mount-string exact).

Un dossier d'équipe que les réglages ne déclarent **pas** apparaît en rouge —
sur les puces du wizard, les cartes de « Mes équipes » et la modale des
dossiers d'équipe. Ce n'est pas une erreur : le `/output` et le `/input` d'une
équipe sont créés dans l'équipe elle-même à l'adoption et ne sont jamais
déclarés. C'est la seule chose qu'une ligne ne peut pas dire en nommant un
chemin virtuel, et une équipe qui sort des dossiers autorisés de la machine ne
devrait pas se découvrir en lisant un sidecar.

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
sans moyen de le dire. Une suppression tient désormais : `WithDerivedWriteMounts`
ne la réajoute plus, exactement l'annulation silencieuse que cette méthode
existe pour empêcher. L'écran avertit et nomme les racines abandonnées, parce
que rien ne leur sera associé et que les agents qui y écrivent échoueront ; un
« Rétablir » unique est le chemin de retour après une croix de trop.
Au lancement, Studio pose les mounts du sidecar sur le run
en arguments `--mount`, devant ceux du lancement — les chips et la commande ne
peuvent pas diverger. Limite assumée : un `orkeon run` nu en terminal ne lit pas
le sidecar — comme le champ `profile`, c'est le confort de Studio, pas le
contrat du moteur.

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
d'équipe — résolu par la recherche inverse du dossier d'équipe vers la session
forge qui l'a promue (`promotedTo`) — rouvre le wizard à l'étape Composer avec
tout le stepper accessible : le moteur reprend la session promue dans un
arbitrage rouvert (le verdict stocké est ré-annoncé d'abord), les agents sont
donc à nouveau éditables, une nouvelle décision `retry` re-exécute l'essai tel
quel (zéro jeton de composition, une itération de budget), et la ré-adoption
**met à jour le même dossier d'équipe** — les fichiers générés (`crew/`,
lanceurs, `FORGE.md`, `schedule/`) sont régénérés, le sidecar et les fichiers de
l'utilisateur survivent, renommer l'équipe ne change que son nom d'affichage.
Après une adoption, la carte le dit (« Rien n'est figé… ») et offre « Modifier
l'équipe » et « Refaire un essai » directement. Une équipe sans session —
importée, ou session supprimée — garde « Modifier » désactivé, la raison en
infobulle.

## Localisation : le port `IStudioStrings`

`Orkeon.Studio.Core` définit un port de localisation, `IStudioStrings` (`Localization/StudioStrings.cs`) : un indexeur par clé plus un événement `CultureChanged` pour que les ViewModels ré-émettent leurs bindings au changement de langue. Les valeurs anglaises par défaut dans `EnglishStudioStrings` font office de registre de clés de référence. Chaque front choisit sa langue : l'application WPF ponte le port sur son service `I18n` adossé aux resx (`I18nStudioStrings`, `Strings.resx`/`Strings.fr.resx`) avec une **bascule EN/FR à chaud** relayée via `CultureChanged` ; les TUIs gardent l'anglais par défaut. Volontairement non traduits, par politique de contrat CLI : les détails des vérifications d'`orkeon doctor`, les résultats de sonde LLM, les descriptions de codes de sortie et les verdicts `VALIDATION OK/FAILED` — traduire la copie de Studio la désynchroniserait de ce que le CLI imprime dans un terminal. Les messages du validateur et les noms des vérifications doctor suivent une répartition révisée : la ligne anglaise brute reste le détail expert (infobulle ou libellé mono), et une surcouche en langage clair par code (clés `Vm_ValMsg_*`, `Vm_Doctor_*`) est ce que les listes montrent d'abord.

## Où Studio range les choses

Deux racines, une règle : **l'état applicatif** vit dans le répertoire de configuration
par utilisateur, **les documents** vivent dans le profil utilisateur. Rien n'est jamais
généré dans le répertoire courant, et les clés d'API ne vivent dans aucun des deux —
elles restent dans les variables d'environnement de l'utilisateur, jamais dans un fichier.

**`%APPDATA%\Orkeon\`** (`$XDG_CONFIG_HOME/Orkeon/` ailleurs) — l'état applicatif :

| Entrée | Ce que c'est |
|---|---|
| `appsettings.json` | les réglages globaux par utilisateur — la base durable sur laquelle chaque lancement compose |
| `studio-model-profiles.json` | les réglages de modèles nommés (fournisseur, modèle, URL, température, timeout, **nom de variable d'environnement de la clé seulement**) |
| `studio-history.json` | l'historique des lancements que lisent l'écran Historique et les cartes d'équipe |
| `Studio\ui-preferences.json` | le confort de la fenêtre : mode, langue, thème |
| `.orkeon\forge\<slug>\` | les **sessions d'atelier** — des chantiers reprenables (brief, blueprint, rendu `crew/` provisoire, `runs/` d'essai), pas les crews adoptées. Le nom à point est la convention d'état de workspace du moteur (SPEC §4.1, comme `.git`) : Studio donne `%APPDATA%\Orkeon` au moteur comme workspace de forge, donc `forge resume <slug>` fonctionne à l'identique depuis un terminal et depuis Studio |

**`%USERPROFILE%\Orkeon\teams\<slug>\`** — les documents : les équipes adoptées.
Chacune est un dossier ordinaire et autonome (définition de la crew, `run.cmd`/`run.sh`,
le sidecar `studio-team.json` avec nom, besoin, réglage, programmation et dossiers) —
copiable, partageable, supprimable, exécutable avec `orkeon run <dossier>` seul.
L'adoption *déplace* le résultat d'une session de la racine d'état vers la racine des
documents ; c'est la frontière entre un brouillon et un livrable.

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
