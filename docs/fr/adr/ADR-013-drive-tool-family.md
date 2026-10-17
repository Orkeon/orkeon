> 🇬🇧 [English version](../../adr/ADR-013-drive-tool-family.md)

> **Voir aussi** : [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [ADR-012](./ADR-012-email-tool-family.md) · [Outils drive](../guides/drive.md) · [Retour à l'index](../INDEX.md)

# ADR-013 — La famille d'outils drive, troisième exception motivée au gel du périmètre

**Statut** : Accepté · **Date** : 2026-10-17
· **Portée** : `src/tools/Orkeon.Tools.Drive`, `Orkeon.Tools.Abstractions/Auth` (le client OAuth que partagent les familles e-mail et drive), `Orkeon.Hosting` (le sous-répertoire `drive` de `/credentials`), `orkeon drive` (`Orkeon.Scripting.Cli`), le paquet ombrelle `Orkeon.Tools`

## Contexte

Le propriétaire a demandé la prise en charge de Google Drive et de OneDrive. Rien dans le dépôt
ne parlait à l'un ni à l'autre ; une note de conception du VFS n'envisageait qu'« un
`IFileSystemService` réseau » pour le stockage distant. Deux formes étaient possibles, et le
propriétaire a choisi la première le 2026-10-17 :

- **Une famille d'outils**, sur le modèle de la famille e-mail : des outils explicites qu'un agent
  appelle, chacun gardé par un droit.
- **Un montage VFS** : un drive monté sous `/drive/...`, chaque outil fichier travaillant dessus.
  Plus profond, mais le VFS est synchrone par endroits, ne connaît ni les ids d'un fournisseur, ni
  le partage, ni les documents propres à Google, et la latence, les quotas et le cache fuiraient
  dans chaque outil fichier.

Le gel du périmètre tient pour la même raison que pour l'e-mail (ADR-012) : un script tourne sur
Jint et ne peut pas parler OAuth ni HTTP à un fournisseur avec un jeton stocké ; un plugin ne
serait pas « directement dans Orkeon », et il lui faudrait reconstruire le stockage des jetons,
les droits et le filtrage.

La famille e-mail avait déjà un client OAuth2 écrit à la main (code d'appareil, PKCE en bouclage
avec un repli par collage, rafraîchissement avec rotation), un magasin de jetons derrière la
racine interne `/credentials`, la lecture des secrets dans la portée utilisateur de Studio, et
Microsoft Graph en HTTP simple — le tout interne à `Orkeon.Tools.Email`.

## Décision

1. **Une neuvième famille, `Orkeon.Tools.Drive`**, avec les couches de l'ADR-005 (Domain,
   Tools.Abstractions, Orkeon.Rag pour le détecteur d'injection, Constants.Configuration), tenues
   par un test de liste d'autorisation. **Aucun nouveau paquet** : l'API Drive v3 et Microsoft
   Graph sont parlés en HTTP simple avec `System.Text.Json`.
2. **La couche OAuth passe dans `Orkeon.Tools.Abstractions/Auth`**, interne et partagée par les
   deux familles via `InternalsVisibleTo` : le client, l'écoute en bouclage, le lecteur de secrets
   et les flux de connexion. Elle lève une `OAuthException` ; chaque famille la traduit en sa
   propre exception, même message, code correspondant. `IEmailLoginInteraction` garde son nom et
   hérite désormais de l'interface publique `IOAuthLoginInteraction`, sans membre propre.
3. **Deux moteurs derrière un même port interne** (`IDrive`), une seule forme d'élément. Google
   Drive est parcouru par nom sous chaque parent (il n'a pas de chemins) et ses documents propres
   sont lus et téléchargés par export ; OneDrive est adressé par chemin.
4. **L'opérateur décide, l'agent nomme un compte.** `Rights` (`Read, Write, Organize, Share,
   Delete, Purge`) est obligatoire ; `Root` confine un compte à un dossier, un élément hors de
   celui-ci étant signalé comme introuvable ; le partage est fermé tant que
   `Share:AllowedRecipients` ne nomme personne, et les liens publics restent désactivés sans
   `Share:AllowPublicLinks`. Un compte qui ne fait que lire demande un scope en lecture seule.
5. **Le jeton ne quitte jamais l'hôte de l'API.** Les téléchargements OneDrive passent par la
   `downloadUrl` pré-authentifiée, les sessions de chargement ne reçoivent aucun en-tête
   `Authorization`, les liens de pagination sont vérifiés — jamais une redirection suivie avec le
   jeton.
6. **Un seul répertoire d'identifiants par run.** Les jetons drive vivent dans un sous-répertoire
   `drive` de la racine `/credentials` de la famille e-mail, réservé au propriétaire sous Unix ;
   quand les deux sections nomment des répertoires différents, celui de l'e-mail l'emporte et le
   runner le dit au départ.
7. **La forge refuse les outils drive** : il n'y a pas de drive d'essai, et un crew forgé ne doit
   pas toucher le vrai drive de l'opérateur. `drive_read` filtre et marque son texte lui-même, et
   contourne le filtrage des résultats comme les outils e-mail ; les autres outils drive restent
   filtrés.

## Conséquences

- 102 classes d'outils intégrés, 49 projets src et 37 projets de test ; le paquet ombrelle porte
  neuf familles.
- `orkeon drive accounts | login | logout | check` ; les typings des scripts `.ork.ts` ; une
  famille « Drive » dans la liste des outils de Studio — sans onglet de réglages pour l'instant,
  l'exigence nomme la section.
- Les drives partagés Google, les bibliothèques SharePoint, les révisions, les commentaires, les
  copies et un montage VFS sont hors périmètre, documentés dans le guide.
- La campagne sur de vrais comptes est celle du propriétaire (DRIVE-07) : les tests hors ligne
  scriptent les deux API, et le guide dit que la famille n'est pas encore campagnée.
