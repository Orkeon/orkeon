> 🇬🇧 [English version](../../adr/ADR-020-agent-cli-provider.md)

> **Voir aussi** : [ADR-010](./ADR-010-agent-framework-interop.md) · [ADR-019](./ADR-019-tool-bridge.md) · [Retour à l'index](../INDEX.md)

# ADR-020 — Un fournisseur expérimental `agent-cli` : une CLI d'agent répond, Orkeon garde ses outils et son historique

**Statut** : Accepté (expérimental, `ORKEXP008`) · **Date** : 2026-11-22
· **Portée** : `Orkeon.Application` (`Interfaces/Ports/ILlmProviderModule.cs`), `Orkeon.Infrastructure` (`LLMs/LlmProviderFactory.cs`, `LLMs/Profiles/LlmSettings.cs`), `Orkeon.Interop.AgentCli`, `Orkeon.Interop.AgentCli.Relay`, et les trois binaires qui l'enregistrent (`orkeon`, `orkeon-repl`, `orkeon-host`).

## Contexte

Un utilisateur qui a un abonnement Claude et pas de clé d'API ne peut pas y faire tourner Orkeon : chaque fournisseur
parle l'API HTTP d'un vendeur et facture une clé. La CLI d'agent du vendeur, `claude`, tourne sur l'abonnement auquel
elle est connectée, et en mode print (`claude -p --input-format stream-json --output-format stream-json`) elle lit des
tours sur son entrée standard et écrit chaque événement d'un tour sur sa sortie. Piloter ce processus donne à Orkeon
un modèle sur l'abonnement, sans réimplémenter la connexion du vendeur. La CLI est un agent, pas un point d'accès de
modèle : elle a ses propres outils, garde sa propre conversation et la compacte quand elle veut. Le contrat d'Orkeon
est l'inverse : une crew envoie tout l'historique, les outils sont ceux d'Orkeon et son pipeline juge chaque appel. Le
périmètre du framework est gelé à ses seize fournisseurs ; un nouveau demande une exception motivée.

## Décision

1. **Un fournisseur livré hors du cœur.** `Llm:Provider` peut nommer n'importe quelle clé. `mock` garde son sens ;
   toute autre clé est un **module de fournisseur** (`ILlmProviderModule`, enregistré dans le conteneur) : la fabrique
   lui remet la section et construit le fournisseur par la même entrée que les autres (cassette, mode d'appel,
   métrage), ne déduit jamais un autre fournisseur de l'adresse ou du nom du modèle, et refuse une clé à laquelle
   aucun module ne répond. `orkeon doctor` et `orkeon llm probe` lisent les modules que l'outil livre.
2. **La clé est générique.** La clé du module est `agent-cli` et son moteur `claude` (`Llm:AgentCli:Engine`) : aucun
   nom de vendeur dans la clé du fournisseur, pour qu'une autre CLI soit un second moteur, pas un second fournisseur.
3. **Les outils d'Orkeon, exécutés par Orkeon.** La CLI démarre avec ses propres outils coupés (`--tools ""`,
   `--strict-mcp-config`) et un seul serveur MCP, `orkeon`, joint par un relais que la CLI lance
   (`Orkeon.Interop.AgentCli.Relay`, sur une socket de domaine Unix dans un dossier temporaire privé). Quand le
   modèle appelle un outil, le fournisseur arrête le tour sur le `message_stop` qui le termine et répond à la crew par
   des blocs Anthropic `tool_use` ; la crew exécute l'outil par son propre pipeline, et l'appel suivant remet le
   résultat à l'appel que la CLI attend. Un appel refusé parvient à la CLI comme un résultat en erreur.
4. **Orkeon garde l'historique.** Chaque appel envoie tout l'historique, comme pour tout fournisseur. Le fournisseur
   garde un processus CLI par conversation pour la durée du run (`AgentRunScope`), et le poursuit quand l'historique
   prolonge ce que la CLI tient déjà ; tout autre historique — réécrit, compacté par Orkeon, ou une CLI qui s'est
   compactée elle-même — démarre un nouveau processus amorcé avec l'historique en texte. La compaction propre de la
   CLI est repoussée au-delà de toute fenêtre (`Llm:AgentCli:AutocompactTokens`).
5. **Sur la connexion, jamais une clé.** Les variables de clé d'API de l'hôte sont retirées de l'environnement de la
   CLI, et une réponse sur une clé plutôt que sur la connexion est refusée (`Llm:AgentCli:RequireLogin`). Le coût vaut
   0 : le chiffre du vendeur est gardé sous `agent_cli_notional_cost_usd`. Une limite d'abonnement
   (`rate_limit_event` refusé) est une limite de débit, donc `Fallback` prend le relais.
6. **Les processus sont bornés et ne survivent jamais à l'hôte.** `Llm:AgentCli:MaxProcesses` (4) ferme d'abord une
   session inactive et n'attend jamais ; un appel annulé interrompt le tour et ferme son processus ; le relais tue
   l'arbre de processus de la CLI quand la socket de l'hôte se ferme, même après un `kill -9` de l'hôte.
7. **Expérimental.** Chaque symbole public porte `[Experimental("ORKEXP008")]`, le projet vit dans `src/interop`, et
   le fournisseur n'est pas compté parmi les seize. Il est fait pour un usage personnel, sur l'abonnement de qui le
   lance.

## Conséquences

- Orkeon tourne sur un abonnement Claude avec la crew, les outils et le pipeline qu'il a sur une clé d'API.
- Un appel coûte le démarrage d'un processus CLI sur une nouvelle conversation, et le prompt système et les règles
  d'outils du vendeur entourent ceux d'Orkeon ; les réponses peuvent différer de celles du même modèle derrière l'API.
- Les options que la CLI ne peut pas recevoir (température, top-p, un schéma JSON, un budget de réflexion) font
  l'objet d'un avertissement unique et ne sont pas envoyées.
- Le flux de la CLI est le protocole d'un vendeur, qui peut changer avec ses versions ; le statut expérimental le
  couvre.
- Laissé pour plus tard : les outils propres de la CLI derrière la barrière d'Orkeon, un fournisseur qui tient le
  contexte lui-même, d'autres moteurs.
