> 🇬🇧 [English version](../../reference/configuration.md)

# Référence de configuration (`appsettings.json`)

Cette page est la carte unique des sections de configuration lues par le framework. La
colonne « Opt-in » nomme le geste d'activation lorsque la section ne prend effet qu'après
un enregistrement dédié — voir [Sous-systèmes opt-in](./opt-in-subsystems.md) pour le
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
l'emportant quand les deux existent.

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
  refusée avec sa migration. Un nom de section sous `Orkeon:` et sous les groupes `Orkeon:Cli`,
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
ne lit pas — `orkeon run` laisse `Orkeon:Host` au démon, qui la juge. **Les comptes e-mail sont
l'exception** : un compte qui porte une valeur ou une clé illisible est mis de côté et signalé quand
un appel ou `orkeon email` le nomme, et les autres continuent de fonctionner ([e-mail](../guides/email.md)).

Un hôte C# qui démarre (`StartAsync`, un AppHost .NET Aspire) refuse les valeurs et les noms que lient
ses inscriptions Orkeon — chacune est inscrite avec `ValidateOnStart` — ; les clés de ses sections
restent les siennes. Un conteneur bâti à la main et jamais démarré ne juge rien tant qu'aucune option
n'est lue.

## Provider LLM (section `Llm`)

La section `Llm` est lue par `RunnerHost.RegisterLlmProvider` et transformée en
`ILlmProvider` via `ILlmProviderFactory`. **Le provider est inféré automatiquement**, dans
l'ordre : motifs d'hôte du `BaseUrl` (p. ex. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/compatible OpenAI), puis motifs du nom de modèle, puis
forme de la clé API ; défaut `openai`. Une section sans `Model` tourne sur le modèle par défaut de
ce fournisseur (la colonne *Défaut (code)* du
[comparatif des fournisseurs](llm-providers-comparison.md#défauts-et-modèles-plus-récents--revue-des-catalogues-du-2026-09-19)),
jamais sur celui d'OpenAI posé sur un autre vendeur. Clés : `Model`, `BaseUrl`, `ApiKeyEnvVar` (la
variable d'environnement qui contient la clé — [plus bas](#la-clé-dapi-apikey-apikeyenvvar) ; la
variable où vit conventionnellement la clé de chaque fournisseur, et les noms qu'on confond avec
elle, sont dans le
[comparatif des fournisseurs](llm-providers-comparison.md#clés-dapi--la-variable-par-fournisseur)), `ApiKey`
(la clé en clair — déconseillé), `Temperature` (omise, rien n'est envoyé et le modèle applique la
sienne — souvent 1 ; écrivez `0.7` pour garder l'ancien défaut du moteur, GAP-36), `MaxTokens`,
`TimeoutSeconds`, `MaxRetries` (défaut 10), et
`Thinking:{Enabled,Effort}` pour les providers à raisonnement, et `Grammar` (défaut `false`) :
ne le passez à `true` que lorsque `BaseUrl` désigne un serveur compatible llama.cpp (Docker
Model Runner, `llama-server`) — le seul genre de point d'accès qui honore le champ GBNF
`grammar` que produit un livrable `structured_output` ; ailleurs la grammaire est abandonnée avec
un avertissement qui nomme la clé ([comparatif des fournisseurs](llm-providers-comparison.md)).
`TimeoutSeconds` vaut 30 s par
défaut, trop court pour un modèle qui réfléchit avant de répondre (Kimi K2.6, DeepSeek V4 et GLM
le font par défaut) : mettez 600 s, ou coupez la réflexion avec `Thinking:Enabled = false`. Un
appel qui atteint le délai est réessayé une fois, puis fait échouer sa tâche avec un message qui
nomme le réglage — il n'est jamais rapporté comme une réponse vide (LLM-11). **Cette section est
la base de chaque appel** à son fournisseur : un composant qui passe sa propre configuration — le
planificateur, la mémoire cognitive, les résumés de fenêtre de contexte et de RaggableTree, les
boucles d'agent hors du client de chat, un client de chat enregistré sans configuration — complète
celle de la section au lieu de la remplacer. Ce qu'il laisse vide est celui de la section (la clé,
`BaseUrl`, `TimeoutSeconds`, `Thinking`, `MaxTokens`…) ; ce qu'il fixe l'emporte (la température
0,3 du planificateur, le plafond de sortie d'une analyse). Le fournisseur d'un profil fait de même
avec ses propres clés. `MaxTokens` est un
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
refuse est rejoué une fois sans le champ, avec un avertissement qui nomme le modèle. Sans
section `Llm`, le
runtime dégrade vers le provider écho et avertit une fois. Voir
[Providers LLM](../architecture/llm-providers.md) ; des gabarits vivent dans
`examples/appsettings/*.json.example`.

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

## Sections hors préfixe `Orkeon:`

| Section | Configure | Consommateur / opt-in |
|---|---|---|
| `Llm` | Provider LLM actif — le profil par défaut (voir ci-dessus) ; `ApiKeyEnvVar` nomme la variable qui contient sa clé | `RunnerHost`, le REPL (`LlmSettings.ReadDefault`, le même lecteur) |
| `Llm:Profiles:<nom>` | Profils LLM nommés qu'une crew choisit par agent ou par tâche, mêmes clés que `Llm` (voir ci-dessus) ; Orkeon Studio en écrit un par réglage de modèle, avec le nom de la variable qui contient sa clé, jamais la clé | `RunnerHost`, le REPL (`AddOrkeonLlmProfiles(configuration)`) |
| `Llm:AvailableModels` | La liste de modèles qu'une commande REPL scriptée `/model` peut proposer (tableau de chaînes, ou une chaîne séparée par des virgules) | `AddOrkeonSessionTools(configuration)` |
| `Memory:Provider` | TYPE du provider mémoire de l'application (`inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`, ou un alias : `in-memory`, `chroma`, `lance` ; absent → in-memory). Sa connexion est la section propre de ce provider (`Orkeon:Redis`, `Orkeon:Sqlite`, … plus bas). C'est aussi là que vit la mémoire d'une crew nommée avec `memory: true` et sans `memoryProvider:` — voir [Système de mémoire](../architecture/memory-system.md#sélection-par-configuration). Un autre nom, `lancedb` sans `Orkeon:LanceDb:Endpoint` ou `pinecone` sans `Orkeon:Pinecone:ApiKey` (ou `Host`) refuse le démarrage : il ne tourne plus sur le provider volatil ([quand un réglage est refusé](#quand-un-réglage-est-refusé)) | `AddOrkeonInfrastructure()` |
| `RateLimiting` | Les plafonds de l'hôte sur les appels au modèle : chaque appel au modèle de l'hôte compte une fois, à l'entrée de son fournisseur — tours d'agent, manager, planificateur, RAG, juges, mémoire cognitive, `ctx.llm` des scripts (pas les embeddings) — face à `GlobalRequestsPerMinute`, `ProviderRequestsPerMinute`, `MaxConcurrentRequests` et `QueueLimit` ; `AgentRequestsPerMinute` plafonne chaque instance d'agent dans sa propre fenêtre, avec son `maxRpm`, le plus strict l'emportant, et une requête de trop attend son tour ([sécurité](../architecture/security.md)) | `AddOrkeonInfrastructure()` (`ILlmRateLimiter` ; `RateLimitingOptions` dans `Orkeon.Application.Configuration`) |
| `LlmLogging` | Réglage de la capture des échanges LLM : `FullEmbeddingLog` (défaut `true`), `LogStreamingExchanges` (`true`), `MaxBodyLengthChars` (`0` = pas de troncature). Un interrupteur qui ne vaut ni `true` ni `false`, une longueur qui n'est pas un nombre entier, ou une autre clé refuse le démarrage d'un run qui passe `--llm-log` | `RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`, uniquement quand le run passe `--llm-log` |
| `PathSecurity` | Validation des chemins physiques : `DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks`, `MaxFileSizeBytes` | `AddOrkeonInfrastructure()` (`IPathValidator`) |
| `Telemetry` | Export OpenTelemetry : `Enabled`, `OtlpEndpoint` (une adresse `http://` ou `https://`, refusée par sa clé sinon), `MaxMemoryMB` (la variable standard `OTEL_EXPORTER_OTLP_ENDPOINT` marche aussi dans les runners). `ExportToConsole` et `PrometheusEndpoint` sont supprimées et refusées, en nommant le collecteur OTLP qui les remplace : l'exportateur console écrivait sur le stdout que lit un programme, et rien ne servait Prometheus (GAP-35) | `AddOrkeonTelemetry(configuration)` — appelé par `AddOrkeonInfrastructure(configuration)` et `RunnerHost` |
| `A2A` | Serveur/client A2A : `EnableServer`, `Port`, `Host`, `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`, `TimeoutSeconds` ; `EnableServer` enregistre aussi le service hébergé qui démarre le serveur avec l'hôte générique | opt-in `AddOrkeonA2A(configuration)`. `orkeon-host` y lit l'identité de la carte et `A2A:Security`, et refuse `EnableServer`, `Host` et `Port` au démarrage — son écoute est `Orkeon:Host:A2A`, plus bas ; voir [Conformité A2A](./a2a-conformance.md#activation) |
| `A2A:Security` | `ClientCertificatePath`, `ClientCertificatePassword`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` (`Bearer`, `ApiKey` — chacun exige son validateur, sinon le serveur refuse de démarrer), `ApiKeySecretNames` (noms des secrets qui portent les clés acceptées, lus via `ISecretProvider`) ; côté client `ClientAuthScheme` (`Bearer`/`ApiKey`) et `ClientCredentialSecretName` (le secret qu'`A2AClient` envoie en `Authorization`) ; validateurs bearer `A2A:Security:AzureAD` (`TenantId`, `ClientId`, `Authority`, `ValidIssuers`, `ValidAudiences`) et `A2A:Security:Oidc` (`Authority`, `ClientId`, `ValidAudiences`, `RequireHttpsMetadata`) | idem — voir [Sécurité](../architecture/security.md#tls-mutuel-a2a) |
| `MCP` | Connexions client MCP (`MCP:Servers:<id>`), l'interrupteur `MCP:Enabled` (défaut `true`), et `MCP:Server` (`Name`, `Version`) — la façon dont le serveur d'`orkeon mcp serve` se présente (il n'expose que des outils). `MCP:EnableServer` est supprimé : une section qui le porte encore est refusée au démarrage, en nommant `orkeon mcp serve` (GAP-24) | `RunnerHost` (`orkeon run`, `orkeon-host`, `orkeon mcp serve`) dès que `MCP:Servers` déclare au moins un serveur et que `MCP:Enabled` n'est pas `false` — les serveurs sont connectés avant le chargement de la crew (STUDIO-21), par `orkeon-host` une fois au démarrage, avant son premier message (GAP-11), et par `orkeon mcp serve` avant qu'il serve ; `MCP:Server` par `AddOrkeonMcpServer`, qu'appelle `orkeon mcp serve` ; les hôtes bibliothèque appellent `AddOrkeonMcp(configuration)` ou la surcharge `AddOrkeonInfrastructure(configuration)` — voir [Intégration MCP](../architecture/mcp.md) |
| `Secrets:<NOM>` | Second maillon de la chaîne de secrets après `ORKEON_<NOM>` (`ConfigurationSecretProvider`), p. ex. `Secrets:TAVILY_API_KEY` pour `web_search` | `AddOrkeonInfrastructure()` |
| `Evaluation` | `EnableLlmJudge` (défaut `false`) : l'`IEvaluationSuite` par défaut exécute aussi les juges LLM de cohérence, fluidité et ancrage sur l'`IChatClient` enregistré | `AddOrkeonInfrastructure(configuration)`, ou `AddOrkeonEvaluation(configuration)` (idempotent : un second appel n'enregistre aucun évaluateur deux fois) |
| `RaggableTree` | Indexation de codebase : `Enabled`, `Embedding` (`Provider` — `None`, `OpenAI`, `Ollama`, `Onnx` ou `LocalSmartComponents`, sans tenir compte de la casse, le dernier par défaut —, `Model`, `ApiKey`, `BaseUrl`, `Dimensions`, `MaxTextChars`, deux nombres entiers) — rien d'autre : toute autre clé, un autre nom de fournisseur ou un nombre fait échouer l'hôte au démarrage ; ce que couvre un index se règle à chaque appel `index_codebase` | **opt-out dans les hôtes runner** : `RunnerHost` l'enregistre par défaut, `RaggableTree:Enabled = false` le désactive ; les consommateurs bibliothèque appellent `AddRaggableTree(options)` explicitement |
| `ToolRateLimiting` | Rate limits par outil : `GlobalToolRequestsPerMinute`, `DefaultToolRequestsPerMinute`, `ToolSpecificLimits` | opt-in `AddOrkeonToolRateLimiting()` (lie depuis l'`IConfiguration` enregistrée ; voir [sous-systèmes opt-in](./opt-in-subsystems.md)) |
| `TokenBudget` | Budgets de tokens : `MaxTokensPerAgent`, `MaxTokensPerCrew`, `MaxCostPerCrew` | idem |
| `Security:Audit` | Sinks d'audit : `Enabled`, `MinSeverity`, `EnabledCategories`, `AuditDirectory`, `RetentionDays` | `AddOrkeonInfrastructure()` |
| `Security:Url` | Validation SSRF : `AllowedSchemes`, `BlockedPorts`, `AllowedDomains`, `BlockedDomains`, `BlockPrivateIPs`, `ResolveDNS` | `AddOrkeonInfrastructure()` (`IUrlValidator`) |
| `Security:Vault` | Chaîne de coffres à secrets : `AzureKeyVaultUri`, `UseAwsSecretsManager`, `DpapiSecretsDirectory`, `CacheTtl` | `AddOrkeonInfrastructure()` |
| `Security:Prompt` | La phase d'entrée du Guardian : `Policy` (`Block` par défaut — High/Critical fait échouer la tâche, moindre avertit ; `Warn` ; `None`), `CustomPatterns`, `EnableExfiltrationDetection` | `AddOrkeonInfrastructure()` — filtre l'invite de chaque tour d'agent ; voir [Sécurité](../architecture/security.md) |
| `Security:ToolResults` | Filtrage des résultats d'outils : `Policy` (`Warn` par défaut — balisé comme donnée et signalé ; `Block` retient High/Critical ; `None`), `TrustedTools` (ajoutés aux `email_*` par défaut). La longueur ne se règle pas ici : une seule règle, `AgentDefaults.ResolveMaxToolResultLength` | `AddOrkeonInfrastructure()` — appliqué à chaque résultat d'outil par `IToolInvocationPipeline` |
| `BRAVE_API_KEY` | Aussi lu comme **clé de configuration** (pas seulement une variable d'env) pour gater l'outil Brave | `RunnerHost` |
| `Plugins` | Découverte du répertoire de plugins : `Directory` (défaut `/plugins`), `SearchPattern` (`*.dll`), `ContinueOnError`, `SharedAssemblyPrefixes` | opt-in `AddOrkeonPlugins(fileSystem, configuration)` — voir [Plugins](../architecture/plugins.md) |

## Sections `Orkeon:*`

### Cœur, orchestration, persistance

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:CrewFactory:StrictTools` | Échec du chargement de crew sur outil inconnu (défaut runners `true`) | `RunnerHost` → `CrewFactoryOptions` | — |
| `Orkeon:ExecutionState:Persistence` | Persistance durable des états d'exécution (`Enabled`, `DeleteFromStoreOnArchive`) | `ScopedCrewExecutionStateManager` | `AddCrewExecutionStatePersistence(configuration)` — appelé automatiquement par `AddOrkeonInfrastructure(configuration)` quand la section existe ; requiert un `IStateStore` |
| `Orkeon:Checkpointing:*` | State store Postgres (`ConnectionString`, `SchemaName` défaut `orkeon`, `AutoMigrate`, `MaxHistoryPerSession`) | `CheckpointingExtensions` | `AddOrkeonPostgresCheckpointing(configuration)` |
| `Orkeon:Consensus` | Vote du mode consensuel : `VotingOptions`, `MaxVotingRounds`, `EnableDiscussion`, `FallbackStrategy`, `RoleWeights` | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:CostTracking` | Suivi des coûts LLM : `Enabled`, `DefaultCrewBudget`, `CustomPricings` | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:TokenCounter` | Estimation des tokens : `CharsPerToken`, `TokensPerMessage`, `TokensPerReply`, `SpecialTokenOverhead` | Infrastructure | idem |
| `Orkeon:Monitoring` | Backend de monitoring : `MaxTraceHistory`, `MetricsRetentionMinutes`, `TraceSourcePrefix` | Infrastructure | `AddOrkeonMonitoring(configuration)` |

### Embeddings, recherche vectorielle, mémoire

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Embeddings` | Sélection du provider d'embeddings (`Provider` — `openai`, tout générateur d'embeddings M.E.AI qu'inscrit l'hôte, ou `ollama` ; un autre nom refuse le démarrage —, `Model`, `Dimension`, `BatchSize`, `EnableCache`) | `DefaultEmbeddingProviderResolver`, quand aucun provider local ni d'analyse de code n'est inscrit | lié par `AddOrkeonInfrastructure()`, et par `AddOrkeonVectorSearch(configuration)` (appelé par `AddOrkeonInfrastructure(configuration)`) |
| `Orkeon:EmbeddingCache` | Cache d'embeddings (`SlidingExpirationMinutes`, `MaxCacheSizeBytes`) | idem | idem |
| `Orkeon:VectorSearch` | Options de recherche vectorielle (`DefaultMetric`, `DefaultTopK`, `DefaultMinScore`, `PreferVectorSearch`) | idem | idem |
| `Orkeon:Redis` | Provider mémoire Redis : `ConnectionString` (défaut `localhost:6379`), `KeyPrefix` (défaut `orkeon:memory:`). La connexion s'ouvre au premier usage | `MemoryProviderFactory` — tout chemin qui sélectionne `redis` : `Memory:Provider`, le `memoryProvider:` d'une crew, `Orkeon:Rag:Provider`, `AddOrkeonRedisMemory` | liée par `AddOrkeonInfrastructure()` |
| `Orkeon:Sqlite` | Provider mémoire SQLite : `ConnectionString` (défaut `Data Source=:memory:` ; une `Data Source` fichier est un chemin virtuel sur un montage inscriptible), `TableName`, `DefaultTopK`, `MinSimilarityScore` | `MemoryProviderFactory` — tout chemin qui sélectionne `sqlite` | idem |
| `Orkeon:ChromaDb` | Serveur ChromaDB : `BaseUrl`, `Tenant`, `Database`, `CollectionName`, `DefaultTopK` | `MemoryProviderFactory` — tout chemin qui sélectionne `chromadb` | idem ; `AddOrkeonChromaDb(configuration)` (appelée par `AddOrkeonInfrastructure(configuration)` quand la section existe) expose aussi le `ChromaDbMemoryProvider` partagé par sa classe |
| `Orkeon:Pinecone` | Index Pinecone : `ApiKey`, `IndexName`, `Host` (facultatif : sans lui, un appel `describe_index` résout l'hôte de l'index au premier usage), `Namespace` | `MemoryProviderFactory` — tout chemin qui sélectionne `pinecone` | idem, `AddOrkeonPinecone(configuration)` |
| `Orkeon:LanceDb` | Serveur LanceDB distant : `Endpoint`, `ApiKey`, `Database`, `TableName`, `EmbeddingDimension`, `DistanceType`, `DefaultTopK`, `MinSimilarityScore`, `VectorWeight`, `FullTextWeight`, `CreateFullTextIndexOnInit` | `MemoryProviderFactory` — tout chemin qui sélectionne `lancedb` | liée par `AddOrkeonInfrastructure()` ; `AddOrkeonLanceDb(configuration)` ajoute la classe concrète et `LanceDbMigrationService` |
| `Orkeon:CrewMemory` | Ce que rappelle une crew `memory: true` avant chaque tâche (`CrewMemoryOptions`) : `RecallLimit` (5 souvenirs ; 0 ne rappelle rien), `MinScore` (0,6, cosinus à l'échelle de l'embedder — mesuré sur le modèle local), `MaxChars` (4 000 caractères de contenu au total) — voir [Système de mémoire](../architecture/memory-system.md#ce-que-rappelle-une-crew) | `MemoryCoordinator` | lié par `AddOrkeonInfrastructure()` |
| `Orkeon:CognitiveMemory` | Couche de mémoire cognitive (`EnableLlmAnalysis`, `EnableContradictionDetection`, `ContradictionCandidateCount`, `AnalysisModel`, `AnalysisTemperature`, `PruningThreshold`, `PruningMinAgeDays`, `RecencyHalfLifeHours`, `DefaultRecallOptions`) | Infrastructure | `AddOrkeonCognitiveMemory(configuration)` |
| `Orkeon:Encryption` | Chiffrement de la mémoire au repos (`Enabled`, `SecretName`, `KeySizeInBits`) | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement ; le décorateur est posé par l'hôte). La rotation de clés reste opt-in : `AddOrkeonKeyRotation()` |

### RAG (`Orkeon:Rag`)

Détails et sémantique : [Pipeline RAG](../architecture/rag-pipeline.md). Tout ce qui suit
requiert `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`), qu'appelle tout hôte
runner (`orkeon run`, `orkeon-host`).

| Section | Configure |
|---|---|
| `Orkeon:Rag:Profile` | Preset de profil `fast` (défaut) / `balanced` / `quality` / `adaptive` / `corrective` ; toute clé `Orkeon:Rag` surcharge le preset clé par clé |
| `Orkeon:Rag:LlmProfile` | Le profil LLM de l'hôte (`Llm:Profiles:<nom>`) qu'appelle le sous-système RAG — génération, transformateurs de requête, reranker listwise, évaluateur et vérificateur d'ancrage correctifs, classifieur `llm`, juge d'évaluation ; absent ou `default` : le profil par défaut. Un nom inconnu fait refuser le démarrage de l'hôte en listant les profils connus ; `orkeon rag eval --offline` l'ignore. Se choisit dans Studio › Réglages › Modèle d'IA (expert) |
| `Orkeon:Rag:Provider` | TYPE du provider du document store RAG (`RagStoreOptions` — un alias de type de `MemoryProviderFactory`), connecté depuis la section propre de ce provider (`Orkeon:Redis`, `Orkeon:Sqlite`, …) ; défaut : l'`IMemoryProvider` ambiant |
| `Orkeon:Rag:Collection` | Collection qu'interroge `rag_search` quand l'agent n'en nomme aucune (non définie : `default`) ; `rag_eval` l'utilise pour un dataset qui ne nomme pas de collection et n'apporte pas de corpus |
| `Orkeon:Rag:Retrieval` (`TopK`, `CandidateK`, `MinScore`) | Bornes de l'étape de retrieval |
| `Orkeon:Rag:Retrieval:Hybrid` (`Enabled`, `RrfK`), `Orkeon:Rag:Retrieval:Mmr` (`Enabled`, `Lambda`) | Récupération hybride BM25+RRF, MMR opt-in |
| `Orkeon:Rag:Rerank` (`Enabled`, `Kind`, `TopN`) | Choix et profondeur du reranker |
| `Orkeon:Rag:Context` (`MaxTokens`, `Ordering`) | Assemblage du contexte (ordre `edges` anti-Lost-in-the-Middle) |
| `Orkeon:Rag:Groundedness` (`Enabled`) | Hook de groundedness |
| `Orkeon:Rag:Generation` (`SystemPrompt`, `Temperature`, `MaxOutputTokens`) | Étape de génération citée |
| `Orkeon:Rag:Ingestion` (`DefaultChunkingStrategy` défaut `recursive`, `ManifestDirectory` défaut `/output/rag/manifests`) | Pipeline d'ingestion (`RagIngestionOptions`) |
| `Orkeon:Rag:QueryTransform` (`Mode`, `VariantCount`), `Orkeon:Rag:QueryRouting` (`Classifier`) | Transformateurs de requête (`multi-query`/`rag-fusion`/`hyde`), routage Adaptive-RAG (`heuristic` ou `llm`) |
| `Orkeon:Rag:Corrective` (`MaxIterations`) | Bornes du graphe correctif CRAG |
| `Orkeon:Rag:Corrective:WebFallback` (`Enabled`, `MaxResults`) + `Orkeon:Rag:WebFallback` (`Enabled`, `Endpoint`, `ApiKeyEnvVar`, `MaxResults`, `Timeout`, `SuspiciousAction`) | Repli web — double opt-in, les deux `Enabled` désactivés par défaut (politique + transport SearxNG) |

### Sécurité, sandbox, outils

| Section | Configure | Consommateur | Opt-in |
|---|---|---|---|
| `Orkeon:Security:PermissionGate` | Barrière par appel d'outil (`Enabled` défaut `false`, `Interactive`) | `ModePermissionGate` | `AddOrkeonPermissionGate(configuration)` — appelé par `RunnerHost` ; sans effet tant que `Enabled = true` n'est pas posé |
| `Orkeon:Dlp` | Politiques DLP / détection PII (`Enabled`, `DefaultAction`, `ChannelPolicies`) | Infrastructure | `AddOrkeonDlp()` |
| `Orkeon:Guardian` | Pipeline de gardes : `Enabled` (défaut `true`), `DefaultPolicy` (`InputGuardEnabled`, `ToolGuardEnabled`, `DelegationGuardEnabled`, `MaxDelegationDepth` — 5) | `GuardianPipeline` — la phase d'entrée de chaque tour d'agent, les phases outil et délégation de chaque appel d'outil | — (actif par défaut, enregistré par `AddOrkeonInfrastructure()`) |
| `Orkeon:CodeSandbox` (+ `:Docker`) | Sandbox de l'interpréteur de code sécurisé (`TimeoutSeconds`, `MaxMemoryBytes`, `MaxOutputBytes`, `AllowHostExecution`, `DefaultPermissions`, `SecurityOptions` ; Docker : `ImageName`, `PullImageOnStartup`) | Infrastructure | — (enregistré par `AddOrkeonInfrastructure()` ; la section gouverne le comportement) |
| `Orkeon:Sandbox` | Montage sandbox du système de fichiers (`/sandbox`, Internal) : `EphemeralRoot`, `CleanupOrphansOlderThan` (24 h), `VirtualPath` | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`Mounts`) | Montages VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)). Une entrée peut porter un **identifiant** — `<ulid>|<physique>:<virtuel>:<droits>` (VFS-90) : ce par quoi le bloc `mounts:` d'une crew, le sidecar d'équipe de Studio et `--mount-id` la désignent ; Studio en écrit un à chaque enregistrement. Un `--mount` CLI sur la même racine virtuelle **remplace toutes les entrées de cette racine** pour ce run ; sur une racine neuve il est ajouté (jamais fusionné, jamais perdu). Une racine déclarée deux fois n'est légitime que si chacune de ses entrées porte un identifiant — `--mount-id`, ou le `mounts:` de la crew, en sélectionne alors une et les autres sont retirées pour le run ; si rien n'en sélectionne une, le run est refusé avant tout host (`'/output' is declared twice in <settings> (<idA>: <dossierA>, <idB>: <dossierB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`), de même qu'une racine déclarée deux fois avec une entrée sans identifiant (`… and '<entrée>' has no id. Give every entry an id …`) ou un identifiant porté par deux entrées. Le chemin de base de chaque entrée déclarée est **mis en liste blanche pour `PathValidator`** sans `--allow-external-mounts` — un dossier déclaré est l'intention du propriétaire de la machine, il reste donc accessible même hors du répertoire de travail du processus ; une entrée retirée ne l'est pas | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`InternalMounts`) | Même grammaire que `Mounts`, enregistrés en `MountVisibility.Internal` : résolubles par le VFS, **absents de `list_mounts`, de la table de montages du prompt agent et des messages de refus d'accès**. C'est là qu'un hôte met ce que le VFS doit atteindre et qu'aucun agent n'a à adresser — le répertoire `--llm-log` y vit, et `/credentials`, les jetons OAuth des comptes e-mail, quand un tel compte est déclaré ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)) | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:Tools:Shell:AllowInterpreters` | Autorise interpréteurs/git mutant dans `ShellCommandTool` (**équivalent RCE**, avertissement émis) | `AddOrkeonCodeTools()` | config seule |
| `Orkeon:Tools:Shell:ExtraAllowedCommands` / `AllowedCommands` | Allowlist shell : additive / remplacement complet (le remplacement annule `AllowInterpreters`) | idem | config seule |

### E-mail (`Orkeon:Tools:Email`)

Lié par `AddOrkeonEmailTools(configuration)` — l'hôte partagé des runners et `orkeon-repl`
l'appellent — et validé paresseusement : un compte est validé la première fois qu'un outil ou une
commande `orkeon email` s'en sert, tous ses problèmes signalés d'un coup, et une valeur que le
binder ne sait même pas lire (un droit mal orthographié, un port en toutes lettres) met ce seul
compte de côté au lieu de faire échouer l'hôte — une section cassée ne casse jamais un crew qui
n'envoie pas de courrier. Les secrets n'y sont jamais des valeurs, seulement les **noms** des
variables d'environnement qui les contiennent. Parcours par fournisseur et table clé par clé :
[Outils e-mail](../guides/email.md).

| Section | Configure |
|---|---|
| `Orkeon:Tools:Email:DefaultAccount` | Le compte qu'utilise un appel qui n'en nomme aucun (facultatif avec un seul compte) |
| `Orkeon:Tools:Email:CredentialsDirectory` | Répertoire physique dont le sous-répertoire `email` contient les jetons OAuth ; un chemin relatif part du répertoire du fichier de réglages. Le runner le monte sur la racine interne `/credentials` quand un compte OAuth est déclaré ; par défaut : `credentials` à côté du fichier de réglages de l'utilisateur |
| `Orkeon:Tools:Email:Screening:WithholdRejected` | Retenir le corps d'un message que le filtre anti-injection de prompt rejette (défaut `false` : signaler seulement) |
| `Orkeon:Tools:Email:Accounts:<nom>` | Un compte. `Provider` (`Gmail`, `Outlook` ou `Custom` — le défaut), `Address`, `DisplayName`, `Rights` (**obligatoire** — `Read, Organize, Draft, Send, Delete, Purge`), `Incoming` (`Protocol` `Imap`, `Pop3` ou `Graph` ; `Host`, `Port`, `Security` `SslOnConnect`, `StartTls` ou `None` — ce dernier vers un hôte de bouclage seulement), `Outgoing` (`Protocol` `Smtp` ou `Graph` ; `Host`, `Port`, `Security`), `Auth` (`Method` `Password` ou `OAuth2` ; `Username`, `PasswordEnvVar`, `ClientId`, `ClientSecretEnvVar`, `Tenant`), `Send` (`AllowedRecipients` — une liste vide n'autorise personne —, `MaxRecipients`, `MaxPerHour`), `TimeoutSeconds`, `SaveSentCopy`. Le nom contient des lettres, des chiffres, `.`, `_` et `-`, commence par une lettre ou un chiffre (64 au plus) |

### Scripting et CLI

| Section | Configure | Consommateur |
|---|---|---|
| `Orkeon:Scripting:Limits` | Sandbox Jint : `MemoryLimitBytes` (défaut 100 Mo), `RecursionLimit` (64), `ExecutionTimeout` (30 s) | `Orkeon.Scripting` |
| `Orkeon:Scripting:Toolchain` | Résolution de la toolchain esbuild (`EsbuildPath`, premier de l'ordre de recherche ; `EsbuildTimeout` 30 s) | `EsbuildTranspiler.Create` — `orkeon run`, le runner partagé, `orkeon doctor`, `orkeon forge`, les commandes `*.cmd.ts` |
| `Orkeon:Cli:ScriptCommands` (+ `:Limits`) | Découverte des commandes CLI TypeScript (`Enabled`, `Directories`, `FailFastOnInvalidScript`, `EsbuildTranspile`, `MaxScripts` 50, `ContinueOnConflict`, `FallbackCommandName` `assistant`) + profil sandbox CLI plus strict | `Orkeon.Cli.Commands.Scripting` |
| `Orkeon:Cli:ScriptHost` | Résolution des crews `<nom>/crew.ork.ts` : `CrewDirectories`, `CrewFileName` (`crew.ork.ts`), `RunCrewTimeout` (10 min) | `ScriptHostFacade` |
| `Orkeon:Cli:ConsoleStreaming` | Deltas `ctx.llm.act` streamés sur la console REPL (`Enabled` défaut `false`) | `AddLlmConsoleStreaming(configuration)` |
| `Orkeon:Cli:Session:ContextWindowTokens` | Fenêtre de contexte utilisée par `token_budget` et le TUI | `TokenBudgetTool`, ConsoleApp |
| `Orkeon:Cli:Tui:SpinnerVerbs` | Verbes du spinner TUI | ConsoleApp |

### Hôte de service (`orkeon-host`)

| Section | Configure | Consommateur |
|---|---|---|
| `Orkeon:Host` | Le démon : `Crews` (chacune `Name` — unique, sans tenir compte de la casse — `Path`, `Profile:MaxConcurrentRuns` défaut 4 — runs de chat et A2A comptés ensemble —, `Mounts` — l'espace de montages propre à la crew —, `Description` — ce que la compétence A2A de la crew dit qu'elle fait), `RunTimeout` (30 min), `ShutdownGracePeriod` (20 s), `LlmProfiles` (la liste blanche des `Llm:Profiles` que les crews hébergées peuvent nommer — leurs agents, tâches et managers — et `Orkeon:Rag:LlmProfile` aussi ; absente, tous sont offerts, `["default"]` n'offre que le défaut ; une entrée qui nomme un profil non défini, ou un profil RAG que la liste écarte, refuse le démarrage) | `Orkeon.Host` — voir [Hôte de service](../architecture/service-host.md) |
| `Orkeon:Host:Discord` | Canal Discord : `Enabled`, `TokenEnvironmentVariable` (`ORKEON_DISCORD_TOKEN`), `AllowedUserIds`, `GuildIds`, `ProgressInterval` (2 s), `Routes` (identifiant de salon Discord → nom de crew : un fil ouvert dans ce salon démarre cette crew), `DefaultCrew` (la crew qu'atteint un salon sans route ; la première crew déclarée si absent). Une route vers une crew non déclarée, une clé de route qui n'est pas un identifiant de salon ou un `DefaultCrew` inconnu refusent le démarrage | idem |
| `Orkeon:Host:A2A` | Serveur A2A, éteint par défaut (GAP-23) : `Enabled`, `Host` (`http://localhost` ; `http://+` écoute sur toutes les interfaces), `Port` (5002), `Crews` (les crews que d'autres agents peuvent lancer, par nom — une compétence chacune, une tâche étant un run de cette crew ; aucune par défaut). Une section activée qui n'expose aucune crew ou une crew que `Crews` ne déclare pas, un `Host` ou un `Port` mal formé, ou une écoute au-delà de la boucle locale alors que `A2A:Security` ne déclare ni schéma d'authentification ni mTLS refuse le démarrage | idem — voir [Hôte de service](../architecture/service-host.md#5-autres-agents-a2a) |

### Multi-modal

| Section | Configure | Opt-in |
|---|---|---|
| `Orkeon:MultiModal` | Validation vision/contenu (`Enabled`, `MaxImageSizeBytes` 20 Mo, `SupportedImageFormats`, `SupportedAudioFormats`, `MaxAudioDurationSeconds`) — aucune image n'est redimensionnée | `AddOrkeonMultiModal(configuration)` |

---

> **Voir aussi** : [Sous-systèmes opt-in](./opt-in-subsystems.md) ·
> [Hébergement](./hosting.md) ·
> [Système de mémoire](../architecture/memory-system.md) ·
> [Pipeline RAG](../architecture/rag-pipeline.md) ·
> [Retour à l'index](../INDEX.md)
