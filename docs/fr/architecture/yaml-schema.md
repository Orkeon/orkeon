> 🇬🇧 [English version](../../architecture/yaml-schema.md)

# Référence YAML — Schéma complet

Ce document est la **source unique de vérité** pour le schéma YAML d'Orkeon. Les documents spécialisés (FSM, Graph, Autonomous) renvoient ici pour le schéma.

## Schéma de configuration complet

La structure YAML suit ce schéma. **Nommage des clés** : le loader (`YamlDotNetSerializer`)
reconnaît les clés en camelCase (la forme canonique, celle qu'écrit `YamlCrewExporter` — voir
[Export](#export)) et se rabat sur le snake_case pour la même propriété — `expectedOutput` et `expected_output`,
`llm_override` et `llmOverride` sont équivalents partout. Toute clé est lue ou refusée au
chargement : une clé qu'aucun modèle ne porte (ou une clé retirée) fait échouer le chargement avec
son fichier, sa ligne et son chemin, et la clé la plus proche (`Did you mean 'expectedOutput'?`) ;
`CrewYamlKeyCheck` s'exécute avant la désérialisation. Seuls les noms que vous choisissez restent libres :
les identifiants sous `agents:` et `tasks:`, les noms d'outils sous `toolRules:`, les noms de collections sous `rag.collections:`, les clés et
valeurs de `context:`, les noms de questions sous `decision.questions:` et `decision.fallback:`, les noms d'options sous le
`choice:` d'une question, et les clés de `when:`.
Une clé de fusion (`<<:`) est refusée, de même qu'une ancre sur une clé inconnue : partagez le texte
sous `anchors:`. Les mêmes modèles servent le format mono-fichier
et les formats multi-fichiers (`crew.yaml` + `agents.yaml` + `tasks.yaml`, ou `config.yaml` +
`agents/` + `tasks/`) décrits dans [YAML et Builders](../getting-started/yaml-and-builders.md) ;
un mapping `anchors:` de premier niveau (chaînes multi-lignes nommées) est développé avant le
parsing (`YamlAnchorPreprocessor`).

```yaml
# Schéma complet CrewYamlConfig
name: string              # Identifiant de la crew (requis) ; portée de sa mémoire long terme
goal: string              # Objectif (requis)
process: string           # "sequential" (défaut) | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous" — insensible à la casse ; une valeur inconnue fait échouer le chargement
verbose: bool             # default: false — chaque agent du run journalise ses tours (appels d'outils, résultats, réponses du modèle) en Information, catégorie Orkeon.Verbose
memory: bool              # default: false. true : la crew range le résultat de chaque tâche et rappelle les plus proches avant chaque tâche (embedder requis au kickoff) ; false : rien n'est rangé ni rappelé
memoryProvider: string    # Exige memory: true (refusé sinon). "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb" — insensible à la casse (alias "in-memory", "chroma", "lance") ; un type inconnu fait échouer le chargement, en listant les types connus. Le TYPE seul : la connexion vient de la section hôte (Orkeon:Redis, Orkeon:Sqlite, …). Absent : le magasin par défaut de l'hôte (Memory:Provider)
planning: bool            # default: false. true : avant la première tâche, un planificateur (le profil par défaut de l'hôte) écrit un plan pas à pas par tâche, que la tâche lit dans son prompt, dans chaque mode ; il ne change ni l'ordre ni les agents
maxRpm: int               # default: aucun — au plus N requêtes au modèle par minute pour toute la crew, tous ses agents et son manager confondus (vagues parallèles comprises) ; une de plus attend son tour. Le max_rpm de CrewAI (max_rpm: est accepté). 0 ou moins fait échouer le chargement
budget: {tokens, wallTimeSeconds, toolCalls, delegationDepth, spawnedAgents}  # process: autonomous seulement — voir Budget autonome plus bas ; default: aucun plafond de tokens ni de durée, 50 / 4 / 10
managerAgent: string      # La clé d'un des agents de la crew (tout autre nom fait échouer le chargement, en les listant). Hiérarchique : le manager — requis (omis, le chargement échoue) ; il assigne et revoit sur son propre bloc llm:, profil et modèle. Consensual : l'arbitre du repli ManagerDecision (inactif, avec un avertissement au début du run, sous un autre repli). Tout autre process : refusé au chargement
graphConfig: {…}          # Réglages du mode Graph, seul réglage de circuit breaker d'une crew (voir la section dédiée)

llm:                      # LLM par défaut de la crew, fusionné CHAMP PAR CHAMP sous le llm: propre de chaque agent (même forme que agents.<id>.llm)
  profile: string         # Profil LLM de l'hôte (Llm:Profiles:<nom>) pour tous les agents — inconnu → le chargement échoue en listant les profils de l'hôte
  model: string
  max_tokens: int         # Positif ; omis = le plafond hérité, 0 ou moins fait échouer le chargement en nommant la crew
  temperature: float

links:                    # ACL EventHub (optionnel) — à qui cette crew peut parler sur le hub
  - to: string            # Le name: de la crew cible, tel quel — ou "client:<nom>" pour un pair externe (p. ex. Studio)
    direction: string     # "outbound" (défaut) | "inbound" | "bidirectional" (alias "both") — une valeur illisible écarte l'entrée avec un warning
    allowed_topics: [string] # Omis ou vide = tous les topics

mounts:                   # Les racines virtuelles que la crew utilise (optionnel, VFS-90) — sélectionne et valide, ne restreint jamais
  - /output               # une racine qu'une entrée des settings (ou un --mount) doit fournir ; refusée en une ligne si rien ne le fait
  - 01J9Z3K4M5N6P7Q8R9S0T1V2W3|/data   # une racine épinglée à UNE entrée des settings par son identifiant, quand plusieurs la déclarent

rag:                      # Configuration RAG au niveau crew (optionnel)
  provider: string        # Supprimée : le chargement la refuse et renvoie à Orkeon:Rag:Provider — le magasin est celui de l'hôte
  collections:
    <nom_collection>:
      sources: [string]   # Fichiers, globs, répertoires ou URL — relatifs au dossier de la crew sauf s'ils sont absolus — ingérés à la création de la crew
      chunking:           # Omis → les défauts du pipeline d'ingestion
        strategy: string  # default: "recursive"
        max_tokens: int   # default: 512 — tokens par chunk (×4 caractères)
        overlap: int      # default: 64 — recouvrement de tokens entre chunks
  defaults:
    profile: string       # Le profil de récupération de chaque attachement de connaissance qui n'en nomme pas (voir Configuration Knowledge & RAG)

agents:
  <agent_id>:             # Clé = identifiant unique de l'agent
    role: string          # Rôle de l'agent (par défaut : la clé de l'agent)
    goal: string          # Objectif personnel de l'agent (requis)
    backstory: string     # Contexte et expertise (multi-ligne recommandé)
    tools: [string]       # Noms d'outils enregistrés dans IToolRegistry (un nom inconnu fait échouer le chargement sous Orkeon:CrewFactory:StrictTools, le défaut des runners)
    allowDelegation: bool # default: true — permet la délégation à d'autres agents
    maxIter: int          # default: 20 — les tours que l'agent peut prendre sur une tâche (en C# et en .ork.ts aussi). 0 ou moins fait échouer le chargement
    maxRpm: int           # default: aucun — au plus N requêtes au modèle par minute pour cet agent ; une de plus attend son tour. Le RateLimiting:AgentRequestsPerMinute de l'hôte le borne aussi, le plus strict l'emporte. 0 ou moins fait échouer le chargement
    maxExecutionTime: int # default: aucun — les secondes qu'une tâche peut prendre chez cet agent (alias max_execution_time) ; 0 ou moins, ou plus de 4294967 (environ 49,7 jours), fait échouer le chargement, tout comme une limite sur le manager hiérarchique
    working_root: string  # default: aucun — un chemin virtuel (alias workingRoot, C# AgentBuilder.WorkingRoot) sous un mount qui confine les outils fichier et shell_command de cet agent ; refusé au démarrage du run, en nommant la racine, si aucun mount ne la contient
    verbose: bool         # default: false — logs détaillés pour cet agent (appels d'outils, résultats, réponses du modèle, en Information, catégorie Orkeon.Verbose)
    llm:                  # Omis (et pas de llm: de crew) → la section Llm des settings du runner
      profile: string     # Profil LLM de l'hôte (Llm:Profiles:<nom>) : le fournisseur de cet agent ; "default" = la section Llm
      model: string       # Identifiant du modèle LLM — omis → le modèle propre du profil
      temperature: float  # Envoyée quelle que soit sa valeur — omise = celle du profil, sinon rien n'est envoyé et le modèle applique la sienne (GAP-36)
      maxTokens: int      # Plafond de tokens en sortie, positif — omis = le maximum documenté du modèle (LLM-10) ; 0 ou moins fait échouer le chargement
      topP: float         # Nucleus sampling, envoyé quelle que soit sa valeur — omis = celui du profil, sinon rien n'est envoyé (Mistral écrit 1)
      thinking:           # Contrôle du raisonnement (gaté par capacité selon le provider)
        enabled: bool
        effort: string    # "low" | "medium" | "high" | "xhigh" | "max" (un provider envoie le niveau le plus proche que son champ connaît)
        budget_tokens: int # Honoré par Qwen seul ; les autres providers émettent un warning
      responseFormat: string # "text" (= défaut du provider) | "json_object" | "json_schema" — toute autre valeur est transmise telle quelle avec un warning
      responseSchema:     # Avec responseFormat: json_schema (un schéma seul implique json_schema)
        name: string      # default: "response"
        schema: string    # Le JSON Schema sous forme de chaîne JSON, p. ex. '{"type":"object"}'
        strict: bool      # default: true
      cache:              # Prompt caching explicite (Anthropic) ; désactivé tant qu'aucun breakpoint n'est demandé
        system: bool      # default: false
        tools: bool       # default: false
        ttl: string       # p. ex. "1h" ; omis = défaut du fournisseur
    guardrails:           # Règles opérationnelles injectées dans le system prompt de l'agent (optionnel)
      preset: string      # "analysis" | "strict" | "creative" — tout autre nom fait échouer le chargement
      header: string      # En-tête de section — utilisé seulement sans preset (chaque preset apporte le sien)
      rules: [string]     # Règles globales numérotées
      toolRules:          # Règles rendues seulement quand l'agent possède l'outil ; un nom d'outil ignore la casse, et un outil écrit sous deux clés fait échouer le chargement
        <tool_name>: [string]
    knowledge:            # Collections de connaissance (RAG) attachées à l'agent (optionnel) ; une clé inconnue d'une entrée est refusée, une valeur mal formée reste un warning
      - string            # Forme courte : nom de collection avec options par défaut
      - collection: string       # Forme longue (clé requise)
        top_k: int               # default: 5 — chunks retenus par requête
        min_score: float         # Score de pertinence minimal dans [0, 1]
        profile: string          # Le profil de récupération de cet attachement — sinon rag.defaults.profile, sinon Orkeon:Rag:Profile (voir Configuration Knowledge & RAG)
        max_context_tokens: int  # default: 2000 — plafond de tokens de contexte injectés

tasks:
  <task_id>:              # Clé = identifiant unique de la tâche
    description: string   # Description détaillée de la tâche (requis)
    expectedOutput: string # Format/contenu attendu en résultat (requis)
    agent: string         # Clé de l'agent assigné à la tâche (une clé qui ne désigne aucun agent fait échouer le chargement, en listant les agents de la crew)
    dependencies: [string] # Clés des tâches prérequises (garantit l'ordre ; une clé inconnue fait échouer le chargement, en listant les tâches de la crew ; un cycle aussi)
    asyncExecution: bool  # default: false — sequential : tourne pendant les tâches suivantes, une dépendante l'attend ; parallel : sans effet propre ; autres modes : true fait échouer le chargement
    humanInput: bool      # default: false — demande intervention humaine
    context: {key: value} # Données additionnelles de contexte
    tools: [string]       # Outils AJOUTÉS à ceux de l'agent pour cette tâche seulement (sans jamais les remplacer) — résolus comme ceux d'un agent : un nom inconnu fait échouer le chargement sous StrictTools
    deliverable:          # Contrat de fichier de sortie (ignoré sans path)
      path: string        # Chemin virtuel, p. ex. "/output/report.md"
      source: string      # "tool_call" (défaut — l'agent reçoit la consigne d'écrire le chemin avec file_write) | "final_message" (le framework écrit la réponse finale) | "structured_output" (JSON écrit par le framework, contraint par le schéma en json_schema là où le fournisseur le déclare, en grammaire GBNF là où Llm:Grammar est allumé, et toujours vérifié au parsing) | "none" — toute autre valeur fait échouer le chargement
      format: string      # "markdown" (défaut) | "json" | "text"
      sanitize: bool      # default: true — retire les tokens de template en fin de texte (final_message)
      schema_path: string # Chemin virtuel d'un fichier JSON Schema (structured_output exige lui ou schema_inline)
      schema_inline: string # Le JSON Schema sous forme de chaîne JSON — alternative à schema_path
    llm_override:         # Surcharge LLM au niveau tâche (cascade crew → agent → tâche)
      profile: string     # Cette tâche tourne sur un autre profil LLM de l'hôte que celui de son agent
      response_format: string  # Mêmes valeurs que llm.responseFormat
      response_schema: {name, schema, strict} # schema est une chaîne JSON
      temperature: float
      max_tokens: int     # Positif ; omis = le plafond hérité, 0 ou moins fait échouer le chargement en nommant la tâche
      top_p: float
      thinking: {enabled, effort, budget_tokens}
      cache: {system, tools, ttl}  # Même forme que le cache: de l'agent — remplace le bloc de l'agent en entier ; un bloc sans point d'arrêt coupe le cache de l'agent pour cette tâche
    guardrails:           # Guardrails au niveau tâche, même forme que le bloc agent (optionnel)
      preset: string      # "analysis" | "strict" | "creative" — tout autre nom fait échouer le chargement
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
    decision:             # Fait de la tâche une tâche de décision : pas d'agent, répondue par le modèle de décision — voir Tâches de décision et conditions plus bas. Avec agent : refusé au chargement
      questions:          # Obligatoire, au moins une ; la clé est le nom de la question
        <question>:
          choice: {<option>: string}  # Une option parmi 255 au plus, chacune avec sa description
          score: [string]             # Une échelle ordonnée de 10 niveaux au plus
          noul: string                # Une question oui/non : l'énoncé
          instructions: string        # Optionnel, pour choice et score
      minConfidence: float   # défaut : 0.5, dans [0, 1] — en dessous, une condition d'option ne sélectionne rien
      state: [string]        # Clés des tâches dont le modèle juge la sortie ; défaut : les sorties des dépendances qui ont tourné
      longContext: bool      # défaut : false — demande le contexte long du modèle
      fallback: {<question>: string}  # Réponses utilisées sans requête quand le circuit de décision est coupé
    when:                 # Ne lance la tâche que si une tâche de décision a répondu comme écrit (une seule tâche de décision par when:)
      <task>.<question>: [string] | ">= 0.7"  # Une liste d'options (choice), ou un seuil : une probabilité (oui/non) ou une position de niveau (score)
      <task>.<question>.<option>: ">= 0.25"   # La probabilité d'une option d'un choice
      <task>: otherwise                       # Seule : vaut quand aucune condition sœur sur cette tâche ne vaut
```

La validation a lieu
au chargement (`CrewDefinitionValidator`) : une crew exige un `name`, un `goal`, au moins un agent
(chacun avec un goal) et au moins une tâche (chacune avec une `description` et un
`expectedOutput`) ; des `dependencies` circulaires, un item de `mounts:` listé deux fois ou deux
identifiants qui sélectionnent la même racine font échouer le chargement. Une clé
`circuitBreaker:` aussi, à la racine comme sur une tâche : le bloc a été supprimé (voir « Supprimé :
`circuitBreaker` » plus bas), et le chargement nomme `graphConfig` au lieu de l'ignorer. Le
`asyncExecution: true` d'une tâche aussi, hors `process: sequential` et `process: parallel` : les quatre
autres modes ordonnent leurs tâches eux-mêmes, et le chargement nomme chaque tâche qui le demande au lieu
d'ignorer le drapeau (voir [Tâches asynchrones](../orchestration/process-types.md#tâches-asynchrones-asyncexecution)).
Un `maxRpm` de 0 ou moins — sur la crew ou sur un agent — et un `maxIter` de 0 ou moins font échouer le
chargement aussi, en nommant la crew ou l'agent : omettre `maxRpm:`, c'est n'avoir aucune limite, omettre
`maxIter:`, c'est le défaut (20). Ils étaient remplacés, par 10 et 15, sans un mot. Un `max_tokens` de 0 ou moins — sur le `llm` de la crew, le `llm` d'un agent ou le
`llm_override` d'une tâche — fait échouer le chargement aussi, en nommant la crew, l'agent ou la tâche : omettre
la clé, c'est garder le plafond hérité. Les plafonds propres
de l'hôte sur les appels au modèle — qui bornent le `maxRpm` d'un agent, le plus strict l'emportant — sont
les réglages de [`RateLimiting`](../reference/configuration.md#ratelimiting).
Le `maxExecutionTime` (ou `max_execution_time`) d'un agent est un nombre entier de secondes : 0 ou moins, un décimal,
une valeur au-dessus du plafond (4294967 s, environ 49,7 jours) et une limite sur le `managerAgent` d'une crew
hiérarchique font échouer le chargement, en nommant l'agent. Une tâche qui dépasse sa limite se termine en
`ExecutionTimeExceeded` au plus tard à la fin de l'appel en cours ; le temps passé à attendre un humain ne compte pas.

Un message nomme un agent ou une tâche par sa clé — `Agent 'researcher' must have a goal.` —, une
configuration bâtie en code par son identifiant.

## Tâches de décision et conditions

Une tâche qui porte un bloc `decision:` est une **tâche de décision** : elle n'a pas d'agent, ne fait
aucun appel LLM et n'écrit aucun texte. À l'exécution, elle envoie son état et toutes ses questions au
modèle de décision en une seule requête (voir
[Modèles de décision](./decision-models.md#tâches-de-décision-et-fan-out-conditionnel)), et sa sortie
est l'ensemble des réponses. Une tâche qui porte un bloc `when:` ne tourne que si une tâche de décision
a répondu comme écrit, et est sautée sinon.

```yaml
agents:
  reader: { role: Reader, goal: Read tickets }
  engineer: { role: Engineer, goal: Fix defects }
  finance: { role: Finance, goal: Handle refunds }
tasks:
  ticket:
    description: Read the support ticket
    expectedOutput: A summary of the ticket
    agent: reader
  triage:
    description: Sort the ticket
    expectedOutput: The category and whether a refund is asked
    dependencies: [ticket]
    decision:
      questions:
        category:
          choice:
            bug: a defect in the product
            feature: a request for a new capability
            billing: a question about an invoice
        refund_requested:
          noul: the customer asks for a refund
      minConfidence: 0.6
  fix_bug:
    description: Fix the defect or plan the feature
    expectedOutput: A fix or a plan
    agent: engineer
    when:
      triage.category: [bug, feature]
  refund:
    description: Handle the refund
    expectedOutput: A refund decision
    agent: finance
    when:
      triage.category: [billing]
      triage.refund_requested: ">= 0.7"
  manual_triage:
    description: Route the ticket to a person
    expectedOutput: A hand-over note
    agent: reader
    when:
      triage: otherwise
  summary:
    description: Summarise what was done
    expectedOutput: A short report
    agent: reader
    dependencies: [fix_bug, refund, manual_triage]
```

**Le bloc `decision:`.**

- `questions` est obligatoire et compte au moins une question. Une question déclare exactement un
  genre : `choice` (une table option vers description, 255 options au plus), `score` (une liste
  ordonnée de 10 niveaux au plus) ou `noul` (l'énoncé d'une question oui/non, le nom que Jev lui
  donne). `instructions` est optionnel sur un choice et un score. Les noms de questions sont uniques.
- `minConfidence` est le seuil de confiance, un nombre dans [0, 1] ; il vaut 0,5 par défaut.
  `min_confidence` se lit de la même façon, comme toute clé du fichier.
- `state` liste les clés des tâches dont le modèle juge la sortie ; sans lui, le modèle lit les sorties
  des dépendances de la tâche qui ont tourné, dans l'ordre. Une tâche listée est ajoutée aux
  `dependencies` de la tâche si elle n'y est pas.
- `longContext` (défaut `false`) demande le contexte long du modèle ; il n'est jamais demandé de
  lui-même.
- `fallback` associe une question à la réponse utilisée quand le circuit de décision est coupé : une
  option d'un choice, un niveau d'un score, `yes` ou `no` d'un oui/non (mettez `"no"` entre guillemets,
  que YAML lit comme un booléen). Une question sans réponse de repli ne sélectionne aucune option. Voir
  [le repli](./decision-models.md#le-circuit-de-décision-et-le-repli).
- Une tâche de décision exige toujours une `description` et un `expectedOutput` comme toute tâche, et
  la crew exige toujours un agent, même si aucune tâche ne l'utilise.

**Le bloc `when:`.** Un `when:` nomme une seule tâche de décision ; chaque clause s'écrit `<task>`,
`<task>.<question>` ou `<task>.<question>.<option>`, où `<task>` est la clé de la tâche de décision.
Plusieurs clauses s'écrivent dans un même bloc et **doivent toutes valoir**. La grammaire :

| Clause | Vaut quand |
|---|---|
| `<task>.<question>: [a, b]` | La question est un choice, l'option choisie est dans la liste et la confiance est au moins égale à `minConfidence`. |
| `<task>.<question>: ">= 0.7"` | La question est un oui/non (sa probabilité d'un oui, dans [0, 1]) ou un score (le score pondéré du modèle sur les positions des niveaux, de 0 à n-1) et la comparaison vaut. Le seuil de confiance ne s'applique pas. |
| `<task>.<question>.<option>: ">= 0.25"` | La probabilité de cette option d'un choice satisfait la comparaison, même si l'option n'est pas la choisie. Le seuil de confiance ne s'applique pas. |
| `<task>: otherwise` | Aucune condition sœur sur cette tâche de décision ne vaut, y compris quand la confiance est sous le seuil. |

Un seuil est un opérateur (`>=`, `>`, `<=`, `<`) et un nombre, écrit comme une chaîne entre
guillemets : une probabilité dans [0, 1] sur un oui/non ou une option, une position de niveau de 0 au
nombre de niveaux moins un sur un score (`"> 1.5"` sur `[low, medium, high]` dépasse le niveau du
milieu). Plusieurs tâches dont les conditions valent pour une même décision tournent toutes (un
fan-out) ; une tâche qui dépend de plusieurs branches tourne quand au moins une d'elles a tourné. La
tâche de décision nommée par un `when:` est ajoutée aux `dependencies` de la tâche si elle n'y est pas.
Une tâche dont la condition ne vaut pas, ou dont toutes les dépendances ont été sautées par une
condition, est sautée : elle ne laisse aucune sortie, et la crew réussit quand même.

**Où cela tourne.** Les tâches de décision et `when:` sont pris en charge avec `process: sequential`,
`parallel` et `graph`. Les autres processus les refusent au chargement (`Task 'triage' declares a
decision or a condition, which the Hierarchical process does not support.`) ; un équipage construit en
C# qui les atteint échoue avant sa première tâche avec le même message.

**Ce qui est refusé au chargement.** Une tâche est nommée par sa clé déclarée, la clé de la table YAML,
dans tous les messages, sous la forme `Task '<clé>' <partie> …` où la partie est `decision`,
`decision.questions.<nom>`, `decision.fallback` ou `when`. Le chargement échoue sur :

- une tâche qui a à la fois un `agent:` et un `decision:`, une décision sans question, un choice de plus
  de 255 options, un score de plus de 10 niveaux, un `minConfidence` hors de [0, 1] ou qui n'est pas un
  nombre, un `longContext` qui n'est pas un booléen ;
- une question qui déclare plus d'un parmi `choice`, `score` et `noul` ;
- un repli qui nomme une question inconnue, ou une réponse que la question n'a pas ;
- un `when:` sur une tâche qui n'est pas une tâche de décision, ou qui nomme une question ou une option
  inconnue, ou avec un seuil hors de sa plage (une probabilité dans [0, 1], une position de score de 0 à
  n-1) ou qui n'est pas écrit comme un opérateur et un nombre fini ;
- `otherwise` écrit à côté d'autres clauses, une seconde tâche `otherwise` sur une même tâche de
  décision, un `otherwise` sans condition sœur sur cette tâche de décision ;
- une clause d'option avec une liste (`triage.category.bug: [bug]`), qui n'accepte qu'une chaîne de
  seuil ;
- une décision ou un `when:` dans un processus autre que les trois ci-dessus.

Avec le circuit de décision coupé, l'exécution est aussi refusée avant sa première tâche quand une
tâche de décision n'a ni `fallback` ni tâche `otherwise`.

## Configuration Guardrails

Les guardrails sont des règles opérationnelles rendues dans le **system prompt** de l'agent exécutant.
Ils peuvent être déclarés sur un **agent** (s'appliquent à toutes les tâches que l'agent exécute) et/ou
sur une **tâche** (s'appliquent uniquement à cette tâche). Quand les deux sont présents, **les deux
s'appliquent — les guardrails de l'agent sont rendus d'abord, puis ceux de la tâche** en section
séparée. `preset` (`analysis` / `strict` / `creative`) fournit un socle de règles de base ; les
`rules`/`toolRules` explicites sont fusionnées par-dessus, et les `toolRules` d'un outil donné ne sont
émises que si l'agent exécutant possède effectivement cet outil pour la tâche — ses propres outils
plus le `tools:` de la tâche. Un en-tête de `preset` prime sur un
`header` personnalisé. Un `preset` autre que ces trois — une faute de frappe — fait échouer le
chargement, en nommant l'agent ou la tâche par sa clé et les trois presets ; de même un outil écrit
sous deux clés de `toolRules` (`file_write` et `File_Write`) : un nom d'outil ignore la casse.

## Configuration Knowledge & RAG

Deux blocs complémentaires (RAG-03/C4). Le bloc crew `rag:` déclare les **collections**
(sources d'ingestion, chunking) et les défauts globaux. Le bloc agent `knowledge:` **attache**
des collections à un agent avec ses options de récupération. À l'assemblage du contexte
d'exécution, chaque collection attachée est interrogée avec l'entrée de la tâche par la moitié
retrieval du pipeline de son **profil** — le `profile` de l'attachement, sinon
`rag.defaults.profile`, sinon `Orkeon:Rag:Profile` — et les résultats sont injectés dans le
prompt utilisateur de l'agent sous forme d'extraits numérotés et cités
(`IKnowledgeContextAugmenter`, qui honore `top_k`, `min_score` et `max_context_tokens`). Les deux
moitiés exigent le sous-système RAG (`AddOrkeonRag(configuration)`), que tout hôte runner
enregistre (`orkeon run` sous toutes ses formes, `orkeon-host`) : à la **création** de la crew
(`CrewFactory`), les collections déclarées sont ingérées de façon incrémentale
(`IRagCollectionsBootstrapper` — une source inchangée n'est pas ré-embeddée ;
`CrewFactoryOptions.PrepareRagCollections = false` désactive l'étape, comme le fait
`--validate`). Sur un hôte sans le sous-système, un bloc `rag:` déclaré et les `knowledge:` de
chaque agent journalisent un warning et n'injectent rien. Le parsing du YAML lui-même ne
déclenche jamais d'ingestion. Le document store relève de l'hôte (`Orkeon:Rag:Provider`, voir
[Pipeline RAG](./rag-pipeline.md)) ; l'ancienne clé `rag.provider` a disparu et est refusée au
chargement, avec la phrase qui renvoie à `Orkeon:Rag:Provider`.

Ce que veut dire une entrée de `sources` (GAP-27) : un chemin qui ne commence pas par `/` est
relatif au dossier de la crew — `/crew` sous `orkeon run crew.yaml` ou un répertoire de crew,
donc `./data/faq.md` à côté du fichier de crew est `/crew/data/faq.md` — et un chemin absolu est
un chemin virtuel (`/kb/faq.md`, un de vos montages). Un glob (`*`, `**`, `?`) est développé à
travers le VFS comme le développent `rag_ingest`, `orkeon rag ingest` et `rag.ingest` ; un
répertoire vaut tous les fichiers qu'il contient ; une adresse `http(s)://` et un fichier simple
parviennent tels quels aux chargeurs. Un glob ou un répertoire qui ne donne aucun fichier, une
source relative d'une crew lue depuis une chaîne (pas de dossier auquel être relative) et un
glob hors de tout montage sont chacun un warning de chargement qui nomme la collection — la crew
se charge quand même ; un fichier absent est signalé par l'ingestion. Un fichier que deux
entrées nomment n'est ingéré qu'une fois.

Forme courte — attacher des collections avec les options par défaut :

```yaml
rag:
  collections:
    produits:
      sources: ["./data/catalogue/**/*.pdf", "./data/faq.md"]

agents:
  support:
    role: "Agent support client"
    goal: "Répondre aux questions produits"
    knowledge: [produits, procedures]
```

Forme longue — options de récupération par collection (mixable avec la forme courte dans la
même liste) :

```yaml
rag:
  collections:
    procedures:
      sources: ["./docs/procedures/"]
      chunking: { strategy: recursive, max_tokens: 512, overlap: 64 }
  defaults: { profile: balanced }

agents:
  expert:
    role: "Expert métier"
    goal: "Fournir des réponses sourcées"
    knowledge:
      - collection: procedures
        profile: quality
        top_k: 8
        min_score: 0.35
        max_context_tokens: 1500
```

Les clés acceptent snake_case (la forme utilisée ci-dessus) et camelCase. Une entrée `knowledge` malformée
(`collection` manquante, `top_k` non numérique, …) est ignorée ou dégradée avec un warning —
elle ne fait jamais planter le loader. L'équivalent fluent est
`AgentBuilder.WithKnowledge("produits")` /
`WithKnowledge("produits", opts => { opts.TopK = 8; opts.Profile = "quality"; })` (cumulable).

## Configuration Graph

Quand `process: "graph"` est utilisé, un bloc `graphConfig` supplémentaire configure le moteur de graphe d'état :

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" (défaut) | "permissive" | "default"
  maxTransitions: int           # Défaut : calculé, 2 × visites + 1
  maxStateVisits: int           # Plafond de tentatives — défaut : calculé, tâches × (1 + maxRetryCycles)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)
```

`graphConfig` est le seul réglage de circuit breaker d'une crew (`CircuitBreakerPolicyFactory.ResolveGraph`).
Le moteur de graphe n'applique que trois limites — transitions, visites d'un même état, durée
totale (`GraphRunner`) ; un timeout par état ou un mode dégradé n'a aucun effet sur un run de graphe.

## Supprimé : `circuitBreaker`

Le bloc `circuitBreaker:` — à la racine de la crew comme sur une tâche — n'existe plus, et une crew
qui en écrit encore un est **refusée au chargement** avec un message qui nomme `graphConfig`. Le
bloc de tâche configurait une machine d'états de tâche qu'aucun chemin d'exécution ne lançait, et
trois limites de garde (`maxRetries`, `maxToolCallsPerRound`, `maxValidationRetries`) que rien ne
lisait. Le bloc de crew n'était lu que par le mode Graph, où `graphConfig` porte le même preset et
les trois mêmes limites effectives.

Ce qui borne une tâche, c'est sa boucle d'agent : le `maxIter` de l'agent, l'arrêt après trois
erreurs d'outil identiques consécutives, et les reprises de validation de sortie. Ce qui borne un
run de graphe, c'est `graphConfig` (ci-dessus). Le moteur générique de machine d'états et
`CircuitBreakerPolicy` restent — le mode Graph, `orkeon forge` et le graphe RAG correctif s'en
servent (voir [FSM](../orchestration/fsm.md)).

## Supprimé : `outputFile`

La clé `outputFile:` d'une tâche n'existe plus, et une crew qui en écrit encore une est **refusée au
chargement** par un message qui indique le remplacement : le bloc `deliverable:` de la tâche (plus haut),
par exemple `deliverable: { path: /output/report.md }`. Avec la `source: tool_call` par défaut, l'agent
reçoit la consigne d'écrire ce chemin ; avec `source: final_message`, le framework y écrit la réponse finale.

## Budget autonome

`budget:` borne le run d'une crew `process: autonomous` (#218) :

```yaml
process: autonomous
budget:
  tokens: 400000          # somme courante prompt + complétion de chaque appel ; aucune par défaut
  wallTimeSeconds: 1800   # aucune par défaut
  toolCalls: 80           # tâches distribuées ; 50 par défaut
  delegationDepth: 2      # délégations du run ; 4 par défaut
  spawnedAgents: 3        # agents créés via spawn_agent ; 10 par défaut
```

Chaque clé est facultative, et une clé omise prend son défaut : une crew sans `budget:` n'a ni
plafond de tokens ni temps mural, si bien qu'une crew ordinaire n'est jamais écourtée par un chiffre
qu'elle n'a pas choisi. Une limite de 0 ou moins, ou un `budget:` sur une crew d'un autre process — le
seul qui le dépense est `autonomous` — fait échouer le chargement, en nommant la crew. L'export
réécrit le bloc, la durée en secondes entières. Le même budget est
`CrewBuilder.Budget(new CrewBudget { ... })` en C# et
`crewBuilder().budget({ tokens, wallTime, toolCalls, delegationDepth, spawnedAgents })` en `.ork.ts`
(voir le [guide de l'orchestration Autonomous](../orchestration/autonomous.md)).

## Export

`YamlCrewExporter` — qu'enregistre `AddOrkeonYaml()` — réécrit une `CrewConfiguration` en YAML :
`ExportToString`, `ExportToFileAsync`, ou `ExportToDirectoryAsync` pour le triplet plat `crew.yaml` +
`agents.yaml` + `tasks.yaml`. Rechargé, le fichier donne la même configuration — chaque clé que lit le
chargeur — et son propre export est le même texte, octet pour octet. Les clés sont écrites en
camelCase, et une clé que rien ne fixe n'est pas écrite du tout.

- **Sous les clés de l'auteur.** Une crew chargée depuis le YAML garde la clé de chaque agent et de
  chaque tâche (`researcher`, `collect`) : l'export écrit chacun sous elle, et chaque référence —
  `agent:`, `dependencies:`, `managerAgent:` — par elle. Une configuration bâtie en C# ou par un script
  `.ork.ts` n'a pas de clés : ses agents et ses tâches sont écrits sous leurs identifiants. Deux entrées
  sous un même nom, ou une référence qui ne nomme aucune entrée, font échouer l'export par une
  `InvalidOperationException` qui les nomme : le chargeur refuserait le fichier.
- **Ce que le chargeur a fait de votre fichier, pas votre fichier.** L'export écrit ce que porte la
  configuration : un `preset:` de guardrails revient sous la forme du `header`, des `rules` et des
  `toolRules` qu'il a apportés, le `llm:` de crew sous la forme du bloc que chaque agent en a reçu, sous
  chaque agent, et les `anchors:` sous la forme du texte qu'elles remplaçaient. Un attachement de
  connaissance qui ne nomme que sa collection est écrit en forme courte, tout autre en forme longue.
- **Une source relative suit le fichier.** Une source de `rag:` est écrite telle quelle : `./data/faq.md`
  se lit donc par rapport au dossier d'où le fichier exporté est chargé (voir
  [Configuration Knowledge & RAG](#configuration-knowledge--rag)) — exportez à côté des données, ou
  écrivez des sources absolues. Le dossier d'où la crew a été lue n'est pas une clé, et n'est pas écrit ;
  rien de ce pour quoi le schéma ci-dessus n'a pas de clé ne l'est non plus.

## Modèles YAML

Les modèles YAML incluent :
Tous vivent dans `Orkeon.Infrastructure.Configuration` (`src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`) ; `YamlCrewMapper` les transforme en `CrewConfiguration` du Domain et `CrewDefinitionValidator` la vérifie.

- `CrewYamlConfig` (définition complète d'une crew) et `CrewSettingsYamlConfig` (le fichier de réglages de crew des formats multi-fichiers — `crew.yaml` ou `config.yaml`)
- `AgentYamlConfig` (rôle, objectif, backstory, outils, limites)
- `TaskYamlConfig` (description, résultat attendu, dépendances, outils, livrable, surcharge LLM, guardrails)
- `LlmYamlConfig` (profil, modèle, température, max tokens, topP, thinking, responseFormat/responseSchema, cache) avec `ThinkingYamlConfig`, `ResponseSchemaYamlConfig`, `CacheYamlConfig`
- `LlmOverrideYamlConfig` (le bloc `llm_override:` de tâche)
- `DeliverableYamlConfig` (le bloc `deliverable:` de tâche)
- `GuardrailsYamlConfig` (les `guardrails:` d'agent et de tâche)
- `LinkYamlConfig` (l'ACL `links:` de crew)
- `CrewYamlConfig.Mounts` / `CrewSettingsYamlConfig.Mounts` (le bloc `mounts:` de crew — items `/racine` ou `<ulid>|/racine`) → `CrewConfiguration.Mounts` (`MountReference`, VFS-90)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, surcharges)
- `RagYamlConfig` (collections + sources/chunking, défauts — avec `RagCollectionYamlConfig`, `RagChunkingYamlConfig`, `RagDefaultsYamlConfig`) → `RagCrewConfig`
- `AgentYamlConfig.Knowledge` (entrées forme courte/longue) → `KnowledgeAttachment`

Il n'existe pas d'objet « configuration prédéfinie » au niveau du framework : un hôte compose ses réglages via `AddOrkeonInfrastructure` / `AddOrkeonApplication` et son `appsettings.json`.

---

> **Voir aussi** : [YAML et Builders](../getting-started/yaml-and-builders.md) · [Orchestration FSM](../orchestration/fsm.md) · [Orchestration Graph](../orchestration/graph.md) · [Orchestration Autonome](../orchestration/autonomous.md) · [Retour à l'index](../INDEX.md)
