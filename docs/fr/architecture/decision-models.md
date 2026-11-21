> 🇬🇧 [English version](../../architecture/decision-models.md)

> **Voir aussi** : [Fournisseurs de modèles](./model-providers.md) · [Fournisseurs LLM](./llm-providers.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Retour à l'index](../INDEX.md)

# Modèles de décision

Un modèle de décision répond à des questions typées sur un état, avec des probabilités calibrées,
et n'écrit aucun texte. Orkeon l'atteint par le port `IDecisionModel` (`Orkeon.Application`) ; le
adaptateur sert trois fournisseurs de décision sur le protocole « System One » : **TypeSafe** (Jev), **Liquid** (d1) et **Kev**. Le
modèle juge et le code d'Orkeon agit : il bloque, route ou escalade un appel, classe des
passages, ou remet une décision à un agent
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

## Le port

`IDecisionModel.DecideAsync(DecisionRequest, ct)` envoie une requête et renvoie une
`DecisionResponse`.

Une `DecisionRequest` porte :

- `state` : le JSON sur lequel portent les questions (un texte, un objet, un document) ;
- `questions` : un dictionnaire d'un nom de votre choix vers une question ;
- `assets` (facultatif, des images nommées), `model` (facultatif, sinon le défaut configuré) et
  `LongContext` (non sérialisé : il pose l'en-tête de contexte long).

| Question | Ce que vous donnez | Ce que vous recevez |
|----------|--------------------|---------------------|
| `choice` | une à 255 options nommées, chacune avec sa description | l'option choisie et la probabilité de chacune |
| `score` | un à dix niveaux d'une échelle ordonnée | le niveau et la probabilité de chacun |
| `noul` | l'énoncé ; `criteria.true` et `criteria.false` sont facultatifs | `noul`, la probabilité de vrai |
| `box` | des images dans `assets` | une zone dans une image |

Une réponse `choice`, `score` ou `box` porte aussi une `confidence`. Un `DecisionRequestValidator` refuse un jeu de questions
vide, un `choice` de plus de 255 options, un `score` de plus de dix niveaux, et une `box` sans images.

## Fournisseurs de décision

Trois fournisseurs servent le même protocole « System One » : **TypeSafe** (modèle Jev), **Liquid**
(modèle d1) et **Kev**. Un seul client, `SystemOneDecisionModel`, les sert par trois descripteurs ;
la requête est `POST <BaseUrl>/systemone` avec `model`, `state` et `questions`, la réponse porte
`answers` et `usage`.

| Fournisseur | Adresse par défaut | Modèle par défaut | Variable de clé | Section |
|-------------|--------------------|-------------------|-----------------|---------|
| `typesafe` | `https://api.typesafe.ai/v1` | `jev-latest` | `TYPESAFE_API_KEY`, obligatoire | `Orkeon:TypeSafe` |
| `liquid` | `https://api.liquid.ai/decisions/v1` | `d1.1` (ou `d1-ultra`) | `LIQUID_API_KEY`, obligatoire | `Orkeon:Liquid` |
| `kev` | `http://127.0.0.1:8009/v1` | `kev-latest` | `KEV_API_KEY`, facultative : envoyée si donnée, sinon l'appel est sans authentification | `Orkeon:Kev` |

```csharp
services.AddOrkeonDecisionProvider(configuration);
```

Les trois binaires livrés (`orkeon run`, `orkeon-host` et `orkeon-repl`) l'appellent eux-mêmes, et
`AddOrkeonDecisionProfiles` dans tous les cas ; un hôte écrit en C# appelle les deux.

```json
{
  "Orkeon": {
    "Decision": { "Provider": "typesafe" },
    "TypeSafe": {
      "Model": "jev-latest",
      "TimeoutSeconds": 30,
      "MaxRetries": 3
    }
  }
}
```

### Le fournisseur actif

L'hôte démarre avec un **fournisseur actif**, ou aucun.

- **Nommé** : `Orkeon:Decision:Provider` vaut `typesafe`, `liquid` ou `kev` (sans tenir compte de
  la casse ; une valeur vide compte comme non nommé). Toute autre valeur refuse le démarrage, en
  listant les noms valides.
- **Déduit**, quand il n'est pas nommé : l'unique fournisseur hébergé (TypeSafe ou Liquid) qui a
  une section de réglages (une clé présente sous `Orkeon`, même vide, compte). Quand aucun n'a de
  section, celui dont la variable de clé est définie. Une section l'emporte sur une clé seule : ce
  fournisseur-là est alors ignoré.
- **Refusé** : les deux sections présentes, ou aucune section et les deux clés définies ; le message
  nomme les deux fournisseurs et `Orkeon:Decision:Provider`.
- **Aucun** : rien de présent, aucun modèle de décision n'est enregistré.
- **Kev** n'est jamais déduit, seulement nommé. Une section `Orkeon:Kev` sans
  `Orkeon:Decision:Provider` est signalée dans le log de démarrage (quand aucun fournisseur n'est nommé, l'avertissement
  nomme `Orkeon:Decision:Provider`) (une ligne de log, pas un avis de réglages).

Le log de démarrage nomme le fournisseur actif, le modèle, l'adresse et `named` ou `deduced`.
Chaque section de fournisseur présente qui n'est pas l'active est nommée dans un avertissement (une
clé seule dans l'environnement n'est pas signalée). `--no-decision` (une exécution sans décisions)
vaut pour tous les fournisseurs.

### Clés, adresses et erreurs

- **La clé** se donne de trois façons : la variable par défaut, une variable nommée dans
  `ApiKeyEnvVar`, ou `ApiKey` dans la section du fournisseur. Elle n'apparaît jamais dans un log,
  une erreur ou un appel de tool. Un fournisseur hébergé démarre sans sa clé ; la première décision
  sans elle échoue, n'est pas réessayée, et nomme la variable ou le réglage.
- **`BaseUrl`** remplace l'adresse par défaut. Une adresse Kev exige un schéma `http` ou `https`,
  refusée au démarrage sinon (le message nomme `Orkeon:Kev:BaseUrl`). Une clé Kev configurée vers
  une adresse `http` non locale est refusée au démarrage (local : `localhost`, `127.0.0.0/8` et
  `::1` ; `https` convient).
- **Les erreurs** deviennent une `DecisionModelException` : un 401 ou 403 donne le message de clé ;
  un autre 4xx sauf 429 est non transitoire et cite le message du fournisseur (un 422 cite le champ
  fautif, un 400 `max_tokens_exceeded` nomme la limite et suggère `longContext` quand la requête ne
  l'a pas demandé, un 413 ou un 415 dit ce que la requête a dépassé). Un 429, un 5xx (529 compris),
  un 408 ou une adresse injoignable est réessayé avec un backoff exponentiel, jusqu'à `MaxRetries`
  fois, chaque tentative avec son propre `TimeoutSeconds` ; puis il échoue en nommant le
  fournisseur, et l'adresse seulement quand elle était injoignable ou n'a pas répondu à temps (un 429, 5xx ou 408
  persistant donne `<Nom> answered <statut>: ...`, sans adresse). Un `usage` sans compteurs de tokens est mesuré à zéro, pas refusé.
- **Les en-têtes « danger zone »** se posent selon la requête : `TypeSafe-Danger-Zone-Images`
  quand elle porte des images ou une question `box`, `TypeSafe-Danger-Zone-Long-Context` quand
  `LongContext` est posé.
- **Questions** : Orkeon envoie chaque question et le modèle par appel tels qu'écrits. Kev, et d1
  servi par llama.cpp, n'acceptent que les questions oui/non, `choice` et `score`.
- **L'usage** est compté une seule fois, comme `decision` (`LlmUsageOperations.Decision`), pour le
  fournisseur actif ; Orkeon ne calcule aucun coût.

### Campagne réelle

`orkeon llm probe -p typesafe` (ou `liquid`, `kev`) exécute sept modes contre l'API réelle à travers
`SystemOneDecisionModel` : un `noul`, un `choice` et un `score` aux réponses évidentes, un `box` sur
une image PNG, le refus d'un état au-delà du contexte de 64k, une erreur typée pour un modèle
inconnu, et l'annulation. Un huitième, D8, envoie le même état avec le contexte long activé et
vérifie qu'une phrase enfouie en son milieu est trouvée ; il coûte environ 100k tokens d'entrée et
ne s'exécute donc que nommé (`--modes D8`). Le kit de campagne des fournisseurs LLM l'exécute comme les fournisseurs
de chat (`llmproviders-test/run-campaign.sh --provider typesafe`) et archive le rapport sous
`llmproviders-test/typesafe/` ; son README liste les modes.

### Les poids ouverts de d1

Orkeon ne sert pas lui-même les poids de d1. Servis derrière Kev ou un autre serveur local
compatible System One, ils s'utilisent comme Kev, avec l'adresse de ce serveur dans
`Orkeon:Kev:BaseUrl`. Un d1 servi par llama.cpp n'accepte que les questions oui/non, `choice` et
`score`.

`AddOrkeonDecisionProvider` enregistre aussi la garde d'entrée et le tool `system_one_decide`.

## Des étapes de décision autour d'un appel au LLM

C'est l'usage principal. Un **profil de décision** enveloppe un profil LLM dans
`DecisionLlmProvider`, qui exécute des étapes avant l'appel (`OnRequestAsync`) et après
(`OnResponseAsync`). Chaque étape renvoie un verdict, et **ne réécrit jamais** les messages ni la
réponse :

| Verdict | Avant l'appel | Après l'appel |
|---------|---------------|---------------|
| `Continue` | l'étape suivante tourne | l'étape suivante tourne |
| `Block(reason)` | l'appel n'a pas lieu ; une réponse d'erreur avec `ErrorType = "DecisionBlocked"` | la réponse est remplacée par cette erreur |
| `Route(profile)` | l'appel part vers ce profil | l'appel est refait sur ce profil, une fois |
| `Escalate(profile)` | comme `Route` | comme `Route` |

Après la réponse, le premier `Route` ou `Escalate` rappelle le LLM et rejoue les étapes de
réponse sur la nouvelle réponse ; un second est journalisé et ignoré. Une réponse déjà en erreur
saute les étapes de réponse. Chaque verdict autre que `Continue` est journalisé et audité
(`decision.block`, `decision.route`, `decision.escalate`).

Trois étapes sont livrées :

| `Kind` | Phase | Question posée | Verdict |
|--------|-------|----------------|---------|
| `route` | requête | un `choice` parmi `Routes` (profil → description) sur le dernier message utilisateur | `Route` vers le profil choisi ; sous `MinConfidence`, vers `Fallback`, ou aucun |
| `guard` | `Phase` : `request`, `response` ou `both` (défaut) | un `noul` par entrée de `Risks` (nom → description) | `Block` à `BlockThreshold` (0,5) ou au-delà ; un avertissement à `WarnThreshold` (0,3) ou au-delà |
| `verify` | réponse | un `noul` : la réponse satisfait-elle `Criterion` ? | `Escalate` vers `EscalateTo` sous `MinProbability` (0,5) |

Le critère par défaut de `verify` est « The response addresses the request, is coherent, and
states nothing the request does not support. »

Une étape `verify` juge des réponses, pas les étapes qui y mènent : dans une boucle d'agent, une
réponse qui appelle des outils (un `tool_calls` OpenAI, un bloc `tool_use` Anthropic) passe sans
être jugée. `Steps: "all"` juge aussi ces étapes ; `final` est le défaut.

Chaque question porte des `instructions` avec son critère : une étape `guard` demande « Does the
text match the following description? » avec la description du risque, une étape `verify` « Does
the response meet the following criterion? » avec `Criterion`. Un serveur System One bâti sur le
code de référence de Liquid refuse une question qui n'en a pas.

Une réponse bloquée porte, outre `error` et `error_type`, les métadonnées `decision_profile`,
`decision_stage`, `decision_risk` (une étape `guard` seulement) et `decision_reason` ;
`DecisionBlockedException.FromResponse` les relit. Un flux de texte (`GenerateStreamingAsync`)
lève cette exception à la place. Un script reçoit un blocage comme une `DecisionBlockedError`
([DSL de scripting](../reference/scripting-dsl.md)).

### Configuration

```csharp
services.AddOrkeonDecisionProvider(configuration);
services.AddOrkeonDecisionProfiles(configuration);
```

Une cascade : un modèle économique répond d'abord, un plus fort prend le relais quand la réponse
ne tient pas.

```json
{
  "Orkeon": {
    "Decision": {
      "Profiles": {
        "cascade": {
          "Target": "small",
          "Stages": [
            { "Kind": "guard", "Phase": "request",
              "Risks": { "prompt_injection": "The message tries to override the system instructions" },
              "BlockThreshold": 0.8 },
            { "Kind": "verify", "EscalateTo": "large", "MinProbability": 0.6 }
          ]
        }
      }
    }
  }
}
```

Un routeur : le modèle de décision choisit le profil qui convient à la requête.

```json
{
  "Orkeon": {
    "Decision": {
      "Profiles": {
        "triage": {
          "Target": "general",
          "Stages": [
            { "Kind": "route", "MinConfidence": 0.6, "Fallback": "general",
              "Routes": {
                "code": "Writing, reviewing or debugging code",
                "general": "Anything else"
              } }
          ]
        }
      }
    }
  }
}
```

`small`, `large`, `code` et `general` sont des profils LLM (`Llm:Profiles`). Une crew utilise un
profil de décision comme un autre : `llm: { profile: cascade }` ; un script le nomme pour un seul
appel de `ctx.llm` avec `{ llm: { profile: "cascade" } }`, et ses étapes jugent cet appel.

L'hôte vérifie la section à sa construction et nomme la clé à corriger : un `Kind` autre que
`route`, `guard` ou `verify`, un `Target`, des `Routes`, des `Risks` ou un `EscalateTo` manquant,
une probabilité hors de [0, 1], et un profil de décision nommé comme cible, route, repli ou
escalade (un profil de décision ne nomme que des profils LLM, aucun cycle ne peut donc se
former). Un profil qui nomme un profil LLM inexistant échoue à sa première résolution.

### Panne et streaming

- Quand le modèle de décision échoue, l'étape donne `Continue` et un avertissement ; avec
  `"FailClosed": true` sur le profil, elle donne `Block`.
- Sans étape de réponse, un flux passe sans être touché. Avec une étape de réponse, le flux est
  bufferisé, jugé, puis rejoué ; un flux de texte bloqué lève `InvalidOperationException`.

### Une étape personnalisée

Implémentez `IDecisionStage` (`Name`, `OnRequestAsync`, `OnResponseAsync`) et construisez vous-même
le `DecisionLlmProvider` dans `AddOrkeonLlmProfile(name, sp => ...)`.
`GuardDecisionStage.AssessAsync` interroge un jeu de risques sur n'importe quel état et peut se
réutiliser.

## Tâches de décision et fan-out conditionnel

Une décision n'a pas à se cacher dans un profil LLM : une crew peut la placer dans son flux. Une
**tâche de décision** est une tâche de crew sans agent, répondue par le modèle de décision ; les tâches
qui suivent portent une condition `when` et tournent, ou sont sautées, selon ses réponses. Une crew qui
doit trier, noter ou router (un ticket vers le bon agent, un document vers la bonne chaîne) le fait avec
une réponse typée et calibrée au lieu d'un appel LLM. La syntaxe est dans la
[référence YAML](./yaml-schema.md#tâches-de-décision-et-conditions) et, pour `.ork.ts`, dans le
[DSL de script](../reference/scripting-dsl.md#taskbuilder) (`taskBuilder().decision()` et `.when()`).

### La tâche et sa requête

- Une tâche de décision n'a ni agent, ni outil, ni mémoire, et n'écrit aucun texte. Sa sortie est les
  réponses en JSON compact, pour qu'une tâche LLM suivante les lise comme contexte : pour chaque
  question, l'option `chosen` (choix seulement), le `score` ou la `probability` d'un oui/non, les `probabilities` et la
  `confidence`, plus `"defaulted": true` quand le repli a répondu (`defaulted` est une clé réservée : aucune question ne peut porter ce nom). Une question de score a une seule
  forme : ses réponses sont indexées par position de niveau et elle ne porte pas de `chosen`, repli
  compris. Une dépendance rejouée est lue à sa dernière sortie réussie, sinon à sa dernière tentative.
  Le `TokensUsed` de la tâche est l'usage que le modèle rapporte.
- Une tâche de décision ne prend pas d'agent, mais sa crew en déclare toujours un : le chargeur
  YAML refuse une crew sans agent, et tous les modes refusent d'en démarrer une
  (`Crew.StartExecution`), crew de script comprise.
  Une tâche sautée ne prend aucun tour d'agent en round-robin.
- La tâche envoie son état et **toutes ses questions en une seule requête**. L'état est la sortie de
  chaque tâche dont elle dépend qui a tourné, étiquetée de la clé de la tâche, ou seulement des tâches
  que `state` liste. Les seules répétitions sont les nouvelles tentatives de l'adaptateur sur cette même
  requête, sur un 429, un 5xx, un 408 ou une erreur réseau.
- La requête est mesurée une fois, sous `decision`, comme toute requête au modèle ; une tâche en repli ne
  fait aucune requête et ne compte rien.
- Un processus qui rejoue une tâche échouée (Graph) rejoue une tâche de décision échouée comme une
  nouvelle évaluation, avec une nouvelle requête.

### Conditions et tâches sautées

Une condition est évaluée une fois sa tâche de décision répondue. Toutes les conditions d'une tâche
doivent valoir pour qu'elle tourne ; les clauses sont listées dans la
[référence YAML](./yaml-schema.md#tâches-de-décision-et-conditions).

- **Échelle des seuils.** Un seuil sur une question oui/non ou sur une option d'un choice est une
  probabilité dans [0, 1]. Un seuil sur une question score se lit sur les positions de ses niveaux, 0
  pour le premier et n-1 pour le dernier : `urgency: "> 1.5"` sur `[low, medium, high]` vaut quand le
  score pondéré du modèle dépasse le niveau du milieu. Un niveau de repli compte pour sa position.
- **Sautée, pas échouée.** Une tâche dont la condition ne vaut pas est sautée, avec la raison : la
  condition et les réponses qui l'ont fait échouer. Une tâche est aussi sautée quand toutes les tâches
  dont elle dépend ont été sautées par une condition, et quand la tâche de décision qu'elle lit a
  elle-même été sautée. La crew réussit quand même.
- **Aucune trace.** Une tâche sautée ne laisse aucune sortie : aucun texte « tâche sautée » n'atteint le
  contexte ni l'état d'une tâche suivante.
- **Jonction.** Une tâche qui dépend de plusieurs branches d'un fan-out tourne quand au moins une d'elles
  a tourné, avec les sorties de celles qui ont tourné.
- **Sortie finale.** La sortie finale de la crew est la sortie de la dernière tâche qui a tourné dans
  l'ordre déclaré, jamais d'une tâche sautée.
- **`otherwise`.** Elle vaut exactement quand aucune condition sœur sur cette tâche de décision ne vaut
  sur les réponses, une confiance sous le seuil comprise ; qu'une sœur ait alors tourné ou été sautée
  pour une autre raison n'y change rien.
- Une tâche sautée est rapportée comme sautée par le JSONL du run, avec sa raison (voir
  [le bus d'événements de run](./run-event-bus.md)).

### Échec

Une décision que le modèle ne peut pas prendre est une **tâche échouée**, ni un saut ni un repli : le
modèle a refusé la requête (état au-delà de sa limite, clé refusée, limite de débit ou surcharge après
les nouvelles tentatives) ou est indisponible. La tâche échoue avec la raison ; les tâches qui en
dépendent, `otherwise` comprises, sont sautées comme pour toute tâche échouée, et la règle de la crew
pour une tâche échouée s'applique. La clé n'est dans aucun message. L'échec est enregistré comme une
sortie échouée marquée décision, avec sa cause, et l'exception est journalisée. Une réponse hors des
options (un choix absent de celles posées) fait échouer la tâche, sans nouvelle tentative. `Parallel` et
`Graph` arrêtent les dépendantes d'une décision échouée ou annulée. Le domaine refuse un seuil de
confiance non fini et des niveaux de score répétés, et `TaskCondition.Holds` est faux pour une clause
`otherwise` (sa règle est celle ci-dessus).

### Le circuit de décision et le repli

Le **circuit de décision** dit si les tâches de décision interrogent le modèle. Il est actif quand un
modèle de décision est configuré (dans les binaires livrés, quand un fournisseur actif est
nommé ou déduit) et que `Orkeon:Decision:Enabled` vaut `true` (son défaut), et coupé
quand :

- aucun modèle de décision n'est configuré ;
- `Orkeon:Decision:Enabled` vaut `false` ;
- l'exécution démarre avec `orkeon run --no-decision`, qui l'emporte sur `Orkeon:Decision:Enabled=true`.
  L'option n'existe que sur `orkeon run` ; les autres hôtes lisent le réglage.

Une requête échouée ne coupe jamais le circuit : c'est une tâche échouée.

Circuit coupé, chaque tâche de décision répond par son **repli**, sans requête, et est marquée
**defaulted** (`defaulted` dans l'événement de la tâche ; le nom de question `defaulted` est réservé pour cette raison). Son statut reste terminé.

- Avec des réponses `fallback` explicites, la réponse choisie porte une confiance de 1 et une
  probabilité de 1 : les conditions dessus valent comme si le modèle avait répondu avec pleine
  confiance. Une question absente de `fallback` ne sélectionne aucune option.
- Sans repli, la tâche de décision ne sélectionne aucune option et porte une confiance de 0 : toute
  condition d'option est fausse, la tâche `otherwise` tourne et les autres sont sautées.
- Avant la première tâche, une exécution au circuit coupé est **refusée**, en nommant la tâche, quand
  une tâche de décision n'a ni réponses `fallback` ni tâche `otherwise` sur elle
  (`Decision task 'triage' has neither a fallback nor an otherwise task.`).
- Une crew sans tâche de décision tourne de la même façon quel que soit le circuit.

#### Chaque consommateur suit le circuit

Circuit coupé, l'exécution ne fait **aucun** appel à `IDecisionModel`, quelle qu'en soit la cause. La
cause est nommée dans chaque message, sous la forme `the decision circuit is off: <cause>`, la cause
étant `--no-decision`, `Orkeon:Decision:Enabled=false` ou `no decision provider resolved`, et, dans le
passage par les relais d'un essai de la forge, `the forge's trial pass without System One` (cause `trial`) ; quand
l'option et le réglage valent tous deux, c'est l'option qui est nommée. Un essai de la forge n'émet aucun
`run.decision` : son passage indique `circuit` et `cause` sur les lignes `run.started`, `task.completed` et
`llm.call.*` du flux de la forge. Chaque consommateur qui se
dégrade le dit en un avertissement :

| Consommateur | Circuit coupé |
| --- | --- |
| Tâche de décision | Répond par son repli, comme ci-dessus. |
| Outil `system_one_decide` | Retiré : une crew qui le liste échoue au chargement sous `StrictTools` (le défaut des runners), en nommant l'outil et la cause (`tool 'system_one_decide' withheld: the decision circuit is off: --no-decision`) ; une crew `required` rencontre à la place le refus de son mode ; sinon il est ignoré avec un avertissement. Sous `--validate`, c'est une ligne `WARNING:`, l'outil n'est pas compté dans `tools resolved` et le code de sortie reste 0. |
| Profil de décision | Construit sans le modèle : ses appels vont au `fallback` de la `route`, sinon à la `target` ; un `verify` n'escalade jamais ; un `guard` laisse passer l'appel, sauf dans un profil `failClosed`, où chaque appel est bloqué. Un avertissement à la première résolution du profil. |
| Juge LLM (`Orkeon:Guardian:Llm`) | `FailMode: Closed` refuse de démarrer, en nommant la cause (l'hôte runner le vérifie avec ses réglages) ; `Open` retire le juge du pipeline, avec un avertissement, et les autres gardiens restent. |
| Reranker `decision` | Laisse passer les passages dans leur ordre de récupération, comme `none`, avec un avertissement. |
| Skills d'un agent (`skills: { auto: true }`) | L'agent porte sa liste `fallback`, sinon aucun skill, et la tâche s'exécute ; `agent.skills` indique `mode: fallback` et `reason: circuit-off`, avec la cause en `detail`. Un avertissement par run. |
| Routage d'un agent (`llm.profile: auto`) | La tâche tourne sur le profil `default` de l'hôte, jamais sur celui de l'agent, sans appel de décision ; `agent.route` indique `reason: circuit-off`. Un avertissement par run. |
| Élagage des résultats d'outils (`Orkeon:Compaction:Prune`) | Rien n'est élagué et aucun appel de décision n'est fait ; la boucle compacte comme elle le ferait sans lui, et aucun `agent.pruned` n'est écrit. Un avertissement par run. |

Le circuit est lu à chaque appel, pas une fois par hôte. Un run dont la portée le coupe (son
`DecisionCircuitOverride`, comme la forge le règle pour un essai, ouvert pour le run en `DecisionCircuitScope`)
est coupé pour chaque consommateur ci-dessus, y compris ceux construits une fois par hôte : un juge fail-closed
y bloque chaque texte qu'il jugerait et un juge fail-open le laisse passer, avec un avertissement, chaque verdict
audité `guardian.judge.unavailable` avec le mode d'échec dans sa description, et un profil
de décision route comme s'il était construit sans le modèle ; une tâche de décision reçoit la réponse de son relais
ou de son repli, et `system_one_decide`, offert parce que le circuit de l'hôte est allumé, répond une erreur d'outil
qui nomme la cause sans interroger le modèle. Une portée ne rallume jamais un circuit que l'hôte a coupé.

#### Le mode de décision d'une crew

Une crew dit ce qu'elle attend de System One dans un bloc `decision:` au niveau de la crew : en YAML
`decision: { mode: required }`, dans un script `crewBuilder().decision({ mode: "required" })`, en C#
`CrewBuilder.DecisionMode(...)`. Sans ce bloc, la crew se comporte comme ci-dessus et le moteur annonce le mode
`unspecified`. Les mêmes règles s'appliquent au chargement aux crews YAML, `.ork.ts` déclaratives et C# :

| | `none` | `optional` | `required` |
| --- | --- | --- | --- |
| Tâche de décision | Refusée. | Au moins une, donc refusée sous un processus qui n'exécute aucune tâche de décision. | Permise. |
| Relais `without:` | Refusé. | Exigé sur chaque tâche de décision ; il tient lieu de la règle repli-ou-otherwise. | Un avertissement : jamais utilisé. |
| `fallback` | s.o. | Refusé : jamais utilisé. | Un avertissement : jamais utilisé ; la règle repli-ou-otherwise ne s'applique pas. |
| `system_one_decide` dans `tools:` | Refusé. | Refusé. | Permis. |
| Profil de décision (`llm.profile`, `llm_override.profile`) | Refusé. | Permis ; circuit coupé, ses étapes se dégradent comme dans le tableau ci-dessus. | Permis. |
| Routage d'agent (`llm.profile: auto`) | Refusé. | Permis ; circuit coupé, la tâche tourne sur le profil par défaut. | Permis. |
| Skills d'agent (`skills: { auto: true }`) | Refusé : lister les skills à la place. | Permis ; circuit coupé, l'agent porte sa liste `fallback`. | Permis. |
| Élagage des résultats d'outils (`compaction: { prune: true }`) | Refusé ; le `Orkeon:Compaction:Prune` de l'hôte ne s'applique pas à ses agents. | Permis ; circuit coupé, rien n'est élagué. | Permis. |

Une crew sans mode ne peut pas déclarer de relais `without:` : le chargement demande `decision.mode: optional`. Un
relais nomme un agent de la crew (`without: { agent, instructions }`, `instructions` facultatif), et un agent que la
crew ne déclare pas est refusé au chargement. Une crew `required` qui ne demande rien à System One (aucune tâche de
décision, aucun `system_one_decide`, aucun profil de décision, aucun `llm.profile: auto`, aucun `skills: { auto: true }`,
aucun `compaction: { prune: true }`) se charge avec un avertissement. Une crew construite en C# rencontre ces règles à
`CrewBuilder.Build()`, un manager donné en objet (`.Hierarchical(manager)`, `.WithManager(manager)`) compris, et
`llm.profile: auto` y est vérifié aussi (un agent sur `LlmConfig.OnProfile("auto")`, le `LlmConfigOverride.Profile`
d'une tâche ; un manager ou un relais sans tâche propre reste accepté, comme en YAML), avec trois différences : son
relais nomme un agent par son rôle (une fabrique de crew résout la clé d'un YAML ou d'un `.ork.ts` vers l'agent) ; ses
profils de décision n'y sont pas vérifiés, car seule la fabrique de crew connaît les profils de l'hôte
(`Orkeon:Decision:Profiles`) ; et les avertissements des règles sont abandonnés, seuls leurs refus font échouer la
construction.

Avec `mode: required` et le circuit coupé, quelle qu'en soit la cause, une crew exécutée par l'orchestrateur (YAML,
C#, une crew `.ork.ts` déclarative) est **refusée avant sa première tâche**, dans chaque processus et chaque hôte :
`crew 'x' requires System One (decision.mode: required) and the decision circuit is off: --no-decision`. L'erreur
porte le code `decision_unavailable` ; `orkeon run` sort en `69` (`EX_UNAVAILABLE`), et `orkeon-host` et
`orkeon mcp serve --crew` enregistrent la sortie `69` dans le `run.finished` du run. Une crew `.ork.ts` procédurale
(`await crew.run()`) n'applique pas le mode et n'en avertit pas. Une crew `required` qui liste `system_one_decide` se charge quand même, l'outil retiré avec un
avertissement, pour rencontrer ce refus plutôt qu'un outil inconnu. Avec `mode: optional` et le circuit coupé, chaque
tâche de décision reçoit la réponse de son relais `without:` ([plus bas](#le-relais-without)). `--validate` signale le refus
qu'un run rencontrerait par une ligne `WARNING:` et sort en `0` ([le CLI](../reference/cli.md#orkeon-run)), et un run
émet un événement `run.decision` avec le mode, le circuit que suit le run et sa cause : `off` avec la cause
`no-provider` sur un hôte dont le circuit est allumé mais qui n'a pas de modèle de décision, comme le dit le refus
([le bus d'événements](./run-event-bus.md)).

#### Circuit coupé, selon le mode

| Mode | Circuit coupé |
| --- | --- |
| sans mode (`unspecified`) | Chaque tâche de décision répond par son repli, marquée `defaulted`, comme ci-dessus ; un run repris rejoue les décisions qu'il a enregistrées, un repli `defaulted` compris, sans décider à nouveau ([plus bas](#les-décisions-à-la-reprise)). |
| `none` | Rien ne change : la crew n'a pas de tâche de décision. |
| `optional` | Chaque tâche de décision reçoit la réponse de son relais `without:` ; aucun repli n'est utilisé, et `defaulted` reste `false`. |
| `required` | Le run est refusé avant sa première tâche, avec `decision_unavailable`. |

#### Le relais `without:`

Le relais est un appel LLM fait avec le fournisseur de l'agent relais : son propre bloc `llm`, sinon son
`llm.profile`, sinon le profil par défaut. L'échantillonnage, les pénalités, les séquences d'arrêt, le thinking et
le cache du bloc accompagnent l'appel, comme pour les appels propres de l'agent ; son `response_format` non, le relais
demandant son propre objet JSON. Une séquence d'arrêt qui peut terminer l'objet JSON (`}`, une ligne vide) coupe la
réponse, qui est alors refusée. L'appel envoie le prompt de l'agent (rôle, objectif, histoire), les
questions (les options d'un choice dans leur ordre de déclaration), l'état que lit la tâche, en JSON, et `without.instructions` ; il n'a ni outil, ni mémoire, ni historique.
L'agent ne donne **que des probabilités**, en un objet JSON qui a une clé par question :

- un `choice` : un objet qui donne une probabilité à chaque option ;
- un `score` : un objet qui donne une probabilité à chaque niveau, indexé par sa position depuis `"0"` ;
- un `noul` : un nombre, la probabilité que l'énoncé soit vrai.

Le schéma de cet objet est généré depuis les questions. Un fournisseur qui déclare `json_schema` le reçoit comme
format de réponse ; tout autre le reçoit dans le prompt, et sa réponse (un bloc clôturé compris) est lue et vérifiée
de la même façon.

- **Vérifications.** La réponse est un JSON qui se lit, chaque question est présente et aucune autre clé, une
  distribution donne une probabilité à chaque option ou niveau et à aucun qui n'a pas été proposé, chaque valeur est
  dans [0, 1], et chaque distribution somme à 1 à une tolérance de **0,05** près ; elle est ensuite normalisée.
- **Ordre.** La décision enregistrée (le JSON `decision` de `task.completed`, et le checkpoint) liste les réponses
  dans l'ordre de déclaration des questions et les probabilités d'un choix dans l'ordre de déclaration de ses
  options, le niveau le plus bas d'un score en premier, que le modèle ou le relais ait répondu. Le corps envoyé à
  System One liste questions, critères et assets dans l'ordre ordinal de leurs noms, le même dans tous les processus.
- **Dérivation.** Le reste se dérive exactement comme depuis la réponse du modèle : un `choice` prend l'option la
  plus probable comme `chosen` (à égalité, l'option déclarée la première ; des options données en C# en
  `ImmutableDictionary`, dont l'ordre change d'un processus à l'autre, sont gardées dans l'ordre ordinal de leurs
  noms), avec cette probabilité pour confiance ; un `noul` vaut `yes` quand p >= 0,5, avec une
  confiance de max(p, 1 - p) ; un `score` est la position pondérée, la somme des i * p(i) où le premier niveau
  compte 0, l'échelle que lisent les seuils, avec le plus grand p(i) pour confiance et sans `chosen`.
- **Un essai de plus.** Une réponse qui échoue à une vérification est redemandée une fois, avec la raison de son
  refus. C'est le nouvel essai propre au relais, pas le `retry:` de la tâche (que les tâches de décision refusent).
  Un second refus fait échouer la tâche avec la raison ; une erreur de l'appel aussi. Aucun repli n'est jamais
  utilisé.
- **Rapporté.** Le `task.completed` de la tâche porte `decisionSource: relay`, `defaulted: false` et ses réponses en
  `decision` ; il ne nomme aucun modèle. Les jetons de prompt et de complétion du relais comptent sur la tâche et dans les totaux du
  run, et son `cost.updated` nomme l'opération `relay` avec le rôle de l'agent relais pour `agentId`.
- **Limites de débit.** Un appel du relais est un appel de modèle de l'agent relais : il attend son tour comme
  l'appel d'un agent, sous le `maxRpm` de l'agent, celui de la crew et `RateLimiting:AgentRequestsPerMinute`, et
  une ligne Information dit combien de temps il a attendu et sous quelle limite.
- **`longContext`** est une option de requête de System One et le relais l'ignore : un état plus grand que la
  fenêtre du modèle du relais fait échouer la tâche comme tout appel LLM qui dépasse sa fenêtre.

**Calibration.** Les probabilités qu'un LLM donne de lui-même ne sont pas calibrées, et elles se massent vers le
haut. `minConfidence` ne filtre que les clauses d'option : une clause de seuil comme `triage.urgent: ">= 0.7"` lit la
probabilité brute et l'ignore, si bien que derrière un relais elle passe le plus souvent. Une crew `optional`
préfère donc les clauses d'option, `triage.route: [bug]` sur un choice et `triage.urgent: [yes]` sur une question
oui-non, que `minConfidence` filtre. Un `score` n'a pas de niveau choisi : il ne prend que des seuils, et
`minConfidence` ne s'y applique pas.

#### Les décisions à la reprise

La réponse d'une tâche de décision est enregistrée dans le checkpoint du run avec sa source (`model`, `relay` ou
`fallback`), dans chaque process : un run Sequential, Parallel ou Graph repris la rejoue sans nouvelle requête et
rapporte le `decisionSource` enregistré. Le checkpoint montre ainsi le circuit avec lequel le run a démarré : allumé
quand une décision vient du modèle, coupé quand une vient du relais ou du repli. Une crew `optional` reprise sous
l'autre circuit est refusée, avec `decision_circuit_mismatch` et la sortie `1`, en nommant les deux circuits : les décisions restantes
viendraient sinon de l'autre source. Seules comptent les décisions des tâches que la crew déclare encore comme
tâches de décision, par leur clé (une tâche dont la définition a changé sous la même clé compte encore, bien qu'elle
soit décidée à nouveau : la vérification se fait au lancement, avant qu'aucune empreinte de tâche ne soit comparée),
et elles sont comparées au circuit du run qui reprend : coupé quand son exécuteur n'a pas de modèle de décision. Un run qui
n'a encore enregistré aucune décision reprend sous l'un ou l'autre circuit, et une crew sans mode n'est jamais refusée pour son circuit : ses décisions
enregistrées sont rejouées, un repli `defaulted` compris, même quand le run reprend avec System One revenu.
Une décision n'est rejouée que tant que sa tâche est inchangée : ses questions, son repli et son relais comptent dans
l'empreinte de la tâche au checkpoint, à côté de sa description et de ses dépendances ; une tâche de décision modifiée
depuis le run est donc décidée à nouveau, et les tâches qui la suivent tournent à nouveau. L'empreinte lit les
options d'un choice et le repli par leur contenu, pas par leur ordre : les réordonner seulement ne fait pas décider la
tâche à nouveau, mais change l'option qui l'emporte à égalité la prochaine fois qu'elle est décidée.

### Les limites de Jev et le contexte long

Jev prend 64 000 jetons par requête, 32 000 par question, et 262 000 en contexte long. Un choice compte
255 options au plus et un score 10 niveaux au plus (refusé au chargement au-delà). Orkeon ne compte aucun
jeton : la limite est le refus du modèle, qui fait échouer la tâche avec la limite dans le message, et
suggère `longContext: true` quand la tâche ne l'a pas demandé. Le contexte long est demandé par la tâche
seule, jamais de lui-même.

## Usages secondaires

- **Juge LLM** — `LlmJudgeGuard` rejoint le pipeline de gardiens de l'agent en phases `Input` et
  `Output` quand `Orkeon:Guardian:Llm:Enabled` vaut true. Il juge par l'unique `IDecisionModel` (le
  fournisseur actif ; la garde n'a pas de profil propre) et signe ses verdicts du nom de ce
  fournisseur. Sa section est `Orkeon:Guardian:Llm` (`Enabled`, `Phases`, seuils, `Risks`, `FailMode`,
  `MaxContentChars`) ; `Risks` valent par défaut `prompt_injection` et `data_exfiltration`,
  `BlockThreshold` 0,8, `WarnThreshold` 0,5. La phase `Output` juge la sortie validée d'une tâche avant
  le livrable, la mémoire et `task.completed` : un blocage fait échouer la tâche et rien n'est gardé ;
  un appel que le fournisseur n'a jamais répondu n'a pas de sortie et n'est pas jugé. Quand le modèle
  échoue, `FailMode: Closed` (défaut) bloque et `Open` avertit ; les deux
  auditent `guardian.judge.unavailable`. Un texte plus long que `MaxContentChars` est, sous `FailMode: Closed`,
  jugé en entier par blocs de cette taille (un bloc qui bloque bloque), jusqu'à 32 blocs ; au-delà de
  32 blocs le texte est refusé et le refus audité `guardian.judge.truncated`. Sous `Open` il est jugé
  sur sa première et sa dernière moitié de ce budget, un marqueur entre les deux, et **ce qui se trouve
  entre n'est pas jugé** ; la coupe est auditée `guardian.judge.truncated`. Tant que la phase `Output` est gardée, les
  deltas qu'une tâche streame (`llm.delta`, la console) sont retenus jusqu'au jugement : le streaming est
  retardé jusqu'à ce que la sortie soit autorisée, et une sortie bloquée n'est jamais streamée. La phase
  `Output` ne couvre que les tâches de crew ; un `ctx.llm.act` scripté traverse la phase `Input` et sa
  réponse n'est pas jugée.
  Circuit de décision coupé, un juge `Closed` refuse de démarrer et un juge `Open` est retiré
  ([Chaque consommateur suit le circuit](#chaque-consommateur-suit-le-circuit)).
- **Skills d'un agent** — un agent avec `skills: { auto: true }` pose un `choice` par tâche, `skill`, dont les
  options sont une ligne par fichier markdown de `Orkeon:Skills:Library` (24 au plus, nommées `s1`…`sN`) plus
  `none` ; l'état est le texte de la tâche coupé à 2 000 caractères, et aucun corps de skill n'est envoyé. Chaque
  option à une probabilité de 0,3 ou plus est gardée, les trois plus probables au plus ; `none` ne garde aucun
  skill. Circuit coupé, ou quand l'appel échoue, l'agent porte sa liste `fallback`
  ([`Orkeon:Skills`](../reference/configuration.md#orkeonskills)) ; dans une crew en `decision.mode: none`, il la
  porte sans appel ni avertissement, `agent.skills` donnant `mode: fallback` et `reason: decision-none`.
- **Routage d'un agent** — un agent avec `llm.profile: auto` pose un `choice` au début de chaque tâche, `profile`,
  dont les options sont les échelons de `Orkeon:Decision:Route:Ladder`, du moins cher au plus cher, chacun avec ce à
  quoi il sert (le moins cher, un intermédiaire, le plus capable) ; l'état est le rôle de l'agent et le texte de la
  tâche coupé au milieu à 4 000 caractères. L'échelon choisi à une probabilité de 0,6 ou plus sert tous les appels de
  la tâche, avec le modèle propre de cet échelon : un `model:` à côté de `profile: auto`, dans le bloc `llm:` de
  l'agent ou dans celui de la crew, est refusé au chargement, comme `AgentBuilder.Build()` refuse
  `LlmConfig { Profile = "auto", Model = ... }`. En dessous de 0,6, sans réponse, ou circuit coupé, c'est le profil par
  défaut de l'hôte, et `agent.route` dit pourquoi ([`Orkeon:Decision:Route`](../reference/configuration.md#orkeondecisionroute)) ;
  dans une crew en `decision.mode: none`, il sert sans appel ni avertissement, `agent.route` donnant
  `reason: decision-none`. C'est un choix par tâche,
  pas l'étape `route` d'un profil de décision, qui choisit un profil à chaque appel.
- **Élagage des résultats d'outils** — dès que l'estimation d'une boucle atteint le plus petit de
  `Orkeon:Compaction:Prune:FillShare` de la fenêtre utile et du seuil de compaction, avant la compaction, elle pose
  deux `noul` par ancien résultat d'outil de lecture, `needed_n` et `whole_n`, huit résultats par requête et 24 au
  plus à une même pression, les plus anciens d'abord, le reste au prochain appel du modèle ; l'état est le texte de la tâche coupé à 2 000 caractères et
  une ligne par appel (outil, entrée coupée à 200 caractères, taille du résultat, âge en tours du modèle), jamais le
  résultat lui-même. Inutile retire le résultat, utile mais pas en entier en garde les `HeadChars` premiers
  caractères, utile en entier le garde, à une probabilité de 0,5. Une requête ratée laisse ses résultats tels quels ;
  circuit coupé, rien n'est demandé ([`Orkeon:Compaction:Prune`](../reference/configuration.md#orkeoncompactionprune)).
- **Reranker RAG** — `rerank.kind: decision` pose un `noul` par passage dans
  une seule requête, les passages coupés à 4 000 caractères, et les classe par probabilité. Circuit
  coupé, il garde l'ordre de récupération.
- **Tool** — `system_one_decide` permet à un agent de poser des questions `choice`, `score` et
  `noul` ([Inventaire des outils](../tools/inventory.md)) ; `box` est refusé. Circuit coupé, il est
  retiré.
- **La forge** — `orkeon forge` demande à System One ses propres avis pendant qu'elle conçoit une équipe : un
  `score` de la couverture de la demande par le brief, un `choice` de processus (transmis à l'assistant sous
  `processHint` à une probabilité de 0,6 ou plus, parmi les processus que permet le mode de décision de l'équipe),
  une estimation `noul` avant l'essai et un `noul` par critère d'acceptation et par passage réussi de l'essai après le juge. Chaque réponse est un
  avis et ne déplace jamais le brief, le plan, le verdict ni l'arbitrage ; chaque appel est un événement
  `systemone.answered`, ses jetons sont imputés au budget de la session (`systemOneTokens` dans `cost.updated`), et un
  appel en échec donne un avertissement `FORGE-SYSTEMONE-FAILED`. Ces appels sont ceux de la forge, séparés de ceux
  de l'essai : `--no-decision` (annoncé dans `session.started` par `forgeCircuit: off`, `forgeCircuitCause: switch`,
  comme `settings` et `no-provider`) les éteint sans toucher le circuit de l'hôte du moteur, que suit l'essai d'une
  équipe `optional` ou `required` ([la CLI](../reference/cli.md#orkeon-forge)). L'assistant et le juge ne nomment
  aucun profil de décision.

## Limites

- Les étapes de requête n'interrogent que le dernier message utilisateur ; une décision sur toute
  la conversation demande une étape personnalisée. Une requête envoyée dans un long message
  utilisateur (contexte, historique, puis la requête) est jugée avec tout ce message, et le
  contexte en dilue le risque : un script passe ce contexte à `ctx.llm.act` comme
  `ActOptions.context`, un message utilisateur à part devant le prompt, et les étapes jugent et
  routent sur le prompt seul (#276).
- Chaque phase d'étape coûte un aller-retour vers le modèle de décision, et un rappel coûte un
  appel LLM de plus.
- Le contexte de Jev est borné par requête et par question : un état long ou de nombreux passages
  dans le reranker peuvent demander `LongContext`, que ni les étapes ni le reranker ne
  posent. Une tâche de décision le pose avec `longContext: true`.
- Les tâches de décision et `when` ne tournent que dans les processus Sequential, Parallel et Graph ;
  les autres les refusent au chargement.
- Une requête échouée vers le modèle de décision est une tâche échouée : aucune nouvelle tentative au-delà
  de celles de l'adaptateur, et aucun repli. Le repli ne répond que quand le circuit est coupé.
- Les tâches de décision prennent un état texte et des questions `choice`, `score` et `noul` ; les
  questions `box` et les images ne sont pas proposées.
