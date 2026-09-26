> 🇬🇧 [English version](../../adr/ADR-012-email-tool-family.md)

> **Voir aussi** : [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [Outils e-mail](../guides/email.md) · [Retour à l'index](../INDEX.md)

# ADR-012 — La famille d'outils e-mail, deuxième exception motivée au gel du périmètre

**Statut** : Accepté · **Date** : 2026-09-26
· **Périmètre** : `src/tools/Orkeon.Tools.Email`, `Orkeon.Hosting` (la racine `/credentials` et le magasin de jetons), `orkeon email` (`Orkeon.Scripting.Cli`), le paquet parapluie `Orkeon.Tools`

## Contexte

Le propriétaire a demandé des outils natifs pour lire et envoyer du courrier — SMTP, POP3,
IMAP — et un client Gmail et Hotmail qui lit, écrit, crée des dossiers et y range des messages,
**directement dans Orkeon**. Il existait `email_parser`, qui lisait les `.eml` à coups
d'expressions régulières et rendait un résultat factice pour les `.msg` ; l'inventaire des outils
consignait le manque (« pas de tool pour envoyer des emails »).

Le périmètre est gelé : CONTRIBUTING dit « pas de nouvel outil intégré — écrivez le vôtre dans
un script `.ork.ts` ou un plugin ». Aucune des deux voies ne convient. Un script tourne sur Jint
et ne peut pas ouvrir de socket ; un plugin ne serait pas « directement dans Orkeon », et il
aurait fallu lui construire le même stockage des jetons, les mêmes droits et le même filtrage.

Les contraintes des fournisseurs, vérifiées le 2026-09-26 :

- **Hotmail / Outlook.com** : OAuth2 est obligatoire et les comptes personnels n'ont pas de mot
  de passe d'application. Depuis le 2026-09-24, une régression côté Microsoft fait échouer
  l'IMAP OAuth des comptes grand public (« User is authenticated but not connected »). Microsoft
  Graph (`Mail.ReadWrite`, `Mail.Send`) fonctionne pour eux, accepte le MIME en base64 (4 Mo par
  requête) et garde des ids stables d'un déplacement à l'autre avec
  `Prefer: IdType="ImmutableId"`.
- **Gmail** : un mot de passe d'application (derrière la validation en deux étapes) fonctionne
  toujours en IMAP et SMTP, de même qu'OAuth2 XOAUTH2 avec `https://mail.google.com/`. Le
  *device flow* de Google refuse les scopes Gmail, et une application laissée en « Test » reçoit
  des jetons de rafraîchissement valables 7 jours.

## Décision

1. **Une nouvelle famille, `Orkeon.Tools.Email`, la huitième du paquet parapluie
   `Orkeon.Tools`** — la deuxième exception motivée au gel du périmètre, après les agrégateurs
   OpenRouter et Mammouth (2026-09-18). Treize outils : douze nouveaux, et `email_parser`
   reconstruit (point 7). Les dépendances suivent l'ADR-005 : `Domain`, `Tools.Abstractions`,
   `Constants.Configuration`, et `Orkeon.Rag` pour son détecteur d'injection de prompt —
   `Orkeon.Rag` atteint lui-même `Application`, la route que documente l'amendement de
   l'ADR-006 ; aucune référence directe à `Application` ni à `Infrastructure`. Tout est
   `internal` sauf les extensions DI, le port du magasin de jetons et son implémentation
   fichier, le type d'erreur, l'utilitaire par lequel l'hôte lit l'emplacement des jetons, et la
   surface d'administration qu'utilisent les verbes `orkeon email`.
2. **MailKit 4.18.0 et MimeKit 4.18.1** (MIT) portent IMAP, POP3, SMTP et le modèle de message.
   Tous deux sont référencés directement — la gestion centrale des paquets n'épingle aucune
   version transitive — et le parapluie les déclare à nouveau. Il n'y a qu'un modèle de message
   pour tous les moteurs : les messages Graph sont lus (`$value`) et écrits en MIME eux aussi, et
   une seule conversion produit ce que rendent les outils.
3. **Trois moteurs derrière un même port interne, choisis par le préréglage du compte.**
   IMAP + SMTP (Gmail et tout serveur standard), POP3 + SMTP (la boîte de réception seule ; le
   moteur déclare ses capacités et toute demande au-delà est refusée explicitement, jamais
   abandonnée en silence), et Microsoft Graph sur un simple `HttpClient` — sans SDK Graph — pour
   Outlook.com, Hotmail et Microsoft 365, le défaut du préréglage `Outlook`. Le transport est
   chiffré (`SslOnConnect` ou `StartTls`) ; `None` n'est accepté que vers un hôte de bouclage, et
   aucune option n'accepte un certificat invalide.
4. **OAuth2 est écrit à la main**, sur `HttpClient` : le flux par code d'appareil RFC 8628 pour
   Microsoft (tenant `consumers` par défaut) ; le code d'autorisation avec PKCE pour Google, la
   redirection reçue par un `TcpListener` lié à `127.0.0.1` — pas `HttpListener`, qui exige une
   réservation d'URL sous Windows — avec un repli par collage de l'adresse de retour pour WSL,
   les conteneurs et les shells distants ; rafraîchissement avec rotation du jeton. Les outils
   ne lancent jamais de connexion interactive : un outil sans jeton utilisable répond « lancez
   `orkeon email login <compte>` ».
5. **Les jetons vivent dans la racine interne `/credentials`, par la vue privilégiée du VFS.**
   `RunnerVirtualRoots.Credentials` est une nouvelle racine réservée. L'hôte des runners la
   monte en montage interne seulement quand un compte OAuth est déclaré, et
   `FileSystemEmailTokenStore` y écrit par `PrivilegedFileSystemAccess` (ADR-008), qu'aucun
   outil destiné aux agents ne résout. Physiquement : `<répertoire des réglages de
   l'utilisateur>/credentials/email/`, ou `CredentialsDirectory` pour un service. Créer ce
   répertoire avant qu'aucun montage n'existe (réservé au propriétaire sous Unix) relève du
   bootstrap du runner — la portée `EXCEPTION-BOOTSTRAP` de `Hosting/Runner*`, déjà ratifiée —
   il n'y a donc pas de nouvelle catégorie d'exception VFS. `IEmailTokenStore` reste
   remplaçable par un hôte à soi (un coffre, par exemple).
6. **Les garde-fous sont la frontière, pas le jugement du modèle.** Le modèle nomme un compte ;
   la configuration de l'opérateur porte les serveurs, les identifiants — sous forme de noms de
   variables d'environnement — et les `Rights` obligatoires (`Read`, `Organize`, `Draft`, `Send`,
   `Delete`, `Purge`). L'envoi échoue fermé sur `Send:AllowedRecipients` (vide : personne),
   l'enveloppe SMTP est passée explicitement pour qu'aucun en-tête `Resent-*` ne puisse
   l'élargir, `From` est imposé au compte, et `MaxRecipients` / `MaxPerHour` plafonnent le
   volume ; `email_draft` est la voie de la relecture humaine. Le contenu reçu est marqué non
   fiable et filtré par `PromptInjectionDocumentValidator` sur le texte rendu — signalement par
   défaut, `Screening:WithholdRejected` pour retenir. Chaque outil déclare son `ToolAccess` pour
   la permission gate, et `orkeon forge` refuse les douze outils de boîte aux lettres aux crews
   qu'il forge. Les résultats sont dimensionnés sous le plafond de 4000 caractères de la boucle
   d'agent sur un résultat d'outil, sans nouvel override : le contenu d'un courrier n'est pas un
   livrable de confiance.
7. **`email_parser` rejoint la famille, reconstruit sur MimeKit** — même nom, paramètres
   `path`, `offset` et `max_chars`, la sortie d'`email_read`, `.msg` retiré (il n'a jamais été
   parsé). C'est un changement cassant assumé, avec sa migration dans le CHANGELOG.

## Alternatives écartées

1. **Un script ou un plugin.** Un script ne peut pas ouvrir de socket ; un plugin est hors
   d'Orkeon, ce qui n'est pas ce qui était demandé, et il aurait fallu le même stockage des
   jetons, les mêmes droits et le même filtrage.
2. **MSAL et le SDK Google.** Deux gros graphes de dépendances pour trois échanges HTTP chacun,
   et un cache de jetons qui vivrait hors du VFS.
3. **IMAP pour les comptes personnels Outlook.com.** Cassé par la régression de Microsoft du
   2026-09-24. Graph fonctionne, garde des ids stables d'un déplacement à l'autre et classe
   lui-même le courrier envoyé ; IMAP reste disponible en option du préréglage `Outlook`.
4. **L'API REST Gmail.** Un deuxième moteur Gmail et une autre revue de consentement, pour ce
   qu'IMAP et SMTP couvrent déjà avec un mot de passe d'application ou XOAUTH2 — `X-GM-RAW`
   apporte en IMAP la syntaxe de recherche propre à Gmail.
5. **Une nouvelle catégorie d'exception VFS pour un simple fichier de jetons.** Les jetons
   passent par le VFS comme n'importe quel fichier ; seule la création du répertoire relève du
   bootstrap, et c'est déjà ratifié.

## Conséquences

- Les classes d'outils intégrés passent de 79 à 91 et `Orkeon.Tools` porte huit familles.
  MailKit, MimeKit et leur dépendance BouncyCastle.Cryptography sont redistribués dans les
  outils `orkeon` et `orkeon-repl` et dans les installeurs
  ([THIRD-PARTY-NOTICES](../../../THIRD-PARTY-NOTICES.md)).
- **Cassant** : `email_parser` quitte `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()`
  ne l'enregistre plus, `AddOrkeonEmailTools(configuration)` le fait — et ses paramètres comme
  sa sortie changent.
- **Limites honnêtes** : les fichiers de jetons sont du JSON en clair, à l'abri des outils du VFS
  mais pas d'un outil shell ou de code qui tourne sous le même utilisateur du système ; chaque
  compte est visible de chaque crew et de chaque script qui résout le même fichier de réglages ;
  `orkeon-repl` ne tient aucun magasin de jetons, les comptes OAuth ne fonctionnent donc que sous
  les runners `orkeon`. Les limites de POP3 et de Graph sont dans le
  [guide](../guides/email.md#limites).
- **La validation réelle sur de vrais comptes Gmail et Hotmail est la campagne du propriétaire**
  (MAIL-07). Tant qu'elle n'est pas archivée, la prise en charge des fournisseurs est documentée
  comme en attente de campagne.
- Hors de cette version : supprimer des dossiers, copier un message ou lui donner plusieurs
  libellés Gmail, les envois Graph au-delà d'environ 3 Mo (une session d'upload), l'approbation
  humaine interactive d'un envoi.
