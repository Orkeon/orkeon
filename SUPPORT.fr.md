> 🇬🇧 [English version](SUPPORT.md)

# Obtenir de l'aide sur Orkeon

- **Questions et discussions** — ouvrez un fil dans les
  [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions). C'est le
  bon endroit pour les « comment faire… », les questions de conception, et
  pour montrer ce que vous avez construit.
- **Bugs** — ouvrez une issue avec le
  [formulaire de bug](https://github.com/Orkeon/orkeon/issues/new/choose).
  La sortie d'`orkeon doctor` et le canal d'installation aident beaucoup.
- **Demandes de fonctionnalités** — le
  [formulaire dédié](https://github.com/Orkeon/orkeon/issues/new/choose).
- **Vulnérabilités de sécurité** — jamais d'issue publique : suivez
  [SECURITY.fr.md](SECURITY.fr.md) (GitHub Private Vulnerability Reporting).
- **Documentation** — commencez par [docs/fr/INDEX.md](docs/fr/INDEX.md) ; les
  contraintes connues vivent dans
  [docs/fr/reference/limitations.md](docs/fr/reference/limitations.md).

Il n'existe pas de canal communautaire Discord ou Slack à ce jour — les
Discussions sont le lieu de la communauté.

## Si le projet s'arrête

Rien dans Orkeon ne dépend de la présence de son mainteneur :

- **La licence est MIT.** N'importe qui peut forker, renommer, relicencier son
  fork et le publier — aucune permission à demander, personne à joindre.
- **Le build est documenté et reproductible depuis un clone public.**
  `git clone` (sans `--recursive`) + `dotnet build Orkeon.sln` ; les images
  conteneur épinglent leurs images de base par digest ; chaque action CI est
  épinglée par SHA de commit ; le pipeline de release vit dans
  `.github/workflows/` et n'a besoin de rien hors du dépôt, sauf des
  identifiants de publication que tout fork remplace par les siens.
- **Aucune infrastructure privée sur le chemin.** Les sous-modules privés ne
  contiennent que du matériel de gestion de projet — le build, les tests et
  les exemples ne les lisent pas. Il n'y a ni service hébergé, ni serveur de
  licence, ni endpoint de télémétrie qu'un fork perdrait.
- **Ce que vous installez se vérifie sans faire confiance à personne** — voir
  [Vérifier ce que vous installez](docs/fr/guides/verify-what-you-install.md).

Un fork qui garde les tests au vert est un remplacement complet, le jour où il
faut.
