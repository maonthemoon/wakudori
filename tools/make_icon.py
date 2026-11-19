# /// script
# requires-python = ">=3.9"
# dependencies = ["pillow"]
# ///
"""Wakudoriのアイコン(app.ico)を作る.

案G「枠とつまみ」: クレイ色の角丸の四角に, アイボリーの細い枠と, 右の辺の太いつまみ.
座標は64×64の格子で決め, 大きさごとに8倍で描いてから縮める(なめらかにするため).
同じ絵をsrc/Theme.csのCreateAppIconHandleでも描いている(タスクトレイ用). 変えるときは両方直す.

使い方: uv run tools/make_icon.py  (リポジトリの直下にapp.icoができる)
"""
from pathlib import Path

from PIL import Image, ImageDraw

CLAY = (0xD9, 0x77, 0x57, 255)
IVORY = (0xFA, 0xF9, 0xF5, 255)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # 縮める前の倍率


def draw(size: int) -> Image.Image:
    n = size * SS
    k = n / 64.0
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 地: クレイ色の角丸の四角
    d.rounded_rectangle([2 * k, 2 * k, 62 * k, 62 * k], radius=14 * k, fill=CLAY)
    # 細い枠. 小さいアイコンでは1px未満にならないようにする
    frame_w = max(3 * k, SS * 1.0)
    d.rounded_rectangle([14 * k, 16 * k, 46 * k, 48 * k], radius=2 * k, outline=IVORY, width=round(frame_w))
    # 右の辺のつまみ(両端は丸く). 小さいアイコンでは2px未満にならないようにする
    knob_w = max(6 * k, SS * 2.0)
    x = 53 * k
    y1, y2 = 25 * k, 39 * k
    r = knob_w / 2
    d.rectangle([x - r, y1, x + r, y2], fill=IVORY)
    d.ellipse([x - r, y1 - r, x + r, y1 + r], fill=IVORY)
    d.ellipse([x - r, y2 - r, x + r, y2 + r], fill=IVORY)
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = Path(__file__).resolve().parent.parent / "app.ico"
    images = [draw(s) for s in SIZES]
    images[-1].save(out, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print(f"{out} ({', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    main()
