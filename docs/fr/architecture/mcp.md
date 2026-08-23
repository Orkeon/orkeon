> 🇬🇧 [English version](../../architecture/mcp.md)

# Intégration MCP (Model Context Protocol)

> **Statut** : expérimental (`[Experimental("ORKEXP004")]` sur chaque type MCP — voir
> [APIs expérimentales](../reference/experimental-apis.md)). Bi-génération depuis PUB-07.
> Code : `src/core/Orkeon.Infrastructure/MCP/`.

L'intégration MCP fonctionne dans les deux sens :

- **Client** — `McpClient` parle JSON-RPC à un serveur MCP externe ;
  `McpToolProvider` se connecte à un nombre quelconque de serveurs, liste leurs outils et
  enregistre chacun dans l'`IToolRegistry` d'Orkeon sous forme d'`McpToolAdapter`
  (`IBaseTool`) : les agents utilisent les outils MCP externes comme des outils natifs.
  `DisconnectServerAsync` les désenregistre.
- **Serveur** — `McpServer` expose les outils de l'`IToolRegistry` d'Orkeon aux clients
  MCP externes (`tools/list` / `tools/call`, avec conversion `ToolSchema` → JSON Schema),
  sur stdio (`RunStdioAsync`) ou par traitement de requêtes individuelles
  (`ProcessRequestAsync`).

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

`AddOrkeonMcp` lie `McpOptions` (`Enabled`, `EnableServer`, `Servers` — un dictionnaire de
`McpServerConfig` : `Transport` stdio/sse, `Command`/`Args` ou `Url`) et enregistre
`McpToolProvider` en singleton ; `McpServer` (+ `McpServerOptions` depuis `MCP:Server` :
`Name`, `Version`) n'est enregistré que si `MCP:EnableServer = true`. La surcharge
`AddOrkeonInfrastructure(IConfiguration)` appelle elle-même `AddOrkeonMcp` — mais
**aucune racine de composition livrée n'utilise cette surcharge** (`orkeon run`,
`orkeon-host` et le REPL appellent tous la version sans paramètre) : MCP est de fait
une surface bibliothèque — un hôte qui embarque appelle lui-même
`AddOrkeonMcp(configuration)` (ou la surcharge config).

Il n'y a **pas de hosted service** : l'hôte résout `McpToolProvider` et appelle
explicitement `ConnectServerAsync(serverId, config)` pour chaque serveur configuré (et
`McpServer.RunStdioAsync()` pour servir).

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
