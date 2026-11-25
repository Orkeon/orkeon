> 🇬🇧 [English version](../../adr/ADR-023-chat-attach-tool.md)

> **Voir aussi** : [ADR-021](./ADR-021-chat-tool-family.md) · [Hôte de service](../architecture/service-host.md) · [Retour à l'index](../INDEX.md)

# ADR-023 — `chat_attach` : un agent envoie des fichiers à la conversation de son run

**Statut** : Accepté · **Date** : 2026-11-25
· **Portée** : un outil intégré dans `Orkeon.Hosting`, sa conversation dans `Orkeon.Host`

## Contexte

Un run hébergé laisse des fichiers dans `/outbox` ; une personne les prend par `/get`. Un agent ne peut pas en remettre un au cours de son travail. La conversation appartient à l'hôte : un script `.ork.ts` ou un plugin ne la tient pas, et la réponse habituelle du gel ne s'applique donc pas. Le périmètre est gelé (CONTRIBUTING, « Le périmètre est gelé »).

## Décision

1. **Un seul outil, `chat_attach`, la sixième exception motivée**, actée par le propriétaire le 2026-11-25. Il prend des chemins et aucune adresse.
2. **La conversation est ambiante.** `CrewRunner` entre dans une portée qui suit le run, lue à chaque appel. Tout ce que le run démarre la voit ; aucun autre run ne la voit. Elle se ferme avec le run. Hors d'elle, l'appel est une simple erreur d'outil.
3. **Une seule implémentation de ce qui peut sortir**, partagée avec `/get` : `/outbox` et les dossiers que la crew déclare en `download`, tenus au système de fichiers virtuel ; un fichier interdit est un fichier absent. La déclaration est lue par l'hôte ; le fichier est lu sous la portée de l'agent qui le demande, y compris quand le canal l'ouvre.
4. **Un run est borné** : fichiers, messages et octets, comptés avant l'envoi, tous ses agents ensemble.
5. **Il n'est pas de la famille chat** : il n'utilise aucun compte et ne demande aucun droit de l'ADR-021.

### Décisions retenues

| Question | Retenu |
|---|---|
| Où il est enregistré | À côté de `human_input` : le run d'un crew YAML, sa liste, `orkeon doctor`, `orkeon-host`, le REPL. Pas dans `RunnerHost` : `orkeon mcp serve` ne l'expose pas et la forge n'y puise pas. Une crew qui le nomme se charge partout. |
| Son accès | Execute : ce qui est envoyé ne se reprend pas. `StaysWithCaller` : il ne franchit jamais un pont d'outils. |
| `/inbox` | Jamais envoyé : la personne a ces fichiers, et l'un d'eux retiré du canal ne doit pas en ressortir. |
| Teams | Un fichier est proposé par une carte de consentement, dans la conversation personnelle seulement. |
| Ce qui compte | Tout, sauf ce que le canal a trouvé trop gros, qui n'a jamais été posté. |

## Alternatives envisagées

- **Envoyer `/outbox` à la fin de chaque run** : des fichiers non demandés dans un fil, et rien au cours du travail.
- **Un argument `conversation`** : un modèle choisirait qui lit le fichier.
- **Un hôte qui n'enregistre l'outil que dans une conversation** : une crew se chargerait dans un hôte et échouerait dans un autre.

## Conséquences

- 135 outils intégrés.
- Un fichier envoyé est vu de tous dans le fil, là où `/get` répond à une seule personne.
