> 🇬🇧 [English version](SECURITY.md)

# Politique de sécurité

## Versions prises en charge

| Version | Prise en charge |
|---|---|
| 1.0.x (release candidates comprises) | ✅ |
| ≤ 0.9.x (beta) | ❌ |

Seule la dernière release publiée (actuellement la ligne release-candidate 1.0.0) reçoit des correctifs de sécurité.

## Signaler une vulnérabilité

**Merci de ne pas ouvrir d'issue publique pour les vulnérabilités de sécurité.**

Signalez les vulnérabilités via le [GitHub Private Vulnerability Reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) de ce dépôt (onglet « Security » → « Report a vulnerability »).

À inclure dans le signalement :

- Une description de la vulnérabilité et de son impact.
- Les étapes de reproduction (code ou configuration de preuve de concept si possible).
- Le composant affecté (outil, fournisseur LLM, fournisseur de mémoire, orchestrateur…).

Nous visons un accusé de réception sous **72 heures** et un plan de remédiation ou un correctif sous **30 jours** pour les problèmes confirmés.

## Modèle de menace — Exécution d'outils pilotée par LLM

Orkeon est un framework d'orchestration d'agents IA : **la sortie d'un LLM peut déclencher l'exécution d'outils**. Cela rend certains composants sensibles en matière de sécurité par conception, et vous devez considérer toute configuration de crew comme faisant partie de votre surface d'attaque :

- **Outils d'exécution de code/shell** (`ShellCommandTool`, `SecureCodeInterpreterTool`) : les commandes shell sont restreintes par allowlist par défaut (ensemble en lecture seule) et les interpréteurs (`node`, `dotnet`, `npm`, `find`) ainsi que les sous-commandes `git` mutantes nécessitent un opt-in explicite. `shell_command` s'exécute sur l'hôte **sans aucun confinement au niveau de l'OS**. `code_interpreter` s'exécute dans le sandbox Docker (`DockerSandbox`) et échoue fermé : quand Docker est indisponible, il refuse de se rabattre sur une exécution directe sur l'hôte tant que `Orkeon:CodeSandbox:AllowHostExecution` n'est pas posé.
- **Outils réseau** (`WebScrapeTool`, `HttpApiTool`, recherche web, `rag_ingest` et le repli web RAG opt-in) : la validation d'URL est **fail-closed** — les adresses privées, de loopback, link-local, IPv6 non spécifiée, multicast, NAT64 et de métadonnées cloud sont refusées par défaut (protection SSRF), et l'ingestion d'URL refuse de fetcher tant qu'aucun `IUrlValidator` n'est enregistré (`AddOrkeonInfrastructure` en enregistre un). Les redirections ne sont pas suivies : `AddOrkeonWebTools` donne aux outils web leur propre `HttpClient` nommé et au loader de pages RAG son propre client typé, tous deux avec `AllowAutoRedirect = false`, de sorte qu'une redirection ne peut pas emmener une requête au-delà du validateur qui a autorisé le premier saut.
- **Outils de système de fichiers** : tout accès aux fichiers passe par le Virtual File System (`IFileSystemService`), validé contre les montages configurés et les droits d'accès.
- **Outils de base de données** : les requêtes passent par `IDatabaseSecurityPolicy` (allowlist par type d'instruction).
- **Outils e-mail** (`email_*`) : ils agissent sur une vraie boîte aux lettres et lisent du courrier écrit par des inconnus. Un agent ne fait que nommer un compte ; serveurs, identifiants et `Rights` obligatoires de chaque compte relèvent de la configuration de l'opérateur, et un secret n'y est jamais que le *nom* d'une variable d'environnement. L'envoi est fermé par défaut — une liste `Send:AllowedRecipients` vide n'autorise personne, et l'enveloppe SMTP est la liste de destinataires contrôlée, jamais lue dans les en-têtes. Chaque message lu est marqué non fiable et passé au filtre anti-injection de prompt, qui signale mais peut être contourné. **Chaque compte est visible de chaque crew et de chaque script `.ork.ts` qui résout le même fichier de réglages** (les crews hébergés par `orkeon-host` partagent celui de l'hôte) : déclarez les comptes dans le propre fichier de réglages du crew — pas dans des variables d'environnement ni dans un `appsettings.json` du répertoire d'où partent les runs, que l'hôte .NET lit pour chaque run —, accordez le moins de droits possible, préférez `email_draft` à `email_send`, et ne donnez jamais à un crew non fiable à la fois le `Read` e-mail et un canal sortant (`http_api`, les outils web). Les jetons OAuth sont dans un fichier sous le répertoire des réglages de l'utilisateur, hors de portée des outils du VFS mais **pas** d'un outil shell ou de code qui tourne sous le même utilisateur du système. Détails : [Outils e-mail](docs/fr/guides/email.md) et [Sécurité](docs/fr/architecture/security.md#outils-e-mail).
- **Injection de prompt** : tout contenu récupéré par les outils (pages web, fichiers, lignes de base de données) peut contenir des instructions adverses. Exécutez les agents avec l'ensemble d'outils au moindre privilège que votre cas d'usage permet.

Les vulnérabilités dans ces couches (contournement d'allowlist, contournement du filtre SSRF, évasion du VFS, évasion du sandbox, fuite de secrets dans les logs, un envoi hors de la liste de destinataires autorisés d'un compte) sont considérées comme de **sévérité haute** — merci de les signaler en privé.

## Recommandations de durcissement

- **`code_interpreter` n'est jamais exposé comme `IBaseTool`** : `SecureCodeInterpreterTool`
  n'est enregistré que comme type concret, donc aucun agent ne peut l'appeler tant que vous ne
  l'exposez pas vous-même.
- **`shell_command`, lui, est livré enregistré.** `AddOrkeonCodeTools()` l'enregistre comme
  `IBaseTool`, et les deux runners livrés (`orkeon run`, `orkeon-repl`) appellent cette méthode
  sans condition : l'outil est donc au catalogue par défaut. Son allowlist par défaut est en
  **lecture seule** (`ls`, `cat`, `pwd`, `grep`… plus les sous-commandes `git` non mutantes) ;
  les interpréteurs et le `git` mutant restent fermés tant que vous ne posez pas
  `Orkeon:Tools:Shell:AllowInterpreters`, qui est **équivalent à une RCE** et fait émettre à
  l'outil un avertissement de sécurité. Préférez ajouter la seule commande dont vous avez
  besoin à `Orkeon:Tools:Shell:ExtraAllowedCommands` ; `Orkeon:Tools:Shell:AllowedCommands`
  remplace purement et simplement l'allowlist. Pour tenir `shell_command` hors de portée d'un
  agent, composez votre hôte sans `AddOrkeonCodeTools()`.
- Utilisez `DockerSandbox` pour tout scénario d'exécution de code avec des entrées non
  fiables, et laissez `Orkeon:CodeSandbox:AllowHostExecution` désactivé.
- Configurez les clés d'API via des variables d'environnement ou un gestionnaire de secrets — jamais dans les définitions YAML de crew.
- Activez le chiffrement de la mémoire au repos (`EncryptedMemoryProviderDecorator`) pour les charges de travail sensibles.

## Vérifier ce que vous installez

Chaque artefact — paquets NuGet, installeurs, archives CLI, `.deb`, MSI — est construit par
un workflow GitHub Actions public et porte une **attestation de provenance de build** signée
par GitHub (SLSA v1) qui nomme le workflow, le tag et le commit ayant produit ces octets
exacts. La publication sur NuGet.org passe par Trusted Publishing (OIDC) : aucune clé API
longue durée n'existe. Chaque release livre aussi `SHA256SUMS` (et `SHA256SUMS.msi` pour
les MSI) et — pour les releases coupées depuis l'arrivée de l'étape SBOM en septembre 2026 —
un SBOM CycloneDX couvert par la même attestation. Deux choses ne portent **aucune**
attestation : les builds dev de `main` (`<version>.dev.<numéro de run>`, sur GitHub Packages
uniquement) et l'image conteneur `orkeon-runners` sur GHCR.

```bash
gh attestation verify orkeon-cli-<version>-osx-arm64.tar.gz --repo Orkeon/orkeon
```

Un paquet téléchargé depuis nuget.org est re-signé par nuget.org, ce qui change ses
octets : retirez d'abord cette signature (`scripts/nupkg-unsign.py`), puis vérifiez. Les
commandes exactes, ce que chaque mécanisme prouve ou non, et la conduite à tenir quand une
vérification échoue sont dans [Vérifier ce que vous installez](docs/fr/guides/verify-what-you-install.md).
Un artefact qui échoue à la vérification est un signalement de sécurité, pas une question
de support.
