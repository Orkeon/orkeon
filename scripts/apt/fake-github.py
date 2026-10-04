#!/usr/bin/env python3
"""Fake GitHub for the apt scripts: three host names, two cross-host 302 redirects.

It answers what apt and the documented installation block ask GitHub for, the way GitHub
does, so an apt index can be proved by real apt clients before it is pushed:

  github.test/Orkeon/orkeon/raw/apt/<path>           302 -> raw.test/Orkeon/orkeon/apt/<path>
  raw.test/Orkeon/orkeon/apt/<path>                  200, the file <branch>/<path>
  github.test/Orkeon/orkeon/releases/download/<tag>/<asset>
                                                     302 -> objects.test/<long signed-looking URL>
  objects.test/...                                   200, the file <assets>/<tag>/<asset>

Anything else is a 404. Every request is logged as "<status> <host> <path>". The clients
map the three names to this server (docker --add-host). Used by test-build-apt-index.sh,
check-apt-branch.sh and test-verify-apt-repo.sh; never by anything a user runs.

Usage: fake-github.py [--branch DIR] [--assets DIR] [--log FILE] [--port N]
  --port also goes into the redirect locations (":N" when it is not 80).
"""
import argparse
import hashlib
import os
import re
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

parser = argparse.ArgumentParser()
parser.add_argument("--branch", default="/srv/branch", help="the files of the apt branch")
parser.add_argument("--assets", default="/srv/assets", help="<tag>/<asset>, the Release assets")
parser.add_argument("--log", default="/srv/requests.log")
parser.add_argument("--port", type=int, default=80)
args = parser.parse_args()

BRANCH = os.path.realpath(args.branch)
ASSETS = os.path.realpath(args.assets)
LOG = args.log
SUFFIX = "" if args.port == 80 else f":{args.port}"


def token(tag, asset):
    return hashlib.sha256(f"{tag}/{asset}".encode()).hexdigest()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def reply(self, status, headers=None, path=None, body=True):
        size = os.path.getsize(path) if path else 0
        self.send_response(status)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(size))
        if path:
            self.send_header("Content-Type", "application/octet-stream")
        self.end_headers()
        if path and body:
            with open(path, "rb") as f:
                self.wfile.write(f.read())
        with open(LOG, "a") as f:
            f.write(f"{status} {self.headers.get('Host', '').split(':')[0]} {self.path}\n")

    def safe(self, root, rel):
        full = os.path.realpath(os.path.join(root, rel))
        return full if full.startswith(root + "/") and os.path.isfile(full) else None

    def serve(self, body):
        host = self.headers.get("Host", "").split(":")[0]
        raw_path = self.path.split("?", 1)[0]
        path = urllib.parse.unquote(raw_path)
        if host == "github.test":
            m = re.fullmatch(r"/Orkeon/orkeon/raw/apt/(.+)", raw_path)
            if m:
                return self.reply(302, {"Location": f"http://raw.test{SUFFIX}/Orkeon/orkeon/apt/{m.group(1)}"})
            m = re.fullmatch(r"/Orkeon/orkeon/releases/download/([^/]+)/([^/]+)", path)
            if m and self.safe(ASSETS, f"{m.group(1)}/{m.group(2)}"):
                tag, asset = m.groups()
                q = urllib.parse.urlencode({
                    "X-Amz-Algorithm": "AWS4-HMAC-SHA256",
                    "X-Amz-Credential": "FAKEKEY/20261004/us-east-1/s3/aws4_request",
                    "X-Amz-Date": "20261004T000000Z",
                    "X-Amz-Expires": "300",
                    "X-Amz-Signature": hashlib.sha256(asset.encode()).hexdigest(),
                    "X-Amz-SignedHeaders": "host",
                    "actor_id": "0",
                    "key_id": "0",
                    "repo_id": "123456789",
                    "response-content-disposition": f"attachment; filename={asset}",
                    "response-content-type": "application/octet-stream",
                }, quote_via=urllib.parse.quote)
                loc = f"http://objects.test{SUFFIX}/github-production-release-asset-2e65be/123456789/{token(tag, asset)}?{q}"
                return self.reply(302, {"Location": loc})
        elif host == "raw.test":
            m = re.fullmatch(r"/Orkeon/orkeon/apt/(.+)", path)
            full = m and self.safe(BRANCH, m.group(1))
            if full:
                return self.reply(200, path=full, body=body)
        elif host == "objects.test":
            m = re.fullmatch(r"/github-production-release-asset-2e65be/123456789/([0-9a-f]{64})", path)
            if m and os.path.isdir(ASSETS):
                for tag in sorted(os.listdir(ASSETS)):
                    for asset in sorted(os.listdir(os.path.join(ASSETS, tag))):
                        if token(tag, asset) == m.group(1):
                            return self.reply(200, path=os.path.join(ASSETS, tag, asset), body=body)
        return self.reply(404)

    def do_GET(self):
        self.serve(True)

    def do_HEAD(self):
        self.serve(False)


ThreadingHTTPServer(("0.0.0.0", args.port), Handler).serve_forever()
