> 🇬🇧 [English version](../../architecture/decision-models.md)

> **Voir aussi** : [Fournisseurs de modèles](./model-providers.md) · [Fournisseurs LLM](./llm-providers.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Retour à l'index](../INDEX.md)

# Modèles de décision

Un modèle de décision répond à des questions typées sur un état, avec des probabilités calibrées,
et n'écrit aucun texte. Orkeon l'atteint par le port `IDecisionModel` (`Orkeon.Application`) ; le
premier adaptateur est **TypeSafe**, qui sert le modèle « System One » **Jev** (`jev-latest`). Le
modèle juge et le code d'Orkeon agit : il bloque, route ou escalade un appel, classe des
passages, ou remet une décision à un agent
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

## Le port

`IDecisionModel.DecideAsync(DecisionRequest, ct)` envoie une requête et renvoie une
`DecisionResponse` ; `ListModelsAsync(ct)` liste les modèles que sert le fournisseur.

Une `DecisionRequest` porte :

- `state` : le JSON sur lequel portent les questions (un texte, un objet, un document) ;
- `questions` : un dictionnaire d'un nom de votre choix vers une question ;
- `assets` (facultatif, des images nommées), `model` (facultatif, sinon le défaut configuré) et
  `LongContext` (non sérialisé : il pose l'en-tête de contexte long).

| Question | Ce que vous donnez | Ce que vous recevez |
|----------|--------------------|---------------------|
| `choice` | au moins deux options nommées, chacune avec sa description | l'option choisie et la probabilité de chacune |
| `score` | au moins deux niveaux d'une échelle ordonnée | le niveau et la probabilité de chacun |
| `noul` | `criteria.true` et `criteria.false` | `noul`, la probabilité de vrai |
| `box` | des images dans `assets` | une zone dans une image |

Une réponse `choice`, `score` ou `box` porte aussi une `confidence`. Un `DecisionRequestValidator` refuse un jeu de questions
vide, un `choice` ou un `score` de moins de deux critères, et une `box` sans images.

## TypeSafe

```csharp
services.AddOrkeonTypeSafe(configuration);
```

```json
{
  "Orkeon": {
    "TypeSafe": {
      "Model": "jev-latest",
      "TimeoutSeconds": 30,
      "MaxRetries": 3
    }
  }
}
```

- **La clé** se lit dans `Orkeon:TypeSafe:ApiKey`, puis dans la variable que nomme `ApiKeyEnvVar`
  (`TYPESAFE_API_KEY` par défaut), dans le processus puis dans la portée utilisateur. Elle
  n'apparaît jamais dans un log, une erreur ou un appel de tool.
- **`BaseUrl`** vaut par défaut `https://api.typesafe.ai/v1` ; les requêtes vont vers
  `POST <BaseUrl>/systemone` et `GET <BaseUrl>/models`.
- **Les erreurs** deviennent une `DecisionModelException` : un 422 cite le champ fautif, un 401
  nomme la variable de clé, un 413 ou un 415 dit ce que la requête a dépassé. Un 429 ou un 529 est
  réessayé avec un backoff exponentiel, jusqu'à `MaxRetries` fois ; chaque tentative a son propre
  `TimeoutSeconds`.
- **Les en-têtes « danger zone »** se posent selon la requête : `TypeSafe-Danger-Zone-Images`
  quand elle porte des images ou une question `box`, `TypeSafe-Danger-Zone-Long-Context` quand
  `LongContext` est posé.
- **L'usage** est compté une seule fois, comme `decision` (`LlmUsageOperations.Decision`) :
  TypeSafe ne facture que les tokens d'entrée.

`AddOrkeonTypeSafe` enregistre aussi la garde d'entrée et le tool `system_one_decide`.

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

### Configuration

```csharp
services.AddOrkeonTypeSafe(configuration);
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
profil de décision comme un autre : `llm: { profile: cascade }`.

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
  question, l'option `chosen`, le `score` ou la `probability` d'un oui/non, les `probabilities` et la
  `confidence`, plus `"defaulted": true` quand le repli a répondu.
- La tâche envoie son état et **toutes ses questions en une seule requête**. L'état est la sortie de
  chaque tâche dont elle dépend qui a tourné, étiquetée de la clé de la tâche, ou seulement des tâches
  que `state` liste. Les seules répétitions sont les nouvelles tentatives de l'adaptateur sur cette même
  requête, sur un 429 ou un 529.
- La requête est mesurée une fois, sous `decision`, comme toute requête au modèle ; une tâche en repli ne
  fait aucune requête et ne compte rien.
- Un processus qui rejoue une tâche échouée (Graph) rejoue une tâche de décision échouée comme une
  nouvelle évaluation, avec une nouvelle requête.

### Conditions et tâches sautées

Une condition est évaluée une fois sa tâche de décision répondue. Toutes les conditions d'une tâche
doivent valoir pour qu'elle tourne ; les clauses sont listées dans la
[référence YAML](./yaml-schema.md#tâches-de-décision-et-conditions).

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
- Une tâche sautée est rapportée par les consoles comme sautée, avec sa raison (voir
  [le bus d'événements de run](./run-event-bus.md)).

### Échec

Une décision que le modèle ne peut pas prendre est une **tâche échouée**, ni un saut ni un repli : le
modèle a refusé la requête (état au-delà de sa limite, clé refusée, limite de débit ou surcharge après
les nouvelles tentatives) ou est indisponible. La tâche échoue avec la raison ; les tâches qui en
dépendent, `otherwise` comprises, sont sautées comme pour toute tâche échouée, et la règle de la crew
pour une tâche échouée s'applique. La clé n'est dans aucun message.

### Le circuit de décision et le repli

Le **circuit de décision** dit si les tâches de décision interrogent le modèle. Il est actif quand un
modèle de décision est configuré et que `Orkeon:Decision:Enabled` vaut `true` (son défaut), et coupé
quand :

- aucun modèle de décision n'est configuré ;
- `Orkeon:Decision:Enabled` vaut `false` ;
- l'exécution démarre avec `orkeon run --no-decision`, qui l'emporte sur `Orkeon:Decision:Enabled=true`.
  L'option n'existe que sur `orkeon run` ; les autres hôtes lisent le réglage.

Une requête échouée ne coupe jamais le circuit : c'est une tâche échouée.

Circuit coupé, chaque tâche de décision répond par son **repli**, sans requête, et est marquée
**defaulted** (`defaulted` dans l'événement de la tâche). Son statut reste terminé.

- Avec des réponses `fallback` explicites, la réponse choisie porte une confiance de 1 et une
  probabilité de 1 : les conditions dessus valent comme si le modèle avait répondu avec pleine
  confiance. Une question absente de `fallback` ne sélectionne aucune option.
- Sans repli, la tâche de décision ne sélectionne aucune option et porte une confiance de 0 : toute
  condition d'option est fausse, la tâche `otherwise` tourne et les autres sont sautées.
- Avant la première tâche, une exécution au circuit coupé est **refusée**, en nommant la tâche, quand
  une tâche de décision n'a ni réponses `fallback` ni tâche `otherwise` sur elle
  (`Decision task 'triage' has neither a fallback nor an otherwise task.`).
- Une crew sans tâche de décision tourne exactement comme avant, quel que soit le circuit.

### Les limites de Jev et le contexte long

Jev prend 64 000 jetons par requête, 32 000 par question, et 262 000 en contexte long. Un choice compte
255 options au plus et un score 10 niveaux au plus (refusé au chargement au-delà). Orkeon ne compte aucun
jeton : la limite est le refus du modèle, qui fait échouer la tâche avec la limite dans le message, et
suggère `longContext: true` quand la tâche ne l'a pas demandé. Le contexte long est demandé par la tâche
seule, jamais de lui-même.

## Usages secondaires

- **Garde d'entrée** — `TypeSafeInputGuard` rejoint le pipeline de gardiens de l'agent en phase
  `Input` quand `Orkeon:TypeSafe:Guard:Enabled` vaut true. Ses `Risks` valent par défaut
  `prompt_injection` et `data_exfiltration`, `BlockThreshold` 0,8, `WarnThreshold` 0,5 ; elle
  avertit sur une panne, ou bloque avec `FailClosed`.
- **Reranker RAG** — `rerank.kind: decision` (alias `typesafe`) pose un `noul` par passage dans
  une seule requête, les passages coupés à 4 000 caractères, et les classe par probabilité.
- **Tool** — `system_one_decide` permet à un agent de poser des questions `choice`, `score` et
  `noul` ([Inventaire des outils](../tools/inventory.md)) ; `box` est refusé.

## Limites

- Les étapes de requête n'interrogent que le dernier message utilisateur ; une décision sur toute
  la conversation demande une étape personnalisée.
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
