> 🇫🇷 [Version française](../fr/getting-started/forge-a-team-from-a-need.md)

# Forge a team from a need

You do not want to build an agent team — you want to solve a concrete problem. The Atelier (`orkeon forge`) is the guided path between the two: you describe the problem in plain words, an assistant turns it into a team, the team is tried in a sandbox on your own example, and the result is judged **against the acceptance criteria you stated** — not against a vague "is it good?".

```
Brief ──▶ Blueprint ──▶ Render ──▶ Validate ──▶ Test ──▶ Diagnose ──▶ Verdict
              ▲                        │          │                      │
              └────── refine ◀─────────┴──── (validation errors) ───────┤ (not conforming)
                                                  │                      ▼
                                                  └─────────────▶ Ready ──▶ Promote
                                                   (adopt without trying)
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

1. **The interview.** The assistant asks only what your need leaves unsaid — what comes in, what comes out and in which shape — one short question at a time, and never how often the work runs (scheduling is settled at promotion). A need that already says it all goes through with no question at all; "I don't know" is always a valid answer. It captures a structured brief: goal, inputs, expected output, constraints, **acceptance criteria** (what "done, correctly" means, in your words), and a sample input for the try.
2. **The folders.** Between the brief and the plan, the forge proposes the team's folders: the ones your need names — « …PDFs in `/inpdf`, the Markdown in `/outmd` » gives `/inpdf` read and `/outmd` written — or, when it names none, `/workspace` to read (only when something comes in) and `/output` to write. The terminal takes the proposal as it is and prints it; a client that drives the forge can show it as a step where you rename a folder, bind it to a real one or keep it inside the team, then confirm. From then on that list is the team's folders: the plan delivers into them, the try mounts them, the promoted team carries them. A folder bound to a real one outside your working directory is read by a try that a `forge resume` runs — the way a client that confirms the folders does it, in a `--dry` run first: the process that tries a team can only read the directories it started with, so a run about to try the team in the same process refuses that folder at the confirmation (a recoverable `FORGE-FOLDERS-INVALID`) and says how. When a folder is kept inside the team, the forge then waits for you to drop its files in — Enter in the terminal, `folders.filled` on the event channel; `--auto` does not wait — and reads what each one holds into the brief, so the plan is made for those files. When the folder is an inbox whose contents change from run to run, say so in your need (« whatever documents are dropped in `/documents` »): the assistant marks the folder (`examples`), the files you drop are then examples of what will come, and the plan names none of them.
3. **The proposal.** From the brief, the assistant plans a team: who does what, in which order, with which tools — drawn from the real tool catalogue only; it cannot invent one. The plan is rendered as ordinary crew files and validated mechanically (unknown tools, unassigned tasks, dependency cycles, and a task or agent that names a file under a deliverable folder without depending on the task that writes it — an existing file under an output folder counts as written; see [`orkeon forge`](../reference/cli.md#orkeon-forge)). Validation errors go back to the assistant for repair — twice, then they surface to you. A text deliverable that one of the agents able to run its task cannot write (no writing tool of its own or granted by the task) is rendered `source: final_message` (the framework saves the agent's final answer there); a binary one (`.docx`, `.xlsx`…) needs its format's writer tool, or the plan fails validation.
4. **The try.** The crew runs in a sandbox, in-process, on your sample input, with the confirmed folders mounted and nothing else of yours: each output folder lands in the session's own directory (`folders/<name>`, snapshotted into `runs/<n>/folders/<name>` after each run), an input folder reads the real folder you bound behind it — else `--read`, else the workspace for `/workspace`, else the session's `folders/<name>`, which moves into the team at adoption — and `/forge` holds the session's working files; `shell_command`/`code_interpreter` and the eleven `drive_*` tools are removed from the catalogue outright (a try has no drive to read). The confinement is the session directory. A try runs **without memory**, whatever the plan's `memory:` says: it stores nothing and recalls nothing, so the promoted team never recalls a trial's output as one of its earlier runs — its memory starts with its first real run. A task that ends without writing its declared file is flagged `deliverableMissing` on its `task.completed`, and the try's `run.finished` counts it in `missingDeliverables`. A task whose agent ended on its iteration cap carries `endedOnIterationCap` on its `task.completed`, and `run.finished` counts it in `cappedTasks`.
5. **The verdict.** A judge grades the output against your acceptance criteria, one by one, and states findings and concrete suggestions. A missed *must* criterion blocks regardless of the score. If no judge can run, the verdict says so (`judge: deterministic`) — it never invents a ✔.
6. **Your call.** Interactive mode arbitrates every verdict, conforming ones included: accept (a conforming crew becomes ready; accepting a non-conforming one is your judgement on sight), re-run the trial as-is (`retry` — zero compose tokens, one budget iteration), refine (the diagnosis is fed back into the plan, verbatim), hand back an edited plan (`edit`), or stop. `--auto` arbitrates alone, within the budget.

Adoption itself is not a one-way door: `forge resume` of a **promoted** session reopens it at the arbitration (the stored verdict is re-announced), and a second `forge promote` to the **same** destination — or to that folder moved or renamed since — updates the folder in place — generated files regenerated, your own files preserved. Any other non-empty destination stays refused, a copy of the folder included. And the session is not a prerequisite either: `forge reopen <team-folder>` finds the session the folder is linked to by the id its `forge.json` carries, or rebuilds one from the folder's own `crew/` (and that `forge.json`) when the original is gone — or when the folder is a copy, which then gets a session of its own — landing at the `--dry` pause: an imported, orphaned or duplicated team can be amended, tried and re-adopted onto the same folder, not only relaunched.

Everything is bounded: 3 iterations by default (`--max-iterations`), optional token and wall-time ceilings (`--max-tokens`, `--max-seconds`). An exhausted budget stops the cycle cleanly; resuming may raise it — consumption always carries over.

## The session on disk

Each cycle lives under `.orkeon/forge/<slug>/` in your working directory:

```
.orkeon/forge/supplier-watch/
├── session.json          id, state, status, budget — the resume point
├── brief.json            what you asked, criteria included
├── folders.json          the folders you confirmed, with the real folder behind each
├── folders/<name>/       a folder kept inside the team until adoption; each try's outputs
├── blueprint.json        the team plan (single source of both renders)
├── repair.json           the validation errors a repair must address, while one is pending
├── crew/                 the rendered crew — what actually runs
├── runs/<n>/             each try: output, metrics, verdict, deliverables (folders/<name>/)
├── runs/<n>/on|off/      the two passes of an `optional` crew's try, one per decision circuit
├── transcript.jsonl      the conversation
└── history.jsonl         every state transition
```

```bash
orkeon forge list                      # what is in progress, what is ready
orkeon forge resume supplier-watch     # pick up exactly where it stopped
orkeon forge "..." --dry               # generate and validate only — never execute
orkeon forge resume supplier-watch --edit --dry   # amend the plan at the pause, re-render, pause again
orkeon forge resume supplier-watch --adopt        # keep the team as generated, without a trial
```

At the `--dry` pause you can amend the plan before ever trying it: `resume --edit` reads the amended blueprint from the channel, validates it in full, re-renders deterministically — zero LLM tokens, same iteration — and with `--dry` pauses again at the same boundary.

The same pause takes a second answer: `resume --adopt` keeps the team as generated and goes straight to `Ready` — offline, no run directory, zero tokens. What it skips is the **evidence** a trial produces, never a check: the crew is rendered and validated at that pause, and promotion never consumed a trial artefact — `FORGE.md` simply says «&nbsp;no verdict was recorded&nbsp;».

## Two formats, one generation

The assistant never writes YAML or TypeScript — it produces one schema-validated plan, and a deterministic renderer derives the files. `--format yaml` (default) renders the per-entity YAML layout; `--format script` renders an editable `crew.ork.ts` — the scripted equivalent of the same crew, a starting point for your own edits, not conditional logic. The script path needs esbuild; without it, a new session falls back to YAML and says so (`FORGE-ESBUILD-MISSING`).

Both renders converge on the same validator, and the try loads the crew **from the rendered files** — what was written is what runs.

## A crew that decides

`--crew-decision` sets what the designed crew asks of System One, the decision model, for the whole session:

- `none` (default): no decision task, no `when:`, no `system_one_decide`, and no `decision:` block in the crew.
- `optional`: the plan may route its work with decision tasks — a key, a description and `decision.questions`, no
  agent and no expected output — and tasks gated by `when:`. Each decision task names its relay, `without: { agent }`,
  the agent that answers when no decision model runs; it may run no task of its own. The try runs twice, with
  System One then through the relays, under `runs/<n>/on/` and `runs/<n>/off/`: it passes when both passes pass, its
  score is the lower one and each finding names its `circuit`. Without a decision model only the relay pass runs,
  with the warning `FORGE-DECISION-PASS-SKIPPED` and the finding `F-PASS-ON-SKIPPED`.
- `required`: the crew needs System One; `system_one_decide` is offered when the decision circuit is on. With the
  circuit off the session is refused before its interview (`FORGE-DECISION-UNAVAILABLE`, exit 1); the session is
  kept and resumes once a decision model is configured. `--dry` only warns, `--adopt` asks for no try.

The mode is recorded in `session.json` and cannot change on resume. A session older than the option resumes as
`required` when its plan holds a decision task or `system_one_decide`, else `none`, with `FORGE-DECISION-MODE-DERIVED`.
The promoted team records it in `forge.json`, and `FORGE.md` and the launcher state it; `reopen` reads it back from
the crew's `decision:` block. When the budget runs out after the first pass, the session stops there and its resume
runs the second pass only.

## System One's advice

When a decision model is configured and on, the forge asks it for advice while it designs the team — distinct from
the crew's own decisions above. Four questions, each answered as one line in the terminal and one
`systemone.answered` event:

```text
◇ System One advises (brief, coverage): coverage: score 2.0 (confidence 0.90)
◇ System One advises (blueprint, process): process: sequential (confidence 0.82)
◇ System One advises (test, estimate): estimate: yes (p 0.74)
◇ System One advises (diagnose, criteria): A1: yes (p 0.91)
```

- **coverage**, once the brief is submitted: does it cover the request as you stated it (`misses`, `partial`,
  `covers`)? For you only: the brief stays as the assistant wrote it.
- **process**, before the plan: which process fits the brief, among those the crew's decision mode allows. At a
  probability of 0.6 or more the assistant receives it as a suggestion; its own choice stands.
- **estimate**, before the try: will the team complete it and meet its criteria?
- **criteria**, after the judge: one reading per acceptance criterion. Never written into `verdict.json`.

None of it changes the verdict, `passing`, the arbitration or `--auto`; the tokens count against the budget and
appear as `systemOneTokens`. `--no-decision` turns this advice off for one run, without touching the trial of an
`optional` or `required` crew, which keeps the decision circuit of the host.

## Adopt it

```bash
orkeon forge promote supplier-watch --to ~/solutions/supplier-watch \
    --name "Supplier watch" --schedule daily@07:30
```

`--name` is the team's name: it titles the card, the record and the session. Once the folder is written, the session follows the team — its folder under `.orkeon/forge/` takes the destination folder's name (`-2` when another session already has it), so `forge list` shows it under the team it made.

The promoted folder is ordinary — nothing about it is proprietary to the forge:

- `crew/` — the team, as tried;
- one folder per confirmed folder (`inpdf/`, `outmd/`; `/workspace` reads `input/`) — created at promotion, holding whatever the session kept for it, so the first launch has somewhere to read and write;
- `run.sh` / `run.cmd` — launch scripts that `cd` into the folder, carry the mounts binding those folders (`--mount "$DIR/outmd":/outmd:rw`) and have your sample inputs pre-filled (adapt them to the real run);
- `FORGE.md` — the crew's identity card: goal, acceptance criteria, verdict, generation date and version — what a colleague reads when picking up the folder;
- `forge.json` — the card's machine-readable twin: the session's id, slug, title, format, promotion instant and the brief — the id links the folder back to its session wherever the folder goes, and the rest is what `forge reopen` reads to rebuild a faithful session once the original is gone (nothing secret in it);
- `schedule/` (with `--schedule`) — a Windows task XML, a systemd timer, a cron line, all named after the team folder (`orkeon-supplier-watch.timer`). The promotion installs none of them: `orkeon forge schedule ~/solutions/supplier-watch` registers the one your system uses, and notes what it registered in `schedule/installed.json` (`--check` says where it stands, `orkeon forge unschedule` removes it). Orkeon has no scheduler of its own — the operating system runs the team — so it promises no supervision it cannot give.

Run it with its own launcher — `~/solutions/supplier-watch/run.sh`. A bare `orkeon run ~/solutions/supplier-watch/crew` also launches it: the promoted `config.yaml` names the folders the team uses (`mounts: [/inpdf, /outmd]`), so a settings entry declaring `/outmd` is used as it stands, and with none the run is refused in one line (`the crew requires '/outmd' … pass --mount <folder>:/outmd:rw`) instead of writing nowhere.

## Other options

| Option | Applies to | What it does |
|---|---|---|
| `--no-decision` | a new session, `forge resume` | The forge asks System One nothing for its own advice (below); the crew's own trial is unchanged. |
| `--read <dir>` | a new session, `forge resume` | The folder the trial reads behind the first input folder no real folder was bound to (`/workspace` by default), in place of the working directory — point a trial at the documents the team is meant to read; the sessions stay under the working directory. |
| `--reference <id>` | a new session | Composes the team from a use case of the catalogue (`orkeon usecases list`): its crew's structure is the assistant's model, and the session, `forge.json` and `FORGE.md` record it. |
| `--lang <code>` | a new session, `forge resume` | The language the assistant converses in and names the team in (`fr`, `en`, `es`, `de`, `zh`; English by default) — until you write in another language or ask for one. |
| `--settings <path>` | a new session, `forge resume` | The settings file, with the same semantics as `orkeon run --settings`. |
| `--with-settings` | `forge promote` | Copies the resolved settings file into the promoted folder. Off by default: a settings file usually holds API keys and the folder is made to be shared — without the copy, the launch scripts reference the file in place. |
| `--events jsonl` | every verb | The versioned event protocol on stdout, the answers on stdin — how an external client drives the forge. |
| `--pack <dir>` | a new session, `forge resume` | A folder whose files override the embedded assistant pack. |

`orkeon forge rename <team-folder> --name "<new name>"` renames a promoted team — every title, the folder itself, the linked session's folder and an installed schedule — all of it or nothing.

## Honest limits

- The quality of the proposal tracks the model you configured; the brief's criteria and the mechanical validation are the guardrails, not a substitute.
- The sandbox restricts by **removing tools from the catalogue**, not by hoping the model abstains; network tools reach the plan only if your brief asked for them.
- Mail is tried, never sent: the twelve mailbox tools are offered, and every try runs them on a fictitious trial mailbox; the sends, drafts and changes it would have made are shown under the verdict and nothing reaches a real mailbox ([Forged crews](../guides/email.md#forged-crews)).
- One session, one problem: the Atelier generates and validates a team — it is not a visual crew editor.
