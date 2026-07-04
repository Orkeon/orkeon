# 55. Telemedecine Augmentee Streaming

> Quatre agents assistent le medecin pendant une consultation via streaming temps reel. Le medecin (HumanAgent) garde le controle total. La memoire de session est chiffree.

## Quality

:lock: Securite -- Medecin controle tout, streaming temps reel, chiffrement session

## Architecture

- **Process**: `parallel`
- **Agents**: 5 -- Transcriber, Question Suggester, Prescription Preparer, EHR Updater, Consulting Physician (human with permanent control)
- **Tools**: `json_tool`, `http_api`, `file_write`
- **Memory**: `EncryptedRedis` (consultation session)
- **Key features**: Streaming capabilities, HumanInputContext permanent, EncryptedRedisMemoryProvider, IContextWindowManager
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/55-telemedicine-streaming/config.yaml
```

## What this example demonstrates

- Real-time streaming AI assistance during live telemedicine consultations
- Physician maintains absolute control with permanent human-in-the-loop
- Parallel AI agents providing transcription, suggestions, prescriptions, and EHR updates simultaneously
