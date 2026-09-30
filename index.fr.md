---
title: Orkeon
---

> 🇬🇧 [English version](index.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Mascotte Orkeon — un caméléon curieux" width="96" align="absmiddle"> Orkeon

**Des équipes d'agents IA qui restent dans les clous — chaque fichier, endpoint et budget qu'un agent peut toucher est déclaré, puis appliqué. Décrites en YAML, TypeScript (`.ork.ts`) ou C# ; un seul runtime .NET exécute les trois.**

Un agent lâché sur un dépôt écrit là où il ne devrait pas, parce que rien n'est là pour
l'arrêter. Orkeon place la frontière devant le modèle : un système de fichiers virtuel où
chaque chemin est un montage déclaré avec des droits déclarés, un sandbox pour le code, un
budget d'exécution pour l'autonomie — et un analyseur Roslyn autonome (`Orkeon.Compliance.Vfs`)
qui fait du `System.IO` brut une erreur de compilation dans votre propre code. Autour, un
framework .NET complet pour des crews d'agents : six stratégies d'orchestration, 16
fournisseurs LLM, mémoire, RAG, analyse de code. Il tourne sur un modèle local sans clé API —
voir le démarrage en deux minutes du [README](README.fr.md#essayez-le-en-deux-minutes--sans-clé-api).

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
dotnet add package Orkeon --prerelease        # le framework complet en un seul paquet
dotnet add package Orkeon.Tools --prerelease  # optionnel : les familles d'outils intégrés
```

La CLI de scripting est distribuée comme outil .NET :

```bash
dotnet tool install -g Orkeon.Scripting.Cli --prerelease
orkeon run crew.ork.ts
```

Voir la [matrice de publication](docs/fr/reference/publication-matrix.md) pour le lineup
complet (dont les paquets opt-in reranker ONNX, embeddings locaux, Agent Framework et Aspire) et la
[section installation du README](README.fr.md) pour les canaux CLI et conteneur. Pour
mettre à jour — vers la dernière release, ou vers le dernier `main` entre deux releases —
voir [Mettre à jour Orkeon](docs/fr/getting-started/three-ways-to-run-orkeon.md#mettre-à-jour-orkeon).

## Projet

- [README](README.fr.md) — la présentation complète, avec le même crew écrit de trois façons
- [CHANGELOG](CHANGELOG.md) — le contenu de chaque version (en anglais)
- [Contribuer](CONTRIBUTING.fr.md) · [Support](SUPPORT.fr.md) · [Sécurité](SECURITY.fr.md)
- [Sources sur GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

Ce site documente la version taguée depuis laquelle il a été construit ; la documentation est
publiée en [français](docs/fr/INDEX.md) et en [anglais](docs/INDEX.md).
