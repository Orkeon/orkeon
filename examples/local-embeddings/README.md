# Local Embeddings example

Minimal console demo of `Orkeon.Tools.Embeddings.Local`: embeds three TypeScript snippets with the on-device BGE-micro-v2 model (no network, no API key) and ranks them against a query by cosine similarity.

## Run

```bash
dotnet run --project examples/local-embeddings/LocalEmbeddingsExample.csproj
```

## Expected output (truncated)

```
Loading sample files from .../examples/local-embeddings/bin/Debug/net10.0/sample-files
Provider ready: 384 dimensions (BGE-micro-v2)

Query: "session expiration check"

Results (descending similarity):
  0.8267  module-b.ts      // User session management: validates expiration and refreshes tokens. ...
  0.4302  module-a.ts      // HTTP client utility for fetching JSON from REST APIs. ...
  0.4231  module-c.ts      // CSV parser: splits a row on commas and trims each field. ...
```

`module-b.ts` ranks first — its docstring matches the query semantically. Scores depend on the model output and may shift slightly across CPU vendors.

## Network requirements

- **First build**: the `SmartComponents.LocalEmbeddings` package fetches the BGE-micro-v2 ONNX file once (~17 MB) from Hugging Face during `dotnet build` and caches it under the user's NuGet packages folder.
- **Subsequent runs**: zero network — model and vocabulary are loaded from disk.
