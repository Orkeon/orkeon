> 🇬🇧 [English version](../../adr/ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md)

> **Voir aussi** : [Orchestration en graphe](../orchestration/graph.md) · [Schéma YAML](../architecture/yaml-schema.md) · [DSL de scripting](../reference/scripting-dsl.md) · [ADR-016](./ADR-016-decision-tasks-and-conditional-fan-out.md) · [Retour à l'index](../INDEX.md)

# ADR-018 — Deux moteurs de graphe, une validation de topologie et un vocabulaire de budgets partagés, et un bloc YAML `graph:`

**Statut** : Accepté · **Date** : 2026-11-07
· **Périmètre** : `Orkeon.Domain` (`Graph/StateGraph.cs`, `GraphRunner.cs`, `Configuration/GraphConfig.cs`), `Orkeon.Infrastructure` (`Crew/Strategies/GraphProcessStrategy.cs`, `Configuration/Yaml/`), `Orkeon.Scripting` (`Orchestration/JsStateGraph.cs`, `Bindings/StateGraphBinding.cs`, `Typings/graph.d.ts`). Cet ADR fixe la sémantique et ne livre aucun code ; le travail qui l'applique est FWK-27 (validation, graphe implicite, `JsStateGraph`) et FWK-28 (le bloc YAML, l'export, Studio).

## Contexte

Deux boucles de graphe existent.

1. **Le moteur du Domain** (`StateGraph<TState>` compilé en `GraphRunner<TState>`) a les sentinelles `__start__` et `__end__`, des arêtes fixes et conditionnelles, et des cycles bornés par une `CircuitBreakerPolicy` : `MaxTransitions` (un nœud ne s'exécute que tant que le nombre de nœuds déjà exécutés est inférieur), `MaxStateVisits` (contrôlé par nœud, déclenchement quand ses visites dépassent la borne) et `MaxTotalDuration`. `AddEdge` et `AddConditionalEdge` refusent une seconde arête sortante depuis un nœud. `Compile()` ne vérifie que trois choses : START a une arête, chaque nœud a une arête sortante, la cible d'une arête fixe existe. Il ne détecte ni un nœud que rien n'atteint, ni un graphe sans chemin vers END, et une arête conditionnelle est contrôlée à l'exécution contre ses cibles déclarées.
2. **`JsStateGraph`** tient sa propre boucle en JavaScript (un trampoline) sur un état `JsValue` quelconque, avec les sentinelles `__START__` et `__END__`. Sa table d'arêtes est indexée par nœud source : une arête par nœud, par construction. `stateGraph(...)` rejette un littéral sans arête depuis START, une arête chaîne vers un nœud non déclaré, et un graphe sans chemin statique de START à END (une arête fonction est supposée atteindre END). Ses budgets sont `maxTransitions`, `maxStateVisits` et `maxRetryCycles` : ce dernier compte, sur tout le run, les entrées dans un nœud déjà entré une fois, pas les visites d'un nœud.
3. **Le process Graph n'utilise pas la forme du moteur.** `GraphProcessStrategy.BuildCrewGraph` construit `execute_task` → `route` → (`execute_task` ou END) : un seul nœud qui boucle sur une file de tâches. Le YAML d'une équipe n'a pas de bloc `graph:`, seulement `graphConfig:` (`maxRetryCycles`, `circuitBreakerPreset`, `maxTransitions`, `maxStateVisits`, `maxTotalDurationSeconds`), et son `maxRetryCycles` est un retry de tâche échouée, pas une borne de cycles.
4. **Un moteur unique est impossible.** SCR-25 T1 a retiré la boucle CLR qui attendait des fonctions JavaScript : Jint n'a qu'un seul drainer, et une boucle CLR se bloquait après le premier `await` jusqu'à son délai de 10 s. La boucle JavaScript doit rester en JavaScript.

## Décision

1. **Deux boucles restent.** Le moteur du Domain et la boucle JavaScript ne sont pas fusionnés. Ils partagent une **validation de topologie** (`GraphTopologyValidator`, dans `Orkeon.Domain`, FWK-27), un **vocabulaire** (point 2) et un **sens des budgets** (point 4).
2. **Vocabulaire.** Un graphe a des nœuds (un id et un corps), un nœud de départ, des arêtes (`from`, `to`) et la sortie `END`. Les chaînes sentinelles du Domain et du script diffèrent (`__start__`/`__end__`, `__START__`/`__END__`) et restent telles quelles ; elles ne sont pas une surface du YAML, où le nœud de départ se nomme par `start:` et la sortie est le mot réservé `END`. `START` et `END` sont refusés comme id de nœud.
3. **Le validateur** prend une description neutre (ids de nœuds, départ, arêtes `from` vers cibles, chacune marquée fixe ou conditionnelle) et rend toutes les fautes, pas la première. Il vérifie :
   1. un id de nœud est unique, non vide et différent de `START`/`END` ;
   2. `start` nomme un nœud ;
   3. une arête part d'un nœud déclaré et arrive à un nœud déclaré ou à `END` (un id inconnu est nommé, avec les ids qui existent) ;
   4. un nœud a une arête sortante : rien ne doit pendre à l'exécution ;
   5. un nœud que START n'atteint pas en suivant toutes les arêtes est orphelin ;
   6. END est atteignable depuis START, et tout nœud atteignable depuis START peut atteindre END (une arête conditionnelle compte pour toutes ses cibles déclarées ; l'arête fonction d'un script, dont les cibles sont inconnues, est supposée atteindre END, comme `stateGraph` le fait aujourd'hui ; une arête conditionnelle du Domain déclarée sans `possibleTargets` est traitée de même). Les textes `has no path from START to END` et les fautes de nœud `ghost` des moteurs actuels sont conservés, de sorte que leurs tests tiennent ;
   7. un nœud a au plus une arête sortante fixe, et une arête fixe exclut toute autre depuis le même nœud. Les arêtes d'un nœud qui portent des conditions forment une seule arête conditionnelle (point 6).

   Le moteur du Domain l'appelle depuis `Compile()` (ses trois contrôles actuels font partie de cette liste), le mapper YAML l'appelle au chargement, `JsStateGraph` l'appelle à la construction (il remplace `ValidateEdgeTargets` et `ValidateReachability`), et l'export l'appelle avant d'écrire. Une faute remonte comme l'exception de la couche appelante (`InvalidOperationException` pour le Domain, l'erreur de chargement de `CrewDefinitionValidator` pour le YAML, `InvalidScriptException` pour un script) avec le même texte.
4. **Budgets : ceux du Domain.** `maxTransitions` et `maxVisits` (le `MaxStateVisits` du Domain) sont les deux bornes ; `maxTotalDurationSeconds` et `circuitBreakerPreset` restent. Les deux moteurs comptent de la même façon : un run exécute au plus `maxTransitions` nœuds, et un nœud entré plus de `maxVisits` fois déclenche le disjoncteur. Dans un script, `maxStateVisits` est déjà ce budget. `maxRetryCycles` est documenté comme le compte, sur tout le run, des revisites du script : il vaut `maxVisits - 1` pour un graphe qui boucle sur un seul nœud et c'est une borne globale différente pour plusieurs. Il reste dans `graph.d.ts`, inchangé, et un test de parité fixe le cas d'un nœud sur les deux moteurs. Les défauts diffèrent aujourd'hui et restent distincts : `StateGraph` seul prend la politique Strict (50 transitions, 5 visites), une `CircuitBreakerPolicy` nue vaut 100 et 10, un script 200 transitions sans borne de visites sans préréglage nommé. Pour une équipe, les défauts de `graph.maxVisits` et `graph.maxTransitions` gardent la règle de `GraphProcessStrategy.ResolvePolicy`, dimensionnés sur l'équipe : visites = nœuds × (1 + tentatives de retry), transitions = 2 × visites + 1, les tentatives du `retry:` des tâches y comptant ; une valeur explicite l'emporte. Une équipe de plus de 49 tâches n'est donc pas plafonnée par les chiffres de Strict. FWK-27 documente chaque défaut du script tel qu'il est.
5. **Le bloc YAML `graph:`.** Avec `process: graph` :

   ```yaml
   process: graph
   graph:
     start: triage              # optionnel : par défaut le premier nœud
     nodes:                     # optionnel : absent = le graphe implicite (point 7)
       - { id: triage,   task: triage }
       - { id: fix,      task: fix }
       - { id: escalate, task: escalate }
     edges:
       - { from: triage, to: fix,      when: { category: [bug] } }
       - { from: triage, to: escalate }          # sans `when` : le « sinon »
       - { from: fix,      to: END }
       - { from: escalate, to: END }
     maxTransitions: 50
     maxVisits: 5
     circuitBreakerPreset: strict
     maxTotalDurationSeconds: 600
   ```

   `graph:` remplace le `graphConfig:` YAML (retiré, `[breaking]` : ses clés de budget passent dans `graph:`, `maxStateVisits` devenant `maxVisits`, `maxRetryCycles` disparaissant). La clé `graphConfig` du littéral `stateGraph({...})` d'un script reste. Un `graph:` hors `process: graph` est refusé au chargement, pas lu puis ignoré. Un nœud est `{id, task}` : `task` nomme une tâche de l'équipe. Une tâche est nommée par un seul nœud au plus, et quand `nodes` est donné une tâche nommée par aucun nœud est refusée au chargement. Un nœud est revisité par une arête qui y revient ; à la revisite, la dernière sortie et le dernier résultat de décision l'emportent, l'état du run et les instantanés étant indexés par id de tâche. Un nœud en échec arrête le run : pas d'aiguillage sur échec (un retry est le `retry:` de la tâche). Aucune clé `maxCycles` n'existe : une borne de cycles est `maxVisits`, et un retry de tâche est le `retry:` de la tâche (FWK-15).
6. **Arêtes.** Plusieurs `edges` depuis un même `from` deviennent une seule `AddConditionalEdge`, évaluée dans l'ordre de déclaration ; la première arête dont le `when` tient l'emporte. Un `from` qui porte des arêtes `when` est une tâche de décision (ADR-016) et a exactement une arête sans `when`, le « sinon », prise quand aucun `when` ne tient, ce qui est aussi le cas quand la tâche de décision a pris sa réponse par défaut. Une tâche ordinaire a exactement une arête. Le `when` d'une arête suit la grammaire du `when` de tâche, avec son préfixe `<task>.` : `<task>.<question>: [option, ...]`, `<task>.<question>: ">= 0.7"`, `<task>.<question>.<option>: ">= 0.25"`. La tâche doit être la tâche `from`, et les questions sont vérifiées au chargement contre celles qu'elle déclare ; `otherwise` ne s'écrit pas, une arête sans `when` l'est. Sont refusés au chargement, chacun avec un message qui nomme le nœud : un `when` sur une arête dont le `from` n'est pas une tâche de décision, un `from` de décision sans arête sans `when`, deux arêtes sans `when` depuis un nœud, une tâche ordinaire à plusieurs arêtes, et un `when:` ou des `dependencies:` de tâche dans une équipe qui a des nœuds (les arêtes ordonnent les tâches, un second aiguilleur les contredirait).
7. **Le graphe implicite.** Sans bloc `graph:`, ou avec un `graph:` qui ne porte que des budgets, `BuildCrewGraph` construit un nœud par tâche, son id étant le nom de la tâche, enchaînés dans l'ordre où l'équipe exécute ses tâches aujourd'hui, le dernier menant à END. La boucle file-et-aiguillage d'`execute_task` disparaît ; le retry d'une tâche échouée est le `retry:` de la tâche. Le `when` de tâche de l'ADR-016 garde sa sémantique d'omission dans le graphe implicite seulement. `edges` sans `nodes` veut dire ce même nœud implicite par tâche, avec les arêtes entre eux.
8. **Quel moteur exécute quoi.** Le moteur du Domain exécute toute équipe du process Graph, avec ou sans bloc `graph:`. `JsStateGraph` exécute `stateGraph(...)` dans un script `.ork.ts`, sur l'état du script. Un script ne peut pas confier un graphe au moteur du Domain et une équipe ne peut pas tourner sur `JsStateGraph` : les deux se rejoignent au validateur, au vocabulaire et aux budgets.
9. **État du run et affichage.** Le nœud courant et les compteurs de visites du process Graph font partie du checkpoint (FWK-07), de sorte qu'un run repris rentre dans le nœud où il s'était arrêté ; la sérialisation est fixée par FWK-07 et utilisée par FWK-28. Un nœud entré émet un événement `graph.node.entered` depuis les deux moteurs (champ `node`, plus `task` et `visit` pour le moteur du Domain) ; Studio dessine le graphe en lecture seule depuis ces événements et n'a pas d'éditeur. L'export YAML réécrit le bloc `graph:`, de sorte qu'import, export, import donnent le même graphe.
10. **Hors de cet ADR : le handoff.** Un agent qui passe une tâche à un autre agent par son nom n'est pas une arête de graphe ; il n'est ici ni spécifié ni refusé.

## Conséquences

- Une équipe peut décrire une boucle (un relecteur qui renvoie le travail à un rédacteur) et un embranchement sur une décision, les budgets du moteur étant la seule borne, et une topologie invalide échoue au chargement, pas au milieu d'un run.
- Deux moteurs restent à maintenir ; ce qu'ils ont en commun est testé, pas supposé : le validateur est l'unique chemin de code des fautes de topologie et un test de parité fixe les budgets.
- `graphConfig:` et le `maxRetryCycles` au niveau de l'équipe disparaissent : une équipe qui les écrit est refusée au chargement et déplace ses clés (`[breaking]`, FWK-15 et FWK-28). FWK-28 migre l'exemple `09-experimental/102-graph-orchestration` (config et README) et les tests `CrewFactoryGraphConfigTests`, `RemovedCircuitBreakerYamlTests` et `UnknownCrewYamlKeysTests`.
- Une équipe du process Graph sans bloc `graph:` exécute un nœud par tâche : sa trace et ses événements nomment les tâches, pas `execute_task`.

## Alternatives écartées

- **Un moteur unique, celui du Domain, qui exécute les scripts** : écarté. Il recrée la boucle CLR qui attendait des fonctions JavaScript, que SCR-25 T1 a retirée parce que Jint n'a qu'un seul drainer.
- **Un moteur unique, celui du script, qui exécute les équipes** : écarté. L'état d'une équipe est typé et ses tâches sont des appels CLR attendus ; chaque tâche passerait derrière un trampoline.
- **Une clé `maxCycles`** : écarté. Elle se lirait comme une borne de retry à côté du `retry:` de la tâche ; `maxVisits` borne les cycles et `retry:` borne les tentatives.
- **Un `maxRetryCycles` conservé dans `graph:`** : écarté pour la même confusion, et parce que c'est un compte sur tout le run, pas une borne par nœud.
- **Des arêtes évaluées comme un script ou une expression** : écarté. Un `when` lit les réponses d'une tâche de décision, comme dans l'ADR-016, et rien d'autre.
