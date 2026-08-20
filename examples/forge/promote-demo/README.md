# Forge: promote a ready session (offline demo)

This folder is a **workspace holding one finished Atelier session** — the state `orkeon forge` leaves behind once a crew has been generated, tried in the sandbox and judged conforming. It lets you exercise the offline half of the cycle (`list`, `promote`) without an LLM; the interview and the sandboxed try themselves need a configured model — see [Forge a team from a need](../../../docs/getting-started/forge-a-team-from-a-need.md) for the full path.

```bash
cd examples/forge/promote-demo

# What the workspace holds: one session, ready to be adopted.
orkeon forge list

# Ship it as an ordinary folder, with a generated daily schedule.
orkeon forge promote supplier-watch --to ./my-solution --schedule daily@07:30
```

The promoted folder is ordinary — nothing in it is proprietary to the forge:

```
my-solution/
├── crew/                 the team, exactly as it was tried
├── run.sh / run.cmd      launch scripts with the brief's sample inputs pre-filled
├── FORGE.md              the crew's identity card: goal, acceptance criteria, verdict
└── schedule/             Windows task XML · systemd timer · cron line
                          (the install command is displayed, never executed)
```

From there, `orkeon run my-solution/crew --var supplier_url=…` runs it like any crew (this one needs an LLM and network access for `web_scrape`), and the Orkeon Studio launcher detects the folder.

## What to look at

- `.orkeon/forge/supplier-watch/` — the session layout of the spec: `session.json` (state, status, budget consumed), `brief.json` (the need **and the acceptance criteria** everything was judged against), `verdict.json`, and `crew/` — the rendered files that actually ran in the sandbox.
- `FORGE.md` in the promoted folder — written in the interview's language, for the colleague who picks the folder up.
- The session stays listed after promotion (`status: Promoted`, with the destination recorded), which is how Studio's "My solutions" offers *run it again*.

> The promotion writes into the session (`status`, destination): only a `Ready` session promotes, so a second run is refused by design. To replay the demo, restore the folder: `git checkout -- .orkeon` and delete `my-solution/`.
