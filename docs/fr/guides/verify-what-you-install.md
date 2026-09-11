> 🇬🇧 [English version](../../guides/verify-what-you-install.md)

# Vérifier ce que vous installez

> **Voir aussi** : [Politique de sécurité](../../../SECURITY.fr.md) · [Matrice de publication](../reference/publication-matrix.md) · [Retour à l'index](../INDEX.md)

Chaque artefact Orkeon — les paquets NuGet, les installeurs, les archives CLI, le `.deb`,
les MSI — est construit par un workflow GitHub Actions public et **attesté** : GitHub signe
une déclaration disant *ce fichier exact a été produit par ce workflow, à ce commit, dans ce
dépôt*. Vous pouvez vérifier cette déclaration vous-même, avec des outils que vous avez
déjà, sans faire confiance au mainteneur, à la page de téléchargement, ni à ce document.

Cette page liste ce que la chaîne prouve, ce qu'elle ne prouve pas, et les commandes —
chacune a été exécutée sur les artefacts `v1.0.0-rc.3` avant d'être écrite ici.

## Ce que la chaîne établit

| Mécanisme | Où il vit | Ce qu'il prouve |
|---|---|---|
| **Trusted Publishing (OIDC)** vers NuGet.org | `publish.yml`, étape *NuGet.org login* (`NuGet/login`) | Le push a utilisé une clé éphémère émise pour ce seul run de workflow. Il n'existe aucune clé API NuGet longue durée — aucune ne peut fuiter, aucune n'est à faire tourner. |
| **Attestation de provenance de build** (SLSA v1, Sigstore) | `publish.yml`, étape *Attest the packages* — sur chaque `*.nupkg` ; `release.yml`, étape *Attest the release assets* — sur chaque archive, `.deb` et MSI | Le SHA-256 du fichier est consigné dans une déclaration signée par l'instance Sigstore de GitHub, qui nomme le fichier de workflow, le tag, le commit et le run. Un fichier au digest différent n'a pas de déclaration. |
| **`ContinuousIntegrationBuild=true`** au pack | `publish.yml`, étape `dotnet pack` | Les chemins dans les PDB et les assemblies sont normalisés : les octets packés ne dépendent pas de l'arborescence du runner. C'est un réglage de *build déterministe*, pas une garantie que vous puissiez reconstruire les octets identiques vous-même — il faudrait le même SDK, la même image de runner et le même graphe NuGet. |
| Manifestes **`SHA256SUMS`** | Assets de release (`SHA256SUMS`, et `SHA256SUMS.msi` pour les MSI) | Intégrité de ce que vous avez téléchargé par rapport à ce que le workflow a envoyé. Bon marché, hors ligne, mais le manifeste n'est lui-même qu'un asset : c'est l'attestation qui le relie au workflow. |
| **Actions et images de base épinglées** | Chaque `uses:` est un SHA de commit ; chaque `FROM` est un digest | Ce qui a tourné sur le runner est ce que le dépôt dit avoir tourné. |

Ce qu'elle n'établit **pas** : quoi que ce soit sur la *qualité* du code. Une attestation
dit que les octets sont sortis de `.github/workflows/publish.yml` au commit X — pas que le
commit X est sans bug, sûr à exécuter avec vos identifiants, ni relu par qui que ce soit.
Lisez le [modèle de menace](../../../SECURITY.fr.md#modèle-de-menace--exécution-doutils-pilotée-par-llm) pour cela.

## Vérifier un asset de release (installeur, archive CLI, `.deb`, MSI)

Les assets de release sont attestés **tels qu'envoyés** : le fichier téléchargé depuis la
page Releases a le digest de la déclaration.

```bash
VER=1.0.0-rc.3
BASE="https://github.com/Orkeon/orkeon/releases/download/v$VER"
curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz" -O "$BASE/SHA256SUMS"

# 1. intégrité — le manifeste a voyagé avec le fichier
sha256sum --check --ignore-missing SHA256SUMS
#    orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz: OK

# 2. provenance — la déclaration signée de GitHub sur ce digest exact (gh >= 2.49)
gh attestation verify "orkeon-cli-$VER-osx-arm64.tar.gz" --repo Orkeon/orkeon
```

La seconde commande affiche le workflow qui l'a construit
(`.github/workflows/release.yml@refs/tags/v1.0.0-rc.3`) et échoue au moindre octet
différent. Sans `gh`, interrogez directement l'API publique — aucun token n'est requis
pour un dépôt public :

```bash
D=$(sha256sum "orkeon-cli-$VER-osx-arm64.tar.gz" | cut -d' ' -f1)
curl -fsSL "https://api.github.com/repos/Orkeon/orkeon/attestations/sha256:$D"
```

Un tableau `attestations` vide (ou un 404) signifie *aucune déclaration pour ces octets*.
Mesuré sur `orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz` : une déclaration, prédicat
`https://slsa.dev/provenance/v1`, builder
`https://github.com/Orkeon/orkeon/.github/workflows/release.yml@refs/tags/v1.0.0-rc.3`,
onze sujets (chaque archive, le `.deb` et les deux MSI de cette release).

## Vérifier un paquet NuGet

Deux choses sont vraies en même temps pour un paquet sur nuget.org, et la seconde surprend :

1. **nuget.org signe (signature « repository ») chaque paquet qu'il accepte.**
   `dotnet nuget verify --all` contrôle cette signature — elle prouve que les octets n'ont pas
   été altérés *après* réception par nuget.org. Les paquets Orkeon ne portent pas de
   signature d'auteur ; la signature repository est la seule.
2. **La signature repository modifie le fichier.** nuget.org ajoute une entrée
   `.signature.p7s` et réécrit le répertoire du zip : le digest du paquet téléchargé n'est
   **pas** celui que GitHub a attesté — l'attestation couvre les octets produits par
   `dotnet pack`, avant le push. `gh attestation verify` sur le `.nupkg` téléchargé échoue
   donc, et cet échec n'est pas une preuve d'altération.

La signature est toujours ajoutée en dernier : les octets d'origine se retrouvent
exactement — sans re-zipper — avec un script en bibliothèque standard de ce dépôt :

```bash
VER=1.0.0-rc.3
curl -fsSL -o "Orkeon.$VER.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/orkeon/$VER/orkeon.$VER.nupkg"

# 1. la signature repository de nuget.org — intacte depuis que nuget.org l'a stockée
dotnet nuget verify --all "Orkeon.$VER.nupkg"
#    Signature type: Repository — CN=NuGet.org Repository by Microsoft …

# 2. les octets tels qu'attestés — retirer la signature repository ajoutée
python3 scripts/nupkg-unsign.py "Orkeon.$VER.nupkg"
#    Orkeon.1.0.0-rc.3.unsigned.nupkg  sha256:5800062e39814ec846cae934d9344b85103b9134948cdff42b56a99c55cbb8e7

# 3. provenance de ces octets
gh attestation verify "Orkeon.$VER.unsigned.nupkg" --repo Orkeon/orkeon
```

Mesuré sur `Orkeon.1.0.0-rc.3.nupkg` : le digest retrouvé a une déclaration, builder
`.github/workflows/publish.yml@refs/tags/v1.0.0-rc.3`, neuf sujets — les six paquets
poussés sur nuget.org à ce tag plus les trois qui n'ont atteint que GitHub Packages
(`Orkeon.Compliance.Vfs`, `Orkeon.ConsoleApp`, `Orkeon.Generators`). Les paquets
téléchargés depuis **GitHub Packages** ne sont pas re-signés et se vérifient directement,
sans retrait.

`scripts/nupkg-unsign.py` refuse de deviner : si la signature n'est pas la dernière entrée
de l'archive, il s'arrête plutôt que d'émettre un fichier qui échouerait de toute façon.

## Vérifier la nomenclature logicielle (SBOM)

Chaque release après `v1.0.0-rc.3` livre un SBOM CycloneDX à côté de ses assets
(`orkeon-<version>.sbom.cdx.json`) — le graphe complet des dépendances NuGet de
`Orkeon.sln`, généré sur le même runner, juste après le pack, et couvert par la **même**
attestation que les archives. Vérifiez-le comme n'importe quel asset :

```bash
gh attestation verify "orkeon-$VER.sbom.cdx.json" --repo Orkeon/orkeon
```

`publish.yml` produit le même SBOM pour le push des paquets, l'atteste avec eux et le
conserve comme artefact du run (`sbom`).

## Que faire quand une vérification échoue

- `sha256sum` en désaccord → téléchargement incomplet ou altéré ; retéléchargez depuis la
  page Releases, jamais depuis un miroir.
- `gh attestation verify` échoue sur un **asset de release** dont la ligne `SHA256SUMS`
  correspondait → le manifeste et le fichier sont d'accord entre eux mais avec aucun run de
  workflow. N'installez pas ; signalez-le via la [politique de sécurité](../../../SECURITY.fr.md).
- `gh attestation verify` échoue sur un `.nupkg` **téléchargé depuis nuget.org** → attendu ;
  retirez d'abord la signature repository (ci-dessus). Si l'échec persiste après retrait, signalez-le.
- L'API renvoie une déclaration dont `workflow.repository` n'est pas
  `https://github.com/Orkeon/orkeon` → les octets ont été construits ailleurs ; signalez-le.
