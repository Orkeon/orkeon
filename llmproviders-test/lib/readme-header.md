<!-- Généré par lib/recap.sh — n'éditez pas README.md, éditez lib/readme-header.md. -->

# Campagnes de validation des providers LLM

Ce dossier porte les **preuves d'exécution réelle** des 12 providers LLM d'Orkéon : les
scripts qui lancent les campagnes, et le rapport archivé de chacune.

> **Pourquoi ce dossier existe.** Le dépôt compte ≈ 400 tests unitaires sur les providers
> LLM, et **tous parlent à un handler HTTP mocké**. Un mock prouve qu'Orkéon envoie ce
> qu'on croit ; il ne prouve pas que le fournisseur l'accepte. C'est la seconde preuve qui
> se construit ici.
>
> Référence du protocole : [`backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md`](../backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md)
> (§5 modes, §7 journal) · chantier : [`backstage/tasks/LLM-08-campagnes-test-reel.md`](../backstage/tasks/LLM-08-campagnes-test-reel.md)

## Prérequis

| | Linux / macOS (`run-campaign.sh`) | Windows (`run-campaign.ps1`) |
|---|---|---|
| **jq** | **requis** — lecture du JSON de campagne et rendu du rapport | non : PowerShell a `ConvertFrom-Json` |
| bash | ≥ 4 (`mapfile`, tableaux associatifs) — le bash 3.2 de macOS ne suffit pas | ➖ |
| PowerShell | ➖ | ≥ 7 |
| .NET SDK | requis, sauf si l'outil `orkeon` est déjà installé ou `ORKEON_BIN` défini | idem |

```bash
# Debian / Ubuntu / WSL
sudo apt-get update && sudo apt-get install -y jq
# macOS
brew install jq
```

Le miroir PowerShell n'a **aucune dépendance externe** au-delà de .NET : sous Windows,
`run-campaign.ps1` fonctionne sans rien installer de plus.

## Démarrer

```bash
# 1. Partir du gabarit — jamais l'inverse : providers.example.json est versionné.
cp llmproviders-test/providers.example.json llmproviders-test/providers.local.json

# 2. Renseigner les variables d'environnement que ce fichier nomme.
export OPENAI_API_KEY=…

# 3. Voir ce qui serait lancé, sans dépenser un crédit.
llmproviders-test/run-campaign.sh --all --config llmproviders-test/providers.local.json --dry-run

# 4. Lancer pour de vrai — Ollama d'abord, son coût est nul.
llmproviders-test/run-campaign.sh --provider ollama --model llama3.2
```

Sous Windows, `run-campaign.ps1` expose exactement les mêmes options.

## Options

| Option | Rôle |
|---|---|
| `--provider <clé>` | `openai`, `anthropic`, `ollama`, `azure`, `groq`, `together`, `qwen`, `deepseek`, `kimi`, `mistral`, `huggingface`, `zai` |
| `--all` | Tous les providers déclarés dans le JSON |
| `--model <id\|glob>` | Un identifiant, ou un motif (`gpt-5.6-*`, `*flash*`) |
| `--modes M1,M8` | Défaut : tous les modes du harnais |
| `--config <fichier>` | Configuration de campagne (voir `providers.schema.json`) |
| `--base-url <url>` | Surcharge d'endpoint (Azure, miroirs régionaux) |
| `--api-version <v>` | `api-version` Azure en mode deployment |
| `--api-key-env <VAR>` | Variable portant la clé |
| `--max-models <n>` | Plafond du wildcard (défaut 5 ; `providers.example.json` l'abaisse à 3) |
| `--dry-run` | Résout et affiche le plan, n'appelle rien |
| `--no-recap` | Ne reconstruit pas l'index — pour les lancements parallèles |
| `--timeout <s>` | Délai par requête, défaut **180 s** (un modèle local froid doit d'abord se charger) |
| `--temperature <t>` | Échantillonnage, défaut **0** — épinglé pour qu'un verdict soit reproductible |
| `--out <dir>` | Racine des rapports (défaut : ce dossier) |

## Lancer plusieurs providers en parallèle

Les campagnes sont longues et indépendantes : rien n'empêche de les lancer côte à côte.
Une seule précaution — **différer l'index**. Deux campagnes qui se terminent ensemble
reconstruisent chacune le récapitulatif à partir d'un instantané pris avant que l'autre
n'ait déposé son rapport : la dernière à écrire produit un index complet mais périmé.
`--no-recap` le diffère, et on le reconstruit une fois à la fin.

```bash
export DEEPSEEK_API_KEY=…  ZAI_API_KEY=…

llmproviders-test/run-campaign.sh --provider deepseek -k DEEPSEEK_API_KEY --no-recap &
llmproviders-test/run-campaign.sh --provider zai      -k ZAI_API_KEY      --no-recap &
wait

llmproviders-test/lib/recap.sh llmproviders-test
```

L'écriture de l'index est de toute façon atomique (fichier temporaire puis renommage), donc
un oubli de `--no-recap` ne corrompt rien : il peut seulement laisser une ligne de retard,
que la reconstruction suivante rattrape.

## Les clés API

**Jamais en argument de ligne de commande** — le harnais le refuse, et le kit ne le
contourne pas. Une clé arrive par variable d'environnement, ou par le champ `apiKey` d'un
fichier de configuration que les scripts **refusent de lire** s'il n'est pas nommé
`*.local.json` ou `*.secrets.json`. Ces deux motifs sont gitignorés.

Les rapports générés sont versionnés : c'est la « sortie archivée » que LLM-08 réclame
comme niveau de preuve. Ils sont construits à partir du comportement observé, ne portent
que l'**hôte** de l'endpoint, et jamais un secret.

## Le wildcard

Un `--model` contenant `*` ou `?` déclenche
`orkeon llm models -p <provider> --filter <motif>`, qui interroge le catalogue amont. Si
l'appel échoue — endpoint absent, réseau, Azure qui n'a pas de catalogue public — le kit
retombe sur la liste `models` déclarée pour ce provider dans le JSON, filtrée par le même
motif. Le plafond `--max-models` s'applique ensuite, et **ce qui est écarté est
journalisé** : un plafond silencieux se lirait comme une couverture complète.

## Les modes du protocole

Le harnais couvre **M1 à M10, M12 et M13**. Un `➖` dans un rapport signale un mode que le
provider ne peut pas satisfaire — rien n'a été exercé, il n'y a rien à corriger.

**M2 envoie la conversation deux fois** (deux appels facturés au lieu d'un), et c'est
délibéré. Les providers OpenAI-compatibles ont deux chemins de chat, choisis sur un critère
que l'appelant ne voit pas : sans outil déclaré, la conversation est **aplatie** en un seul
tour utilisateur (`"user: …\nassistant: …"`) ; avec un outil, elle part en **vrai tableau
`messages`**. N'exercer qu'une des deux formes a laissé passer un défaut réel (D-02 : message
système perdu sur le seul chemin natif). Les deux formes rendent aussi un verdict que l'une
seule ne peut pas donner :

| Aplatie | Tableau `messages` | Lecture |
|---|---|---|
| ✅ | ✅ | Rien à signaler |
| ❌ | ✅ | **Défaut d'Orkéon** — c'est l'aplatissement qui a perdu le modèle |
| ✅ | ❌ | Inspecter la charge utile du chemin structuré, **mais sans conclure trop vite** (voir ci-dessous) |
| ❌ | ❌ | L'instruction est bien parvenue à l'API des deux façons : **le modèle ne la suit pas** |

> ⚠️ **Ce que la troisième ligne ne prouve pas.** Le chemin structuré n'est atteignable qu'en
> envoyant un schéma d'outil — c'est lui qui le sélectionne. Deux choses changent donc
> ensemble : la forme de la requête *et* la présence d'un catalogue d'outils dans le contexte,
> ce dernier suffisant à dégrader le suivi d'instruction de certains modèles. La sonde émet
> `tool_choice: "none"` pour au moins écarter le cas « le modèle a appelé l'outil au lieu de
> répondre », mais elle ne peut pas isoler le reste. Un `✅ / ❌` appelle une lecture de la
> charge utile émise, pas un correctif immédiat.
>
> **Mesuré le 2026-08-01, et instruit jusqu'au bout.** À la température par défaut du framework
> (`0.7`), trois exécutions de M2 seul sur le même `llama3.2` ont rendu ❌ ✅ ❌ — un verdict qui
> tire à pile ou face. À `temperature: 0`, les trois rendent ❌, toujours sur la forme
> structurée. L'inspection de la charge utile qu'appelle cette ligne a été faite et **épinglée
> en test** (`OllamaChatEndpointTests.ShouldCarryTheConfiguredSystemMessage_OnTheStructuredPath`) :
> Ollama construit son propre payload `/api/chat` et y prépose correctement le message système.
> L'instruction parvient donc bien au modèle, qui ne la suit pas dès qu'un catalogue d'outils
> partage son contexte. **Comportement du modèle, pas défaut d'Orkéon** — mais c'est le test qui
> le dit, pas une lecture de code faite une fois.

## Reproductibilité — la température

La sonde épingle **`temperature: 0`** sur tous les appels. C'est un correctif, pas une
préférence : le harnais héritait du défaut du framework (`0.7`) et mesurait donc la conformité
au travers d'un échantillonnage créatif — d'où le ❌ ✅ ❌ ci-dessus. Chaque rapport porte
désormais la valeur employée ; un rapport marqué `non épinglée` est antérieur à ce correctif et
son verdict doit être lu comme indicatif.

Deux limites à connaître, plutôt que de les découvrir en campagne :

- **Le déterminisme reste approché.** `LlmConfig.Seed` existe mais **aucun provider HTTP ne le
  met sur le fil** — seuls les adaptateurs Microsoft.Extensions.AI le lisent. La sonde ne le
  renseigne donc pas : poser un champ qui ne part nulle part serait exactement le *silent drop*
  que ce projet combat. À `temperature: 0` la variance devient faible, pas nulle.
- **Quelques modèles de raisonnement refusent toute température imposée** et n'acceptent que leur
  propre défaut. Pour ceux-là, `--temperature 1` — le refus est explicite côté fournisseur, pas
  silencieux.

Un mode qui reste instable à température nulle traduit un modèle assis sur une frontière de
décision. Le relancer trois fois et rapporter la tendance vaut mieux qu'archiver une passe.

**M11 (contexte long) et M14 (crew de bout en bout) restent manuels**, et c'est délibéré :

- **M11** facture une requête proche de la fenêtre annoncée — jusqu'à 1 M de tokens sur
  GPT-5.6 ou Claude. La matrice documente deux pièges qu'il doit trancher par la mesure :
  contexte natif ≠ contexte servi, et certains providers comptent entrée + sortie ensemble.
  Procédure : construire un prompt de la taille visée, appeler, vérifier l'absence de
  troncature silencieuse, consigner la valeur mesurée sur le **chemin réellement emprunté**.
- **M14** demande un crew réel avec délégation. Passer par un exemple du dossier
  `examples/` configuré sur le provider visé, et archiver sa sortie.

## Budget

Préférer le modèle le moins cher de chaque famille pour M1–M6, et réserver le haut de
gamme aux modes qui l'exigent (M7 thinking, M9 vision, M11 contexte long). Cadrer le budget
par provider **avant** de lancer : ces campagnes consomment des crédits réels sur des
comptes réels.

## Reporter dans la matrice

Chaque rapport se termine par les deux fragments prêts à coller : la ligne du journal §7 et
la ligne du tableau modèles §6. Le remplissage de la matrice (LLM-08/C4) est mécanique.
