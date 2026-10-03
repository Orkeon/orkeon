> 🇬🇧 [English version](../../architecture/mcp.md)

# Intégration MCP (Model Context Protocol)

> **Statut** : expérimental (`[Experimental("ORKEXP004")]` sur chaque type MCP — voir
> [APIs expérimentales](../reference/experimental-apis.md)). Bi-génération depuis PUB-07.
> Code : `src/core/Orkeon.Infrastructure/MCP/`.

L'intégration MCP fonctionne dans les deux sens :

- **Client** — `McpClient` parle JSON-RPC à un serveur MCP externe ;
  `McpToolProvider` se connecte à un nombre quelconque de serveurs, liste leurs outils et
  enregistre chacun dans l'`IToolRegistry` d'Orkeon sous forme d'`McpToolAdapter`
  (`IBaseTool`). `DisconnectServerAsync` les désenregistre. Un agent de crew liste un tel
  outil par son nom, comme n'importe quel autre — voir [Outils MCP et crews](#outils-mcp-et-crews).
- **Serveur** — `McpServer` expose les outils de l'`IToolRegistry` d'Orkeon aux clients
  MCP externes (`tools/list` / `tools/call`, avec conversion `ToolSchema` → JSON Schema),
  sur stdio (`RunStdioAsync`) ou par traitement de requêtes individuelles
  (`ProcessRequestAsync`). Chaque `tools/call` franchit le point d'invocation que franchissent
  les appels d'un agent de crew — garde, troncature, assainisseur de résultat, audit. Seule
  la capacité outils est annoncée — ni ressources, ni prompts, ni crews. `orkeon mcp serve`
  le sert sur stdio — voir [Servir les outils avec `orkeon mcp serve`](#servir-les-outils-avec-orkeon-mcp-serve).

## Négociation de version : deux lignées de protocole

`McpProtocol` déclare ce que les deux côtés parlent :

- **Lignée moderne, sans état** — `ModernVersion = "2026-07-28"`. Pas de handshake :
  chaque requête porte `_meta` (`io.modelcontextprotocol/protocolVersion`, `clientInfo`,
  `clientCapabilities`) ; la découverte des capacités est la méthode `server/discover`.
- **Lignée legacy à `initialize`** — `SupportedLegacyVersions = ["2025-11-25",
  "2025-06-18", "2024-11-05"]` : handshake `initialize` classique suivi du
  `notifications/initialized` obligatoire. **`2025-03-26` est délibérément exclue** :
  c'est la seule révision qui impose le batching JSON-RPC, que cette implémentation ne
  parle pas.

### Client (`McpClient.ConnectAsync`)

1. Sonde avec `server/discover` (en déclarant `2026-07-28` dans `_meta`).
2. **Succès avec `supportedVersions`** → serveur moderne ; adoption de la révision la plus
   récente supportée des deux côtés. Un succès *sans* `supportedVersions` est un serveur
   legacy répondant avec indulgence à une méthode inconnue → repli sur `initialize`.
3. **Erreur `-32022` `UnsupportedProtocolVersionError`** → serveur moderne quand même ;
   lecture de sa liste supportée dans les données de l'erreur, renégociation, nouvelle
   tentative de `server/discover`.
4. **Toute autre erreur** → serveur legacy ; handshake `initialize`, qui négocie
   réellement (la révision choisie par le serveur est acceptée si le client la supporte)
   et se termine par `notifications/initialized`.

Sur une connexion moderne établie, chaque requête porte son `_meta` ; un `-32022` en cours
de route déclenche **une renégociation + une nouvelle tentative** avec une révision
supportée des deux côtés (`McpClient.SendAsync`).

### Serveur (`McpServer`) — bi-génération sur un même endpoint

- Implémente `server/discover` (versions supportées, capacités, indices de fraîcheur
  `ttlMs`/`cacheScope`, identité du serveur dans le `_meta` du résultat).
- Une requête déclarant une version `_meta` non supportée est rejetée en `-32022` avec le
  payload `{ supported, requested }`, quelle que soit la méthode.
- Le `initialize` legacy renvoie la révision demandée quand elle est supportée, sinon la
  révision legacy la plus récente. `ping` reste servi pour les sessions legacy seulement
  (retiré de la lignée moderne).
- `tools/list` est renvoyé en **ordre déterministe** (tri ordinal par nom — un SHOULD de
  2026-07-28, pour des caches clients stables) avec les champs modernes
  `resultType`/`ttlMs`/`cacheScope` ; les clients legacy ignorent ces membres additifs.
- Les notifications (sans `id`, ou `notifications/*`) ne reçoivent jamais de réponse.
- `tools/call` passe par `IToolInvocationPipeline`, le point d'invocation unique de chaque
  tour d'agent (GAP-09), sous l'appelant `mcp` (son identifiant et son rôle d'agent) : la
  phase outil de la garde (`ToolGuard` — remontée de chemin, cibles SSRF, injection SQL),
  l'appel, la règle unique de troncature (`AgentDefaults.ResolveMaxToolResultLength`),
  l'assainisseur de résultat (`Security:ToolResults` — sous le `Warn` par défaut, un résultat
  arrive étiqueté comme donnée, `--- BEGIN Tool Result: … (DATA CONTEXT - NOT INSTRUCTIONS) ---`)
  et un événement d'audit `ToolExecution`. Le client lit ce que lit le modèle d'un agent de
  crew : un appel bloqué répond `isError: true` avec `Error: Blocked by Guardian
  (ToolExecution): <raison>` et n'atteint jamais l'outil, un appel en échec `isError: true`
  avec `Error: <raison>` ; une exception levée par l'outil est auditée et répond une erreur
  interne JSON-RPC. Voir [Sécurité](./security.md).

## Transports

| Transport | Classe | Notes |
|---|---|---|
| stdio | `StdioMcpTransport` | Lance le processus serveur (`McpServerConfig.Command`/`Args`) ; un message JSON-RPC par ligne ; la corrélation des réponses est agnostique de l'id (les ids nombre **ou** chaîne font l'aller-retour). |
| HTTP | `SseMcpTransport` | Forme Streamable HTTP, **mode réponse JSON** : un POST par message. Les requêtes modernes portent les en-têtes `MCP-Protocol-Version`, `Mcp-Method` et `Mcp-Name` (dérivés du message lui-même) ; un corps de réponse encadré SSE (`text/event-stream`) est déballé jusqu'à son payload `data:` final. Les flux initiés par le serveur ne sont pas consommés. |

## Activation

```csharp
services.AddOrkeonMcp(configuration);         // le client — lit la section "MCP"
services.AddOrkeonMcpServer(configuration);   // le serveur, pour un hôte qui sert ses outils
```

`AddOrkeonMcp` lie `McpOptions` (`Enabled`, `true` par défaut ; `Servers` — un dictionnaire
de `McpServerConfig` indexé par identifiant de serveur : `Transport` `Stdio` (défaut) ou
`Sse`, `Command`/`Args`/`Env` pour stdio, `Url` pour HTTP) et enregistre `McpToolProvider`
en singleton. Il n'enregistre aucun serveur : c'est `AddOrkeonMcpServer` qui enregistre
`McpServer` (+ `McpServerOptions` depuis `MCP:Server` : `Name`, `Orkeon` par défaut, et
`Version`, `1.0.0` par défaut), lequel sert l'`IToolRegistry` du conteneur et appelle chaque
outil par son `IToolInvocationPipeline` — `AddOrkeonInfrastructure()` et
`AddOrkeonApplication()` enregistrent les deux. Enregistrer n'est pas servir : l'hôte lance
`RunStdioAsync` — ce que fait `orkeon mcp serve` — ou `ProcessRequestAsync` lui-même.
**`MCP:EnableServer` n'existe plus (GAP-24) :** il enregistrait un serveur qu'aucun binaire
livré ne résolvait, si bien que l'écrire ne donnait rien. Une section qui le porte encore,
`true` ou `false`, est refusée au démarrage — par les deux extensions et par chaque runner bâti
sur `RunnerHost`, serveurs déclarés ou non — avec un message qui nomme ce qui l'a remplacé ;
retirez la clé. Le serveur n'expose que des outils — Orkeon n'a
aucun modèle de ressource ni de prompt à servir, donc `resources/*` et `prompts/*` répondent
`Method not found`, et aucune option ne prétend le contraire (les options inertes
`ExposeResources`/`ExposePrompts` ont été supprimées, GAP-11). La surcharge
`AddOrkeonInfrastructure(IConfiguration)` appelle elle-même `AddOrkeonMcp`, jamais
`AddOrkeonMcpServer`.

```json
{
  "MCP": {
    "Enabled": true,
    "Servers": {
      "filesystem": {
        "Transport": "Stdio",
        "Command": "npx",
        "Args": [ "-y", "@modelcontextprotocol/server-filesystem", "/srv/data" ],
        "Env": { "NODE_ENV": "production" }
      },
      "search": { "Transport": "Sse", "Url": "https://mcp.example.com/mcp" }
    },
    "Server": { "Name": "Orkeon", "Version": "1.0.0" }
  }
}
```

Le client se présente comme `Orkeon` `1.0.0`. Le transport HTTP n'envoie aucun en-tête
`Authorization` : un serveur protégé par authentification est hors de portée (voir les
limites plus bas).

**L'hôte partagé des runners honore la section de lui-même (STUDIO-21).** `orkeon run`
— et tout runner bâti sur `RunnerHost` — appelle `AddOrkeonMcp` dès que `MCP:Servers`
déclare au moins un serveur et que `MCP:Enabled` n'est pas `false`, puis connecte chaque
serveur dans un pas de démarrage explicite (`McpStartup`) avant le chargement de la crew
— et avant que `--validate` juge la crew et que `--list-tools` imprime le manifeste, pour
que les trois voient la même surface d'outils. Un serveur qui ne peut pas être connecté
(commande inexistante, point de terminaison muet, poignée de main toujours en attente
après 30 s) coûte une ligne d'erreur qui le nomme, dans le journal et sur stderr, et le run
continue. La sortie d'erreur d'un serveur stdio est lue au fil de l'eau — chaque ligne va au
journal en Debug (`MCP server '<id>' stderr: …`), si bien qu'un serveur qui y écrit beaucoup
ne s'y bloque plus — et un serveur qui s'arrête avant de répondre dit pourquoi : son code de
sortie et la dernière ligne écrite sur sa sortie d'erreur rejoignent cette ligne d'erreur
(`Transport disconnected: the MCP server exited with code 1; its last line on stderr: …`).
Les outils sont enregistrés sous leur propre nom, sans préfixe de serveur, et un
nom déjà tenu par le registre — un outil intégré, un outil de script ou l'outil d'un
serveur connecté plus tôt — est **refusé** : l'outil MCP n'est pas enregistré, une ligne
d'erreur nomme le serveur et l'outil (`The tool 'file_read' of MCP server 'x' collides with
an already-registered tool and was not registered`), dans le journal et sur stderr, et
l'outil qui tenait le nom le garde, y compris après la déconnexion de ce serveur. Les
autres outils du serveur sont enregistrés normalement (`--list-tools` montre la surface
fusionnée). Orkeon Studio écrit la section depuis son onglet « Réglages › MCP ».
**`orkeon-host` les connecte aussi (GAP-11) :** bâti sur `RunnerHost`, il lit la même
section et exécute le même pas, une fois, depuis son premier service hébergé, au démarrage
du démon — avant que le canal de chat puisse livrer un message qui charge une crew — et
déconnecte les serveurs à l'arrêt, après le drainage. Les mêmes réglages donnent à
`orkeon run` et à `orkeon-host` les mêmes outils ; un serveur injoignable coûte la même
ligne d'erreur et le démon démarre sans lui. Le REPL appelle `AddOrkeonInfrastructure()`
sans paramètre et n'enregistre aucun MCP ; là, et dans tout hôte qui embarque, MCP reste une
surface bibliothèque — l'hôte appelle lui-même `AddOrkeonMcp(configuration)` (ou la
surcharge config) et connecte lui-même les serveurs.

La bibliothèque ne livre **pas de hosted service** pour cela : un hôte qui embarque résout
`McpToolProvider` et appelle explicitement `ConnectServerAsync(serverId, config)` pour
chaque serveur configuré — exactement ce que fait le pas de démarrage de l'hôte des runners,
et ce qu'appelle le service de connexion d'`orkeon-host`. Un hôte qui sert ses outils appelle
`AddOrkeonMcpServer(configuration)`, puis résout `McpServer` et lance `RunStdioAsync()` — ce
que fait `orkeon mcp serve`. `orkeon-host` ne sert pas MCP.

### Outils MCP et crews

Un outil MCP connecté est un outil comme les autres : un agent de crew le liste par son nom,
en YAML ou dans un script `.ork.ts`, et le reçoit.

```yaml
agents:
  analyst:
    tools: [search_issues]   # un outil exposé par un serveur MCP déclaré dans MCP:Servers
```

`CrewFactory` attache tout outil que le registre résout ; il n'y a pas d'interface plus
étroite qu'`IBaseTool` à implémenter. Les serveurs sont connectés avant le chargement de la
crew : sous `StrictTools` — le défaut des runners — une crew qui nomme l'outil d'un serveur
qui n'a pas pu être connecté échoue au chargement avec la ligne ordinaire `unknown tool(s)`.
`--list-tools` et `--validate` voient les outils MCP, `McpServer` ré-expose ce que contient
le registre, et du code C# peut en résoudre un par `IToolRegistry.GetToolByNameAsync(name)`.
Le décorateur des runs observés de `--events jsonl` enveloppe les outils enregistrés dans la
DI ; les outils MCP arrivent dans le registre à l'exécution et ne sont pas enveloppés.

## Servir les outils avec `orkeon mcp serve`

```bash
orkeon mcp serve [--settings <fichier>] [--tools a,b,…]
```

Le verbe sert les outils d'un hôte Orkeon à un client MCP sur **stdio** — le transport par
lequel Claude Desktop, les éditeurs et les frameworks d'agents démarrent un serveur local : le
client lance le processus, écrit un message JSON-RPC par ligne sur son stdin et lit les
réponses sur son stdout, une par ligne ; fermer stdin arrête le serveur.

- **L'hôte est celui que construit un run.** Les réglages se résolvent comme ceux
  d'`orkeon run`, ancrés au répertoire courant : `--settings`, sinon `appsettings.json` à cet
  endroit, sinon un `appsettings/appsettings.json` trouvé en remontant, sinon le fichier par
  utilisateur d'`orkeon init`, sinon les seules variables d'environnement `ORKEON_*` ; le
  fichier retenu est nommé sur stderr. Les montages qu'il déclare (`Orkeon:FileSystem:Mounts`)
  sont ce qu'atteignent les outils de fichiers, et les serveurs MCP qu'il déclare sous
  `MCP:Servers` sont connectés d'abord, leurs outils servis aussi. Ce qui est servi est ce
  qu'imprime `orkeon run --list-tools` pour les mêmes réglages, `human_input` à part : cet
  outil répond pour l'opérateur d'un run, et le client d'un serveur MCP a son propre humain.
  Aucune crew n'est chargée, donc aucun outil que déclare une crew `.ork.ts` n'est servi : ceux-là
  sont enregistrés au chargement de leur crew.
- **`--tools a,b,…`** ne sert que les outils nommés (séparés par des virgules) ; un nom que
  l'hôte n'a pas refuse le démarrage en le nommant.
- **Chaque appel est gardé** : le point d'invocation du [serveur](#serveur-mcpserver--bi-génération-sur-un-même-endpoint),
  sous l'appelant `mcp` — un appel bloqué n'atteint jamais l'outil, un résultat arrive tronqué et
  étiqueté comme donnée, et la piste d'audit enregistre chaque appel sous l'agent `mcp` ; un appel
  bloqué ou en échec atteint aussi stderr, par le puits de journal de la piste (`Agent=mcp`).
- **Stdout ne porte que le protocole.** Chaque ligne de journal, avertissement et diagnostic
  va sur stderr — là où un client MCP enregistre la sortie d'un serveur —, de même que
  `--help` et les erreurs d'usage.
- **Codes de sortie** : `0` le client a fermé le flux (ou `--help`) ; `1` une erreur d'usage,
  un réglage refusé (`MCP:EnableServer`, un montage déclaré par les réglages qui ne peut pas
  être monté, tout autre réglage que refuse l'hôte des runners — une ligne `ERROR:` le nomme),
  un nom de `--tools` que l'hôte n'a pas, un serveur lancé par un autre (plus bas) ; `2` une
  erreur inattendue. Ctrl+C termine le processus.

Une configuration client — le `claude_desktop_config.json` de Claude Desktop :

```json
{
  "mcpServers": {
    "orkeon": {
      "command": "orkeon",
      "args": ["mcp", "serve", "--settings", "/home/moi/orkeon/appsettings.json"]
    }
  }
}
```

Un client démarre ses serveurs dans un répertoire de travail de son choix : nommez le fichier
de réglages avec `--settings`, ou comptez sur le fichier par utilisateur d'`orkeon init`.

**Un serveur ne démarre jamais sous un autre.** Avant de connecter les serveurs MCP de ses
réglages, un serveur pose `ORKEON_MCP_SERVE` dans son propre environnement — chaque processus
qu'il lance en hérite, et la variable est retirée quand il s'arrête — et un serveur qui la
trouve posée refuse de démarrer : code `1`, et une ligne sur stderr, que le serveur qui l'a
lancé joint à son erreur de connexion
(``orkeon mcp serve: refused to start: another `orkeon mcp serve` started this one (ORKEON_MCP_SERVE is set), …``).
Des réglages qui déclarent `orkeon mcp serve` lui-même sous `MCP:Servers` ne coûtent donc
plus que cette ligne, là où chaque serveur en démarrait un autre avant de répondre, jusqu'à
ce que le premier abandonne au bout de 30 s : retirez l'entrée, ou déclarez les deux
serveurs côté client. `orkeon run` et `orkeon-host` ne posent aucun marqueur, et peuvent
utiliser un `orkeon mcp serve` lancé sur d'autres réglages. Comme `ORKEON_DEBUG`, la variable
est aussi lue par la couche de configuration `ORKEON_`, comme une clé (`MCP_SERVE`) qui n'est
aucun réglage.

## Limites honnêtes

Alignées sur [Limitations connues](../reference/limitations.md) et l'entrée PUB-07 du
changelog :

- **Pas de `subscriptions/listen`** — les notifications de changement poussées par le
  serveur ne sont pas consommées.
- **Pas de requêtes multi-aller-retour (MRTR)** — un résultat intermédiaire moderne
  `input_required` remonte comme une erreur d'outil explicite, jamais comme des données
  partielles.
- **Pas d'autorisation MCP (OAuth)**.
- **HTTP = mode réponse JSON uniquement** — en-têtes modernes envoyés, corps SSE
  déballés, mais pas de streaming initié par le serveur.
- **`2025-03-26` exclue** (batching JSON-RPC obligatoire).
- **Le serveur ne parle que stdio** (`orkeon mcp serve`, ou `RunStdioAsync` /
  `ProcessRequestAsync` dans un hôte C#) et répond à une requête à la fois : pas de
  transport HTTP, ni ressources, ni prompts, ni crews exposées comme outils.
- **L'interop contre les serveurs de référence (MCP Inspector) n'a pas encore tourné** —
  suivie dans la note de clôture de PUB-07.

## Statut expérimental

Chaque type MCP porte `[Experimental("ORKEXP004")]` : y faire référence depuis votre code
est une erreur de compilation tant que le diagnostic n'est pas supprimé — cette
suppression vaut opt-in et reconnaissance que la surface (notamment les types wire) peut
encore bouger. Voir [APIs expérimentales](../reference/experimental-apis.md).

---

> **Voir aussi** : [Sous-systèmes opt-in](../reference/opt-in-subsystems.md) ·
> [APIs expérimentales](../reference/experimental-apis.md) ·
> [Limitations connues](../reference/limitations.md) ·
> [Retour à l'index](../INDEX.md)
