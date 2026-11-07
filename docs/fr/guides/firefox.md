> 🇬🇧 [English version](../../guides/firefox.md)

# L'outil `firefox`

> **Voir aussi** : [Inventaire des outils](../tools/inventory.md) ·
> [Référence de configuration](../reference/configuration.md#orkeontoolsfirefox) ·
> [RaggableTree](../architecture/raggable-tree.md) · [Limitations connues](../reference/limitations.md) ·
> [Retour à l'index](../INDEX.md)

`firefox` donne un navigateur à un agent : le Firefox installé sur la machine, piloté par
[WebDriver BiDi](https://w3c.github.io/webdriver-bidi/) sur un WebSocket. Aucun binaire de
pilote, aucun paquet d'éditeur, aucun téléchargement de navigateur. L'agent ouvre une page, la lit
comme un plan de sections, de liens et de contrôles, clique, saisit, presse des touches, cherche
dans la page par le sens, exécute un script et enregistre une capture d'écran. Les pages exécutent
leur JavaScript : ce que `web_scrape` ne voit pas, `firefox` le voit.

- **Un navigateur par exécution d'agent.** Firefox démarre à la première action de l'exécution,
  garde ses pages d'une action à la suivante et se ferme avec l'exécution. Des agents qui
  s'exécutent côte à côte ont chacun leur navigateur, ou en partagent un avec des onglets à eux
  quand l'hôte le dit (`SharedInstance`). Hors de toute exécution, une action reçoit un
  navigateur à elle, fermé juste après. Un hôte qui veut que le navigateur survive à
  l'exécution le garde avec `Lifetime: host`
  ([ci-dessous](#un-navigateur-qui-survit-à-lexécution)).
- **Un profil jetable** par défaut : un dossier neuf, supprimé à la fermeture du navigateur.
  L'hôte peut démarrer le navigateur sur un profil du Firefox installé sur la machine à la place,
  et nommer les profils qu'un agent peut demander et ceux qu'aucun navigateur n'utilise jamais
  ([Profils](#profils)).
- **La fenêtre est visible** par défaut — l'opérateur voit ce que fait l'agent. `Headless` la
  cache ; une machine sans affichage démarre de toute façon sans fenêtre, avec un avertissement
  dans le journal.
- **Chaque adresse est vérifiée** par la politique d'URL de l'hôte (`IUrlValidator`), ou par la
  garde fermée par défaut quand l'hôte n'en enregistre aucune : les adresses privées et de
  boucle locale sont refusées avant que le navigateur ne démarre. L'adresse où une page arrive
  après un clic, une touche ou un script est vérifiée de la même façon : une adresse refusée est
  quittée pour une page vide, et la réponse le dit.
- **La page est un graphe.** Un snapshot projette la page dans un `RaggableTree` à elle — jamais
  l'index de code de l'hôte — et chaque nœud porte un identifiant que les actions suivantes
  prennent.

## Le donner à un agent

```yaml
agents:
  - name: researcher
    tools: [firefox]
```

Firefox doit être installé sur l'hôte (`firefox` sur le PATH, ou `ExecutablePath`). L'outil est
enregistré par `AddOrkeonWebTools()`, que chaque hôte livré appelle ; un hôte bibliothèque a aussi
besoin d'un `IFileSystemService` (la capture d'écran passe par lui) et, pour `search`, de
`AddOrkeonEphemeralSearch()` avec un fournisseur d'embeddings configuré — sans lui, toutes les
autres actions fonctionnent et `search` le dit.

## Les actions

Un outil, un argument `action`. La classe de permission d'un appel dépend de l'action : les
actions de lecture passent en mode `plan`, `screenshot` est une édition, les autres sont des
exécutions.

| Action | Arguments | Classe | Ce qu'elle fait |
|---|---|---|---|
| `navigate` | `url` | Read | Ouvre une page http ou https et rend son plan |
| `back` | — | Read | Une page en arrière dans l'historique de l'onglet |
| `snapshot` | `max_nodes`, `offset` | Read | Le plan de la page affichée : sections, liens, contrôles |
| `read` | `node_id`, `offset` | Read | Le texte de la page, ou d'un nœud et des nœuds qu'il contient |
| `find` | `kind`, `name`, `node_id` | Read | Les nœuds d'une sorte (`section`, `link`, `control`), dont le nom contient un texte, sous un nœud |
| `search` | `query`, `max_nodes` | Read | Les sections les plus proches en sens d'une question (RAG) |
| `screenshot` | `save_to_path` | Edit | Écrit le PNG de la partie visible de la page à un chemin virtuel |
| `click` | `node_id` | Execute | Clique un élément, amené à l'écran d'abord |
| `type` | `node_id`, `text` | Execute | Remplace ce qu'un champ contient ; un saut de ligne presse Entrée |
| `press` | `key` | Execute | Une touche ou un accord sur l'élément qui a le focus : `Enter`, `Tab`, `Control+a` |
| `evaluate` | `script` | Execute | Une expression JavaScript exécutée dans la page ; une promesse est attendue |
| `tabs` | — | Read | Les onglets de l'agent, dans l'ordre d'ouverture : id, `url`, `title`, `current` |
| `new_tab` | `url` | Read | Ouvre un onglet, sur `url` quand elle est donnée, et le pilote désormais |
| `switch_tab` | `tab` | Read | Pilote désormais l'onglet `tab` (`t1`, `t2`), affiché devant les autres |
| `close_tab` | `tab` | Execute | Ferme un onglet, le courant par défaut ; le dernier onglet n'est jamais fermé (`close` est là pour ça) |
| `close` | `profile` | Execute | Ferme le navigateur de l'exécution ; l'action suivante en démarre un nouveau. Sous `Lifetime: host`, hors de toute exécution, ferme le navigateur de l'hôte du profil `profile` |

Chaque réponse porte l'action, l'`url` et le `title` de la page affichée, `tab`, la poignée de
l'onglet que les actions pilotent désormais, `profile`, celui avec lequel le navigateur tourne,
et `dialogs` : les boîtes `alert`, `confirm` et `prompt` que la page a ouvertes pendant l'action,
fermées pour que la page ne se fige jamais (une question `beforeunload` est acceptée : partir
était ce que l'agent demandait).

Chaque action accepte un `profile` facultatif : il compte à la première action d'une exécution,
quand le navigateur démarre ; un navigateur déjà ouvert garde son profil, et une action qui en
nomme un autre est refusée jusqu'à `close`. La règle tient sous `Lifetime: host` : l'exécution
est liée au profil de sa première action, et une action sans `profile` va à ce navigateur, quel
que soit le profil par défaut de l'hôte.

### Onglets

Un agent pilote un onglet à la fois et ne voit que les siens : ceux qu'il a ouverts avec
`new_tab`, et ceux qu'un lien d'une de ses pages a ouverts — l'agent suit un tel onglet, comme
le feraient les yeux d'un utilisateur. Un onglet ouvert par l'utilisateur ou par un autre agent
n'est jamais listé, lu ni fermé. `close_tab` sur l'onglet piloté ramène l'agent au précédent.

### Identifiants de nœuds

`snapshot`, `find` et `search` nomment les nœuds `p<lecture>#<index>` — `p2#7` est l'élément 7
de la deuxième lecture d'une page. Les identifiants restent valides tant que la page ne change
pas : saisir dans un champ puis cliquer un bouton ne demande pas de nouveau snapshot. Après une
navigation, ou quand une action a changé la page, un ancien identifiant est refusé avec la
raison, et l'agent rappelle `snapshot`. Une liste est coupée à `MaxNodes` (`Orkeon:Tools:Firefox`,
40 par défaut, 200 au plus) : `truncated` et `total_nodes` le disent, et `offset` lit la suite.

### Un enchaînement typique

```text
navigate  url=https://example.org/docs           → plan : p1#0 section "Getting started" …
search    query="comment configure-t-on une crew ?" → hits : p1#5 (0.82) "Configuration" …
read      node_id=p1#5                           → le texte de cette section
find      kind=control name=search               → p1#12 textbox "Search"
type      node_id=p1#12 text="crew yaml\n"       → la page après Entrée, p2#…
click     node_id=p2#3                           → la page que le lien a ouverte
screenshot save_to_path=/output/docs.png         → saved_path
```

`search` envoie le texte de chaque section de la page au fournisseur d'embeddings comme source
en ligne d'une collection éphémère nommée d'après la page (préfixe `firefox`) : une page lue deux
fois n'est embarquée qu'une fois. Chaque résultat nomme le nœud de la section, et l'agent lit ou
clique ce que la recherche a trouvé : c'est le lien entre le sous-système RAG et le graphe de la
page.

## Ce qui ne passe jamais par l'outil

- Un **secret** : le texte de `type` est un argument d'outil, que le modèle écrit et que la
  transcription garde. Une page qui demande un mot de passe est une page que l'agent ne doit pas
  remplir.
- Une **page hors de la politique** : les sous-ressources qu'une page charge (images, scripts,
  cadres) sont récupérées par Firefox lui-même et ne sont pas vérifiées par la politique d'URL ;
  seules les pages que l'onglet affiche le sont. Gardez le navigateur sur un réseau isolé quand la
  politique compte.
- Le **texte d'une page n'est pas filtré** contre l'injection de consignes : un agent le lit comme
  il lit tout résultat d'outil. Le Guardian de l'hôte s'applique à ce que l'agent dit et fait
  ensuite.

## Profils

Un navigateur démarre sur l'un de trois genres de profil, et le `profile` de la réponse dit lequel :

- **`disposable`**, par défaut : un dossier neuf sous `ProfileRoot`, avec les préférences d'un
  premier démarrage silencieux, supprimé à la fermeture du navigateur. Rien ne survit au navigateur.
- **Un profil du Firefox installé sur l'hôte**, par le nom que `profiles.ini` lui donne
  (`about:profiles` les montre) : utilisé sur place, avec ses cookies, ses sessions, ses extensions
  et son historique, et jamais supprimé. L'agent voit ce que voit l'utilisateur du profil, connecté
  là où il l'est : donnez à un agent un profil fait pour lui, jamais celui de l'opérateur. Un
  profil qu'un autre Firefox tient ouvert est refusé : fermez ce Firefox, ou prenez un autre profil.
- **Un dossier de profil**, par son chemin : la même chose, pour un profil que `profiles.ini` ne
  liste pas.

Qui choisit : `Orkeon:Tools:Firefox:Profile` nomme le profil sur lequel un navigateur démarre
quand l'agent n'en nomme aucun. Un agent peut nommer `disposable`, ce profil, ou l'un de
`AllowedProfiles` — rien d'autre. `DeniedProfiles` l'emporte sur tous : un profil qui y figure ne
démarre aucun navigateur, qui que ce soit qui le demande. Les noms sont comparés sans égard à la
casse.

```json
"Orkeon": { "Tools": { "Firefox": {
  "Profile": "agent-work",
  "AllowedProfiles": ["agent-reviews"],
  "DeniedProfiles": ["default", "default-release"]
} } }
```

## Un navigateur pour plusieurs exécutions

Par défaut, chaque exécution d'agent a son propre navigateur. `SharedInstance: true` donne à
l'hôte un navigateur par profil, partagé entre les exécutions qui l'utilisent : chacune y pilote
ses propres onglets, ne voit qu'eux, et le navigateur se ferme quand la dernière exécution le
quitte — ou quand l'hôte s'arrête. `close` ferme les onglets de l'exécution qui l'appelle, pas le
navigateur que les autres partagent. C'est un profil nommé qui rend le partage utile : plusieurs
agents sur une même session connectée, côte à côte.

## Un navigateur qui survit à l'exécution

`Lifetime: host` garde le navigateur une fois l'exécution terminée : sa fenêtre, ses onglets et
son profil - le jetable compris - restent comme la dernière action les a laissés, et l'exécution
suivante, ou une action hors de toute exécution, les retrouve. Un navigateur par profil : une
action nomme avec `profile` le navigateur qu'elle vise, ou reçoit celui du profil par défaut de
l'hôte. Le navigateur se ferme quand un agent appelle `close`, quand l'utilisateur ferme la
fenêtre, ou quand l'hôte s'arrête ; le dossier jetable part avec lui. Un navigateur que
l'utilisateur a fermé, ou qui est mort, est remarqué à l'action suivante, qui en démarre un
nouveau.

Des exécutions qui se chevauchent sur un même profil mettent leurs actions en file sur son
navigateur, une à la fois : le navigateur de l'hôte est partagé par toutes les exécutions de
l'hôte, ce qui convient à une console à un seul opérateur - un agent qui reprend là où le
précédent s'est arrêté, une page que l'utilisateur lit entre deux exécutions - pas à des agents
qui naviguent côte à côte. Pour ceux-là, `SharedInstance` reste la réponse, et les deux se
combinent : les onglets d'une exécution se ferment avec elle, la fenêtre reste ouverte pour la
suivante sur un onglet vierge.

```json
{ "Orkeon": { "Tools": { "Firefox": { "Lifetime": "host" } } } }
```

Dans une exécution, `close` ferme le navigateur de l'exécution, comme toujours ; hors de toute
exécution - une console, un script - `close` avec un `profile` ferme le navigateur de l'hôte de
ce profil, celui du profil par défaut sans. À chaque démarrage, l'outil supprime les dossiers
jetables sous `ProfileRoot` qu'aucun Firefox ne tient plus.

## Réglages

`Orkeon:Tools:Firefox` — voir la [référence de configuration](../reference/configuration.md#orkeontoolsfirefox) :
`ExecutablePath`, `Headless`, `Profile`, `AllowedProfiles`, `DeniedProfiles`, `SharedInstance`,
`Lifetime`, `ProfileRoot`, `StartupTimeoutSeconds`, `CommandTimeoutSeconds`, `MaxNodes`,
`MaxTextLength`. Quand Firefox est un snap, la racine de profil par défaut est
`~/snap/firefox/common/orkeon/profiles` : un snap ne lit pas le dossier temporaire du système.
