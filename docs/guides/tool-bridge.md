> 🇫🇷 [Version française](../fr/guides/tool-bridge.md)

# Orkeon in a container, the browser on the desktop

> **See also**: [ADR-019](../adr/ADR-019-tool-bridge.md) ·
> [Configuration reference](../reference/configuration.md#orkeontoolsbridge) ·
> [CLI reference](../reference/cli.md#orkeon-bridge) · [Security](../architecture/security.md#tool-bridge) ·
> [The `firefox` tool](./firefox.md) · [Back to index](../INDEX.md)

An Orkeon that runs in a container cannot reach what lives on your machine: the browser you are signed in to, a
desktop application, a device. The **tool bridge** lets some built-in tools of a run execute on your machine while
the run, its model calls, its pipeline and its workspace stay in the container. Two Orkeon take part:

- **the bridge**, on your machine: `orkeon bridge serve` runs the tools it selects for a caller;
- **the caller**, in the container: the section `Orkeon:Tools:Bridge` names the bridge and the tools to send there.

A tool is bridged when both sides select it. The caller keeps judging every call as it judges a local one: its
permission gate, its guardian, its truncation and its sanitizer see the call and its result. Only the call crosses.

## 1. The bridge, on your machine

The settings file of the folder you start it from (`appsettings.json`, or `--settings <file>`):

```json
{ "Orkeon": { "Bridge": {
  "Allow": ["firefox"]
} } }
```

The token is never in a file. Choose one, then start the bridge with it in its environment:

```bash
export ORKEON_BRIDGE_TOKEN="$(openssl rand -hex 32)"
orkeon bridge serve
```

```text
orkeon bridge serve: listening on http://127.0.0.1:7410/
orkeon bridge serve: serving firefox
orkeon bridge serve: Ctrl+C stops it.
```

It listens on loopback, port 7410 (`http://localhost:7410/` under Windows), until Ctrl+C or SIGTERM. The tool runs
with the bridge's own settings: `Orkeon:Tools:Firefox` and the URL policy are read on your machine, since the network
the browser reaches is your machine's.

## 2. The caller, in the container

```json
{ "Orkeon": { "Tools": { "Bridge": {
  "Address": "http://127.0.0.1:7410/",
  "Allow": ["firefox"]
} } } }
```

The container receives the same token in `ORKEON_BRIDGE_TOKEN` (`docker run -e ORKEON_BRIDGE_TOKEN`). At start the
caller asks the bridge which tools it serves and refuses to start when the bridge cannot be reached, refuses the
token, does not serve a tool `Allow` names, or serves one whose contract differs (the two sides run different
versions of it). Once started, a call the bridge cannot answer fails with the reason, and the next call tries again;
a bridged tool never falls back to running in the container. A section `Orkeon:Tools:Firefox` left in the container
is reported once and not read.

`orkeon run`, `validate`, the `.ork.ts` scripts, `eval` and `orkeon-host` all connect the same way; `orkeon-host`
exits 78 on a refusal.

## 3. Reaching the bridge from the container

The bridge answers http on loopback only; an address other than loopback requires https, so that the token never
travels in clear off the machine.

- **A Linux engine.** Run the container on the host's network: its loopback is then the machine's, and the caller's
  `Address` is `http://127.0.0.1:7410/`.

  ```bash
  docker run --rm --network host -e ORKEON_BRIDGE_TOKEN -v "$PWD:/workspace" ghcr.io/orkeon/orkeon \
    run /workspace/crew.ork.ts --settings /workspace/appsettings.json --mount /workspace/output:/output:rw
  ```

  The Docker bridge address (`172.17.0.1`) would need the bridge to serve https, which it does under Windows only
  (`HttpListener` has no TLS on Linux and macOS): an https `Orkeon:Bridge:Prefix` there is refused at start. Without
  `--network host`, put a TLS terminator of your choice on the Docker bridge address in front of the loopback
  listener, and give the caller its `https://` address.
- **Docker Desktop (Windows, macOS).** `host.docker.internal` reaches a listener on the host's loopback. The bridge
  keeps listening on loopback; the caller's `Address` is `http://host.docker.internal:7410/`, the one http address
  besides loopback the caller accepts. The listener answers only the host its prefix names, so the caller sends the
  `Host` header `Orkeon:Tools:Bridge:HostHeader`, `127.0.0.1:<port>` when nothing sets it: right for the default
  prefix on macOS. Under Windows the default prefix is `http://localhost:7410/`, so set `HostHeader` to
  `localhost:7410` (HTTP.sys matches that prefix on the `Host` header alone).

  ```json
  { "Orkeon": { "Tools": { "Bridge": { "Address": "http://host.docker.internal:7410/", "HostHeader": "localhost:7410", "Allow": ["firefox"] } } } }
  ```

  The start logs a warning: over http the token and every call travel in clear over the Docker host's virtual
  network, which other containers of that host may see. This path is not yet checked on a real Docker Desktop; see
  [Known limitations](../reference/limitations.md).
- **A bridge under Windows on another address.** An https prefix other than `localhost` needs a URL reservation for
  the account that runs the bridge, and a certificate bound to the port, both made once in an elevated prompt:

  ```powershell
  netsh http add urlacl url=https://192.168.1.20:7410/ user=DOMAIN\you
  netsh http add sslcert ipport=192.168.1.20:7410 certhash=<thumbprint> appid={<any GUID>}
  ```

  Without the reservation the start fails, and the message names the command.

## 4. What travels, and for how long

- **One run on the caller is one run on the bridge.** The bridge keeps one scope per caller run, so "one browser per
  run" means what it means locally. The caller ends that scope (`POST end-run`) when its own run ends, and beats
  (`POST heartbeat`) every third of `Orkeon:Bridge:HeartbeatWindow` (30 s) while it has runs in flight. A caller
  silent past the window loses its runs on the bridge, and their browsers, at once: a caller that comes back starts
  new ones.
- **A path stays the caller's.** A `screenshot` to `/output/page.png` lands in the caller's `/output` mount. A call of
  a tool that uses files carries the caller's mounts and the files its arguments name; the bridge presents them at
  the same virtual paths in a scratch folder for the length of the call, sends back what the tool wrote, then removes
  the folder. The caller writes those files through its own file system, under the rights of its run.
  `Orkeon:Tools:Bridge:MaxFileBytes` and `Orkeon:Bridge:MaxFileBytes` (16 MiB each) bound each direction.
- **What `*` never selects.** `"Allow": ["*"]` selects every tool, on either side, except those that act on the
  workspace (files, shell, code index): bridged, they would act on your machine while the workspace is in the
  container. Naming one bridges it, and each side logs a warning. A coworker delegation tool is never bridged.

## 5. When it does not start

| Message (abridged) | What to do |
| --- | --- |
| `reads its token from the environment variable ORKEON_BRIDGE_TOKEN, which is not set` | export the token where that side starts |
| `the tool bridge at … refused the token` | give both sides the same token |
| `Orkeon:Tools:Bridge:Allow names 'x', which the tool bridge at … does not serve` | add `x` to the bridge's `Allow`, or remove it from the caller's |
| `names a host other than loopback over http: such an address requires https` | use loopback (`--network host`) or https |
| `could not listen on …` | another process holds the port, or, under Windows, the prefix has no URL reservation |
