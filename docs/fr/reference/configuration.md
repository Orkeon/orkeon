> 🇬🇧 [English version](../../reference/configuration.md)

# Référence de configuration (`appsettings.json`)

Cette page est la carte unique des réglages que lit Orkeon : d'où ils viennent, quand l'un d'eux
est refusé, puis **chaque clé, par catégorie** — son type, son défaut, son sens et l'hôte qui la
lit ([Trouver un réglage](#trouver-un-réglage)) —, et enfin les
[variables d'environnement](#variables-denvironnement) que lit Orkeon. Une section qui ne prend effet qu'après un
enregistrement dédié le dit — voir [Sous-systèmes opt-in](./opt-in-subsystems.md) pour le
détail de chacun.

## D'où viennent les réglages

Chaque hôte Orkeon — les hôtes runner (`RunnerHost.Build`, utilisé par `orkeon run`, les runners
YAML et `orkeon-host`) et le REPL — compose les mêmes couches, et elles seules
(`RunnerSettings.ComposeSources`). Sous toutes se trouvent les **variables d'environnement sans
préfixe** : le `OTEL_EXPORTER_OTLP_ENDPOINT` standard que pose un collecteur ou un AppHost .NET
Aspire atteint par elles l'exportateur OpenTelemetry ([télémétrie](./hosting.md#télémétrie)), et
un `Llm__Model` nu est lu aussi, sous chacune des couches qui suivent :

1. **Un `appsettings.json` résolu** — chaîne de résolution (`RunnerSettings.ResolveSettingsPath`) :
   `--settings <chemin>` explicite → `appsettings.json` à côté de la config de crew →
   `appsettings/appsettings.json` en remontant l'arborescence (`examples/appsettings/appsettings.json`
   dans ce dépôt ; `_shared/appsettings.json` est un repli déprécié) → la config globale
   par utilisateur écrite par `orkeon init`. Aucun fichier trouvé ⇒ variables
   d'environnement uniquement.
2. **Les variables d'environnement préfixées `ORKEON_`** (`AddEnvironmentVariables("ORKEON_")`,
   pour chaque hôte et pour `orkeon doctor`). Mapping .NET standard : `__` sépare les
   niveaux — `ORKEON_Llm__ApiKey` surcharge `Llm:ApiKey`, `ORKEON_Orkeon__Rag__Profile`
   surcharge `Orkeon:Rag:Profile`. Le préfixe et les clés se comparent sans la casse, et `:`
   sépare les niveaux comme `__` : `ORKEON_LLM__APIKEY`, `orkeon_llm__apikey` et
   `ORKEON_Llm:ApiKey` valent tous `Llm:ApiKey`. Sous Linux et macOS, deux graphies d'un réglage
   sont deux variables, et un run lit l'une ou l'autre : ne posez jamais un même réglage sous deux
   graphies. Les variables sont ajoutées **après** le fichier :
   elles gagnent. Une clé n'a pas à venir de l'une ou l'autre couche : le fichier peut nommer la
   variable qui la contient (`ApiKeyEnvVar`, [plus bas](#la-clé-dapi-apikey-apikeyenvvar)), lue
   quand aucune ne résout d'`ApiKey`.
3. **Les surcharges CLI de montage** — chaque argument `--mount` devient une entrée
   mémoire `Orkeon:FileSystem:Mounts:<i>` (précédence maximale), placée **par racine
   virtuelle** : un `--mount` sur une racine que le tableau déclaré (les couches ci-dessus) tient
   déjà est écrit à l'index de la première entrée et **remplace toutes les entrées déclarées
   de cette racine** pour le run ; un `--mount` sur une racine neuve est ajouté après le plus
   haut index déclaré. Le montage `/crew` (ou `/script`) du runner et les `InternalMounts`
   sont toujours ajoutés. Une entrée déclarée peut porter un **identifiant** — l'ULID de
   26 caractères devant un `|`, `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` (VFS-90) —
   et plusieurs entrées peuvent déclarer une même racine si chacune en porte un :
   `--mount-id <ulid>`, sinon le bloc `mounts:` de la crew, sélectionne l'entrée que le run
   garde, et toute autre entrée de cette racine est **retirée** — sa clé est écrite à `null`
   à son propre index, son chemin de base n'est pas mis en liste blanche et son dossier
   n'est pas sondé.

**Rien d'autre n'est lu.** L'hôte .NET par défaut posait aussi, sous le fichier résolu,
l'`appsettings.json` et l'`appsettings.{Environment}.json` du **répertoire courant** — depuis .NET 10,
le `<binaire>.settings.json` et son jumeau d'environnement aussi (`orkeon.settings.json`) — et, en
`Development`, ses secrets utilisateur : un fichier d'un autre projet — le dossier où se trouvait
un terminal — ajoutait au run les profils, serveurs MCP ou montages qu'il déclarait, et aucun
diagnostic ne les voyait. Ils ne sont plus lus (GAP-36). `orkeon-host` lit `./appsettings.json`
comme **son** fichier de réglages, quand `--settings` n'en nomme aucun, jamais sous celui qu'il
nomme ; le REPL lit ses fichiers `--settings`, sinon le fichier global, puis sa ligne de commande,
en dernier ([CLI](./cli.md#orkeon-repl--la-console-interactive-séparée)).

Le même préfixe `ORKEON_` alimente aussi `EnvironmentSecretProvider` (résolution de
secrets, p. ex. `OPENAI_API_KEY` → `ORKEON_OPENAI_API_KEY` ; la clé Tavily de l'outil
`web_search` est `ORKEON_TAVILY_API_KEY`). Le second maillon de cette chaîne est la
section `Secrets` du fichier (`Secrets:TAVILY_API_KEY`), la variable d'environnement
l'emportant quand les deux existent. Les variables qu'un binaire lit par leur propre nom —
elles ne portent aucun réglage — sont listées avec les autres dans
[Variables d'environnement](#variables-denvironnement).

**Ce que cela signifie en pratique.** Le fichier est la base durable et partagée ; tout
ce qui se pose dessus est un calque éphémère qui vit et meurt avec un processus. La
vérification `llm-config` d'`orkeon doctor` compose exactement comme un runner (même
chaîne de résolution, mêmes couches : les variables sans préfixe, le fichier, la surcouche
`ORKEON_`) : son verdict répond à la question
*qu'utiliserait un run lancé depuis ce shell, sans calque propre au lancement ?* Les
réglages nommés d'Orkeon Studio empruntent la couche 2 : le réglage élu par défaut est
écrit en entier dans la section `Llm` du fichier — adresse, modèle, délai, interrupteur de
réflexion et variable qui contient sa clé (`ApiKeyEnvVar`), jamais la clé —, si bien qu'un
`orkeon run` manuel en terminal ou une équipe planifiée suit la même élection, ses 600 s et sa
clé comprises (c'est ce que `llm-config` reflète), tandis qu'une équipe qui a élu un autre
réglage le reçoit en variables `ORKEON_Llm__*` sur son seul lancement — chaque champ que le
réglage modélise, vide là où il n'en fixe pas, pour que rien du défaut, et sa clé moins que tout,
n'atteigne l'adresse de l'équipe ; `llm-config` ne peut pas les voir, car elles n'existent nulle
part tant que ce lancement n'a pas démarré. Chaque réglage est aussi écrit dans `Llm:Profiles` du
fichier, avec le nom de la variable qui contient sa clé et jamais la clé, comme profil d'hôte
qu'une crew peut nommer ([plus bas](#studio-écrit-cette-section)), et chaque lancement depuis
Studio les porte tous, clés comprises, en `ORKEON_Llm__Profiles__<id>__*`. Aucun fichier n'est
jamais généré : la composition est en mémoire.

**Un fichier d'où partir.** Chaque installation porte `appsettings.sample.json` à la racine de ce
que son canal installe, à côté de `VERSION` : chaque clé qu'un binaire livré lit, par catégorie,
à son défaut — produit depuis le catalogue des réglages, celui que liste `orkeon settings`. Aucun
binaire ne le charge. Copié tel quel à la place du fichier de réglages, il ne change rien : chaque
clé qu'il écrit porte la valeur qu'elle a quand on l'omet, et ce qui ne s'écrit pas ainsi — une
clé sans défaut, un secret, une liste, une entrée sous un nom que vous choisissez, une clé qu'un
profil RAG fixe, la section `Llm` entière — est montré en commentaire, à décommenter. C'est du
JSON avec des commentaires `//` et une virgule après chaque membre, que les lecteurs de réglages
acceptent (les runners et Orkeon Studio) ; un analyseur JSON strict, non.

## Quand un réglage est refusé

Chaque hôte livré — `orkeon run` sous toutes ses formes (`--validate`, `--list-tools`, `mcp serve`,
la forge, `rag`, `email`), `orkeon-host` et `orkeon-repl` — juge tout réglage qu'il lit **à son
démarrage**, que le run s'en serve ou non (GAP-40) : avant le chargement de la crew, avant tout appel
au modèle, avant qu'un avertissement ne s'imprime ou que la télémétrie ne démarre. Un refus tient en
une ligne qui nomme la clé — code de sortie `1` pour `orkeon` et `orkeon-repl`, `78` pour
`orkeon-host` —, et `orkeon doctor` rapporte les mêmes refus sur le même fichier, une ligne
`runner-settings` chacun. Ce qui est refusé :

- **Une valeur** que le lieur de configuration ne convertit pas (`"Orkeon:Guardian:Enabled": "oui"`),
  ou qu'une règle de sa section refuse (`Orkeon:Rag:Retrieval:TopK` à `0`, un
  `Orkeon:Sqlite:TableName` qu'on ne peut pas donner à SQLite). La section `Llm` est lue aussi
  strictement que ses profils : un `TimeoutSeconds` de `"600s"` est refusé, là où il tournait sur
  30 s, `Thinking:Enabled` et `Grammar` valent `true` ou `false`, et une `Temperature` est un nombre
  fini — `NaN`, `Infinity` ou `1e400` (lu comme l'infini) passaient la lecture, puis faisaient échouer
  chaque requête, JSON n'écrivant aucun tel nombre. De même pour tout nombre qu'une section lit en
  float ou en double (`Orkeon:Rag:Generation:Temperature`, `Orkeon:CrewMemory:MinScore`, …), que le
  lieur prend en `NaN` ou en infini sans un mot. Un niveau de `Logging` est l'un de `Trace`, `Debug`,
  `Information`, `Warning`, `Error`, `Critical`, `None`, sans tenir compte de la casse.
- **Une clé** qu'aucune section ne porte, contre la forme que déclarent ses lecteurs — les lecteurs
  d'une même section ensemble, une sous-section qu'un autre lecteur déclare comprise :
  `Orkeon:Guardian:Enabeld`, `Llm:Provider` (rien ne la lit : le fournisseur se déduit de `BaseUrl`
  et du modèle). Une clé que GAP-08 a retirée — `Memory:ConnectionString`,
  `Orkeon:Rag:ConnectionString`, `Orkeon:Rag:ProviderOptions`, `Orkeon:Pinecone:Environment` — est
  refusée avec sa migration. Le refus d'une clé inconnue se termine par la commande qui liste les
  clés de sa section — `` `orkeon settings Orkeon:Guardian` lists its keys ``. Un nom de section sous `Orkeon:` et sous les groupes `Orkeon:Cli`,
  `Orkeon:Tools`, `Orkeon:Scripting`, `Orkeon:Security` et `Security` doit être l'une de celles
  qu'Orkeon lit (`SettingsSections`) ; le refus propose la plus proche (`Orkeon:Guardain` →
  `Orkeon:Guardian`). Clés et noms de section se comparent sans tenir compte de la casse, comme la
  configuration les lit : `ORKEON_LLM__APIKEY` est `Llm:ApiKey`, et `"llm": { "apikey": … }` aussi.
- **Un nom** par lequel on choisit un composant, contre les noms que connaît cet hôte, la liste dans
  le refus : `Memory:Provider` et `Orkeon:Rag:Provider`, avec ce qu'il faut au fournisseur nommé
  (`Orkeon:LanceDb:Endpoint` pour `lancedb` ; `Orkeon:Pinecone:ApiKey` et `IndexName`, ou `Host`, pour
  `pinecone`) ; `Orkeon:Rag:Profile` ; les `Rerank:Kind`, `QueryTransform:Mode`, `Context:Ordering` et
  `Ingestion:DefaultChunkingStrategy` des options RAG effectives — profil plus surcharges, si bien
  qu'un hôte sans le reranker ONNX refuse `balanced` et `quality` en nommant `onnx` ;
  `Orkeon:Rag:QueryRouting:Classifier` ; `Orkeon:Embeddings:Provider` ;
  `RaggableTree:Embedding:Provider`. Le `memoryProvider:` d'une crew YAML est vérifié à son chargement.

**Ce qui reste ouvert** : la racine de la configuration — elle reçoit aussi les variables
d'environnement sans préfixe, et rien n'y distingue une section mal écrite d'une variable de la
machine —, `Logging` au-delà de ses niveaux, `Secrets`, les clés d'un dictionnaire
(`Llm:Profiles:<nom>`, `MCP:Servers:<id>`, `…:Env:<VAR>`, `Orkeon:Consensus:RoleWeights:<rôle>`,
`Orkeon:Tools:Email:Accounts:<nom>`) et les index d'une liste, et les clés d'une section que cet hôte
ne lit pas — `orkeon run` laisse `Orkeon:Host` au démon, qui la juge ; une section qu'**aucun**
binaire livré ne lit est signalée, pas jugée ([ci-dessous](#quand-un-réglage-est-seulement-signalé)). **Les comptes e-mail sont
l'exception** : un compte qui porte une valeur ou une clé illisible est mis de côté et signalé quand
un appel ou `orkeon email` le nomme, et les autres continuent de fonctionner ([e-mail](../guides/email.md)).

Un hôte C# qui démarre (`StartAsync`, un AppHost .NET Aspire) refuse les valeurs et les noms que lient
ses inscriptions Orkeon — chacune est inscrite avec `ValidateOnStart` — ; les clés de ses sections
restent les siennes. Un conteneur bâti à la main et jamais démarré ne juge rien tant qu'aucune option
n'est lue.

### Quand un réglage est seulement signalé

Une section qu'Orkeon connaît et qu'**aucun binaire livré ne lit** n'est pas refusée : un fichier
de réglages partagé entre `orkeon run` et un hôte écrit en C# est légitime. Elle n'est pas lue en
silence non plus — le run démarrerait sans la limite, le budget ou le filtrage que le fichier
décrit. Chaque hôte livré le dit à son démarrage, une fois par section et par processus, sur
stderr (la sortie standard reste celle du run) et dans son journal, puis démarre ; le code de
sortie est celui du run :

```text
WARNING: ToolRateLimiting is read by no component of this host: a C# host reads it through AddOrkeonToolRateLimiting(). The calls to the model are limited by RateLimiting, which this host reads.
```

`orkeon doctor` rapporte la même phrase en ligne `warn` de `runner-settings`, une par section, et
sort toujours en `0`. Les sections sont celles que le catalogue des réglages marque comme lues par
aucun binaire livré : `ToolRateLimiting`, `TokenBudget`, `Orkeon:Dlp`, `Orkeon:Monitoring`,
`Orkeon:CognitiveMemory`, `Orkeon:MultiModal`, `Plugins`, `Evaluation`, `Orkeon:VectorSearch`,
`Orkeon:Checkpointing` et `Orkeon:ExecutionState:Persistence`
([sous-systèmes opt-in](./opt-in-subsystems.md)). La liste est calculée, pas écrite : une section
qu'un binaire livré se met à lire en sort. Ne sont pas signalées : une section qu'un autre binaire
livré lit (`Orkeon:Host` dans un fichier que lit `orkeon run`), une section de la racine
qu'Orkeon ne connaît pas, et une section que l'hôte lit lui-même parce que sa composition C# a
enregistré ce qui la lit. L'environnement `ORKEON_` écrit une section comme le fichier :
`ORKEON_ToolRateLimiting__GlobalToolRequestsPerMinute` reçoit la même ligne.

## Trouver un réglage

Chaque réglage que lit Orkeon est listé plus bas, rangé par ce à quoi il sert : onze catégories,
une partie de cette page chacune. Dans une catégorie, chaque section a sa sous-partie — ce que
fait la section, qui la lit, puis **une ligne par clé** : son type, son défaut, les valeurs
qu'elle accepte quand leur liste est fermée, et son sens.

<!-- settings-index -->
| Catégorie | Sections | Clés |
|---|---|---|
| [Modèles](#modèles) | [`Evaluation`](#evaluation), [`Llm`](#llm), [`LlmLogging`](#llmlogging), [`Orkeon:CostTracking`](#orkeoncosttracking), [`Orkeon:TokenCounter`](#orkeontokencounter) | 44 |
| [Débit et budgets](#débit-et-budgets) | [`RateLimiting`](#ratelimiting), [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting) | 11 |
| [Mémoire et vecteurs](#mémoire-et-vecteurs) | [`Memory`](#memory), [`Orkeon:ChromaDb`](#orkeonchromadb), [`Orkeon:CognitiveMemory`](#orkeoncognitivememory), [`Orkeon:CrewMemory`](#orkeoncrewmemory), [`Orkeon:EmbeddingCache`](#orkeonembeddingcache), [`Orkeon:Embeddings`](#orkeonembeddings), [`Orkeon:Encryption`](#orkeonencryption), [`Orkeon:LanceDb`](#orkeonlancedb), [`Orkeon:Pinecone`](#orkeonpinecone), [`Orkeon:Redis`](#orkeonredis), [`Orkeon:Sqlite`](#orkeonsqlite), [`Orkeon:VectorSearch`](#orkeonvectorsearch) | 58 |
| [RAG](#rag) | [`Orkeon:Rag`](#orkeonrag), [`Orkeon:Rag:Ingestion`](#orkeonragingestion), [`Orkeon:Rag:QueryRouting`](#orkeonragqueryrouting), [`Orkeon:Rag:Retrieval:Hybrid`](#orkeonragretrievalhybrid), [`Orkeon:Rag:WebFallback`](#orkeonragwebfallback) | 34 |
| [Fichiers et bac à sable](#fichiers-et-bac-à-sable) | [`Orkeon:CodeSandbox`](#orkeoncodesandbox), [`Orkeon:CodeSandbox:Docker`](#orkeoncodesandboxdocker), [`Orkeon:FileSystem`](#orkeonfilesystem), [`Orkeon:Sandbox`](#orkeonsandbox), [`PathSecurity`](#pathsecurity) | 30 |
| [Sécurité](#sécurité) | [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:Guardian`](#orkeonguardian), [`Orkeon:Security:PermissionGate`](#orkeonsecuritypermissiongate), [`Secrets`](#secrets), [`Security:Audit`](#securityaudit), [`Security:Prompt`](#securityprompt), [`Security:ToolResults`](#securitytoolresults), [`Security:Url`](#securityurl), [`Security:Vault`](#securityvault) | 31 |
| [Outils](#outils) | [`BRAVE_API_KEY`](#brave_api_key), [`MCP`](#mcp), [`MCP:Server`](#mcpserver), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Orkeon:Tools:Email`](#orkeontoolsemail), [`Orkeon:Tools:Shell`](#orkeontoolsshell), [`Plugins`](#plugins), [`RaggableTree`](#raggabletree) | 54 |
| [Orchestration et persistance](#orchestration-et-persistance) | [`Orkeon:Checkpointing`](#orkeoncheckpointing), [`Orkeon:Consensus`](#orkeonconsensus), [`Orkeon:CrewFactory`](#orkeoncrewfactory), [`Orkeon:ExecutionState:Persistence`](#orkeonexecutionstatepersistence) | 16 |
| [Scripts et console](#scripts-et-console) | [`Orkeon:Cli:ConsoleStreaming`](#orkeoncliconsolestreaming), [`Orkeon:Cli:ScriptCommands`](#orkeoncliscriptcommands), [`Orkeon:Cli:ScriptHost`](#orkeoncliscripthost), [`Orkeon:Cli:Session`](#orkeonclisession), [`Orkeon:Cli:Tui`](#orkeonclitui), [`Orkeon:Scripting:Limits`](#orkeonscriptinglimits), [`Orkeon:Scripting:Toolchain`](#orkeonscriptingtoolchain) | 21 |
| [Hôte de service et A2A](#hôte-de-service-et-a2a) | [`A2A`](#a2a), [`A2A:Security`](#a2asecurity), [`A2A:Security:AzureAD`](#a2asecurityazuread), [`A2A:Security:Oidc`](#a2asecurityoidc), [`Orkeon:Host`](#orkeonhost), [`Orkeon:Host:Discord`](#orkeonhostdiscord) | 46 |
| [Observabilité](#observabilité) | [`Logging`](#logging), [`Orkeon:Monitoring`](#orkeonmonitoring), [`Telemetry`](#telemetry) | 8 |

67 sections, 353 clés. Lues par aucun binaire livré, seulement par un hôte écrit en C# (11) : [`Evaluation`](#evaluation), [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting), [`Orkeon:CognitiveMemory`](#orkeoncognitivememory), [`Orkeon:VectorSearch`](#orkeonvectorsearch), [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Plugins`](#plugins), [`Orkeon:Checkpointing`](#orkeoncheckpointing), [`Orkeon:ExecutionState:Persistence`](#orkeonexecutionstatepersistence), [`Orkeon:Monitoring`](#orkeonmonitoring).
<!-- /settings-index -->

Lire un tableau :

- **Lue par** nomme les binaires livrés qui lisent la section : `orkeon` est le CLI — `orkeon run`
  sous toutes ses formes et tout verbe qui construit l'hôte des runners (`--validate`,
  `--list-tools`, `mcp serve`, la forge, `rag`, `email`) —, `orkeon-host` l'hôte de service,
  `orkeon-repl` la console interactive. Une section **qu'aucun binaire livré ne lit** le dit, et
  nomme l'enregistrement par lequel un hôte écrit en C# la lit : écrite dans le fichier de
  réglages d'`orkeon run`, elle n'y règle rien ([Sous-systèmes opt-in](./opt-in-subsystems.md)
  donne le détail de chacune).
- **Clé** s'écrit depuis sa section : `QueueLimit` sous `RateLimiting` est le réglage
  `RateLimiting:QueueLimit` — `"RateLimiting": { "QueueLimit": 5 }` dans le fichier,
  `ORKEON_RateLimiting__QueueLimit` dans l'environnement. `<nom>` tient la place d'un nom que vous
  choisissez (un profil, un serveur MCP, un compte e-mail, un rôle) et `<i>` celle d'un indice
  dans une liste.
- **Type** vaut `chaîne`, `entier`, `nombre`, `booléen`, `durée` (`hh:mm:ss`, `j.hh:mm:ss` à
  partir d'un jour), `URI`, `date et heure`, `énumération` (ses noms sont sous **Valeurs**),
  `liste de …`, ou `libre` pour une valeur que son lecteur prend telle qu'elle est écrite.
  `secret` marque une clé dont la valeur est un secret : aucun tableau n'en montre une, et les
  clés qui nomment une variable à sa place (`ApiKeyEnvVar`, `PasswordEnvVar`, …) la tiennent hors
  du fichier.
- **Défaut** est la valeur de la clé quand rien ne la règle ; `—` dit qu'elle n'en a pas, et le
  sens dit alors ce que fait une clé absente. Un défaut qui n'est pas une constante est dit en
  mots.
- **Sens** est la phrase que le code porte sur la propriété où la clé est lue, traduite.

Les tableaux sont produits depuis le code — le catalogue de réglages avec lequel un hôte refuse
une clé inconnue ([plus haut](#quand-un-réglage-est-refusé)) — et un test les y tient. **Le même
inventaire est dans l'outil, hors ligne** : `orkeon settings` liste les catégories,
`orkeon settings RateLimiting` les clés d'une section, `orkeon settings rate` tout ce à quoi un
mot correspond, et `orkeon settings --json` donne le catalogue entier à un programme
([CLI](./cli.md#orkeon-settings)).

Les options d'un fichier de crew — `maxRpm`, `llm: { profile: … }`, `memoryProvider:`, `rag:`,
`mounts:` — ne sont pas des réglages d'hôte : elles sont décrites dans le
[schéma YAML](../architecture/yaml-schema.md). Cette page en nomme une là où elle borne ou
sélectionne un réglage d'hôte.

Les variables d'environnement ont leur propre partie, après les catégories : celles qui portent
un réglage, celles qu'un binaire lit par leur nom — `ORKEON_DEBUG`, `TUI_DRIVER` — et celles
qu'un réglage nomme ([Variables d'environnement](#variables-denvironnement)).

## Modèles

Le modèle qu'appelle un hôte, la façon dont ses échanges sont journalisés, et ce que coûtent ses
tokens. Les plafonds sur les appels eux-mêmes sont sous [Débit et budgets](#débit-et-budgets).

<a id="llm-provider-llm-section"></a>

### `Llm`

La section `Llm` est lue par `RunnerHost.RegisterLlmProvider` et transformée en
`ILlmProvider` via `ILlmProviderFactory` ; le REPL la lit avec le même lecteur
(`LlmSettings.ReadDefault`). **Le provider est inféré automatiquement**, dans
l'ordre : motifs d'hôte du `BaseUrl` (p. ex. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/compatible OpenAI), puis motifs du nom de modèle, puis
forme de la clé API ; défaut `openai`. Une section sans `Model` tourne sur le modèle par défaut de
ce fournisseur (la colonne *Défaut (code)* du
[comparatif des fournisseurs](llm-providers-comparison.md#défauts-et-modèles-plus-récents--revue-des-catalogues-du-2026-09-19)),
jamais sur celui d'OpenAI posé sur un autre vendeur. Sans
section `Llm`, le
runtime dégrade vers le provider écho et avertit une fois. Voir
[Providers LLM](../architecture/llm-providers.md) ; des gabarits vivent dans
`examples/appsettings/*.json.example`.

**Cette section est
la base de chaque appel** à son fournisseur : un composant qui passe sa propre configuration — le
planificateur, la mémoire cognitive, les résumés de fenêtre de contexte et de RaggableTree, les
boucles d'agent hors du client de chat, un client de chat enregistré sans configuration — complète
celle de la section au lieu de la remplacer. Ce qu'il laisse vide est celui de la section (la clé,
`BaseUrl`, `TimeoutSeconds`, `Thinking`, `MaxTokens`…) ; ce qu'il fixe l'emporte (la température
0,3 du planificateur, le plafond de sortie d'une analyse). Le fournisseur d'un profil fait de même
avec ses propres clés.

<!-- settings:Llm -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ApiKey` | chaîne, secret | — | La clé d'API, en clair. Préférez `ApiKeyEnvVar` : une clé écrite ici est dans le fichier de réglages. |
| `ApiKeyEnvVar` | chaîne | — | Le nom de la variable d'environnement qui contient la clé d'API, lue quand `ApiKey` n'en résout aucune : l'environnement du processus, puis la portée utilisateur de Windows. |
| `AvailableModels` | liste de chaînes | `[]` | Les modèles qu'un `/model` scripté peut proposer : une liste, ou une seule valeur séparée par des virgules. |
| `BaseUrl` | chaîne | — | L'adresse du point d'accès du fournisseur, `http://` ou `https://`. Omise, l'adresse du fournisseur que désigne le nom du modèle ou la clé. |
| `Grammar` | booléen | `false` | Si le point d'accès honore une grammaire GBNF — un serveur compatible llama.cpp, comme Docker Model Runner. Ailleurs une grammaire est abandonnée avec un avertissement. |
| `MaxRetries` | entier | `10` | Combien de fois un appel qui échoue sur une erreur passagère est retenté ; `0` ne retente jamais. |
| `MaxTokens` | entier | — | Le plus grand nombre de tokens qu'une réponse peut contenir : un épinglage. Omise, une requête porte le maximum documenté de son modèle, et 4096 pour un modèle que le catalogue ne connaît pas. |
| `Model` | chaîne | — | Le nom du modèle. Omis, le fournisseur utilise son propre modèle par défaut. |
| `Profiles:<nom>:ApiKey` | chaîne, secret | — | La clé d'API, en clair. Préférez `ApiKeyEnvVar` : une clé écrite ici est dans le fichier de réglages. |
| `Profiles:<nom>:ApiKeyEnvVar` | chaîne | — | Le nom de la variable d'environnement qui contient la clé d'API, lue quand `ApiKey` n'en résout aucune : l'environnement du processus, puis la portée utilisateur de Windows. |
| `Profiles:<nom>:BaseUrl` | chaîne | — | L'adresse du point d'accès du fournisseur, `http://` ou `https://`. Omise, l'adresse du fournisseur que désigne le nom du modèle ou la clé. |
| `Profiles:<nom>:Grammar` | booléen | `false` | Si le point d'accès honore une grammaire GBNF — un serveur compatible llama.cpp, comme Docker Model Runner. Ailleurs une grammaire est abandonnée avec un avertissement. |
| `Profiles:<nom>:MaxRetries` | entier | `10` | Combien de fois un appel qui échoue sur une erreur passagère est retenté ; `0` ne retente jamais. |
| `Profiles:<nom>:MaxTokens` | entier | — | Le plus grand nombre de tokens qu'une réponse peut contenir : un épinglage. Omise, une requête porte le maximum documenté de son modèle, et 4096 pour un modèle que le catalogue ne connaît pas. |
| `Profiles:<nom>:Model` | chaîne | — | Le nom du modèle. Omis, le fournisseur utilise son propre modèle par défaut. |
| `Profiles:<nom>:StreamIdleSeconds` | entier | — | La plus longue absence de réponse tolérée entre deux fragments d'une réponse en flux, en secondes. Omise, rien ne la borne : `TimeoutSeconds` seule borne l'appel entier, en flux ou non. Un modèle qui réfléchit avant d'écrire peut rester silencieux un moment : ne la fixez qu'au-dessus de ce silence, ou coupez sa réflexion. |
| `Profiles:<nom>:Temperature` | nombre | — | La température d'échantillonnage. Omise, aucune n'est envoyée et le modèle applique la sienne. |
| `Profiles:<nom>:Thinking:Effort` | chaîne | — | L'intensité de sa réflexion, dans les mots du fournisseur (`low`, `medium`, `high`…). Omise, celle du fournisseur. |
| `Profiles:<nom>:Thinking:Enabled` | booléen | — | Si le modèle réfléchit avant de répondre. Omise, le comportement propre du fournisseur. |
| `Profiles:<nom>:TimeoutSeconds` | entier | `30` | La durée maximale d'un appel, en secondes. Trop courte à son défaut pour un modèle qui réfléchit avant de répondre : écrivez 600 pour un tel modèle, ou coupez sa réflexion. |
| `StreamIdleSeconds` | entier | — | La plus longue absence de réponse tolérée entre deux fragments d'une réponse en flux, en secondes. Omise, rien ne la borne : `TimeoutSeconds` seule borne l'appel entier, en flux ou non. Un modèle qui réfléchit avant d'écrire peut rester silencieux un moment : ne la fixez qu'au-dessus de ce silence, ou coupez sa réflexion. |
| `Temperature` | nombre | — | La température d'échantillonnage. Omise, aucune n'est envoyée et le modèle applique la sienne. |
| `Thinking:Effort` | chaîne | — | L'intensité de sa réflexion, dans les mots du fournisseur (`low`, `medium`, `high`…). Omise, celle du fournisseur. |
| `Thinking:Enabled` | booléen | — | Si le modèle réfléchit avant de répondre. Omise, le comportement propre du fournisseur. |
| `TimeoutSeconds` | entier | `30` | La durée maximale d'un appel, en secondes. Trop courte à son défaut pour un modèle qui réfléchit avant de répondre : écrivez 600 pour un tel modèle, ou coupez sa réflexion. |
<!-- /settings -->

- **La clé.** `ApiKeyEnvVar` nomme la variable d'environnement qui contient la clé —
  [plus bas](#la-clé-dapi-apikey-apikeyenvvar) ; la variable où vit conventionnellement la clé de
  chaque fournisseur, et les noms qu'on confond avec elle, sont dans le
  [comparatif des fournisseurs](llm-providers-comparison.md#clés-dapi--la-variable-par-fournisseur).
  `ApiKey` est la clé en clair — déconseillé.
- **`Temperature`.** Omise, rien n'est envoyé et le modèle applique la sienne — souvent 1 ;
  écrivez `0.7` pour garder l'ancien défaut du moteur (GAP-36).
- **`Thinking:Enabled`, `Thinking:Effort`** concernent les providers à raisonnement.
- **`Profiles:<nom>`** porte les profils nommés qu'une crew choisit par agent ou par tâche,
  chacun avec les mêmes clés que la section elle-même ([plus bas](#profils-nommés-llmprofiles)) ;
  Orkeon Studio en écrit un par réglage de modèle, avec le nom de la variable qui contient sa clé,
  jamais la clé.
- **`AvailableModels`** est la liste de modèles qu'une commande REPL scriptée `/model` peut
  proposer (tableau de chaînes, ou une chaîne séparée par des virgules), lue par
  `AddOrkeonSessionTools(configuration)`.

#### Le délai, et les modèles qui réfléchissent

`TimeoutSeconds` vaut 30 s par
défaut, trop court pour un modèle qui réfléchit avant de répondre (Kimi K2.6, DeepSeek V4 et GLM
le font par défaut) : mettez 600 s, ou coupez la réflexion avec `Thinking:Enabled = false`. Un
appel qui atteint le délai est réessayé une fois, puis fait échouer sa tâche avec un message qui
nomme le réglage — il n'est jamais rapporté comme une réponse vide (LLM-11).

Le délai borne l'appel entier, en flux ou non : un appel en flux (tout run Studio ou `--events`)
n'était borné que jusqu'à l'arrivée de ses en-têtes, et un modèle qui réfléchissait plusieurs
minutes avant son premier jeton suspendait le run (LLM-12). `StreamIdleSeconds` ajoute une
seconde borne, omise par défaut : la plus longue absence de réponse entre deux fragments du flux.
Un modèle qui réfléchit avant d'écrire peut rester silencieux un moment : fixez-la au-dessus de
ce silence, ou coupez la réflexion. L'une ou l'autre borne atteinte est un appel échoué dont le
message nomme le réglage — jamais une réponse vide.

#### `MaxTokens` est un épinglage

`MaxTokens` est un
**épinglage** : absent, la requête porte le **maximum de sortie documenté** du modèle,
lu dans le catalogue `LlmModelOutputLimits` (128K sur `gpt-5.6-sol` et la génération Claude 5,
384K sur `deepseek-flash`, 131 072 sur les familles GLM-5 et Qwen 3.7/3.8, 65 536 sur Gemini 3.x
Flash — voir la [table des plafonds](llm-providers-comparison.md#plafonds-de-sortie--le-maximum-documenté-par-modèle-llm-10)),
aucun plafond quand le fournisseur n'en documente pas (Mistral, un Ollama local), et **4096
seulement pour un modèle inconnu du catalogue** — la valeur que le moteur envoyait pour tous
les modèles, qu'un modèle raisonneur dépense à réfléchir avant d'écrire un mot et qui revient
vide (une réponse finale vide fait échouer la tâche au lieu de passer pour une tâche
accomplie). Épinglez-le quand le modèle n'est pas au catalogue ou pour un plafond plus serré ;
un profil de modèle Studio l'épingle en `ORKEON_Llm__MaxTokens`, et l'éditeur de profil dit ce
qu'un champ vide signifie pour le modèle choisi. Un plafond du catalogue que le point d'accès
refuse est rejoué une fois sans le champ, avec un avertissement qui nomme le modèle.

#### `Grammar` est pour un serveur compatible llama.cpp

`Grammar` (défaut `false`) :
ne le passez à `true` que lorsque `BaseUrl` désigne un serveur compatible llama.cpp (Docker
Model Runner, `llama-server`) — le seul genre de point d'accès qui honore le champ GBNF
`grammar` que produit un livrable `structured_output` ; ailleurs la grammaire est abandonnée avec
un avertissement qui nomme la clé ([comparatif des fournisseurs](llm-providers-comparison.md)).

### La clé d'API (`ApiKey`, `ApiKeyEnvVar`)

Une section n'a jamais à contenir sa clé. `ApiKeyEnvVar` nomme la variable d'environnement qui la
contient — le nom, jamais la clé —, dans la section `Llm` comme dans chaque profil :
`"ApiKeyEnvVar": "DEEPSEEK_API_KEY"`. Un run prend, dans cet ordre :

1. **Une clé que la configuration résout** : `ApiKey` dans le fichier, `ORKEON_Llm__ApiKey`
   (`ORKEON_Llm__Profiles__<id>__ApiKey` pour un profil), ou ce qu'un lancement d'Orkeon Studio pose
   sur son processus enfant. Une installation existante garde la clé qu'elle avait, et
   l'environnement l'emporte toujours sur le fichier.
2. **Sinon la variable que nomme `ApiKeyEnvVar`**, lue dans l'environnement du processus, puis —
   sous Windows — dans la portée Utilisateur persistante (`HKCU\Environment`), où Orkeon Studio
   mémorise une clé : un terminal ouvert avant la mémorisation, ou une équipe planifiée, la trouve
   quand même. La valeur est lue, jamais recopiée dans le processus : ce que le run lance — un outil
   shell, un serveur MCP stdio, le bac à sable de code — n'hérite d'aucune clé qu'il n'aurait pas eue.
   Une portée Utilisateur illisible (registre refusé, compte de service virtuel) vaut une variable
   absente. Linux et macOS n'ont pas de portée Utilisateur : le processus seul.
3. **Sinon pas de clé**, et chaque appel répond qu'une clé d'API est requise, comme sans la
   référence.

Le nom est lu tel qu'il est écrit : Windows compare les noms de variable sans la casse, Linux
avec. Une référence qui ne peut pas être un nom de variable — un `=`, une espace, un saut de
ligne — refuse le démarrage de l'hôte, en nommant le chemin de configuration et jamais la valeur,
qui peut être une clé collée dans le mauvais champ. Une `ApiKey` écrite comme un gabarit `${NOM}` —
la forme que portaient les anciens gabarits d'`examples/appsettings`, que rien n'a jamais développée,
si bien que le texte partait comme clé — refuse aussi le démarrage, avec la correction :
`"ApiKeyEnvVar": "NOM"`. Au démarrage, l'hôte runner dit d'où vient chaque clé, jamais la clé ni le
nom de la variable — `LLM resolved: … apiKey=from the variable named by Llm:ApiKeyEnvVar (user
environment)`, puis une ligne `LLM profile <id>: apiKey=…` par profil offert aux crews (`from
configuration (…:ApiKey)`, `from the variable named by … (process environment)`, `none`) — et
avertit une fois par section dont la référence nomme une variable posée nulle part, par son chemin,
sur son journal et sur stderr ; `orkeon doctor`, la sonde d'`orkeon init` et le REPL disent la même
chose. Un profil que cache la liste blanche d'`orkeon-host` est nommé sur une ligne à part et jamais
signalé : aucune crew ne peut le nommer ([hôte de service](../architecture/service-host.md)). Une valeur vide se lit comme
absente, pour chaque clé de la section (`Thinking:Effort` comprise) : un lancement Studio vide les
champs du défaut que le réglage de son équipe ne fixe pas, et une section dont toutes les valeurs
sont vides ne configure aucun défaut — le provider écho, avec son avertissement.
`orkeon init --api-key-env <nom>` et `orkeon-studio-config` écrivent `Llm:ApiKeyEnvVar` ; Orkeon
Studio l'écrit pour le réglage élu et pour chaque profil qu'il possède
([plus bas](#studio-écrit-cette-section)). La référence peut nommer n'importe quelle variable : un
fichier de réglages est une configuration de confiance ([Sécurité](../architecture/security.md#clés-llm--le-fichier-de-réglages-nomme-la-variable)).

### Profils nommés (`Llm:Profiles`)

Un hôte peut offrir plusieurs fournisseurs. Chaque enfant de `Llm:Profiles` est un **profil** :
un nom, et un fournisseur décrit avec exactement les clés de la section `Llm` (`BaseUrl`,
`ApiKeyEnvVar`, `ApiKey`, `Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`,
`Thinking`, `Grammar`). La section `Llm` elle-même reste le profil **par défaut** — celui de tout agent qui
n'en nomme pas d'autre. Une crew choisit un profil **par son nom**, jamais par une clé ou une URL :
`llm: { profile: claude }` sur la crew, un agent ou le `llm_override` d'une tâche en YAML,
`llm.profile("claude")` sur un agent et `taskBuilder().withProfile("claude")` sur une tâche en `.ork.ts` ([YAML et builders](../getting-started/yaml-and-builders.md#un-fournisseur-par-agent-profils)).

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash", "ApiKeyEnvVar": "DEEPSEEK_API_KEY",
    "Profiles": {
      "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5", "ApiKeyEnvVar": "ANTHROPIC_API_KEY" },
      "local":  { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
    }
  }
}
```

Les clés restent hors des fichiers de la même façon : le profil nomme sa variable
(`ApiKeyEnvVar`), et `ORKEON_Llm__Profiles__claude__ApiKey` l'emporte sur elle quand elle est
posée. Le
fournisseur de chaque profil est construit une fois, au premier usage, compté comme celui par
défaut — les relevés `cost.updated` d'un run nomment le fournisseur de chaque appel, et la barre
d'état de Studio ventile les jetons par fournisseur. Les profils sont validés au démarrage de
l'hôte : `default` est un nom réservé (il désigne la section `Llm`, et une crew peut le nommer pour
ramener un agent au défaut), et une `BaseUrl` invalide ou une valeur qui n'est pas un nombre fait
échouer le démarrage en nommant la clé à corriger. Une crew qui nomme un profil que l'hôte ne
définit pas **échoue au chargement**, et le message liste les profils offerts. **Un modèle non
précisé est celui du profil, sur tous les chemins** : un bloc `llm:` YAML ou `.ork.ts` sans
`model`, le `.Thinking()` ou le `.MaxOutputTokens(n)` d'un agent C#, `llm.default_` sur un hôte qui
ne configure aucun modèle, le planificateur, les boucles d'agent hors client de chat et les appels
d'analyse de la mémoire cognitive laissent tous le modèle au fournisseur qu'ils atteignent, qui
envoie le modèle que son profil configure, sinon son propre défaut — jamais un modèle vide, jamais
celui d'OpenAI sur un autre vendeur.

Qui tourne sur quel profil :

- **Les tours d'un agent** — sur son profil `llm:`, ou le `llm_override` de sa tâche pour cette
  tâche (`taskBuilder().withProfile(nom)` en `.ork.ts`). Un agent construit en C# peut porter à la
  place son propre fournisseur (`AgentBuilder.WithLlm(fournisseur)`, ou un agent Microsoft Agent
  Framework par `WithAgentFrameworkAgent` — `Agent.Llm`) : ses tours, sa correction de sortie et son
  bulletin y tournent, sur un client que l'hôte construit une fois par fournisseur et compte comme le
  travail de l'agent ; le profil du `llm_override` d'une tâche déplace toujours cette tâche, et l'agent
  ne peut pas nommer en plus un profil (le build refuse les deux).
- **Le manager** d'une crew hiérarchique (et celui qui distribue les tâches d'une crew autonome) —
  sur le LLM que la crew lui donne : en C#, le fournisseur que pose `CrewBuilder.WithManagerLlm`,
  compté comme celui de l'hôte ; sinon le fournisseur que porte son agent manager (`WithLlm`,
  `WithAgentFrameworkAgent`), compté de même ; sinon le bloc `llm:` de son agent manager, profil et
  modèle ; sinon le profil par défaut.
- **Le sous-système RAG** — génération citée, transformateurs de requête, reranker listwise,
  évaluateur et vérificateur d'ancrage du graphe correctif, classifieur `llm` et juge d'évaluation —
  sur le profil que nomme `Orkeon:Rag:LlmProfile` (absent : le défaut). Un nom que l'hôte n'offre
  pas fait refuser le démarrage de l'hôte, en listant ceux qu'il offre ; le fournisseur du profil est
  construit au premier appel RAG qui en a besoin, jamais au démarrage.
- **Le planificateur, le Guardian, les juges d'`Evaluation` et les analyses de la mémoire
  cognitive** restent sur le profil par défaut : ce sont des services de l'hôte, pas des rôles de
  crew (`CrewBuilder.WithPlanningLlm` garde la main en C#, avec `.Planning()`, ses appels comptés
  comme ceux de l'hôte).

Une section qui ne contient que `Profiles` ne configure aucun fournisseur par défaut — le défaut
est alors le provider écho, avec l'avertissement habituel. `orkeon-host` peut restreindre les
profils que ses crews peuvent nommer (`Orkeon:Host:LlmProfiles`, plus bas) ; la restriction vaut
aussi pour le profil RAG et pour celui d'un agent manager.

**Un run sur un autre profil.** `orkeon run --llm-profile <id>` élit un profil comme défaut du run
([CLI](./cli.md#orkeon-run)) : pour ce run, la section `Llm` est ce profil, en entier — ce qu'il laisse
vide reste vide, sa clé est la sienne —, et chaque rôle ci-dessus qui tourne sur le défaut tourne
dessus. Un identifiant que la configuration ne définit pas refuse le run, en listant ceux qu'elle
définit. C'est ce qu'Orkeon Studio écrit dans les lanceurs d'une équipe planifiée.

#### Studio écrit cette section

Aucune section `Orkeon:Studio` n'existe, et Studio n'écrit ici aucune de ses propres préférences : ce fichier est celui que lit `orkeon run`, et l'hôte refuse au démarrage toute section `Orkeon:*` qu'il ne connaît pas — une clé `Orkeon:Studio:TeamsRoot` ferait échouer chaque run de la machine. Le dossier des équipes que Studio liste se choisit par la variable `ORKEON_STUDIO_TEAMS_ROOT`, l'option `--teams-root` ou Réglages › Studio, dont la préférence vit dans le `ui-preferences.json` propre à Studio (STUDIO-61, [Studio](../architecture/studio.md)).

Les réglages de modèle d'Orkeon Studio sont les profils de l'hôte (STUDIO-48). Chaque réglage de
l'onglet Modèle d'IA qui nomme un fournisseur est le profil nommé d'après lui par la règle des
noms de dossier des équipes — « Claude » donne `claude`, « Z.AI » donne `z-ai` —, et sa carte comme
son éditeur montrent ce qu'une crew écrit, `profile: claude`. Créer, modifier, renommer ou
supprimer le réglage écrit, déplace ou retire son entrée : chaque champ que le réglage épingle
(`BaseUrl`, `Model`, `Temperature`, `TimeoutSeconds`, `MaxTokens`, `Thinking`) et la variable où
Studio mémorise sa clé (`ApiKeyEnvVar`, aucune pour un réglage sans clé), jamais la clé, et une
clé que Studio ne modélise pas (`MaxRetries`, `Grammar`) reste où elle est. Un réglage Docker Model
Runner écrit `"ApiKey": "not-needed"` là où son entrée ne porte aucune clé, comme `orkeon init`
(STUDIO-54) : le run lit ce serveur comme OpenAI, dont le dialecte refuse d'appeler sans clé, et le
serveur n'en vérifie aucune — une clé déjà là reste. Un réglage sans
modèle (la carte écho), ou dont le nom ne garde aucune lettre ni aucun chiffre ASCII, n'est offert
à aucune crew ; `default`, un nom auquel un autre réglage répond déjà et un nom qui prendrait la
place d'une entrée écrite à la main sont refusés.

- **Les clés.** Le fichier nomme la variable où Studio mémorise chaque clé — `ApiKeyEnvVar`, le nom
  conventionnel du fournisseur (`DEEPSEEK_API_KEY`, `ZAI_API_KEY`) —, et un run hors de Studio la lit
  ([plus haut](#la-clé-dapi-apikey-apikeyenvvar)) : `orkeon run` dans un terminal, une équipe
  planifiée, `orkeon-host` lancé par l'utilisateur, `orkeon-repl`. L'éditeur nomme cette variable en
  mode expert. Un lancement depuis Studio — un run, un essai, l'assistant de création — pose
  toujours chaque réglage sur son processus enfant en `ORKEON_Llm__Profiles__<id>__*`, clé comprise :
  il ne dépend donc ni de l'enregistrement du fichier ni du fichier de réglages qu'il lit. Une
  équipe d'un dossier Orkeon Workshop — son dossier juste sous la racine des équipes, avec
  `settings/<slug>/appsettings.json` à côté de cette racine — est lancée sur ce fichier, passé en
  `--settings` sauf si le mode Expert en épingle un autre (STUDIO-62) : sa section `Llm` et son
  `RateLimiting` s'appliquent tels quels quand la carte de l'équipe ne nomme aucun réglage ; une
  carte qui nomme un réglage de cette machine pose par-dessus les `ORKEON_Llm__*` de ce réglage,
  clé par clé.
- **Le défaut.** Le réglage élu est écrit en entier dans `Llm` — chaque champ qu'il épingle et son
  `ApiKeyEnvVar`, un champ qu'il laisse vide retirant sa clé ; `ApiKey`, `MaxRetries`, `Grammar`,
  `AvailableModels` et `Profiles` restent. Seule la clé de remplacement de Docker Model Runner suit la
  carte (STUDIO-54) : un réglage Docker Model Runner élu écrit `"ApiKey": "not-needed"` là où `Llm` ne
  porte aucune clé, et l'élection de toute autre carte retire exactement cette valeur — laissée, elle
  passerait avant l'`ApiKeyEnvVar` du réglage élu ; le fichier qu'a écrit
  `orkeon init --preset docker-model-runner`, puis DeepSeek élu dans Studio, compris. Toute autre
  `ApiKey` reste. Une équipe lancée sur un autre réglage pose tous ces champs sur son processus enfant
  en `ORKEON_Llm__*`, valeur ou vide, `ORKEON_Llm__ApiKeyEnvVar` compris : une équipe sur Z.AI dont la
  clé n'est pas mémorisée échoue sans clé au lieu d'envoyer à Z.AI la clé DeepSeek du défaut ; une
  équipe sur un réglage Docker Model Runner pose `ORKEON_Llm__ApiKey=not-needed`. Une équipe sur le
  réglage élu lui-même pose ce qu'il fixe.
  `orkeon-studio-config` modifie `Llm` champ par champ ; une élection faite ensuite dans Studio
  réécrit les champs qu'elle possède.
- **Un fichier plus ancien guérit au geste suivant.** Une entrée que Studio possède sans sa
  référence, et une section `Llm` sans celle du réglage élu, sont réécrites — le réglage entier — au
  changement suivant sur les réglages de modèle (modifier puis enregistrer un réglage), jamais au
  démarrage ; de même une entrée et une section `Llm` dont la clé ne s'accorde pas à leur carte
  (STUDIO-54) : celle d'un réglage Docker Model Runner sans la clé de remplacement la reçoit, celle
  d'une autre carte qui la porte la perd — une seule fois, puisque l'écrire les accorde. Un réglage
  écrit avant dans une autre langue, ou dont le fournisseur a été vidé par le défaut, retrouve sa
  carte quand Studio lit `studio-model-profiles.json`, avec la variable de clé de cette carte, et le
  nom de la carte rejoint le fichier à ce même geste.
- **Une équipe planifiée.** Les lanceurs d'une équipe que Studio a adoptée portent le réglage de
  l'équipe en `--llm-profile <id>` — son entrée ici —, si bien que l'exécution que planifie le système
  d'exploitation le prend, comme un lancement depuis Studio
  ([Studio](../architecture/studio.md#une-équipe-planifiée-tourne-comme-studio-la-lance-studio-50)).
  Cette exécution lit ce fichier tel qu'enregistré, les clés là où il les nomme : la carte de l'équipe
  dit quand ce fichier ne définit pas encore son entrée, en garde une version ancienne, ou quand le
  réglage est un réglage qu'aucune exécution hors de Studio ne peut prendre — l'exécution prend alors
  le défaut —, et « Installer la planification » enregistre d'abord les réglages quand ils ne l'ont
  pas. Un réglage renommé dans Studio emporte ses équipes
  ([Studio](../architecture/studio.md#une-équipe-garde-son-réglage-et-ses-dossiers-studio-52)).
- **« Autre compatible OpenAI ».** Un réglage créé depuis cette carte range sa clé dans
  `ORKEON_CUSTOM_LLM_API_KEY`, partagée par les réglages de la carte. Un réglage créé avant garde
  `ORKEON_Llm__ApiKey` — la clé native du défaut pour le runtime : mémorisée en portée Utilisateur,
  elle devient la clé du défaut de chaque run de l'utilisateur, quels que soient son fichier de
  réglages et son adresse. Sa carte le dit en mode expert quand il n'est pas le défaut, avec le
  chemin du retour : le recréer depuis la carte, supprimer l'ancien, retirer `ORKEON_Llm__ApiKey` de
  l'environnement utilisateur. Studio ne le migre jamais de lui-même : il ne distingue pas sa propre
  écriture d'une variable posée à la main.
- **Les entrées écrites à la main** — aucun réglage ne les possède — sont listées en lecture seule
  sous les réglages, et Studio ne les réécrit ni ne les retire jamais. La propriété tient au nom du
  réglage : un identifiant est à Studio quand un réglage de `studio-model-profiles.json` y répond.
- **Le profil du RAG** (`Orkeon:Rag:LlmProfile`) se choisit sur le même onglet, en mode expert,
  parmi les profils ; il suit son réglage lors d'un renommage et revient au défaut quand le réglage
  est supprimé. Studio avertit, avant d'enregistrer, d'un fichier dont le profil RAG nomme un profil
  que le fichier ne définit pas.

`orkeon-studio-config` montre la section — chaque profil avec la variable qui contient sa clé,
`key: ZAI_API_KEY` — et le profil du RAG en lecture seule.

### `LlmLogging`

Règle la capture des échanges LLM. Lue uniquement quand le run passe `--llm-log`, qui dit où va
le journal (`RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`). Un interrupteur qui
ne vaut ni `true` ni `false`, une longueur qui n'est pas un nombre entier, ou une autre clé
refuse le démarrage d'un run qui passe `--llm-log`.

<!-- settings:LlmLogging -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `FullEmbeddingLog` | booléen | `true` | Si un vecteur d'embedding est journalisé en entier. `false` remplace chacun par ses deux premières et ses deux dernières valeurs : l'échange garde sa forme et perd ses mégaoctets. |
| `LogStreamingExchanges` | booléen | `true` | Si un échange en flux est journalisé : sa requête, et la réponse une fois le flux terminé. |
| `MaxBodyLengthChars` | entier | `0` | Le plus grand nombre de caractères journalisés d'un corps de requête ou de réponse ; `0` le journalise en entier. |
<!-- /settings -->

### `Orkeon:CostTracking`

Suivi des coûts LLM : un budget par défaut par crew (`DefaultCrewBudget`) et le prix d'un modèle,
par nom ou par motif (`CustomPricings`). Enregistré par `AddOrkeonInfrastructure()` ; la section
gouverne le comportement. Les plafonds sur le nombre d'appels sont
[`RateLimiting`](#ratelimiting).

<!-- settings:Orkeon:CostTracking -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `CustomPricings:<i>:CompletionPricePerMillion` | nombre | `0` | Coût par million de tokens de complétion (sortie), dans la devise indiquée. |
| `CustomPricings:<i>:Currency` | chaîne | `"USD"` | Devise des prix (défaut : USD). |
| `CustomPricings:<i>:EffectiveDate` | date et heure | l'instant où la valeur est lue | Date à laquelle ce prix est entré en vigueur. |
| `CustomPricings:<i>:EmbeddingPricePerMillion` | nombre | — | Coût par million de tokens d'embedding (null si ce n'est pas un modèle d'embedding). |
| `CustomPricings:<i>:ModelPattern` | chaîne | `""` | Nom ou motif du modèle (p. ex. "gpt-4o", "claude-3-opus"). |
| `CustomPricings:<i>:PromptPricePerMillion` | nombre | `0` | Coût par million de tokens de prompt (entrée), dans la devise indiquée. |
| `DefaultCrewBudget:AlertThresholdPercent` | nombre | `80` | Pourcentage du budget à partir duquel une alerte d'avertissement est émise (défaut : 80 %). |
| `DefaultCrewBudget:MaxCallsPerMinute` | entier | — | Nombre maximal d'appels LLM par minute (null = illimité). |
| `DefaultCrewBudget:MaxCostUsd` | nombre | — | Coût total maximal en USD (null = illimité). |
| `DefaultCrewBudget:MaxTokens` | entier | — | Nombre total maximal de tokens (null = illimité). |
| `Enabled` | booléen | `true` | Si le suivi des coûts est activé (défaut : true). |
<!-- /settings -->

### `Orkeon:TokenCounter`

Estimation des tokens. Enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le
comportement.

<!-- settings:Orkeon:TokenCounter -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `CharsPerToken` | nombre | `3.5` | Nombre moyen de caractères par token (défaut : 3,5). |
| `SpecialTokenOverhead` | entier | `2` | Nombre de tokens spéciaux de surcoût (p. ex. BOS/EOS) par requête (défaut : 2). |
| `TokensPerMessage` | entier | `4` | Nombre de tokens de surcoût par message au format chat (défaut : 4). |
| `TokensPerReply` | entier | `3` | Nombre de tokens de surcoût pour l'amorce de la réponse (défaut : 3). |
<!-- /settings -->

### `Evaluation`

Avec `EnableLlmJudge`, l'`IEvaluationSuite` par défaut exécute aussi les juges LLM de cohérence,
fluidité et ancrage sur l'`IChatClient` enregistré. Liée par
`AddOrkeonInfrastructure(configuration)`, ou `AddOrkeonEvaluation(configuration)` (idempotent : un
second appel n'enregistre aucun évaluateur deux fois).

<!-- settings:Evaluation -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonEvaluation()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `EnableLlmJudge` | booléen | `false` | Quand la clé est vraie, la suite d'évaluation par défaut exécute aussi les évaluateurs « LLM juge » (cohérence, fluidité, ancrage). Requiert un IChatClient enregistré dans le conteneur. Défaut : false. Liée depuis la section `Evaluation`. |
<!-- /settings -->

## Débit et budgets

Deux choses différentes s'appellent « rate limiting ». **`RateLimiting`** plafonne les appels au
modèle, et tout hôte livré l'applique. **`ToolRateLimiting`** plafonne les appels d'outils et
**`TokenBudget`** les tokens et le coût d'un agent ou d'une crew : les deux ne prennent effet que
dans un hôte écrit en C# qui appelle `AddOrkeonToolRateLimiting()` — aucun binaire livré ne les
lit. Une crew borne un agent par `maxRpm` ([schéma YAML](../architecture/yaml-schema.md)), le plus
strict de lui et de `RateLimiting:AgentRequestsPerMinute` l'emportant ; ce que coûtent les appels
est suivi par [`Orkeon:CostTracking`](#orkeoncosttracking).

### `RateLimiting`

Les plafonds de l'hôte sur les appels au modèle : chaque appel au modèle de l'hôte compte une
fois, à l'entrée de son fournisseur — tours d'agent, manager, planificateur, RAG, juges, mémoire
cognitive, `ctx.llm` des scripts (pas les embeddings) — face à `GlobalRequestsPerMinute`,
`ProviderRequestsPerMinute`, `MaxConcurrentRequests` et `QueueLimit` ; `AgentRequestsPerMinute`
plafonne chaque instance d'agent dans sa propre fenêtre, avec son `maxRpm`, le plus strict
l'emportant, et une requête de trop attend son tour ([sécurité](../architecture/security.md)).
Liée par `AddOrkeonInfrastructure()` (`ILlmRateLimiter` ; `RateLimitingOptions` dans
`Orkeon.Application.Configuration`).

<!-- settings:RateLimiting -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AgentRequestsPerMinute` | entier | `20` | Nombre maximal de requêtes au modèle par minute pour chaque agent — chaque instance d'agent, pas chaque rôle : il borne la fenêtre propre de l'agent avec son `maxRpm`, le plus strict l'emportant, et une requête de trop attend son tour au lieu d'échouer. Lu par l'orchestrateur d'exécution et le manager. |
| `GlobalRequestsPerMinute` | entier | `60` | Nombre maximal de requêtes au modèle par minute, tous fournisseurs et agents confondus. Une requête de trop est retenue dans une file de `QueueLimit`, puis retentée, puis mise en échec. |
| `MaxConcurrentRequests` | entier | `0` | Nombre maximal de requêtes au modèle simultanées (en cours). 0 signifie aucune limite de concurrence (limitation de débit seule). |
| `ProviderRequestsPerMinute` | entier | `30` | Nombre maximal de requêtes au modèle par minute et par fournisseur, retenues et refusées comme pour le plafond global. |
| `QueueLimit` | entier | `5` | Nombre maximal de requêtes que les plafonds global, par fournisseur et de concurrence retiennent en file quand ils sont atteints ; une requête au-delà est refusée, puis retentée. |
<!-- /settings -->

### `ToolRateLimiting`

Rate limits par outil. Opt-in : `AddOrkeonToolRateLimiting()`, qui lie depuis l'`IConfiguration`
enregistrée (voir [sous-systèmes opt-in](./opt-in-subsystems.md)).

<!-- settings:ToolRateLimiting -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonToolRateLimiting()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `DefaultToolRequestsPerMinute` | entier | `30` | Nombre maximal de requêtes par minute, par défaut, pour les outils sans limite propre. |
| `GlobalToolRequestsPerMinute` | entier | `120` | Nombre maximal de requêtes d'outils par minute, tous outils confondus. |
| `ToolSpecificLimits:<nom>` | entier | FileWriteTool = 15, HttpApiTool = 20, SecureCodeInterpreterTool = 5, WebScrapeTool = 10 | Limites de débit par outil, indexées par le nom de l'outil. |
<!-- /settings -->

### `TokenBudget`

Budgets de tokens. Même opt-in que [`ToolRateLimiting`](#toolratelimiting).

<!-- settings:TokenBudget -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonToolRateLimiting()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `MaxCostPerCrew` | nombre | `10.0` | Coût estimé maximal par crew, en USD. 0 signifie illimité. |
| `MaxTokensPerAgent` | entier | `100000` | Nombre maximal de tokens par agent. 0 signifie illimité. |
| `MaxTokensPerCrew` | entier | `500000` | Nombre maximal de tokens par crew. 0 signifie illimité. |
<!-- /settings -->

## Mémoire et vecteurs

Où vivent les souvenirs, et comment les textes sont vectorisés et cherchés. `Memory:Provider`
nomme le provider ; sa connexion est la section propre de ce provider.

### `Memory`

`Memory:Provider` est le TYPE du provider mémoire de l'application (`inmemory`, `redis`,
`sqlite`, `chromadb`, `pinecone`, `lancedb`, ou un alias : `in-memory`, `chroma`, `lance` ;
absent → in-memory). Sa connexion est la section propre de ce provider (`Orkeon:Redis`,
`Orkeon:Sqlite`, … plus bas). C'est aussi là que vit la mémoire d'une crew nommée avec
`memory: true` et sans `memoryProvider:` — voir
[Système de mémoire](../architecture/memory-system.md#sélection-par-configuration). Un autre nom,
`lancedb` sans `Orkeon:LanceDb:Endpoint` ou `pinecone` sans `Orkeon:Pinecone:ApiKey` (ou `Host`)
refuse le démarrage : il ne tourne plus sur le provider volatil
([quand un réglage est refusé](#quand-un-réglage-est-refusé)). Liée par
`AddOrkeonInfrastructure()`.

<!-- settings:Memory -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Provider` | chaîne | — | Le type du provider — `inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`, ou un alias. Absent, c'est le provider en mémoire. |
<!-- /settings -->

### `Orkeon:Redis`

Provider mémoire Redis. `ConnectionString` vaut `localhost:6379` par défaut ; la connexion
s'ouvre au premier usage. Lue par `MemoryProviderFactory` sur tout chemin qui sélectionne
`redis` : `Memory:Provider`, le `memoryProvider:` d'une crew, `Orkeon:Rag:Provider`,
`AddOrkeonRedisMemory`. Liée par `AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Redis -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ConnectionString` | chaîne, secret | — | La chaîne de connexion StackExchange.Redis (p. ex. `localhost:6379`, ou `redis.internal:6379,password=…,ssl=true`). |
| `KeyPrefix` | chaîne | `"orkeon:memory:"` | Le préfixe qui range dans un même espace toutes les clés qu'écrit le provider. |
<!-- /settings -->

### `Orkeon:Sqlite`

Provider mémoire SQLite. `ConnectionString` vaut `Data Source=:memory:` par défaut ; une
`Data Source` fichier est un chemin virtuel sur un montage inscriptible. Lue par
`MemoryProviderFactory` sur tout chemin qui sélectionne `sqlite`. Liée par
`AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Sqlite -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ConnectionString` | chaîne, secret | — | La chaîne de connexion SQLite (p. ex. `"Data Source=/data/orkeon-memory.db"` ou `"Data Source=:memory:"`). Par défaut une base en mémoire (`Data Source=:memory:`) ; une `Data Source` fichier est un chemin virtuel, qui doit désigner un montage inscriptible. |
| `DefaultTopK` | entier | `10` | Le nombre de résultats renvoyés par défaut par les requêtes vectorielles. |
| `MinSimilarityScore` | nombre | `0` | Le seuil minimal de score de similarité des résultats de recherche vectorielle. |
| `TableName` | chaîne | `"memory_items"` | Le nom de la table où sont stockés les éléments de mémoire. Doit correspondre à `^[A-Za-z_][A-Za-z0-9_]*$` : l'identifiant est interpolé dans des instructions SQL. Vérifié au démarrage d'un hôte, puis de nouveau à la construction. |
<!-- /settings -->

### `Orkeon:ChromaDb`

Serveur ChromaDB. Lue par `MemoryProviderFactory` sur tout chemin qui sélectionne `chromadb`.
Liée par `AddOrkeonInfrastructure()` ; `AddOrkeonChromaDb(configuration)` (appelée par
`AddOrkeonInfrastructure(configuration)` quand la section existe) expose aussi le
`ChromaDbMemoryProvider` partagé par sa classe.

<!-- settings:Orkeon:ChromaDb -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `BaseUrl` | URI | `"http://localhost:8000/"` | L'URL de base du serveur ChromaDB. |
| `CollectionName` | chaîne | `"orkeon_memories"` | Le nom de la collection où sont stockés les souvenirs. |
| `Database` | chaîne | `"default_database"` | La base de données adressée par l'API v2 (`/api/v2/tenants/{tenant}/databases/{database}/...`). Vaut `default_database` par défaut. Une base autre que celle par défaut doit déjà exister sur le serveur. |
| `DefaultTopK` | entier | `10` | Le nombre de résultats renvoyés par défaut par les requêtes. |
| `Tenant` | chaîne | `"default_tenant"` | Le tenant adressé par l'API v2 (`/api/v2/tenants/{tenant}/...`). Vaut `default_tenant` par défaut. Un tenant autre que celui par défaut doit déjà exister sur le serveur. |
<!-- /settings -->

### `Orkeon:Pinecone`

Index Pinecone. `Host` est facultatif : sans lui, un appel `describe_index` résout l'hôte de
l'index au premier usage. Lue par `MemoryProviderFactory` sur tout chemin qui sélectionne
`pinecone`. Liée par `AddOrkeonInfrastructure()`, et par `AddOrkeonPinecone(configuration)`.

<!-- settings:Orkeon:Pinecone -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ApiKey` | chaîne, secret | — | La clé d'API Pinecone. |
| `Host` | chaîne | — | L'hôte de l'index, tel que Pinecone le donne (p. ex. `orkeon-memories-abc1234.svc.aped-4627-b74a.pinecone.io` ; le schéma est facultatif). Vide, le provider interroge une fois le plan de contrôle, au premier usage (`GET https://api.pinecone.io/indexes/{IndexName}`), et utilise le `host` renvoyé. |
| `IndexName` | chaîne | `"orkeon-memories"` | Le nom de l'index Pinecone. |
| `Namespace` | chaîne | `"default"` | L'espace de noms dans l'index. |
<!-- /settings -->

### `Orkeon:LanceDb`

Serveur LanceDB distant. Lue par `MemoryProviderFactory` sur tout chemin qui sélectionne
`lancedb`. Liée par `AddOrkeonInfrastructure()` ; `AddOrkeonLanceDb(configuration)` ajoute la
classe concrète et `LanceDbMigrationService`.

<!-- settings:Orkeon:LanceDb -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ApiKey` | chaîne, secret | — | La clé d'API envoyée dans l'en-tête `x-api-key`. |
| `CreateFullTextIndexOnInit` | booléen | `true` | Si le provider crée un index plein texte (FTS) sur la colonne `content` quand il crée la table. La recherche plein texte côté serveur exige cet index ; un échec de création est journalisé en avertissement et réapparaît plus tard en erreur serveur sur `SearchAsync`. |
| `Database` | chaîne | — | Le nom de base de données facultatif envoyé dans l'en-tête `x-lancedb-database` (déploiements Enterprise derrière un point d'accès partagé ou privé). |
| `DefaultTopK` | entier | `10` | Le nombre de résultats renvoyés par défaut par les requêtes. |
| `DistanceType` | chaîne | `"cosine"` | La métrique de distance de la recherche vectorielle côté serveur (`cosine`, `l2` ou `dot`). Le provider convertit la `_distance` renvoyée en score de similarité par `1 - distance`, ce qui n'a de sens que pour la métrique par défaut, `cosine`. |
| `EmbeddingDimension` | entier | `1536` | La dimension du vecteur d'embedding de la colonne `vector`, de taille fixe, de la table. Doit correspondre aux embeddings stockés par le provider. |
| `Endpoint` | chaîne | `""` | L'URL de base du point d'accès REST LanceDB Cloud/Enterprise (p. ex. `https://my-deployment.us-east-1.api.lancedb.com`). |
| `FullTextWeight` | nombre | `0.3` | Le poids appliqué au classement plein texte (BM25) côté serveur lors de la fusion des résultats de recherche hybride. |
| `MinSimilarityScore` | nombre | `0` | Le seuil minimal de score de similarité des résultats de recherche vectorielle. |
| `TableName` | chaîne | `"orkeon_memories"` | Le nom de la table où sont stockés les souvenirs. |
| `VectorWeight` | nombre | `0.7` | Le poids appliqué au classement par similarité vectorielle côté serveur lors de la fusion des résultats de recherche hybride. |
<!-- /settings -->

### `Orkeon:CrewMemory`

Ce que rappelle une crew `memory: true` avant chaque tâche (`CrewMemoryOptions`) : au plus
`RecallLimit` souvenirs (0 ne rappelle rien), ceux dont la similarité cosinus atteint
`MinScore` — à l'échelle de l'embedder, mesurée sur le modèle local —, coupés à `MaxChars`
caractères de contenu au total — voir
[Système de mémoire](../architecture/memory-system.md#ce-que-rappelle-une-crew). Lue par
`MemoryCoordinator` ; liée par `AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:CrewMemory -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `MaxChars` | entier | `4000` | Combien de caractères de contenu de mémoire l'invite d'une tâche reçoit au plus, tous souvenirs rappelés confondus. 4 000 par défaut ; le dernier souvenir qui ne tient pas est coupé. |
| `MinScore` | nombre | `0.6` | La similarité cosinus minimale entre une tâche et un souvenir pour que le souvenir soit rappelé. 0,6 par défaut, mesuré sur le modèle d'embedding local (BGE-micro-v2), sur lequel la même tâche d'un run antérieur obtient 0,72 et plus, et une tâche anglaise sans rapport 0,53 et moins. Son échelle est celle de l'embedder : un autre modèle demande sa propre mesure. |
| `RecallLimit` | entier | `5` | Combien de souvenirs une tâche rappelle au plus. 5 par défaut ; 0 ne rappelle rien (la crew stocke quand même ce que produisent ses tâches). |
<!-- /settings -->

### `Orkeon:CognitiveMemory`

Couche de mémoire cognitive. Opt-in : `AddOrkeonCognitiveMemory(configuration)`.

<!-- settings:Orkeon:CognitiveMemory -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonCognitiveMemory()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `AnalysisModel` | chaîne | — |  | Modèle de remplacement facultatif pour les appels d'analyse. Null (ou vide), ils tournent sur le modèle propre du fournisseur. |
| `AnalysisTemperature` | nombre | `0.1` |  | Température des appels d'analyse LLM. Défaut : 0,1. |
| `ContradictionCandidateCount` | entier | `10` |  | Nombre de souvenirs existants où chercher des contradictions. Défaut : 10. |
| `DefaultRecallOptions:ImportanceWeight` | nombre | `0.2` |  | Poids de l'importance dans le score composite. Défaut : 0,2. |
| `DefaultRecallOptions:MinScore` | nombre | `0.1` |  | Seuil minimal du score composite. Défaut : 0,1. |
| `DefaultRecallOptions:RecencyWeight` | nombre | `0.3` |  | Poids de la récence dans le score composite. Défaut : 0,3. |
| `DefaultRecallOptions:SemanticWeight` | nombre | `0.5` |  | Poids de la similarité sémantique dans le score composite. Défaut : 0,5. |
| `DefaultRecallOptions:TagFilter` | liste de chaînes | `[]` |  | Filtre facultatif par étiquettes (une seule suffit). |
| `DefaultRecallOptions:TopK` | entier | `10` |  | Nombre maximal de résultats renvoyés. Défaut : 10. |
| `DefaultRecallOptions:TypeFilter` | énumération | — | `ShortTerm`, `LongTerm`, `Episodic`, `Entity`, `Procedural` | Filtre facultatif par type de mémoire. |
| `EnableContradictionDetection` | booléen | `true` |  | Si la détection de contradictions est activée à la mémorisation. Défaut : true. |
| `EnableLlmAnalysis` | booléen | `true` |  | Si l'analyse du contenu par LLM est activée à la mémorisation. Défaut : true. |
| `PruningMinAgeDays` | entier | `30` |  | Âge minimal, en jours, avant qu'un souvenir puisse être élagué. Défaut : 30. |
| `PruningThreshold` | nombre | `0.1` |  | Seuil d'importance en dessous duquel un souvenir est candidat à l'élagage. Défaut : 0,1. |
| `RecencyHalfLifeHours` | nombre | `69` |  | Demi-vie de la décroissance de récence, en heures (exp(-ln2/halfLife * ageHours)). Défaut : 69,0. |
<!-- /settings -->

### `Orkeon:Encryption`

Chiffrement de la mémoire au repos. Enregistré par `AddOrkeonInfrastructure()` ; la section
gouverne le comportement, et le décorateur est posé par l'hôte. La rotation de clés reste
opt-in : `AddOrkeonKeyRotation()`.

<!-- settings:Orkeon:Encryption -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `false` | Si le chiffrement est activé. |
| `KeySizeInBits` | entier | `256` | Taille de la clé en bits (128, 192 ou 256). |
| `SecretName` | chaîne | `"orkeon-encryption-key"` | Le nom du secret par lequel la clé de chiffrement est obtenue auprès de `ISecretProvider`. |
<!-- /settings -->

### `Orkeon:Embeddings`

Sélection du provider d'embeddings : `Provider` vaut `openai`, tout générateur d'embeddings M.E.AI
qu'inscrit l'hôte, ou `ollama` ; un autre nom refuse le démarrage. Lue par
`DefaultEmbeddingProviderResolver`, quand aucun provider local ni d'analyse de code n'est inscrit.
Liée par `AddOrkeonInfrastructure()`, et par `AddOrkeonVectorSearch(configuration)` (appelé par
`AddOrkeonInfrastructure(configuration)`).

<!-- settings:Orkeon:Embeddings -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `BatchSize` | entier | `100` | Taille maximale d'un lot pour les requêtes d'embedding groupées. |
| `Dimension` | entier | `1536` | La dimension des vecteurs d'embedding. |
| `EnableCache` | booléen | `true` | Si le cache d'embeddings est activé. |
| `Model` | chaîne | `"text-embedding-3-small"` | Le nom du modèle utilisé pour les embeddings. |
| `Provider` | chaîne | `"openai"` | Le provider d'embeddings à utiliser (p. ex. "openai", "ollama"). |
<!-- /settings -->

### `Orkeon:EmbeddingCache`

Cache d'embeddings. Liée comme [`Orkeon:Embeddings`](#orkeonembeddings).

<!-- settings:Orkeon:EmbeddingCache -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `MaxCacheSizeBytes` | entier | `104857600` | Taille totale maximale du cache, en octets. |
| `SlidingExpirationMinutes` | entier | `60` | Durée d'expiration glissante, en minutes, des embeddings en cache. |
<!-- /settings -->

### `Orkeon:VectorSearch`

Options de recherche vectorielle. Liée par `AddOrkeonVectorSearch(configuration)`, qu'appelle
`AddOrkeonInfrastructure(configuration)`.

<!-- settings:Orkeon:VectorSearch -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonVectorSearch()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `DefaultMinScore` | nombre | `0.7` | Le seuil minimal de score de similarité par défaut. |
| `DefaultTopK` | entier | `10` | Le nombre de meilleurs résultats renvoyés par défaut. |
| `PreferVectorSearch` | booléen | `true` | S'il faut préférer la recherche vectorielle à la recherche textuelle quand elle est disponible. |
<!-- /settings -->

## RAG

Détails et sémantique : [Pipeline RAG](../architecture/rag-pipeline.md). Tout ce qui suit
requiert `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`), qu'appelle tout hôte
livré.

### `Orkeon:Rag`

Le pipeline de requête, étape par étape :

- **`Profile`** — le preset : `fast` (défaut), `balanced`, `quality`, `adaptive` ou
  `corrective` ; toute clé `Orkeon:Rag` surcharge le preset clé par clé. **Les défauts du tableau
  sont ceux du profil par défaut, `fast`** : une clé que les profils règlent différemment le dit,
  et les cinq presets sont comparés dans [Pipeline RAG](../architecture/rag-pipeline.md).
- **`LlmProfile`** — le profil LLM de l'hôte (`Llm:Profiles:<nom>`) qu'appelle le sous-système
  RAG — génération, transformateurs de requête, reranker listwise, évaluateur et vérificateur
  d'ancrage correctifs, classifieur `llm`, juge d'évaluation ; absent ou `default` : le profil par
  défaut. Un nom inconnu fait refuser le démarrage de l'hôte en listant les profils connus ;
  `orkeon rag eval --offline` l'ignore. Se choisit dans Studio › Réglages › Modèle d'IA (expert).
- **`Provider`** — TYPE du provider du document store RAG (`RagStoreOptions` — un alias de type
  de `MemoryProviderFactory`), connecté depuis la section propre de ce provider (`Orkeon:Redis`,
  `Orkeon:Sqlite`, …) ; défaut : l'`IMemoryProvider` ambiant.
- **`Collection`** — la collection qu'interroge `rag_search` quand l'agent n'en nomme aucune (non
  définie : `default`) ; `rag_eval` l'utilise pour un dataset qui ne nomme pas de collection et
  n'apporte pas de corpus.
- **`Retrieval`** — les bornes de l'étape de retrieval ; **`Retrieval:Mmr`** — MMR opt-in.
- **`Rerank`** — choix et profondeur du reranker.
- **`Context`** — assemblage du contexte (ordre `edges` anti-Lost-in-the-Middle).
- **`Groundedness`** — le hook de groundedness.
- **`Generation`** — l'étape de génération citée.
- **`QueryTransform`** — transformateurs de requête (`multi-query`/`rag-fusion`/`hyde`).
- **`Corrective`** — les bornes du graphe correctif CRAG ; **`Corrective:WebFallback`** — la
  moitié « politique » du repli web, dont le transport est
  [`Orkeon:Rag:WebFallback`](#orkeonragwebfallback).

<!-- settings:Orkeon:Rag -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Collection` | chaîne | — | Collection interrogée par défaut quand le site d'appel n'en nomme aucune. |
| `Context:MaxTokens` | entier | `2000` | Budget du contexte en tokens (heuristique : 1 token ≈ 4 caractères ; les fragments en trop sont écartés, la génération ne se fait jamais en silence au-delà). |
| `Context:Ordering` | chaîne | `"edges"` | Disposition des fragments dans le bloc de contexte : `edges` (défaut — anti-Lost-in-the-Middle : les rangs impairs 1, 3, 5… ouvrent le bloc, les rangs pairs …6, 4, 2 le ferment, si bien que les deux meilleurs fragments sont aux extrémités) ou `linear` (simple ordre des rangs). Les marqueurs de citation restent fondés sur le rang (`[1]` = meilleur fragment) quelle que soit la disposition. |
| `Corrective:MaxIterations` | entier | `3` | Nombre maximal d'itérations correctives (réécritures de requête — déclenchées par un verdict de retrieval `Incorrect` ou par une réponse non ancrée) avant que le pipeline ne génère avec les meilleurs fragments disponibles. Vaut `3` par défaut. |
| `Corrective:WebFallback:Enabled` | booléen | `false` | Si le repli web peut se déclencher. Désactivé par défaut (opt-in) : le retrieval reste strictement local tant que l'hôte ne l'active pas explicitement ET n'enregistre pas un retriever de documents web. |
| `Corrective:WebFallback:MaxResults` | entier | `3` | Nombre maximal de documents web demandés au retriever. Vaut `3` par défaut. |
| `Generation:MaxOutputTokens` | entier | — | Nombre maximal de tokens de sortie passé au client de chat. |
| `Generation:SystemPrompt` | chaîne | — | Invite système ancrée ; `null` sélectionne celle du pipeline par défaut (anti-hallucination, marqueurs `[n]`). |
| `Generation:Temperature` | nombre | — | Température d'échantillonnage passée au client de chat. |
| `Groundedness:Enabled` | booléen | `false` — sous le profil par défaut, `fast` : chaque profil règle le sien | Si la réponse est vérifiée contre le contexte récupéré après la génération. Requiert l'enregistrement d'un `IGroundednessChecker` ; activée sans lui, l'étape est tracée comme sautée. |
| `LlmProfile` | chaîne | — | Le profil LLM de l'hôte (`Llm:Profiles:<nom>`) vers lequel va chaque appel au modèle du sous-système — génération ancrée, transformateurs de requête, reranker listwise, évaluateur et vérificateur d'ancrage du graphe correctif, classifieur `llm` et juge d'évaluation. Null, vide ou `default` : le profil par défaut de l'hôte. Une clé d'hôte : un seul profil pour tout le sous-système, qu'aucun preset ne règle. |
| `Profile` | chaîne | `"fast"` | Nom du profil (`fast`, `balanced`, `quality`, `adaptive`, `corrective`). Vaut `fast` par défaut — `balanced` demande le paquet opt-in du reranker ONNX. Un nom inconnu échoue bruyamment. |
| `Provider` | chaîne | — | Le type de provider mémoire qui porte le document store du RAG : `inmemory`, `in-memory`, `redis`, `sqlite`, `chromadb`, `chroma`, `pinecone`, `lancedb`, `lance`. `null` ou vide sélectionne le provider ambiant du conteneur. |
| `QueryTransform:Mode` | chaîne | `"none"` | Nom du transformateur, résolu par la fabrique de transformateurs de requête : `none` (défaut), `multi-query`, `rag-fusion` ou `hyde`. `none` saute l'étape ; un nom inconnu échoue bruyamment. |
| `QueryTransform:VariantCount` | entier | `3` | Nombre de variantes demandées aux transformateurs autres que `none`. |
| `Rerank:Enabled` | booléen | `false` — sous le profil par défaut, `fast` : chaque profil règle le sien | Si l'étape de rerank s'exécute. Désactivée, les candidats sont tronqués à TopN dans l'ordre du retrieval. |
| `Rerank:Kind` | chaîne | `"none"` — sous le profil par défaut, `fast` : chaque profil règle le sien | Nom du reranker, résolu par la fabrique de rerankers (`none`/`noop`, `llm`/`listwise`, `onnx`/`cross-encoder` — ce dernier par le paquet opt-in `Orkeon.Rag.Onnx`). Un nom inconnu échoue bruyamment. |
| `Rerank:TopN` | entier | `5` | Nombre de fragments gardés par défaut après le rerank (un `RagQuery.TopN` du site d'appel l'emporte). |
| `Retrieval:CandidateK` | entier | `5` — sous le profil par défaut, `fast` : chaque profil règle le sien | Nombre de candidats récupérés avant la fusion et le rerank (l'étape large de la cascade 50 → 5). Le pipeline récupère toujours au moins le TopN final. |
| `Retrieval:MinScore` | nombre | — | Plancher de score facultatif appliqué aux scores bruts du retrieval avant la fusion. `null` (défaut) n'en applique aucun — le plancher global toxique de 0,7 de l'ancien sous-système a été retiré à dessein. |
| `Retrieval:Mmr:Enabled` | booléen | `false` | Si la diversification MMR s'exécute à l'étape de fusion et de dédoublonnage. Désactivée par défaut. |
| `Retrieval:Mmr:Lambda` | nombre | `0.7` | Compromis pertinence/diversité dans `[0, 1]` : `1` garde le pur ordre de pertinence, `0` maximise la diversité. Vaut `0.7` par défaut. |
| `Retrieval:TopK` | entier | `5` | Nombre de fragments gardés par défaut pour l'assemblage du contexte (un `RagQuery.TopN` du site d'appel l'emporte). |
<!-- /settings -->

### `Orkeon:Rag:Retrieval:Hybrid`

Récupération hybride BM25+RRF.

<!-- settings:Orkeon:Rag:Retrieval:Hybrid -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `false` — sous le profil par défaut, `fast` : chaque profil règle le sien | Si le retrieval fusionne les classements lexical (BM25 / plein texte natif) et vectoriel. Honoré requête par requête par un document store capable d'hybride. |
| `RrfK` | entier | `60` | Constante de la Reciprocal Rank Fusion (doit être positive ; 60 est la valeur standard). |
<!-- /settings -->

### `Orkeon:Rag:Ingestion`

Le pipeline d'ingestion (`RagIngestionOptions`).

<!-- settings:Orkeon:Rag:Ingestion -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `DefaultChunkingStrategy` | chaîne | `"recursive"` | Stratégie de découpage résolue par la fabrique de découpage quand une `IngestionRequest` n'en nomme aucune. Vaut `recursive` par défaut. |
| `ManifestDirectory` | chaîne | `"/output/rag/manifests"` | Répertoire virtuel (chemin VFS) qui contient les manifestes d'ingestion par collection (`{collection}.json`). Vaut `/output/rag/manifests` par défaut — sous le montage inscriptible conventionnel `/output`, à côté des autres artefacts d'un run. |
<!-- /settings -->

### `Orkeon:Rag:QueryRouting`

Routage Adaptive-RAG : le classifieur est `heuristic` ou `llm`.

<!-- settings:Orkeon:Rag:QueryRouting -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Classifier` | chaîne | `"heuristic"` | Nom du classifieur : `heuristic` (défaut — sûr, déterministe, fonctionne sans aucun LLM) ou `llm`. Un nom inconnu échoue bruyamment à la résolution. Quand `llm` est choisi mais qu'aucun `IChatClient` n'est enregistré, le classifieur heuristique sert de repli documenté (avec un avertissement) au lieu d'échouer. |
<!-- /settings -->

### `Orkeon:Rag:WebFallback`

Le repli web est un double opt-in, les deux `Enabled` désactivés par défaut : la politique
(`Orkeon:Rag:Corrective:WebFallback`, dans le tableau d'[`Orkeon:Rag`](#orkeonrag)) et cette
section, son transport SearxNG.

<!-- settings:Orkeon:Rag:WebFallback -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `ApiKeyEnvVar` | chaîne | `""` |  | Nom de la variable d'environnement qui contient la clé d'API, envoyée dans un en-tête `Authorization: Bearer` quand elle est définie. Vide = pas d'en-tête d'authentification. La clé elle-même ne vit jamais dans un fichier de configuration. |
| `Enabled` | booléen | `false` |  | Interrupteur principal du transport. `false` par défaut — opt-in strict. |
| `Endpoint` | chaîne | `""` |  | URL du point d'accès de recherche d'une API JSON compatible SearxNG (interrogée par `{Endpoint}?q={query}&format=json`, en attendant un tableau JSON `results[].url`). Vide = désactivé (journalisé). |
| `MaxResults` | entier | `3` |  | Nombre maximal de résultats de recherche récupérés par requête. 3 par défaut. |
| `SuspiciousAction` | énumération | `"Flag"` | `Flag`, `Discard` | Que faire des documents que le validateur d'injection marque `Suspicious` : les garder, marqués dans leurs métadonnées (défaut), ou les écarter. Les documents rejetés sont toujours écartés. |
| `Timeout` | durée | `"00:00:10"` |  | Délai par requête (l'appel de recherche et chaque téléchargement de page). 10 s par défaut. |
<!-- /settings -->

## Fichiers et bac à sable

Les dossiers qu'un run peut atteindre, et l'endroit où peut tourner le code qu'écrit un agent.

### `Orkeon:FileSystem`

**`Mounts`** — les montages VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)). Une
entrée peut porter un **identifiant** — `<ulid>|<physique>:<virtuel>:<droits>` (VFS-90) : ce par
quoi le bloc `mounts:` d'une crew, le sidecar d'équipe de Studio et `--mount-id` la désignent ;
Studio en écrit un à chaque enregistrement. Un `--mount` CLI sur la même racine virtuelle
**remplace toutes les entrées de cette racine** pour ce run ; sur une racine neuve il est ajouté
(jamais fusionné, jamais perdu). Une racine déclarée deux fois n'est légitime que si chacune de
ses entrées porte un identifiant — `--mount-id`, ou le `mounts:` de la crew, en sélectionne alors
une et les autres sont retirées pour le run ; si rien n'en sélectionne une, le run est refusé
avant tout host (`'/output' is declared twice in <settings> (<idA>: <dossierA>, <idB>: <dossierB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`),
de même qu'une racine déclarée deux fois avec une entrée sans identifiant
(`… and '<entrée>' has no id. Give every entry an id …`) ou un identifiant porté par deux entrées.
Le chemin de base de chaque entrée déclarée est **mis en liste blanche pour `PathValidator`** sans
`--allow-external-mounts` — un dossier déclaré est l'intention du propriétaire de la machine, il
reste donc accessible même hors du répertoire de travail du processus ; une entrée retirée ne
l'est pas.

**`InternalMounts`** — même grammaire que `Mounts`, enregistrés en `MountVisibility.Internal` :
résolubles par le VFS, **absents de `list_mounts`, de la table de montages du prompt agent et des
messages de refus d'accès**. C'est là qu'un hôte met ce que le VFS doit atteindre et qu'aucun
agent n'a à adresser — le répertoire `--llm-log` y vit, et `/credentials`, les jetons OAuth des
comptes e-mail, quand un tel compte est déclaré
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)).

Les deux sont lues par `AddOrkeonFileSystem(...)`.

<!-- settings:Orkeon:FileSystem -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `InternalMounts` | liste de chaînes | `[]` | Montages d'infrastructure, même grammaire que `Mounts` : résolubles par l'hôte, absents de `list_mounts`, de la table de montages de l'invite d'agent et des messages de refus d'accès, et refusés à tout outil. C'est là qu'un runner met ce que le VFS doit atteindre et qu'aucun agent n'a à adresser — le répertoire du journal des échanges LLM, par exemple. |
| `Mounts` | liste de chaînes | `[]` | Définitions de montages au format "physical:virtual:rights[;subpath:rights;...]". |
<!-- /settings -->

### `Orkeon:Sandbox`

Le montage sandbox du système de fichiers (`/sandbox`, Internal) ; les répertoires de session
orphelins sont nettoyés au-delà de `CleanupOrphansOlderThan` (24 h). Lue par
`AddOrkeonFileSystem(...)`.

<!-- settings:Orkeon:Sandbox -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `CleanupOrphansOlderThan` | durée | `"1.00:00:00"` | Seuil d'âge des répertoires de session orphelins. Les répertoires plus anciens sont supprimés par le concierge au démarrage. 24 heures par défaut. |
| `EphemeralRoot` | chaîne | — | Répertoire racine sous lequel sont créés les répertoires de sandbox par session. À `null`, vaut `Path.Combine(Path.GetTempPath(), "orkeon-sandbox")`. |
| `VirtualPath` | chaîne | `"/sandbox"` | Chemin virtuel où le sandbox est monté. `/sandbox` par défaut. |
<!-- /settings -->

### `PathSecurity`

Validation des chemins physiques. Liée par `AddOrkeonInfrastructure()` (`IPathValidator`).

<!-- settings:PathSecurity -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AdditionalAllowedDirectories` | liste de chaînes | `[]` | Répertoires supplémentaires autorisés au-delà de la racine de l'espace de travail. |
| `AdditionalBlockedExtensions` | liste de chaînes | `[]` | Extensions de fichier supplémentaires à bloquer, en plus de la liste intégrée. |
| `DefaultWorkspaceRoot` | chaîne | — | Le répertoire racine de l'espace de travail par défaut. À null, c'est le répertoire de travail courant. |
| `MaxFileSizeBytes` | entier | `52428800` | Taille de fichier maximale autorisée, en octets. Défaut : 50 Mo. |
| `ResolveSymlinks` | booléen | `true` | S'il faut résoudre les liens symboliques et vérifier que la cible est dans l'espace de travail. Défaut : true. |
<!-- /settings -->

### `Orkeon:CodeSandbox`

Le sandbox de l'interpréteur de code sécurisé. Enregistré par `AddOrkeonInfrastructure()` ; la
section gouverne le comportement.

<!-- settings:Orkeon:CodeSandbox -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AllowHostExecution` | booléen | `false` | Opt-in explicite et bruyant pour autoriser l'exécution de code dans un sandbox qui n'offre AUCUNE isolation au niveau du système (l'exécuteur de processus de l'hôte) quand un sandbox isolant (Docker) est indisponible. Laissée à `false`, l'exécution de code est refusée plutôt que lancée sur l'hôte ; `true` équivaut à une RCE sur l'hôte, pour du code de confiance dans un environnement de confiance seulement. |
| `DefaultPermissions:AllowedPaths` | liste de chaînes | `[]` | Liste des chemins de fichiers autorisés. |
| `DefaultPermissions:AllowFileRead` | booléen | `false` | Autorise la lecture de fichiers. |
| `DefaultPermissions:AllowFileWrite` | booléen | `false` | Autorise l'écriture de fichiers. |
| `DefaultPermissions:AllowNetworkAccess` | booléen | `false` | Autorise l'accès au réseau. |
| `DefaultPermissions:AllowProcessExec` | booléen | `false` | Autorise le lancement de processus enfants. |
| `DefaultPermissions:AllowReflection` | booléen | `false` | Autorise les API de réflexion. |
| `DefaultPermissions:WorkingDirectory` | chaîne | — | Répertoire de travail du sandbox. |
| `MaxMemoryBytes` | entier | `268435456` | Mémoire maximale en octets (défaut : 256 Mo). |
| `MaxOutputBytes` | entier | `50000` | Taille maximale de la sortie en octets (défaut : 50 Ko). |
| `SecurityOptions:AllowedNamespaces` | liste de chaînes | `["System", "System.Collections.Generic", "System.Linq", "System.Text", "System.Text.Json", "System.Text.RegularExpressions", "System.Math"]` | Espaces de noms autorisés dans les directives using. |
| `SecurityOptions:AllowFileIO` | booléen | `false` | Autorise les opérations d'entrée-sortie sur fichiers. |
| `SecurityOptions:AllowNetworking` | booléen | `false` | Autorise les opérations réseau. |
| `SecurityOptions:AllowProcessExec` | booléen | `false` | Autorise le lancement de processus. |
| `SecurityOptions:AllowReflection` | booléen | `false` | Autorise les API de réflexion. |
| `SecurityOptions:AllowUnsafeCode` | booléen | `false` | Autorise les blocs de code unsafe. |
| `SecurityOptions:BlockedTypes` | liste de chaînes | `["System.Diagnostics.Process", "System.IO.File", "System.IO.Directory", "System.Reflection.Assembly", "System.Runtime.InteropServices.Marshal", "System.Net.Sockets.Socket", "System.AppDomain"]` | Types toujours bloqués. |
| `TimeoutSeconds` | entier | `30` | Délai d'exécution par défaut, en secondes. |
<!-- /settings -->

### `Orkeon:CodeSandbox:Docker`

Le sandbox Docker de l'interpréteur de code sécurisé.

<!-- settings:Orkeon:CodeSandbox:Docker -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ImageName` | chaîne | `"mcr.microsoft.com/dotnet/sdk:10.0-alpine"` | Image Docker utilisée pour l'exécution de code. |
| `PullImageOnStartup` | booléen | `false` | S'il faut tirer l'image au démarrage. |
<!-- /settings -->

## Sécurité

Ce qui filtre une invite, un appel d'outil et un résultat d'outil, ce qui est audité, et d'où
viennent les secrets. Le récit est dans [Sécurité](../architecture/security.md).

### `Orkeon:Guardian`

Le pipeline de gardes : `GuardianPipeline` joue la phase d'entrée de chaque tour d'agent, et les
phases outil et délégation de chaque appel d'outil. Actif par défaut, enregistré par
`AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Guardian -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `DefaultPolicy:DelegationGuardEnabled` | booléen | `true` | Si la phase de délégation (profondeur, auto-délégation, cycles) s'exécute. |
| `DefaultPolicy:InputGuardEnabled` | booléen | `true` | Si la phase d'entrée (filtrage anti-injection de l'invite utilisateur composée) s'exécute. |
| `DefaultPolicy:MaxDelegationDepth` | entier | `5` | Combien de délégations synchrones peuvent s'emboîter avant que la suivante ne soit bloquée. |
| `DefaultPolicy:ToolGuardEnabled` | booléen | `true` | Si la phase outil (traversée de chemin, SSRF et injection SQL dans les arguments d'outil) s'exécute. |
| `Enabled` | booléen | `true` | Si le guardian s'exécute. À false, aucun guardian n'est enregistré dans le pipeline : les tours d'agent ne sont filtrés ni en entrée ni sur les appels d'outils. |
<!-- /settings -->

### `Security:Prompt`

La phase d'entrée du Guardian : `Policy` vaut `Block` par défaut — High/Critical fait échouer la
tâche, moindre avertit —, `Warn` ou `None`. Liée par `AddOrkeonInfrastructure()` ; elle filtre
l'invite de chaque tour d'agent — voir [Sécurité](../architecture/security.md).

<!-- settings:Security:Prompt -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `CustomPatterns` | liste de chaînes | `[]` |  | Motifs regex personnalisés à détecter en plus des motifs intégrés. |
| `EnableExfiltrationDetection` | booléen | `true` |  | Si la détection des tentatives d'exfiltration de données est activée. Vrai par défaut. |
| `Policy` | énumération | `"Block"` | `None`, `Warn`, `Block` | La politique que la phase d'entrée du Guardian applique à l'invite utilisateur composée. Défaut `Block` : un motif High ou Critical fait échouer la tâche avant tout appel au fournisseur, un motif moindre est journalisé et audité ; l'invite n'est jamais réécrite. |
<!-- /settings -->

### `Security:ToolResults`

Filtrage des résultats d'outils : `Policy` vaut `Warn` par défaut — balisé comme donnée et
signalé —, `Block` retient High/Critical, `None` laisse tout passer ; les `TrustedTools` s'ajoutent
aux `email_*` par défaut. La longueur ne se règle pas ici : une seule règle,
`AgentDefaults.ResolveMaxToolResultLength`. Liée par `AddOrkeonInfrastructure()` et appliquée à
chaque résultat d'outil par `IToolInvocationPipeline`.

<!-- settings:Security:ToolResults -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `Policy` | énumération | `"Warn"` | `None`, `Warn`, `Block` | La politique appliquée à un résultat d'outil réussi. Défaut `Warn` : le résultat arrive au modèle balisé comme donnée, et tout motif d'injection est journalisé et audité. `Block` retient un résultat qui porte un motif High ou Critical (le modèle en est informé) ; `None` laisse passer les résultats intacts et sans balise. |
| `TrustedTools` | liste de chaînes | `["email_accounts", "email_create_folder", "email_delete", "email_draft", "email_folders", "email_mark", "email_move", "email_parser", "email_read", "email_rename_folder", "email_save_attachment", "email_search", "email_send"]` |  | Outils dont les résultats contournent le filtrage. Par défaut les outils `email_*`, qui filtrent ce qu'ils lisent avec `PromptInjectionDocumentValidator` et le marquent eux-mêmes comme non fiable (ADR-012) — les baliser de nouveau empilerait deux enveloppes. Les entrées de la configuration s'ajoutent à celles-ci. |
<!-- /settings -->

### `Security:Url`

Validation SSRF. Liée par `AddOrkeonInfrastructure()` (`IUrlValidator`).

<!-- settings:Security:Url -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AllowedDomains` | liste de chaînes | `[]` | Si la liste n'est pas vide, seuls ces domaines (et leurs sous-domaines) sont autorisés. |
| `AllowedSchemes` | liste de chaînes | `["http", "https"]` | Schémas d'URL autorisés. Par défaut http et https. |
| `BlockedDomains` | liste de chaînes | `[]` | Domaines (et leurs sous-domaines) explicitement bloqués. |
| `BlockedPorts` | liste d'entiers | `[22, 23, 25, 110, 143, 445, 3306, 5432, 6379, 27017]` | Ports dont l'accès est bloqué (ports de services courants). |
| `BlockPrivateIPs` | booléen | `true` | S'il faut bloquer les requêtes vers les plages d'adresses IP privées ou internes. Vrai par défaut. |
| `ResolveDNS` | booléen | `true` | S'il faut résoudre le DNS et confronter l'adresse IP résolue aux plages privées. Vrai par défaut. |
<!-- /settings -->

### `Security:Audit`

Sinks d'audit. Liée par `AddOrkeonInfrastructure()`.

<!-- settings:Security:Audit -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `AuditDirectory` | chaîne | `"./audit-logs"` |  | Répertoire où sont stockés les fichiers du journal d'audit. |
| `Enabled` | booléen | `true` |  | Si la journalisation d'audit est activée. |
| `EnabledCategories` | liste d'énumérations | `[]` | `LlmCall`, `ToolExecution`, `FileAccess`, `HttpRequest`, `SecurityEvent`, `CrewLifecycle`, `AgentDecision`, `MemoryOperation`, `ConfigChange` | Catégories à journaliser. Vide signifie que toutes les catégories sont activées. |
| `MinSeverity` | énumération | `"Info"` | `Debug`, `Info`, `Warning`, `Error`, `Critical` | Niveau de sévérité minimal des événements journalisés. |
| `RetentionDays` | entier | `90` |  | Nombre de jours de conservation des journaux d'audit. |
<!-- /settings -->

### `Security:Vault`

La chaîne de coffres à secrets. Liée par `AddOrkeonInfrastructure()`.

<!-- settings:Security:Vault -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AzureKeyVaultUri` | URI | — | URI d'Azure Key Vault (p. ex. https://my-vault.vault.azure.net/) |
| `CacheTtl` | durée | `"00:05:00"` | Durée de vie du cache des secrets en mémoire (défaut : 5 min) |
| `DpapiSecretsDirectory` | chaîne | — | Répertoire du magasin de secrets DPAPI (Windows seulement) |
| `UseAwsSecretsManager` | booléen | `false` | S'il faut utiliser AWS Secrets Manager |
<!-- /settings -->

### `Secrets`

`Secrets:<NOM>` est le second maillon de la chaîne de secrets après `ORKEON_<NOM>`
(`ConfigurationSecretProvider`), p. ex. `Secrets:TAVILY_API_KEY` pour `web_search`. Liée par
`AddOrkeonInfrastructure()`.

<!-- settings:Secrets -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `<nom>` | chaîne, secret | — | Un secret par son nom — `Secrets:TAVILY_API_KEY` —, là où la chaîne de secrets cherche après la variable d'environnement `ORKEON_<NAME>` et avant un coffre. Une valeur écrite ici est en clair dans le fichier de réglages. |
<!-- /settings -->

### `Orkeon:Security:PermissionGate`

La barrière par appel d'outil (`ModePermissionGate`). `AddOrkeonPermissionGate(configuration)` —
appelé par `RunnerHost` — est sans effet tant que `Enabled = true` n'est pas posé.

<!-- settings:Orkeon:Security:PermissionGate -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `false` | Si la barrière est enregistrée. Laissée à `false`, aucun appel d'outil n'y est soumis. |
| `Interactive` | booléen | `false` | S'il existe un canal d'approbation pour interroger un opérateur. Aucun hôte livré n'en a : laissée à `false`, un appel qui demande une approbation est refusé, et le modèle lit la raison comme résultat de l'outil. |
<!-- /settings -->

### `Orkeon:Dlp`

Politiques DLP et détection de PII. Opt-in : `AddOrkeonDlp()`.

<!-- settings:Orkeon:Dlp -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonDlp()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `ChannelPolicies:<nom>` | libre | — |  | Les surcharges de politique par canal. |
| `DefaultAction` | énumération | `"Audit"` | `Allow`, `Mask`, `Block`, `Audit` | L'action par défaut pour tous les canaux. |
| `Enabled` | booléen | `true` |  | Si la DLP est activée globalement. |
<!-- /settings -->

## Outils

Ce que les outils d'un agent ont le droit de faire, et les serveurs et index qu'ils atteignent.

### `Orkeon:Tools:Shell`

Ce que `ShellCommandTool` peut lancer, par la configuration seule (`AddOrkeonCodeTools()`) :
`AllowInterpreters` autorise les interpréteurs et le git mutant (**équivalent RCE**, un
avertissement est émis) ; `ExtraAllowedCommands` ajoute à l'allowlist et `AllowedCommands` la
remplace en entier — le remplacement annule `AllowInterpreters`.

<!-- settings:Orkeon:Tools:Shell -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AllowedCommands` | liste de chaînes | `[]` | Un remplacement complet de l'allowlist par défaut, tel quel ; il annule `AllowInterpreters` et rétablit la restriction de git à la lecture seule. Vide garde l'allowlist par défaut. |
| `AllowInterpreters` | booléen | `false` | Active les interpréteurs (`node`, `dotnet`, `npm`, `find`) et les sous-commandes git qui modifient — équivalent à une RCE sur l'hôte, pour les hôtes d'agent de code de confiance seulement. Faux par défaut. |
| `ExtraAllowedCommands` | liste de chaînes | `[]` | Commandes ajoutées à l'allowlist par défaut (ou de remplacement), comme `make`. |
<!-- /settings -->

### `Orkeon:Tools:Email`

Lié par `AddOrkeonEmailTools(configuration)` — l'hôte partagé des runners et `orkeon-repl`
l'appellent — et validé paresseusement : un compte est validé la première fois qu'un outil ou une
commande `orkeon email` s'en sert, tous ses problèmes signalés d'un coup, et une valeur que le
binder ne sait même pas lire (un droit mal orthographié, un port en toutes lettres) met ce seul
compte de côté au lieu de faire échouer l'hôte — une section cassée ne casse jamais un crew qui
n'envoie pas de courrier. Les secrets n'y sont jamais des valeurs, seulement les **noms** des
variables d'environnement qui les contiennent (`Auth:PasswordEnvVar`, `Auth:ClientSecretEnvVar`),
lues comme l'est la variable que nomme `ApiKeyEnvVar` : dans l'environnement du processus, puis —
sous Windows — dans la portée persistante de l'utilisateur (`HKCU\Environment`), où Orkeon Studio
mémorise un mot de passe saisi dans **Réglages › Mails** ; lues, jamais recopiées dans le
processus ; l'environnement du processus seul sous Linux et macOS
([où une variable de secret est lue](../guides/email.md#où-une-variable-de-secret-est-lue)).
Parcours par fournisseur et table clé par clé :
[Outils e-mail](../guides/email.md).

- **`DefaultAccount`** — le compte qu'utilise un appel qui n'en nomme aucun (facultatif avec un
  seul compte).
- **`CredentialsDirectory`** — le répertoire physique dont le sous-répertoire `email` contient
  les jetons OAuth ; un chemin relatif part du répertoire du fichier de réglages. Le runner le
  monte sur la racine interne `/credentials` quand un compte OAuth est déclaré ; par défaut :
  `credentials` à côté du fichier de réglages de l'utilisateur.
- **`Screening:WithholdRejected`** — retenir le corps d'un message que le filtre anti-injection
  de prompt rejette (défaut `false` : signaler seulement).
- **`Accounts:<nom>`** — un compte. `Provider` vaut `Gmail`, `Outlook` ou `Custom` (le défaut) ;
  `Rights` est **obligatoire** (`Read, Organize, Draft, Send, Delete, Purge`) ;
  `Incoming:Security` et `Outgoing:Security` valent `SslOnConnect`, `StartTls` ou `None` — ce
  dernier vers un hôte de bouclage seulement ; une liste `Send:AllowedRecipients` vide n'autorise
  personne. Le nom contient des lettres, des chiffres, `.`, `_` et `-`, commence par une lettre
  ou un chiffre (64 au plus).

<!-- settings:Orkeon:Tools:Email -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `Accounts:<nom>:Address` | chaîne | — |  | L'adresse du compte ; aussi le `From` de chaque message qu'il envoie. |
| `Accounts:<nom>:Auth:ClientId` | chaîne | — |  | Identifiant client OAuth (application Google Cloud ou Microsoft Entra). |
| `Accounts:<nom>:Auth:ClientSecretEnvVar` | chaîne | — |  | Nom de la variable d'environnement qui contient le secret client OAuth (clients de bureau Google). |
| `Accounts:<nom>:Auth:Method` | énumération | — | `Password`, `OAuth2` | `Password` ou `OAuth2`. |
| `Accounts:<nom>:Auth:PasswordEnvVar` | chaîne | — |  | Nom de la variable d'environnement qui contient le mot de passe ou le mot de passe d'application. |
| `Accounts:<nom>:Auth:Tenant` | chaîne | — |  | Tenant Microsoft : `consumers` (défaut), `organizations`, `common` ou un identifiant de tenant. |
| `Accounts:<nom>:Auth:Username` | chaîne | — |  | Nom de connexion ; par défaut l'adresse. |
| `Accounts:<nom>:DisplayName` | chaîne | — |  | Nom affiché, associé à `Address` dans `From`. |
| `Accounts:<nom>:Incoming:Host` | chaîne | — |  | Nom d'hôte du serveur. |
| `Accounts:<nom>:Incoming:Port` | entier | — |  | Port du serveur. |
| `Accounts:<nom>:Incoming:Protocol` | énumération | — | `Imap`, `Pop3`, `Graph` | `Imap`, `Pop3` ou `Graph` ; le preset décide quand la clé est absente. |
| `Accounts:<nom>:Incoming:Security` | énumération | — | `SslOnConnect`, `StartTls`, `None` | Sécurité du transport. |
| `Accounts:<nom>:Outgoing:Host` | chaîne | — |  | Nom d'hôte du serveur. |
| `Accounts:<nom>:Outgoing:Port` | entier | — |  | Port du serveur. |
| `Accounts:<nom>:Outgoing:Protocol` | énumération | — | `Smtp`, `Graph` | `Smtp` ou `Graph` ; le preset décide quand la clé est absente. |
| `Accounts:<nom>:Outgoing:Security` | énumération | — | `SslOnConnect`, `StartTls`, `None` | Sécurité du transport. |
| `Accounts:<nom>:Provider` | énumération | `"Custom"` | `Custom`, `Gmail`, `Outlook` | Le preset : `Gmail`, `Outlook` ou `Custom` (le défaut). |
| `Accounts:<nom>:Rights` | énumération | `"None"` | `None`, `Read`, `Organize`, `Draft`, `Send`, `Delete`, `Purge` | Ce qu'un agent peut faire, p. ex. `"Read, Organize, Draft"`. Obligatoire. |
| `Accounts:<nom>:SaveSentCopy` | booléen | — |  | Si un message envoyé est ajouté au dossier des messages envoyés. Absente, la clé suit le preset : Gmail, Outlook et Graph classent eux-mêmes le courrier envoyé, un serveur personnalisé en général non. |
| `Accounts:<nom>:Send:AllowedRecipients` | liste de chaînes | `[]` |  | Qui peut recevoir du courrier de ce compte : une adresse, `*@domain`, ou `*` pour tout le monde. Vide signifie personne : l'envoi est fermé tant qu'un opérateur ne l'ouvre pas. |
| `Accounts:<nom>:Send:MaxPerHour` | entier | — |  | Nombre maximal de messages que ce processus envoie par heure depuis le compte. Absente, pas de plafond. |
| `Accounts:<nom>:Send:MaxRecipients` | entier | — |  | Nombre maximal de destinataires d'un message. Absente, la limite est laissée au serveur. |
| `Accounts:<nom>:TimeoutSeconds` | entier | — |  | Délai du protocole, en secondes. Absente, le défaut de la bibliothèque est gardé. |
| `CredentialsDirectory` | chaîne | — |  | Répertoire physique qui contient les jetons OAuth, pour un hôte dont le répertoire de réglages par utilisateur n'est pas le bon (un compte de service). Lu par l'hôte, pas par les outils. |
| `DefaultAccount` | chaîne | — |  | Le compte qu'utilise un appel quand il n'en nomme aucun. Facultatif avec un seul compte. |
| `Screening:WithholdRejected` | booléen | `false` |  | À true, le corps d'un message que le détecteur d'injection de prompt rejette est retenu. Désactivé par défaut : le détecteur a été réglé sur des pages web, et les lettres d'information le déclenchent. |
<!-- /settings -->

### `MCP`

Connexions client MCP (`MCP:Servers:<nom>`) et l'interrupteur `MCP:Enabled`. `MCP:EnableServer`
est supprimé : une section qui le porte encore est refusée au démarrage, en nommant
`orkeon mcp serve` (GAP-24). Lue par `RunnerHost` (`orkeon run`, `orkeon-host`,
`orkeon mcp serve`) dès que `MCP:Servers` déclare au moins un serveur et que `MCP:Enabled` n'est
pas `false` — les serveurs sont connectés avant le chargement de la crew (STUDIO-21), par
`orkeon-host` une fois au démarrage, avant son premier message (GAP-11), et par
`orkeon mcp serve` avant qu'il serve ; les hôtes bibliothèque appellent
`AddOrkeonMcp(configuration)` ou la surcharge `AddOrkeonInfrastructure(configuration)` — voir
[Intégration MCP](../architecture/mcp.md).

<!-- settings:MCP -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `Enabled` | booléen | `true` |  | Si MCP est activé, tout simplement. |
| `Servers:<nom>:Args` | liste de chaînes | `[]` |  | Arguments de ligne de commande du processus serveur. |
| `Servers:<nom>:Command` | chaîne | `""` |  | Commande qui lance le processus du serveur MCP (pour le transport stdio). |
| `Servers:<nom>:Env:<nom>` | chaîne | — |  | Variables d'environnement du processus serveur. |
| `Servers:<nom>:Transport` | énumération | `"Stdio"` | `Stdio`, `Sse` | Type de transport à utiliser. |
| `Servers:<nom>:Url` | URI | — |  | URL du point d'accès pour le transport SSE. |
<!-- /settings -->

### `MCP:Server`

La façon dont le serveur d'`orkeon mcp serve` se présente (il n'expose que des outils). Lue par
`AddOrkeonMcpServer`, qu'appelle `orkeon mcp serve`.

<!-- settings:MCP:Server -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Name` | chaîne | `"Orkeon"` | Nom du serveur annoncé à l'initialisation MCP. |
| `Version` | chaîne | `"1.0.0"` | Version du serveur annoncée à l'initialisation MCP. |
<!-- /settings -->

### `RaggableTree`

Indexation de codebase : `Enabled`, et `Embedding` — rien d'autre : toute autre clé, un autre nom
de fournisseur ou un nombre qui n'est pas entier fait échouer l'hôte au démarrage ; ce que couvre
un index se règle à chaque appel `index_codebase`. **Opt-out dans les hôtes runner** :
`RunnerHost` l'enregistre par défaut, `RaggableTree:Enabled = false` le désactive ; les
consommateurs bibliothèque appellent `AddRaggableTree(options)` explicitement.

<!-- settings:RaggableTree -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `Embedding:ApiKey` | chaîne, secret | — |  | La clé d'un fournisseur distant. Le fournisseur local n'en a pas besoin. |
| `Embedding:BaseUrl` | chaîne | — |  | L'adresse `http://` ou `https://` du point d'accès du fournisseur, quand ce n'est pas celle du fournisseur lui-même. |
| `Embedding:Dimensions` | entier | — |  | La taille des vecteurs, pour un modèle qui la laisse choisir. Omise, celle du modèle. |
| `Embedding:MaxTextChars` | entier | — |  | Le plus grand nombre de caractères d'un texte envoyé au modèle ; ce qui dépasse est coupé. Omise, la limite propre du fournisseur. |
| `Embedding:Model` | chaîne | — |  | Le modèle d'embedding. Omis, le fournisseur utilise le sien — `bge-micro-v2` pour le fournisseur local. |
| `Embedding:Provider` | énumération | `"LocalSmartComponents"` | `None`, `OpenAI`, `Ollama`, `Onnx`, `LocalSmartComponents` | Qui calcule les embeddings, sans tenir compte de la casse. Le défaut tourne sur la machine : pas de clé, pas de réseau. |
| `Enabled` | booléen | `true` |  | Si l'index et ses outils sont enregistrés. `false` les laisse hors de l'hôte. |
<!-- /settings -->

### `BRAVE_API_KEY`

Aussi lue comme **clé de configuration** (pas seulement une variable d'environnement) pour
conditionner l'outil Brave, par `RunnerHost`.

<!-- settings:BRAVE_API_KEY -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `BRAVE_API_KEY` | chaîne, secret | — | La clé de l'API Brave Search. Définie — ici ou dans la variable d'environnement du même nom —, elle enregistre l'outil `brave_search` ; absente, l'outil n'est pas proposé. |
<!-- /settings -->

### `Orkeon:MultiModal`

Validation de la vision et des contenus — aucune image n'est redimensionnée. Opt-in :
`AddOrkeonMultiModal(configuration)`.

<!-- settings:Orkeon:MultiModal -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonMultiModal()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `true` | Si les contenus multimodaux sont activés. |
| `MaxAudioDurationSeconds` | entier | `300` | Durée audio maximale autorisée, en secondes (défaut : 300 secondes, soit 5 minutes). |
| `MaxImageSizeBytes` | entier | `20971520` | Taille d'image maximale autorisée, en octets (défaut : 20 Mo). |
| `SupportedAudioFormats` | liste de chaînes | `["audio/wav", "audio/mp3", "audio/ogg"]` | Liste des types MIME audio pris en charge. |
| `SupportedImageFormats` | liste de chaînes | `["image/png", "image/jpeg", "image/gif", "image/webp"]` | Liste des types MIME d'image pris en charge. |
<!-- /settings -->

### `Plugins`

Découverte du répertoire de plugins. Opt-in : `AddOrkeonPlugins(fileSystem, configuration)` —
voir [Plugins](../architecture/plugins.md).

<!-- settings:Plugins -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonPlugins()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ContinueOnError` | booléen | `false` | À `true`, un assembly de plugin qui échoue à se charger ou à configurer ses services est consigné dans `IPluginRegistry.Failures` et les candidats restants continuent de se charger. À `false` (défaut), le premier échec lève une `PluginLoadException` (échec immédiat). |
| `Directory` | chaîne | `"/plugins"` | Chemin virtuel (VFS) du répertoire parcouru à la recherche d'assemblies de plugin. Doit commencer par `/` et se résoudre dans un montage configuré. Défaut : `/plugins`. |
| `SearchPattern` | chaîne | `"*.dll"` | Motif glob simple appliqué aux noms de fichier des assemblies candidats (p. ex. `*.dll`, `MyCompany.*.dll`). Les candidats doivent en outre porter l'extension `.dll`, quel que soit le motif. Défaut : `*.dll`. |
| `SharedAssemblyPrefixes` | liste de chaînes | `["Orkeon.", "Orkeon.Rag.Abstractions", "Microsoft.Extensions."]` | Préfixes de noms simples d'assemblies qui ne sont jamais chargés dans l'`AssemblyLoadContext` isolé du plugin ; la résolution s'en remet au contexte de l'hôte (par défaut), si bien que les types de contrat (p. ex. `IOrkeonPlugin`, `IBaseTool`, `IServiceCollection`) gardent une identité unique, partagée entre l'hôte et le plugin. Défauts : `Orkeon.`, `Orkeon.Rag.Abstractions` et `Microsoft.Extensions.`. |
<!-- /settings -->

## Orchestration et persistance

Comment une crew se construit et décide, et ce qui est gardé de son exécution.

### `Orkeon:CrewFactory`

`StrictTools` fait échouer le chargement d'une crew sur un nom d'outil inconnu (les runners le
mettent à `true` par défaut). Lue par `RunnerHost` → `CrewFactoryOptions`.

<!-- settings:Orkeon:CrewFactory -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `StrictTools` | booléen | `true` | Si un outil qu'une crew nomme et que le registre ne contient pas fait échouer le chargement de la crew, en listant les noms inconnus et ceux qui sont disponibles. `false` écarte l'outil avec un avertissement — le comportement tolérant de la bibliothèque, que les hôtes livrés désactivent. |
<!-- /settings -->

### `Orkeon:Consensus`

Le vote du mode consensuel — les modes et ce qu'est un bulletin sont dans
[Types de processus](../orchestration/process-types.md). Enregistré par
`AddOrkeonInfrastructure()` ; la section gouverne le comportement.

<!-- settings:Orkeon:Consensus -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `EnableDiscussion` | booléen | `true` |  | Si des tours de discussion ont lieu quand le consensus n'est pas atteint. Pendant la discussion, les agents reçoivent le contexte des résultats des autres agents. |
| `FallbackStrategy` | énumération | `"AcceptBestScore"` | `AcceptBestScore`, `Fail`, `ManagerDecision` | La stratégie de repli quand le consensus ne peut être atteint. |
| `MaxVotingRounds` | entier | `3` |  | Le nombre maximal de tours de vote avant d'appliquer le repli. Un tour fait travailler chaque agent sur la tâche, puis recueille un bulletin par agent. |
| `RoleWeights:<nom>` | nombre | — |  | Les poids par rôle pour le vote par consensus pondéré. La clé est le rôle de l'agent, la valeur le multiplicateur de poids. |
| `VotingOptions:AllowAbstention` | booléen | `true` |  | Si l'abstention est permise. À true (le défaut), une abstention ne compte que pour le quorum. À false, une abstention compte comme un vote contre chaque choix : elle reste au dénominateur de chaque part, et elle rompt l'unanimité. Un décompte de Borda, qui n'a pas de seuil de part, n'atteint alors aucun consensus tant que quelqu'un s'abstient. |
| `VotingOptions:ConsensusThreshold` | nombre | `66.7` |  | Le seuil de consensus, en pourcentage. Utilisé par les types SuperMajority et WeightedConsensus. |
| `VotingOptions:ConsensusType` | énumération | `"Majority"` | `Majority`, `SuperMajority`, `Unanimity`, `WeightedConsensus`, `BordaCount` | Le type de consensus à utiliser. |
| `VotingOptions:QuorumPercent` | nombre | `50` |  | Le quorum, en pourcentage : la part minimale de bulletins exprimés parmi tous les bulletins. En dessous, aucun consensus n'est atteint, quoi que disent les bulletins exprimés. |
| `VotingOptions:UseWeightedVotes` | booléen | `false` |  | S'il faut utiliser des votes pondérés. |
<!-- /settings -->

### `Orkeon:ExecutionState:Persistence`

Persistance durable des états d'exécution d'une crew (`ScopedCrewExecutionStateManager`).
Opt-in : `AddCrewExecutionStatePersistence(configuration)` — appelé automatiquement par
`AddOrkeonInfrastructure(configuration)` quand la section existe ; requiert un `IStateStore`.

<!-- settings:Orkeon:ExecutionState:Persistence -->
**Lue par** : un hôte écrit en C# seulement, par `AddCrewExecutionStatePersistence()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `DeleteFromStoreOnArchive` | booléen | `false` | À `true`, archiver une exécution (à son achèvement ou au nettoyage d'expiration) supprime aussi son entrée persistée du state store. Défaut : `false` — le dernier instantané est gardé dans le store pour que les requêtes d'état continuent de répondre une fois l'entrée en mémoire évincée. |
| `Enabled` | booléen | `false` | Active la persistance durable des états d'exécution. Défaut : `false` (en mémoire seulement, identique au comportement historique). |
<!-- /settings -->

### `Orkeon:Checkpointing`

Le state store Postgres (`CheckpointingExtensions`). Opt-in :
`AddOrkeonPostgresCheckpointing(configuration)`.

<!-- settings:Orkeon:Checkpointing -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonPostgresCheckpointing()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AutoMigrate` | booléen | `true` | S'il faut créer automatiquement le schéma et les tables au premier usage. |
| `ConnectionString` | chaîne, secret | — | La chaîne de connexion PostgreSQL. |
| `MaxHistoryPerSession` | entier | `1000` | Le nombre maximal d'entrées de version par session (défaut : 1000). |
| `SchemaName` | chaîne | `"orkeon"` | Le nom du schéma (défaut : "orkeon"). |
<!-- /settings -->

## Scripts et console

Le sandbox des scripts `.ork.ts`, la toolchain esbuild, et la console interactive.

### `Orkeon:Scripting:Limits`

Le sandbox Jint de chaque moteur de script (`Orkeon.Scripting`) : environ 100 Mo de mémoire, une
profondeur de récursion de 64 et 30 s par défaut.

<!-- settings:Orkeon:Scripting:Limits -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ExecutionTimeout` | durée | `"00:00:30"` | Durée maximale, en temps réel, pendant laquelle le moteur peut tourner, au total — le temps passé à attendre un outil ou un modèle compte. Défaut : 30 secondes, un plafond pour des scripts de provenance inconnue ; un run long de confiance le relève. |
| `MemoryLimitBytes` | entier | `104857600` | Mémoire cumulée maximale que le moteur peut allouer, en octets — comptée depuis la création du moteur, pas à son pic. Défaut : 100 Mo, un plafond pour des scripts de provenance inconnue ; un run de confiance qui en demande plus le relève. |
| `RecursionLimit` | entier | `64` | Profondeur de récursion maximale avant que le moteur ne lève une erreur. Défaut : 64 : elle arrête une récursion emballée bien avant le débordement de la pile .NET et laisse de la place à une logique de script légitimement imbriquée. Un script de confiance peut la relever. |
<!-- /settings -->

### `Orkeon:Scripting:Toolchain`

Résolution de la toolchain esbuild : `EsbuildPath` vient en premier dans l'ordre de recherche.
Lue par `EsbuildTranspiler.Create` — `orkeon run`, le runner partagé, `orkeon doctor`,
`orkeon forge`, les commandes `*.cmd.ts`.

<!-- settings:Orkeon:Scripting:Toolchain -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `EsbuildPath` | chaîne | — | Chemin absolu du binaire esbuild. À null, le transpileur se rabat sur la variable d'environnement `ORKEON_ESBUILD_PATH`, puis sur un binaire livré à côté de l'application (`esbuild-bin/esbuild`), puis sur une recherche dans le PATH. |
| `EsbuildTimeout` | durée | `"00:00:30"` | Temps maximal dont dispose esbuild pour transpiler une source. Défaut : 30 s. |
<!-- /settings -->

### `Orkeon:Cli:ScriptCommands`

Découverte des commandes CLI TypeScript (`Orkeon.Cli.Commands.Scripting`), et sous `Limits` un
profil de sandbox plus strict que celui des scripts.

<!-- settings:Orkeon:Cli:ScriptCommands -->
**Lue par** : `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ContinueOnConflict` | booléen | `true` | À false, deux scripts qui déclarent le même nom de commande sont une erreur fatale au démarrage. |
| `Directories` | liste de chaînes | `[]` | Chemins virtuels parcourus à la recherche de `*.cmd.ts`. Le premier répertoire l'emporte en cas de conflit. |
| `Enabled` | booléen | `true` | Interrupteur principal. False ⇒ la découverte est sautée, le registre est vide. |
| `EsbuildTranspile` | booléen | `true` | À false, `*.cmd.js` seulement (pas de transpilation TS). Pour le test et le débogage ; la production le laisse à true. |
| `FailFastOnInvalidScript` | booléen | `false` | À true, le premier script invalide interrompt le chargeur (à utiliser en CI). |
| `FallbackCommandName` | chaîne | `"assistant"` | Nom de la commande scriptée qui traite le texte libre (une ligne du REPL sans préfixe `/`). Dans la surface de l'agent de code, c'est l'assistant interactif relié à la crew `main-loop`. Vide ou inconnu ⇒ le texte libre donne le message de commande inconnue au lieu d'être routé. |
| `Limits:ExecutionTimeout` | durée | `"00:05:00"` | Délai strict d'une invocation de commande. Défaut : 5 minutes. |
| `Limits:MemoryLimitBytes` | entier | `67108864` | Plafond de mémoire en octets. Défaut : 64 Mo (profil CLI, spec §8.1). |
| `Limits:RecursionLimit` | entier | `100` | Plafond de profondeur de récursion. Défaut : 100 (hérité de la spec §8.1 — le même que le plafond global). |
| `MaxScripts` | entier | `50` | Plafond strict du nombre de moteurs gardés en cache. |
<!-- /settings -->

### `Orkeon:Cli:ScriptHost`

Résolution des crews `<nom>/crew.ork.ts` (`ScriptHostFacade`).

<!-- settings:Orkeon:Cli:ScriptHost -->
**Lue par** : `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `CrewDirectories` | liste de chaînes | `[]` | Répertoires virtuels sous lesquels vivent les crews, en `<dir>/<name>/crew.ork.ts`. La résolution essaie chaque répertoire dans l'ordre ; le premier qui correspond l'emporte. |
| `CrewFileName` | chaîne | `"crew.ork.ts"` | Le nom du fichier d'entrée de la crew, résolu sous chaque `<dir>/<name>/`. |
| `RunCrewTimeout` | durée | `"00:10:00"` | Borne supérieure d'un appel synchrone `script-host.runCrew`. Quand elle expire, le script appelant reçoit une `TimeoutException` claire, le run abandonné est annulé de façon coopérative, et le fil du moteur ou du REPL est toujours libéré. Défaut : 10 minutes — généreux à dessein, puisque `runCrew` est fait pour des crews courtes (les workflows longs passent par le cycle de tickets de `runCrewAsync`). Zéro ou une valeur négative désactive la borne. |
<!-- /settings -->

### `Orkeon:Cli:ConsoleStreaming`

Deltas `ctx.llm.act` streamés sur la console REPL (`AddLlmConsoleStreaming(configuration)`).

<!-- settings:Orkeon:Cli:ConsoleStreaming -->
**Lue par** : `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `false` | Si chaque morceau d'une réponse est écrit sur la console à mesure que le modèle l'envoie, au lieu de la réponse entière à la fin. |
<!-- /settings -->

### `Orkeon:Cli:Session`

La fenêtre de contexte utilisée par `token_budget` (`TokenBudgetTool`) et le TUI.

<!-- settings:Orkeon:Cli:Session -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `ContextWindowTokens` | entier | — | La fenêtre de contexte du modèle, en tokens : un nombre supérieur à zéro. Absente, elle vaut `200000`. |
<!-- /settings -->

### `Orkeon:Cli:Tui`

Les verbes du spinner du TUI.

<!-- settings:Orkeon:Cli:Tui -->
**Lue par** : `orkeon-repl`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `SpinnerVerbs` | liste de chaînes | `[]` | Les verbes que la ligne d'état fait défiler pendant que le modèle travaille ("Thinking", "Reading"…). Omise ou vide, la liste propre de la console. |
<!-- /settings -->

## Hôte de service et A2A

Le démon `orkeon-host`, son canal de discussion, et le protocole A2A — voir
[Hôte de service](../architecture/service-host.md).

### `Orkeon:Host`

Le démon (`Orkeon.Host`) :

- **`Crews:<i>`** — chaque crew hébergée : `Name` (unique, sans tenir compte de la casse),
  `Path`, `Profile:MaxConcurrentRuns` (runs de chat et A2A comptés ensemble), `Mounts` (l'espace
  de montages propre à la crew) et `Description` (ce que la compétence A2A de la crew dit qu'elle
  fait).
- **`RunTimeout`**, **`ShutdownGracePeriod`**.
- **`LlmProfiles`** — la liste blanche des `Llm:Profiles` que les crews hébergées peuvent
  nommer — leurs agents, tâches et managers — et `Orkeon:Rag:LlmProfile` aussi ; absente, tous
  sont offerts, `["default"]` n'offre que le défaut ; une entrée qui nomme un profil non défini,
  ou un profil RAG que la liste écarte, refuse le démarrage.
- **`A2A`** — le serveur A2A, éteint par défaut (GAP-23) : `Host` vaut `http://localhost`, et
  `http://+` écoute sur toutes les interfaces ; `Crews` liste les crews que d'autres agents
  peuvent lancer, par nom — une compétence chacune, une tâche étant un run de cette crew ; aucune
  par défaut. Une section activée qui n'expose aucune crew ou une crew que `Crews` ne déclare
  pas, un `Host` ou un `Port` mal formé, ou une écoute au-delà de la boucle locale alors que
  `A2A:Security` ne déclare ni schéma d'authentification ni mTLS refuse le démarrage — voir
  [Hôte de service](../architecture/service-host.md#5-autres-agents-a2a).

<!-- settings:Orkeon:Host -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `A2A:Crews` | liste de chaînes | `[]` | Les crews hébergées que d'autres agents peuvent lancer, par nom — une compétence chacune. Aucune par défaut. |
| `A2A:Enabled` | booléen | `false` | Sert les crews exposées en A2A. Éteint, l'hôte n'écoute sur aucun port. |
| `A2A:Host` | chaîne | `"http://localhost"` | Le schéma et le nom d'hôte de l'écoute : `http://localhost` par défaut, `http://+` pour toutes les interfaces. Ni port (c'est `Port`) ni chemin. |
| `A2A:Port` | entier | `5002` | Le port de l'écoute. |
| `Crews:<i>:Description` | chaîne | — | Ce que fait la crew, en une phrase : la description de sa compétence sur la carte d'agent A2A quand `Orkeon:Host:A2A` l'expose. Lue dans la configuration, jamais dans la définition de la crew — la carte se construit sans charger de crew. Facultative. |
| `Crews:<i>:Mounts` | liste de chaînes | `[]` | Les dossiers que cet hôte accorde à CETTE crew, sous forme de chaînes de montage (`<physical>:<virtual>:<rights>`) : un espace de montages propre à chaque run, si bien que deux crews hébergées peuvent toutes deux adresser `/output` sur deux dossiers différents. Vide (le défaut) garde les montages de l'hôte pour cette crew. Un dossier accordé doit se trouver sous la racine de l'espace de travail, ou sous `PathSecurity:AdditionalAllowedDirectories`. |
| `Crews:<i>:Name` | chaîne | `""` | Le nom par lequel les canaux et l'opérateur la désignent. |
| `Crews:<i>:Path` | chaîne | `""` | Chemin de la définition de la crew — un fichier YAML, un répertoire de crew multi-fichiers ou un script `.ork.ts`. Les mêmes cibles qu'accepte `orkeon run`. |
| `Crews:<i>:Profile:MaxConcurrentRuns` | entier | `4` | Combien de runs de cette crew peuvent être en cours à la fois. Borné à dessein : un démon qui accepte un nombre illimité de runs simultanés est un démon qui meurt à sa première rafale. |
| `LlmProfiles` | liste de chaînes | `[]` | Les profils LLM (`Llm:Profiles:<nom>`) que les crews hébergées peuvent nommer. Le service lance des crews qu'il ne contrôle pas : absente, tout profil que définit la configuration est offert ; présente, seuls ceux de la liste — une crew qui en nomme un autre échoue à se charger, et le run avec elle. Le profil par défaut (la section `Llm`) est toujours offert, donc `["default"]` n'offre que lui. Chaque entrée doit nommer un profil défini. |
| `RunTimeout` | durée | `"00:30:00"` | Combien de temps un run peut durer avant que l'hôte ne l'annule. Un démon n'a pas d'utilisateur qui le regarde pour presser Ctrl-C : un run sans borne est un démon bloqué. |
| `ShutdownGracePeriod` | durée | `"00:00:20"` | Combien de temps l'hôte attend les runs en cours à l'arrêt avant d'abandonner. Systemd envoie SIGKILL après son propre délai : celui-ci doit rester en dessous. |
<!-- /settings -->

### `Orkeon:Host:Discord`

Le canal Discord. `Routes` associe un identifiant de salon Discord à un nom de crew : un fil
ouvert dans ce salon démarre cette crew ; `DefaultCrew` est la crew qu'atteint un salon sans
route, la première crew déclarée si absent. Une route vers une crew non déclarée, une clé de
route qui n'est pas un identifiant de salon ou un `DefaultCrew` inconnu refusent le démarrage.

<!-- settings:Orkeon:Host:Discord -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AllowedUserIds` | liste de chaînes | `[]` | Identifiants des utilisateurs Discord autorisés à parler au bot. **Vide refuse tout le monde** — un bot sur un serveur public, porte ouverte, dépense le budget d'API de quelqu'un pour des inconnus. |
| `DefaultCrew` | chaîne | — | La crew qu'atteint un salon sans route. Absente, c'est la première crew déclarée sous `Orkeon:Host:Crews` ; présente, elle doit nommer l'une d'elles. |
| `Enabled` | booléen | `false` | Si le canal est allumé. Éteint tant qu'un déploiement ne dit pas le contraire. |
| `GuildIds` | liste de chaînes | `[]` | Identifiants des guildes (serveurs) où enregistrer les commandes slash. **Vide les enregistre globalement**, ce qui ne demande aucune configuration mais reste en cache chez Discord jusqu'à une heure ; nommer des guildes rend `/status` et `/stop` disponibles immédiatement — la boucle de développement. |
| `ProgressInterval` | durée | `"00:00:02"` | L'espacement des mises à jour de progression. La limite de débit de Discord est par salon et sans indulgence ; un message par pensée d'agent l'épuiserait en une seule crew. |
| `Routes:<nom>` | chaîne | — | Quelle crew atteint chaque salon : un identifiant de salon Discord (le salon où s'ouvrent les fils) associé au nom d'une crew hébergée. Un fil ouvert dans un salon routé démarre cette crew ; un fil ouvert ailleurs démarre `DefaultCrew`. Une route vers une crew que l'hôte ne déclare pas, ou une clé qui n'est pas un identifiant de salon, refuse le démarrage. |
| `TokenEnvironmentVariable` | chaîne | `"ORKEON_DISCORD_TOKEN"` | Nom de la variable d'environnement qui contient le jeton du bot. |
<!-- /settings -->

### `A2A`

Serveur et client A2A ; `EnableServer` enregistre aussi le service hébergé qui démarre le serveur
avec l'hôte générique. Opt-in : `AddOrkeonA2A(configuration)`. `orkeon-host` y lit l'identité de
la carte et `A2A:Security`, et refuse `EnableServer`, `Host` et `Port` au démarrage — son écoute
est `Orkeon:Host:A2A`, [plus haut](#orkeonhost) ; voir
[Conformité A2A](./a2a-conformance.md#activation).

<!-- settings:A2A -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AgentDescription` | chaîne | `"Orkeon A2A Agent"` | La description de l'agent annoncée dans la carte d'agent. |
| `AgentName` | chaîne | `"Orkeon"` | Le nom de l'agent annoncé dans la carte d'agent. |
| `AgentVersion` | chaîne | `"1.0.0"` | La version de l'agent annoncée dans la carte d'agent. |
| `ContactUrl` | URI | — | URL de contact du champ fournisseur de la carte d'agent. |
| `EnableServer` | booléen | `false` | S'il faut lancer un serveur A2A qui expose les agents locaux à la soumission de tâches distantes : enregistre `IA2AServer` et un service hébergé qui le démarre avec l'hôte. |
| `Host` | chaîne | `"http://localhost"` | L'hôte ou le préfixe du serveur HTTP A2A (p. ex. "http://localhost"). |
| `Organization` | chaîne | — | Nom de l'organisation du champ fournisseur de la carte d'agent. |
| `Port` | entier | `5002` | Le port sur lequel écoute le serveur HTTP A2A. |
| `TimeoutSeconds` | entier | `30` | Délai, en secondes, des requêtes HTTP sortantes vers les agents distants. |
<!-- /settings -->

### `A2A:Security`

TLS mutuel et authentification des points d'accès A2A : chaque schéma d'`AllowedAuthSchemes`
(`Bearer`, `ApiKey`) exige son validateur, sinon le serveur refuse de démarrer ;
`ApiKeySecretNames` sont les noms des secrets qui portent les clés acceptées, lus via
`ISecretProvider` ; côté client, `ClientCredentialSecretName` est le secret qu'`A2AClient` envoie
en `Authorization`. Même opt-in qu'[`A2A`](#a2a) — voir
[Sécurité](../architecture/security.md#tls-mutuel-a2a).

<!-- settings:A2A:Security -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `AllowedAuthSchemes` | liste de chaînes | `[]` | Schémas d'authentification que le serveur accepte sur ses points d'accès de tâches : `Bearer` et/ou `ApiKey`. Vide signifie qu'aucune authentification n'est requise. Chaque schéma déclaré est validé, pas seulement reconnu (`A2ACredentialValidator`) : un jeton bearer par un `IAuthenticationProvider` enregistré (`A2A:Security:AzureAD` / `A2A:Security:Oidc`), une clé d'API contre `ApiKeySecretNames`. Le serveur refuse de démarrer quand un schéma déclaré n'a pas de validateur. |
| `ApiKeySecretNames` | liste de chaînes | `[]` | Noms des secrets qui portent les clés d'API qu'accepte le schéma `ApiKey` (`Authorization: ApiKey <key>`). Chaque nom est lu par l'`ISecretProvider` à chaque requête — `ORKEON_<NAME>` avec la chaîne par défaut — si bien que les clés ne résident jamais dans le fichier de configuration et tournent sans redémarrage. |
| `ClientAuthScheme` | chaîne | — | Côté client : le schéma de l'identifiant que le client envoie à chaque appel de tâche (`Authorization: <scheme> <credential>`) — `Bearer` ou `ApiKey`, en accord avec ce que le pair déclare dans ses `AllowedAuthSchemes`. Vide signifie que le client n'envoie pas d'en-tête `Authorization`. La découverte de la carte d'agent ne le porte jamais. |
| `ClientCertificatePassword` | chaîne, secret | — | Mot de passe de la clé privée du certificat client. |
| `ClientCertificatePath` | chaîne | — | Chemin du fichier de certificat client pour l'authentification mTLS. |
| `ClientCredentialSecretName` | chaîne | — | Côté client : nom du secret qui porte l'identifiant envoyé avec `ClientAuthScheme`, lu par l'`ISecretProvider` à chaque appel (`ORKEON_<NAME>` avec la chaîne par défaut), si bien qu'il ne réside jamais dans le fichier de configuration et tourne sans redémarrage. Un appel échoue avant toute requête quand il ne peut pas être lu. |
| `RequireMutualTls` | booléen | `false` | Si le mTLS est requis pour la communication de serveur à serveur. Activé, le serveur exige un certificat client qui se rattache à l'une des `TrustedCertificateAuthorities` ou correspond à l'une des `TrustedClientCertificateThumbprints` ; démarrer le serveur sans aucune ancre de confiance configurée lève une erreur (fermé par défaut) — la simple présence d'un certificat en cours de validité n'est pas une authentification. |
| `TrustedCertificateAuthorities` | liste de chaînes | `[]` | Liste des chemins de certificats d'autorités de certification de confiance. Côté client : quand elle est définie, seuls les serveurs présentant un certificat signé par ces AC sont de confiance (épinglage d'une AC privée). Côté serveur : quand `RequireMutualTls` est activé, les certificats clients entrants doivent se rattacher à l'une de ces AC (sauf épinglage par `TrustedClientCertificateThumbprints`). |
| `TrustedClientCertificateThumbprints` | liste de chaînes | `[]` | Empreintes (hexadécimales, insensibles à la casse) des certificats clients acceptés par le serveur quand `RequireMutualTls` est activé — un épinglage exact, vérifié avant la validation de chaîne des `TrustedCertificateAuthorities`. Un certificat épinglé reste rejeté hors de sa période de validité. |
<!-- /settings -->

### `A2A:Security:AzureAD`

Le validateur Azure AD des jetons bearer.

<!-- settings:A2A:Security:AzureAD -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Authority` | chaîne | `""` | URL de l'autorité (p. ex. https://login.microsoftonline.com/{tenantId}/v2.0). |
| `ClientId` | chaîne | `""` | Identifiant d'application (client) enregistré dans Azure AD. |
| `TenantId` | chaîne | `""` | Identifiant du tenant Azure AD. |
| `ValidAudiences` | liste de chaînes | `[]` | Audiences valides pour la validation des jetons. |
| `ValidIssuers` | liste de chaînes | `[]` | Émetteurs de jetons valides. Par défaut les émetteurs Azure AD standard du tenant. |
<!-- /settings -->

### `A2A:Security:Oidc`

Le validateur OIDC générique des jetons bearer.

<!-- settings:A2A:Security:Oidc -->
**Lue par** : `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Authority` | chaîne | `""` | URL de l'autorité OIDC (p. ex. https://login.example.com/realms/myapp). |
| `ClientId` | chaîne | `""` | Identifiant client enregistré auprès du fournisseur OIDC. |
| `RequireHttpsMetadata` | booléen | `true` | S'il faut exiger HTTPS pour le point d'accès des métadonnées. Vrai par défaut. |
| `ValidAudiences` | liste de chaînes | `[]` | Audiences valides pour la validation des jetons. |
<!-- /settings -->

## Observabilité

Journaux, traces et métriques.

### `Logging`

La configuration de journalisation de .NET. Un niveau vaut `Trace`, `Debug`, `Information`,
`Warning`, `Error`, `Critical` ou `None`, sans tenir compte de la casse ; la section reste
ouverte au-delà de ses niveaux ([plus haut](#quand-un-réglage-est-refusé)).

<!-- settings:Logging -->
**Lue par** : `orkeon`, `orkeon-host`, `orkeon-repl`.

| Clé | Type | Défaut | Valeurs | Sens |
|---|---|---|---|---|
| `Console` | libre | — |  | La section propre du journaliseur console : son `LogLevel` par catégorie, `FormatterName`, `FormatterOptions` et les autres clés du journaliseur console de .NET. |
| `LogLevel:<nom>` | énumération | — | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` | Le niveau le plus bas journalisé, par catégorie : `Default` pour toute catégorie sans ligne à elle, un espace de noms comme `Orkeon.Infrastructure` pour ce qu'il journalise. Un niveau qui n'en est pas un refuse le démarrage, en nommant la clé. |
<!-- /settings -->

### `Telemetry`

Export OpenTelemetry. `OtlpEndpoint` est une adresse `http://` ou `https://`, refusée par sa clé
sinon ; la variable standard `OTEL_EXPORTER_OTLP_ENDPOINT` marche aussi dans les runners.
`ExportToConsole` et `PrometheusEndpoint` sont supprimées et refusées, en nommant le collecteur
OTLP qui les remplace : l'exportateur console écrivait sur le stdout que lit un programme, et
rien ne servait Prometheus (GAP-35). Lue par `AddOrkeonTelemetry(configuration)` — appelé par
`AddOrkeonInfrastructure(configuration)` et `RunnerHost`.

<!-- settings:Telemetry -->
**Lue par** : `orkeon`, `orkeon-host`.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `Enabled` | booléen | `true` | Si la télémétrie est activée. Vrai par défaut. |
| `MaxMemoryMB` | entier | `2048` | Le seuil de mémoire maximal, en Mo, des contrôles de santé. 2048 Mo par défaut. |
| `OtlpEndpoint` | chaîne | — | Le point d'accès de l'exportateur OTLP (OpenTelemetry Protocol). Quand il est défini, les traces et les métriques y sont exportées. Exemple : "http://localhost:4317" pour gRPC ou "http://localhost:4318" pour HTTP. |
<!-- /settings -->

### `Orkeon:Monitoring`

Le backend de monitoring. Opt-in : `AddOrkeonMonitoring(configuration)`.

<!-- settings:Orkeon:Monitoring -->
**Lue par** : un hôte écrit en C# seulement, par `AddOrkeonMonitoring()` — aucun binaire livré ne lit cette section.

| Clé | Type | Défaut | Sens |
|---|---|---|---|
| `MaxTraceHistory` | entier | `1000` | Nombre maximal de traces terminées gardées dans le tampon circulaire. Les traces les plus anciennes sont évincées quand cette limite est atteinte. |
| `MetricsRetentionMinutes` | entier | `60` | Nombre de minutes de conservation des métriques détaillées. |
| `TraceSourcePrefix` | chaîne | `"Orkeon"` | Préfixe des noms de sources d'activité dont les traces sont capturées. Par défaut toutes les sources Orkeon ; resserrez-le pour observer un seul sous-système — ou, dans un test, sur un nom que rien d'autre n'émet, puisque l'écouteur vaut pour tout le processus et capterait sinon une activité sans rapport. |
<!-- /settings -->

## Variables d'environnement

Trois sortes de variables d'environnement atteignent Orkeon : celles qui **portent un réglage**,
celles qu'un binaire **lit par leur nom**, et celles **qu'un réglage nomme**. `orkeon settings env`
imprime les trois mêmes listes dans le terminal, hors ligne ([CLI](./cli.md#orkeon-settings)).

### Les variables qui portent un réglage

Tout réglage de cette page peut venir de l'environnement plutôt que du fichier : la variable est
le chemin du réglage, `__` entre ses niveaux. L'ordre des couches, la règle sur la casse et les
deux orthographes qu'un réglage a sous Linux et macOS sont dans
[D'où viennent les réglages](#doù-viennent-les-réglages).

| Variable | Ce qu'elle est | Où elle se place |
|---|---|---|
| `ORKEON_<Section>__<Clé>` | Tout réglage, par son chemin : `ORKEON_Llm__Model` est `Llm:Model`, `ORKEON_RateLimiting__MaxConcurrentRequests` est `RateLimiting:MaxConcurrentRequests`, `ORKEON_Orkeon__Rag__Profile` est `Orkeon:Rag:Profile`. `ORKEON_Llm__ApiKey` est la variable qu'`orkeon init` et Orkeon Studio proposent pour la clé du modèle par défaut. | Par-dessus le fichier de réglages : la variable l'emporte. |
| `<Section>__<Clé>` | Le même réglage sans le préfixe : `Llm__Model`. | Sous le fichier de réglages : le fichier l'emporte. |
| `ORKEON_<NOM>` | Un secret, par son nom : `ORKEON_TAVILY_API_KEY` est la clé de `web_search`, `ORKEON_OPENAI_API_KEY` celle d'`image_generation`. | Le premier maillon de la chaîne des secrets, avant `Secrets:<NOM>` dans le fichier. |

Une variable du tableau suivant qui commence par `ORKEON_` passe aussi par cette couche, comme
une clé qu'aucun réglage n'est — `ORKEON_DEBUG` est la clé `DEBUG` de la racine. La racine de la
configuration reste ouverte ([plus haut](#quand-un-réglage-est-refusé)) : elle ne refuse rien.

### Les variables qu'un binaire lit par leur nom

Ce ne sont pas des réglages : aucune clé de cette page n'en tient lieu, et le binaire nommé lit
la variable lui-même. La liste est celle que porte le code — un test y tient ce tableau.

| Variable | Lue par | Valeur | Effet |
|---|---|---|---|
| `ORKEON_ALLOW_EXTERNAL_MOUNTS` | `orkeon` | `1`, `true` ou `yes` | Vaut `--allow-external-mounts` pour chaque `orkeon run` et chaque `orkeon rag` : un `--mount` peut désigner un dossier hors du répertoire courant. Un déploiement en bac à sable la pose une fois, sa propre frontière tenant lieu d'isolation. |
| `ORKEON_DEBUG` | `orkeon` | `1`, `true` ou `yes` | Une erreur inattendue imprime son exception — la chaîne des types et la pile — au lieu d'une ligne. |
| `ORKEON_MCP_SERVE` | `orkeon` | posée par `orkeon mcp serve`, jamais à la main | Marque chaque processus que lance `orkeon mcp serve`. Sous elle, `orkeon mcp serve` refuse de démarrer : des réglages qui le rangent parmi leurs propres serveurs MCP ne peuvent pas le lancer en boucle. |
| `ORKEON_ESBUILD_PATH` | `orkeon`, `orkeon-host`, `orkeon-repl` | le chemin d'un exécutable esbuild | Où un script `.ork.ts` trouve esbuild, après `Orkeon:Scripting:Toolchain:EsbuildPath` et avant la copie livrée à côté du binaire. Les lanceurs d'une installation la posent sur l'esbuild qu'ils livrent quand elle est absente. |
| `ORKEON_LLM_API_KEY` | `orkeon` | une clé d'API | La variable où `orkeon llm probe` et `orkeon llm models` lisent la clé quand `--api-key-env` n'en nomme pas d'autre. |
| `BRAVE_API_KEY` | `orkeon`, `orkeon-host` | une clé d'API Brave Search | Inscrit l'outil `brave_search`. Elle est lue d'abord comme une clé de configuration : le fichier de réglages et `ORKEON_BRAVE_API_KEY` la donnent aussi. |
| `ORKEON_DISCORD_TOKEN` | `orkeon-host` | un jeton de bot Discord | Le jeton du canal Discord : le défaut de `Orkeon:Host:Discord:TokenEnvironmentVariable`, qui peut nommer une autre variable. |
| `OLLAMA_BASE_URL` | `orkeon`, `orkeon-host`, `orkeon-repl` | l'adresse d'un serveur Ollama | L'adresse que prend le fournisseur Ollama quand on ne lui en donne aucune : `Llm:BaseUrl` l'emporte, et `http://localhost:11434` est ce qui reste sans l'une ni l'autre. |
| `ORKEON_CLI_DIR` | `orkeon-studio`, `orkeon-studio-config`, `orkeon-studio-run` | un dossier | Où Orkeon Studio cherche l'exécutable `orkeon`, après `--cli-dir` et son propre dossier d'installation, avant le `PATH`. |
| `ORKEON_STUDIO_TEAMS_ROOT` | `orkeon-studio`, `orkeon-studio-run` | un dossier, par son chemin complet | Le dossier où Orkeon Studio range ses équipes. Elle l'emporte sur `--teams-root` et sur le dossier choisi dans Réglages › Studio. |
| `ORKEON_CUSTOM_LLM_API_KEY` | `orkeon-studio`, `orkeon-studio-config` | une clé d'API | La variable qu'Orkeon Studio propose pour la clé d'un réglage de modèle « compatible OpenAI », et qu'il écrit dans l'`ApiKeyEnvVar` de ce réglage : un run la lit par cette clé, pas par ce nom. |
| `TUI_DRIVER` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | `windows`, `dotnet` ou `ansi` | Le pilote Terminal.Gui des interfaces texte, pour le diagnostic. Absente : `windows` sous Windows, `dotnet` ailleurs — `ansi` ne dessine rien sous WSL ni dans bien des terminaux de conteneur. |
| `TUI_DIAG` | `orkeon-repl` | `1` | La console à deux volets écrit des lignes `[tui-diag]` sur stderr pendant son démarrage : le terminal, le pilote choisi. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `orkeon`, `orkeon-host` | l'adresse d'un collecteur OTLP | Active l'export OpenTelemetry quand `Telemetry:OtlpEndpoint` est vide ; l'exportateur lit lui-même l'adresse, et les autres variables `OTEL_EXPORTER_OTLP_*`. |
| `XDG_CONFIG_HOME` | `orkeon`, `orkeon-host`, `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | un dossier, par son chemin absolu | Sous Linux et macOS, la racine des réglages par utilisateur — `$XDG_CONFIG_HOME/Orkeon/appsettings.json`, `~/.config` quand elle est absente — et des unités systemd utilisateur qu'installe `orkeon forge schedule`. |
| `CI` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | `true` | Aucune interface texte ne s'ouvre : `orkeon-repl` se replie sur sa console simple, les deux applications terminal de Studio disent qu'il leur faut un terminal et s'arrêtent. |
| `TERM` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | un type de terminal | Sous Linux, absente ou vide, elle a l'effet de `CI=true` : il n'y a pas de terminal où dessiner. |

**Transmises, pas lues.** L'outil `shell_command` remet à la commande qu'il lance une liste fixe
de variables de l'hôte — `PATH`, `HOME`, la locale, les dossiers temporaires, `DOTNET_ROOT`,
`NUGET_PACKAGES` et ce qu'il faut à Windows pour lancer un programme —, et le bac à sable de code
bâtit un environnement plus réduit encore : ni l'un ni l'autre ne les lit comme des réglages, et
aucune clé ne passe par là. `PATH` est aussi l'endroit où un script `.ork.ts` cherche esbuild en
dernier, et Orkeon Studio l'exécutable `orkeon` ; `HOME` est lue quand le système ne nomme aucun
dossier de profil, pour placer les réglages par utilisateur. Les variables écrites sous
`MCP:Servers:<nom>:Env` sont celles de l'opérateur : Orkeon les remet au serveur qu'il lance et
n'en lit aucune. L'image de conteneur a ses propres variables, lues par son script d'entrée et
non par les binaires — `ORKEON_RUNNER` et ses voisines
([trois façons de lancer Orkeon](../getting-started/three-ways-to-run-orkeon.md#3-conteneur)).

### Les variables qu'un réglage nomme

Une clé qui finit par `EnvVar` ou `EnvironmentVariable` porte le **nom** d'une variable, jamais
une valeur : le secret reste hors du fichier. `orkeon settings <clé>` décrit chacune.

| Clé | Ce que porte la variable qu'elle nomme | Lue où |
|---|---|---|
| `Llm:ApiKeyEnvVar` | La clé d'API du modèle par défaut, lue quand aucun `Llm:ApiKey` n'est résolu ([la clé d'API](#la-clé-dapi-apikey-apikeyenvvar)). | L'environnement du processus, puis les variables de l'utilisateur sous Windows. |
| `Llm:Profiles:<nom>:ApiKeyEnvVar` | La clé d'API de ce profil, lue de la même façon. | L'environnement du processus, puis les variables de l'utilisateur sous Windows. |
| `Orkeon:Tools:Email:Accounts:<nom>:Auth:PasswordEnvVar` | Le mot de passe, ou mot de passe d'application, du compte e-mail. | L'environnement du processus, puis les variables de l'utilisateur sous Windows. |
| `Orkeon:Tools:Email:Accounts:<nom>:Auth:ClientSecretEnvVar` | Le secret client OAuth du compte e-mail. | L'environnement du processus, puis les variables de l'utilisateur sous Windows. |
| `Orkeon:Rag:WebFallback:ApiKeyEnvVar` | La clé de la recherche web sur laquelle le RAG correctif se replie ; vide, aucune clé n'est envoyée. | L'environnement du processus. |
| `Orkeon:Host:Discord:TokenEnvironmentVariable` | Le jeton de bot du canal Discord ; la clé nomme `ORKEON_DISCORD_TOKEN` quand rien ne la règle. | L'environnement du processus. |

La variable conventionnelle de chaque fournisseur — `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, … — est
un nom à écrire dans `ApiKeyEnvVar` :
[Clés d'API : la variable par fournisseur](./llm-providers-comparison.md#clés-dapi--la-variable-par-fournisseur).

---

> **Voir aussi** : [Sous-systèmes opt-in](./opt-in-subsystems.md) ·
> [Hébergement](./hosting.md) ·
> [Système de mémoire](../architecture/memory-system.md) ·
> [Pipeline RAG](../architecture/rag-pipeline.md) ·
> [Retour à l'index](../INDEX.md)
