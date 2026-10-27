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
  posent.
