> 🇫🇷 [Version française](../fr/getting-started/forge-a-team-from-a-need.md)

# Forge a team from a need

You do not want to build an agent team — you want to solve a concrete problem. The Atelier (`orkeon forge`) is the guided path between the two: you describe the problem in plain words, an assistant turns it into a team, the team is tried in a sandbox on your own example, and the result is judged **against the acceptance criteria you stated** — not against a vague "is it good?".

```
Brief ──▶ Blueprint ──▶ Render ──▶ Validate ──▶ Test ──▶ Diagnose ──▶ Verdict
              ▲                        │                                 │
              └────── refine ◀─────────┴──── (validation errors) ────────┤ (not conforming)
                                                                         ▼
                                                                   Ready ──▶ Promote
```

## Prerequisites

A configured LLM. If you have not done it yet:

```bash
orkeon init          # the 5-choice wizard (ollama, openai, custom…)
orkeon doctor        # sanity-check the installation
```

The forge refuses to open the interview without a model (`FORGE-LLM-UNAVAILABLE`) — it would only echo your words back.

## The cycle, end to end

```bash
orkeon forge "summarize my supplier's new offers every morning"
```

1. **The interview.** The assistant asks short questions, one at a time — "I don't know" is always a valid answer. It captures a structured brief: goal, inputs, expected output, constraints, **acceptance criteria** (what "done, correctly" means, in your words), and a sample input for the try.
2. **The proposal.** From the brief, the assistant plans a team: who does what, in which order, with which tools — drawn from the real tool catalogue only; it cannot invent one. The plan is rendered as ordinary crew files and validated mechanically (unknown tools, unassigned tasks, dependency cycles). Validation errors go back to the assistant for repair — twice, then they surface to you.
3. **The try.** The crew runs in a sandbox, in-process, on your sample input: writes are confined to the session's output folder, and `shell_command`/`code_interpreter` are removed from the catalogue outright.
4. **The verdict.** A judge grades the output against your acceptance criteria, one by one, and states findings and concrete suggestions. A missed *must* criterion blocks regardless of the score. If no judge can run, the verdict says so (`judge: deterministic`) — it never invents a ✔.
5. **Your call.** Interactive mode arbitrates every verdict, conforming ones included: accept (a conforming crew becomes ready; accepting a non-conforming one is your judgement on sight), re-run the trial as-is (`retry` — zero compose tokens, one budget iteration), refine (the diagnosis is fed back into the plan, verbatim), hand back an edited plan (`edit`), or stop. `--auto` arbitrates alone, within the budget.

Adoption itself is not a one-way door: `forge resume` of a **promoted** session reopens it at the arbitration (the stored verdict is re-announced), and a second `forge promote` to the **same** destination updates the folder in place — generated files regenerated, your own files preserved. Any other non-empty destination stays refused.

Everything is bounded: 3 iterations by default (`--max-iterations`), optional token and wall-time ceilings (`--max-tokens`, `--max-seconds`). An exhausted budget stops the cycle cleanly; resuming may raise it — consumption always carries over.

## The session on disk

Each cycle lives under `.orkeon/forge/<slug>/` in your working directory:

```
.orkeon/forge/supplier-watch/
├── session.json          state, status, budget — the resume point
├── brief.json            what you asked, criteria included
├── blueprint.json        the team plan (single source of both renders)
├── crew/                 the rendered crew — what actually runs
├── runs/<n>/             each try: output, metrics, verdict, deliverables
├── transcript.jsonl      the conversation
└── history.jsonl         every state transition
```

```bash
orkeon forge list                      # what is in progress, what is ready
orkeon forge resume supplier-watch     # pick up exactly where it stopped
orkeon forge "..." --dry               # generate and validate only — never execute
orkeon forge resume supplier-watch --edit --dry   # amend the plan at the pause, re-render, pause again
```

At the `--dry` pause you can amend the plan before ever trying it: `resume --edit` reads the amended blueprint from the channel, validates it in full, re-renders deterministically — zero LLM tokens, same iteration — and with `--dry` pauses again at the same boundary. This is what Studio's « Modifier » does on the Composer step's agent cards.

## Two formats, one generation

The assistant never writes YAML or TypeScript — it produces one schema-validated plan, and a deterministic renderer derives the files. `--format yaml` (default) renders the per-entity YAML layout; `--format script` renders an editable `crew.ork.ts` — the scripted equivalent of the same crew, a starting point for your own edits, not conditional logic. The script path needs esbuild; without it, a new session falls back to YAML and says so (`FORGE-ESBUILD-MISSING`).

Both renders converge on the same validator, and the try loads the crew **from the rendered files** — what was written is what runs.

## Adopt it

```bash
orkeon forge promote supplier-watch --to ~/solutions/supplier-watch \
    --schedule daily@07:30
```

The promoted folder is ordinary — nothing about it is proprietary to the forge:

- `crew/` — the team, as tried;
- one folder per deliverable root the team writes to (`output/` when its tasks declare `deliverable: /output/…`) — created empty, so the first launch has somewhere to write;
- `run.sh` / `run.cmd` — launch scripts that `cd` into the folder, carry the mounts binding those roots (`--mount "$DIR/output":/output:rw`) and have your sample inputs pre-filled (adapt them to the real run);
- `FORGE.md` — the crew's identity card: goal, acceptance criteria, verdict, generation date and version — what a colleague reads when picking up the folder;
- `schedule/` (with `--schedule`) — a Windows task XML, a systemd timer, a cron line. The install command is **displayed, never executed**: Orkeon has no scheduler, and pretending otherwise would promise supervision it cannot give.

Run it with its own launcher — `~/solutions/supplier-watch/run.sh` — or point Orkeon Studio at the folder, which detects it. A bare `orkeon run ~/solutions/supplier-watch/crew` also launches it, but without the `--mount` arguments the launcher carries: the team then has no `/output` and writes nothing.

## In Orkeon Studio

The same engine drives the **Create a team** wizard of Orkeon Studio (Windows): four steps — Décrire ▸ Composer ▸ Essayer ▸ Adopter — where the stepper follows the engine's milestones, the proposal and the ✔/✘ checklist are its events rendered in cards, and adoption promotes straight into the teams directory. Studio runs `orkeon forge --events jsonl` as a child process and never touches the LLM itself; every capability of the screen is a projection of the same event stream the terminal renders. See [Studio](../architecture/studio.md).

## Honest limits

- The quality of the proposal tracks the model you configured; the brief's criteria and the mechanical validation are the guardrails, not a substitute.
- The sandbox restricts by **removing tools from the catalogue**, not by hoping the model abstains; network tools reach the plan only if your brief asked for them.
- One session, one problem: the Atelier generates and validates a team — it is not a visual crew editor.
