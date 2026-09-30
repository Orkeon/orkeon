> 🇬🇧 [English version](../../architecture/mcp.md)

# Intégration MCP (Model Context Protocol)

> **Statut** : expérimental (`[Experimental("ORKEXP004")]` sur chaque type MCP — voir
> [APIs expérimentales](../reference/experimental-apis.md)). Bi-génération depuis PUB-07.
> Code : `src/core/Orkeon.Infrastructure/MCP/`.

L'intégration MCP fonctionne dans les deux sens :

- **Client** — `McpClient` parle JSON-RPC à un serveur MCP externe ;
  `McpToolProvider` se connecte à un nombre quelconque de serveurs, liste leurs outils et
  enregistre chacun dans l'`IToolRegistry` d'Orkeon sous forme d'`McpToolAdapter`
  (`IBaseTool`). `DisconnectServerAsync` les désenregistre. **Un agent de crew ne peut pas
  encore s'en servir** — voir [Outils MCP et crews](#outils-mcp-et-crews).
- **Serveur** — `McpServer` expose les outils de l'`IToolRegistry` d'Orkeon aux clients
  MCP externes (`tools/list` / `tools/call`, avec conversion `ToolSchema` → JSON Schema),
  sur stdio (`RunStdioAsync`) ou par traitement de requêtes individuelles
  (`ProcessRequestAsync`). Seule la capacité outils est annoncée — ni ressources, ni
  prompts, ni crews — et aucune commande livrée ne le sert : il n'existe pas
  d'`orkeon mcp serve`, c'est un hôte qui embarque qui le démarre lui-même.

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

## Transports

| Transport | Classe | Notes |
|---|---|---|
| stdio | `StdioMcpTransport` | Lance le processus serveur (`McpServerConfig.Command`/`Args`) ; un message JSON-RPC par ligne ; la corrélation des réponses est agnostique de l'id (les ids nombre **ou** chaîne font l'aller-retour). |
| HTTP | `SseMcpTransport` | Forme Streamable HTTP, **mode réponse JSON** : un POST par message. Les requêtes modernes portent les en-têtes `MCP-Protocol-Version`, `Mcp-Method` et `Mcp-Name` (dérivés du message lui-même) ; un corps de réponse encadré SSE (`text/event-stream`) est déballé jusqu'à son payload `data:` final. Les flux initiés par le serveur ne sont pas consommés. |

## Activation

```csharp
services.AddOrkeonMcp(configuration);   // lit la section "MCP"
```

`AddOrkeonMcp` lie `McpOptions` (`Enabled`, `true` par défaut ; `EnableServer`, `false` par
défaut ; `Servers` — un dictionnaire de `McpServerConfig` indexé par identifiant de
serveur : `Transport` `Stdio` (défaut) ou `Sse`, `Command`/`Args`/`Env` pour stdio, `Url`
pour HTTP) et enregistre `McpToolProvider` en singleton ; `McpServer` (+ `McpServerOptions`
depuis `MCP:Server` : `Name`, `Orkeon` par défaut, et `Version`, `1.0.0` par défaut) n'est
enregistré que si `MCP:EnableServer = true`. `McpServerOptions` porte aussi
`ExposeResources` et `ExposePrompts`, que rien ne lit encore. La surcharge
`AddOrkeonInfrastructure(IConfiguration)` appelle elle-même `AddOrkeonMcp`.

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
    "EnableServer": false,
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
continue. Les outils sont enregistrés sous leur propre nom, sans préfixe de serveur, et un
nom déjà tenu par le registre — un outil intégré, un outil de script ou l'outil d'un
serveur connecté plus tôt — est **refusé** : l'outil MCP n'est pas enregistré, une ligne
d'erreur nomme le serveur et l'outil (`The tool 'file_read' of MCP server 'x' collides with
an already-registered tool and was not registered`), dans le journal et sur stderr, et
l'outil qui tenait le nom le garde, y compris après la déconnexion de ce serveur. Les
autres outils du serveur sont enregistrés normalement (`--list-tools` montre la surface
fusionnée). Orkeon Studio écrit la section depuis son onglet « Réglages › MCP ».
Les autres racines livrées ne connectent rien : `orkeon-host`, bâti sur `RunnerHost`,
enregistre `McpToolProvider` depuis la même section, mais charge ses crews sans le pas de
démarrage et ne connecte jamais de serveur ; le REPL appelle `AddOrkeonInfrastructure()`
sans paramètre et n'enregistre aucun MCP. Là, et dans tout hôte qui embarque, MCP reste une
surface bibliothèque — l'hôte appelle lui-même `AddOrkeonMcp(configuration)` (ou la
surcharge config) et connecte lui-même les serveurs.

Il n'y a **pas de hosted service** : un hôte qui embarque résout `McpToolProvider` et
appelle explicitement `ConnectServerAsync(serverId, config)` pour chaque serveur
configuré (et `McpServer.RunStdioAsync()` pour servir) — exactement ce que fait le pas
de démarrage de l'hôte des runners.

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
