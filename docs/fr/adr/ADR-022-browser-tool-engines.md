> 🇬🇧 [English version](../../adr/ADR-022-browser-tool-engines.md)

> **Voir aussi** : [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-021](./ADR-021-chat-tool-family.md) · [Retour à l'index](../INDEX.md)

# ADR-022 — Un seul outil navigateur quel que soit le moteur : la quatrième exception au gel du périmètre, élargie

**Statut** : Accepté · **Date** : 2026-11-24
· **Portée** : l'outil navigateur d'`Orkeon.Tools.Web`, sa section de réglages, ses moteurs

Cet ADR consigne la décision et la conception. Les moteurs sont livrés un par un : le guide du navigateur dit lesquels sont construits.

## Contexte

Orkeon pilote un seul navigateur, Firefox, par l'outil `firefox`. Les issues #440 à #444 demandent Chrome, Edge, Brave et Safari. L'essentiel de l'outil parle WebDriver BiDi et n'est lié à aucun navigateur : les actions, le plan de page, les ids de nœud, les onglets, les dialogues, la politique d'URL, l'instance partagée et la durée de vie. Ce qui est lié à Firefox : le lancement, le point d'accès, les profils et le texte des erreurs.

Le périmètre est gelé (CONTRIBUTING, « Le périmètre est gelé ») ; `firefox` en est la quatrième exception motivée.

## Décision

1. **Un seul outil, `browser`, et aucun outil par éditeur.** Il prend la place de `firefox` dans le compte des outils intégrés. La quatrième exception passe d'un moteur à cinq (Firefox, Chrome, Edge, Brave, Safari) ; aucun moteur n'ajoute d'outil.
2. **L'hôte choisit le moteur, jamais l'agent.** `Orkeon:Tools:Browser:Engine`, `firefox` par défaut, un moteur par hôte. Un moteur inconnu est refusé à la construction des sessions de navigateur de l'hôte ; rien ne se replie sur Firefox. Chaque réponse porte `engine`.
3. **Un moteur est un port interne.** Il trouve son exécutable, crée ou ouvre un profil, refuse un profil qu'un autre navigateur tient, retire ses propres dossiers jetables périmés, lance le navigateur et rend un canal BiDi. Il déclare ce qu'il ne sait pas faire ; une action qui demande ce qui lui manque est refusée avec la raison. Rien n'est simulé. Tout moteur fait des captures d'écran.
4. **`firefox` reste un second nom déprécié** du même outil, sur le même gestionnaire de sessions : les deux noms ne lancent jamais deux navigateurs sur un même profil. La section `Orkeon:Tools:Firefox` est lue quand `Orkeon:Tools:Browser` est absente ; les deux présentes, c'est refusé. Le nom, la section et les types publics `Firefox*` partent à la prochaine majeure.
5. **Les moteurs Chromium exécutent eux-mêmes le mapper chromium-bidi**, par le protocole DevTools, sans binaire de pilote. Le mapper est embarqué à une version épinglée, avec sa notice Apache-2.0.
6. **Safari est mesuré avant tout code** : ce que `safaridriver` couvre en BiDi choisit son canal.

### Décisions retenues (D1–D10)

| # | Question | Retenu |
|---|---|---|
| D1 | Durée de vie de l'alias | Toute la majeure. |
| D2 | Préfixe des collections `search` | `browser` pour tous les moteurs : une page indexée sous `firefox` l'est une fois de plus. |
| D3 | Le gel du périmètre | La quatrième exception est élargie à cinq moteurs, livrés un par un. |
| D4 | Comment un outil répond à deux noms | L'outil est enregistré deux fois sur un seul gestionnaire de sessions, la seconde sous le nom `firefox`. Pas d'alias dans `IToolRegistry`. |
| D5 | Types publics | Les types `Firefox*` gardent leur nom jusqu'à la prochaine majeure ; `Engine` s'ajoute aux options et à la réponse. |
| D6 | Profils nommés sous une racine d'Orkeon | Pas au départ : un profil Chromium nommé est un chemin de dossier. |
| D7 | Le canal Chromium | Le mapper exécuté par Orkeon ; un chemin de pilote seulement si un essai mesuré échoue. |
| D8 | D'où vient le mapper | Le paquet npm `chromium-bidi`, épinglé, son bundle embarqué. |
| D9 | CI | Chrome sur le runner Ubuntu, Edge sur le runner Windows, Safari sur le runner macOS ; Brave en local. |
| D10 | Safari sans BiDi | Décidé sur la mesure : BiDi, ou WebDriver classic derrière le même canal. |

## Alternatives envisagées

- **Un outil par éditeur** : les équipes nommeraient un éditeur, et le compte gelé grandirait de quatre.
- **Un argument `engine` sur chaque action** : un modèle choisirait le navigateur.
- **Des classes Chrome séparées à côté de celles de Firefox** : deux copies de la logique de session à tenir ensemble.
- **chromedriver comme point d'accès BiDi** : un binaire à installer, lié à la version majeure de Chrome.

## Conséquences

- Une équipe qui liste `firefox` continue de marcher ; les nouvelles listent `browser`.
- Un moteur est validé sur le vrai navigateur : une machine qui ne l'a pas saute la suite de ce moteur, et le dit.
