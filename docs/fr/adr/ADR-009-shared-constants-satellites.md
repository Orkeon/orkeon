> 🇬🇧 [English version](../../adr/ADR-009-shared-constants-satellites.md)

> **Voir aussi** : [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-006](./ADR-006-rag-subsystem.md) · [Retour à l'index](../INDEX.md)

# ADR-009 — Une constante sur laquelle deux projets doivent s'accorder vit dans un satellite, pas dans une copie

**Statut** : Accepté · **Date** : 2026-08-29
· **Périmètre** : `src/constants/*`, `Orkeon.Domain`, `Orkeon.Infrastructure`, `Orkeon.Hosting`, `Orkeon.Studio.Core`

## Contexte

L'extraction des constantes n'est pas le problème de ce dépôt. Elle est largement faite :
**1030 `const string` dans 270 fichiers**, dont 51 rangés dans des dossiers `Constants/` répartis
sur sept projets, sous une forme qui ne varie jamais — `static class` portant des `const`, aucun
record, aucune énumération détournée en porteur de chaînes.

Ce qui n'est pas résolu, c'est **l'accord entre projets qui n'ont pas le droit de se référencer**.
`Orkeon.Studio.Core` ne peut référencer ni `Orkeon.Infrastructure` ni `Orkeon.Hosting` : Studio
est une application hôte, et dépendre des internes du runtime inverserait précisément la
stratification que la règle Clean Architecture protège. Mais Studio a besoin des mêmes valeurs
que le runtime — l'endpoint LLM qu'il écrit dans un fichier de réglages doit être celui que le
provider appellera, et la racine virtuelle qu'il prédit dans un aperçu de lancement doit être
celle que le runner monte.

L'issue prise jusqu'ici a été de **recopier les valeurs à la main et de garder les copies par un
test**. `tests/apps/Orkeon.Studio.Core.Tests/ConstantDriftTests.cs` n'existe pour rien d'autre.
Il épingle huit familles :

| Famille | Source de vérité | Copie manuelle |
|---|---|---|
| Endpoints LLM (11) | `Constants/Llm/LlmEndpoints.cs` | `Studio.Core/Presets/OrkeonCliDefaults.cs` |
| Modèle par défaut par provider (12) | `Constants/Llm/ProviderDefaults.cs` | `OrkeonCliDefaults.*DefaultModel` |
| Catalogue des providers | `ProviderDefaults` | `Studio.Core/Presets/LlmPresets.cs` |
| Docker Model Runner (3) | `DockerModelRunnerDefaults` | `OrkeonCliDefaults` + `LlmPresets` + un gabarit commité |
| Chemin des réglages globaux | `Hosting/RunnerSettings.cs` | `Studio.Core` `SettingsLocations` |
| Racines virtuelles | `Hosting/RunnerMounts.cs` | `Studio.Core` `MountAutoInjection` |
| Dossier `crew/` promu | `ForgeRenderReader.CrewDirectoryName` | `RunTargetDetector.PromotedCrewDirectoryName` |
| Formulation WIN-01 | `RunnerHost.LlmNotConfiguredMessage` | `AppSettingsValidator.LlmNotConfiguredMessage` |

Les copies le disent elles-mêmes. `MountAutoInjection` documente la racine crew ainsi :
« Mirrors `RunnerMounts.CrewVirtualRoot`; Studio.Core cannot reference Orkeon.Hosting, so a drift
test pins the pair. »

Un test de drift est un vrai filet, et il a attrapé de vraies dérives. C'est aussi la mauvaise
forme de solution, et il échoue d'une manière facile à manquer : il affirme une **égalité deux à
deux entre les constantes que quelqu'un a pensé à lister**. Quand `/sandbox` a rejoint les
racines réservées du runner, le miroir ne l'a jamais gagnée et le test ne pouvait pas s'en
apercevoir — les trois égalités restaient vraies. L'éditeur acceptait `/sandbox` comme mount
utilisateur alors que tous les runners le refusaient.

## Décision

**Une valeur sur laquelle deux projets doivent s'accorder est déclarée une fois, dans un projet
satellite qui ne dépend de rien, et les deux côtés la référencent.**

Quatre satellites sous `src/constants/`, un par famille de vocabulaire :

```
Orkeon.Constants.Llm             endpoints, modèles par défaut, ids de providers, champs de wire
Orkeon.Constants.FileSystem      racines virtuelles, noms conventionnels de dossiers et fichiers
Orkeon.Constants.Configuration   clés Orkeon:*, chemins de réglages, messages partagés
Orkeon.Constants.Cli             verbes et noms d'options
```

Trois règles les bornent :

1. **Le partagé uniquement.** Une constante utilisée par un seul projet reste chez lui, dans son
   dossier `Constants/<Domaine>/` existant. Les satellites ne sont pas un entrepôt ; ce sont le
   lieu où deux projets se rencontrent. Les 51 porteurs existants que personne ne duplique ne
   bougent pas.
2. **Aucune dépendance d'exécution.** Un satellite ne référence ni projet ni paquet. C'est ce qui
   le rend référençable de partout, y compris depuis `Orkeon.Domain`, sans rien inverser.
3. **Des constantes et leurs propres aides de comparaison, rien d'autre.** Aucun comportement,
   aucun type modélisant un concept — cela appartient au Domain. `LlmRoles.Is()` est la borne
   haute : une aide de normalisation sur ses propres valeurs.

### Pourquoi cela ne casse pas la stratification

La règle interdit à un cercle interne de dépendre d'un cercle **externe**. Un projet sans
dépendance n'est dans aucun cercle — c'est un noyau partagé, et ce dépôt en a déjà accepté quatre
sur exactement ce raisonnement : ADR-002 (`Infrastructure → Tools.Abstractions`), ADR-003
(`Application → Analysis.Abstractions`), ADR-005 (`Tools.Web/EventHub → Application`), ADR-006
(`Orkeon.Rag.Abstractions`). Ce qui rendait chacun acceptable, c'est que le projet référencé
porte des contrats, pas de l'infrastructure. Un satellite de `const string` porte encore moins.

**`Orkeon.Domain` a le droit de référencer un satellite.** Domain n'a aujourd'hui aucune
dépendance projet, et l'unique `ProjectReference` qu'il porte — le générateur de source — est en
`OutputItemType="Analyzer"` avec `ReferenceOutputAssembly="false"` : elle disparaît à l'exécution.
Un satellite, lui, est une vraie référence d'exécution, la première de Domain. Elle est acceptée
parce qu'un projet qui ne dépend de rien ne peut rien entraîner derrière lui, et parce que
l'alternative est ce que cet ADR existe pour clore — Domain gardant sa propre copie d'une valeur
qu'un autre détient aussi.

### Ce que « aucune dépendance » veut dire exactement

`src/Directory.Build.props` injecte l'analyseur `Orkeon.Compliance.Vfs` en `ProjectReference` dans
tout projet de `src/`, satellites compris. Cette référence est de compilation
(`OutputItemType="Analyzer"`). La promesse est donc exacte : **aucune dépendance d'exécution**.
Un assemblage satellite s'expédie seul.

## Conséquences

- `ConstantDriftTests` rétrécit d'une assertion par famille migrée, et disparaît quand la
  dernière part. La preuve du changement est un test qui s'efface, pas un test qui s'ajoute.
- Quatre nouveaux projets packageables : entrées `.sln`, `PublicAPI.Shipped.txt` +
  `PublicAPI.Unshipped.txt` (obligatoires — `OrkeonFreezePublicApi` vaut vrai par défaut et passe
  RS0016/RS0017 en erreurs), les compteurs de projets dans `CLAUDE.md` que
  `scripts/check-doc-claims.py` vérifie, et la matrice de publication.
- Les consommateurs gagnent un `using`. Rien d'autre ne change chez eux : les valeurs sont
  identiques, ce que les tests de drift affirmaient depuis le début.
- Un satellite est un paquet NuGet publié. Sa surface est gelée comme les autres, donc une
  constante qui y entre est un engagement public — ce qui est le bon niveau d'exigence pour une
  valeur sur laquelle deux projets s'accordent.

## Ce que cet ADR ne tranche délibérément pas

- **Si les 51 porteurs existants doivent y converger un jour.** Ils restent. La règle est « le
  partagé va en satellite », et un porteur que personne ne duplique n'est pas partagé. Si la
  duplication apparaît plus tard, la famille déménagera à ce moment-là.
- **Les noms d'outils.** `public override string Name => "file_read"` est déclaré une fois, dans
  la classe dont il est l'identité. L'éloigner n'achète rien et coûte une indirection. Seules les
  *références* à un nom d'outil depuis d'autres projets sont candidates.
- **Le texte visible par l'utilisateur.** Une formulation partagée (WIN-01) est une constante
  comme une autre, mais le texte qui appartient à la localisation de Studio passe par
  `IStudioStrings` et y reste.
