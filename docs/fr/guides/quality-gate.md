> 🇬🇧 [English version](../../guides/quality-gate.md)

# Quality Gate SonarQube — politique transitoire et trajectoire de durcissement

> Chantier **R5.4** (finding TST-005) · Décision mainteneur (QCM 2026-06-11) :
> **« Assouplir puis durcir »** — le Quality Gate devient **bloquant immédiatement**,
> avec des seuils transitoires réalistes, puis est durci au fil de la résorption
> de la dette. Ce document est la référence de la trajectoire de durcissement.

## 1. Contexte

Avant R5.4, le Quality Gate SonarQube était en **ERROR sur 3 conditions**
(`new_coverage 71,3 < 80`, `new_reliability_rating 3 > 1`,
`new_security_hotspots_reviewed 0.0 < 100` — rapport du 2026-05-31) mais
**aucun pipeline ne consommait ce verdict** : l'analyse se terminait sans
`sonar.qualitygate.wait=true` ni lecture du statut, et la CI restait verte.
La politique « 80 % sur le nouveau code » était documentée mais jamais appliquée.

Depuis R5.4 :

- le gate est **bloquant là où l'analyse tourne** : analyse avec
  `sonar.qualitygate.wait=true`, verdict relu via l'API
  (`/api/qualitygates/project_status`), **code de sortie non nul** si le gate
  est FAILED. L'analyse est **lancée par les mainteneurs contre le SonarQube
  auto-hébergé** (`scripts/sonar-analyze.{sh,ps1}`) — **aucun workflow CI
  n'exécute SonarQube aujourd'hui** (les anciens workflows Sonar ont été
  supprimés ; câbler l'analyse dans une voie CI planifiée reste un
  reliquat ouvert) ;
- les seuils sont **transitoires** (réalistes au regard de l'état mesuré au
  2026-05-31) et leur **durcissement est planifié** ci-dessous.

**Où en est le projet au 2026-09-05** (mesuré par `scripts/sonar-analyze.sh` ; le rapport
complet qu'il écrit sous `sonarqube/` est un artefact local que le dépôt ne versionne
pas) : le gate est **OK**
sur chaque condition qu'il rapporte — `new_reliability_rating` **A (1)**,
`new_security_rating` **A**, `new_maintainability_rating` **A**, `new_coverage` **78,1 %**,
`new_duplicated_lines_density` **0,0 %** — sur 162 k lignes portant **0 bug,
0 vulnérabilité, 0 hotspot non statué** (17 hotspots, tous REVIEWED) et 0 min de dette
technique. Les deux conditions que cette page décrivait rouges ou neutralisées en mai sont
donc **tenues**. Les seuils ci-dessous n'ont pas encore bougé : les relever relève de la
procédure du §6, pas de cette mesure.

## 2. Clé de projet

La clé de projet SonarQube est **`Orkeon`** (renommée le 2026-08-17 en suite
de l'audit PUB-01 — l'ancienne clé était la dernière trace du nom d'avant
renommage du projet). Cette décision remplace la
décision QCM du 2026-06-11 et assume le compromis qu'elle voulait éviter :
l'historique d'analyse côté serveur (baseline « nouveau code », tendances)
repart de la première analyse sous la nouvelle clé ; l'ancien projet reste
consultable sur l'instance auto-hébergée. La clé est surchargeable via la
variable d'environnement `SONAR_PROJECT_KEY`.

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
| `new_coverage` | ≥ | **70 %** | 80 % | Nouveau code à 71,3 % au 2026-05-31 ; aligné sur la cible de couverture à 70 % (décision R5.2). Remonte avec R5.5/R5.6. |
| `new_reliability_rating` | ≤ | **B (2)** | A (1) | B tolérait les bugs *mineurs* le temps de corriger les 3 bugs *majeurs* connus. Au 2026-05-31 le nouveau code était à **C** et la condition restait rouge à dessein — pression ciblée du gate bloquant sur les seules vraies causes (cf. fiche R5.4). Ces bugs ont disparu : au 2026-09-05 le nouveau code est à **A (1)** et le projet relève 0 bug, donc B est devenu du mou et non de la pression. Le durcissement vers A est débloqué (§5). |
| `new_security_rating` | ≤ | **A (1)** | A (1) | Déjà tenue — maintenue stricte. |
| `new_maintainability_rating` | ≤ | **A (1)** | A (1) | Déjà tenue — maintenue stricte. |
| `new_duplicated_lines_density` | ≤ | **3 %** | 3 % | Déjà tenue (0,56 %) — maintenue. |
| `new_security_hotspots_reviewed` | ≥ | **0 % (neutralisée)** | 100 % | Neutralisée le temps que 9 hotspots hérités soient statués (remédiation sécurité, fiche 07). Les 17 hotspots sont **REVIEWED** au 2026-09-05 et aucun ne reste non statué, donc la neutralisation ne masque plus rien ; le seuil 0 garde la condition **visible** dans le gate et les rapports jusqu'à sa montée à 100 (§5). |

## 4. Où le verdict est appliqué

| Surface | Mécanisme | Effet en cas de gate FAILED |
|---|---|---|
| `scripts/sonar-analyze.sh` | `sonar.qualitygate.wait=true` + relecture API du verdict (statut + conditions échouées loguées) | **exit code ≠ 0** (le rapport Markdown est tout de même généré) |
| `scripts/sonar-analyze.ps1` | idem | **exit code ≠ 0** |

Il n'y a **pas de surface CI** : aucun workflow GitHub n'exécute d'analyse
SonarQube (ce que la CI garde sur chaque PR, c'est le build `-warnaserror`
avec le jeu complet d'analyseurs, le gel d'API, les suites de tests et les
gates documentaires — voir [État du projet](../../../README.fr.md#état-du-projet)).
Le verdict (statut + chaque condition échouée avec valeur réelle et seuil) est
visible dans les logs du script.

## 5. Trajectoire de durcissement

Chaque condition est durcie **dès que son critère de passage est rempli** —
pas de big-bang. Récapitulatif :

| Condition | T0 (en vigueur) | Critère de passage | T1 | Critère de passage | T2 (cible) |
|---|:--:|---|:--:|---|:--:|
| `new_coverage` | 70 % | R5.5 (tests contractuels `Tools.Analysis`) **et** R5.6 (dé-flake) livrés ; cible de couverture montée à 75 % (R5.2). Nouveau code mesuré à 78,1 % au 2026-09-05 | 75 % | nouveau code stable ≥ 80 % sur ~1 mois de merges | **80 %** |
| `new_reliability_rating` | B | les 3 bugs majeurs connus corrigés (campagne de remédiation) — **tenu au 2026-09-05** : 0 bug, nouveau code à A | A | — | **A** |
| `new_security_hotspots_reviewed` | 0 % (neutralisée) | les 9 hotspots hérités statués sur le serveur (remédiation sécurité, fiche 07) — **tenu au 2026-09-05** : 17/17 REVIEWED | 100 % | — | **100 %** |
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
2. Mettre à jour le présent document (tables §3 et §5, historique §8).
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
  C'est pourquoi le script installe ReportGenerator automatiquement.
- Sur une machine headless, le fallback Docker des scripts peut être désactivé
  (`SONAR_NO_DOCKER=1`) : un serveur injoignable fait alors échouer la passe au
  lieu de booter une instance éphémère sans historique (qui rendrait le verdict
  « nouveau code » dénué de sens).

## 8. Historique

| Date | Événement |
|---|---|
| 2026-06-11 | Création du gate « Orkeon Transitional » (T0), activation du blocage local (R5.4), alignement de la clé de projet des scripts sur la clé historique |
| 2026-08-17 | Clé de projet renommée en `Orkeon` (suite de l'audit PUB-01 — clé historique d'avant renommage retirée ; remplace la décision QCM du 2026-06-11, l'historique d'analyse repart sous la nouvelle clé) |
| 2026-08-18 | Document réaligné sur la réalité (DOC-02) : aucun workflow CI Sonar n'existe — l'application du verdict est locale aux scripts d'analyse ; références aux anciens workflows retirées |
