#!/usr/bin/env python3
"""Watch an Orkeon run from another program.

Launches `orkeon run <target> --events jsonl`, reads the protocol on stdout, prints a
compact progress view, and answers the run's questions on stdin. About 90 lines, no
dependencies — the point is that the protocol is small enough to consume in an afternoon.

    ./watch-run.py path/to/crew.yaml [--settings path/to/appsettings.json]

Pass --replay sample-stream.jsonl to try the reader with no model configured.
"""

import argparse
import json
import subprocess
import sys


def render(event, send):
    """Print one event, and answer it when it is a question."""
    kind = event.get("kind")

    if kind == "run.started":
        print(f"▶  {event.get('target')}")

    elif kind == "task.completed":
        mark = "✔" if event.get("success") else "✘"
        who = event.get("agentRole") or event.get("taskId") or "—"
        print(f"   {mark} {who}  ({event.get('durationMs', 0) / 1000:.1f}s)")

    elif kind == "cost.updated":
        print(f"   · {event.get('tokens', 0)} tokens")

    elif kind == "error":
        print(f"   ! {event.get('code')}: {event.get('message')}", file=sys.stderr)

    elif kind == "input.needed":
        prompt = event.get("prompt", "")
        choices = event.get("choices") or []
        if choices:
            prompt += f" [{'/'.join(choices)}]"

        if send is None:
            # Replaying a recording: there is no run left to answer, and prompting for an
            # answer nobody will receive would just hang.
            print(f"?  {prompt}")
            return

        # Silence is not consent: the run waits, so we actually ask.
        answer = input(f"?  {prompt} ")
        send({"kind": "input.given", "correlationId": event.get("correlationId"), "value": answer})

    elif kind == "hub.message":
        print(f"   ✉ {event.get('topic') or event.get('from')}: {json.dumps(event.get('payload'))}")

    elif kind == "run.finished":
        print("✔  done" if event.get("success") else f"✘  exit {event.get('exitCode')}")

    # Any other kind is ignored on purpose: a newer Orkeon says more than this reader
    # understands, and crashing on an unread line would be worse than showing less.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", nargs="?", help="the crew to run")
    parser.add_argument("--settings", help="path to appsettings.json")
    parser.add_argument("--replay", help="read a recorded stream instead of running anything")
    args = parser.parse_args()

    if args.replay:
        with open(args.replay, encoding="utf-8") as recorded:
            for line in recorded:
                consume(line, None)
        return 0

    if not args.target:
        parser.error("a target is required unless --replay is used")

    argv = ["orkeon", "run", args.target, "--events", "jsonl"]
    if args.settings:
        argv += ["--settings", args.settings]

    run = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)

    def send(message):
        run.stdin.write(json.dumps(message) + "\n")
        run.stdin.flush()

    for line in run.stdout:
        consume(line, send)

    return run.wait()


def consume(line, send):
    """Read one line. What is not protocol stays visible rather than being dropped."""
    line = line.strip()
    if not line:
        return

    try:
        event = json.loads(line)
    except json.JSONDecodeError:
        print(line)
        return

    if isinstance(event, dict) and "kind" in event:
        render(event, send)
    else:
        print(line)


if __name__ == "__main__":
    sys.exit(main())
