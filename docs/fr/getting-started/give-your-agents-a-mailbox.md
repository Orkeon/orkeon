> 🇬🇧 [English version](../../getting-started/give-your-agents-a-mailbox.md)

# Donner une boîte aux lettres à vos agents

> **Voir aussi** : [Outils e-mail](../guides/email.md) · [Écrire une crew en TypeScript](../guides/write-a-crew-in-typescript.md) · [Référence CLI](../reference/cli.md#orkeon-email) · [Retour à l'index](../INDEX.md)

En un quart d'heure environ, vous allez lancer un agent qui trie une vraie boîte Gmail : il lit
le courrier non lu, range chaque message dans un dossier selon son genre, et écrit les réponses
en **brouillons** qu'un humain relit et envoie. Rien n'est envoyé. Cette page est le chemin
court à travers un exemple fourni ; le [guide des outils e-mail](../guides/email.md) est la
référence complète.

## Ce que vous obtenez

L'exemple est [`13-email-triage.ork.ts`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.ork.ts)
avec son fichier de réglages [`13-email-triage.appsettings.json`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.appsettings.json).
Trois règles tiennent ensemble les outils e-mail, et l'exemple s'appuie sur les trois :

- **Un agent ne fait que nommer un compte.** Serveurs, identifiants et droits sont les réglages de l'opérateur (`Orkeon:Tools:Email`).
- **Les `Rights` du compte décident de ce qu'un agent peut faire**, et l'envoi reste fermé tant que `Send:AllowedRecipients` ne liste pas qui peut recevoir du courrier.
- **Le courrier reçu est une donnée, jamais une instruction.** Chaque résultat de lecture porte le verdict d'un filtre anti-injection de prompt.

## Prérequis

| Il vous faut | Notes |
|---|---|
| Orkeon | Un clone du dépôt, ou l'outil `orkeon` installé — voir [Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md). Un script `.ork.ts` demande aussi esbuild : les archives de release le livrent, un clone l'installe à son premier build, et l'outil dotnet ne le livre pas — `npm install -g esbuild` ([où il est cherché](../architecture/scripting.md#configuration-et-chaîne-doutils) ; `orkeon doctor` le vérifie). |
| Un modèle | Les réglages de l'exemple pointent vers un [Docker Model Runner](https://docs.docker.com/desktop/features/model-runner/) local (`ai/granite-4.0-h-tiny` sur `localhost:12434`). N'importe quel profil de [`examples/appsettings/`](https://github.com/orkeon/orkeon/blob/main/examples/appsettings/README.md) convient : une exécution résout **un seul** fichier de réglages, copiez donc la section `Llm` du profil dans le fichier de l'exemple. |
| Un compte Gmail | Avec la **validation en deux étapes** activée — les mots de passe d'application n'existent pas sans elle. |

## 1. Créer un mot de passe d'application Gmail

1. Ouvrez <https://myaccount.google.com/apppasswords>, créez un mot de passe d'application
   nommé `Orkeon` et copiez les 16 lettres que Google affiche — **sans** les espaces entre les
   groupes. Si Google répond que les mots de passe d'application ne sont pas disponibles pour
   votre compte, passez par [Gmail avec OAuth2](../guides/email.md#gmail-avec-oauth2).
2. Exportez-le dans le shell qui lancera l'exemple :

   ```bash
   export TRIAGE_GMAIL_APP_PASSWORD='abcdefghijklmnop'      # bash / zsh
   ```

   ```powershell
   $env:TRIAGE_GMAIL_APP_PASSWORD = 'abcdefghijklmnop'      # PowerShell
   ```

## 2. Les réglages

Le fichier de réglages de l'exemple déclare un compte sous `Orkeon:Tools:Email` (sa section
`Llm`, et la limite de temps `Scripting` traitée à l'étape 4, sont omises ici). Remplacez l'adresse par la vôtre :

```json
"Orkeon": {
  "Tools": {
    "Email": {
      "DefaultAccount": "triage",
      "Accounts": {
        "triage": {
          "Provider": "Gmail",
          "Address": "your.name@gmail.com",
          "Rights": "Read, Organize, Draft",
          "Auth": {
            "Method": "Password",
            "PasswordEnvVar": "TRIAGE_GMAIL_APP_PASSWORD"
          }
        }
      }
    }
  }
}
```

- **`Provider: Gmail`** est un préréglage : il renseigne `imap.gmail.com:993` pour la lecture et `smtp.gmail.com:465` pour l'envoi, tous deux en TLS.
- **`PasswordEnvVar`** *nomme* la variable d'environnement qui contient le mot de passe. Le secret n'est jamais dans le fichier, ni jamais un argument d'outil — les arguments d'outil sont journalisés. La variable peut porter n'importe quel nom.
- **`Rights`** est obligatoire. `Read, Organize, Draft` permet à l'agent de chercher et lire, de créer des dossiers et d'y déplacer le courrier, et d'enregistrer des brouillons — exactement ce qu'utilise le script. Il ne peut rien envoyer, supprimer ni purger ; un appel hors de ces droits échoue avec un message qui nomme le droit manquant.
- **`DefaultAccount`** est le compte qu'utilise un appel qui n'en nomme aucun. Avec un seul compte, on pourrait l'omettre ; le script ne nomme aucun compte.

## 3. Vérifier le compte depuis le terminal

Les commandes `orkeon email` sont le côté opérateur de la famille — les agents ne les lancent
jamais. Depuis un clone, remplacez `orkeon` par `dotnet run --project src/scripting/Orkeon.Scripting.Cli --`.

```bash
orkeon email accounts --settings examples/scripting/13-email-triage.appsettings.json
orkeon email check triage --settings examples/scripting/13-email-triage.appsettings.json
```

- `accounts` n'ouvre aucune connexion : il liste `triage (default)`, son adresse, comment il lit et envoie, ses droits, et affiche `ready` — ou `NOT READY:` suivi de ce qu'il faut corriger (le plus souvent la variable qui n'est pas définie dans ce shell).
- `check` se connecte, s'authentifie et liste les dossiers, puis affiche `E-mail account 'triage' is reachable:` avec le nombre de dossiers et les compteurs de la boîte de réception.

Codes de sortie : `0` succès, `1` ce que vous corrigez (les réglages, un compte inconnu, une
variable non définie), `2` ce qu'ont fait le serveur ou le réseau (un mot de passe refusé, un
échec de connexion), `130` Ctrl+C. Le fichier de réglages utilisé est nommé sur stderr
(`Using settings: …`).

## 4. Lancer le tri

Depuis un clone, à la racine du dépôt :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/13-email-triage.ork.ts \
  --settings examples/scripting/13-email-triage.appsettings.json
```

Avec l'outil installé, placez les deux fichiers de l'exemple côte à côte et lancez :

```bash
orkeon run 13-email-triage.ork.ts --settings 13-email-triage.appsettings.json
```

Ce que fait le script, dans l'ordre :

1. Il s'arrête aussitôt si les réglages ne déclarent aucun modèle — l'écho qui répond sans modèle ne sait pas trier du courrier.
2. Il crée les dossiers `Triage/Reply`, `Triage/Read`, `Triage/Newsletters` et `Triage/Review` (des libellés sur Gmail). Un dossier existant est laissé tel quel : chaque exécution peut le refaire.
3. Il prend une page de la boîte de réception : les dix messages **non lus** les plus récents (`email_search`). Les lire (`email_read`) les laisse non lus.
4. Un message que le filtre anti-injection ne juge pas `clean` n'atteint jamais le modèle : il part dans `Triage/Review`. Les autres sont remis au modèle encadrés, comme des données, et classés en `reply`, `read` ou `newsletter` ; toute autre réponse part aussi dans `Triage/Review`.
5. Pour un `reply`, le modèle rédige une courte réponse que le script enregistre comme brouillon de réponse (`email_draft`) — il attend dans les Brouillons de Gmail.
6. Chaque message est ensuite déplacé dans son dossier (`email_move`), un à la fois : une exécution interrompue à mi-chemin a rangé ce qu'elle a fini, et la suivante reprend sur le reste.

L'exécution se termine en affichant son résultat en JSON sur stdout — la ligne de synthèse de
l'agent, le nombre de messages rangés par genre et le nombre de brouillons :

```json
{
  "result": {
    "output": "sorted 10 message(s), 3 draft(s) to review",
    "filed": { "reply": 3, "read": 4, "newsletter": 2, "review": 1 },
    "drafts": 3
  }
}
```

Les nombres ci-dessus sont illustratifs. Ouvrez Gmail : la boîte de réception a perdu ces
messages, les libellés `Triage/…` les contiennent, et les Brouillons contiennent les réponses,
que vous modifiez et envoyez — ou non.

> **Une limite de temps s'applique.** Le bac à sable du scripting borne une exécution entière
> à 30 secondes de temps réel par défaut, appels de messagerie et de modèle attendus compris,
> et dix messages passés par un modèle local prennent plus longtemps. Le fichier de réglages
> de l'exemple relève la borne à dix minutes,
> `"Orkeon": { "Scripting": { "Limits": { "ExecutionTimeout": "00:10:00" } } }` à côté de
> `Tools` ; gardez ce bloc quand vous écrivez votre propre fichier de réglages. Les messages
> rangés avant un arrêt restent rangés.

## 5. L'adapter

- **D'autres dossiers, d'autres genres.** L'objet `folders` en tête du script associe chaque genre à un dossier, et `kinds` liste ce que le modèle peut répondre ; changez les deux, ainsi que le prompt qui nomme les genres. `emailSearch` accepte d'autres critères (`from`, `subject`, `since`, `limit` jusqu'à 50…) — les [paramètres](../guides/email.md#paramètres) les listent. Dans un script, les noms d'outils sont en camelCase (`tools.emailSearch`) mais les clés des arguments gardent le snake_case des outils (`unread_only`, `reply_to_id`).
- **Le laisser envoyer — sans risque.** Ajoutez `Send` aux `Rights` **et** listez les destinataires : tant que `Send:AllowedRecipients` ne contient aucune entrée, `email_send` refuse tout message. Les entrées sont des adresses, `*@domaine` ou `*` ; To, Cc et Bcc sont tous vérifiés, et `MaxRecipients` / `MaxPerHour` plafonnent le volume. Les brouillons restent le meilleur chemin pour toute personne hors de vos propres adresses. Voir [Droits et liste d'autorisation d'envoi](../guides/email.md#droits-et-liste-dautorisation-denvoi).

  ```json
  "Rights": "Read, Organize, Draft, Send",
  "Send": { "AllowedRecipients": [ "your.name@gmail.com" ], "MaxPerHour": 10 }
  ```

- **Depuis un crew YAML plutôt.** L'hôte d'exécution derrière `orkeon run` enregistre les treize outils `email_*` : un agent YAML les liste par leur nom — `tools: [email_accounts, email_search, email_read, email_move, email_draft]` — et c'est le modèle qui décide des appels. Déclarez les comptes dans le fichier de réglages **propre à ce crew** (il contient aussi la section `Llm` du crew), pas dans un `appsettings.json` du dossier d'où vous lancez, ni dans des variables d'environnement : ceux-là atteignent toutes les exécutions. Voir [Depuis un crew YAML](../guides/email.md#depuis-un-crew-yaml). `orkeon forge` ne met jamais les outils de boîte aux lettres dans les crews qu'il forge.

## Autres boîtes aux lettres

- **Hotmail et Outlook.com** passent par Microsoft Graph en OAuth2 : enregistrez une application une fois, puis connectez-vous une fois avec `orkeon email login <compte>` — [Hotmail et Outlook.com (Microsoft Graph)](../guides/email.md#hotmail-et-outlookcom-microsoft-graph).
- **Gmail sans mot de passe d'application** : [Gmail avec OAuth2](../guides/email.md#gmail-avec-oauth2).
- **Votre propre serveur**, en IMAP ou POP3 et SMTP, avec le préréglage `Custom` : [Votre propre serveur (IMAP, POP3, SMTP)](../guides/email.md#votre-propre-serveur-imap-pop3-smtp).
- **Où sont stockés les jetons OAuth**, et ce que cela protège : [Où vivent les jetons](../guides/email.md#où-vivent-les-jetons).
- **Quelque chose refuse ?** Chaque erreur nomme sa correction ; les cas courants sont dans [Dépannage](../guides/email.md#dépannage).

> **Campagne en attente.** Les outils e-mail n'ont pas encore été exécutés contre de vrais
> comptes Gmail et Hotmail : cette campagne réelle (MAIL-07) est en attente, comme le dit le
> [guide](../guides/email.md).
