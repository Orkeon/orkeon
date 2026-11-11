> 🇬🇧 [English version](../../architecture/run-event-bus.md)

# Le bus d'événements du run

**Périmètre** : `orkeon run --events jsonl`, le protocole qu'il parle, et le siège qu'il donne à un processus observateur sur l'[EventHub](event-hub-and-crew-lifecycle.md) du run.
**Public** : quiconque pilote Orkeon depuis un autre programme — un front-end de bureau, une passerelle, un job de CI, votre propre outillage.

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

**`v` ne bouge que pour un changement qu'un client existant lirait de travers** — un champ retiré, renommé ou qui change de sens. Ajouter n'en est pas un : un client ignore les kinds et les champs qu'il ne connaît pas (§6), si bien qu'un nouveau kind ou un nouveau champ de charge utile garde `v: 2`.

Les huit noms ci-dessus sont **réservés** : un champ de charge utile qui entre en collision avec l'un d'eux est supprimé plutôt qu'autorisé à se faire passer pour l'enveloppe. C'est pourquoi la charge utile de l'entrée humaine nomme son champ `inputKind`.

Les champs de charge utile sont **à plat** à côté de l'enveloppe, pas imbriqués sous une clé `payload`. Seule exception : `hub.message`, dont la charge est opaque au protocole et transportée telle quelle.

---

## 3. Sortant — ce qu'un run dit

Le vocabulaire est déclaré une seule fois, dans le satellite sans dépendance `Orkeon.Constants.Protocol` ([ADR-009](../adr/ADR-009-shared-constants-satellites.md)) : `RunEventKinds` nomme les dix-neuf kinds — les dix-huit ci-dessous et l'entrant `input.given` (§4) — et `RunEventKinds.All` est l'ensemble qu'un client vérifie savoir traiter. Le CLI les écrit et un client les lit sans que l'un référence l'autre. Un hôte C# lit les mêmes mots en processus : les événements que rend `ICrewOrchestrationService.KickoffStreamingAsync` portent ces kinds — `task.started`, `tool.called`, `tool.returned`, `llm.delta`, `task.completed`, `error`, `run.finished` (avec le `CrewOutput` du run) — et les codes d'erreur de `Orkeon.Constants.Protocol.RunEventErrorCodes` : `crew_failed` (une tâche n'a pas réussi, ou le run s'est arrêté sur une erreur) et `crew_cancelled` (un délai dépassé, Ctrl+C, un consommateur qui a quitté le flux). Le flux du CLI reste sur le hook d'exécution et le compteur : il ne passe pas par cette API.

| `kind` | Charge utile | Quand |
|---|---|---|
| `run.started` | `target`, `stream` | Le run commence. |
| `task.started` | `taskId`, `agentRole`, `attempt` | Une tâche commence, dans les six modes d'orchestration — au moment où son agent est choisi, avant qu'on ne lui demande quoi que ce soit. Entre ce kind et son `task.completed`, un observateur montre la tâche en cours ; la charge utile est volontairement mince, rien n'a encore été mesuré. Un client ne doit pas refuser un `task.completed` dont il n'a jamais vu le départ. `attempt` (entier, toujours présent) vaut 1 pour la première exécution de la tâche et 2, 3, … pour ses retries ([Retry d'une tâche](yaml-schema.md#retry-dune-tâche)) : une tâche avec un `retry:` s'annonce de nouveau à chaque retry, et un client traite un `task.started` répété pour une tâche déjà en cours comme la même tâche. |
| `task.completed` | `taskId`, `agentRole`, `success`, `skipped`, `resumed`, `attempt`, `durationMs`, `tokens`, `toolCalls`, `skipReason`, `defaulted`, `isDecision`, `decision` | Chaque tâche se termine, **dans les six modes d'orchestration** — échec et annulation compris (voir `error`). `attempt` (entier, toujours présent) est la tentative qui a clos la tâche : 1 quand elle a tourné une fois, 3 pour une tâche dont c'est la troisième tentative ([Retry d'une tâche](yaml-schema.md#retry-dune-tâche)) ; une tâche qui n'a pas tourné dit 1. `resumed: true` marque une tâche d'un run repris (`orkeon run --resume`) dont le résultat est revenu du checkpoint : aucun modèle n'a été interrogé pour elle (`false` sinon, toujours présent, dans tous les modes d'orchestration). `skipped: true` marque une tâche qui n'a jamais tourné, soit parce qu'une tâche dont elle dépend n'a pas réussi, soit parce que sa condition `when` n'était pas vraie (`success` vaut alors `false` et `durationMs` `0`). `tokens` et `toolCalls` valent `0` quand le mode ne les mesure pas, jamais absents. `skipReason` (chaîne ou `null`) dit pourquoi une tâche ignorée n'a pas tourné : la dépendance qui n'a pas réussi, ou la condition `when` qui n'était pas vraie ([ADR-016](../adr/ADR-016-decision-tasks-and-conditional-fan-out.md)) ; il vaut `null` tant que `skipped` n'est pas `true`. `isDecision` (booléen) vaut `true` pour une tâche de décision, qui n'a pas d'agent ; `decision` est alors ses réponses en chaîne JSON compacte, et `null` sinon. `defaulted` (booléen) vaut `true` pour une tâche de décision qui a répondu par son repli parce que le circuit de décision est coupé (`--no-decision`, `Orkeon:Decision:Enabled=false` ou aucun modèle de décision configuré). Les quatre sont toujours présents. |
| `tool.called` | `toolName`, `argsSummary?` | Un outil est invoqué. `argsSummary` résume les **noms** d'arguments, jamais leur contenu : un appel peut porter un fichier entier. |
| `tool.returned` | `toolName`, `success`, `durationMs` | L'outil a fini — **y compris s'il a levé**, pour qu'un observateur n'affiche jamais une étape éternellement en cours. Corrélé à son `tool.called`. Pour un tour déployé en parallèle (appels en lecture), les `tool.called` d'un groupe parallèle peuvent tous précéder leurs `tool.returned`, et les `tool.returned` suivent l'ordre des appels. |
| `delegation.started` | `toRole?` | Un agent a confié du travail à un autre (l'outil `delegate_work_to_coworker`). La description de la tâche reste hors du flux, comme toute valeur d'argument. |
| `agent.spawned` | `role?`, `reason?`, `name?` | L'équipe a grandi en cours d'exécution — émis quand un appel à l'outil `spawn_agent` est observé, et quand un script `.ork.ts` lance un enfant (le `spawn_agent` du modèle pendant `ctx.llm.act`, ou `ctx.spawnBackground`), une fois le lancement accepté : `role` est le rôle de l'enfant, `name` son nom d'agent de crew, jamais son entrée. Le framework ne câble l'outil `spawn_agent` de crew sur aucun agent par défaut : hors scripts, ce kind n'apparaît que dans les déploiements qui l'attachent eux-mêmes. |
| `graph.node.entered` | `node`, `visit`, `task?`, `graph?` | Un moteur de graphe va exécuter un nœud, une fois son disjoncteur franchi. `visit` compte les entrées de ce nœud dans le run (1 la première fois, 2 quand une boucle ou un retry revient). Une crew en process Graph nomme la tâche (`task` : son id ; `node` : son nom), un `stateGraph` de script nomme le graphe (`graph`). Un lecteur qui ne connaît pas ce kind l'ignore. |
| `cost.updated` | `tokens`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?`, `model?`, `provider?`, `operation?`, `cost?`, `currency?`, `costSource?` | Le compteur bouge — après **chaque appel de génération** du run, pendant qu'il se déroule : les tours d'agent et leurs relances, le manager hiérarchique, le planificateur, les pipelines RAG, les services mémoire, les juges LLM, les appels `ctx.llm.*` (voir *Ce que le compteur compte* plus bas). Chaque chiffre est le cumul du run, comme ceux de `run.finished` : `promptTokens` est ce qui est monté, `completionTokens` ce qui est revenu, et la paire de cache (une partition du côté prompt) apparaît dès qu'un fournisseur l'a mesurée — non mesurée, elle est absente, jamais `0`. `estimatedTokens` apparaît dès qu'un fournisseur n'a rien compté pour un appel et que le runtime l'a estimé : la part de `tokens` qui est approximative, qu'un client signale au lieu de la faire passer pour un décompte. `model` et `provider` disent qui a répondu, et `operation` à quoi servait l'appel — `agent`, `manager`, `planning`, `rag`, `memory`, `judge`, ou la méthode `ctx.llm.*` d'un script (`complete`, `chat`, `stream`, `extract`, `decide`, `act`) — absent pour un appel que rien n'a revendiqué : un client qui nomme le modèle sur lequel travaille une équipe lit les lectures de ses agents, pas celles d'un juge ou d'un pipeline RAG ; l'enveloppe porte `crewId` et, sous `agentId`, le rôle de l'agent pour qui l'appel a été fait — absent pour un appel qu'aucun agent n'a fait, comme le plan. **Le prix est celui du fournisseur ou rien** : `cost` apparaît dès qu'un fournisseur a facturé dans sa réponse (le `usage.cost` d'OpenRouter), avec `costSource: "vendor"` et `currency` (ISO 4217) quand le provider l'énonce ; un appel gratuit est facturé `0`, relayé comme `0`. Aucune estimation ne passe ici — le registre de prix du framework sert les budgets, et un chiffre calculé depuis lui se lirait comme une facture. |
| `llm.delta` | `deltaKind`, `text` ou `toolCall` | Un fragment de ce que génère le modèle, tel qu'il arrive : chaque tour d'agent d'une crew (YAML, dossier de crew ou `.ork.ts` déclaratif) et chaque appel `ctx.llm.*` d'un script. `deltaKind` vaut `content` (le texte, dans `text`) ou `tool_call` (un fragment d'appel d'outil que le modèle est en train de construire, dans `toolCall: {index, id?, name?, argumentsDelta}` : `id` et `name` viennent sur le fragment qui les porte, `argumentsDelta` est un morceau des arguments JSON ; Ollama envoie un appel entier, en un seul fragment). Un consommateur qui lit `text` ignore un fragment `tool_call` ; l'appel d'outil final reste l'événement `tool.called`. Un hôte qui a enregistré la DLP (`AddOrkeonDlp`) reçoit les arguments de chaque appel retenus jusqu'à la fin de l'appel, scannés une fois et envoyés en un seul fragment `tool_call` rédigé. Les fragments sont retenus avec le texte tant que le gardien juge la sortie. **Seulement sous `--stream`** — sans lui, les tours ne sont pas diffusés du tout. |
| `llm.call.started` | `callId`, `mode`, `estimatedPromptTokens` | Un appel au modèle est parti, quel que soit l'appelant que le compteur compte : `mode` dit comment il passe sur le fil (`stream` ou `buffer`, le `CallMode` du réglage résolu), et `estimatedPromptTokens` ce que le runtime estime être monté. Écrit avec ou sans `--stream`. |
| `llm.call.progress` | `callId`, `estimatedCompletionTokens` | Un appel en stream a reçu davantage : l'estimation cumulée de ce qui est revenu. Au plus une ligne par appel toutes les 250 ms, et toujours la dernière avant `llm.call.finished` ; un appel en buffer n'en écrit aucune. |
| `llm.call.finished` | `callId`, `mode`, `fellBack`, `promptTokens?`, `completionTokens?` | L'appel est terminé, répondu ou en échec : `mode` est le mode réellement utilisé, `fellBack` dit qu'un stream refusé a été refait en buffer, et les décomptes sont ceux du fournisseur quand il les a donnés. Un client retire l'estimation de l'appel quand cette ligne porte le décompte du fournisseur, que le `cost.updated` qui suit ajoute aux chiffres cumulés, et la garde, signalée comme estimée, quand il n'en vient aucun. |
| `llm.fallback` | `profile`, `from`, `to`, `reason` | La [chaîne de secours](llm-providers.md#bascule-de-profil) d'un profil a fait passer un appel au profil suivant : `profile` est celui qu'un crew a nommé, `from` le profil qui a échoué, `to` celui où l'appel part, `reason` la classe de l'échec (`server_error`, `rate_limited`, `timeout`, `transport`, `exhausted`). Une ligne par bascule : une chaîne de trois peut en écrire deux pour un appel. Les lignes `llm.call.*` et `cost.updated` nomment le fournisseur qui a répondu. |
| `input.needed` | `inputKind` (`text`\|`confirm`\|`choice`\|`multi`\|`tool_approval`), `prompt`, `requestId?`, `choices?`, `defaultValue?`, `taskKey?`, `toolName?`, `suspending?`, `taskDescription?` | Une tâche déclarée `humanInput: true` pose une question. |
| `hub.message` | `from?`, `topic?`, `payload?`, `expectsReply?`, `messageId?`, `metadata?` | Le hub du run a relayé quelque chose à ce processus. `from` est l'adresse hub de l'expéditeur (`agent://{crew}/{agent}`, `crew://{crew}`), pour que le pair puisse attribuer et répondre ; absent quand le hub ne la connaît pas (la réponse à un `send`, appariée par `correlationId`). `expectsReply: true` n'apparaît que sur le `send` d'un agent : l'expéditeur est bloqué en attente d'un `reply` sous son propre timeout, et le silence au-delà est un refus. Le pair n'a pas à deviner quelles lignes corrélées sont des questions — un relais de topic peut porter un `correlationId` lui aussi. `messageId` est l'identifiant du message du hub — sur le `post` d'un agent, celui qu'a rendu son `post_message` — et `metadata` les paires de chaînes de l'enveloppe, écrites seulement quand il y en a ; la réponse à un `send` ne porte ni l'un ni l'autre. |
| `error` | `code`, `message`, `recoverable` | Quelque chose a échoué. Un run qui s'arrête — annulé ou en échec, dans n'importe quel mode, et avant sa première tâche aussi (une mémoire inutilisable, un plan dont le fournisseur a échoué) — se termine par `code: crew_cancelled` ou `crew_failed` avant `run.finished`. |
| `run.finished` | `success`, `exitCode`, `status?`, `requestId?`, `tokens`, `durationMs`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?` | Le run se termine, avec ce qu'il a dépensé : le total de jetons et sa répartition — la somme de toutes les lectures `cost.updated`, si bien que les deux ne peuvent pas diverger —, la durée réelle, la paire de cache quand un fournisseur l'a mesurée, et la part estimée quand un fournisseur a laissé un appel sans décompte. |

Un run qui ne rapporte rien n'est pas un run qui se passe bien — c'est un run qui ne rapporte rien. Un client doit montrer la différence, pas la masquer.

### Ce que le compteur compte

Chaque appel de génération du run, mesuré en un seul endroit : la fabrique de fournisseurs enveloppe chaque fournisseur qu'elle construit dans un décorateur de mesure, et un hôte qui enregistre son propre fournisseur le fait par `AddOrkeonLlmProvider`, qui l'enveloppe de la même façon. Un appel est donc compté par le fournisseur qui l'a reçu, quel que soit l'appelant — jamais par l'appelant, jamais deux fois.

Chaque lecture dit à qui revient l'appel. L'orchestrateur ouvre une portée pour chaque tâche — la crew, l'agent, la tâche — et la partie du run qui fait un appel nomme le travail, que la lecture porte sous `operation` : `agent`, `manager`, `planning`, `rag`, `memory`, `judge`, ou la méthode `ctx.llm.*`. Un pipeline RAG interrogé par l'outil d'un agent reste l'affaire de cet agent. Un appel que personne ne revendique est compté quand même — `unattributed` dans les rapports du moteur, et aucun `operation` sur le fil.

Deux choses échappent au compteur : **les appels d'embeddings**, qui ne sont pas des appels de génération, et un `IChatClient` qu'un hôte enregistre sans qu'il repose sur un fournisseur Orkeon. Un flux texte seul ne porte aucun usage : il est compté à sa fin, sur estimation, sous `estimatedTokens` — et une estimation n'est jamais chiffrée en coût. Voir [Limitations connues](../reference/limitations.md).

---

## 4. Entrant — ce qu'un processus observateur peut dire

Un document JSON par ligne sur stdin. Chaque verbe se projette sur un membre d'`IEventHub`, sauf la réponse humaine :

| `kind` | Charge | Effet |
|---|---|---|
| `input.given` | `correlationId?` (ou `requestId?`), `value` | Répond à un `input.needed` en attente. Aussi sortant : `{requestId}` quand un run en attente a reçu sa réponse (`POST /runs/{id}/input`) ou qu'un run repris en a reçu une (`--input`). |
| `post` | `to`, `payload` | `IEventHub.PostAsync` — écrit dans une boîte aux lettres. |
| `send` | `to`, `payload`, `timeoutMs?`, `correlationId?` | `SendAsync` (`timeoutMs` vaut 30 s par défaut) ; la réponse revient en `hub.message` corrélé. |
| `publish` | `topic`, `payload`, `retainAs?` | `PublishAsync`. |
| `reply` | `correlationId`, `payload` | Répond à une question **qu'un agent a posée à ce processus**. |
| `subscribe` / `unsubscribe` | `topic` | Ouvre ou ferme un relais de ce topic vers `hub.message`. |

Un `input.given` **sans** identifiant de corrélation répond à la plus ancienne question en attente : un humain qui tape dans un terminal n'a pas d'identifiant à citer.

Un `input.given` **sans** id de corrélation répond à la plus ancienne question en attente. L'id d'une question durable est son `requestId` : `correlationId` ou `requestId` peut le porter.

**Une question qui survit au processus.** Avec `Orkeon:Checkpointing:Enabled`, une question à un humain est persistée avec la session de checkpoint du run (`PendingInput`, phase `WaitingForInput`) sous un id stable, `<TaskKey>#<n>.<q>` (`n` l'invocation de la tâche, `q` la question dans celle-ci). `Orkeon:HumanInput:Pause` dit ce que fait le run tant que personne n'a répondu : `Hold` attend, jusqu'à `HoldTimeout`, puis suspend ; `Suspend` s'arrête aussitôt. Sans valeur, un run qui a quelqu'un à interroger (un terminal, un flux qu'un client lit) attend, un run sans personne (un `orkeon run` simple) suspend ; le démon et `--events` attendent par défaut (30 minutes, sans limite). Un run suspendu finit par `run.finished` `status: "waiting_for_input"`, `exitCode: 75`, `success: false` et le `requestId` de la question : ni un succès ni un échec. Il reprend avec `orkeon run <crew> --resume <id> --input "..."` : la tâche qui a posé la question est rejouée (granularité : la tâche), pose la même question sous le même id et trouve la réponse ; le prompt montré est celui persisté la première fois, jamais recalculé, et la réponse est rapprochée par `requestId` seul. La première réponse gagne.

Une commande du hub qui arrive avant que le hub du run existe est retenue — jusqu'à 64 — puis rejouée dès qu'il existe ; au-delà, elle est ignorée, comme toute autre ligne dont le run ne peut rien faire.

### Le silence ne vaut pas consentement

Sans `--events`, une tâche déclarée `humanInput: true` est **approuvée d'office** — un repli défendable pour un run non surveillé, et la mauvaise réponse dès qu'un écran regarde. Demander le protocole remplace ce fournisseur : la question part sur le flux et le run attend.

Si aucune réponse ne vient — canal fermé, run annulé — la confirmation est **refusée**, jamais accordée.

---

## 5. Le siège au hub

Un processus observateur est adressable en `client://{nom}` (`--client`, défaut `client` ; le nom doit être non vide et ne contenir aucun `/`, sinon le run refuse de démarrer). Les agents lui écrivent exactement comme à un autre agent, et il peut écrire, publier et s'abonner en retour.

**Qui a le droit de l'atteindre est la décision de la crew**, pas celle du protocole. Une crew autorise l'échange en nommant le pair dans son bloc `links:` :

```yaml
name: billing-crew
links:
  - to: "client:client"
    direction: bidirectional
    allowed_topics: [run.progress]
```

Sans lien déclaré, la politique par défaut de l'ACL laisse passer le trafic, si bien qu'une crew qui ne déclare rien fonctionne — mais une crew qui déclare un lien est tenue à ce qu'elle a déclaré. Les règles complètes sont dans [EventHub §10](event-hub-and-crew-lifecycle.md).

---

## 6. Piloter un run depuis un autre programme

```bash
orkeon run crew.yaml --events jsonl --client mon-observateur
```

Lisez stdout ligne par ligne, analysez chaque ligne en JSON, aiguillez sur `kind`. Écrivez réponses et commandes sur stdin, un document JSON par ligne, avec vidage du tampon. Trois faits sur lesquels un pilote peut compter :

- **Les deux dialectes le parlent.** Une cible `.ork.ts` est observée par les mêmes coutures qu'une crew YAML — outils, compteur de jetons, pont du hub et fournisseur de réponses humaines arrivent tous jusqu'à l'hôte de script.
- **stdout porte le protocole et rien d'autre.** Sur un run observé, chaque ligne de log part sur stderr ; un log entre deux documents JSONL serait une erreur de parsing chez vous. Une ligne de commande que la CLI refuse n'écrit rien sur stdout : le flux ne s'ouvre pas, le refus tient en une ligne sur stderr et le code de sortie est `1`.
- **`jsonl` est la seule valeur que `--events` accepte**, et il le dit plutôt que de deviner ; `--client` sans `--events` avertit au lieu d'être ignoré en silence.

Un échange minimal :

```
→ {"v":2,"seq":1,"ts":"…","kind":"run.started","target":"crew.yaml","stream":false}
→ {"v":2,"seq":2,"ts":"…","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Publier le rapport ?"}
← {"kind":"input.given","correlationId":"c-1","value":"yes"}
→ {"v":2,"seq":3,"ts":"…","kind":"task.started","taskId":"t1","agentRole":"writer","attempt":1}
→ {"v":2,"seq":4,"ts":"…","kind":"cost.updated","crewId":"01K…","agentId":"writer","tokens":1840,"promptTokens":1600,"completionTokens":240,"model":"google/gemini-3.7-flash","provider":"openrouter","operation":"agent","cost":0.0021,"currency":"USD","costSource":"vendor"}
→ {"v":2,"seq":5,"ts":"…","kind":"task.completed","taskId":"t1","agentRole":"writer","success":true,"attempt":1,"durationMs":4200,"tokens":1840,"toolCalls":3}
→ {"v":2,"seq":6,"ts":"…","kind":"run.finished","success":true,"exitCode":0,"tokens":1840,"durationMs":4300,"promptTokens":1600,"completionTokens":240}
```

Deux règles à respecter en construisant votre client. **Ignorez un `kind` que vous ne connaissez pas** — un Orkeon plus récent en dit plus qu'un client plus ancien n'en comprend, et planter sur une ligne non lue est pire qu'en afficher un peu moins. Et **gardez ce que vous n'avez pas su analyser** : une ligne non protocolaire reste quelque chose que le run a dit, et la perdre perd le diagnostic.

---

## 7. Qui lit ceci aujourd'hui

- **Les front-ends de bureau et de terminal** lisent le flux pour montrer la progression — la tâche en cours et l'outil au travail, depuis `task.started` et `tool.called`, autant que les tâches terminées — le coût et les questions du run au lieu d'un défilement. Un tel client peut aussi occuper le siège du hub : le `send` d'un agent vers `client://<nom>` (marqué `expectsReply`) apparaît comme une demande à laquelle l'utilisateur répond, et la réponse repart par stdin ; les posts du hub sont listés au lieu d'être perdus. Le silence au-delà du timeout propre à l'agent reste un refus — la règle que le silence suit partout sur ce bus — le client donne simplement à un humain la chance de parler avant. Un pair qui prend le siège sans écran suit la même forme : subscribe, post, reply.

La même enveloppe porte le flux de [l'Atelier](../reference/cli.md#orkeon-forge) et la session [`orkeon usecases`](../reference/cli.md#orkeon-usecases) (`usecases search --events jsonl`, dont `UseCaseEventKinds` déclare les kinds à côté de `RunEventKinds`), donc un client qui lit l'un les lit tous — et la connexion d'un compte e-mail, [`orkeon email login --events jsonl`](../reference/cli.md#orkeon-email), dont `EmailEventKinds` déclare les kinds (cinq kinds `email.*` et `error`).

## 8. Où vont les événements : les sinks et l'historique de run

Les observateurs d'un run (`RunEventObserver`, `ObservedTool`, `RunEventsLlmCallObserver`, dans `Orkeon.Hosting`) écrivent dans
un seul `IRunEventSink` (`Orkeon.Application.Runs`, expérimental `ORKEXP007`), un `RunEventPublisher`. Le publisher estampille
chaque événement une fois (`seq`, `ts`, l'identité du run) et le remet à chaque destination : toutes tiennent les mêmes lignes.

- **`StdoutJsonLinesRunEventSink`** (la CLI) : le flux de `orkeon run --events jsonl`. Ses octets sont figés par un
  test golden (`StdoutEventContractGoldenTests`). Les autres verbes qui streament (`forge`, `email`, `drive`,
  `usecases`) gardent leur propre writer, qui partage le code de l'enveloppe et l'observateur d'appels de modèle.
- **`JsonLinesFileRunEventSink`** (`Orkeon.Hosting`) : `<run id>.jsonl` et un `index.jsonl` sous le montage `/runs`, par le VFS,
  un writer sérialisé par run.
- **`SqliteRunEventSink`** (`Orkeon.Infrastructure`) : tables `runs` et `run_events`, WAL, dans `runs.db` sous le même montage.

Une destination qui échoue est journalisée et sautée : observer un run ne change jamais son issue. L'historique est la section
[`Orkeon:RunHistory`](../reference/configuration.md#orkeonrunhistory) (éteinte dans la bibliothèque, allumée dans `orkeon-host`
et le gabarit de `orkeon init`). Ses réglages sont lus à la construction de l'hôte, après l'écriture de `run.started` : le
publisher garde donc les premiers événements et les rejoue à l'historique quand il le rejoint. `llm.delta` reste sur le flux
stdout et n'est gardé qu'avec `IncludeDeltas`. Une purge applique `RetentionDays` et `MaxRuns` quand un processus écrit pour la première fois et à la fin d'un run, jamais quand l'historique est seulement lu. Plusieurs processus écrivent un même dossier : l'index JSONL n'est donc qu'augmenté (une purge ajoute une ligne de pierre tombale au lieu de le réécrire), un fichier de run que l'index a perdu est quand même listé, résumé d'après ses propres événements, et le système de fichiers par lequel le magasin écrit est le sien, car l'événement de clôture d'`orkeon run` s'écrit après la destruction de son hôte. Les secrets sont masqués (`RedactingRunEventSink`, le masquage des logs) avant qu'un sink fichier ou Sqlite écrive ; stdout ne l'est pas. Un run repris sous son propre id poursuit la séquence que l'historique tient pour lui (le second `run.started` porte `resumed: true`), de sorte que `seq` reste strictement croissant au sein d'un id de run. `run.started` est enregistré avec le pid et l'hôte du processus qui écrit : un run encore `running` dont le processus a disparu du même hôte est lu `orphaned`.

`IRunHistoryReader` (`ListAsync`, `GetAsync`, `ReadEventsAsync`) relit un run ; [`orkeon runs`](../reference/cli.md#orkeon-runs)
l'affiche comme les lignes du flux. `orkeon-host` enregistre chaque run hébergé sous son `ExecutionId`, qui est aussi l'id de run du démon (un seul id de bout en bout : l'API du host, A2A,
l'historique et les checkpoints nomment le run par lui) ; `hostRunId` dans `run.started` le répète ; les événements de tâche et l'événement de clôture sont enregistrés, pas les
événements `tool.*`, `llm.call.*` et `cost.updated` d'un run hébergé (voir les [limites](../reference/limitations.md)). Un lecteur
ignore un `kind` qu'il ne connaît pas, et l'historique le garde intact.
