// TypeScript twin of crew.yaml — same crew, same agents, same response_format
// guarantee. Drop this file into the standard scripted-crew runner; the
// adapter materialises identical AgentConfiguration / TaskConfiguration
// records as the YAML loader.

const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice extractor")
    .goal("Read a pasted invoice and emit one JSON object with the fields.")
    .backstory("Tireless accountant who never adds prose.")
    .llm({ provider: "deepseek", model: "deepseek-chat" })
    .withResponseFormat("json_object")
    .build();

const extract = taskBuilder()
    .name("extract_invoice")
    .description(
        "You will be handed the raw text of an invoice. " +
        "Reply with a single JSON object using the fields: vendor_name, invoice_number, " +
        "issue_date, due_date, currency, total_amount, line_items[] (each with " +
        "description, quantity, unit_price). Numbers stay as JSON numbers, dates as " +
        "YYYY-MM-DD strings. No prose, no markdown — json only."
    )
    .expectedOutput("JSON object with all invoice fields.")
    .agent(extractor)
    .withResponseFormat("json_object")   // surgical: also enforces on the task
    .build();

crewBuilder()
    .name("invoice-extractor")
    .goal("Extract invoice fields and return them as a single JSON object")
    .process("sequential")
    .withAgent(extractor)
    .withTask(extract)
    .build();
