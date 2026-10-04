> 🇬🇧 [English version](../../guides/install-with-apt.md)

# Installer avec apt (Debian / Ubuntu)

> **Voir aussi** : [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md) · [Vérifier ce que vous installez](./verify-what-you-install.md) · [Politique de sécurité](../../../SECURITY.fr.md) · [Matrice de publication](../reference/publication-matrix.md) · [Retour à l'index](../INDEX.md)

Ajoutez **une fois** la source Orkeon à apt, puis gérez Orkeon comme n'importe quel paquet :
`apt install orkeon`, `apt update`, `apt upgrade`. Plus de `.deb` à retélécharger à chaque
version.

Le dépôt sert deux paquets :

| Paquet | Contenu |
|---|---|
| `orkeon` | le CLI `orkeon` en `/usr/bin/orkeon` et les deux applications terminal **Orkeon Studio** (`orkeon-studio-config`, `orkeon-studio-run`) — self-contained, il ne tire jamais de paquet `dotnet-runtime` ni de dépôt Microsoft |
| `orkeon-archive-keyring` | la clé publique avec laquelle apt vérifie le dépôt, en `/usr/share/keyrings/orkeon-archive-keyring.gpg` — une nouvelle clé de signature vous arrive ainsi par `apt upgrade` |

Il est construit pour **Debian 12 et 13** et **Ubuntu 22.04, 24.04 et 26.04**, en **amd64** et
en **arm64**. Les dérivées (Linux Mint, Pop!\_OS, Ubuntu sous WSL) suivent leur base Ubuntu ou
Debian. Ubuntu 20.04 et plus ancien ne sont pas pris en charge : le paquet exige
`libc6 (>= 2.34)`.

Rien dans Orkeon n'ajoute cette source à votre place — ni le `.deb`, ni l'`install.sh` des
archives : c'est vous qui lancez les commandes ci-dessous.

## Mise en place

Copiez le bloc ci-dessous dans un terminal. Il fonctionne depuis une image Debian ou Ubuntu
nue, sous un utilisateur qui peut faire `sudo` :

<!-- apt-setup:begin -->
```bash
sudo apt-get update && sudo apt-get install -y ca-certificates curl
curl -fsSL -o /tmp/orkeon-archive-keyring.gpg \
  https://github.com/Orkeon/orkeon/raw/apt/orkeon-archive-keyring.gpg
echo "0cc5e804e1ee49c50ec5b23145ee7ee65d4834a08b8dc38914674249dfcc6a21  /tmp/orkeon-archive-keyring.gpg" | sha256sum --check
sudo install -m 0644 /tmp/orkeon-archive-keyring.gpg /usr/share/keyrings/orkeon-archive-keyring.gpg
sudo tee /etc/apt/sources.list.d/orkeon.sources > /dev/null <<'EOF'
Types: deb
URIs: https://github.com/Orkeon/orkeon/
Suites: raw/apt/stable/
Include: orkeon orkeon-archive-keyring
Signed-By: /usr/share/keyrings/orkeon-archive-keyring.gpg
EOF
sudo apt-get update && sudo apt-get install -y orkeon orkeon-archive-keyring
```
<!-- apt-setup:end -->

> **Pour installer le build dev de `main`**, voyez [Installer un build dev](#installer-un-build-dev).
> Le bloc installe depuis `stable`, qui ne porte que les versions finales : avant la première,
> `apt-get update` s'y arrête sur *« does not have a Release file »* (canal jamais publié) ou
> l'installation sur *« Unable to locate package orkeon »* (canal encore vide).

Ensuite, comme pour tous les autres canaux :

```bash
orkeon init          # écrit ~/.config/Orkeon/appsettings.json
orkeon run crew.yaml
```

Pourquoi chaque ligne est là :

- **`ca-certificates` et `curl`** manquent aux images de base ; apt a besoin du premier pour
  joindre GitHub en HTTPS.
- **La clé est vérifiée avant d'être crue.** Le premier téléchargement d'une clé repose sur la
  confiance au premier usage : `sha256sum --check` la compare au SHA-256 publié dans la
  [politique de sécurité](../../../SECURITY.fr.md#clé-de-signature-du-dépôt-apt) sur `main`, et
  arrête le bloc au moindre écart.
- **La clé est un fichier binaire en mode 0644.** apt vérifie les signatures sous l'utilisateur
  non privilégié `_apt`, et un fichier armuré (ASCII) enregistré sous un nom `.gpg` échoue en
  `NO_PUBKEY` avec apt 2.4 à 2.8.
- **La source est un fichier deb822 `.sources`** avec une ligne `Signed-By:` : la clé n'est
  reconnue que pour cette source, jamais pour tout le système. Pas d'`apt-key`, rien sous
  `/etc/apt/trusted.gpg.d`. `Include:` limite la source aux deux paquets Orkeon à partir
  d'apt 3.1 ; les versions antérieures ignorent le champ.
- **`orkeon-archive-keyring` est installé explicitement.** `orkeon` ne fait que le
  *recommander*, et `--no-install-recommends` — courant dans les Dockerfiles — le sauterait. Une
  fois installé, le paquet prend possession du fichier de clé posé à la main et le tient à jour.

Le même fichier de source est conservé dans le dépôt :
[`installers/apt/orkeon.sources`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon.sources).

## Usage courant

| Vous voulez… | Commande |
|---|---|
| Installer | `sudo apt install orkeon orkeon-archive-keyring` |
| Mettre à jour | `sudo apt update && sudo apt upgrade` |
| Voir les versions que propose le canal | `apt list -a orkeon` (ou `apt-cache policy orkeon`) |
| Revenir à une version antérieure | `sudo apt install orkeon=<version>` |
| Rester sur une version | `sudo apt-mark hold orkeon` (`unhold` pour suivre de nouveau le canal) |
| Voir ce qui tourne | `orkeon --version` |

**Forme des versions.** apt écrit le séparateur de pré-version avec `~`, qui classe une
pré-version *avant* sa version finale : la release taguée `v1.0.0-rc.4` est la version de
paquet `1.0.0~rc.4`, et y revenir s'écrit `sudo apt install orkeon=1.0.0~rc.4`. apt demande de
confirmer un retour en arrière ; dans un script,
`sudo apt-get install -y --allow-downgrades orkeon=<version>`.

Chaque version encore attachée à une Release GitHub reste dans son canal : un retour en arrière
a toujours où aller. Une version retirée pour un défaut disparaît du canal au `apt update`
suivant (voir [Versions retirées](#versions-retirées)).

Vos réglages ne font pas partie du paquet : `~/.config/Orkeon/appsettings.json`, écrit par
`orkeon init`, survit aux mises à jour, aux retours en arrière et à la désinstallation.

## Canaux

| Canal | Ligne `Suites:` | Contenu |
|---|---|---|
| `stable` | `raw/apt/stable/` | les versions finales seulement (un tag sans segment de pré-version, comme `v1.0.0`) |
| `rc` | `raw/apt/rc/` | toutes les releases taguées — les release candidates **et** les versions finales |
| `dev` | `raw/apt/dev/` | les builds de `main` entre deux releases — voir [Suivre `main`](#suivre-main-builds-dev) |

`stable` reste vide tant qu'aucune version finale n'est publiée : une machine sur `stable`
n'installe rien jusque-là. Pour essayer une release candidate, prenez `rc` — toute version
finale arrive aussi dans `rc`, une machine sur `rc` n'en manque donc aucune.

**Changer de canal** : modifiez la ligne `Suites:`, puis mettez à jour :

```bash
sudo sed -i 's|^Suites: .*|Suites: raw/apt/rc/|' /etc/apt/sources.list.d/orkeon.sources
sudo apt update && sudo apt upgrade
```

Passer à un canal aux versions plus récentes est une mise à jour ordinaire. Revenir à un canal
dont la version la plus récente est *plus ancienne* que celle installée (de `dev` vers `rc`, de
`rc` vers `stable`) ne met rien à jour : apt garde la version installée jusqu'à ce que le canal
en propose une plus récente. Pour revenir tout de suite, installez explicitement une version du
nouveau canal : `sudo apt install orkeon=<version>`.

Ne modifiez que le nom du canal sur cette ligne : le reste du chemin doit correspondre à ce que
déclare l'index du dépôt, sinon apt avertit « Conflicting distribution » à chaque mise à jour.

## Suivre `main` (builds dev)

Le canal `dev` porte le paquet `orkeon` construit à partir de chaque commit de `main` validé par
la CI, entre deux releases :

```text
Suites: raw/apt/dev/
```

- **Un build arrive quelques minutes après le passage de la CI** sur un push vers `main`. Ses
  paquets sont attachés à la prerelease `apt-dev` du dépôt — un tag fixe qui ne bouge jamais, et
  pas une release.
- **Les versions** ont la forme `1.0.0~rc.4.dev.<n>`, `n` étant le numéro de l'exécution de CI qui
  a validé le commit, donc croissant à chaque build : au-dessus de
  la release dont elles partent, en dessous de la suivante — une machine dev passe donc à la
  release candidate suivante par un simple `apt upgrade` quand elle est publiée. Après une
  version finale, les builds dev passent au patch suivant (`1.0.1~dev.<n>` après `1.0.0`), comme
  le fait le [canal dev NuGet](../reference/publication-matrix.md#canal-dev--le-dernier-main-entre-deux-tags).
- **Seuls les trois derniers builds sont gardés.** Un build plus ancien quitte le canal ;
  `apt install orkeon=<version>` ne peut revenir que jusque-là.
- **Instable et sans support.** Un build dev n'est pas une release : il peut casser d'une mise à
  jour à l'autre, et un bogue trouvé dessus se corrige sur `main`, jamais sur le build. Prenez
  une version publiée (`rc` ou `stable`) pour tout ce qui doit continuer de marcher.
- **apt seulement.** Les builds dev ne sont jamais publiés sur NuGet.org ; l'outil dotnet
  `orkeon` suit `main` par son propre canal sur GitHub Packages, décrit dans
  [Trois façons d'exécuter Orkeon](../getting-started/three-ways-to-run-orkeon.md#suivre-main--le-canal-dev).

### Installer un build dev

**Sur une machine sans la source**, lancez le [bloc de mise en place](#mise-en-place) avec une
seule modification : avant de le coller, remplacez `Suites: raw/apt/stable/` par
`Suites: raw/apt/dev/`. Tout le reste — la clé, la vérification de son SHA-256, le fichier de
source — reste identique.

**Sur une machine qui a déjà la source** (sur `stable` ou `rc`), passez-la sur `dev` :

```bash
sudo sed -i 's|^Suites: .*|Suites: raw/apt/dev/|' /etc/apt/sources.list.d/orkeon.sources
sudo apt-get update && sudo apt-get install -y orkeon orkeon-archive-keyring
orkeon --version   # <version>.dev.<n>
```

**Ensuite, pour prendre le build dev le plus récent :** `sudo apt update && sudo apt upgrade`.

Le fichier de source, la clé et les commandes sont ceux des autres canaux — le canal `dev` porte
lui aussi `orkeon-archive-keyring` ; seule la ligne `Suites:` change.

## Désinstaller

Dans cet ordre — la source d'abord, puis les paquets :

```bash
sudo rm /etc/apt/sources.list.d/orkeon.sources
sudo apt purge orkeon orkeon-archive-keyring
sudo apt update
```

Purger le trousseau alors que la source est encore déclarée fait échouer chaque `apt update`
suivant sur une clé absente. Votre `~/.config/Orkeon` reste en place ; supprimez-le vous-même si
vous voulez qu'il disparaisse.

## Ce qu'apt vérifie, et ce que vous vérifiez une fois

**apt vérifie, à chaque mise à jour et installation, sans vous :**

- la signature de l'index du canal (`InRelease`), avec la clé de
  `/usr/share/keyrings/orkeon-archive-keyring.gpg` et elle seule ;
- le SHA-256 de chaque paquet qu'il télécharge, contre l'index — un paquet qui diffère d'un seul
  octet est refusé (« Hash Sum mismatch »).

**Vous vérifiez une fois, en ajoutant la source :**

- le SHA-256 du fichier de clé — la ligne `sha256sum --check` du bloc le fait ;
- si vous le souhaitez, l'empreinte de la clé, publiée dans la
  [politique de sécurité](../../../SECURITY.fr.md#clé-de-signature-du-dépôt-apt) :

```text
orkeon-archive-keyring fingerprint: 4765 9C57 4882 5078 5C78  2C37 1CF8 CD8A 4C20 1A2E
orkeon-archive-keyring.gpg sha256: 0cc5e804e1ee49c50ec5b23145ee7ee65d4834a08b8dc38914674249dfcc6a21
```

Les deux mêmes lignes figurent dans la politique de sécurité ; la garde de la CI compare les deux
pages, ainsi que le SHA-256 du bloc ci-dessus, au certificat.

```bash
gpg --show-keys /usr/share/keyrings/orkeon-archive-keyring.gpg   # exige le paquet gnupg
```

La ligne de la clé primaire doit montrer cette empreinte et l'identité
`Orkeon Archive Signing Key <arion@orkeon.org>`.

Les paquets eux-mêmes sont les assets des Releases GitHub : chacun porte donc aussi
l'attestation de provenance de build et la ligne `SHA256SUMS` décrites dans
[Vérifier ce que vous installez](./verify-what-you-install.md).

## Images Docker

Un Dockerfile installe depuis le dépôt comme une machine, avec deux précautions :

- **Limites de débit.** L'index est servi par `raw.githubusercontent.com`, qui limite les accès
  anonymes et peut répondre `429`. `apt update` n'en fait qu'un avertissement et garde l'index
  qu'il avait — mais une image neuve n'en a aucun, et l'`apt-get install` suivant échoue.
  Réessayez, et sortez la clé des téléchargements du build.
- **`--no-install-recommends`** saute `orkeon-archive-keyring` si vous ne le nommez pas —
  gardez-le dans la liste.

Vérifiez la clé une fois sur votre machine avec le bloc ci-dessus, posez
`orkeon-archive-keyring.gpg` et
[`orkeon.sources`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon.sources)
à côté du Dockerfile, puis :

```dockerfile
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates
COPY orkeon-archive-keyring.gpg /usr/share/keyrings/orkeon-archive-keyring.gpg
COPY orkeon.sources /etc/apt/sources.list.d/orkeon.sources
RUN chmod 0644 /usr/share/keyrings/orkeon-archive-keyring.gpg /etc/apt/sources.list.d/orkeon.sources \
 && apt-get -o Acquire::Retries=5 update \
 && apt-get install -y --no-install-recommends orkeon orkeon-archive-keyring \
 && rm -rf /var/lib/apt/lists/*
```

`ca-certificates` passe en premier : sans lui, apt ne peut pas joindre la source Orkeon en
HTTPS.

## Versions retirées

Une version jugée défectueuse après sa publication peut être **retirée** d'un canal : l'index
est signé de nouveau sans elle, et votre `apt update` suivant ne la propose plus. Une copie
installée reste installée — `apt upgrade` la fait passer à la plus récente des versions
restantes, ou `sudo apt install orkeon=<version>` à celle de votre choix. Un paquet publié n'est
jamais remplacé par d'autres octets sous la même version : un correctif arrive toujours comme
une nouvelle version.

## Dépannage

| Message | Cause | Solution |
|---|---|---|
| `NO_PUBKEY` | Le fichier de clé manque, ou c'est un fichier armuré enregistré en `.gpg` | Relancez le bloc : il installe la clé binaire en mode 0644 |
| `EXPKEYSIG` | La clé de la machine est périmée — le dépôt est signé avec une plus récente | `sudo apt install orkeon-archive-keyring` si apt le permet encore, sinon retéléchargez et vérifiez la clé avec les lignes du bloc jusqu'à `sudo install` |
| `Conflicting distribution` | La ligne `Suites:` a été modifiée au-delà du nom du canal | Rétablissez la ligne d'après le [tableau des canaux](#canaux), `/` final compris |
| `429 Too Many Requests` | La limite de GitHub sur les accès anonymes | Réessayez plus tard ; dans un build Docker, réessayez l'étape (`-o Acquire::Retries=5`) |
| `Hash Sum mismatch` juste après une release | Le cache de GitHub servait encore l'index précédent pendant quelques minutes | Attendez quelques minutes, puis relancez `sudo apt update` |
| Erreurs de signature après l'annonce d'une révocation de clé | La clé de signature a été révoquée | Suivez les instructions publiées dans la [politique de sécurité](../../../SECURITY.fr.md#clé-de-signature-du-dépôt-apt) : c'est le seul cas où vous devez télécharger vous-même une nouvelle clé |

## Sans le dépôt

Une machine qui n'atteint pas le contenu brut de GitHub peut quand même installer une version
isolée : téléchargez `orkeon_<version>_amd64.deb` (ou `_arm64.deb`) et `SHA256SUMS` depuis les
[Releases](https://github.com/Orkeon/orkeon/releases), où `<version>` est la version du tag
(`1.0.0-rc.4`), puis :

```bash
grep " orkeon_<version>_amd64.deb$" SHA256SUMS | sha256sum --check   # attendu : OK
sudo apt install ./orkeon_<version>_amd64.deb
```

Une telle installation ne reçoit aucune mise à jour : apt ne met à jour que depuis une source
qu'il connaît.
