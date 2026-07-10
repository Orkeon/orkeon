# 16 - Interactive Q&A Session

An interactive crew that answers user questions in a conversational loop, with web search, source citations, and automatic language detection.

## Concept

This example demonstrates how to build an **interactive session** where:

1. The user types a question (in any language)
2. A crew of three agents collaborates to produce an answer:
   - **Question Analyst** — breaks down and classifies the question, detects the language
   - **Knowledge Researcher** — searches the web (`web_search`) and scrapes authoritative pages (`web_scrape`) to gather relevant, sourced information
   - **Answer Composer** — synthesizes a clear, well-structured response in the same language as the question, with inline source citations
3. The answer is displayed with a `## Sources` section listing all referenced URLs
4. The loop continues until the user types `stop`

## Features

- **Automatic language detection** — the answer is written in the same language as the question (defaults to English if uncertain)
- **Web search + scrape** — the researcher first searches for relevant pages, then scrapes the best results for detailed content
- **Source citations** — inline references `[1]`, `[2]` in the answer body, with a full sources list at the end
- **Multi-level verbosity** — `-v 0` (quiet), `-v 1` (LLM & tool exchanges), `-v 2` (full debug)

## Running

### Via the interactive runner

```bash
dotnet run --project examples/runners/interactive \
  -c examples/01-enterprise/16-interactive-qa/config.yaml \
  -s examples/appsettings/appsettings.json \
  -v 1
```

### Via the standard runner (single question)

```bash
dotnet run --project examples/runners/standard \
  -c examples/01-enterprise/16-interactive-qa/config.yaml
```

## Configuration

### API Keys

The `web_search` tool uses the [Tavily API](https://tavily.com) (free tier: 1000 requests/month). Set the API key as an environment variable:

```bash
export ORKEON_TAVILY_API_KEY="tvly-your-key-here"
```

Or in your `appsettings.json`:

```json
{
  "Secrets": {
    "TAVILY_API_KEY": "tvly-your-key-here"
  }
}
```

### Crew Settings

The crew uses three agents in a sequential pipeline. You can customize:

- **Agent behaviors** in `config.yaml` (backstory, tools, max iterations)
- **LLM settings** in `appsettings.json` (model, temperature, max tokens)
- **Stop words** via the `--stop` CLI argument (default: `stop`, `quit`, `exit`)
