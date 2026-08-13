# Multi-file crew directory

The same crew you would write in a single `config.yaml`, split across one file per
entity. `orkeon run` accepts the **directory** as its target (Orkeon >= 0.9.2-beta):

```bash
# validate only — loads the crew, resolves the tools, never calls the LLM
orkeon run examples/crew-multifile --validate

# real run
orkeon run examples/crew-multifile --initial-context "The Orkeon multi-file layout"
```

## Layout

```
crew-multifile/
├── config.yaml          # crew settings (name, goal, process, llm, …) — no agents/tasks here
├── agents/
│   ├── researcher.yaml  # one agent per file; the file stem IS the agent id
│   └── writer.yaml
└── tasks/
    ├── collect.yaml     # one task per file; the file stem IS the task id
    └── report.yaml
```

Each entity file holds exactly what the corresponding block held in the single-file
form: `agents/researcher.yaml` is the body of the `researcher:` entry, `tasks/collect.yaml`
the body of the `collect:` entry. Cross-references keep working on file stems — a task's
`agent: researcher` resolves to `agents/researcher.yaml`, and `dependencies: [collect]`
to `tasks/collect.yaml`.

`crew.yaml` is accepted instead of `config.yaml`; when both exist `config.yaml` wins.
Every `orkeon run` option behaves the same on a directory as on a single file
(`--settings`, `-V/--var`, `--initial-context`, `--mount`, `--validate`, `--verbose`,
`--llm-log`).

## Notes and limits

- YAML anchors cannot span files — each file is preprocessed on its own (they could
  not span the flat `crew.yaml`/`agents.yaml`/`tasks.yaml` triplet either).
- A directory mixing layouts (`agents.yaml` **and** `agents/`) is rejected rather than
  resolved by a silent precedence, as is a directory that also holds a `.ork.ts`
  scripting entry point.
- The flat legacy triplet (`crew.yaml` + `agents.yaml` + `tasks.yaml`) still loads
  from a directory too.
