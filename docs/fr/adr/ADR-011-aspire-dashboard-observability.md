> 🇬🇧 [English version](../../adr/ADR-011-aspire-dashboard-observability.md)

> **Voir aussi** : [ADR-010](./ADR-010-agent-framework-interop.md) · [Orkeon Studio](../architecture/studio.md) · [Retour à l'index](../INDEX.md)

# ADR-011 — Le dashboard Aspire est la surface d'observabilité cross-platform ; il n'y a pas de Studio web

**Statut** : Accepté · **Date** : 2026-09-11
· **Périmètre** : `src/hosting/Orkeon.Hosting.Aspire`, `src/packaging/Orkeon.Hosting.Aspire`, `Orkeon.Hosting` (activation de la télémétrie), `Orkeon.Studio.Wpf`

## Contexte

Deux faits se côtoyaient. `Orkeon.Studio.Wpf` est le seul projet ciblant `net10.0-windows` :
le Studio de bureau — lancer, observer, capturer — n'existe que pour Windows, et la question
d'un Studio web revenait à chaque fois que la liste des plateformes revenait. Et les runners
portaient une plomberie OpenTelemetry complète qui n'exportait rien : `orkeon run` et
`orkeon-host` construisaient leur hôte avec le `AddOrkeonInfrastructure()` sans paramètre
(pas de section de télémétrie), ne démarraient jamais l'hôte (le service hébergé
d'OpenTelemetry ne créait donc jamais les providers) et ignoraient la variable standard
`OTEL_EXPORTER_OTLP_ENDPOINT` — un collecteur devait être nommé dans les réglages propres
d'Orkeon.

Pendant ce temps, .NET Aspire livre dans chaque AppHost un dashboard qui rend les traces,
métriques et logs structurés de tout processus qui parle OTLP — cross-platform, maintenu par
Microsoft, déjà ouvert sur la machine du développeur que le README vise désormais.

## Décision

1. **Les runners exportent OpenTelemetry selon le contrat standard.** `RunnerHost` enregistre
   la télémétrie depuis les réglages *et* honore `OTEL_EXPORTER_OTLP_ENDPOINT` (avec le
   protocole et les en-têtes que l'exportateur lit lui-même) pour les traces, les métriques
   et les logs ; il résout les providers de traces et de métriques après avoir construit
   l'hôte, puisque les runners ne le démarrent jamais. Mesuré : avec la seule variable
   d'environnement, un run du quickstart envoie `v1/traces` (`invoke_agent Scribe`,
   `chat llama3.2:1b`, `execute_tool file_write` avec leurs attributs `gen_ai.*`),
   `v1/metrics` et — en verbosité 1 — `v1/logs`.
2. **`Orkeon.Hosting.Aspire` est l'intégration AppHost**, un paquet séparé sur
   `Orkeon` + `Aspire.Hosting` (wrapper PUB-25, neuvième identifiant de la gamme) :
   `AddOrkeonHost` (le démon) et `AddOrkeonCrewRun` (un run) comme ressources exécutables
   avec les réglages, montages et environnement `ORKEON_` qu'un opérateur passerait à la
   main, `.WithOtlpExporter()` appliqué pour que le dashboard reçoive le run. Vérifié : un
   AppHost a lancé le crew du quickstart comme ressource, le crew a écrit `out/hello.md`,
   Aspire a transmis au processus l'endpoint OTLP (épinglé par un test sans lancement sur
   l'environnement évalué).
3. **Le dashboard Aspire est la surface d'observabilité pour toutes les plateformes. Aucun
   Studio web ne sera construit.** `Orkeon.Studio.Wpf` reste ce qu'il est — une application
   de bureau Windows pour lancer, observer et capturer — et ne gagne pas de jumeau web ; ce
   qu'un Studio web aurait montré (les spans, tokens et logs d'un run), le dashboard le
   montre déjà, pour tout le monde, sans code à maintenir ici. La moitié observabilité de la
   préoccupation « Studio Windows seulement » est close par cette décision ; la moitié
   lancement reste sur le bureau.

## Conséquences

- N'importe quel backend OTLP, pas seulement Aspire, reçoit un run sans réglage propre à
  Orkeon : Langfuse, Honeycomb, Application Insights, un OpenTelemetry Collector.
- La télémétrie est désormais *active* dans les runners (un tracer provider OpenTelemetry
  écoute les sources `Orkeon.*`) même sans exportateur ; les spans sont créés puis
  abandonnés. Le coût est quelques allocations par appel LLM ; le bénéfice est qu'un
  exportateur s'attache sans redémarrage.
- L'exemple d'AppHost (`examples/aspire/AppHost/`) requiert `orkeon` sur le PATH ou
  `ORKEON_CLI` ; le dashboard est celui d'Aspire, son jeton de connexion imprimé par
  l'AppHost. Une capture du dashboard avec un run a sa place sous `docs/assets/` une fois
  prise sur un poste de travail — elle n'a pas été prise dans la session qui a pris cette
  décision.
