# 49. Structuration Dossiers Medicaux FHIR

> Extraction et normalisation FHIR de dossiers medicaux multi-format. Le IDocumentLoaderFactory gere automatiquement PDF, XML, JSON. Le ITextChunker decoupe les dossiers volumineux.

## Quality

:lock: Securite -- Multi-format automatique, chiffrement, audit, conformite sanitaire

## Architecture

- **Process**: `sequential`
- **Agents**: 3 -- Medical Document Extractor, FHIR Data Structurer, Clinical Coherence Verifier
- **Tools**: `pdf_reader`, `json_tool`, `xml_parser`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: IDocumentLoader + IDocumentLoaderFactory, ITextChunker, EncryptedSqliteMemoryProvider, AuditEventTypes, output validation FHIR
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/49-medical-records-fhir/config.yaml
```

## What this example demonstrates

- Multi-format medical document processing with automatic format detection
- FHIR R4 resource mapping with ICD-10, LOINC, and SNOMED CT coding
- Intelligent chunking for large medical records preserving clinical context
