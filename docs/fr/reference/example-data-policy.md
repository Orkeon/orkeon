> 🇬🇧 [English version](../../reference/example-data-policy.md)

# Politique de données des exemples

> **Voir aussi** : [Exécuter votre premier exemple](../getting-started/run-your-first-example.md) · [Gabarit de README d'exemple](../templates/example-readme.md) · [Conformité VFS](../architecture/vfs-compliance.md) · [Retour à l'index](../INDEX.md)

La plupart des exemples embarqués livrent leur **définition de crew** (`config.yaml`)
et leur code, mais **pas** les données d'entrée sur lesquelles ils travaillent. Cette
page explique pourquoi, et comment nourrir un exemple avec vos propres données.

## La politique

- **Les exemples livrent de la configuration, pas des jeux de données.** Un exemple
  est un crew que vous pouvez lire et exécuter — pas une distribution de données.
  Quand la section d'exécution d'un README dit *« cet exemple ne livre pas encore de
  données d'exemple »*, c'est exactement cela : aucun fichier d'entrée n'est commité
  à côté du `config.yaml`.
- **Les exemples vitrines qui ont besoin d'une fixture en portent une minuscule.**
  Une poignée d'exemples livrent un petit échantillon synthétique pour tourner sans
  préparation. Ceux-là ont un tableau **Données requises** dans leur README et un
  `--mount` dans leur commande d'exécution ; les deux catégories se distinguent à ce
  tableau.
- **Aucune donnée propriétaire, protégée par le droit d'auteur ou personnelle**
  n'est jamais commitée dans le dépôt.

### Pourquoi

- **Taille du dépôt** — des corpus réalistes (PDF, datasets, scrapes) alourdiraient
  le clone pour tous, alors que la plupart n'exécutent que quelques exemples.
- **Licences** — les documents et jeux de données tiers portent leurs propres
  conditions ; nous ne les redistribuons pas.
- **Fraîcheur** — beaucoup d'exemples travaillent sur des sources vivantes
  (actualités, prix, pages web). Un instantané commité aujourd'hui est périmé
  demain ; laisser le crew récupérer des données à jour est précisément l'intérêt.
- **Reproductibilité** — vous contrôlez exactement ce que voit le crew, ce qui rend
  les exécutions auditables et les résultats vôtres.

## Comment un exemple obtient ses données

Selon le crew, l'une de ces trois choses est vraie :

1. **Les outils récupèrent leurs propres données.** Les crews bâtis autour de
   `web_scrape`, `http_api`, `search` ou d'outils similaires tirent des données
   vivantes à l'exécution. Vous ne fournissez rien — juste un profil LLM et,
   généralement, un sujet via `--var` ou `--initial-context`. Voir la référence des
   flags dans
   [Exécuter votre premier exemple](../getting-started/run-your-first-example.md#chaque-flag-expliqué).
2. **Vous montez vos propres entrées.** Les crews qui lisent des fichiers locaux
   (PDF, CSV, JSON, une base de code) attendent que vous exposiez un répertoire hôte
   dans le [système de fichiers virtuel](../architecture/vfs-compliance.md) du crew :

   ```bash
   orkeon run examples/<chemin>/config.yaml \
     --settings examples/appsettings/appsettings.deepseek.local.json \
     --mount ./data:/data:ro ./out:/output:rw
   ```

   Le `config.yaml` du crew référence les entrées par leur chemin **virtuel** (p. ex.
   `/data/report.pdf`), jamais par un chemin hôte. `:ro` pour les entrées, `:rw` pour
   tout ce que le crew écrit.
3. **Une fixture d'exemple commitée.** Pour les exemples vitrines, la fixture est
   déjà dans le dossier de l'exemple et la commande d'exécution la monte pour vous —
   rien à fournir.

### Où va la sortie

Par convention, les crews écrivent leurs résultats sur le montage `/output`.
Mappez-le sur un répertoire local avec `--mount ./out:/output:rw` ; déclarer un
montage `/output:rw` active aussi l'écriture automatique du résumé d'exécution.

## Ajouter des données d'exemple (contributeurs)

Si votre exemple a réellement besoin d'une fixture embarquée :

- **Petite et synthétique.** Quelques Ko de données écrites à la main ou générées,
  pas un extrait du monde réel.
- **Propre côté licences.** Aucun contenu protégé ou personnel. Si elle doit
  ressembler à des données réelles, générez-la.
- **Placez-la dans le dossier de l'exemple** et montez-la en lecture seule depuis la
  commande d'exécution (`--mount ./data:/data:ro`).
- **Documentez-la** dans un tableau **Données requises** du README de l'exemple, en
  suivant le [gabarit de README d'exemple](../templates/example-readme.md). Ce
  tableau (plus un `--mount` dans la commande) est ce qui sort l'exemple de la
  catégorie « ne livre pas encore de données d'exemple ».

## Jeux de données des vitrines

Un ensemble d'exemples vitrines (un par catégorie) livre une fixture embarquée pour
tourner sur de vrais fichiers sans préparation. Leurs données sortent d'un
générateur unique :

```bash
python3 scripts/generate-vitrine-data.py      # (re)génère chaque dossier data/ de vitrine
```

Règles suivies par ces fixtures, en plus du « petite et synthétique » ci-dessus :

- **Déterministes.** Le générateur utilise une graine RNG fixe : le relancer
  reproduit les fichiers commités à l'octet près. Ne modifiez jamais un fichier
  généré à la main — modifiez le générateur et relancez-le.
- **Synthétiques et petites.** Données synthétiques uniquement (rien de réel,
  personnel ou propriétaire) et **moins de 100 Ko par fichier**, sauf nécessité
  justifiée dans le README.
- **Aucune dépendance externe.** CSV/JSON viennent de la stdlib Python ; les PDF
  sont écrits par un mini-writer intégré (police Helvetica standard, texte
  extractible par `pdf_reader`). Le script tourne sur un Python 3.9+ nu.
- **Les tâches nomment les chemins virtuels.** Les descriptions de tâches du
  `config.yaml` référencent les chemins VFS concrets (p. ex.
  `/data/experiment-measurements.csv`) pour que l'agent lise le fichier livré au
  lieu d'inventer un chemin que le VFS rejetterait.

### Quels outils déclenchent l'exigence de données

Un outil ne déclenche l'exigence « livrer une fixture » que s'il lit un chemin
fourni par l'appelant à travers le VFS — `csv_reader`, `pdf_reader`, `file_read`,
`directory_read`, `docx_reader` et similaires. Les outils qui n'exigent **pas** par
eux-mêmes de fichier embarqué : `json_tool` (opère sur des chaînes JSON inline),
`file_write` (écrit seulement), `http_api` / `web_scrape` (ressources distantes) et
`relational_database_query` (chaîne de connexion fournie par l'appelant). Un exemple
bâti uniquement sur ceux-là n'a pas besoin de dossier `data/` ; voir
`examples/09-experimental/97-multi-party-negotiation`, qui n'en livre aucun et passe
son scénario via `--initial-context`.

### Vérifier une fixture

Utilisez le flag de dry-run du runner pour confirmer que le crew se charge sous
résolution stricte des outils et que le montage de données est accepté, sans appeler
de LLM :

```bash
orkeon run examples/<chemin>/config.yaml \
  --mount examples/<chemin>/data:/data:ro --validate
```

Une ligne `VALIDATION OK: … (agents=N, tasks=M, tools resolved=K)` signifie le
succès.

> **Piège de syntaxe des montages.** Les valeurs répétées de `--mount` se passent
> **séparées par des espaces sous un seul flag** — `--mount a:/data:ro b:/output:rw`
> — et non comme deux flags `--mount` séparés (le parseur CLI rejette une option
> répétée).
