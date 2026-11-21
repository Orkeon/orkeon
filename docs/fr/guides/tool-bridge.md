> 🇬🇧 [English version](../../guides/tool-bridge.md)

# Orkeon dans un conteneur, le navigateur sur le bureau

> **Voir aussi** : [ADR-019](../adr/ADR-019-tool-bridge.md) ·
> [Référence de configuration](../reference/configuration.md#orkeontoolsbridge) ·
> [Référence du CLI](../reference/cli.md#orkeon-bridge) · [Sécurité](../architecture/security.md#pont-doutils) ·
> [L'outil `firefox`](./firefox.md) · [Retour à l'index](../INDEX.md)

Un Orkeon qui tourne dans un conteneur n'atteint pas ce qui vit sur votre machine : le navigateur où vous êtes
connecté, une application de bureau, un périphérique. Le **pont d'outils** fait exécuter certains outils intégrés
d'une exécution sur votre machine, tandis que l'exécution, ses appels au modèle, son pipeline et son espace de travail
restent dans le conteneur. Deux Orkeon y prennent part :

- **le pont**, sur votre machine : `orkeon bridge serve` exécute pour un appelant les outils qu'il sélectionne ;
- **l'appelant**, dans le conteneur : la section `Orkeon:Tools:Bridge` nomme le pont et les outils à y envoyer.

Un outil est ponté quand les deux côtés le sélectionnent. L'appelant continue de juger chaque appel comme il juge un
appel local : sa porte de permissions, son gardien, sa troncature et son assainisseur voient l'appel et son résultat.
Seul l'appel traverse.

## 1. Le pont, sur votre machine

Le fichier de réglages du dossier d'où vous le lancez (`appsettings.json`, ou `--settings <fichier>`) :

```json
{ "Orkeon": { "Bridge": {
  "Allow": ["firefox"]
} } }
```

Le jeton n'est jamais dans un fichier. Choisissez-en un, puis lancez le pont avec lui dans son environnement :

```bash
export ORKEON_BRIDGE_TOKEN="$(openssl rand -hex 32)"
orkeon bridge serve
```

```text
orkeon bridge serve: listening on http://127.0.0.1:7410/
orkeon bridge serve: serving firefox
orkeon bridge serve: Ctrl+C stops it.
```

Il écoute en boucle locale, port 7410 (`http://localhost:7410/` sous Windows), jusqu'à Ctrl+C ou SIGTERM. L'outil
s'exécute avec les réglages du pont : `Orkeon:Tools:Firefox` et la politique d'URL se lisent sur votre machine,
puisque le réseau qu'atteint le navigateur est celui de votre machine.

## 2. L'appelant, dans le conteneur

```json
{ "Orkeon": { "Tools": { "Bridge": {
  "Address": "http://127.0.0.1:7410/",
  "Allow": ["firefox"]
} } } }
```

Le conteneur reçoit le même jeton dans `ORKEON_BRIDGE_TOKEN` (`docker run -e ORKEON_BRIDGE_TOKEN`). Au démarrage,
l'appelant demande au pont quels outils il sert et refuse de démarrer quand le pont est injoignable, refuse le jeton,
ne sert pas un outil que nomme `Allow`, ou en sert un dont le contrat diffère (les deux côtés en exécutent des
versions différentes). Une fois démarré, un appel auquel le pont ne peut répondre échoue avec la raison, et l'appel
suivant réessaie ; un outil ponté ne se rabat jamais sur une exécution dans le conteneur. Une section
`Orkeon:Tools:Firefox` laissée dans le conteneur est signalée une fois et n'est pas lue.

`orkeon run`, `validate`, `--list-tools`, `orkeon mcp serve`, les scripts `.ork.ts`, `eval`, `orkeon-repl` et
`orkeon-host` se connectent tous de la même façon ; `orkeon-host` sort avec 78 sur un refus. `orkeon bridge serve`
lui-même ne se connecte jamais à un pont, quoi que disent ses réglages.

## 3. Atteindre le pont depuis le conteneur

Le pont ne répond en http qu'en boucle locale ; une adresse autre que la boucle locale exige https, pour que le jeton
ne voyage jamais en clair hors de la machine.

- **Un moteur Linux.** Lancez le conteneur sur le réseau de l'hôte : sa boucle locale est alors celle de la machine,
  et l'`Address` de l'appelant est `http://127.0.0.1:7410/`.

  ```bash
  docker run --rm --network host -e ORKEON_BRIDGE_TOKEN -v "$PWD:/workspace" ghcr.io/orkeon/orkeon \
    run /workspace/crew.ork.ts --settings /workspace/appsettings.json --mount /workspace/output:/output:rw
  ```

  L'adresse du pont Docker (`172.17.0.1`) exigerait que le pont serve https, ce qu'il ne fait que sous Windows
  (`HttpListener` n'a pas de TLS sous Linux et macOS) : un `Orkeon:Bridge:Prefix` en https y est refusé au démarrage.
  Sans `--network host`, placez un terminateur TLS de votre choix sur l'adresse du pont Docker, devant l'écouteur en
  boucle locale, et donnez à l'appelant son adresse `https://`.
- **Docker Desktop (Windows, macOS).** `host.docker.internal` atteint un écouteur sur la boucle locale de l'hôte. Le
  pont continue d'écouter en boucle locale ; l'`Address` de l'appelant est `http://host.docker.internal:7410/`, la
  seule adresse http hors boucle locale que l'appelant accepte. L'écouteur ne répond qu'à l'hôte que nomme son
  préfixe, donc l'appelant envoie l'en-tête `Host` `Orkeon:Tools:Bridge:HostHeader`, `127.0.0.1:<port>` quand rien
  ne le fixe : juste pour le préfixe par défaut sous macOS. Sous Windows le préfixe par défaut est
  `http://localhost:7410/` : fixez `HostHeader` à `localhost:7410` (HTTP.sys ne fait correspondre ce préfixe que
  sur l'en-tête `Host`).

  ```json
  { "Orkeon": { "Tools": { "Bridge": { "Address": "http://host.docker.internal:7410/", "HostHeader": "localhost:7410", "Allow": ["firefox"] } } } }
  ```

  Le démarrage consigne un avertissement : en http, le jeton et chaque appel circulent en clair sur le réseau
  virtuel de l'hôte Docker, que d'autres conteneurs de cet hôte peuvent voir. Ce chemin n'est pas encore vérifié
  sur un vrai Docker Desktop ; voir les [Limites connues](../reference/limitations.md).
- **Un pont sous Windows sur une autre adresse.** Un préfixe https autre que `localhost` demande une réservation d'URL
  pour le compte qui exécute le pont, et un certificat lié au port, les deux faits une fois dans une invite élevée :

  ```powershell
  netsh http add urlacl url=https://192.168.1.20:7410/ user=DOMAINE\vous
  netsh http add sslcert ipport=192.168.1.20:7410 certhash=<empreinte> appid={<un GUID quelconque>}
  ```

  Sans la réservation, le démarrage échoue, et le message nomme la commande. Le conteneur doit faire confiance à ce
  certificat (son autorité ajoutée au magasin de confiance de l'image) : l'appelant refuse un certificat qu'il ne peut
  vérifier.

## 4. Ce qui voyage, et pour combien de temps

- **Une exécution de l'appelant est une exécution sur le pont.** Le pont garde une portée par exécution de
  l'appelant, si bien qu'« un navigateur par exécution » signifie ce qu'il signifie en local. L'appelant termine
  cette portée (`POST end-run`) quand sa propre exécution se termine, et bat (`POST heartbeat`) tous les tiers de
  `Orkeon:Bridge:HeartbeatWindow` (30 s, entre 1 s et 10 min) tant qu'il a des exécutions en cours. Un appelant
  silencieux au-delà de la fenêtre perd ses exécutions sur le pont, et leurs navigateurs, en au plus 1,25 fois la
  fenêtre (le pont vérifie tous les quarts de fenêtre) : un appelant qui revient en ouvre de nouvelles.
- **Un chemin reste celui de l'appelant.** Un `screenshot` vers `/output/page.png` arrive dans le montage `/output` de
  l'appelant. Un appel d'un outil qui utilise des fichiers porte les montages de l'appelant et les fichiers que
  nomment ses arguments ; le pont les présente aux mêmes chemins virtuels dans un dossier temporaire le temps de
  l'appel, renvoie ce que l'outil a écrit, puis supprime le dossier. L'appelant écrit ces fichiers par son propre
  système de fichiers, sous les droits de son exécution, et seulement ceux qu'a nommés un argument de chemin de l'appel
  (un paramètre de format `path` ; sinon un paramètre nommé `path`, `file`, `filename`, `filepath`, `dir`, `directory`,
  `folder`, l'un de leurs pluriels, `output`, `destination`, `dest` ou `target`, ou dont le dernier mot, coupé sur `_`,
  `-` et les changements de casse, est l'un des sept premiers ou un pluriel, comme `save_to_path`), à ce chemin ou sous
  ce dossier, pour un appel classé `Edit` ou `Execute` qui a réussi. Un texte saisi dans une page ne nomme jamais un
  chemin, et le répertoire de travail, la racine d'un montage, `/` ou un dossier au-dessus de l'un d'eux n'ouvrent
  jamais tout un arbre. Tout autre fichier est refusé avec un avertissement.
  `Orkeon:Tools:Bridge:MaxFileBytes` et
  `Orkeon:Bridge:MaxFileBytes` (16 Mio chacun) bornent chaque sens.
- **Un dossier voyage avec ses fichiers.** Un argument de chemin qui nomme un dossier de l'appelant envoie les
  fichiers qu'il contient, à toute profondeur, liens symboliques exclus, au plus 256 fichiers par dossier et dans la
  limite de `MaxFileBytes` : au-delà de l'une ou l'autre borne, l'appel échoue avant d'être envoyé. Le répertoire de
  travail, la racine d'un montage, `/` et un dossier au-dessus d'eux ne sont jamais envoyés en entier. Tout autre
  argument texte n'envoie que le fichier qu'il nomme.
- **Les fichiers reviennent tous ou aucun.** L'appelant vérifie chaque fichier de la réponse (la borne de taille, puis
  les règles de son propre système de fichiers) avant d'écrire le premier : un seul fichier refusé fait échouer
  l'appel et aucun n'est écrit. Une écriture qui échoue en cours de route (un disque plein) fait échouer l'appel et
  nomme les fichiers déjà écrits.
- **Un pont perdu est un appel en échec.** Quoi qui échoue en route (le pont injoignable, l'hôte qui s'arrête, un
  argument qui ne peut être envoyé en JSON), l'appel rend un résultat en échec avec la raison du pont, marqué
  `bridge_failure` dans ses métadonnées ; il ne lève jamais d'exception dans l'exécution et ne se replie jamais sur
  l'outil local. Un outil MCP ponté le reste quand son serveur, chez l'appelant, change sa liste (`list_changed`) :
  l'adaptateur de l'appelant n'est jamais remis par-dessus le proxy du pont, et l'appelant journalise
  `mcp.tool.held` une fois.
- **Ce que `*` ne sélectionne jamais.** `"Allow": ["*"]` ne sélectionne, d'un côté comme de l'autre, que les outils
  d'Orkeon sûrs sur une autre machine (`firefox`, `web_search`, `github`...). Il ne sélectionne jamais :
  - un outil qui agit sur l'espace de travail (fichiers, shell, `code_interpreter`, `list_mounts`, index de code) :
    ponté, il agirait sur votre machine alors que l'espace de travail est dans le conteneur ;
  - un outil d'un serveur MCP, ou un outil dont la classe ne déclare aucun `[ToolContract]` (celui d'un plugin, d'un
    script) : ce qu'il atteint sur l'autre machine n'est pas connu ;
  - un outil de l'exécution de l'appelant (`human_input`, `memory_*`, `session_*`, `token_budget`, `spawn_agent`,
    `system_one_decide`, `progress_report`, le bus d'événements, les magasins RAG), marqué
    `[ToolContract(StaysWithCaller = true)]` : sur le pont, il agirait sur l'état du pont, non sur celui de l'exécution ;
  - un outil de délégation à un coéquipier.

  Nommer un outil des deux premières sortes le ponte, et chaque côté journalise un avertissement. Nommer un outil de
  l'exécution de l'appelant ou un outil de délégation refuse le démarrage.

## 5. Quand il ne démarre pas

| Message (abrégé) | Que faire |
| --- | --- |
| `reads its token from the environment variable ORKEON_BRIDGE_TOKEN, which is not set` | exporter le jeton là où ce côté démarre |
| `the token in the environment variable … is shorter than 16 characters` | prendre une longue valeur aléatoire (`openssl rand -hex 32`) |
| `the tool bridge at … refused the token` | donner le même jeton aux deux côtés |
| `Orkeon:Tools:Bridge:Allow names 'x', which the tool bridge at … does not serve` | ajouter `x` à l'`Allow` du pont, ou le retirer de celui de l'appelant |
| `the contract of 'x' differs between this Orkeon and the tool bridge` | exécuter la même version d'Orkeon des deux côtés (l'empreinte couvre la forme du schéma, le transport de fichiers de l'outil et ses classes d'accès) |
| `Allow names 'x', a tool of the caller's own run` | retirer `x` : il reste chez l'appelant |
| `names a host other than loopback over http: such an address requires https` | utiliser la boucle locale (`--network host`) ou https |
| `asks for https, which the tool bridge serves under Windows only` | garder le préfixe en boucle locale en http et utiliser `--network host`, ou placer un terminateur TLS devant |
| `could not listen on …` | un autre processus tient le port, ou, sous Windows, le préfixe n'a pas de réservation d'URL |
