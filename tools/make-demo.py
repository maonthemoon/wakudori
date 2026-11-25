# /// script
# requires-python = ">=3.10"
# dependencies = ["playwright>=1.48", "pillow>=10"]
# ///
"""READMEの動く画像(docs/images/demo-*.webp)を, 紹介のページ(docs/index.html)の動くデモから書き出す.

READMEではJavaScriptが動かないので, デモのコマを撮って動くWebPにする.
ライト用とダーク用, 広い幅用と狭い幅用の4枚. READMEの<picture>が, 見る人のブラウザーに合わせて選ぶ.
ページの時計を50msずつ進めて撮るので, PCの速さに左右されない.

使い方: uv run tools/make-demo.py [light|dark|narrow-light|narrow-dark ...]
(初めてのときは uv run --with playwright playwright install chromium)
"""
import fcntl
import io
import sys
from contextlib import contextmanager
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "images"
STEP = 50  # ms
PAD = 16
VARIANTS = {  # 名前: (テーマ, 画面の幅, 解像度)
    "light": ("light", 900, 2), "dark": ("dark", 900, 2),
    "narrow-light": ("light", 412, 2), "narrow-dark": ("dark", 412, 2),
}
STATE = "() => { const r = document.querySelector('.demo'), o = []; for (const s of ['.ov', '.nf', '.opened', '.cursor', '.keycap', '.demo-tabs', '.demo-caption']) { const e = r.querySelector(s); o.push(e.hidden, e.className, e.style.cssText, e.innerHTML); } return [r.dataset.scene, o.join('|')]; }"

LOCK = Path.home() / ".cache" / "make-demo.lock"  # wakudoriとhikidashiで共通


class Frames:
    """PNGのまま持ち, 保存のときに1コマずつ展開する(全部展開するとメモリを2GB以上使うため)."""

    def __init__(self, pngs):
        self.pngs, self.n_frames = pngs, len(pngs)

    def seek(self, i):
        self.image = open_png(self.pngs[i])

    def __getattr__(self, name):
        if name == "image":
            raise AttributeError(name)
        return getattr(self.image, name)


def open_png(png):
    return Image.open(io.BytesIO(png)).convert("RGB")


@contextmanager
def lock():
    """同時に1つだけ生成する(重なるとRaspberry Piのメモリが足りなくなるため). プロセスが終われば自動で外れる."""
    LOCK.parent.mkdir(parents=True, exist_ok=True)
    with open(LOCK, "w") as f:
        try:
            fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print("ほかの生成が終わるのを待っています", file=sys.stderr, flush=True)
            fcntl.flock(f, fcntl.LOCK_EX)
        yield


def record(browser, name):
    theme, width, dpr = VARIANTS[name]
    pg = browser.new_page(viewport={"width": width, "height": 1100}, device_scale_factor=dpr, color_scheme=theme, reduced_motion="no-preference")
    pg.clock.install()
    pg.goto((ROOT / "docs" / "index.html").as_uri())
    pg.wait_for_load_state('networkidle')  # 書体の読み込み
    pg.clock.pause_at(pg.evaluate('Date.now()') / 1000 + 5)  # ここから先, 時計はrun_forの分だけ進む
    # デモだけを見せる. 場面が変わっても大きさが変わらないよう, 説明の高さはいちばん長いものに合わせる
    pg.add_style_tag(content=f"body > *:not(main), main > *:not(:has(.demo)), .skip-link {{ display: none !important; }} main .container {{ padding: {PAD}px !important; margin: 0 !important; max-width: none !important; }} .demo {{ padding: 0 !important; }} .caret {{ animation: none !important; }} *, *::before, *::after {{ transition: none !important; }}")
    pg.evaluate("""() => { const c = document.querySelector('.demo-caption'), tabs = [...document.querySelectorAll('.demo-tab')]; let h = 0;
      for (const t of tabs) { t.click(); h = Math.max(h, c.scrollHeight); } c.style.minHeight = h + 'px'; scrollTo(0, 0); tabs[0].click(); }""")
    clip = pg.evaluate("() => { const b = document.querySelector('.demo').parentElement.getBoundingClientRect(); return { x: 0, y: 0, width: innerWidth, height: Math.ceil(b.bottom) }; }")
    frames, durations, last, seen_last = [], [], None, False
    while True:
        scene, state = pg.evaluate(STATE)
        if scene == "manage":
            seen_last = True
        elif seen_last:
            break  # 最初の場面に戻った
        if state != last:
            frames.append(pg.screenshot(clip=clip))
            durations.append(STEP)
            last = state
        else:
            durations[-1] += STEP
        pg.clock.run_for(STEP)
    pg.close()
    path = OUT / f"demo-{name}.webp"
    first = open_png(frames[0])
    first.save(path, save_all=True, append_images=[Frames(frames[1:])], duration=durations, loop=0, quality=78, method=4, minimize_size=False, kmin=9, kmax=40)
    print(f"{path.name}: {first.size}, {len(frames)}コマ, {sum(durations) / 1000:.1f}秒, {path.stat().st_size / 1e6:.1f}MB", flush=True)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for n in sys.argv[1:] or list(VARIANTS):
        with lock(), sync_playwright() as p:
            b = p.chromium.launch()
            record(b, n)
            b.close()
