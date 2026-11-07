> 🇬🇧 [English version](../../guides/mcp-interop.md)

# Vérifier le client MCP contre le serveur de référence

> **Voir aussi** : [Intégration MCP](../architecture/mcp.md) · [Limites connues](../reference/limitations.md) · [Whisper par MCP](./whisper-mcp.md)

Le client MCP est testé contre des doubles qui parlent le protocole, et contre le serveur de référence du projet MCP,
`@modelcontextprotocol/server-everything`, qui expose des outils, des ressources, des prompts et des
abonnements de ressource. Deux façons de le lancer : le test nocturne, et le MCP Inspector à la main.

## Le test nocturne

`McpReferenceServerInteropTests` (`tests/core/Orkeon.Infrastructure.Tests/MCP/`, `[Trait("Category", "Integration")]`)
démarre `npx -y --prefer-offline @modelcontextprotocol/server-everything` une fois en **stdio** et une fois en **Streamable
HTTP** (`streamableHttp`, sur un port loopback qu'il choisit), le connecte par `McpToolProvider` et vérifie :

- les outils du serveur sont enregistrés (dont `echo`) ;
- les prompts sont listés et l'un d'eux, sans argument, est obtenu ;
- les ressources sont listées (toutes les pages suivies) et l'une est lue ;
- une ressource abonnée est mise à jour par le serveur (`resources/updated`), ce qui prouve que les notifications que le
  serveur envoie de lui-même parviennent au client — en stdio par la sortie du processus, en HTTP par le flux `GET`.

`integration.yml` installe Node avec `actions/setup-node` après le nettoyage du disque du runner (ce nettoyage retire le
npm du runner). Sans `npx`, le test est sauté sur un poste et échoue quand `CI=true`. Il télécharge le paquet : sur une
connexion mesurée, lancez-le une fois avec le cache chaud (`npx -y @modelcontextprotocol/server-everything --help` le
remplit), puis en local :

```bash
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --no-build \
  --filter "FullyQualifiedName~McpReferenceServerInteropTests"
```

`tools/list_changed` n'a pas de déclencheur déterministe sur le serveur de référence : ce chemin est couvert par les
faux serveurs (`McpToolListChangedTests`, `McpSubscriptionTests`).

## À la main avec le MCP Inspector

Le [MCP Inspector](https://github.com/modelcontextprotocol/inspector) est l'interface de débogage du projet. Servez-vous-en pour
voir ce que votre serveur envoie avant d'y pointer Orkeon :

1. Démarrez votre serveur (Streamable HTTP, par exemple `http://127.0.0.1:3001/mcp`).
2. `npx @modelcontextprotocol/inspector`, ouvrez l'URL affichée, choisissez le transport **Streamable HTTP**, saisissez l'URL.
3. Connectez-vous, puis listez outils, ressources et prompts. Comparez avec ce que `orkeon mcp` rapporte pour la même
   entrée `MCP:Servers`.
4. Dans le volet History, vérifiez les en-têtes des POST : `Mcp-Session-Id` est renvoyé après `initialize`, une requête
   répondue `404` fait réinitialiser Orkeon.

Le transport qu'Orkeon ne parle **pas** est le HTTP+SSE de 2024-11-05, où le serveur annonce un endpoint de messages séparé :
l'Inspector le propose sous « SSE » ; choisissez Streamable HTTP.
