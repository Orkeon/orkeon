# 16 - Interactive Q&A Session

A crew that answers user questions with web search, source citations, and automatic language detection — one question per run through the stock `orkeon` CLI.

## Concept

This example demonstrates how to build an **interactive session** where:

1. The user types a question (in any language)
2. A crew of three agents collaborates to produce an answer:
   - **Question Analyst** — breaks down and classifies the question, detects the language
   - **Knowledge Researcher** — searches the web (`web_search`) and scrapes authoritative pages (`web_scrape`) to gather relevant, sourced information
   - **Answer Composer** — synthesizes a clear, well-structured response in the same language as the question, with inline source citations
3. The answer is displayed with a `## Sources` section listing all referenced URLs
4. Run it again for the next question — each invocation answers one

## Features

- **Automatic language detection** — the answer is written in the same language as the question (defaults to English if uncertain)
- **Web search + scrape** — the researcher first searches for relevant pages, then scrapes the best results for detailed content
- **Source citations** — inline references `[1]`, `[2]` in the answer body, with a full sources list at the end
- **Multi-level verbosity** — `-v 0` (quiet), `-v 1` (LLM & tool exchanges), `-v 2` (full debug)

## Running

```bash
orkeon run examples/01-enterprise/16-interactive-qa/config.yaml \
  --settings examples/appsettings/appsettings.json \
  -v 1
```

**LLM profile** — the `--settings` file above is one of the ready-made profiles in [`examples/appsettings/`](../../appsettings/README.md). Copy a `*.example` template (drop the `.example` suffix) and add your key, or point it at any other profile (OpenAI, GLM, local Docker Model Runner). First run? See [Run your first example](../../../docs/getting-started/run-your-first-example.md).

**Data** — this example does not ship sample data yet; see [docs/reference/example-data-policy.md](../../../docs/reference/example-data-policy.md).

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
