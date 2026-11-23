> 🇬🇧 [English version](../../adr/ADR-021-chat-tool-family.md)

> **Voir aussi** : [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-012](./ADR-012-email-tool-family.md) · [ADR-013](./ADR-013-drive-tool-family.md) · [Retour à l'index](../INDEX.md)

# ADR-021 — La famille d'outils chat, Microsoft Teams d'abord : cinquième exception motivée au gel du périmètre

**Statut** : Accepté · **Date** : 2026-11-23
· **Portée** : une future famille `Orkeon.Tools.Chat`, `Orkeon.Tools.Abstractions/Auth` (le client OAuth qu'elle partage avec les familles e-mail et drive), `Orkeon.Hosting` (le sous-répertoire `chat` de `/credentials`), `orkeon chat` (`Orkeon.Scripting.Cli`), `Orkeon.Host` (le canal entrant Teams), le paquet ombrelle `Orkeon.Tools`

Cet ADR consigne la décision et la conception. Il ne livre aucun outil : les outils arrivent avec les lots qui le suivent, et ces lots corrigent les décomptes, l'inventaire, la page des limites et CONTRIBUTING.

## Contexte

L'issue #446 demande Microsoft Teams. Un agent lancé par Orkeon ne peut ni lire un canal ou une conversation, ni poster un résultat là où l'équipe travaille, ni lire la transcription d'une réunion. Aucun code ne nomme Teams ; l'inventaire des outils le nomme une fois, pour dire qu'il manque (« there is no SMS, push or Teams tool »), et la note de conception de la passerelle de messagerie le range comme canal de conversation d'entreprise de la passerelle.

Pour le chat comme outil, il n'existe que deux outils Slack nommés d'après le fournisseur, `slack_send_message` et `slack_read_messages`. Aucune racine livrée ne les enregistre : un jeton de bot passé par un hôte écrit en C#, aucune clé de configuration, aucun droit, aucune liste d'autorisation.

Le périmètre est gelé (CONTRIBUTING, « The scope is frozen »). Le cas est celui d'ADR-012 et d'ADR-013 : un script `.ork.ts` tourne sur Jint et ne peut ni détenir un jeton OAuth ni garder une connexion, et un plugin ne mettrait pas les droits, la liste d'autorisation et le filtrage dans Orkeon même.

L'essentiel de ce qu'il faut à un connecteur Teams est déjà livré :

- Microsoft Graph v1.0 sur un `HttpClient` simple, pour le courrier et pour OneDrive, sans paquet `Microsoft.Graph` ni MSAL (`EmailDefaults.GraphHost` vérifie qu'un lien de pagination reste sur l'hôte Graph).
- Un client OAuth partagé dans `src/tools/Orkeon.Tools.Abstractions/Auth/` (`OAuth2Client`, `LoopbackRedirectListener`, `OAuthSignIn`) : code d'autorisation avec PKCE sur une redirection loopback, code d'appareil, rafraîchissement, jetons gardés sous la racine interne `/credentials`. Une connexion exécute le seul flux que ses réglages nomment.
- Un modèle de compte fixé par l'opérateur, jamais par le modèle : `Rights` obligatoire, liste d'autorisation fermée quand elle est vide, plafond d'envois par heure, secrets donnés comme noms de variables d'environnement, contenu filtré contre l'injection de prompt.
- Une passerelle de chat dans `orkeon-host` avec un seul canal, Discord (`IChatChannel`, `DiscordChannel`), où un canal activé dont `AllowedUserIds` est vide refuse le démarrage (`ChatChannelService`). L'hôte a aussi ses propres écoutes HTTP (l'API de runs, A2A).

## Décision

1. **Une cinquième exception motivée au gel du périmètre : une famille `chat_*` de dix outils.** Les noms disent la fonction, jamais le fournisseur : un second fournisseur (Slack, puis d'autres) ajoute un fournisseur et aucun outil. Les deux outils Slack deviennent le second fournisseur dans une suite et quittent alors le décompte. Microsoft Teams est le premier fournisseur.

   | Outil | Fait | Accès |
   |---|---|---|
   | `chat_accounts` | les comptes déclarés, leur fournisseur, leurs droits et l'état de connexion | Read |
   | `chat_list` | les lieux d'un compte : équipes, canaux, conversations | Read |
   | `chat_read` | les messages d'un canal ou d'une conversation, ou les réponses à un message | Read |
   | `chat_search` | les messages par texte, par auteur, ou ceux qui mentionnent l'utilisateur connecté | Read |
   | `chat_members` | les membres d'un lieu ; une personne recherchée, pour la cibler ou la mentionner | Read |
   | `chat_meetings` | les transcriptions d'une réunion, et le texte de l'une d'elles | Read |
   | `chat_send` | un message à un canal, une conversation ou une personne, ou une réponse | Execute |
   | `chat_edit` | réécrire un message que le compte a envoyé | Execute |
   | `chat_delete` | supprimer un message que le compte a envoyé | Execute |
   | `chat_react` | poser ou retirer une réaction | Execute |

   Les classes d'accès sont celles que déclarent `email_read` et `email_send` : le mode `plan` lit et ne poste jamais. Une mention est un identifiant de membre rendu par `chat_members`, jamais un nom écrit par le modèle. Aucun outil ne surveille un lieu pour de nouveaux messages : Graph n'autorise l'interrogation des changements qu'une fois par jour au plus, et le temps réel est le canal entrant (décision 8). Cette famille ne téléverse pas de fichier : `drive_upload` le dépose dans le OneDrive de l'expéditeur et `chat_send` le lie.

2. **L'opérateur possède comptes, droits et lieux ; l'agent nomme un compte.** Les comptes vivent sous `Orkeon:Tools:Chat:Accounts:<nom>` avec un `Provider`. `Rights` est obligatoire (`Read`, `Send`, `Edit`, `Delete`, `React`, `Meetings`) ; un outil dont le droit manque répond avec le droit à ajouter. Un lieu s'écrit d'une seule façon dans les réglages et dans un appel : `team:<équipe>`, `team:<équipe>/channel:<canal>`, `chat:<id>`, `user:<adresse>` ou `user:*@<domaine>`, ou `*`. Les noms sont résolus en identifiants à la première utilisation du compte, puis seuls les identifiants sont comparés ; un nom qui ne correspond à rien ou à deux lieux met l'entrée de côté avec un message qui la nomme. `Send:AllowedPlaces` et `Read:AllowedPlaces` sont fermées toutes deux quand elles sont vides (D9), `Send:MaxPerHour` plafonne le processus, et l'adaptateur respecte les limites propres de Graph et `Retry-After`. `Cloud` choisit l'hôte Graph et l'autorité de connexion (`Global`, `UsGov`, `UsGovDoD`, `China`) ; un lien de pagination n'est suivi que sur l'hôte du compte. Un échec est un code typé dont le message donne le remède (`RightMissing`, `PlaceNotAllowed`, `ConsentMissing`, `SignInRequired`, `Throttled`, `NotFound`, `NotSupportedByAccount`).

3. **Les portées suivent les droits, et les portées d'administrateur seulement quand `AdminGranted` les nomme.** Les portées demandées sont celles que les `Rights` du compte exigent, jamais davantage. Trois groupes de permissions déléguées exigent un administrateur du locataire (`ChannelMessage.Read.All` pour lire un canal, `ChannelMessage.ReadWrite` pour y éditer ou supprimer, et les portées de transcription de réunion), et Entra refuse toute la demande de consentement quand l'une de ses permissions exige l'administrateur. Elles ne sont donc jamais demandées par défaut : `Auth:AdminGranted` liste ce que l'administrateur a consenti pour cette application (`ChannelMessages`, `ChannelEdit`, `Meetings`), chaque entrée ajoute sa portée à la connexion suivante, et sans elle l'outil répond `ConsentMissing` avec la permission et qui l'accorde, au lieu d'échouer sur un 403 nu. Sans administrateur, un utilisateur lit et écrit encore ses conversations et poste dans les canaux. Un compte détient un seul jeu de jetons ; une nouvelle connexion redemande tout le jeu et le remplace.

4. **Connexion : loopback d'abord, `--device-code` sur demande.** `orkeon chat login <compte>` utilise le navigateur sur une redirection loopback, et le code d'appareil seulement quand `--device-code` le demande (un conteneur, un shell distant). Un compte Microsoft n'a aujourd'hui que le code d'appareil, et une politique d'accès conditionnel gérée par Microsoft bloque ce flux dans de nombreux locataires : le flux se choisit donc à la connexion et n'est pas fixé par le fournisseur. Les comptes e-mail et drive gardent leur flux. La famille a `login`, `logout` (jetons supprimés, jeton de rafraîchissement révoqué), `check` et `accounts`. `orkeon-host` n'a pas de terminal et n'ouvre jamais de connexion : un jeton refusé plus tard fait répondre `SignInRequired` aux outils, avec la commande à lancer. Les jetons vont sous `/credentials/chat`.

5. **Un compte webhook, en envoi seul.** `"Provider": "TeamsWebhook"` porte `Webhook:UrlEnvVar` (l'URL est un secret : son nom seul), n'a que le droit `Send` et sert `chat_send` sans connexion. L'URL fixe le lieu : `chat_send` n'en prend pas et le compte n'a pas de `Send:AllowedPlaces` (déclarer le compte est l'ouverture) ; `Send:MaxPerHour` s'applique. Il remplace les connecteurs Office 365 retirés par un webhook « Workflows » de Power Automate.

6. **Un compte d'application, en lecture seule.** `"Auth": { "Method": "ClientCredentials" }` avec un certificat ou un jeton fédéré, chacun nommé par une variable d'environnement, ou un secret client pour un locataire qui n'émet rien d'autre. Le flux client-credentials du client partagé, utilisé aujourd'hui par le client MCP avec un secret client, gagne l'assertion signée. Une application n'a pas de conversations propres et ne rejoint aucune équipe : elle lit ce que `Read:AllowedPlaces` nomme et rien d'autre (`*` est refusé), et ce qui veut dire « moi », avec `chat_search`, répond `NotSupportedByAccount`. Graph refuse les envois d'une application hors migration de données ; un tel compte est donc refusé à la validation quand ses `Rights` dépassent `Read` et `Meetings`. Un pipeline qui doit aussi poster l'associe à un compte webhook.

7. **Un message est du texte non fiable, et le forgeron refuse la famille.** Un corps est du HTML écrit par quiconque peut poster dans le lieu, invité compris. Il parvient au modèle rendu en texte, filtré par le validateur d'injection de prompt de la famille e-mail, signalé par défaut et retenu sous `Orkeon:Tools:Chat:Screening:WithholdRejected`, et coupé par `Orkeon:Tools:ResultLimits`. Un message que Teams marque comme violation de politique est rendu retenu, jamais cité. Le forgeron refuse la famille aux équipages qu'il forge, comme il refuse les outils drive (ADR-013, décision 7) : il n'existe pas de locataire d'essai.

8. **Un canal entrant dans `orkeon-host`, avec sa propre écoute et sans SDK Teams.** `TeamsChannel` implémente `IChatChannel` à côté de `DiscordChannel`, sous `Orkeon:Host:Teams` (`Enabled` désactivé par défaut, l'identifiant et le secret de l'application par nom de variable, `AllowedUserIds` portant des identifiants d'objet Entra, démarrage refusé quand elle est vide, comme pour Discord) : un fil est un run, la réponse revient dans le fil. À la différence de Discord, c'est Teams qui appelle le bot : l'hôte a donc besoin d'un point HTTPS joignable depuis Microsoft et de la vérification du jeton que porte chaque activité entrante. L'écoute est la sienne (`Orkeon:Host:Teams:Prefix`), ni celle de l'API de runs ni celle d'A2A ; le protocole de bot est écrit à la main sur HTTP, comme Graph, et le jeton est vérifié avec les paquets `Microsoft.IdentityModel.*` que le dépôt épingle déjà. Aucun paquet de SDK Teams n'est ajouté. C'est la partie à la plus grande surface, et la dernière livrée.

9. **Répertoire des identifiants.** Les jetons chat vivent dans un sous-répertoire `chat` de la même racine `/credentials` que les deux autres familles, avec la règle d'ADR-013, décision 6.

### Décisions retenues (D1–D10)

Chacune était une question laissée ouverte par l'issue ; la recommandation a été retenue, et chacune peut changer sans toucher aux autres.

| # | Question | Retenu |
|---|---|---|
| D1 | À qui l'application Entra | Chaque utilisateur enregistre la sienne (`Auth:ClientId`), comme pour le courrier. Pas d'application multi-locataire au nom d'Orkeon. |
| D2 | Réunions | Le lien de participation, ou la conversation de réunion quand Graph donne le lien avec elle. Le calendrier (`Calendars.Read`) reste dehors. |
| D3 | Fichiers d'un canal | `chat_send` n'attache rien dans un canal au départ ; SharePoint reste hors ADR-013. |
| D4 | Réactions dans un canal | Livrées pour les conversations ; dans un canal seulement après essai sur un vrai locataire, sinon `NotSupportedByAccount`. |
| D5 | Cloud Chine | Deux pages Microsoft donnent deux hôtes de connexion : `China` est livré en dernier, ou refusé à la validation, tant qu'un locataire ne tranche pas. |
| D6 | Écoute et protocole entrants | Une écoute propre, le protocole de bot à la main sur HTTP, jeton vérifié avec les paquets `Microsoft.IdentityModel.*` épinglés. Pas de SDK Teams. |
| D7 | Un bot pour `chat_send` | Le bot reste celui de la passerelle seule. |
| D8 | Cartes adaptatives | Texte et Markdown seulement. |
| D9 | `Read:AllowedPlaces` vide | Fermé, comme `Send` : une seule règle. |
| D10 | Les deux outils Slack | Inchangés ici ; second fournisseur dans une suite. |

## Alternatives envisagées

- **Un serveur MCP** (le serveur Teams de Microsoft, un serveur communautaire). Ses outils arrivent avec la classe d'accès `Unspecified`, une écriture pour la barrière : le mode `plan` refuse donc aussi les lectures, sauf si l'opérateur fait confiance aux annotations du serveur. Le client MCP ne persiste aucun jeton, donc une connexion par appareil est demandée à chaque run, et il n'a pas de flux par code d'autorisation. Pas de droits par compte, pas de liste d'autorisation de lieux, pas de filtrage.
- **Un plugin** : confiance totale, non livré, et le modèle de compte serait reconstruit hors d'Orkeon.
- **Des outils `teams_*`** : une famille par fournisseur, comme ont commencé les deux outils Slack ; dix outils de plus à chaque nouveau service.
- **`http_api` à la main** : aucun jeton gardé, aucun rafraîchissement, les portées et les chemins Graph laissés au modèle.
- **Le webhook seul** : poste dans un seul lieu, ne lit rien.

## Conséquences

- Dix outils, un fournisseur par compte, et les parties connexion, webhook, compte d'application et canal entrant décrites ci-dessus, livrées en lots après celui-ci. Tant qu'un lot n'a pas livré une partie, cette page énonce la décision et non le comportement.
- Pages à corriger avec le lot qui ajoute les outils : CONTRIBUTING (« The scope is frozen »), la page des limites, l'inventaire des outils (dont la ligne qui dit qu'il n'existe pas d'outil Teams), les décomptes d'outils, les typings, un guide `chat` et la référence de configuration.
- Aucun locataire Microsoft 365 n'est disponible ici pour les mainteneurs : les outils sont validés sur des réponses Graph rejouées, et les essais sur un vrai locataire (D4, D5, transcriptions, webhook) reviennent au propriétaire.
