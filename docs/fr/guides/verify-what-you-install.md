> 🇬🇧 [English version](../../guides/verify-what-you-install.md)

# Vérifier ce que vous installez

> **Voir aussi** : [Politique de sécurité](../../../SECURITY.fr.md) · [Matrice de publication](../reference/publication-matrix.md) · [Retour à l'index](../INDEX.md)

Chaque artefact Orkeon publié en release — les paquets NuGet, les installeurs, les archives
CLI, les paquets `.deb`, les MSI, le SBOM — est construit par un workflow GitHub Actions
public et **attesté** : une déclaration signée dit *ce fichier exact a été produit par ce
workflow, à ce commit, dans ce dépôt*. Vous pouvez vérifier cette déclaration vous-même, avec
des outils que vous avez déjà, sans faire confiance au mainteneur, à la page de
téléchargement, ni à ce document.

Cette page liste ce que la chaîne prouve, ce qu'elle ne prouve pas, et les commandes. Chacune
a été exécutée sur les artefacts publiés de `v1.0.0-rc.5` avant d'être écrite ici, et les
commentaires qui la suivent montrent ce qu'elle a affiché. Pour une autre release, changez
`VER`.

## Quelle vérification pour quel fichier

| Vous avez | Ce qu'il faut vérifier | Section |
|---|---|---|
| Une archive, un `.deb`, un MSI ou le SBOM de la page Releases | sa ligne `SHA256SUMS`, puis son attestation | [Vérifier un asset de release](#vérifier-un-asset-de-release-installeur-archive-cli-deb-msi) |
| Orkeon installé avec apt | la clé de l'archive, une fois ; apt vérifie ensuite chaque paquet par l'index signé | [Vérifier la clé de signature APT](#vérifier-la-clé-de-signature-apt) |
| Un `.nupkg` venu de nuget.org | sa signature repository, puis l'attestation des octets que nuget.org a reçus | [Vérifier un paquet NuGet](#vérifier-un-paquet-nuget) |
| Un `.nupkg` d'une version taguée venu de GitHub Packages | son attestation, directement | [Vérifier un paquet NuGet](#vérifier-un-paquet-nuget) |
| Un build dev (`<version>.dev.<n>`) ou l'image `orkeon-runners` | rien à vérifier : aucun des deux n'est attesté | [Ce que la chaîne établit](#ce-que-la-chaîne-établit) |

## Ce que la chaîne établit

| Mécanisme | Où il vit | Ce qu'il prouve |
|---|---|---|
| **Trusted Publishing (OIDC)** vers NuGet.org | `publish.yml`, étape *NuGet.org login* (`NuGet/login`) | Le push a utilisé une clé éphémère émise pour ce seul run de workflow. Il n'existe aucune clé API NuGet longue durée — aucune ne peut fuiter, aucune n'est à faire tourner. |
| **Attestation de provenance de build** (SLSA v1, Sigstore) | `publish.yml`, étape *Attest the packages and the SBOM* — sur chaque `*.nupkg` et le SBOM ; `release.yml`, étape *Attest the release assets* — sur chaque archive, `.deb`, MSI et le SBOM | Le SHA-256 du fichier est consigné dans une déclaration qui nomme le fichier de workflow, le tag, le commit et le run. Elle est signée avec un certificat éphémère que l'instance publique de Sigstore a émis pour ce workflow, et inscrite dans le journal de transparence public de Sigstore (Rekor). Un fichier au digest différent n'a pas de déclaration. |
| **Releases immuables** | Le réglage du dépôt ; `release.yml` crée la Release en brouillon et la publie une fois chaque asset attaché (étape *Publish the draft Release*) | Une fois la Release publiée, aucun de ses assets ne peut être remplacé ni supprimé. GitHub signe sa propre déclaration à son sujet (`https://in-toto.io/attestation/release/v0.2`) : le tag et le digest de chaque asset, les deux manifestes `SHA256SUMS` compris. |
| **`ContinuousIntegrationBuild=true`** au pack | `publish.yml`, étape `dotnet pack` | Les chemins dans les PDB et les assemblies sont normalisés : les octets packés ne dépendent pas de l'arborescence du runner. C'est un réglage de *build déterministe*, pas une garantie que vous puissiez reconstruire les octets identiques vous-même — il faudrait le même SDK, la même image de runner et le même graphe NuGet. |
| Manifestes **`SHA256SUMS`** | Assets de release (`SHA256SUMS` pour les archives, les paquets `.deb`, l'archive des typings d'éditeur `orkeon-typings-<version>.tar.gz` et le SBOM ; `SHA256SUMS.msi` pour les deux MSI) | Intégrité de ce que vous avez téléchargé par rapport à ce que le workflow a envoyé. Bon marché, hors ligne — mais un manifeste n'est qu'un asset de plus : ce qui relie chaque fichier listé à un workflow, c'est l'attestation de ce fichier. |
| **Vérification après publication** | `release-verify.yml`, appelé par `release.yml` juste après la publication de la Release (job `verify-published`), ou à la main pour n'importe quel tag | Un runner neuf télécharge les assets *publiés*, contrôle que les deux manifestes listent chaque asset publié, et rien d'autre, et que chaque empreinte est juste, puis rejoue le smoke d'onboarding sur les deux `.deb` (amd64, arm64) et l'archive `osx-arm64` : un asset différent de ce que les smokes de release ont installé est ainsi détecté. |
| **Dépôt APT signé** | La branche `apt`, signée dans `release.yml` (job `apt-publish`) avec une clé OpenPGP dédiée ; son empreinte et le SHA-256 du trousseau sont dans [SECURITY.fr.md](../../../SECURITY.fr.md#clé-de-signature-du-dépôt-apt) | apt vérifie la signature de l'index du canal avec cette clé seule, puis chaque `.deb` téléchargé contre le SHA-256 que liste l'index. Vous vérifiez la clé une fois, en ajoutant la source. Voir [Installer avec apt](./install-with-apt.md). |
| **Actions et images de base épinglées** | Chaque action qu'un workflow appelle par `uses:` est épinglée sur un SHA de commit (les seuls autres `uses:` appellent le workflow `release-verify.yml` du dépôt) ; chaque image `FROM` externe est épinglée par digest | Les actions et les images de base qui ont tourné sont, octet pour octet, celles que le dépôt nomme ; un tag déplacé en amont n'y change rien. |

Deux choses ne portent **aucune** attestation : les builds dev de `main` sur GitHub Packages
(`<version>.dev.<n>`, voir plus bas) et l'image conteneur `orkeon-runners` sur GHCR.

Ce que la chaîne n'établit **pas** : quoi que ce soit sur la *qualité* du code. Une attestation
dit que les octets sont sortis de `.github/workflows/publish.yml` au commit X — pas que le
commit X est sans bug, sûr à exécuter avec vos identifiants, ni relu par qui que ce soit.
Lisez le [modèle de menace](../../../SECURITY.fr.md#modèle-de-menace--exécution-doutils-pilotée-par-llm) pour cela.

## Vérifier un asset de release (installeur, archive CLI, `.deb`, MSI)

Les assets de release sont attestés **tels qu'envoyés** : le fichier téléchargé depuis la
page Releases a le digest de la déclaration.

```bash
VER=1.0.0-rc.5
BASE="https://github.com/Orkeon/orkeon/releases/download/v$VER"
curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz" -O "$BASE/SHA256SUMS"

# 1. intégrité — le manifeste a voyagé avec le fichier
sha256sum --check --ignore-missing SHA256SUMS
#    orkeon-cli-1.0.0-rc.5-osx-arm64.tar.gz: OK

# 2. provenance — la déclaration signée sur ce digest exact (gh >= 2.49, connecté)
gh attestation verify "orkeon-cli-$VER-osx-arm64.tar.gz" --repo Orkeon/orkeon
```

La seconde commande vérifie la signature de la déclaration, nomme le workflow qui a construit
le fichier (`.github/workflows/release.yml@refs/tags/v1.0.0-rc.5`) et échoue au moindre octet
différent. Elle veut un `gh` connecté (`gh auth login`), même pour un dépôt public, et
n'affiche son rapport que dans un terminal : dans un script, fiez-vous à son code de sortie.

Sans `gh`, interrogez directement l'API publique — sans token ni connexion. Elle renvoie
chaque déclaration que le dépôt détient pour ce digest, encodée en base64 ; `jq` les décode :

```bash
D=$(sha256sum "orkeon-cli-$VER-osx-arm64.tar.gz" | cut -d' ' -f1)
curl -fsSL "https://api.github.com/repos/Orkeon/orkeon/attestations/sha256:$D" |
  jq -r '.attestations[].bundle.dsseEnvelope.payload | @base64d | fromjson
         | "\(.predicateType)  \(.subject | length) subjects",
           (.predicate.runDetails.builder.id // empty),
           (.predicate.buildDefinition.resolvedDependencies[0].digest.gitCommit // empty)'
#    https://in-toto.io/attestation/release/v0.2  17 subjects
#    https://slsa.dev/provenance/v1  14 subjects
#    https://github.com/Orkeon/orkeon/.github/workflows/release.yml@refs/tags/v1.0.0-rc.5
#    e7dba7a43ee6d4460bc86f8f6d76cedf3e36dc72
```

Aucune ligne — curl signale un 404, ou la liste est vide — signifie *aucune déclaration pour
ces octets*. L'API montre ce que GitHub détient ; vérifier la signature, c'est ce que `gh`
ajoute.

Un asset de release a deux déclarations :

- La ligne **`slsa.dev/provenance/v1`** est la déclaration du workflow ; les deux lignes
  suivantes sont son builder — le fichier de workflow et le tag sur lequel il a tourné — et le
  commit qu'il a construit. Ses quatorze sujets sont tous les fichiers que le run a attachés à
  la Release : les cinq archives d'installeur, les trois archives CLI, les deux paquets
  `.deb`, le paquet du trousseau apt, les deux MSI et le SBOM. Les deux manifestes
  `SHA256SUMS` sont les seuls assets absents, alors que chaque fichier qu'ils listent y est.
- La ligne **`in-toto.io/attestation/release/v0.2`** est la déclaration de GitHub lui-même
  sur la Release publiée, signée par GitHub et non par le workflow : les Releases d'Orkeon
  sont immuables. Ses dix-sept sujets sont le tag (`pkg:github/Orkeon/orkeon@v1.0.0-rc.5`,
  épinglé au SHA-1 de son objet tag), les quatorze fichiers ci-dessus et les deux manifestes.

Ces nombres, quatorze et dix-sept, sont ceux de `v1.0.0-rc.5`. Les releases suivantes attachent
aussi `orkeon-typings-<version>.tar.gz`, que `SHA256SUMS` liste et que les attestations couvrent :
quinze et dix-huit sujets.

La première dit *quel run a construit ces octets* ; la seconde, *ce sont les octets que ce
tag a publiés, et ils ne peuvent plus changer*.

## Vérifier la clé de signature APT

La clé du dépôt se vérifie une fois, en ajoutant la source : le bloc d'installation
d'[Installer avec apt](./install-with-apt.md) compare le trousseau téléchargé au SHA-256 publié
dans [SECURITY.fr.md](../../../SECURITY.fr.md#clé-de-signature-du-dépôt-apt) et s'arrête au
moindre écart. Pour vérifier aussi l'empreinte (`gnupg` installé) :

```bash
sha256sum /usr/share/keyrings/orkeon-archive-keyring.gpg          # = le SHA-256 du trousseau dans SECURITY.fr.md
gpg --show-keys /usr/share/keyrings/orkeon-archive-keyring.gpg    # = l'empreinte dans SECURITY.fr.md
```

La sortie doit montrer une clé primaire ed25519 portant l'empreinte publiée, l'identité
`Orkeon Archive Signing Key <arion@orkeon.org>`, et une sous-clé de signature `[S]` non
expirée. La source armurée de la même clé est
[`installers/apt/orkeon-archive-keyring.asc`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon-archive-keyring.asc) ;
`gpg --dearmor` la transforme en les octets exacts du trousseau. Ensuite, apt vérifie seul
chaque mise à jour. Les paquets qu'il installe sont les assets des Releases — le paquet du
trousseau, `orkeon-archive-keyring`, compris — et les commandes d'attestation ci-dessus s'y
appliquent telles quelles. Une fois installé, le paquet Debian se vérifie fichier par
fichier : `dpkg -V orkeon` n'affiche rien quand chaque fichier installé correspond à
l'empreinte enregistrée par le paquet. Une image allégée — les images Docker d'Ubuntu — demande
à dpkg de ne pas déballer `/usr/share/doc` (`path-exclude` dans `/etc/dpkg/dpkg.cfg.d/excludes`) :
les notices et licences de `/usr/share/doc/orkeon/` y apparaissent `missing`, et seules ces
lignes sont attendues.

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
# Depuis la racine d'un clone, pour scripts/nupkg-unsign.py :
#    git clone --depth 1 https://github.com/Orkeon/orkeon && cd orkeon
VER=1.0.0-rc.5
curl -fsSL -o "Orkeon.$VER.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/orkeon/$VER/orkeon.$VER.nupkg"

# 1. la signature repository de nuget.org — intacte depuis que nuget.org l'a stockée
dotnet nuget verify --all "Orkeon.$VER.nupkg"
#    Signature type: Repository — CN=NuGet.org Repository by Microsoft …

# 2. les octets tels qu'attestés — retirer la signature repository ajoutée
python3 scripts/nupkg-unsign.py "Orkeon.$VER.nupkg"
#    Orkeon.1.0.0-rc.5.unsigned.nupkg  sha256:a6c81a0c77a451e0af8408d5a7774c4c8527bcfbae213f2d4c005550701548e8

# 3. provenance de ces octets
gh attestation verify "Orkeon.$VER.unsigned.nupkg" --repo Orkeon/orkeon
```

Le script garde tout ce qui précède l'en-tête local de la signature, retire l'entrée de la
signature du répertoire central, et réécrit l'enregistrement de fin de répertoire central
avec les compteurs, la taille et l'offset rétablis. Il refuse de deviner : si la signature
n'est pas la dernière entrée de l'archive, il s'arrête plutôt que d'émettre un fichier qui
échouerait de toute façon.

Mesuré sur `Orkeon.1.0.0-rc.5.nupkg`, le digest retrouvé a une déclaration (le filtre `jq`
ci-dessus l'affiche aussi) : builder `.github/workflows/publish.yml@refs/tags/v1.0.0-rc.5`,
le commit dont sont issus les assets de la release, douze sujets — les neuf paquets poussés
sur nuget.org à ce tag, les deux qui n'atteignent que GitHub Packages (`Orkeon.ConsoleApp`,
`Orkeon.Generators`), et le SBOM. L'attestation est littérale : elle liste ce que le workflow
a construit, pas ce qui a atteint nuget.org.

Une version taguée téléchargée depuis **GitHub Packages** n'est pas re-signée et se vérifie
directement, sans retrait. Les builds dev de ce flux (`<version>.dev.<n>`, voir la
[matrice de publication](../reference/publication-matrix.md)) ne sont pas des releases et ne
portent aucune attestation : il n'y a rien contre quoi les vérifier.

## Vérifier la nomenclature logicielle (SBOM)

Depuis `v1.0.0-rc.4`, chaque release livre un SBOM CycloneDX à côté de ses assets —
`orkeon-<version>.sbom.cdx.json`, le graphe des dépendances NuGet d'`Orkeon.sln`, paquets
transitifs compris et projets de test exclus. Un outil dotnet `CycloneDX` épinglé (pas une
action tierce) le génère sur le runner qui a empaqueté les archives, juste après elles ; il
est listé dans `SHA256SUMS` et attesté dans la même étape que les archives : une seule
déclaration couvre les binaires et le graphe de dépendances dont ils sont issus.
Vérifiez-le comme n'importe quel asset :

```bash
VER=1.0.0-rc.5
curl -fsSL -O "https://github.com/Orkeon/orkeon/releases/download/v$VER/orkeon-$VER.sbom.cdx.json"
gh attestation verify "orkeon-$VER.sbom.cdx.json" --repo Orkeon/orkeon
```

Le fichier de `v1.0.0-rc.5` liste 276 composants, au format CycloneDX 1.7. C'est un graphe de
dépendances, pas un scan de vulnérabilités : donnez-le au scanner que vous utilisez déjà. Ce
que l'attestation ajoute, c'est que le graphe scanné est celui à partir duquel la release a
été construite, pas un graphe régénéré après coup.

`publish.yml` lance le même outil juste après le pack et atteste ce second fichier avec les
paquets — son propre digest, dans sa propre déclaration, conservé comme artefact du run
(`sbom`) parce que nuget.org n'a pas d'emplacement pour un SBOM. La copie à scanner est celle
de la Release.

## Que faire quand une vérification échoue

- `sha256sum` en désaccord → téléchargement incomplet ou altéré ; retéléchargez depuis la
  page Releases, jamais depuis un miroir.
- `gh attestation verify` sort avec le code 4 et demande `gh auth login` → ce n'est pas une
  vérification ratée : `gh` veut une connexion, même pour un dépôt public. Connectez-vous, ou
  interrogez l'API (ci-dessus).
- `gh attestation verify` échoue, ou l'API n'a aucune déclaration, pour un **asset de
  release** dont la ligne `SHA256SUMS` correspondait → le manifeste et le fichier concordent
  entre eux, mais ne correspondent à aucun run de workflow. N'installez pas ; signalez-le via
  la [politique de sécurité](../../../SECURITY.fr.md).
- `gh attestation verify` échoue sur un `.nupkg` **téléchargé depuis nuget.org** → attendu ;
  retirez d'abord la signature repository (ci-dessus). Si l'échec persiste après retrait,
  signalez-le.
- La déclaration de provenance nomme un autre tag que la version que vous vouliez installer →
  le fichier est un build authentique de cette autre release, pas celui que son nom annonce ;
  téléchargez la version voulue depuis sa propre Release.
