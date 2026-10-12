#!/usr/bin/env python3
"""Lists or checks the API-key console links of the Studio provider catalogue.

Manual only: never called by a workflow (BR-12), because it reaches the network
and the connection may be metered. The catalogue is read from
src/apps/Orkeon.Studio.Core/Presets/LlmPresets.cs, never copied here (BR-02).

  check-console-urls.py --list   prints one link per line, offline
  check-console-urls.py          requests each link; exit 1 if one is in error
                                 (HTTP 404/410, unreachable host, timeout)
"""
import re
import sys
import urllib.error
import urllib.request
from urllib.parse import urlsplit
from pathlib import Path

SOURCE = Path(__file__).resolve().parent.parent / "src/apps/Orkeon.Studio.Core/Presets/LlmPresets.cs"
ENTRY = re.compile(r'new\(\s*(\w+)\s*,(?:(?!new\().)*?LlmPresetKind\.Cloud,\s*"([^"]+)"', re.S)
USER_AGENT = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
TIMEOUT = 15


def normalise(url: str) -> str:
    """A bare host gets its root slash, as the C# Uri prints it."""
    return url if urlsplit(url).path else url + "/"


def catalogue() -> list[tuple[str, str]]:
    text = SOURCE.read_text(encoding="utf-8")
    return [(name, normalise("https://" + value.strip())) for name, value in ENTRY.findall(text)]


def request(url: str, method: str):
    req = urllib.request.Request(url, method=method, headers={"User-Agent": USER_AGENT})
    return urllib.request.urlopen(req, timeout=TIMEOUT)


def check(url: str) -> tuple[bool, str, str]:
    """Returns (answers, landing url, status or reason)."""
    for method in ("HEAD", "GET"):
        try:
            with request(url, method) as response:
                return True, response.geturl(), str(response.status)
        except urllib.error.HTTPError as error:
            if method == "HEAD" and error.code in (405, 501):
                continue
            return error.code not in (404, 410), error.url or url, str(error.code)
        except Exception as error:  # unreachable host, timeout, TLS
            if method == "HEAD":
                continue
            return False, url, str(getattr(error, "reason", error))
    return False, url, "no answer"


def main(argv: list[str]) -> int:
    links = catalogue()
    if not links:
        print("no console link found in " + str(SOURCE), file=sys.stderr)
        return 1
    if "--list" in argv:
        for name, url in links:
            print(f"{url}  {name}")
        return 0
    failures = 0
    for name, url in links:
        answers, landing, status = check(url)
        failures += 0 if answers else 1
        print(f"{name}: {url} -> {landing} : {'répond' if answers else 'en erreur'} ({status})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
