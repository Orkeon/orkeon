# The chromium-bidi mapper

`mapperTab.js` is `out/Default/gen/src/mapperTab.js` of the npm package `chromium-bidi` 160.0.8181-0 (Apache-2.0, `LICENSE` beside it), byte for byte.
Its SHA-256, which `ChromiumBiDiTransportTests` checks, is `6950a24bb711fefa888f92f0992b0e11a3f8b981f5e698f5646925c1cf9c2292`.

To move to another version: `npm pack chromium-bidi@<version>`, copy the two files, update the version and the hash here,
in `ChromiumMapper` and in `THIRD-PARTY-NOTICES.md`, then run the Chromium integration tests against a real browser.
