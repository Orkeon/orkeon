> 🇫🇷 [Version française](../fr/guides/whisper-mcp.md)

# Transcribe audio with Whisper through an MCP server

> **See also**: [MCP integration](../architecture/mcp.md) · [`orkeon-repl`](../reference/cli.md#orkeon-repl--the-separate-interactive-console) · [Write a crew in TypeScript](./write-a-crew-in-typescript.md) · [Back to the index](../INDEX.md)

Orkeon ships no speech-to-text tool, and `orkeon-repl` does not capture a microphone. The way
in is an MCP server: `orkeon-repl` connects the servers its settings declare under
`MCP:Servers` before it loads its commands, and the tools they expose enter the tool registry
under their own names. A crew agent lists such a tool by name, a `*.cmd.ts` command reaches it
through its `tools` namespace, and `ctx.llm.act` hands it to the model — see
[MCP integration](../architecture/mcp.md#activation). `orkeon run` and `orkeon-host` read the
same section, so the same settings give them the same tool.

This guide wires a local Whisper ([faster-whisper](https://github.com/SYSTRAN/faster-whisper))
behind one tool, `transcribe_audio`: the settings, a minimal server, how an agent calls it, and
the Orkeon limits the server is written around.

```
agent ── .tools(["transcribe_audio"]) ──► tool registry ──► MCP client (stdio)
                                                               │
                                       whisper_mcp.py ◄────────┘
                                       reads   <audio folder>/meeting.m4a   (= /audio/meeting.m4a)
                                       writes  <transcripts folder>/meeting.txt (= /transcripts/meeting.txt)
agent ── file_read("/transcripts/meeting.txt") ──► the full transcript
```

## 1. The server

The server is yours: Orkeon does not ship it, and its CI does not run it. It needs Python 3.9
or later, the `mcp` package (its `FastMCP` class) and `faster-whisper`;
[uv](https://docs.astral.sh/uv/) fetches both on the first launch.

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

`language` is a plain string with an empty default rather than an optional one: the MCP client
turns each property of the tool's input schema into an Orkeon parameter by its `type`, and a
plain `string` keeps the schema the agent reads simple. An exception the tool raises reaches the
agent as a failed call carrying its message.

## 2. The settings

Add the two sections to the settings file `orkeon-repl` already reads — the global file
`orkeon init` writes, or the one `--settings` names (a file named on the command line replaces
the global one):

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

- **The two folders appear twice, and must match**: as the physical side of a mount, and in
  `Env`. The agents know `/audio` and `/transcripts`; the server knows the folders.
- **`Command` is a program, not a shell line**: the client starts `uv` with `Args` as its
  arguments, found on the `PATH` of the process that starts it.
- **`Env` is added to the environment the server inherits.** A key an API-based server needs
  (`OPENAI_API_KEY`) is exported in the shell that starts the REPL, never written in the
  settings file.
- `WHISPER_MODEL` takes a faster-whisper model name (`tiny`, `base`, `small`, `medium`,
  `large-v3`, …); `WHISPER_DEVICE=cuda` with `WHISPER_COMPUTE_TYPE=float16` runs it on a GPU.

Start the REPL as usual. A server that cannot be connected costs one error line naming it, on
the log and on stderr, and the REPL starts without it; a server that stops before answering adds
its exit code and the last line it wrote on stderr.

## 3. Calling it

In a `.ork.ts` crew, the agent lists the tool beside `file_read`, which reads a long transcript:

```typescript
const secretary = agentBuilder()
    .name("secretary")
    .role("Meeting secretary")
    .goal("Turn a recorded meeting into minutes: decisions, owners, open questions")
    .backstory("Transcribes first, then writes only what the transcript says.")
    .tools(["transcribe_audio", "file_read"])
    .build();
```

The same names go in a YAML crew (`tools: [transcribe_audio, file_read]`). Under `StrictTools`
— the runners' default — a crew naming `transcribe_audio` while the server is not connected fails
at load with the ordinary `unknown tool(s)` line, which lists the tools that are available.

## Why the server is written this way

| Orkeon behaviour | Where | What the server does about it |
|---|---|---|
| The server is a separate process: it reads the disk, not the VFS. The agents only know virtual paths, and the client passes the arguments unchanged. | `McpToolAdapter.CallAsync` | It maps `/audio/…` onto its own folder and refuses a path that leaves it. The mount's `ro` binds the agents, not the server: the server reads and writes what its operating-system user can. |
| Only the `text` blocks of a result reach the agent. | `McpToolAdapter.CallAsync` | It returns text. |
| A tool result is cut at 4000 characters; `file_read` gets 32,000. | `AgentDefaults.MaxToolResultLength`, `MaxToolResultLengthOverrides` | It writes the whole transcript to a file and returns its virtual path when the text is longer than `INLINE_LIMIT`. |
| The handshake must answer within 30 s at startup. | the runners' MCP startup step | It loads the model on the first call. |
| A tool name the registry already holds is refused; the earlier tool keeps it. | `McpToolProvider` | It names its tool `transcribe_audio`, no built-in name. |
| The legacy handshake asks for `2025-11-25` and refuses a server that answers `2025-03-26`. | `McpClient.InitializeAsync` | It needs an `mcp` package recent enough to answer `2025-06-18` or later; `uv run --with mcp` takes the latest. |
| The HTTP transport sends no `Authorization` header. | `SseMcpTransport` | It runs over stdio. |

A transcript is data from outside: it crosses the result sanitizer like any tool result
(`Security:ToolResults`), so a recording that says "ignore previous instructions" is screened
the same way a web page is — heuristically, see [Security](../architecture/security.md).

## Variants

- **The OpenAI transcription API instead of a local model**: replace `model().transcribe(...)`
  with a call of the `openai` package (`client.audio.transcriptions.create`), add it to `Args`
  (`--with openai`), and export `OPENAI_API_KEY` before starting the REPL. The audio file leaves
  the machine.
- **A plugin instead of an MCP server**: a C# tool loaded from the `Plugins` section
  ([Plugins](../architecture/plugins.md)) reads the audio through `IFileSystemService`, in
  virtual paths and under the mount's rights, so the path mapping above disappears; it runs with
  the REPL's own rights, and it is C# to write and build.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `… the MCP server exited with code 1; its last line on stderr: KeyError: 'WHISPER_AUDIO_DIR'` | `Env` lacks a variable the server reads. |
| The server's error line says `An error occurred trying to start process 'uv' …` | `uv` is not on the `PATH` of the REPL's process: give `Command` its full path. |
| `MCP server selected protocol version '2025-03-26', which this client does not support` | An old `mcp` package. Update it, or let `uv run --with mcp` fetch the latest. |
| `Crew configuration references unknown tool(s): transcribe_audio` | The server was not connected: read the error line above it in the log. |
| The first call takes minutes | faster-whisper downloads the model on its first use (several hundred MB for `small`), then reads it from its cache. On a metered connection, fetch it once beforehand. |
| `no audio file at /audio/…` | The file is not in `WHISPER_AUDIO_DIR`, or the mount and `Env` name different folders. |
