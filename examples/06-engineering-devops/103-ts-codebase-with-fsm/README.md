# 103. Analyse de Codebase TypeScript avec FSM et Circuit Breaker

> Extension de l'exemple 102 avec orchestration par machine à états finis (FSM) et circuit breaker intégré pour la stabilité production.

## Ce que cet exemple demontre

- **FSM generique** `StateMachine<TState, TEvent>` pour le cycle de vie des taches
- **Circuit breaker** avec 4 mecanismes : max transitions, timeout par etat, detection de cycles, duree totale max
- **Mode degrade** : la tache passe en etat `Degraded` au lieu de crasher quand le circuit trip
- **Configuration YAML a deux niveaux** : defauts crew + override par tache
- **Guards types** : budget de tool calls, limite de retries, validation de tool enregistre

## Architecture FSM

```
                    StartPlanning        BeginExecution
  [Assigned] ─────────────────► [Planning] ──────────► [Executing]
       │                                                   │  ▲
       │ BeginExecution                    ToolCallCompleted│  │RequestToolCall
       └───────────────────────────► [Executing] ◄─────────┘  │ (guard: budget)
                                        │    │                 ▼
                            SubmitFor   │    │           [ToolCalling]
                            Validation  │    │Fail            │
                                        ▼    ▼                │ToolCallFailed
                                  [Validating] [Failed]◄──────┘
                                   │       │     │
                        Passed     │       │     │ Retry (guard: maxRetries)
                                   ▼       │     └────────► [Executing]
                              [Completed]  │ Failed
                                           ▼
                                       [Failed] ──Cancel──► [Cancelled]

  Circuit breaker trip ──────────────────────────────────► [Degraded]
```

## Nouveau bloc YAML : `circuitBreaker`

### Au niveau crew (defauts pour toutes les taches)

```yaml
circuitBreaker:
  preset: "strict"               # "strict" | "permissive" | "default"
  useDegradedMode: true          # Degraded au lieu de crash
  maxRetries: 3                  # retries apres echec
  maxToolCallsPerRound: 10       # tool calls max par round
  maxValidationRetries: 3        # boucles validation max
```

### Au niveau task (override)

```yaml
tasks:
  my_task:
    description: "..."
    circuitBreaker:
      maxTransitions: 200        # surcharge le preset
      stateTimeoutSeconds: 600   # 10 min par etat
      maxStateVisits: 20         # detection de cycles plus permissive
```

### Presets disponibles

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `strict` | 50 | 2 min | 5 | 10 min | true |
| `default` | 100 | 5 min | 10 | 30 min | false |
| `permissive` | 1000 | 30 min | 50 | 2 h | false |

### Hierarchie de resolution

```
1. Task-level circuitBreaker (priorite haute)
2. Crew-level circuitBreaker (defaut)
3. Preset built-in "strict" (fallback si rien n'est configure)
```

## Run

```bash
orkeon run examples/06-engineering-devops/103-ts-codebase-with-fsm/config.yaml \
  --mount /home/you/my-ts-project:/src:ro /home/you/analysis-output:/output:rw
```

**Profil LLM** — ajoutez `--settings examples/appsettings/appsettings.deepseek.local.json` pour choisir explicitement un profil. Les profils prêts à l'emploi sont dans [`examples/appsettings/`](../../appsettings/README.md) : copiez un gabarit `*.example` (retirez le suffixe `.example`) et ajoutez votre clé. Première fois ? Voir [Lancer votre premier exemple](../../../docs/getting-started/run-your-first-example.md).

**Données** — cet exemple ne fournit pas de données d'exemple : il analyse le projet TypeScript que vous montez via `--mount` ci-dessus (voir [docs/reference/example-data-policy.md](../../../docs/reference/example-data-policy.md)).

## Differences avec l'exemple 102

| Aspect | 102 (sans FSM) | 103 (avec FSM) |
|--------|---------------|----------------|
| Orchestration | Switch sur ProcessType | FSM avec etats explicites |
| Protection boucles | Aucune (maxIter seulement) | Circuit breaker 4 mecanismes |
| Echec catastrophique | Exception non rattrapee | Mode degrade avec sortie propre |
| Observabilite | Logs textuels | Events `OnTransition` + `OnCircuitBroken` + histogramme |
| Config | maxIter/maxRpm | circuitBreaker avec preset + overrides |
| Tool calls | Pas de limite | maxToolCallsPerRound (anti-hallucination) |
| Validation loops | Pas de limite | maxValidationRetries |

## Fichiers source cles

| Fichier | Role |
|---------|------|
| `Orkeon.Domain/Common/StateMachine/StateMachine.cs` | Moteur FSM generique |
| `Orkeon.Domain/Common/StateMachine/CircuitBreakerPolicy.cs` | Configuration circuit breaker |
| `Orkeon.Domain/Common/StateMachine/StateMachineBuilder.cs` | API fluent pour le graphe |
| `Orkeon.Domain/Task/TaskExecutionStateMachine.cs` | Specialisation Task avec guards |
| `Orkeon.Domain/Configuration/CircuitBreakerConfig.cs` | DTO de config domain |
| `Orkeon.Infrastructure/Configuration/CircuitBreakerPolicyFactory.cs` | YAML → Policy → FSM |
| `Orkeon.Infrastructure/Configuration/YamlCrewDefinitionLoader.cs` | Parsing YAML circuitBreaker |
