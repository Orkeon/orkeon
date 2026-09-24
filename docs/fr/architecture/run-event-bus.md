> 🇬🇧 [English version](../../architecture/run-event-bus.md)

# Le bus d'événements du run

**Périmètre** : `orkeon run --events jsonl`, le protocole qu'il parle, et le siège qu'il donne à un processus observateur sur l'[EventHub](event-hub-and-crew-lifecycle.md) du run.
**Public** : quiconque pilote Orkeon depuis un autre programme — Studio, une passerelle, un job de CI, votre propre outillage.

Un run sans `--events` imprime du texte pour un humain. Avec, il parle un protocole versionné : un document JSON par ligne sur stdout, un par ligne sur stdin en retour. Rien d'autre ne change — même crew, même hôte, mêmes codes de sortie.

---

## 1. Pourquoi un protocole plutôt qu'une sortie analysée

Le texte destiné à une personne change dès que quelqu'un en améliore la formulation. Un programme qui le lit casse sur une virgule. Le protocole existe pour que les deux publics cessent de partager un canal : les humains gardent le rendu, les programmes obtiennent un contrat.

Le contrat, c'est **le flux sortant**. L'entrant est délibérément tolérant : une ligne malformée, un verbe inconnu, une charge utile inutilisable sont ignorés plutôt que fatals. Un client qui envoie n'importe quoi ne doit pas arrêter une crew qui fonctionnait.

---

## 2. L'enveloppe

Chaque ligne sortante est un objet portant ces clés, et elles seules comme noms réservés :

| Clé | Signification |
|---|---|
| `v` | Version du protocole. Actuellement `2`. |
| `seq` | Numéro de séquence monotone dans le run, à partir de 1. |
| `ts` | Horodatage ISO-8601 UTC. |
| `kind` | Ce qui s'est passé (voir §3). |
| `crewId`, `agentId` | Qui est concerné, quand le run le sait. |
| `correlationId` | Lie une question à sa réponse, une requête à sa réplique. |
| `causationId` | L'événement qui a causé celui-ci, pour qu'un client reconstruise l'arbre. |

**Une clé absente est omise, jamais écrite à `null`.** Un client traite tout champ d'identité comme optionnel.

**`v` ne bouge que pour un changement qu'un client existant lirait de travers** — un champ retiré, renommé ou qui change de sens. Ajouter n'en est pas un : un client ignore les kinds et les champs qu'il ne connaît pas (§6), si bien qu'un nouveau kind ou un nouveau champ de charge utile garde `v: 2`. C'est ainsi que `task.started` est arrivé, et que `cost.updated` a reçu la répartition prompt / completion, le coût du fournisseur et la part estimée (`estimatedTokens`).

Les huit noms ci-dessus sont **réservés** : un champ de charge utile qui entre en collision avec l'un d'eux est supprimé plutôt qu'autorisé à se faire passer pour l'enveloppe. Ce n'est pas théorique — la charge utile de l'entrée humaine appelait d'abord son champ `kind` et le perdait, d'où le nom `inputKind` sur le fil.

Les champs de charge utile sont **à plat** à côté de l'enveloppe, pas imbriqués sous une clé `payload`. Seule exception : `hub.message`, dont la charge est opaque au protocole et transportée telle quelle.

---

## 3. Sortant — ce qu'un run dit

| `kind` | Charge utile | Quand |
|---|---|---|
| `run.started` | `target`, `stream` | Le run commence. |
| `task.started` | `taskId`, `agentRole` | Une tâche commence, dans les six modes d'orchestration — au moment où son agent est choisi, avant qu'on ne lui demande quoi que ce soit. Entre ce kind et son `task.completed`, un observateur montre la tâche en cours ; la charge utile est volontairement mince, rien n'a encore été mesuré. Un CLI plus ancien ne dit rien ici, et un client ne doit pas refuser un `task.completed` dont il n'a jamais vu le départ. |
| `task.completed` | `taskId`, `agentRole`, `success`, `durationMs`, `tokens`, `toolCalls` | Chaque tâche se termine, **dans les six modes d'orchestration** — échec et annulation compris (voir `error`). `tokens` et `toolCalls` valent `0` quand le mode ne les mesure pas, jamais absents. |
| `tool.called` | `toolName`, `argsSummary?` | Un outil est invoqué. `argsSummary` résume les **noms** d'arguments, jamais leur contenu : un appel peut porter un fichier entier. |
| `tool.returned` | `toolName`, `success`, `durationMs` | L'outil a fini — **y compris s'il a levé**, pour qu'un observateur n'affiche jamais une étape éternellement en cours. Corrélé à son `tool.called`. |
| `delegation.started` | `toRole?` | Un agent a confié du travail à un autre (l'outil `delegate_work_to_coworker`). La description de la tâche reste hors du flux, comme toute valeur d'argument. |
| `agent.spawned` | `role?`, `reason?` | L'équipe a grandi en cours d'exécution — émis quand un appel à l'outil `spawn_agent` est observé. rc.2 ne câble cet outil sur aucun agent par défaut : ce kind n'apparaît que dans les déploiements qui l'attachent eux-mêmes. |
| `cost.updated` | `tokens`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?`, `model?`, `provider?`, `cost?`, `currency?`, `costSource?` | Le compteur bouge — après **chaque appel de génération** du run, pendant qu'il se déroule : les tours d'agent et leurs relances, le manager hiérarchique, le planificateur, les pipelines RAG, les services mémoire, les étapes de flow, les juges LLM, les appels `ctx.llm.*` (voir *Ce que le compteur compte* plus bas). Chaque chiffre est le cumul du run, comme ceux de `run.finished` : `promptTokens` est ce qui est monté, `completionTokens` ce qui est revenu, et la paire de cache (une partition du côté prompt) apparaît dès qu'un fournisseur l'a mesurée — non mesurée, elle est absente, jamais `0`. `estimatedTokens` apparaît dès qu'un fournisseur n'a rien compté pour un appel et que le runtime l'a estimé : la part de `tokens` qui est approximative, qu'un client signale au lieu de la faire passer pour un décompte. `model` et `provider` disent qui a répondu ; l'enveloppe porte `crewId` et, sous `agentId`, le rôle de l'agent pour qui l'appel a été fait — absent pour un appel qu'aucun agent n'a fait, comme le plan. **Le prix est celui du fournisseur ou rien** : `cost` apparaît dès qu'un fournisseur a facturé dans sa réponse (le `usage.cost` d'OpenRouter), avec `costSource: "vendor"` et `currency` (ISO 4217) quand le provider l'énonce ; un appel gratuit est facturé `0`, relayé comme `0`. Aucune estimation ne passe ici — le registre de prix du framework sert les budgets, et un chiffre calculé depuis lui se lirait comme une facture. |
| `llm.delta` | `text` | Un fragment de texte généré. **Seulement sous `--stream`.** |
| `input.needed` | `inputKind` (`text`\|`confirm`\|`choice`), `prompt`, `choices?`, `defaultValue?`, `taskDescription?` | Une tâche déclarée `humanInput: true` pose une question. |
| `hub.message` | `from?`, `topic?`, `payload?`, `expectsReply?` | Le hub du run a relayé quelque chose à ce processus. `from` est l'adresse hub de l'expéditeur (`agent://{crew}/{agent}`, `crew://{crew}`), pour que le pair puisse attribuer et répondre ; absent quand le hub ne la connaît pas (la réponse à un `send`, appariée par `correlationId`). `expectsReply: true` n'apparaît que sur le `send` d'un agent : l'expéditeur est bloqué en attente d'un `reply` sous son propre timeout, et le silence au-delà est un refus. Le pair n'a pas à deviner quelles lignes corrélées sont des questions — un relais de topic peut porter un `correlationId` lui aussi. |
| `error` | `code`, `message`, `recoverable` | Quelque chose a échoué. Un run qui s'arrête — annulé ou en échec, dans n'importe quel mode — se termine par `code: crew_cancelled` ou `crew_failed` avant `run.finished`. |
| `run.finished` | `success`, `exitCode`, `tokens`, `durationMs`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?` | Le run se termine, avec ce qu'il a dépensé : le total de jetons et sa répartition — la somme de toutes les lectures `cost.updated`, si bien que les deux ne peuvent pas diverger —, la durée réelle, la paire de cache quand un fournisseur l'a mesurée, et la part estimée quand un fournisseur a laissé un appel sans décompte. |

Un run qui ne rapporte rien n'est pas un run qui se passe bien — c'est un run qui ne rapporte rien. Un client doit montrer la différence, pas la masquer.

### Ce que le compteur compte

Chaque appel de génération du run, mesuré en un seul endroit : la fabrique de fournisseurs enveloppe chaque fournisseur qu'elle construit dans un décorateur de mesure, et un hôte qui enregistre son propre fournisseur le fait par `AddOrkeonLlmProvider`, qui l'enveloppe de la même façon. Un appel est donc compté par le fournisseur qui l'a reçu, quel que soit l'appelant — jamais par l'appelant, jamais deux fois. Auparavant, seuls la boucle d'agent et la façade de script rapportaient leurs appels : le manager, le planificateur, les pipelines RAG, les services mémoire, les flows, les juges et les relances de l'agent lui-même dépensaient des jetons que ni le compteur en direct ni `run.finished` ne montraient.

Chaque lecture dit à qui revient l'appel. L'orchestrateur ouvre une portée pour chaque tâche — la crew, l'agent, la tâche — et la partie du run qui fait un appel nomme le travail : `agent`, `manager`, `planning`, `rag`, `memory`, `flow`, `judge`, ou la méthode `ctx.llm.*`. Un pipeline RAG interrogé par l'outil d'un agent reste l'affaire de cet agent. Un appel que personne ne revendique est compté quand même, sous `unattributed`.

Deux choses échappent au compteur : **les appels d'embeddings**, qui ne sont pas des appels de génération, et un `IChatClient` qu'un hôte enregistre sans qu'il repose sur un fournisseur Orkeon. Un flux texte seul ne porte aucun usage : il est compté à sa fin, sur estimation, sous `estimatedTokens` — et une estimation n'est jamais chiffrée en coût. Voir [Limitations connues](../reference/limitations.md).

---

## 4. Entrant — ce qu'un processus observateur peut dire

Un document JSON par ligne sur stdin. Chaque verbe se projette sur un membre d'`IEventHub`, sauf la réponse humaine :

| `kind` | Charge | Effet |
|---|---|---|
| `input.given` | `correlationId?`, `value` | Répond à un `input.needed` en attente. |
| `post` | `to`, `payload` | `IEventHub.PostAsync` — écrit dans une boîte aux lettres. |
| `send` | `to`, `payload`, `timeoutMs?`, `correlationId?` | `SendAsync` ; la réponse revient en `hub.message` corrélé. |
| `publish` | `topic`, `payload`, `retainAs?` | `PublishAsync`. |
| `reply` | `correlationId`, `payload` | Répond à une question **qu'un agent a posée à ce processus**. |
| `subscribe` / `unsubscribe` | `topic` | Ouvre ou ferme un relais de ce topic vers `hub.message`. |

Un `input.given` **sans** identifiant de corrélation répond à la question en attente : un humain qui tape dans un terminal n'a pas d'identifiant à citer.

### Le silence ne vaut pas consentement

Sans `--events`, une tâche déclarée `humanInput: true` est **approuvée d'office** — un repli défendable pour un run non surveillé, et la mauvaise réponse dès qu'un écran regarde. Demander le protocole remplace ce fournisseur : la question part sur le flux et le run attend.

Si aucune réponse ne vient — canal fermé, run annulé — la confirmation est **refusée**, jamais accordée.

---

## 5. Le siège au hub

Un processus observateur est adressable en `client://{nom}` (`--client`, défaut `studio`). Les agents lui écrivent exactement comme à un autre agent, et il peut écrire, publier et s'abonner en retour.

**Qui a le droit de l'atteindre est la décision de la crew**, pas celle du protocole. Une crew autorise l'échange en nommant le pair dans son bloc `links:` :

```yaml
name: billing-crew
links:
  - to: "client:studio"
    direction: bidirectional
    allowed_topics: [run.progress]
```

Sans lien déclaré, la politique par défaut de l'ACL laisse quand même passer — le hub a été livré sans ACL, et refuser le trafic non déclaré casserait toutes les crews existantes — mais une crew qui déclare un lien est tenue à ce qu'elle a déclaré. Les règles complètes sont dans [EventHub §10](event-hub-and-crew-lifecycle.md).

---

## 6. Piloter un run depuis un autre programme

```bash
orkeon run crew.yaml --events jsonl --client mon-observateur
```

Lisez stdout ligne par ligne, analysez chaque ligne en JSON, aiguillez sur `kind`. Écrivez réponses et commandes sur stdin, un document JSON par ligne, avec vidage du tampon. Trois faits sur lesquels un pilote peut compter :

- **Les deux dialectes le parlent.** Une cible `.ork.ts` est observée par les mêmes coutures qu'une crew YAML — outils, compteur de jetons, pont du hub et fournisseur de réponses humaines arrivent tous jusqu'à l'hôte de script.
- **stdout porte le protocole et rien d'autre.** Sur un run observé, chaque ligne de log part sur stderr ; un log entre deux documents JSONL serait une erreur de parsing chez vous.
- **`jsonl` est la seule valeur que `--events` accepte**, et il le dit plutôt que de deviner ; `--client` sans `--events` avertit au lieu d'être ignoré en silence.

Un échange minimal :

```
→ {"v":2,"seq":1,"ts":"…","kind":"run.started","target":"crew.yaml","stream":false}
→ {"v":2,"seq":2,"ts":"…","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Publier le rapport ?"}
← {"kind":"input.given","correlationId":"c-1","value":"yes"}
→ {"v":2,"seq":3,"ts":"…","kind":"task.started","taskId":"t1","agentRole":"writer"}
→ {"v":2,"seq":4,"ts":"…","kind":"cost.updated","crewId":"01K…","agentId":"writer","tokens":1840,"promptTokens":1600,"completionTokens":240,"model":"google/gemini-3.7-flash","provider":"openrouter","cost":0.0021,"currency":"USD","costSource":"vendor"}
→ {"v":2,"seq":5,"ts":"…","kind":"task.completed","taskId":"t1","agentRole":"writer","success":true,"durationMs":4200,"tokens":1840,"toolCalls":3}
→ {"v":2,"seq":6,"ts":"…","kind":"run.finished","success":true,"exitCode":0,"tokens":1840,"durationMs":4300,"promptTokens":1600,"completionTokens":240}
```

Deux règles à respecter en construisant votre client. **Ignorez un `kind` que vous ne connaissez pas** — un Orkeon plus récent en dit plus qu'un client plus ancien n'en comprend, et planter sur une ligne non lue est pire qu'en afficher un peu moins. Et **gardez ce que vous n'avez pas su analyser** : une ligne non protocolaire reste quelque chose que le run a dit, et la perdre perd le diagnostic.

---

## 7. Qui lit ceci aujourd'hui

- **Orkeon Studio**, dont l'écran « Lancer » montre la progression — la tâche en cours et l'outil au travail, depuis `task.started` et `tool.called`, autant que les tâches terminées — le coût et les questions du run au lieu d'un défilement — voir [Studio](studio.md). L'écran « Lancer » occupe aussi le siège du hub : le `send` d'un agent vers `client://studio` (marqué `expectsReply`) apparaît comme un panneau de demande auquel l'utilisateur répond, et la réponse repart par stdin ; les posts du hub sont listés au lieu d'être perdus. Le silence au-delà du timeout propre à l'agent reste un refus — la règle que le silence suit partout sur ce bus — l'écran donne simplement à un humain la chance de parler avant.
- `Orkeon.Studio.Core.Run` — `RunClient` et `RunProgressModel`, un client de référence en ~460 lignes, sans aucune dépendance à Infrastructure ni à un LLM. `RunClient` est la forme à copier pour un pair qui prend le siège sans écran : subscribe, post, reply.

La même enveloppe porte le flux de [l'Atelier](../reference/cli.md#orkeon-forge), donc un client qui lit l'un lit l'autre.
