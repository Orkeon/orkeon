# Exécuter Orkeon sur des modèles locaux

> **Voir aussi** : [Trois façons d'exécuter Orkeon (EN)](../../getting-started/three-ways-to-run-orkeon.md) · [Profils de settings (EN)](../../../examples/appsettings/README.md) · [Retour à l'index](../INDEX.md)

Chaque exemple et chaque crew Orkeon peut tourner sur un modèle installé sur
votre machine — sans clé API, sans cloud. Trois approches, du zéro-install au
tout-embarqué :

| Option | Où tourne le modèle | Idéal pour |
|---|---|---|
| **Docker Model Runner** (DMR) | sur l'hôte, port `12434` | utilisateurs Docker Desktop — les settings par défaut des conteneurs pointent déjà dessus |
| **Ollama** | sur l'hôte, port `11434` | installations Ollama existantes |
| **Embarqué** (`--target local-llm`) | dans l'image `orkeon-runners` | zéro configuration hôte, démos hors-ligne |

L'image conteneur `orkeon-runners` utilise l'option 1 par défaut : ses settings
embarqués ciblent `host.docker.internal:12434` avec le modèle
`ai/granite-4.0-h-tiny`.

## Option 1 — Docker Model Runner (recommandé avec Docker Desktop)

```bash
# 1. Télécharger un modèle du catalogue Docker (une fois, sur l'HÔTE) :
docker model pull ai/granite-4.0-h-tiny

# 2. Vérifier qu'il est complètement téléchargé — un pull interrompu ne laisse RIEN :
docker model list

# 3. Lancer les exemples — les settings par défaut fonctionnent tels quels :
docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners
# puis dans le shell :
orkeon-example run 1
```

**Pièges rencontrés (pour vous les épargner) :**

- **Tags du catalogue** : `docker model pull` résout dans le catalogue Docker
  Hub `ai/`. Un tag absent du catalogue échoue avec
  `404 Not Found: Model not found` — p. ex. `ai/gemma4` existe, un
  `ai/gemma4:128K` inventé non. Vérifiez sur https://hub.docker.com/u/ai ou
  avec `docker model list`.
- **Pulls interrompus** : un `docker model pull` annulé ne laisse rien, et
  `docker model configure` sur un modèle absent **échoue silencieusement**.
  Confirmez toujours avec `docker model list` avant de configurer.
- **Identifiants de modèles** : DMR annonce les modèles sous leur identifiant
  complet (`docker.io/ai/granite-4.0-h-tiny:latest`). Les settings Orkeon
  peuvent utiliser la forme courte (`ai/granite-4.0-h-tiny`) — le préflight du
  conteneur matche par sous-chaîne et le serveur accepte les deux.

### Changer de modèle

```bash
# lancer n'importe quel modèle téléchargé sans toucher aux fichiers de settings :
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_Llm__Model=gemma4 \
  ghcr.io/orkeon/orkeon-runners
```

Les variables d'environnement `ORKEON_Llm__*` écrasent tous les fichiers de
settings (ce sont des overrides de configuration .NET), donc
`-e ORKEON_Llm__Model=…` se combine avec n'importe quel profil.
`docker model list` sur l'hôte montre les noms utilisables.

### Fenêtres de contexte plus grandes

DMR sert chaque modèle avec sa taille de contexte par défaut. L'augmenter
(p. ex. à 128K pour `gemma4`) est une configuration runtime par modèle — mais
l'outillage autour est trompeusement muet : suivez la procédure ci-dessous et
**vérifiez toujours avec `configure show`**, jamais avec `docker model list` ni
`inspect` (tous deux ne montrent que les métadonnées de *packaging* ; la
colonne `CONTEXT` reste vide même quand la config runtime est appliquée, et
`configure` lui-même n'affiche rien, succès comme échec).

**Étape 1 — tenter la configuration directe :**

```bash
docker model configure --context-size 131072 gemma4
docker model configure show gemma4
```

Si `configure show` affiche le réglage, c'est terminé :

```json
[
  {
    "Backend": "llama.cpp",
    "Model": "docker.io/ai/gemma4:latest",
    "Mode": "completion",
    "Config": { "context-size": 131072 }
  }
]
```

**Étape 2 — si `configure show` n'affiche rien (ou échoue) : passer par
`docker model tag`.** Le tag duplique instantanément un modèle installé (sans
re-téléchargement) et fournit une référence à laquelle la configuration
s'accroche de façon fiable. Déroulé complet avec `gemma4` :

```bash
# 1. le modèle doit être ENTIÈREMENT téléchargé — un pull interrompu ne laisse
#    rien, et configure/tag sont silencieux ou échouent sur un modèle absent
docker model list                     # gemma4 doit apparaître avec sa taille

# 2. le dupliquer sous un alias dédié grand-contexte
docker model tag gemma4 gemma4:128K
#    -> Model "gemma4" tagged successfully with "gemma4:128K"

# 3. configurer l'alias
docker model configure --context-size 131072 gemma4:128K

# 4. VÉRIFIER — la seule commande qui montre la config runtime
docker model configure show gemma4:128K
#    -> "Config": { "context-size": 131072 }
```

Puis lancez vos crews avec le nom du modèle :

```bash
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_Llm__Model=gemma4 \
  -v "$PWD/out:/output" ghcr.io/orkeon/orkeon-runners
```

Note (observé sur Docker Desktop, juillet 2026) : `configure show` rapporte la
configuration sur la référence canonique (`docker.io/ai/gemma4:latest`) — elle
est indexée par l'**ID** du modèle, que les deux tags partagent, donc le
réglage s'applique à tous les tags du même modèle ;
`-e ORKEON_Llm__Model=gemma4` en bénéficie.

**Étape 3 — vérité terrain (optionnelle mais définitive) :** demander à
llama.cpp lui-même. Déclenchez une première requête (chargement à la demande),
puis lisez le `n_ctx` servi :

```bash
curl -s -X POST http://host.docker.internal:12434/engines/llama.cpp/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4","messages":[{"role":"user","content":"hi"}],"max_tokens":1}' > /dev/null
curl -s http://host.docker.internal:12434/engines/llama.cpp/v1/models | grep -o '"n_ctx[^,]*'
```

(depuis un conteneur ; sur l'hôte utilisez `localhost:12434`.)

Si votre version de Docker Desktop n'a pas ces commandes du tout, passez par
l'UI Desktop (Models → modèle → réglages) ou par la **variante embarquée** avec
`--build-arg LOCAL_MODEL_CTX=131072` (option 3 ci-dessous), où la taille de
contexte est entièrement sous votre contrôle.

Deux notions à ne pas confondre :

- La **taille de contexte** est une propriété côté serveur (ce que le modèle
  peut lire) ; un cache KV à 128K coûte plusieurs Go de RAM en plus sur l'hôte.
- `Llm.MaxTokens` dans les settings Orkeon plafonne uniquement la **réponse** —
  indépendant de la taille de contexte.

### Concurrence : gardez `MaxConcurrentRequests` à 1

Un serveur d'inférence local sature la machine avec **une seule** requête ;
deux agents en parallèle doublent la mémoire du cache KV et font thrasher le
CPU/GPU au lieu d'accélérer quoi que ce soit. Tous les profils locaux livrés
par Orkeon épinglent donc le rate limiter :

```json
"RateLimiting": { "MaxConcurrentRequests": 1 }
```

Si vous écrivez votre propre fichier de settings, conservez ce bloc —
**absent ou à `0`, la concurrence est ILLIMITÉE** (le limiteur ne s'active que
pour les valeurs `> 0`), et les crews parallèles ou consensuels ouvriront
joyeusement une connexion par agent. Ne l'augmentez que pour les fournisseurs
cloud (les templates cloud livrés utilisent 2-16).

## Option 2 — Ollama sur l'hôte

```bash
ollama pull llama3.2          # sur l'hôte
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_LLM_PROFILE=host-ollama \
  ghcr.io/orkeon/orkeon-runners
```

Le profil `host-ollama` cible `http://host.docker.internal:11434` (le port
`11434` route vers le provider Ollama natif d'Orkeon). Changez de modèle avec
`-e ORKEON_Llm__Model=<nom>` si vous avez téléchargé autre chose.

Sur un moteur Linux nu (sans Docker Desktop), `host.docker.internal` n'existe
pas — ajoutez `--add-host=host.docker.internal:host-gateway` au `docker run`.

## Option 3 — Embarquer le modèle dans l'image

Construisez une variante qui ne demande aucun serveur côté hôte : le
`llama-server` de llama.cpp et un GGUF sont embarqués et servis dans le
conteneur avec la même forme d'URL que DMR — zéro changement de settings. Les
recettes vivent au stage `local-llm` de `Dockerfile.runners` :

```bash
# Granite 4.0 h-tiny (Apache 2.0, ~4,2 Go de poids → image ~6 Go)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/ibm-granite/granite-4.0-h-tiny-GGUF/resolve/main/granite-4.0-h-tiny-Q4_K_M.gguf \
  -t orkeon-runners:granite .

# Gemma 4 E4B avec un contexte 128K (licence Gemma — gardez l'image locale)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF/resolve/main/gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf \
  --build-arg LOCAL_MODEL_NAME=ai/gemma4 \
  --build-arg LOCAL_MODEL_CTX=131072 \
  -t orkeon-runners:gemma4 .

docker run -it --rm -m 8g -e ORKEON_RUNNER=shell orkeon-runners:granite
```

- `LOCAL_MODEL_CTX` bake la taille de contexte par défaut (8192 sinon) ;
  surchargeable au run avec `-e ORKEON_LOCAL_LLM_CTX=…`.
- `-e ORKEON_LOCAL_LLM=0` démarre le conteneur sans le serveur embarqué.
- Inférence CPU : comptez ~5-15 tokens/s ; donnez de la mémoire au conteneur
  (`-m 8g`, plus à grand contexte — sur WSL2, augmentez `.wslconfig`).
- Ces variantes sont à construire soi-même **par conception** : aucun tag
  pré-construit n'est publié, les obligations de licence des poids (notamment
  Gemma) restent de votre côté.

## Dépannage

| Symptôme | Cause | Correctif |
|---|---|---|
| `docker model pull …: 404 Not Found` | tag absent du catalogue `ai/` | vérifier le catalogue / `docker model list` ; `ai/gemma4` existe, pas les tags inventés |
| `orkeon-example: cannot reach the LLM endpoint` | pas de serveur sur l'hôte / moteur Linux | démarrer DMR ou Ollama ; sur Linux ajouter `--add-host=host.docker.internal:host-gateway` |
| `endpoint … is up, but it does not serve the configured model` | modèle non téléchargé (ou nom différent) | le message imprime le `docker model pull` exact et les modèles réellement servis |
| `LLM API Error (NotFound): model not found` en plein crew | préflight sauté (`ORKEON_SKIP_PREFLIGHT=1` ou `orkeon run` direct) | mêmes correctifs que ci-dessus |
| `docker model configure` semble ne rien faire | configure est toujours silencieux ; le modèle peut être absent, ou votre version de Desktop peut ignorer le flag | `docker model list` d'abord ; puis vérifier avec le `n_ctx` servi (voir plus haut) — pas avec la colonne `CONTEXT` |
| le crew « réussit » mais invente un contenu sans rapport avec les documents fournis | le `data/` de l'exemple n'était pas monté | utiliser `orkeon-example run` (le monte automatiquement) plutôt qu'un `orkeon run` nu |
| `Access denied … Mounts granting Write: /output` | le modèle a écrit hors de `/output` | ce message lui permet de se corriger à l'itération suivante ; seul `/output` est inscriptible |
| très lent / OOM à 128K de contexte | mémoire du cache KV | réduire le contexte, ou augmenter la mémoire conteneur/VM |
