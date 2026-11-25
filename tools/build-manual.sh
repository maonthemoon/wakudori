#!/bin/sh
# マニュアル(docs/manual.html)と紹介のページ(docs/index.html. GitHub Pagesのトップ)を,
# 開発用の文書の元のファイル(dev/manual-src/, dev/site-src/)から作り直す.
# 動くデモ(dev/demo-src/)は両方に埋め込む.
# anthropic-htmlスキルのbuild_html.pyでCSSを埋め込み, 見出しの明朝を使う文字だけ埋め込む.
# 公開するファイルのCSSのコメントから, 他社のサイト名と出どころの記述を消す(G2).
set -eu
cd "$(dirname "$0")/.."
export SOURCE_DATE_EPOCH=1767225600 # 埋め込む書体の日時を固定し, 中身が同じなら同じファイルになるようにする
SKILL="${ANTHROPIC_HTML:-$HOME/.claude/skills/anthropic-html}"
uv run "$SKILL/scripts/build_html.py" dev/manual-src/index.html docs/manual.html --extra "範囲に名前を付ける"
# デモの中の名前の画面の見出しはJSで描くので, 明朝の文字を足す
uv run "$SKILL/scripts/build_html.py" dev/site-src/index.html docs/index.html --extra "範囲に名前を付ける"
python3 - <<'PY'
import re
words = r"(?:Anthropic|anthropic|Claude|公開CSS|research/)"
for p in ["docs/manual.html", "docs/index.html"]:
    s = open(p, encoding="utf-8").read()
    s = re.sub(r"/\*(?:(?!\*/)[\s\S])*?" + words + r"(?:(?!\*/)[\s\S])*?\*/", "", s)
    open(p, "w", encoding="utf-8").write(s)
    left = re.findall(r"(?i)anthropic|claude", s)
    print(p + ": 他社名の残り", len(left), "件")
PY
# 配るマニュアルは, 本文とコードの書体も埋め込み, Webを見にいかないようにする(H9)
uv run tools/embed-fonts.py docs/manual.html
