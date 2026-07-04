# 82. Curation de Newsletter Scraping

> Scraping parallele de multiples sources puis pipeline de filtrage et mise en forme. La deduplication semantique Redis elimine les contenus similaires.

## Quality

🎯 Simplicite — Scraping parallele rapide, deduplication semantique, pipeline simple

## Architecture

- **Process**: `parallel`
- **Agents**: 5 — News Collector, Blog Collector, Paper Collector, Relevance Curator, Format Editor
- **Tools**: `web_scrape`, `http_api`, `pdf_reader`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: Batch execution, vector search (deduplication), MemoryEvents, TaskPriority
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/82-newsletter-curation/config.yaml
```

## What this example demonstrates

- Parallel multi-source scraping for content aggregation
- Semantic deduplication eliminating similar content across sources
- Editorial pipeline from raw collection to formatted newsletter
