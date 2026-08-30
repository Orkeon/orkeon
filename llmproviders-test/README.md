<!-- Généré par lib/recap.sh — n'éditez pas README.md, éditez lib/readme-header.md. -->

# Campagnes de validation des providers LLM

Ce dossier porte les **preuves d'exécution réelle** des 14 providers LLM d'Orkéon : les
scripts qui lancent les campagnes, et le rapport archivé de chacune.

> **Pourquoi ce dossier existe.** Le dépôt compte ≈ 400 tests unitaires sur les providers
> LLM, et **tous parlent à un handler HTTP mocké**. Un mock prouve qu'Orkéon envoie ce
> qu'on croit ; il ne prouve pas que le fournisseur l'accepte. C'est la seconde preuve qui
> se construit ici.
>
> **Référence du protocole.** Les modes M1–M14 et le journal des campagnes sont définis par
> `LLM-PROVIDERS-TEST-MATRIX.md` (§5 et §7) et la fiche de chantier `LLM-08`, deux documents
> du suivi interne des mainteneurs qui ne sont **pas publiés** — d'où l'absence de lien.
> Ce fichier est autosuffisant : les modes sont rappelés plus bas, et chaque rapport se
> termine par les fragments prêts à coller dans la matrice. Elle ne sert qu'à consolider,
> côté mainteneurs, ce que les rapports de ce dossier établissent déjà.

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
| `--provider <clés>` | Une clé, ou plusieurs séparées par des virgules — `openai`, `anthropic`, `ollama`, `azure`, `together`, `qwen`, `deepseek`, `kimi`, `mistral`, `huggingface`, `zai`, `gemini`, `grok`, `minimax` |
| `--all` | Tous les providers déclarés dans le JSON |
| `--parallel` | Lance les providers sélectionnés simultanément (voir plus bas) |
| `--model <id\|glob>` | Un identifiant, ou un motif (`gpt-5.6-*`, `*flash*`) |
| `--modes M1,M8` | Défaut : tous les modes du harnais |
| `--config <fichier>` | Configuration de campagne (voir `providers.schema.json`) |
| `--base-url <url>` | Surcharge d'endpoint (Azure, miroirs régionaux) |
| `--api-version <v>` | `api-version` Azure en mode deployment |
| `--workspace-id <w>` | Workspace des requêtes, pour les clés à portée de workspace — les clés identity-linked d'Anthropic l'exigent (aussi déclarable par provider : `workspaceId` dans le JSON) |
| `--api-key-env <VAR>` | Variable portant la clé |
| `--max-models <n>` | Plafond du wildcard (défaut 5 ; `providers.example.json` l'abaisse à 3) |
| `--dry-run` | Résout et affiche le plan, n'appelle rien |
| `--no-recap` | Ne reconstruit pas l'index — pour les lancements parallèles |
| `--timeout <s>` | Délai par requête, défaut **180 s** (un modèle local froid doit d'abord se charger) |
| `--temperature <t>` | Échantillonnage, défaut **0** — épinglé pour qu'un verdict soit reproductible. Les modèles qui exigent une autre valeur sont consignés **par modèle** dans `requiredParams` (catalogue) et résolus automatiquement |
| `--thinking-effort <e>` | Effort de raisonnement de base (ex. `none`) — pour les modèles qui refusent les tools en raisonnant ; consigné par modèle dans `requiredParams` (catalogue) |
| `--out <dir>` | Racine des rapports (défaut : ce dossier) |

## Lancer plusieurs providers en parallèle

Les campagnes sont longues et indépendantes. `--provider` accepte une liste et `--parallel`
les lance ensemble :

```bash
export DEEPSEEK_API_KEY=…  ZAI_API_KEY=…

llmproviders-test/run-campaign.sh --provider deepseek,zai,ollama --parallel
```

Une seule campagne, un seul index reconstruit à la fin, un seul code de sortie. Les variables
de clé sont résolues par provider depuis le catalogue, donc une liste hétérogène n'a besoin
d'aucun `-k`.

**Ce qui est parallélisé, et ce qui ne l'est pas.** Les providers tournent côte à côte ; les
modèles d'un *même* provider restent en série. Ils partagent son quota, et les faire courir
ensemble mesurerait l'étranglement plutôt que le protocole.

**La sortie reste lisible.** Chaque provider écrit dans son propre tampon, restitué à la fin
dans l'ordre où vous l'avez nommé — un run parallèle se lit exactement comme un run séquentiel.
Le prix est qu'il n'y a pas de progression fine en direct : seules les lignes `▶ … — started`
arrivent immédiatement.

**Si un processus meurt** sans rendre son compte, il est compté comme un échec. Un plantage
qui passerait pour un succès serait pire qu'un rouge.

Pour lancer plusieurs *processus* séparés — plusieurs terminaux, un ordonnanceur — la
précaution reste de **différer l'index** avec `--no-recap`, puis de le reconstruire une fois :

```bash
llmproviders-test/run-campaign.sh --provider deepseek --no-recap &
llmproviders-test/run-campaign.sh --provider zai      --no-recap &
wait
llmproviders-test/lib/recap.sh llmproviders-test
```

Sans ça, deux campagnes qui se terminent ensemble reconstruisent chacune le récapitulatif à
partir d'un instantané pris avant que l'autre n'ait déposé son rapport, et la dernière à
écrire produit un index complet mais périmé. L'écriture est atomique (fichier temporaire puis
renommage), donc un oubli ne corrompt rien : il peut seulement laisser une ligne de retard,
que la reconstruction suivante rattrape.

## Les clés API

**Jamais en argument de ligne de commande** — le harnais le refuse, et le kit ne le
contourne pas. Une clé arrive par variable d'environnement, ou par le champ `apiKey` d'un
fichier de configuration que les scripts **refusent de lire** s'il n'est pas nommé
`*.local.json` ou `*.secrets.json`. Ces deux motifs sont gitignorés.

Les rapports générés sont versionnés : c'est la « sortie archivée » que LLM-08 réclame
comme niveau de preuve. Ils sont construits à partir du comportement observé, ne portent
que l'**hôte** de l'endpoint, et jamais un secret.

Sans `--api-key-env` ni configuration, le kit lit la variable conventionnelle du SDK du
fournisseur, déclarée dans `lib/catalog.json` :

| Provider | Variable | Provider | Variable |
|---|---|---|---|
| `openai` | `OPENAI_API_KEY` | `deepseek` | `DEEPSEEK_API_KEY` |
| `anthropic` | `ANTHROPIC_API_KEY` | `kimi` · `moonshot` | `MOONSHOT_API_KEY` |
| `azure` · `azure-openai` | `AZURE_OPENAI_API_KEY` | `qwen` | `DASHSCOPE_API_KEY` |
| `mistral` | `MISTRAL_API_KEY` | `minimax` | `MINIMAX_API_KEY` |
| `together` · `togetherai` | `TOGETHER_API_KEY` | `huggingface` · `hf` | `HF_TOKEN` |
| `ollama` | *aucune* | `zai` · `glm` · `zhipu` | `ZAI_API_KEY` |
| `gemini` · `google` | `GEMINI_API_KEY` | `grok` · `xai` | `XAI_API_KEY` |

`--api-key-env` l'emporte, puis le champ `apiKeyEnv` de la configuration, puis cette
table, et enfin `ORKEON_LLM_API_KEY`. Le défaut était auparavant `ORKEON_LLM_API_KEY`
pour les treize : toute campagne lancée sans `--config` cherchait une variable que
personne n'exporte.

## Le modèle choisi pour vous

Sans `--model`, chaque fournisseur reçoit **son** modèle, pas un modèle générique. La
résolution va du plus explicite au plus général : `--model` > la liste `models` du JSON de
campagne > le `defaultModel` du catalogue.

| Fournisseur | Défaut | Modèle vision |
|---|---|---|
| `openai` | `gpt-5.6-sol` | |
| `anthropic` | `claude-sonnet-5` | |
| `azure` | *(aucun — voir plus bas)* | |
| `ollama` | `llama3.2` | `llava` |
| `together` | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | *(non-serverless seulement — voir catalogue)* |
| `deepseek` | `deepseek-v4-flash` | `deepseek-v4-flash-vision-exp` |
| `kimi` | `kimi-k2.6` | |
| `qwen` | `qwen3.7-plus` | |
| `mistral` | `mistral-medium-2604` | |
| `huggingface` | `meta-llama/Llama-3.1-8B-Instruct` | `Qwen/Qwen3-VL-30B-A3B-Instruct` |
| `zai` | `glm-5.2` | `glm-4.6v-flash` |
| `gemini` | `gemini-3.7-flash` | *(le défaut voit)* |
| `grok` | `grok-4.6` | *(le défaut voit)* |
| `minimax` | `MiniMax-M2` | *(pas de compagnon : la famille VL est documentée mais absente du `/models` de la plateforme)* |

Ces identifiants viennent des sections §6.x de la matrice, **pas des défauts compilés dans
les providers** : six d'entre eux y sont signalés retirés ou faux (G-01 à G-04, G-07, G-08).
En hériter aurait envoyé une campagne sur deux vers un modèle que l'API ne sert plus.

**Gemini est la seule exception, et pour une raison qui ne se reproduira pas** : il est
arrivé après l'audit du 2026-07-27, aucune §6.x ne le couvrait, et sa fiche §6.13 a donc été
ouverte à partir de son défaut compilé — `gemini-3.7-flash`, vérifié le 2026-08-18 contre la
documentation de compatibilité OpenAI de Google. C'est un défaut daté, pas un défaut hérité
d'une génération périmée ; la campagne reste ce qui le confirme.

**Azure n'en déclare aucun, volontairement.** Les déploiements sont propres à un compte, il
n'existe pas de catalogue portable, et en inventer un enverrait chaque campagne vers un
déploiement que personne n'a créé. Azure exige donc `--model <déploiement>` ou une entrée
dans le JSON — et le dit plutôt que d'échouer plus loin.

### La campagne vision compagnon

Quand le modèle par défaut d'un fournisseur ne voit pas mais que le fournisseur déclare un
modèle qui voit, **M9 est rejoué sur celui-ci**, en plus. C'est la moitié pratique de D-03 :
les capacités sont déclarées par fournisseur, la réalité est par modèle, donc un seul modèle
par défaut ne peut jamais répondre pour M9 chez un fournisseur dont la vue habite un autre
identifiant. Mesuré deux fois le 2026-08-02 — `glm-5.2` renvoie `1210` là où
`glm-4.6v-flash` lit l'image, `llama3.2` refuse le multimodal là où `llava` la lit.

Le modèle par défaut **garde son M9 et garde son rouge** : ce rouge *est* la preuve de D-03,
et le supprimer masquerait précisément l'écart que la matrice existe pour suivre. Le
compagnon ajoute le fait complémentaire — que le chemin multimodal d'Orkéon fonctionne — que
ni l'un ni l'autre ne donne seul.

Uniquement sur le chemin automatique : un `--model` explicite est un choix, et le
contredire dépenserait des crédits que vous n'avez pas demandé à dépenser.

## Endpoints custom

Le dossier `custom-endpoints/` archive les campagnes menées contre un endpoint
OpenAI-compatible arbitraire via le fallback (`--provider openai --base-url … --out
llmproviders-test/custom-endpoints --no-recap`) : elles valident le chemin « serveur
compatible inconnu » sans polluer l'index des fournisseurs officiels. Première preuve :
`api.x.ai` / `grok-4.6`, 12/12 le 2026-08-30.

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
- **Quelques modèles refusent toute température imposée** et n'acceptent que leur propre défaut.
  Le refus est explicite côté fournisseur, pas silencieux — mais il porte sur *chaque* appel, donc
  il ne colore pas un mode, il en abat douze. La campagne Kimi du 2026-08-03 l'a payé plein
  tarif : `kimi-k2.6` répond `invalid temperature: only 1 is allowed for this model`, dix modes sur
  douze au rouge, une seule cause. Cette page le documentait déjà — sans que rien ne l'applique.
  Depuis, **un fournisseur qui ne supporte pas `0` déclare sa température au catalogue**
  (`lib/catalog.json`, champ `temperature`), `--temperature` reste prioritaire, et l'en-tête du
  rapport porte la valeur réellement employée : un verdict Kimi n'est pas reproductible au même
  titre qu'un verdict à température nulle, et cela se lit.

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

## Campagnes exécutées

| Horodatage (UTC) | Provider | Modèle | ✅/❌/➖ | Version | Rapport |
|---|---|---|---|---|---|
| 2026-08-30T13:47:03Z | `minimax` | `MiniMax-M2` | ❌ 7/2/3 | 1.0.0-rc.2 | [rapport](minimax/2026-08-30-134626-MiniMax-M2.md) |
| 2026-08-30T13:36:55Z | `minimax` | `MiniMax-M2` | ❌ 7/2/3 | 1.0.0-rc.2 | [rapport](minimax/2026-08-30-133614-MiniMax-M2.md) |
| 2026-08-30T13:33:54Z | `minimax` | `MiniMax-M2` | ❌ 2/8/2 | 1.0.0-rc.2 | [rapport](minimax/2026-08-30-133347-MiniMax-M2.md) |
| 2026-08-30T13:32:27Z | `minimax` | `MiniMax-M2` | ❌ 2/8/2 | 1.0.0-rc.2 | [rapport](minimax/2026-08-30-133219-MiniMax-M2.md) |
| 2026-08-30T13:23:22Z | `minimax` | `MiniMax-M2` | ❌ 2/8/2 | 1.0.0-rc.2 | [rapport](minimax/2026-08-30-132313-MiniMax-M2.md) |
| 2026-08-30T12:46:33Z | `huggingface` | `Qwen/Qwen3-VL-30B-A3B-Instruct` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](huggingface/2026-08-30-124631-Qwen_Qwen3-VL-30B-A3B-Instruct.md) |
| 2026-08-30T12:46:30Z | `together` | `Qwen/Qwen3-VL-8B-Instruct` | ❌ 0/1/0 | 1.0.0-rc.2 | [rapport](together/2026-08-30-124627-Qwen_Qwen3-VL-8B-Instruct.md) |
| 2026-08-30T12:45:22Z | `huggingface` | `meta-llama/Llama-3.1-8B-Instruct` | ❌ 8/2/2 | 1.0.0-rc.2 | [rapport](huggingface/2026-08-30-124456-meta-llama_Llama-3.1-8B-Instruct.md) |
| 2026-08-30T12:45:16Z | `together` | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | ❌ 9/1/2 | 1.0.0-rc.2 | [rapport](together/2026-08-30-124456-meta-llama_Llama-3.3-70B-Instruct-Turbo.md) |
| 2026-08-30T12:04:24Z | `grok` | `grok-4.6` | ✅ 12/0/0 | 1.0.0-rc.2 | [rapport](grok/2026-08-30-120310-grok-4.6.md) |
| 2026-08-30T11:34:05Z | `qwen` | `qwen3.7-plus` | ✅ 12/0/0 | 1.0.0-rc.2 | [rapport](qwen/2026-08-30-113159-qwen3.7-plus.md) |
| 2026-08-30T11:26:05Z | `qwen` | `qwen3.7-plus` | ❌ 11/1/0 | 1.0.0-rc.2 | [rapport](qwen/2026-08-30-112358-qwen3.7-plus.md) |
| 2026-08-30T11:11:20Z | `mistral` | `mistral-medium-2604` | ❌ 11/1/0 | 1.0.0-rc.2 | [rapport](mistral/2026-08-30-111105-mistral-medium-2604.md) |
| 2026-08-30T11:04:40Z | `mistral` | `mistral-medium-2604` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](mistral/2026-08-30-110426-mistral-medium-2604.md) |
| 2026-08-30T11:01:22Z | `mistral` | `mistral-medium-2604` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](mistral/2026-08-30-110109-mistral-medium-2604.md) |
| 2026-08-30T10:56:37Z | `mistral` | `mistral-medium-2604` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](mistral/2026-08-30-105625-mistral-medium-2604.md) |
| 2026-08-30T10:31:19Z | `anthropic` | `claude-sonnet-5` | ✅ 12/0/0 | 1.0.0-rc.2 | [rapport](anthropic/2026-08-30-103048-claude-sonnet-5.md) |
| 2026-08-30T10:25:16Z | `anthropic` | `claude-sonnet-5` | ❌ 11/1/0 | 1.0.0-rc.2 | [rapport](anthropic/2026-08-30-102446-claude-sonnet-5.md) |
| 2026-08-30T10:03:33Z | `ollama` | `llava` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](ollama/2026-08-30-100320-llava.md) |
| 2026-08-30T10:03:19Z | `ollama` | `llama3.2` | ❌ 8/3/1 | 1.0.0-rc.2 | [rapport](ollama/2026-08-30-100305-llama3.2.md) |
| 2026-08-30T08:51:33Z | `openai` | `gpt-5.6-sol` | ❌ 11/1/0 | 1.0.0-rc.2 | [rapport](openai/2026-08-30-085110-gpt-5.6-sol.md) |
| 2026-08-30T08:44:28Z | `openai` | `gpt-5.6-sol` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](openai/2026-08-30-084408-gpt-5.6-sol.md) |
| 2026-08-30T08:41:30Z | `zai` | `glm-4.6v-flash` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](zai/2026-08-30-084124-glm-4.6v-flash.md) |
| 2026-08-30T08:41:23Z | `zai` | `glm-5.2` | ❌ 11/1/0 | 1.0.0-rc.2 | [rapport](zai/2026-08-30-084018-glm-5.2.md) |
| 2026-08-30T08:41:03Z | `gemini` | `gemini-3.7-flash` | ✅ 11/0/1 | 1.0.0-rc.2 | [rapport](gemini/2026-08-30-084018-gemini-3.7-flash.md) |
| 2026-08-30T08:40:44Z | `deepseek` | `deepseek-v4-flash-vision-exp` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](deepseek/2026-08-30-084042-deepseek-v4-flash-vision-exp.md) |
| 2026-08-30T08:40:41Z | `deepseek` | `deepseek-v4-flash` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](deepseek/2026-08-30-084018-deepseek-v4-flash.md) |
| 2026-08-30T08:40:28Z | `openai` | `gpt-5.6-sol` | ❌ 2/10/0 | 1.0.0-rc.2 | [rapport](openai/2026-08-30-084018-gpt-5.6-sol.md) |
| 2026-08-30T07:58:13Z | `kimi` | `kimi-k2.6` | ✅ 11/0/1 | 1.0.0-rc.2 | [rapport](kimi/2026-08-30-075629-kimi-k2.6.md) |
| 2026-08-30T07:57:39Z | `zai` | `glm-4.6v-flash` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](zai/2026-08-30-075736-glm-4.6v-flash.md) |
| 2026-08-30T07:57:35Z | `zai` | `glm-5.2` | ❌ 10/2/0 | 1.0.0-rc.2 | [rapport](zai/2026-08-30-075629-glm-5.2.md) |
| 2026-08-30T07:57:26Z | `gemini` | `gemini-3.7-flash` | ❌ 7/3/2 | 1.0.0-rc.2 | [rapport](gemini/2026-08-30-075629-gemini-3.7-flash.md) |
| 2026-08-30T07:56:51Z | `deepseek` | `deepseek-v4-flash` | ❌ 10/1/1 | 1.0.0-rc.2 | [rapport](deepseek/2026-08-30-075629-deepseek-v4-flash.md) |
| 2026-08-30T07:56:39Z | `openai` | `gpt-5.6-sol` | ❌ 2/10/0 | 1.0.0-rc.2 | [rapport](openai/2026-08-30-075629-gpt-5.6-sol.md) |
| 2026-08-30T07:24:13Z | `ollama` | `llava` | ✅ 1/0/0 | 1.0.0-rc.2 | [rapport](ollama/2026-08-30-072359-llava.md) |
| 2026-08-30T07:23:59Z | `ollama` | `llama3.2` | ❌ 8/3/1 | 1.0.0-rc.2 | [rapport](ollama/2026-08-30-072345-llama3.2.md) |
| 2026-08-03T11:42:17Z | `kimi` | `kimi-k2.6` | ❌ 11/1/0 | 0.9.2-beta | [rapport](kimi/2026-08-03-114109-kimi-k2.6.md) |
| 2026-08-03T11:18:13Z | `kimi` | `kimi-k2.6` | ✅ 11/0/1 | 0.9.2-beta | [rapport](kimi/2026-08-03-111707-kimi-k2.6.md) |
| 2026-08-03T10:42:56Z | `kimi` | `kimi-k2.6` | ❌ 2/10/0 | 0.9.2-beta | [rapport](kimi/2026-08-03-104249-kimi-k2.6.md) |
| 2026-08-02T12:56:44Z | `zai` | `glm-4.6v-flash` | ✅ 1/0/0 | 0.9.2-beta | [rapport](zai/2026-08-02-125641-glm-4.6v-flash.md) |
| 2026-08-02T12:56:40Z | `zai` | `glm-5.2` | ❌ 11/1/0 | 0.9.2-beta | [rapport](zai/2026-08-02-125548-glm-5.2.md) |
| 2026-08-02T12:56:24Z | `ollama` | `llava` | ✅ 1/0/0 | 0.9.2-beta | [rapport](ollama/2026-08-02-125613-llava.md) |
| 2026-08-02T12:56:13Z | `ollama` | `llama3.2` | ❌ 8/3/1 | 0.9.2-beta | [rapport](ollama/2026-08-02-125548-llama3.2.md) |
| 2026-08-02T12:56:08Z | `deepseek` | `deepseek-v4-flash` | ❌ 10/1/1 | 0.9.2-beta | [rapport](deepseek/2026-08-02-125548-deepseek-v4-flash.md) |

## Dernière campagne par provider

| Provider | Modèle | Date | Statut | Modes exercés |
|---|---|---|---|---|
| `anthropic` | `claude-sonnet-5` | 2026-08-30 | ✅ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `deepseek` | `deepseek-v4-flash-vision-exp` | 2026-08-30 | ✅ | M9 |
| `gemini` | `gemini-3.7-flash` | 2026-08-30 | ✅ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `grok` | `grok-4.6` | 2026-08-30 | ✅ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `huggingface` | `Qwen/Qwen3-VL-30B-A3B-Instruct` | 2026-08-30 | ✅ | M9 |
| `kimi` | `kimi-k2.6` | 2026-08-30 | ✅ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `minimax` | `MiniMax-M2` | 2026-08-30 | ❌ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `mistral` | `mistral-medium-2604` | 2026-08-30 | ❌ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `ollama` | `llava` | 2026-08-30 | ✅ | M9 |
| `openai` | `gpt-5.6-sol` | 2026-08-30 | ❌ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `qwen` | `qwen3.7-plus` | 2026-08-30 | ✅ | M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M12 M13 |
| `together` | `Qwen/Qwen3-VL-8B-Instruct` | 2026-08-30 | ❌ | M9 |
| `zai` | `glm-4.6v-flash` | 2026-08-30 | ✅ | M9 |

---

_Index régénéré par `lib/recap.sh` ou `lib/recap.ps1` depuis les rapports présents sur disque._
_44 campagne(s) archivée(s)._
