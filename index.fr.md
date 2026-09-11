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
framework .NET complet pour des crews d'agents : six stratégies d'orchestration, 14
fournisseurs LLM, mémoire, RAG, analyse de code. Il tourne sur un modèle local sans clé API —
voir le démarrage en deux minutes du [README](README.fr.md#essayez-le-en-deux-minutes--sans-clé-api).

## Par où commencer

- [**Démarrage**](docs/fr/getting-started/bootstrap.md) — votre premier crew, en quelques minutes
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
complet (dont les paquets opt-in reranker ONNX et embeddings locaux) et la
[section installation du README](README.fr.md) pour les canaux CLI et conteneur.

## Projet

- [README](README.fr.md) — la présentation complète, avec le même crew écrit de trois façons
- [CHANGELOG](CHANGELOG.md) — le contenu de chaque version (en anglais)
- [Contribuer](CONTRIBUTING.fr.md) · [Support](SUPPORT.fr.md) · [Sécurité](SECURITY.fr.md)
- [Sources sur GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

Ce site documente la version taguée depuis laquelle il a été construit ; la documentation est
publiée en [français](docs/fr/INDEX.md) et en [anglais](docs/INDEX.md).
