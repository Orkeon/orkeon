---
title: Orkeon
---

> 🇬🇧 [English version](index.md)

# Orkeon

**Des équipes d'agents IA qui restent dans le cadre — chaque fichier, chaque point d'accès et chaque budget qu'un agent peut utiliser est déclaré, puis appliqué par le runtime. Décrites en YAML, TypeScript (`.ork.ts`) ou C# ; un seul runtime .NET exécute les trois, et l'équipe mise au point est celle que l'on intègre, sans réécriture.**

<p align="center">
  <img src="docs/assets/orkeon-mascot.png" alt="Mascotte Orkeon — un caméléon curieux" width="320">
</p>

Orkeon est un framework open source pour construire et orchestrer des équipes d'agents IA en
.NET. Il repose sur deux convictions. **La confiance se construit dans le runtime, pas dans
le prompt** : un système de fichiers virtuel où chaque chemin est un montage déclaré avec des
droits déclarés, un bac à sable pour le code, un budget d'exécution pour l'autonomie, des
capacités sensibles qui restent fermées tant qu'un humain ne les a pas ouvertes — et un
analyseur Roslyn autonome (`Orkeon.Compliance.Vfs`) qui fait du `System.IO` brut une erreur
de compilation dans votre propre code. **Ce que l'on met au point est ce que l'on déploie** :
le runtime qui exécute le premier essai est celui qui tourne dans l'application finale, et
une équipe n'est jamais réécrite pour passer de l'essai au produit. Autour, un framework .NET
complet pour des crews d'agents : six stratégies d'orchestration, 16 fournisseurs LLM,
mémoire, RAG, analyse de code. Il tourne sur un modèle local sans clé API — voir le démarrage
en deux minutes du [README](README.fr.md#essayez-le-en-deux-minutes--sans-clé-dapi).

Chaque porte d'entrée s'appuie sur le même runtime : [Orkeon Studio](https://github.com/Orkeon/orkeon-studio),
une application de bureau pour Windows, sans code ni terminal ;
[Orkeon Workshop](https://github.com/Orkeon/orkeon-workshop), un atelier prêt à l'emploi pour
concevoir, tester et mesurer des équipes ; [Orkeon Code](https://github.com/Orkeon/orkeon-code),
un assistant de codage dans le terminal ; la ligne de commande `orkeon` ; et le paquet NuGet
`Orkeon`.

## Par où commencer

- [**Exécuter votre premier exemple**](docs/fr/getting-started/run-your-first-example.md) — un crew fourni, lancé depuis un clone en quelques minutes
- [**Trois façons d'exécuter Orkeon**](docs/fr/getting-started/three-ways-to-run-orkeon.md) — depuis les sources, un binaire de release ou le conteneur
- [**Démarrage**](docs/fr/getting-started/bootstrap.md) — embarquer Orkeon dans votre propre hôte .NET : paquets, câblage DI, exécution d'un crew
- [**Donner une boîte aux lettres à vos agents**](docs/fr/getting-started/give-your-agents-a-mailbox.md) — Gmail, Outlook.com ou votre propre serveur : un agent lit, range et rédige des brouillons de réponse ; l'envoi reste fermé tant que vous ne l'ouvrez pas
- [**Vue d'ensemble**](docs/fr/getting-started/overview.md) — les concepts : agents, tâches, crews, outils
- [**Documentation**](docs/fr/INDEX.md) — la carte complète : architecture, orchestration, outils, référence
- [**Référence API**](api/index.md) — générée depuis les assemblies publiées (en anglais)

## Installation

```bash
dotnet add package Orkeon        # le framework complet en un seul paquet
dotnet add package Orkeon.Tools  # optionnel : les familles d'outils intégrés
```

La CLI de scripting est distribuée comme outil .NET :

```bash
dotnet tool install -g Orkeon.Scripting.Cli
orkeon run crew.ork.ts
```

Voir la [matrice de publication](docs/fr/reference/publication-matrix.md) pour le lineup
complet (dont les paquets opt-in reranker ONNX, embeddings locaux, Agent Framework et Aspire) et la
[section installation du README](README.fr.md) pour les canaux CLI et conteneur. Pour
mettre à jour — vers la dernière release, ou vers le dernier `main` entre deux releases —
voir [Mettre à jour Orkeon](docs/fr/getting-started/three-ways-to-run-orkeon.md#mettre-à-jour-orkeon).

## Projet

- [README](README.fr.md) — la présentation complète : pourquoi Orkeon, ce qu'une équipe fait, comment ça marche, quelle porte choisir, ce qu'Orkeon garantit et ce qu'il ne garantit pas
- [CHANGELOG](CHANGELOG.md) — le contenu de chaque version (en anglais)
- [Contribuer](CONTRIBUTING.fr.md) · [Support](SUPPORT.fr.md) · [Sécurité](SECURITY.fr.md)
- [Sources sur GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

Ce site documente la version taguée depuis laquelle il a été construit ; la documentation est
publiée en [français](docs/fr/INDEX.md) et en [anglais](docs/INDEX.md).
