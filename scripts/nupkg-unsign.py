#!/usr/bin/env python3
"""Recover the exact bytes a nupkg had before nuget.org repository-signed it.

nuget.org appends a `.signature.p7s` entry to every package it accepts and rewrites the
zip central directory around it. The bytes GitHub attested (`publish.yml`, step
"Attest the packages") are the bytes *before* that happened, so `gh attestation verify`
on a package downloaded from nuget.org always fails on the digest -- not because the
package is wrong, but because it is no longer the same file.

The signature is always the last local entry and the last central-directory record, so
the original is recovered byte-for-byte without re-zipping: everything before the
signature's local header, the central directory minus its record, and an end-of-central-
directory record with the counts, size and offset put back. Measured on
`Orkeon.1.0.0-rc.3.nupkg`: the recovered digest is the one in the publish.yml attestation.

    python3 scripts/nupkg-unsign.py Orkeon.1.0.0-rc.3.nupkg
    gh attestation verify Orkeon.1.0.0-rc.3.unsigned.nupkg --repo Orkeon/orkeon

Standard library only; prints the output path and its SHA-256.
"""

from __future__ import annotations

import hashlib
import struct
import sys
import zipfile
from pathlib import Path

SIGNATURE_ENTRY = ".signature.p7s"
EOCD_SIGNATURE = b"PK\x05\x06"
EOCD_STRUCT = struct.Struct("<IHHHHIIH")
CD_STRUCT = struct.Struct("<IHHHHHHIIIHHHHHII")


def unsign(data: bytes) -> bytes:
    eocd = data.rfind(EOCD_SIGNATURE)
    if eocd < 0:
        raise ValueError("not a zip archive (no end-of-central-directory record)")
    (sig, disk, cd_disk, n_disk, n_total, cd_size, cd_off, comment_len) = EOCD_STRUCT.unpack(
        data[eocd:eocd + EOCD_STRUCT.size])

    records: list[tuple[str, int, int, int]] = []  # name, record offset, record size, local offset
    p = cd_off
    while p < cd_off + cd_size:
        fields = CD_STRUCT.unpack(data[p:p + CD_STRUCT.size])
        name_len, extra_len, comment_len2, local_off = fields[10], fields[11], fields[12], fields[16]
        name = data[p + CD_STRUCT.size:p + CD_STRUCT.size + name_len].decode("utf-8")
        size = CD_STRUCT.size + name_len + extra_len + comment_len2
        records.append((name, p, size, local_off))
        p += size

    signature = [r for r in records if r[0] == SIGNATURE_ENTRY]
    if not signature:
        raise ValueError(f"no {SIGNATURE_ENTRY} entry: this package is not repository-signed")
    sig_name, sig_rec_off, sig_rec_size, sig_local_off = signature[0]
    if sig_local_off != max(r[3] for r in records) or records[-1][0] != SIGNATURE_ENTRY:
        raise ValueError("the signature is not the last entry -- unexpected layout, refusing to guess")

    central = b"".join(data[r[1]:r[1] + r[2]] for r in records if r[0] != SIGNATURE_ENTRY)
    new_eocd = EOCD_STRUCT.pack(sig, disk, cd_disk, n_disk - 1, n_total - 1,
                                len(central), sig_local_off, comment_len)
    return data[:sig_local_off] + central + new_eocd + data[eocd + EOCD_STRUCT.size:]


def main(argv: list[str]) -> int:
    if len(argv) != 2 or not argv[1].endswith(".nupkg"):
        print("usage: nupkg-unsign.py <package.nupkg>", file=sys.stderr)
        return 2
    src = Path(argv[1])
    out = src.with_name(src.name[:-len(".nupkg")] + ".unsigned.nupkg")
    original = unsign(src.read_bytes())
    out.write_bytes(original)
    if zipfile.ZipFile(out).testzip() is not None:
        print(f"{out}: the recovered archive does not pass zip validation", file=sys.stderr)
        return 1
    print(f"{out}  sha256:{hashlib.sha256(original).hexdigest()}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
