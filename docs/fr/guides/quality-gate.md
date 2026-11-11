> 🇬🇧 [English version](../../guides/quality-gate.md)

# Quality Gate SonarQube — politique transitoire et trajectoire de durcissement

> **« Desserrer puis durcir »** : le Quality Gate est **bloquant**, avec des seuils
> transitoires durcis à mesure que la dette se résorbe. Ce document est la référence de
> la trajectoire de durcissement.

## 1. Contexte

Le gate est **bloquant là où l'analyse tourne** : analyse avec
`sonar.qualitygate.wait=true`, verdict relu par `/api/qualitygates/project_status`,
**code de sortie non nul** quand le gate est FAILED. L'analyse est lancée par un mainteneur
contre le SonarQube auto-hébergé (`scripts/sonar-analyze.{sh,ps1}`) ; aucun workflow CI
n'exécute SonarQube. Les seuils sont **transitoires** et leur durcissement est planifié
ci-dessous.

## 2. Clé de projet

La clé de projet SonarQube est **`Orkeon`** ; la variable d'environnement
`SONAR_PROJECT_KEY` la remplace.

## 3. Le gate « Orkeon Transitional »

Le gate est **provisionné automatiquement et de façon idempotente** par les
scripts d'analyse, puis **associé au projet** :

- `scripts/sonar-analyze.sh` — table `QUALITY_GATE_CONDITIONS` ;
- `scripts/sonar-analyze.ps1` — table `$QualityGateConditions`.

Ces **deux tables sont la source de vérité des seuils** et doivent rester
identiques entre elles (et synchrones avec ce document). À chaque exécution,
le script crée le gate s'il n'existe pas, crée ou met à jour chaque condition
dont le seuil diffère, et (ré)associe le gate au projet.

### Conditions (toutes sur le **nouveau code**)

| Condition | Sens | Seuil transitoire (T0) | Cible finale | Justification du seuil transitoire |
|---|---|:--:|:--:|---|
| `new_coverage` | ≥ | **70 %** | 80 % | En dessous de la cible de 80 % le temps que les suites grandissent. |
| `new_reliability_rating` | ≤ | **B (2)** | A (1) | Le projet relève 0 bug et le nouveau code est à A : le durcissement vers A est débloqué (§5). |
| `new_security_rating` | ≤ | **A (1)** | A (1) | Déjà tenue — maintenue stricte. |
| `new_maintainability_rating` | ≤ | **A (1)** | A (1) | Déjà tenue — maintenue stricte. |
| `new_duplicated_lines_density` | ≤ | **3 %** | 3 % | Déjà tenue (0,56 %) — maintenue. |
| `new_security_hotspots_reviewed` | ≥ | **0 % (neutralisée)** | 100 % | Tous les hotspots sont statués ; le seuil 0 garde la condition visible jusqu'à sa montée à 100 (§5). |

## 4. Où le verdict est appliqué

| Surface | Mécanisme | Effet en cas de gate FAILED |
|---|---|---|
| `scripts/sonar-analyze.sh` | `sonar.qualitygate.wait=true` + relecture API du verdict (statut + conditions échouées loguées) | **exit code ≠ 0** (le rapport Markdown est tout de même généré) |
| `scripts/sonar-analyze.ps1` | idem | **exit code ≠ 0** |

Il n'y a **pas de surface CI** : aucun workflow GitHub n'exécute d'analyse
SonarQube (ce que la CI garde sur chaque PR, c'est le build `-warnaserror`
avec le jeu complet d'analyseurs, le gel d'API, les suites de tests et les
gates documentaires — voir [le workflow CI](https://github.com/Orkeon/orkeon/blob/main/.github/workflows/ci.yml)).
Le verdict (statut + chaque condition échouée avec valeur réelle et seuil) est
visible dans les logs du script.

## 5. Trajectoire de durcissement

Chaque condition est durcie **dès que son critère de passage est rempli** —
pas de big-bang. Récapitulatif :

| Condition | T0 (en vigueur) | Critère de passage | T1 | Critère de passage | T2 (cible) |
|---|:--:|---|:--:|---|:--:|
| `new_coverage` | 70 % | nouveau code ≥ 75 % sur les analyses mesurées | 75 % | nouveau code stable ≥ 80 % sur ~1 mois de merges | **80 %** |
| `new_reliability_rating` | B | tenu : 0 bug, nouveau code à A | A | — | **A** |
| `new_security_hotspots_reviewed` | 0 % (neutralisée) | tenu : tous les hotspots statués | 100 % | — | **100 %** |
| `new_security_rating` | A | déjà au niveau cible | A | — | **A** |
| `new_maintainability_rating` | A | déjà au niveau cible | A | — | **A** |
| `new_duplicated_lines_density` | 3 % | déjà au niveau cible | 3 % | — | **3 %** |

**État final (T2)** : les conditions rejoignent celles du gate intégré
« Sonar way ». Deux options à ce stade : basculer le projet sur « Sonar way »
et supprimer « Orkeon Transitional », ou conserver le gate nommé avec les
valeurs finales (préférer la première pour réduire la surface de configuration
custom).

## 6. Procédure de modification des seuils

1. Modifier la table `QUALITY_GATE_CONDITIONS` dans `scripts/sonar-analyze.sh`
   **et** `$QualityGateConditions` dans `scripts/sonar-analyze.ps1` (les deux
   doivent rester identiques).
2. Mettre à jour le présent document (tables §3 et §5).
3. Relancer une analyse : le provisionnement idempotent pousse les nouveaux
   seuils sur le serveur (`update_condition`).

Ne pas modifier les seuils directement dans l'UI SonarQube : ils seraient
écrasés à la prochaine exécution du script.

## 7. Limites connues

- Le provisionnement exige un token avec la permission **« Administer Quality
  Gates »** (et « Create Projects » pour un serveur vierge). À défaut, le
  script l'indique en WARN et continue : le gate **actuellement associé** au
  projet est alors appliqué (le blocage reste effectif, mais avec les seuils
  du serveur).
- `new_coverage` n'est **pas évaluée** par SonarQube si aucune couverture n'est
  importée : un import de couverture cassé peut faire passer le gate à tort.
  Les scripts installent donc les deux outils de couverture quand ils manquent —
  `dotnet-coverage`, qui mesure (les projets de tests tournent sur
  Microsoft.Testing.Platform, qui refuse le collecteur VSTest `--collect`), et
  ReportGenerator, qui convertit sa sortie Cobertura au format générique SonarQube —
  et **interrompent l'analyse** quand le rapport Cobertura ne contient aucune classe,
  plutôt que de laisser le gate s'évaluer sur du vide.
- Sur une machine headless, le fallback Docker des scripts peut être désactivé
  (`SONAR_NO_DOCKER=1`) : un serveur injoignable fait alors échouer la passe au
  lieu de booter une instance éphémère sans historique (qui rendrait le verdict
  « nouveau code » dénué de sens).
- Les scripts mesurent la couverture sur les **suites unitaires et rapides seulement**, avec le
  filtre de `coverage.yml` (`--filter "Category!=Integration&Category!=Slow"`, §8). Les
  catégories Integration (Testcontainers : Docker et des Go d'images) et Slow ne tournent pas :
  les tests ne demandent jamais Docker, et le chiffre local couvre le même périmètre que le
  chiffre public. Seules les assemblies d'Orkeon sont mesurées (`dotnet-coverage.settings.xml`, partagé
  avec `coverage.yml`) : `Orkeon.*`, `orkeon` et `orkeon-host`, sans les assemblies de tests.
  Ces catégories se lancent à part :
  `dotnet test Orkeon.sln --no-build --filter "Category=Integration|Category=Slow" -- --ignore-exit-code 8`.

## 8. D'où vient le chiffre public de couverture

La passe SonarQube ci-dessus est locale : son rapport est écrit sous `sonarqube/`, que le
dépôt ne versionne pas, si bien que rien de ce qu'elle mesure n'est vérifiable par un tiers.
Le chiffre de couverture qui *peut* l'être vient du
[workflow Coverage](https://github.com/Orkeon/orkeon/actions/workflows/coverage.yml)
(`.github/workflows/coverage.yml`) : à chaque push sur `main` et chaque semaine, il exécute
les suites unitaires et rapides — le même filtre `Category!=Integration&Category!=Slow` que
`ci.yml` — sous `dotnet-coverage` (épinglé) sur les seules assemblies d'Orkeon
(`dotnet-coverage.settings.xml` : les paquets livrés avec leur PDB, comme Aspire.Hosting ou
StackExchange.Redis, ne sont pas mesurés), refuse une mesure vide, écrit le résumé
ReportGenerator sur la page du run et conserve `coverage.cobertura.xml` plus un rapport HTML
comme artefact `coverage` (90 jours). C'est la *couverture de lignes par les suites
unitaires et rapides* ; les catégories Integration et Slow tournent dans `integration.yml`
sans couverture. Le README renvoie au workflow et ne cite aucun nombre : un run est une
mesure, un nombre tapé est une affirmation.
