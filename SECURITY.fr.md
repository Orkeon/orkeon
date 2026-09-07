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

- **Outils d'exécution de code/shell** (`ShellCommandTool`, `SecureCodeInterpreterTool`) : les commandes sont restreintes par allowlist par défaut (ensemble en lecture seule) et les interpréteurs (`node`, `dotnet`, `npm`) nécessitent un opt-in explicite. Il n'y a **aucun confinement au niveau de l'OS** sauf si vous routez l'exécution à travers le sandbox Docker (`DockerSandbox`).
- **Outils réseau** (`WebScrapeTool`, `HttpApiTool`, recherche web) : la validation d'URL est **fail-closed** — les adresses privées, de loopback, link-local et de métadonnées cloud sont refusées par défaut (protection SSRF). Les redirections ne sont pas suivies automatiquement.
- **Outils de système de fichiers** : tout accès aux fichiers passe par le Virtual File System (`IFileSystemService`), validé contre les montages configurés et les droits d'accès.
- **Outils de base de données** : les requêtes passent par `IDatabaseSecurityPolicy` (allowlist par type d'instruction).
- **Injection de prompt** : tout contenu récupéré par les outils (pages web, fichiers, lignes de base de données) peut contenir des instructions adverses. Exécutez les agents avec l'ensemble d'outils au moindre privilège que votre cas d'usage permet.

Les vulnérabilités dans ces couches (contournement d'allowlist, contournement du filtre SSRF, évasion du VFS, évasion du sandbox, fuite de secrets dans les logs) sont considérées comme de **sévérité haute** — merci de les signaler en privé.

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
  l'outil un avertissement de sécurité. Pour tenir `shell_command` hors de portée d'un agent,
  composez votre hôte sans `AddOrkeonCodeTools()`.
- Utilisez `DockerSandbox` pour tout scénario d'exécution de code avec des entrées non fiables.
- Configurez les clés d'API via des variables d'environnement ou un gestionnaire de secrets — jamais dans les définitions YAML de crew.
- Activez le chiffrement de la mémoire au repos (`EncryptedMemoryProviderDecorator`) pour les charges de travail sensibles.
