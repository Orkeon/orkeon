// A stdio MCP server for the REPL's tests: newline-delimited JSON-RPC, one tool, echo_text, which
// returns the text it is given. A legacy server: it does not know server/discover, so the client
// falls back to initialize, as it does for most servers in the wild.
import readline from "node:readline";

const send = (message) => process.stdout.write(JSON.stringify({ jsonrpc: "2.0", ...message }) + "\n");

readline.createInterface({ input: process.stdin }).on("line", (line) => {
  if (!line.trim()) return;
  const request = JSON.parse(line);
  if (request.id === undefined) return; // a notification: nothing to answer
  switch (request.method) {
    case "initialize":
      send({
        id: request.id,
        result: {
          protocolVersion: request.params?.protocolVersion ?? "2025-06-18",
          capabilities: { tools: {} },
          serverInfo: { name: "echo", version: "1.0.0" },
        },
      });
      break;
    case "tools/list":
      send({
        id: request.id,
        result: {
          tools: [{
            name: "echo_text",
            description: "Returns the text it is given",
            inputSchema: { type: "object", properties: { text: { type: "string" } }, required: ["text"] },
          }],
        },
      });
      break;
    case "tools/call":
      send({ id: request.id, result: { content: [{ type: "text", text: String(request.params?.arguments?.text ?? "") }] } });
      break;
    default:
      send({ id: request.id, error: { code: -32601, message: `Method not found: ${request.method}` } });
  }
});
