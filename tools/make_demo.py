# /// script
# requires-python = ">=3.10"
# dependencies = ["pillow>=10"]
# ///
"""READMEとマニュアル用の紹介GIF(イメージ図)と静止画を作る.

出力: demo.gif(README), overview.png, adjust.png, list.png(マニュアル docs/index.html).

実際の画面は撮らず, 架空のデスクトップの上に, 選択画面を
src/OverlayForm.cs と src/Theme.cs と同じ色・大きさで描く.
選択画面の見た目を変えたら, ここの定数もそろえる.

使い方:
    uv run tools/make_demo.py [--font NotoSansJP.ttf] [--out docs/images]

フォントは Noto Sans JP(可変フォント, OFL)を使う. --font がなければ
~/.cache/wakudori/NotoSansJP.ttf を探し, なければ Google Fonts から取ってくる.
"""

from __future__ import annotations

import argparse
import math
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT_URL = "https://github.com/google/fonts/raw/main/ofl/notosansjp/NotoSansJP%5Bwght%5D.ttf"

SS = 2  # 2倍で描いてから縮める(角丸をなめらかにするため)
W, H = 880, 520  # 画面
CAP = 56  # 下の説明の帯

# ---- src/Theme.cs ----
BG = (0xFA, 0xF9, 0xF5)
BG_SUBTLE = (0xF0, 0xEE, 0xE6)
BG_MUTED = (0xE8, 0xE6, 0xDC)
TEXT = (0x14, 0x14, 0x13)
TEXT2 = (0x3D, 0x3D, 0x3A)
TEXT3 = (0x5E, 0x5D, 0x59)
BORDER = (0xD1, 0xCF, 0xC5)
ACCENT = (0xD9, 0x77, 0x57)
ACCENT_I = (0xB8, 0x5A, 0x39)

# ---- src/OverlayForm.cs ----
DIM = (0x14, 0x14, 0x13, 140)
LINE = (0xFA, 0xF9, 0xF5, 242)
KNOB = (0xFA, 0xF9, 0xF5)
KNOB_HOVER_ALPHA = 115
KNOB_THICK, KNOB_LEN, KNOB_GAP = 6, 64, 2
CHIP_H, CHIP_PAD, CHIP_GAP, CHIP_TOP, CHIP_FONT = 32, 14, 8, 24, 14
BADGE, DELETE_ZONE = 18, 24

# 保存した範囲(架空). x, y, 幅, 高さ(画面の座標)
REGIONS = [
    ("売上グラフ", (216, 180, 376, 278)),
    ("KPI", (216, 96, 608, 84)),
    ("チャネル別", (588, 180, 236, 278)),
]
MORE = 2  # 「+N」にまとめた数(架空)


class Fonts:
    def __init__(self, path: Path):
        self.path = path
        self.cache: dict[tuple[int, str], ImageFont.FreeTypeFont] = {}

    def get(self, size: float, weight: str = "Regular") -> ImageFont.FreeTypeFont:
        key = (round(size * SS), weight)
        if key not in self.cache:
            f = ImageFont.truetype(str(self.path), key[0])
            try:
                f.set_variation_by_name(weight)
            except Exception:
                pass
            self.cache[key] = f
        return self.cache[key]


def s(v: float) -> int:
    return int(round(v * SS))


def box(x, y, w, h):
    return [s(x), s(y), s(x + w) - 1, s(y + h) - 1]


def layer(img: Image.Image, fn):
    """透明な面に描いてから重ねる(半透明を正しく混ぜるため)."""
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    fn(ImageDraw.Draw(lay))
    img.alpha_composite(lay)


# ---------------- 架空のデスクトップ ----------------

SKY = (0x6A, 0x9B, 0xCC)
OLIVE = (0x78, 0x8C, 0x5D)
CLAY = (0xD9, 0x77, 0x57)
SAND = (0xE3, 0xC5, 0x9A)
WIN = (0x2F, 0x2E, 0x2B)  # ウィンドウの文字

WIN_RECT = (48, 22, 784, 446)  # アプリのウィンドウ


def draw_wallpaper() -> Image.Image:
    """暖色の地に, 色の丸を大きくぼかした壁紙."""
    q = 4  # 小さく描いてぼかし, 拡大する(速くするため)
    small = Image.new("RGB", (W // q, H // q), (0xF2, 0xE8, 0xDA))
    d = ImageDraw.Draw(small)
    blobs = [
        (CLAY, (90, 430), 250),
        ((0xE8, 0x9F, 0x7E), (330, 520), 200),
        (SKY, (820, 70), 230),
        ((0xA9, 0xC4, 0xDE), (560, -40), 200),
        (OLIVE, (760, 500), 190),
        (SAND, (300, 120), 220),
    ]
    for color, (cx, cy), r in blobs:
        d.ellipse([(cx - r) / q, (cy - r) / q, (cx + r) / q, (cy + r) / q], fill=color)
    small = small.filter(ImageFilter.GaussianBlur(120 / q))
    return small.resize((s(W), s(H)), Image.BICUBIC).convert("RGBA")


def shadow(img: Image.Image, rect, radius, alpha=70, blur=16, dy=8):
    x, y, w, h = rect
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle(box(x, y + dy, w, h), radius=s(radius), fill=(0x14, 0x10, 0x0C, alpha))
    img.alpha_composite(lay.filter(ImageFilter.GaussianBlur(s(blur))))


def card(img: Image.Image, rect, radius=10):
    shadow(img, rect, radius, alpha=22, blur=6, dy=2)
    ImageDraw.Draw(img).rounded_rectangle(box(*rect), radius=s(radius), fill=(255, 255, 255), outline=(0xEC, 0xEA, 0xE3), width=SS)


def draw_taskbar(img: Image.Image, fonts: Fonts):
    """Windows 11風の半透明のタスクバー(後ろの壁紙をぼかして透かす)."""
    y0 = H - 36
    region = img.crop((0, s(y0), s(W), s(H))).filter(ImageFilter.GaussianBlur(s(18)))
    img.paste(region, (0, s(y0)))
    layer(img, lambda d: d.rectangle(box(0, y0, W, 36), fill=(0xFB, 0xF8, 0xF3, 185)))
    d = ImageDraw.Draw(img)
    d.line([(0, s(y0)), (s(W), s(y0))], fill=(255, 255, 255, 255), width=SS)
    x0 = W / 2 - (6 * 32 - 12) / 2
    # スタート(4つの四角)
    for k, (ox, oy) in enumerate([(0, 0), (9, 0), (0, 9), (9, 9)]):
        d.rounded_rectangle(box(x0 + 1 + ox, H - 27 + oy, 8, 8), radius=s(1.5), fill=SKY)
    apps = [(0x9A, 0x98, 0x90), (SKY), (CLAY), (OLIVE), (0xA3, 0x9A, 0xB8)]
    for i, c in enumerate(apps):
        x = x0 + (i + 1) * 32
        d.rounded_rectangle(box(x, H - 28, 20, 20), radius=s(5), fill=c)
    # 動いているアプリ(ダッシュボード)の下の印
    d.rounded_rectangle(box(x0 + 2 * 32 + 6, H - 5, 8, 3), radius=s(1.5), fill=SKY)
    # トレイ: Wakudoriのアイコン(クレイ色の四角に枠とつまみ)と時計
    tx = W - 98
    d.rounded_rectangle(box(tx, H - 27, 18, 18), radius=s(4), fill=ACCENT)
    d.rectangle(box(tx + 4, H - 23, 10, 10), outline=BG, width=SS)
    d.rounded_rectangle(box(tx + 13, H - 21, 2.5, 6), radius=s(1), fill=BG)
    d.text((s(W - 22), s(H - 24)), "10:24", font=fonts.get(10), fill=WIN, anchor="rm")
    d.text((s(W - 22), s(H - 12)), "2026/11/20", font=fonts.get(9), fill=WIN, anchor="rm")


def draw_desktop(fonts: Fonts) -> Image.Image:
    img = draw_wallpaper()
    wx, wy, ww, wh = WIN_RECT
    shadow(img, WIN_RECT, 10, alpha=80, blur=20, dy=10)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle(box(*WIN_RECT), radius=s(10), fill=(0xFB, 0xFA, 0xF7), outline=(0xE2, 0xDF, 0xD6), width=SS)

    # タイトルバー
    d.rounded_rectangle(box(wx + 14, wy + 11, 12, 12), radius=s(3), fill=SKY)
    d.text((s(wx + 34), s(wy + 17)), "月次ダッシュボード", font=fonts.get(11), fill=WIN, anchor="lm")
    bx = wx + ww - 22
    d.line([(s(bx - 4), s(wy + 13)), (s(bx + 4), s(wy + 21))], fill=WIN, width=SS)
    d.line([(s(bx - 4), s(wy + 21)), (s(bx + 4), s(wy + 13))], fill=WIN, width=SS)
    d.rectangle(box(bx - 50, wy + 13, 8, 8), outline=WIN, width=SS)
    d.line([(s(bx - 96), s(wy + 17)), (s(bx - 88), s(wy + 17))], fill=WIN, width=SS)

    # 左のメニュー
    sx, sy, sw = wx, wy + 34, 152
    d.rectangle(box(sx + 1, sy, sw, wh - 35), fill=(0xF4, 0xF2, 0xEC))
    d.rounded_rectangle(box(sx + 1, wy + wh - 12, sw, 11), radius=s(10), fill=(0xF4, 0xF2, 0xEC))
    d.line([(s(sx + sw), s(sy)), (s(sx + sw), s(wy + wh - 1))], fill=(0xE6, 0xE3, 0xDB), width=SS)
    nav = ["概要", "売上", "顧客", "在庫", "レポート", "設定"]
    for i, name in enumerate(nav):
        y = sy + 16 + i * 34
        if i == 0:
            d.rounded_rectangle(box(sx + 10, y, sw - 20, 28), radius=s(7), fill=(0xF6, 0xE2, 0xD8))
        d.rounded_rectangle(box(sx + 22, y + 9, 10, 10), radius=s(3), fill=CLAY if i == 0 else (0xB9, 0xB5, 0xAA))
        d.text((s(sx + 42), s(y + 14)), name, font=fonts.get(12, "Medium" if i == 0 else "Regular"),
               fill=(0xA9, 0x47, 0x2A) if i == 0 else WIN, anchor="lm")

    # 中身
    cx = sx + sw + 24  # 224
    d.text((s(cx), s(wy + 60)), "11月の概要", font=fonts.get(18, "Bold"), fill=TEXT, anchor="lm")
    d.rounded_rectangle(box(wx + ww - 140, wy + 48, 116, 24), radius=s(12), fill=(255, 255, 255), outline=(0xE2, 0xDF, 0xD6), width=SS)
    d.text((s(wx + ww - 82), s(wy + 60)), "2026年11月", font=fonts.get(11), fill=WIN, anchor="mm")

    # KPIのカード
    kpis = [("売上", "¥12.4M", "+8.2%", OLIVE, [3, 4, 3.5, 5, 4.6, 6, 6.8]),
            ("注文数", "3,218", "+4.1%", OLIVE, [4, 3.6, 4.4, 4.2, 5, 4.8, 5.4]),
            ("平均単価", "¥3,860", "−1.3%", CLAY, [5.6, 5.2, 5.4, 4.9, 5, 4.6, 4.5])]
    kw = (592 - 24) / 3
    for i, (label, value, delta, color, spark) in enumerate(kpis):
        x, y = cx + i * (kw + 12), wy + 82
        card(img, (x, y, kw, 70))
        d = ImageDraw.Draw(img)
        d.text((s(x + 14), s(y + 18)), label, font=fonts.get(11), fill=TEXT3, anchor="lm")
        d.text((s(x + 14), s(y + 46)), value, font=fonts.get(20, "Bold"), fill=TEXT, anchor="lm")
        d.text((s(x + 14 + fonts.get(20, "Bold").getlength(value) / SS + 8), s(y + 48)), delta,
               font=fonts.get(11, "Medium"), fill=color, anchor="lm")
        pts = [(x + kw - 74 + k * 10, y + 52 - v * 4.5) for k, v in enumerate(spark)]
        d.line([(s(px), s(py)) for px, py in pts], fill=color, width=s(1.6), joint="curve")

    # 月別の売上(面グラフ)
    gx, gy, gw, gh = cx, wy + 166, 360, 262
    card(img, (gx, gy, gw, gh))
    d = ImageDraw.Draw(img)
    d.text((s(gx + 16), s(gy + 20)), "月別の売上", font=fonts.get(13, "Bold"), fill=TEXT, anchor="lm")
    d.ellipse(box(gx + gw - 112, gy + 16, 8, 8), fill=SKY)
    d.text((s(gx + gw - 100), s(gy + 20)), "今年", font=fonts.get(10), fill=TEXT3, anchor="lm")
    d.ellipse(box(gx + gw - 62, gy + 16, 8, 8), fill=(0xC9, 0xC5, 0xBA))
    d.text((s(gx + gw - 50), s(gy + 20)), "前年", font=fonts.get(10), fill=TEXT3, anchor="lm")
    px0, px1, py0, py1 = gx + 40, gx + gw - 18, gy + 48, gy + gh - 34
    for k in range(5):
        y = py0 + (py1 - py0) * k / 4
        d.line([(s(px0), s(y)), (s(px1), s(y))], fill=(0xEF, 0xED, 0xE7), width=SS)
        d.text((s(px0 - 8), s(y)), f"{(4 - k) * 4}M", font=fonts.get(9), fill=TEXT3, anchor="rm")
    months = ["6月", "7月", "8月", "9月", "10月", "11月"]
    this = [0.42, 0.55, 0.50, 0.66, 0.73, 0.86]
    last = [0.38, 0.44, 0.47, 0.52, 0.55, 0.62]

    def curve(vals):
        pts = []
        n = len(vals) - 1
        for k in range(n * 16 + 1):
            t = k / 16
            i = min(int(t), n - 1)
            f = t - i
            f = (1 - math.cos(math.pi * f)) / 2
            v = vals[i] + (vals[i + 1] - vals[i]) * f
            pts.append((px0 + (px1 - px0) * t / n, py1 - (py1 - py0) * v))
        return pts

    for i, m in enumerate(months):
        d.text((s(px0 + (px1 - px0) * i / 5), s(py1 + 14)), m, font=fonts.get(9), fill=TEXT3, anchor="mm")
    pts_last = curve(last)
    d.line([(s(x), s(y)) for x, y in pts_last], fill=(0xC9, 0xC5, 0xBA), width=s(1.6))
    pts = curve(this)
    # 面: 上から下へ薄くなる空色
    grad = Image.new("RGBA", img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(s(py0), s(py1) + 1):
        a = int(110 * (1 - (y - s(py0)) / max(1, s(py1) - s(py0))))
        gd.line([(s(px0), y), (s(px1), y)], fill=SKY + (a,))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon([(s(x), s(y)) for x, y in pts] + [(s(px1), s(py1)), (s(px0), s(py1))], fill=255)
    area = Image.new("RGBA", img.size, (0, 0, 0, 0))
    area.paste(grad, (0, 0), mask)
    img.alpha_composite(area)
    d = ImageDraw.Draw(img)
    d.line([(s(x), s(y)) for x, y in pts], fill=SKY, width=s(2.2), joint="curve")
    lx, ly = pts[-1]
    d.ellipse(box(lx - 4.5, ly - 4.5, 9, 9), fill=(255, 255, 255), outline=SKY, width=s(2))

    # チャネル別(ドーナツ)
    nx, ny, nw, nh = gx + gw + 12, gy, 592 - gw - 12, gh
    card(img, (nx, ny, nw, nh))
    d = ImageDraw.Draw(img)
    d.text((s(nx + 16), s(ny + 20)), "チャネル別", font=fonts.get(13, "Bold"), fill=TEXT, anchor="lm")
    r, ocx, ocy = 54, nx + nw / 2, ny + 104
    segs = [(0.62, CLAY, "オンライン"), (0.26, SKY, "店舗"), (0.12, OLIVE, "法人")]
    a0 = -90
    for frac, color, _ in segs:
        a1 = a0 + 360 * frac
        d.pieslice(box(ocx - r, ocy - r, 2 * r, 2 * r), a0 + 1, a1 - 1, fill=color)
        a0 = a1
    d.ellipse(box(ocx - r + 16, ocy - r + 16, 2 * r - 32, 2 * r - 32), fill=(255, 255, 255))
    d.text((s(ocx), s(ocy - 6)), "62%", font=fonts.get(16, "Bold"), fill=TEXT, anchor="mm")
    d.text((s(ocx), s(ocy + 13)), "オンライン", font=fonts.get(9), fill=TEXT3, anchor="mm")
    for i, (frac, color, name) in enumerate(segs):
        y = ny + 182 + i * 22
        d.rounded_rectangle(box(nx + 20, y - 4, 8, 8), radius=s(2), fill=color)
        d.text((s(nx + 36), s(y)), name, font=fonts.get(11), fill=WIN, anchor="lm")
        d.text((s(nx + nw - 18), s(y)), f"{round(frac * 100)}%", font=fonts.get(11, "Medium"), fill=TEXT, anchor="rm")

    draw_taskbar(img, fonts)
    return img


# ---------------- 選択画面 ----------------

def chip_layout(fonts: Fonts):
    font = fonts.get(CHIP_FONT)
    items = []
    for i, (name, _) in enumerate(REGIONS):
        tw = font.getlength(name) / SS + 6
        items.append(CHIP_PAD + BADGE + 6 + tw + DELETE_ZONE)
    more_w = font.getlength(f"+{MORE}") / SS + CHIP_PAD * 2
    total = sum(items) + more_w + CHIP_GAP * len(items)
    x = W / 2 - total / 2
    rects = []
    for w in items:
        rects.append((x, CHIP_TOP, w, CHIP_H))
        x += w + CHIP_GAP
    return rects, (x, CHIP_TOP, more_w, CHIP_H)


def draw_overlay(img: Image.Image, fonts: Fonts, frame, hover_knob, sel_knob, hot_chip, active_chip):
    fx, fy, fw, fh = frame

    def dim(d):
        d.rectangle([0, 0, s(W), s(H)], fill=DIM)
        d.rectangle(box(fx, fy, fw, fh), fill=(0, 0, 0, 0))
        w = 1
        d.rectangle(box(fx - w, fy - w, fw + 2 * w, w), fill=LINE)
        d.rectangle(box(fx - w, fy + fh, fw + 2 * w, w), fill=LINE)
        d.rectangle(box(fx - w, fy, w, fh), fill=LINE)
        d.rectangle(box(fx + fw, fy, w, fh), fill=LINE)

    layer(img, dim)

    def knob(edge, alpha):
        th, o = KNOB_THICK, 1 + KNOB_GAP
        color = KNOB + (alpha,)

        def fn(d):
            if edge in ("left", "right"):
                length = max(th, min(KNOB_LEN, int(fh * 0.4)))
                x = fx - o - th if edge == "left" else fx + fw + o
                d.rounded_rectangle(box(x, fy + (fh - length) / 2, th, length), radius=s(th / 2), fill=color)
            else:
                length = max(th, min(KNOB_LEN, int(fw * 0.4)))
                y = fy - o - th if edge == "top" else fy + fh + o
                d.rounded_rectangle(box(fx + (fw - length) / 2, y, length, th), radius=s(th / 2), fill=color)

        layer(img, fn)

    if hover_knob and hover_knob != sel_knob:
        knob(hover_knob, KNOB_HOVER_ALPHA)
    if sel_knob:
        knob(sel_knob, 255)

    rects, more = chip_layout(fonts)
    font, badge_font = fonts.get(CHIP_FONT), fonts.get(12, "Bold")

    def chips(d):
        for i, (rc, (name, _)) in enumerate(zip(rects, REGIONS)):
            active = i == active_chip
            back = ACCENT_I if active else (BG_MUTED if i == hot_chip else BG)
            fore = (255, 255, 255) if active else TEXT
            x, y, w, h = rc
            d.rounded_rectangle(box(x, y, w, h), radius=s(h / 2), fill=back)
            bx = x + CHIP_PAD
            badge_bg = mix(back, (255, 255, 255), 64 / 255) if active else mix(back, TEXT, 26 / 255)
            by = y + (h - BADGE) / 2
            d.rounded_rectangle(box(bx, by, BADGE, BADGE), radius=s(4), fill=badge_bg)
            d.text((s(bx + BADGE / 2), s(by + BADGE / 2)), str(i + 1), font=badge_font, fill=fore, anchor="mm")
            d.text((s(bx + BADGE + 6), s(y + h / 2)), name, font=font, fill=fore, anchor="lm")
        x, y, w, h = more
        d.rounded_rectangle(box(x, y, w, h), radius=s(h / 2), fill=BG + (220,))
        d.text((s(x + w / 2), s(y + h / 2)), f"+{MORE}", font=font, fill=TEXT, anchor="mm")

    layer(img, chips)


def mix(a, b, t):
    return tuple(int(round(x + (y - x) * t)) for x, y in zip(a, b))


# ---------------- カーソルと注記 ----------------

ARROW = [(0, 0), (0, 17), (4.2, 13.2), (7, 19.5), (9.6, 18.4), (6.9, 12.3), (12.2, 12.3)]


def draw_cursor(img: Image.Image, pos, kind):
    x, y = pos

    def fn(d):
        if kind == "ew":
            # 左右の両矢印(辺をつかめるときの形)
            pts = [(-10, 0), (-5, -5), (-5, -2), (5, -2), (5, -5), (10, 0), (5, 5), (5, 2), (-5, 2), (-5, 5)]
            d.polygon([(s(x + px), s(y + py)) for px, py in pts], fill=(0, 0, 0), outline=(255, 255, 255), width=SS)
        else:
            d.polygon([(s(x + px), s(y + py)) for px, py in ARROW], fill=(0, 0, 0), outline=(255, 255, 255), width=SS)

    layer(img, fn)


def draw_keycap(img: Image.Image, fonts: Fonts, label: str, pressed: bool):
    font = fonts.get(15, "Medium")
    w = font.getlength(label) / SS + 36
    x, y, h = W / 2 - w / 2, H - 100, 38
    off = 2 if pressed else 0

    def fn(d):
        d.rounded_rectangle(box(x, y + 3, w, h), radius=s(8), fill=BORDER)
        d.rounded_rectangle(box(x, y + off, w, h), radius=s(8), fill=(255, 255, 255), outline=BORDER, width=SS)
        d.text((s(x + w / 2), s(y + off + h / 2)), label, font=font, fill=TEXT, anchor="mm")

    layer(img, fn)


def draw_caption(img: Image.Image, fonts: Fonts, step: int, text: str):
    d = ImageDraw.Draw(img)
    y0 = H
    d.rectangle(box(0, y0, W, CAP), fill=BG)
    d.line([(0, s(y0)), (s(W), s(y0))], fill=BG_MUTED, width=SS)
    if step:
        d.ellipse(box(20, y0 + CAP / 2 - 12, 24, 24), fill=ACCENT_I)
        d.text((s(32), s(y0 + CAP / 2)), str(step), font=fonts.get(13, "Bold"), fill=(255, 255, 255), anchor="mm")
    d.text((s(56), s(y0 + CAP / 2)), text, font=fonts.get(15, "Medium"), fill=TEXT, anchor="lm")
    d.text((s(W - 20), s(y0 + CAP / 2)), "イメージ(架空の画面)", font=fonts.get(11), fill=TEXT3, anchor="rm")


# ---------------- 筋書き ----------------

def ease(t: float) -> float:
    return 0.5 - math.cos(math.pi * max(0.0, min(1.0, t))) / 2


def lerp(a, b, t):
    if isinstance(a, tuple):
        return tuple(lerp(x, y, t) for x, y in zip(a, b))
    return a + (b - a) * t


def storyboard(fonts: Fonts):
    """(状態, 表示する時間ms)を順に返す."""
    rects, _ = chip_layout(fonts)
    chip1 = rects[0]
    chip1_pos = (chip1[0] + chip1[2] * 0.55, chip1[1] + chip1[3] * 0.6)
    start = (588, 180, 172, 278)  # 前回の範囲(右が足りない)
    adjusted = REGIONS[2][1]
    graph = REGIONS[0][1]
    home = (470, 330)
    edge_y = 300
    edge_pos = (start[0] + start[2] + 1, edge_y)

    cap1 = (1, "ホットキーで選択画面を開く. 前回の範囲がそのまま出る")
    cap2 = (2, "辺をドラッグして大きさを調整")
    cap3 = (3, "保存した名前に乗せると下見, クリックで呼び出す")
    cap4 = (4, "Enterで撮影. クリップボードとPNGファイルに出る")

    base = dict(overlay=False, frame=start, hover=None, sel=None, hot=None, active=None,
                cursor=home, ckind="arrow", key=None, cap=cap1)

    def st(**kw):
        d = dict(base)
        d.update(kw)
        return d

    out = []
    out.append((st(), 900))
    out.append((st(key="Ctrl+Shift+S"), 500))
    out.append((st(key="Ctrl+Shift+S!"), 250))
    out.append((st(overlay=True), 1600))

    n = 10
    for i in range(1, n + 1):
        out.append((st(overlay=True, cap=cap2, cursor=lerp(home, edge_pos, ease(i / n))), 50))
    out.append((st(overlay=True, cap=cap2, cursor=edge_pos, ckind="ew", hover="right"), 600))
    n = 12
    for i in range(1, n + 1):
        f = lerp(start, adjusted, ease(i / n))
        f = tuple(int(round(v)) for v in f)
        out.append((st(overlay=True, cap=cap2, frame=f, cursor=(f[0] + f[2] + 1, edge_y), ckind="ew", sel="right"), 50))
    end_edge = (adjusted[0] + adjusted[2] + 1, edge_y)
    out.append((st(overlay=True, cap=cap2, frame=adjusted, cursor=end_edge, ckind="ew", sel="right"), 1100))

    n = 12
    for i in range(1, n + 1):
        out.append((st(overlay=True, cap=cap3, frame=adjusted, sel="right", cursor=lerp(end_edge, chip1_pos, ease(i / n))), 50))
    out.append((st(overlay=True, cap=cap3, frame=graph, hot=0, cursor=chip1_pos), 1300))
    out.append((st(overlay=True, cap=cap3, frame=graph, active=0, cursor=chip1_pos), 1200))

    out.append((st(overlay=True, cap=cap4, frame=graph, active=0, cursor=chip1_pos, key="Enter"), 500))
    out.append((st(overlay=True, cap=cap4, frame=graph, active=0, cursor=chip1_pos, key="Enter!"), 250))
    out.append((st(cap=cap4, cursor=chip1_pos), 2400))
    return out


def render(fonts: Fonts, desktop: Image.Image, state) -> Image.Image:
    img = Image.new("RGBA", (s(W), s(H + CAP)), BG + (255,))
    screen = desktop.copy()
    if state["overlay"]:
        draw_overlay(screen, fonts, state["frame"], state["hover"], state["sel"], state["hot"], state["active"])
    draw_cursor(screen, state["cursor"], state["ckind"])
    if state["key"]:
        draw_keycap(screen, fonts, state["key"].rstrip("!"), state["key"].endswith("!"))
    img.paste(screen, (0, 0))
    draw_caption(img, fonts, *state["cap"])
    return img.resize((W, H + CAP), Image.LANCZOS).convert("RGB")


def find_font(arg: str | None) -> Path:
    if arg:
        return Path(arg)
    cached = Path.home() / ".cache" / "wakudori" / "NotoSansJP.ttf"
    if not cached.exists():
        cached.parent.mkdir(parents=True, exist_ok=True)
        print(f"フォントを取得: {FONT_URL}")
        urllib.request.urlretrieve(FONT_URL, cached)
    return cached


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font")
    ap.add_argument("--out", default=str(Path(__file__).resolve().parent.parent / "docs" / "images"))
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    fonts = Fonts(find_font(args.font))
    desktop = draw_desktop(fonts)
    board = storyboard(fonts)
    frames = [render(fonts, desktop, st) for st, _ in board]
    durations = [ms for _, ms in board]

    # 全コマで同じパレットを使う(コマごとに色がちらつかないように)
    sheet = Image.new("RGB", (W, (H + CAP) * 4))
    for k, idx in enumerate([0, 2, len(frames) // 2, len(frames) - 4]):
        sheet.paste(frames[idx], (0, (H + CAP) * k))
    palette = sheet.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    pframes = [f.quantize(palette=palette, dither=Image.Dither.FLOYDSTEINBERG) for f in frames]
    gif = out / "demo.gif"
    pframes[0].save(gif, save_all=True, append_images=pframes[1:], duration=durations, loop=0, optimize=True)

    # 静止画(READMEの先頭やマニュアル用): 名前を呼び出したところ
    still_index = next(i for i, (st, _) in enumerate(board) if st["active"] == 0 and not st["key"])
    frames[still_index].save(out / "overview.png", optimize=True)

    # マニュアル用: 辺を選んでつまみが出たところ
    adjust_index = max(i for i, (st, _) in enumerate(board) if st["sel"] == "right" and st["cap"][0] == 2)
    frames[adjust_index].save(out / "adjust.png", optimize=True)

    # マニュアル用: 一覧(チップ)の部分だけ. 名前に乗せて下見しているところ
    hover_index = next(i for i, (st, _) in enumerate(board) if st["hot"] == 0)
    rects, more = chip_layout(fonts)
    left, right = rects[0][0] - 24, more[0] + more[2] + 24
    frames[hover_index].crop((int(left), 4, int(right), CHIP_TOP + CHIP_H + 20)).save(out / "list.png", optimize=True)

    print(f"{gif} ({gif.stat().st_size // 1024} KB, {len(frames)}コマ)")
    for name in ("overview.png", "adjust.png", "list.png"):
        print(out / name)


if __name__ == "__main__":
    main()
