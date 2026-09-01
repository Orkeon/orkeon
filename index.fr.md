---
title: Orkeon
---

> 🇬🇧 [English version](index.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Mascotte Orkeon — un caméléon curieux" width="96" align="absmiddle"> Orkeon

**Créez et orchestrez des équipes d'agents IA — décrites en YAML déclaratif, en TypeScript
programmatique (`.ork.ts`) ou en C# pur ; une seule pile full-.NET les exécute toutes**

Orkeon est un framework C# pour créer et piloter des équipes d'agents IA collaboratifs qui
traitent des tâches complexes et multi-étapes à l'aide de grands modèles de langage. Les agents
sont organisés en crews, chacun avec un rôle, un objectif et un outillage définis, et
collaborent selon l'une des six stratégies d'orchestration (séquentielle, hiérarchique,
parallèle, consensuelle, graphe ou autonome).

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
dotnet tool install -g orkeon --prerelease
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
