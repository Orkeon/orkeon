> 🇬🇧 [English version](../../guides/whisper-mcp.md)

# Transcrire de l'audio avec Whisper par un serveur MCP

> **Voir aussi** : [Intégration MCP](../architecture/mcp.md) · [`orkeon-repl`](../reference/cli.md#orkeon-repl--la-console-interactive-séparée) · [Écrire une crew en TypeScript](./write-a-crew-in-typescript.md) · [Retour à l'index](../INDEX.md)

Orkeon ne livre aucun outil de transcription, et `orkeon-repl` ne capte pas de micro. L'entrée
est un serveur MCP : `orkeon-repl` connecte les serveurs que ses réglages déclarent sous
`MCP:Servers` avant de charger ses commandes, et les outils qu'ils exposent entrent dans le
registre d'outils sous leur propre nom. Un agent de crew liste un tel outil par son nom, une
commande `*.cmd.ts` l'atteint par son espace de noms `tools`, et `ctx.llm.act` le donne au modèle
— voir [Intégration MCP](../architecture/mcp.md#activation). `orkeon run` et `orkeon-host` lisent
la même section : les mêmes réglages leur donnent le même outil.

Ce guide branche un Whisper local ([faster-whisper](https://github.com/SYSTRAN/faster-whisper))
derrière un outil, `transcribe_audio` : les réglages, un serveur minimal, la façon dont un agent
l'appelle, et les limites d'Orkeon autour desquelles le serveur est écrit.

```
agent ── .tools(["transcribe_audio"]) ──► registre d'outils ──► client MCP (stdio)
                                                                   │
                                       whisper_mcp.py ◄────────────┘
                                       lit     <dossier audio>/meeting.m4a        (= /audio/meeting.m4a)
                                       écrit   <dossier transcriptions>/meeting.txt (= /transcripts/meeting.txt)
agent ── file_read("/transcripts/meeting.txt") ──► la transcription entière
```

## 1. Le serveur

Le serveur est le vôtre : Orkeon ne le livre pas, et sa CI ne l'exécute pas. Il demande Python 3.9
ou plus, le paquet `mcp` (sa classe `FastMCP`) et `faster-whisper` ;
[uv](https://docs.astral.sh/uv/) récupère les deux au premier lancement.

```python
# whisper_mcp.py - an MCP server exposing one tool, transcribe_audio, over stdio.
import os
from pathlib import Path

from mcp.server.fastmcp import FastMCP

AUDIO_DIR = Path(os.environ["WHISPER_AUDIO_DIR"]).resolve()  # the folder Orkeon mounts as /audio
OUT_DIR = Path(os.environ["WHISPER_OUT_DIR"]).resolve()      # the folder Orkeon mounts as /transcripts
INLINE_LIMIT = 3500                                          # under Orkeon's 4000-character cap

mcp = FastMCP("whisper")
_model = None


def model():
    # Loaded on the first call, never at startup: Orkeon waits 30 s for the handshake.
    global _model
    if _model is None:
        from faster_whisper import WhisperModel
        _model = WhisperModel(
            os.environ.get("WHISPER_MODEL", "small"),
            device=os.environ.get("WHISPER_DEVICE", "cpu"),
            compute_type=os.environ.get("WHISPER_COMPUTE_TYPE", "int8"))
    return _model


@mcp.tool()
def transcribe_audio(path: str, language: str = "") -> str:
    """Transcribe an audio file under /audio (an Orkeon virtual path, e.g. /audio/meeting.m4a).
    language: an ISO code such as 'fr' or 'en'; empty to detect it."""
    if not path.startswith("/audio/"):
        raise ValueError("path must be under /audio/")
    source = (AUDIO_DIR / path.removeprefix("/audio/")).resolve()
    if not source.is_relative_to(AUDIO_DIR) or not source.is_file():
        raise ValueError(f"no audio file at {path}")

    segments, info = model().transcribe(str(source), language=language or None)
    text = " ".join(segment.text.strip() for segment in segments)

    target = OUT_DIR / source.relative_to(AUDIO_DIR).with_suffix(".txt")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8")
    virtual = "/transcripts/" + target.relative_to(OUT_DIR).as_posix()

    if len(text) <= INLINE_LIMIT:
        return f"{text}\n\n(also written to {virtual})"
    return (f"Transcript of {len(text)} characters (language {info.language}) written to {virtual}; "
            f"read it with file_read. It begins: {text[:500]}")


if __name__ == "__main__":
    mcp.run()  # stdio
```

`language` est une simple chaîne à défaut vide plutôt qu'une chaîne optionnelle : le client MCP
convertit chaque propriété du schéma d'entrée de l'outil en paramètre Orkeon d'après son `type`, et
un `string` simple garde lisible le schéma que l'agent reçoit. Une exception levée par l'outil
parvient à l'agent comme un appel échoué portant son message.

## 2. Les réglages

Ajoutez les deux sections au fichier de réglages que `orkeon-repl` lit déjà — le fichier global
qu'écrit `orkeon init`, ou celui que nomme `--settings` (un fichier nommé sur la ligne de commande
remplace le global) :

```json
{
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "/home/me/audio:/audio:ro",
        "/home/me/transcripts:/transcripts:ro"
      ]
    }
  },
  "MCP": {
    "Servers": {
      "whisper": {
        "Transport": "Stdio",
        "Command": "uv",
        "Args": [ "run", "--with", "mcp", "--with", "faster-whisper", "/home/me/mcp/whisper_mcp.py" ],
        "Env": {
          "WHISPER_AUDIO_DIR": "/home/me/audio",
          "WHISPER_OUT_DIR": "/home/me/transcripts",
          "WHISPER_MODEL": "small"
        }
      }
    }
  }
}
```

- **Les deux dossiers apparaissent deux fois, et doivent concorder** : côté physique d'un montage,
  et dans `Env`. Les agents connaissent `/audio` et `/transcripts` ; le serveur connaît les dossiers.
- **`Command` est un programme, pas une ligne de shell** : le client lance `uv` avec `Args` pour
  arguments, trouvé dans le `PATH` du processus qui le lance.
- **`Env` s'ajoute à l'environnement dont le serveur hérite.** Une clé dont un serveur à API a
  besoin (`OPENAI_API_KEY`) est exportée dans le shell qui lance le REPL, jamais écrite dans le
  fichier de réglages.
- `WHISPER_MODEL` prend un nom de modèle faster-whisper (`tiny`, `base`, `small`, `medium`,
  `large-v3`, …) ; `WHISPER_DEVICE=cuda` avec `WHISPER_COMPUTE_TYPE=float16` le fait tourner sur GPU.

Lancez le REPL comme d'habitude. Un serveur qui ne peut être connecté coûte une ligne d'erreur qui
le nomme, dans le journal et sur stderr, et le REPL démarre sans lui ; un serveur qui s'arrête avant
de répondre y ajoute son code de sortie et la dernière ligne qu'il a écrite sur stderr.

## 3. L'appeler

Dans une crew `.ork.ts`, l'agent liste l'outil à côté de `file_read`, qui lit une transcription
longue :

```typescript
const secretary = agentBuilder()
    .name("secretary")
    .role("Meeting secretary")
    .goal("Turn a recorded meeting into minutes: decisions, owners, open questions")
    .backstory("Transcribes first, then writes only what the transcript says.")
    .tools(["transcribe_audio", "file_read"])
    .build();
```

Les mêmes noms vont dans une crew YAML (`tools: [transcribe_audio, file_read]`). Sous
`StrictTools` — le défaut des runners — une crew qui nomme `transcribe_audio` alors que le serveur
n'est pas connecté échoue au chargement avec la ligne ordinaire `unknown tool(s)`, qui liste les
outils disponibles.

## Pourquoi le serveur est écrit ainsi

| Comportement d'Orkeon | Où | Ce qu'en fait le serveur |
|---|---|---|
| Le serveur est un processus à part : il lit le disque, pas le VFS. Les agents ne connaissent que des chemins virtuels, et le client transmet les arguments tels quels. | `McpToolAdapter.CallAsync` | Il fait correspondre `/audio/…` à son propre dossier et refuse un chemin qui en sort. Le `ro` du montage lie les agents, pas le serveur : le serveur lit et écrit ce que peut son utilisateur système. |
| Seuls les blocs `text` d'un résultat parviennent à l'agent. | `McpToolAdapter.CallAsync` | Il renvoie du texte. |
| Un résultat d'outil est coupé à 4000 caractères ; `file_read` a droit à 32 000. | `AgentDefaults.MaxToolResultLength`, `MaxToolResultLengthOverrides` | Il écrit la transcription entière dans un fichier et renvoie son chemin virtuel quand le texte dépasse `INLINE_LIMIT`. |
| Le handshake doit répondre en 30 s au démarrage. | le pas de démarrage MCP des runners | Il charge le modèle au premier appel. |
| Un nom d'outil que le registre détient déjà est refusé ; l'outil antérieur le garde. | `McpToolProvider` | Il nomme son outil `transcribe_audio`, aucun nom d'outil intégré. |
| Le handshake legacy demande `2025-11-25` et refuse un serveur qui répond `2025-03-26`. | `McpClient.InitializeAsync` | Il demande un paquet `mcp` assez récent pour répondre `2025-06-18` ou plus ; `uv run --with mcp` prend le dernier. |
| Le transport HTTP n'envoie pas d'en-tête `Authorization`. | `SseMcpTransport` | Il passe par stdio. |

Une transcription est une donnée venue de l'extérieur : elle traverse l'assainisseur de résultats
comme tout résultat d'outil (`Security:ToolResults`), de sorte qu'un enregistrement qui dit
« ignore previous instructions » est filtré comme une page web — de façon heuristique, voir
[Sécurité](../architecture/security.md).

## Variantes

- **L'API de transcription d'OpenAI au lieu d'un modèle local** : remplacez
  `model().transcribe(...)` par un appel du paquet `openai` (`client.audio.transcriptions.create`),
  ajoutez-le à `Args` (`--with openai`), et exportez `OPENAI_API_KEY` avant de lancer le REPL. Le
  fichier audio quitte la machine.
- **Un plugin au lieu d'un serveur MCP** : un outil C# chargé depuis la section `Plugins`
  ([Plugins](../architecture/plugins.md)) lit l'audio par `IFileSystemService`, en chemins virtuels
  et sous les droits du montage, ce qui supprime la correspondance de chemins ci-dessus ; il tourne
  avec les droits du REPL, et c'est du C# à écrire et compiler.

## Dépannage

| Symptôme | Cause |
|---|---|
| `… the MCP server exited with code 1; its last line on stderr: KeyError: 'WHISPER_AUDIO_DIR'` | `Env` n'a pas une variable que lit le serveur. |
| La ligne d'erreur du serveur dit `An error occurred trying to start process 'uv' …` | `uv` n'est pas dans le `PATH` du processus du REPL : donnez à `Command` son chemin complet. |
| `MCP server selected protocol version '2025-03-26', which this client does not support` | Un paquet `mcp` ancien. Mettez-le à jour, ou laissez `uv run --with mcp` prendre le dernier. |
| `Crew configuration references unknown tool(s): transcribe_audio` | Le serveur n'a pas été connecté : lisez la ligne d'erreur qui la précède dans le journal. |
| Le premier appel prend des minutes | faster-whisper télécharge le modèle à sa première utilisation (plusieurs centaines de Mo pour `small`), puis le lit dans son cache. Sur une connexion limitée, récupérez-le une fois à l'avance. |
| `no audio file at /audio/…` | Le fichier n'est pas dans `WHISPER_AUDIO_DIR`, ou le montage et `Env` nomment des dossiers différents. |
