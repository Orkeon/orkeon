> 🇬🇧 [English version](../../guides/drive.md)

# Outils drive

> **Voir aussi** : [Inventaire des outils](../tools/inventory.md#tools-drive-orkeontoolsdrive--addorkeondrivetoolsconfiguration) ·
> [Outils e-mail](email.md) · [Référence de configuration](../reference/configuration.md#orkeontoolsdrive) ·
> [ADR-013](../adr/ADR-013-drive-tool-family.md) · [Retour à l'index](../INDEX.md)

La famille drive donne aux agents un drive dans le nuage : **Google Drive** (« Mon Drive ») via
l'API Drive v3, et **OneDrive** — personnel, ou professionnel et scolaire — via Microsoft Graph.
Les deux sont parlés en HTTP simple, sans SDK de fournisseur. Un agent liste, cherche, lit,
télécharge vers le système de fichiers virtuel, charge sur le drive des fichiers qui en viennent, crée des dossiers,
déplace, renomme, supprime et partage — dans les limites de ce qu'autorise l'opérateur :

- **L'opérateur déclare les comptes** sous `Orkeon:Tools:Drive`. Un agent nomme un compte (ou
  prend celui par défaut), jamais un id client, un secret ou un jeton.
- **`Rights` est obligatoire** pour chaque compte : `Read, Write, Organize, Share, Delete, Purge`.
- **`Root` confine un compte à un dossier** de son drive. Tout élément que touche un appel doit
  se trouver dessous ; un élément hors de ce dossier est signalé comme introuvable.
- **Le partage est fermé par défaut** : une liste `Share:AllowedRecipients` vide n'autorise
  personne, et les liens publics restent désactivés tant que `Share:AllowPublicLinks` ne vaut pas
  `true`.
- **Chaque compte se connecte une fois en OAuth2** (`orkeon drive login` dans un terminal, ou
  « Se connecter » dans Orkeon Studio), et les outils rafraîchissent eux-mêmes le jeton d'accès.

> **Pas encore campagné.** La famille est couverte par des tests hors ligne contre des API Google
> et Microsoft scriptées. Elle n'a pas encore été exécutée contre un vrai Google Drive ou un vrai
> OneDrive : cette campagne est celle du propriétaire (DRIVE-07). Signalez ce qui diffère.

## Les onze outils

| Outil | Droit requis | Ce qu'il fait |
|---|---|---|
| `drive_accounts` | aucun | Liste les comptes : nom, droits, dossier racine, si chacun est prêt |
| `drive_list` | Read | Une page d'un dossier (la racine par défaut), dossiers en tête |
| `drive_search` | Read | Les éléments qui correspondent à des mots, un nom, un type MIME, une nature, une date ou un dossier, dans la racine |
| `drive_info` | Read | Les métadonnées d'un élément, son adresse dans le navigateur comprise |
| `drive_read` | Read | Le texte d'un fichier texte ou d'un document Google, filtré, par tranches |
| `drive_download` | Read + un montage accessible en écriture | Enregistre un fichier dans un répertoire virtuel ; les documents Google sont exportés |
| `drive_upload` | Write | Charge un fichier du VFS, ou remplace le contenu d'un fichier (`replace_id`) |
| `drive_create_folder` | Write | Crée un dossier |
| `drive_move` | Organize | Déplace et/ou renomme un élément |
| `drive_delete` | Delete (+ Purge avec `permanent`) | Met un élément à la corbeille, ou le supprime définitivement |
| `drive_share` | Share | Donne accès à des adresses autorisées, ou crée un lien public quand c'est permis |

Les ids sont ceux du fournisseur, et opaques : repassez-les exactement tels que `drive_list` ou
`drive_search` les ont rendus. `root` désigne le dossier racine du compte partout où un id de
dossier est attendu (`folder_id`, `parent_id`, `destination_id`).

## OneDrive (Microsoft Graph)

### Enregistrer une application dans Microsoft Entra

1. Ouvrez le [centre d'administration Microsoft Entra](https://entra.microsoft.com) ›
   **Inscriptions d'applications** (*App registrations*) › **Nouvelle inscription**.
2. Nommez-la (`Orkeon drive`, par exemple) et choisissez les types de comptes pris en charge :
   **Comptes Microsoft personnels uniquement** pour un OneDrive personnel, **Comptes dans un
   annuaire organisationnel et comptes Microsoft personnels** pour servir les deux. Aucun URI de
   redirection n'est nécessaire.
3. Dans **Authentification**, réglez **Autoriser les flux de clients publics** (*Allow public
   client flows*) sur **Oui** : la connexion utilise le flux par code d'appareil.
4. Dans **Autorisations d'API** › **Microsoft Graph** › **Autorisations déléguées**, ajoutez
   `Files.ReadWrite` (ou `Files.Read` pour un compte qui ne fait que lire) et `offline_access`.
   Sans `offline_access`, Microsoft ne délivre aucun jeton de rafraîchissement, et le login refuse
   d'enregistrer une connexion qui expirerait dans l'heure.
5. Copiez l'**ID d'application (client)**.

Une application enregistrée pour la famille e-mail peut aussi servir la famille drive :
ajoutez-lui les autorisations `Files.*`.

### Déclarer le compte et se connecter

Sous `Orkeon:Tools:Drive:Accounts` :

```json
"pro": {
  "Provider": "OneDrive",
  "Rights": "Read, Write, Organize",
  "Root": "Orkeon/agents",
  "Auth": { "ClientId": "00000000-0000-0000-0000-000000000000" }
}
```

`Auth:Tenant` vaut par défaut `consumers`, le tenant des comptes personnels ; pour un OneDrive
professionnel ou scolaire, mettez `"Tenant": "organizations"` ou l'id de votre tenant.

**Depuis Orkeon Studio**, c'est un formulaire : **Réglages › Stockage cloud** — « Ajouter un
compte », un nom et OneDrive, puis l'ID client dans l'onglet Connexion, les droits dans l'onglet
Droits et le dossier dans l'onglet Compte (le tenant est un champ expert). Studio écrit la section
ci-dessus dans le fichier de réglages qu'il édite, et l'onglet dit ce que le moteur fait de chaque
compte. « Se connecter » lance le login ci-dessous et affiche l'adresse de Microsoft et le code,
avec son compte à rebours ; « Tester la connexion » lance la vérification.

Dans un terminal :

```bash
orkeon drive login pro
```

La commande affiche l'adresse de vérification de Microsoft et un code. Ouvrez l'adresse dans
n'importe quel navigateur — sur n'importe quel appareil —, saisissez le code, connectez-vous et
acceptez. La commande enregistre les jetons et affiche `Signed in`. Vérifiez le compte avec
`orkeon drive check pro` : elle trouve le dossier racine et liste ses premiers éléments.

## Google Drive

Le *device flow* de Google ne propose pas les scopes Drive : le login affiche donc l'adresse de la
page de consentement de Google, à ouvrir, et reçoit la réponse sur une adresse de bouclage
(`127.0.0.1`), avec PKCE — le même flux qu'un compte Gmail en OAuth2.

1. Dans la [console Google Cloud](https://console.cloud.google.com), créez un projet et activez
   l'**API Google Drive**.
2. Configurez l'**écran de consentement OAuth** : type d'utilisateur **Externe**, ajoutez le scope
   `https://www.googleapis.com/auth/drive` (ou `drive.readonly` pour un compte qui ne fait que
   lire), et votre propre adresse comme **utilisateur test**.
3. Créez un **ID client OAuth** de type **Application de bureau** (*Desktop app*). Copiez son ID
   client, et mettez son secret client dans une variable d'environnement
   (`GOOGLE_DRIVE_CLIENT_SECRET` ci-dessous) — dans Orkeon Studio, saisissez-le dans l'onglet
   Connexion du compte : Studio le garde dans une variable d'environnement utilisateur
   (`DRIVE_<COMPTE>_CLIENT_SECRET`) et n'écrit que ce nom dans le fichier.
4. Déclarez le compte, sous `Orkeon:Tools:Drive:Accounts` :

   ```json
   "perso": {
     "Provider": "GoogleDrive",
     "Rights": "Read, Write",
     "Root": "Orkeon",
     "Auth": {
       "ClientId": "123456789012-abcdefghijklmnop.apps.googleusercontent.com",
       "ClientSecretEnvVar": "GOOGLE_DRIVE_CLIENT_SECRET"
     }
   }
   ```

5. Connectez-vous — **depuis Orkeon Studio** : Réglages › Stockage cloud, le compte, « Se
   connecter », qui affiche l'adresse de Google avec « Copier le lien » et « Ouvrir dans le
   navigateur », et un champ pour l'adresse où le navigateur aboutit, qui dit pourquoi quand ce
   n'est pas la bonne ; dans un terminal, lancez `orkeon drive login perso`. La commande affiche
   une adresse Google à ouvrir et écoute sur un port libre de `127.0.0.1`. Connectez-vous et acceptez ; Google affiche un écran
   d'avertissement pour une application qu'il n'a pas vérifiée, que vous pouvez passer pour votre
   propre application. **Quand le navigateur tourne sur une autre machine** — WSL, un conteneur,
   une session SSH — la dernière page ne peut pas se charger : copiez l'adresse où elle aboutit
   (`http://127.0.0.1:…/?state=…&code=…`) depuis la barre d'adresse, collez-la dans le terminal
   et appuyez sur Entrée.

**Garder la connexion.** Une application Google laissée au statut de publication **Test** reçoit
des jetons de rafraîchissement qui expirent au bout de 7 jours. Publiez-la (**En production**)
pour les garder.

## Référence des réglages

| Clé (sous `Orkeon:Tools:Drive`) | Signification |
|---|---|
| `DefaultAccount` | Le compte que prend un appel qui n'en nomme aucun ; facultatif avec un seul compte |
| `CredentialsDirectory` | Où sont gardés les jetons (son sous-répertoire `drive`) ; par défaut `credentials` à côté des réglages de l'utilisateur |
| `Screening:WithholdRejected` | Retenir le texte d'un fichier que le filtre anti-injection de prompt rejette (défaut `false` : signaler seulement) |
| `Accounts:<nom>:Provider` | `GoogleDrive` ou `OneDrive` — obligatoire |
| `Accounts:<nom>:Rights` | Parmi `Read, Write, Organize, Share, Delete, Purge` — obligatoire |
| `Accounts:<nom>:Root` | Le dossier auquel le compte est confiné, en chemin depuis la racine du drive (`Orkeon/agents`) ; non renseigné : tout le drive |
| `Accounts:<nom>:Auth:ClientId` | L'application Google Cloud ou Microsoft Entra — obligatoire |
| `Accounts:<nom>:Auth:ClientSecretEnvVar` | Le **nom** de la variable qui contient le secret client Google — obligatoire pour Google Drive |
| `Accounts:<nom>:Auth:Tenant` | Microsoft seulement : `consumers` (défaut), `organizations`, `common` ou un id de tenant |
| `Accounts:<nom>:Share:AllowedRecipients` | Avec qui un agent peut partager : une adresse, `*@domaine` ou `*` ; vide : personne |
| `Accounts:<nom>:Share:AllowPublicLinks` | Si un agent peut créer un lien que quiconque le détient peut ouvrir (défaut `false`) |
| `Accounts:<nom>:MaxUploadMegabytes` | Le plus gros chargement (défaut 100) |
| `Accounts:<nom>:TimeoutSeconds` | Délai HTTP des appels du compte |

La section est validée paresseusement, comme celle de l'e-mail : un compte cassé est signalé
quand un appel ou `orkeon drive` s'en sert, et ne casse jamais un crew qui n'ouvre aucun drive. Le
secret client est lu comme les secrets de l'e-mail : l'environnement du processus, puis — sous
Windows — les variables propres à l'utilisateur.

## Droits, dossier racine et scopes

| Droit | Outils qu'il ouvre |
|---|---|
| `Read` | `drive_list`, `drive_search`, `drive_info`, `drive_read`, `drive_download` |
| `Write` | `drive_upload`, `drive_create_folder` |
| `Organize` | `drive_move` |
| `Share` | `drive_share` |
| `Delete` | `drive_delete` (vers la corbeille) |
| `Purge` | `drive_delete` avec `permanent: true` |

- **Un compte qui ne fait que lire demande un scope en lecture seule** (`drive.readonly`,
  `Files.Read`) : le jeton divulgué d'un crew qui lit ne peut pas écrire non plus. Accorder `Write`
  plus tard change les scopes, et le compte demande une nouvelle connexion.
- **`Root`** est trouvé une fois par run : par chemin sur OneDrive, par nom sous chaque parent sur
  Google Drive (qui n'a pas de chemins). Un élément est dans la racine quand son chemin parent le
  dit (OneDrive) ou quand la remontée de ses parents atteint la racine (Google Drive, 32 niveaux
  au plus). Une recherche sur Google Drive ne peut pas être bornée à une sous-arborescence : ses
  résultats hors de la racine sont écartés, si bien qu'une page peut contenir moins d'éléments que
  `limit`.
- **Un nom déjà pris dans un dossier est refusé** au chargement, à la création de dossier et au
  déplacement : Google Drive accepterait deux éléments du même nom, et un agent qui désigne les
  fichiers par leur nom prendrait alors le mauvais.
- **La corbeille d'abord.** `drive_delete` met à la corbeille (la corbeille de OneDrive), d'où une
  personne le restaure. `permanent: true` exige `Purge` ; un OneDrive personnel n'a pas de
  suppression définitive et la refuse.
- **Un crew dont les outils drive exigent un droit qu'aucun compte prêt n'accorde est refusé au
  départ**, avant tout appel au modèle, avec une ligne par outil — comme pour les outils mail.

## Ce qui atteint l'agent, et ce qui ne sort jamais

- **Le jeton reste auprès de l'API.** Il n'est envoyé qu'à `www.googleapis.com` ou
  `graph.microsoft.com`, en HTTPS. Un téléchargement OneDrive passe par la
  `@microsoft.graph.downloadUrl` pré-authentifiée sans lui, une session de chargement reçoit les
  octets sans lui, et un curseur de pagination qui ne pointe pas vers l'API est refusé.
- **Le contenu est une donnée.** `drive_read` s'ouvre sur un avis qui dit que le texte n'est pas
  fiable, et sur le verdict du filtre anti-injection de prompt (`clean`, `suspicious`,
  `rejected`). Avec `Screening:WithholdRejected`, le texte d'un fichier rejeté est retenu. Les
  noms et métadonnées des autres outils passent par le filtrage habituel des résultats du run.
- **Un téléchargement n'écrase jamais rien.** `drive_download` écrit un nouveau fichier
  (`nom (2).ext` quand le nom est pris) dans un montage où le crew a le droit de créer.
- **Les résultats tiennent dans la boucle d'agent.** Une page est redemandée avec moins
  d'éléments jusqu'à tenir dans un résultat d'outil, si bien que `next_cursor` reprend exactement
  après le dernier élément montré ; `drive_read` rend des tranches avec `next_offset`.

## Utiliser les outils

Depuis un crew YAML :

```yaml
agents:
  archivist:
    role: "Archivist"
    goal: "File the weekly report in the team drive"
    tools: [drive_list, drive_upload, drive_move]
```

Depuis un script `.ork.ts`, les outils sont typés (`Typings/tools.d.ts`), avec les clés
snake_case propres à l'outil :

```ts
const page = await tools.driveList({ folder_id: "root" });
const report = await tools.driveUpload({ path: "/output/report.pdf", folder_id: page.items[0].id });
```

## Commandes

```bash
orkeon drive accounts [--json]     # the accounts, their rights, root folder and readiness (no network)
orkeon drive login <account>       # the one interactive step: device code (OneDrive) or browser (Google)
orkeon drive check <account>       # connects, finds the root folder, lists its first items
orkeon drive logout <account>      # forgets the stored tokens
```

Chaque verbe prend `--settings <fichier>` ; sans lui, les réglages sont résolus comme `orkeon run`
les résout, si bien que les jetons arrivent là où chaque run les cherche. `login` et `check`
prennent aussi `--events jsonl` : une ligne d'événement par étape, pour un programme qui les
pilote — l'onglet Stockage cloud d'Orkeon Studio, qui liste, teste, connecte et déconnecte les
comptes du fichier qu'il édite ([référence CLI](../reference/cli.md#orkeon-drive)).

## Où vivent les jetons

Le runner les garde sous sa racine interne `/credentials`, qu'aucun outil destiné aux agents ne
résout, dans un sous-répertoire `drive` créé réservé au propriétaire sous Unix — à côté du
sous-répertoire `email` de la famille e-mail, dans le même répertoire d'identifiants. Comme pour
l'e-mail, les jetons sont à l'abri des outils du VFS, **pas** d'un `shell_command` qui tourne sous
le même utilisateur du système. `orkeon-repl` ne tient aucun magasin de jetons : un outil drive
appelé depuis la REPL le dit.

## Hors périmètre pour l'instant

Les drives partagés Google et les bibliothèques de documents SharePoint (un compte voit son
propre « Mon Drive » ou son OneDrive), les révisions, les commentaires, le retrait d'un partage, la
copie, et un drive d'essai pour la forge — la forge retire les outils drive d'un crew forgé. Un
drive monté dans le système de fichiers virtuel n'est pas proposé non plus : les outils sont
l'interface.
