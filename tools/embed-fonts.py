# /// script
# requires-python = ">=3.9"
# dependencies = ["fonttools[woff]==4.60.1"]
# ///
"""マニュアルの本文(Noto Sans JP)とコード(Noto Sans Mono)の書体を, 使う文字だけ埋め込む.

配ったマニュアルを開いたときに, Web(Google Fonts)を見にいかないようにするため(H9).
見出しの明朝は, anthropic-htmlスキルのbuild_html.pyが埋め込む.
元の書体(SIL Open Font License 1.1)は, 決まった版を落として~/.cache/wakudori-fonts/に置く.

使い方: uv run tools/embed-fonts.py docs/manual.html
"""
import base64
import hashlib
import io
import re
import sys
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

BASE = "https://raw.githubusercontent.com/google/fonts/66a36c8c94b1a5d992ee4e7f392fccfe4945767c/ofl/"
CACHE = Path.home() / ".cache" / "wakudori-fonts"
# 名前, 置き場所, sha256, 残す軸の範囲
FONTS = [
    ("Noto Sans JP", "notosansjp/NotoSansJP%5Bwght%5D.ttf", "c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f", {"wght": (400, 700)}),
    ("Noto Sans Mono", "notosansmono/NotoSansMono%5Bwdth,wght%5D.ttf", "2cb2adb378a8f574213e23df697050b83c54c27df465a2015552740b2769a081", {"wght": (400, 700), "wdth": 100}),
]

# コードの中の日本語は, 埋め込んだNoto Sans JPで出す
FALLBACK = ':root { --font-mono: "Noto Sans Mono", "Noto Sans JP", ui-monospace, Menlo, Consolas, monospace; }\n'



def fetch(path, sha256):
    CACHE.mkdir(parents=True, exist_ok=True)
    f = CACHE / (sha256 + ".ttf")
    if not f.exists():
        data = urllib.request.urlopen(BASE + path, timeout=120).read()
        got = hashlib.sha256(data).hexdigest()
        if got != sha256:
            sys.exit("書体のsha256が違います: " + path + " " + got)
        f.write_bytes(data)
    return f


def subset_woff2(path, axes, chars):
    font = TTFont(str(path))
    have = set(font.getBestCmap())
    opts = subset.Options()
    opts.hinting = False
    opts.layout_features = ["palt", "kern", "liga", "ccmp", "locl"]
    sub = subset.Subsetter(opts)
    sub.populate(text="".join(c for c in chars if ord(c) in have))
    sub.subset(font)
    buf = io.BytesIO()
    font.save(buf)
    buf.seek(0)
    font = instantiateVariableFont(TTFont(buf), axes)
    font.flavor = "woff2"
    buf = io.BytesIO()
    font.save(buf)
    return buf.getvalue(), [c for c in chars if ord(c) not in have]


def main():
    page = Path(sys.argv[1])
    html = page.read_text(encoding="utf-8")
    # 文字はHTMLとJavaScript(デモの中の文字)から集める. CSSと埋め込み済みのデータは除く
    text = re.sub(r"<style>.*?</style>", "", html, flags=re.S)
    text = re.sub(r"data:[a-z0-9/+.-]+;base64,[A-Za-z0-9+/=]+", "", text)
    chars = sorted({c for c in text if c >= " "} | {chr(c) for c in range(0x20, 0x7F)} | set("…、。・"))

    faces = []
    for name, path, sha256, axes in FONTS:
        data, missing = subset_woff2(fetch(path, sha256), axes, chars)
        b64 = base64.b64encode(data).decode("ascii")
        faces.append('@font-face { font-family: "%s"; font-weight: 400 700; font-style: normal; font-display: block;\n'
                     '  src: url(data:font/woff2;base64,%s) format("woff2"); }\n' % (name, b64))
        print("%s: %s %d字, %.1fKB, 書体にない文字 %d" % (page, name, len(chars) - len(missing), len(b64) / 1024, len(missing)))
        if name == "Noto Sans JP" and missing:
            print("  Noto Sans JPにない文字: " + "".join(missing))

    html = re.sub(r'[ \t]*<link [^>]*href="https://fonts\.(?:googleapis|gstatic)\.com[^>]*>\n?', "", html)
    style = "<style>\n/* 本文とコードの書体: Noto Sans JP, Noto Sans Monoのサブセット(SIL Open Font License 1.1) */\n" + "".join(faces) + FALLBACK + "</style>\n"
    html = html.replace("</head>", style + "</head>", 1)
    left = re.findall(r"fonts\.(?:googleapis|gstatic)", html)
    if left:
        sys.exit("Google Fontsの読み込みが残っています")
    page.write_text(html, encoding="utf-8")


main()
