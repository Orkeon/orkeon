> 🇬🇧 [English version](../../getting-started/forge-a-team-from-a-need.md)

# Forger une équipe à partir d'un besoin

Vous ne voulez pas construire une équipe d'agents — vous voulez résoudre un problème concret. L'Atelier (`orkeon forge`) est le parcours guidé entre les deux : vous décrivez le problème en langage naturel, un assistant en fait une équipe, l'équipe est essayée en bac à sable sur votre propre exemple, et le résultat est jugé **contre les critères d'acceptation que vous avez énoncés** — pas contre un vague « c'est bien ? ».

```
Brief ──▶ Blueprint ──▶ Render ──▶ Validate ──▶ Test ──▶ Diagnose ──▶ Verdict
              ▲                        │          │                      │
              └───── correction ◀──────┴── (erreurs de validation) ──────┤ (non conforme)
                                                  │                      ▼
                                                  └─────────────▶ Ready ──▶ Promote
                                                   (adopter sans essayer)
```

## Prérequis

Un LLM configuré. Si ce n'est pas encore fait :

```bash
orkeon init          # l'assistant à 5 choix (ollama, openai, custom…)
orkeon doctor        # vérifier l'installation
```

La forge refuse d'ouvrir l'entretien sans modèle (`FORGE-LLM-UNAVAILABLE`) — elle ne ferait que vous renvoyer vos mots en écho.

## Le cycle, de bout en bout

```bash
orkeon forge "résumer chaque matin les nouvelles offres de mon fournisseur"
```

1. **L'entretien.** L'assistant pose des questions courtes, une à la fois — « je ne sais pas » est toujours une réponse valide. Il capte un brief structuré : objectif, entrées, sortie attendue, contraintes, **critères d'acceptation** (ce que « fait, et bien fait » veut dire, dans vos mots), et un exemple d'entrée pour l'essai.
2. **La proposition.** Du brief, l'assistant planifie une équipe : qui fait quoi, dans quel ordre, avec quels outils — tirés du seul catalogue réel ; il ne peut pas en inventer. Le plan est rendu en fichiers de crew ordinaires et validé mécaniquement (outils inconnus, tâches sans agent, cycles de dépendances). Les erreurs de validation repartent chez l'assistant pour réparation — deux fois, puis elles remontent vers vous.
3. **L'essai.** L'équipe tourne dans un bac à sable, in-process, sur votre exemple d'entrée : les écritures atterrissent dans le dossier de la session — `/output` pour les livrables, `/forge` pour ses fichiers de travail —, l'espace de travail est monté en lecture seule, et `shell_command`/`code_interpreter` sont retirés du catalogue, tout simplement. Le confinement porte sur le dossier de session, pas sur le dossier de sortie qu'il contient.
4. **Le verdict.** Un juge note la sortie contre vos critères d'acceptation, un à un, et énonce des constats et des suggestions concrètes. Un critère *must* raté bloque quel que soit le score. Si aucun juge ne peut tourner, le verdict le dit (`judge: deterministic`) — il n'invente jamais un ✔.
5. **Votre arbitrage.** Le mode interactif arbitre chaque verdict, conformes compris : accepter (une équipe conforme devient prête ; accepter une non conforme, c'est juger sur pièce), refaire l'essai tel quel (`retry` — zéro jeton de composition, une itération de budget), corriger (le diagnostic est réinjecté dans le plan, mot pour mot), rendre un plan édité à la main (`edit`), ou en rester là. `--auto` arbitre seul, dans les limites du budget.

L'adoption elle-même n'est pas une porte à sens unique : `forge resume` d'une session **promue** la rouvre à l'arbitrage (le verdict stocké est ré-annoncé), et un second `forge promote` vers la **même** destination met le dossier à jour en place — fichiers générés régénérés, vos propres fichiers préservés. Toute autre destination non vide reste refusée.

Tout est borné : 3 itérations par défaut (`--max-iterations`), plafonds optionnels de jetons et de temps (`--max-tokens`, `--max-seconds`). Un budget épuisé arrête le cycle proprement ; une reprise peut le relever — la consommation est toujours reportée.

## La session sur disque

Chaque cycle vit sous `.orkeon/forge/<slug>/` dans votre répertoire de travail :

```
.orkeon/forge/veille-fournisseur/
├── session.json          état, statut, budget — le point de reprise
├── brief.json            ce que vous avez demandé, critères compris
├── blueprint.json        le plan d'équipe (source unique des deux rendus)
├── crew/                 la crew rendue — ce qui tourne vraiment
├── runs/<n>/             chaque essai : sortie, métriques, verdict, livrables
├── transcript.jsonl      la conversation
└── history.jsonl         chaque transition d'état
```

```bash
orkeon forge list                        # ce qui est en cours, ce qui est prêt
orkeon forge resume veille-fournisseur   # reprendre exactement là où c'était
orkeon forge "..." --dry                 # générer et valider seulement — jamais exécuter
orkeon forge resume supplier-watch --edit --dry   # amender le plan à la pause, re-rendre, re-pauser
orkeon forge resume supplier-watch --adopt        # garder l'équipe telle quelle, sans essai
```

À la pause `--dry`, vous pouvez amender le plan avant même de l'essayer : `resume --edit` lit le blueprint amendé sur le canal, le valide intégralement, re-rend de façon déterministe — zéro jeton LLM, même itération — et avec `--dry` se remet en pause à la même frontière. C'est ce que fait le « Modifier » de Studio sur les cartes d'agent de l'étape Composer.

La même pause accepte une seconde réponse : `resume --adopt` garde l'équipe telle qu'elle a été générée et passe directement à `Ready` — hors ligne, sans dossier d'exécution, zéro jeton. Ce qui est sauté, ce sont les **preuves** que produit un essai, jamais un contrôle : à cette pause le crew est rendu et validé, et la promotion n'a jamais consommé d'artefact d'essai — `FORGE.md` écrit simplement «&nbsp;aucun verdict enregistré&nbsp;». C'est l'« Adopter sans essayer » de Studio, à côté d'« Essayer l'équipe ».

## Deux formats, une seule génération

L'assistant n'écrit jamais ni YAML ni TypeScript — il produit un plan validé par schéma, et un renderer déterministe en dérive les fichiers. `--format yaml` (défaut) rend le layout YAML par entité ; `--format script` rend un `crew.ork.ts` éditable — l'équivalent scripté de la même crew, un point de départ pour vos propres retouches, pas de la logique conditionnelle. Le chemin script exige esbuild ; sans lui, une nouvelle session retombe sur YAML et le dit (`FORGE-ESBUILD-MISSING`).

Les deux rendus convergent vers le même validateur, et l'essai charge la crew **depuis les fichiers rendus** — ce qui est écrit est ce qui tourne.

## L'adopter

```bash
orkeon forge promote veille-fournisseur --to ~/solutions/veille-fournisseur \
    --schedule daily@07:30
```

Le dossier promu est ordinaire — rien n'y est propriétaire à la forge :

- `crew/` — l'équipe, telle qu'essayée ;
- un dossier par racine de livrable où l'équipe écrit (`output/` quand ses tâches déclarent `deliverable: /output/…`) — créé vide, pour que le premier lancement ait où écrire ;
- `run.sh` / `run.cmd` — des scripts de lancement qui se placent (`cd`) dans le dossier, portent les montages liant ces racines (`--mount "$DIR/output":/output:rw`) et ont vos entrées d'exemple pré-remplies (à adapter au vrai usage) ;
- `FORGE.md` — la carte d'identité de l'équipe : objectif, critères d'acceptation, verdict, date et version de génération — ce qu'un collègue lit en récupérant le dossier ;
- `schedule/` (avec `--schedule`) — un XML de tâche Windows, un timer systemd, une ligne cron. La commande d'installation est **affichée, jamais exécutée** : Orkeon n'a pas d'ordonnanceur, et prétendre le contraire promettrait une supervision qu'il ne peut pas donner.

Lancez-la par son propre script — `~/solutions/veille-fournisseur/run.sh` — ou pointez Orkeon Studio sur le dossier, qu'il détecte. Un `orkeon run ~/solutions/veille-fournisseur/crew` nu la lance aussi, mais sans les arguments `--mount` que porte le lanceur : l'équipe n'a alors aucun `/output` et n'écrit rien.

## Dans Orkeon Studio

Le même moteur anime l'assistant **Créer une équipe** d'Orkeon Studio (Windows) : quatre étapes — Décrire ▸ Composer ▸ Essayer ▸ Adopter — où le stepper suit les jalons du moteur, où la proposition et la checklist ✔/✘ sont ses événements rendus en cartes, et où l'adoption promeut directement dans le dossier des équipes. Studio lance `orkeon forge --events jsonl` en processus enfant et ne touche jamais au LLM lui-même ; chaque capacité de l'écran est une projection du même flux d'événements que le terminal rend. Voir [Studio](../architecture/studio.md).

## Limites honnêtes

- La qualité de la proposition suit le modèle que vous avez configuré ; les critères du brief et la validation mécanique sont les garde-fous, pas un substitut.
- Le bac à sable restreint en **retirant les outils du catalogue**, pas en espérant que le modèle s'abstienne ; les outils réseau n'atteignent le plan que si votre brief les a demandés.
- Une session, un problème : l'Atelier génère et valide une équipe — ce n'est pas un éditeur visuel de crews.
