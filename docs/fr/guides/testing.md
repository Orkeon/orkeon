> 🇬🇧 [English version](../../guides/testing.md)

# Enregistrer et rejouer les appels LLM

`--llm-record` fige ce qu'un run réel a envoyé à ses modèles et ce qu'ils ont répondu ; `--llm-replay`
rejoue ce run depuis le fichier, sans appel réseau ni modèle. Un test de crew lit alors une vraie réponse
sans la payer deux fois, et une régression se voit comme un échec de rejeu plutôt que comme une facture qui change.

Pour des réponses écrites à la main, utilisez plutôt [`--llm-mock`](../reference/cli.md#orkeon-run). Les trois
options sont exclusives deux à deux (code `1`, une ligne).

## Enregistrer, puis rejouer

```bash
orkeon run crew.yaml --llm-record cassettes/report.jsonl     # vrai fournisseur, chaque appel écrit
orkeon run crew.yaml --llm-replay cassettes/report.jsonl     # même sortie, sans réseau
```

Les deux mêmes options existent sur un script procédural (`orkeon run script.ork.ts`) et sur `orkeon eval run --crew`. Un rejeu compte comme hors ligne : `--offline --llm-replay` tourne sur n'importe quel profil. Le dossier de la cassette
est monté sur `/llm-cassette` (en lecture-écriture pour enregistrer, en lecture seule pour rejouer) et ne peut
contenir ni le crew ni le script. Le fichier est écrit par le système de fichiers virtuel, comme tout fichier que
le framework touche ; une cassette hors du répertoire de travail demande `--allow-external-mounts`.

Un hôte qui n'utilise pas la CLI pose la même chose dans ses réglages : `Llm:Replay:Mode` (`off`, `record`,
`replay`) et `Llm:Replay:Cassette` (un chemin virtuel), voir [configuration](../reference/configuration.md).

## Ce que contient une cassette

Une ligne JSONL par appel : le profil qui l'a fait, sa clé, son numéro d'occurrence, la réponse (contenu,
nombres de tokens, modèle, métadonnées, corps brut) et, pour un appel en flux, la liste des deltas, fragments
d'appel d'outil compris. Le décorateur se pose dans la fabrique de fournisseurs, sous la couche de mode d'appel,
sur le fournisseur de l'éditeur : la cassette contient les messages qui sont vraiment partis sur le fil, y compris
l'émulation de l'appel d'outil par le prompt pour un modèle sans outils natifs.

La **clé** est le SHA-256 du profil, des messages (rôle, contenu, identifiants d'appel d'outil), des outils, du
format de réponse, du nom du modèle et des options d'échantillonnage (`temperature`, `max_tokens`, réflexion). Les horodatages (`2026-11-08T10:11:12Z`) et les identifiants générés (GUID) du texte sont d'abord
remplacés par une marque fixe : un second run demande donc la même clé. Un appel répété avec la même clé reçoit un
numéro d'occurrence : le n-ième appel identique rejoue la n-ième réponse enregistrée. Des appels identiques faits au
même instant sont servis dans l'ordre où ils arrivent.

Changer le modèle ou l'une de ces options provoque donc un échec : enregistrez de nouveau la cassette. La graine et le
délai ne font pas partie de la clé. Les valeurs de forme secrète ci-dessous sont masquées dans le texte avant son
hachage : la réponse qu'une cassette stocke (masquée) et le tour suivant qui la répète demandent la même clé.

Un appel qui échoue par une exception est enregistré aussi, et un rejeu relève le même genre d'exception (une
`HttpRequestException` avec son statut, un délai dépassé, une limite de débit) : une chaîne de repli se comporte comme
avant. Un rejeu juge aussi la requête comme un appel réel : un son pour un modèle qui n'en prend pas échoue de la même façon.

**Secrets** : avant l'écriture d'une ligne, ces éléments sont remplacés par `[REDACTED]` : la clé d'API avec laquelle
le fournisseur est configuré, un jeton `Bearer`, une clé d'éditeur de la forme `sk-...` (pas un mot qui contient seulement `sk-`), une clé Google de la forme
`AIza...`, la valeur d'un paramètre de requête `key=`, `api_key=`, `apikey=`, `token=` ou `access_token=` d'une URL, et
les informations utilisateur d'une URL (`https://user:motdepasse@hote`). La requête elle-même n'est jamais stockée,
seulement sa clé. Rien d'autre n'est masqué : les réponses sont stockées telles quelles, et un modèle peut répéter ce
qu'on lui a donné. Relisez une cassette avant de la versionner.

## Un échec de rejeu

Un appel que la cassette ne contient pas (un autre prompt, une autre liste d'outils, un appel de plus que
l'enregistrement) échoue par une `ReplayMissException` : le message nomme la cassette, le profil, le numéro d'appel
et le début du prompt. Un rejeu ne retombe jamais sur le réseau, et une cassette inexistante est refusée avant le run.

## Ce que cela ne fait pas

- Les outils s'exécutent de nouveau au rejeu : seules les réponses du modèle sont figées. Un outil à effet de bord l'a deux fois.
- Des appels identiques que deux agents font au même instant prennent les réponses enregistrées dans l'ordre où ils arrivent : un run dont des appels parallèles identiques changent d'ordre peut les permuter.
- Les embeddings et la mémoire ne sont pas des appels LLM et ne sont pas enregistrés.
- Changer un prompt, un outil, le format de réponse, le modèle ou la température change la clé : enregistrez de nouveau la cassette.
