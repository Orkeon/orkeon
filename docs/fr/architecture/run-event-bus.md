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

Les huit noms ci-dessus sont **réservés** : un champ de charge utile qui entre en collision avec l'un d'eux est supprimé plutôt qu'autorisé à se faire passer pour l'enveloppe. Ce n'est pas théorique — la charge utile de l'entrée humaine appelait d'abord son champ `kind` et le perdait, d'où le nom `inputKind` sur le fil.

Les champs de charge utile sont **à plat** à côté de l'enveloppe, pas imbriqués sous une clé `payload`. Seule exception : `hub.message`, dont la charge est opaque au protocole et transportée telle quelle.

---

## 3. Sortant — ce qu'un run dit

| `kind` | Charge utile | Quand |
|---|---|---|
| `run.started` | `target`, `stream` | Le run commence. |
| `task.completed` | `taskId`, `agentRole`, `success`, `durationMs`, `tokens`, `toolCalls` | Chaque tâche se termine, **dans les six modes d'orchestration** — échec et annulation compris (voir `error`). `tokens` et `toolCalls` valent `0` quand le mode ne les mesure pas, jamais absents. |
| `tool.called` | `toolName`, `argsSummary?` | Un outil est invoqué. `argsSummary` résume les **noms** d'arguments, jamais leur contenu : un appel peut porter un fichier entier. |
| `tool.returned` | `toolName`, `success`, `durationMs` | L'outil a fini — **y compris s'il a levé**, pour qu'un observateur n'affiche jamais une étape éternellement en cours. Corrélé à son `tool.called`. |
| `delegation.started` | `toRole?` | Un agent a confié du travail à un autre (l'outil `delegate_work_to_coworker`). La description de la tâche reste hors du flux, comme toute valeur d'argument. |
| `agent.spawned` | `role?`, `reason?` | L'équipe a grandi en cours d'exécution — émis quand un appel à l'outil `spawn_agent` est observé. rc.2 ne câble cet outil sur aucun agent par défaut : ce kind n'apparaît que dans les déploiements qui l'attachent eux-mêmes. |
| `cost.updated` | `tokens`, `model?`, `provider?` | Le compteur de jetons bouge. `tokens` est cumulatif ; `model` quand le fournisseur le rapporte ; `provider` seulement sur les appels de la façade de scripting. **Aucun champ de prix** : le framework n'a pas de table de prix, et en inventer une serait pire que l'omettre. |
| `llm.delta` | `text` | Un fragment de texte généré. **Seulement sous `--stream`.** |
| `input.needed` | `inputKind` (`text`\|`confirm`\|`choice`), `prompt`, `choices?`, `defaultValue?`, `taskDescription?` | Une tâche déclarée `humanInput: true` pose une question. |
| `hub.message` | `from?`, `topic?`, `payload?`, `expectsReply?` | Le hub du run a relayé quelque chose à ce processus. `from` est l'adresse hub de l'expéditeur (`agent://{crew}/{agent}`, `crew://{crew}`), pour que le pair puisse attribuer et répondre ; absent quand le hub ne la connaît pas (la réponse à un `send`, appariée par `correlationId`). `expectsReply: true` n'apparaît que sur le `send` d'un agent : l'expéditeur est bloqué en attente d'un `reply` sous son propre timeout, et le silence au-delà est un refus. Le pair n'a pas à deviner quelles lignes corrélées sont des questions — un relais de topic peut porter un `correlationId` lui aussi. |
| `error` | `code`, `message`, `recoverable` | Quelque chose a échoué. Un run qui s'arrête — annulé ou en échec, dans n'importe quel mode — se termine par `code: crew_cancelled` ou `crew_failed` avant `run.finished`. |
| `run.finished` | `success`, `exitCode`, `tokens` | Le run se termine. |

Un run qui ne rapporte rien n'est pas un run qui se passe bien — c'est un run qui ne rapporte rien. Un client doit montrer la différence, pas la masquer.

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
→ {"v":2,"seq":3,"ts":"…","kind":"task.completed","taskId":"t1","agentRole":"writer","success":true,"durationMs":4200}
→ {"v":2,"seq":4,"ts":"…","kind":"run.finished","success":true,"exitCode":0,"tokens":1840}
```

Deux règles à respecter en construisant votre client. **Ignorez un `kind` que vous ne connaissez pas** — un Orkeon plus récent en dit plus qu'un client plus ancien n'en comprend, et planter sur une ligne non lue est pire qu'en afficher un peu moins. Et **gardez ce que vous n'avez pas su analyser** : une ligne non protocolaire reste quelque chose que le run a dit, et la perdre perd le diagnostic.

---

## 7. Qui lit ceci aujourd'hui

- **Orkeon Studio**, dont l'écran « Lancer » montre progression, coût et questions du run au lieu d'un défilement — voir [Studio](studio.md). L'écran « Lancer » occupe aussi le siège du hub : le `send` d'un agent vers `client://studio` (marqué `expectsReply`) apparaît comme un panneau de demande auquel l'utilisateur répond, et la réponse repart par stdin ; les posts du hub sont listés au lieu d'être perdus. Le silence au-delà du timeout propre à l'agent reste un refus — la règle que le silence suit partout sur ce bus — l'écran donne simplement à un humain la chance de parler avant.
- `Orkeon.Studio.Core.Run` — `RunClient` et `RunProgressModel`, un client de référence en ~380 lignes, sans aucune dépendance à Infrastructure ni à un LLM. `RunClient` est la forme à copier pour un pair qui prend le siège sans écran : subscribe, post, reply.

La même enveloppe porte le flux de [l'Atelier](../reference/cli.md#orkeon-forge), donc un client qui lit l'un lit l'autre.
