# Prompt de vérification : correction dépendance Tools.Abstractions → Application

Copie ce prompt dans Claude Code pour vérifier que la correction est complète.

---

```
Tu dois vérifier que la suppression de la dépendance Orkeon.Tools.Abstractions → Orkeon.Application est correcte et que rien n'est cassé. Exécute ces étapes dans l'ordre.

## Contexte

On a déplacé IPathValidator, PathValidationResult, IUrlValidator et UrlValidationResult de :
  Orkeon.Application.Interfaces.Security → Orkeon.Domain.Tools.Security

Puis supprimé la ProjectReference Application du .csproj de Tools.Abstractions.

## 1. Vérification de la dépendance

Vérifie que src/tools/Orkeon.Tools.Abstractions/Orkeon.Tools.Abstractions.csproj ne contient PLUS de référence à Orkeon.Application. Il ne doit référencer QUE Orkeon.Domain.

Vérifie aussi qu'aucun fichier .cs dans src/tools/ ne contient "using Orkeon.Application".

## 2. Vérification des nouveaux fichiers Domain

Vérifie que ces 2 fichiers existent et contiennent les types attendus :
- src/core/Orkeon.Domain/Tools/Security/IPathValidator.cs (interface + PathValidationResult record)
- src/core/Orkeon.Domain/Tools/Security/IUrlValidator.cs (interface + UrlValidationResult record)

## 3. Vérification que les anciens fichiers Application sont supprimés

Vérifie que ces fichiers N'EXISTENT PLUS :
- src/core/Orkeon.Application/Interfaces/Security/IPathValidator.cs
- src/core/Orkeon.Application/Interfaces/Security/IUrlValidator.cs

## 4. Build de la chaîne de dépendances

Build dans l'ordre des dépendances pour isoler les erreurs :
```bash
dotnet build src/core/Orkeon.Domain/Orkeon.Domain.csproj --configuration Release
dotnet build src/tools/Orkeon.Tools.Abstractions/Orkeon.Tools.Abstractions.csproj --configuration Release
dotnet build src/tools/Orkeon.Tools.Code/Orkeon.Tools.Code.csproj --configuration Release
dotnet build src/tools/Orkeon.Tools.Data/Orkeon.Tools.Data.csproj --configuration Release
dotnet build src/tools/Orkeon.Tools.FileSystem/Orkeon.Tools.FileSystem.csproj --configuration Release
dotnet build src/tools/Orkeon.Tools.Web/Orkeon.Tools.Web.csproj --configuration Release
dotnet build src/core/Orkeon.Application/Orkeon.Application.csproj --configuration Release
dotnet build src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj --configuration Release
```

Si un build échoue à cause d'un type IPathValidator/IUrlValidator/PathValidationResult/UrlValidationResult introuvable, ajoute "using Orkeon.Domain.Tools.Security;" dans le fichier concerné et relance.

## 5. Build complet de la solution

```bash
dotnet build Orkeon.sln --configuration Release
```

## 6. Tests

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --configuration Release --verbosity normal
dotnet test tests/core/Orkeon.Application.Tests/Orkeon.Application.Tests.csproj --configuration Release --verbosity normal
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --configuration Release --verbosity normal
dotnet test tests/plugins/Orkeon.Plugins.Tests/Orkeon.Plugins.Tests.csproj --configuration Release --verbosity normal
```

## 7. Vérification d'architecture

Confirme le graphe de dépendances final :
- Orkeon.Domain : aucune ProjectReference
- Orkeon.Application : → Domain uniquement
- Orkeon.Tools.Abstractions : → Domain uniquement (PAS Application)
- Orkeon.Tools.Code/Data/FileSystem/Web : → Tools.Abstractions uniquement
- Orkeon.Infrastructure : → Domain + Application + tous les Tools.*
- Orkeon.Plugins : → Domain uniquement

Pour chaque .csproj ci-dessus, affiche ses ProjectReference et vérifie qu'elles correspondent au graphe.

## 8. Rapport final

Produis un rapport avec :
- ✅ / ❌ pour chaque étape
- Le graphe de dépendances vérifié
- Nombre de tests exécutés / passés / échoués
- Liste des corrections appliquées (si applicable)
```
