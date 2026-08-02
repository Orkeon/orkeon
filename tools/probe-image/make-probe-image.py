"""Regenerates the M9 vision probe image embedded in LlmProbeRunner.

Run it, paste the printed block over the `ProbeImagePng` constant in
`src/scripting/Orkeon.Scripting.Cli/Commands/LlmProbeRunner.cs`, and keep
`VisionProbeNumber` and the dimension assertions in
`tests/scripting/Orkeon.Scripting.Cli.Tests/Commands/LlmProbeRunnerTests.cs`
in step with whatever you changed here.

    python3 tools/probe-image/make-probe-image.py

Why the image is what it is
---------------------------
The probe used to send a flat red square and ask for the dominant colour. On
2026-08-02 `glm-4.6v-flash` answered "orange", and nothing in the exchange could
separate "saw red, named it badly" from "saw nothing, guessed a colour" — over a
single flat colour the answer space is small enough that a blind guess lands. The
mode was archived red while the real finding (the base64 image had transited
fine) went unrecorded.

So the image now carries two independent facts. The number is the assertion:
1 in 100 by chance, and no model produces it without reading the glyphs. The red
field is the corroborating channel, which is what lets a report say *which* half
failed — colour right and number wrong is a model that cannot read, colour wrong
too is an image that probably never arrived.

No Pillow dependency: the PNG is emitted by hand so this runs anywhere Python does.
"""

import base64
import struct
import zlib

# 5x7 pixel font, scaled up rather than anti-aliased — crisp edges survive the
# aggressive downscaling some vision stacks apply before inference.
FONT = {
    "0": [".111.", "1...1", "1..11", "1.1.1", "11..1", "1...1", ".111."],
    "1": ["..1..", ".11..", "..1..", "..1..", "..1..", "..1..", ".111."],
    "2": [".111.", "1...1", "....1", "...1.", "..1..", ".1...", "11111"],
    "3": [".111.", "1...1", "....1", "..11.", "....1", "1...1", ".111."],
    "4": ["...1.", "..11.", ".1.1.", "1..1.", "11111", "...1.", "...1."],
    "5": ["11111", "1....", "1111.", "....1", "....1", "1...1", ".111."],
    "6": ["..11.", ".1...", "1....", "1111.", "1...1", "1...1", ".111."],
    "7": ["11111", "....1", "...1.", "..1..", ".1...", ".1...", ".1..."],
    "8": [".111.", "1...1", "1...1", ".111.", "1...1", "1...1", ".111."],
    "9": [".111.", "1...1", "1...1", ".1111", "....1", "...1.", ".11.."],
}

TEXT = "73"          # keep in sync with VisionProbeNumber
SCALE = 12
GAP = 16
MARGIN_X = 12
MARGIN_Y = 14

BACKGROUND = (255, 0, 0)      # pure red — the corroborating channel
FOREGROUND = (255, 255, 255)  # white digits, maximum contrast


def render():
    glyph_w, glyph_h = 5 * SCALE, 7 * SCALE
    width = MARGIN_X * 2 + glyph_w * len(TEXT) + GAP * (len(TEXT) - 1)
    height = MARGIN_Y * 2 + glyph_h
    rows = [[BACKGROUND] * width for _ in range(height)]

    for index, char in enumerate(TEXT):
        x0 = MARGIN_X + index * (glyph_w + GAP)
        for row, bits in enumerate(FONT[char]):
            for col, bit in enumerate(bits):
                if bit != "1":
                    continue
                for dy in range(SCALE):
                    line = rows[MARGIN_Y + row * SCALE + dy]
                    start = x0 + col * SCALE
                    for dx in range(SCALE):
                        line[start + dx] = FOREGROUND

    return width, height, rows


def encode(width, height, rows):
    raw = bytearray()
    for row in rows:
        raw.append(0)  # filter type None: the rows are flat, filtering buys nothing
        for pixel in row:
            raw += bytes(pixel)

    def chunk(tag, data):
        body = tag + data
        return (struct.pack(">I", len(data)) + body
                + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def main():
    width, height, rows = render()
    png = encode(width, height, rows)
    b64 = base64.b64encode(png).decode()

    print("# %dx%d, %d bytes, %d base64 chars\n" % (width, height, len(png), len(b64)))
    print("    private const string ProbeImagePng =")
    lines = [b64[i:i + 78] for i in range(0, len(b64), 78)]
    for index, line in enumerate(lines):
        terminator = ";" if index == len(lines) - 1 else " +"
        print('        "%s"%s' % (line, terminator))


if __name__ == "__main__":
    main()
