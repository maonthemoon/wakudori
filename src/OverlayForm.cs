using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Wakudori
{
    internal delegate void RegionChosenHandler(object sender, SavedRegion region);
    internal delegate void RegionMovedHandler(object sender, int from, int to);

    // 1つのモニターを覆う選択画面.
    // 異なるDPIのモニターをまたぐ1枚のウィンドウはずれやすいので, モニターごとに1枚ずつ作る.
    // 枠(Selection)は全フォームで共有し, 枠が変わったら関係するフォームだけ描き直す.
    // 描画はUpdateLayeredWindow(ピクセル単位のアルファ)で行い, 下の画面は動いたまま見える.
    internal sealed class OverlayForm : Form
    {
        private const int DimAlpha = 140;   // 範囲の外の暗さ(0〜255)
        private const int InsideAlpha = 1;  // 範囲の内側. 0にするとクリックが下へ抜けるので1にする
        private const int Grip96 = 5;       // 辺をつかめる幅(96 DPIのとき). 角はこの2倍
        // つまみ(選んだ辺・カーソルを乗せた辺の外側に出す太い線. 角はL字). 96 DPIのときの大きさ
        private const int KnobThick96 = 6;  // 太さ
        private const int KnobLength96 = 64; // 辺のつまみの長さ(短い辺では辺の40%まで)
        private const int KnobCorner96 = 30; // 角のL字の1本の長さ
        private const int KnobGap96 = 2;    // 線とつまみのすき間
        private const int KnobHoverAlpha = 115; // カーソルを乗せただけのときのつまみの濃さ(選んだときは255)

        // 色はanthropic-htmlのライトテーマに合わせる(B12). 黒は暖色の黒(#141413), 線はアイボリー(#FAF9F5).
        internal static readonly Color DimColor = Color.FromArgb(DimAlpha, 0x14, 0x14, 0x13); // 範囲の外
        // 線は範囲のすぐ外側(必ず暗くした側)に引くので, 明るい色の実線ならどんな画面の上でも見える.
        private static readonly Color LineColor = Color.FromArgb(242, 0xFA, 0xF9, 0xF5);
        private static readonly Color KnobColor = Color.FromArgb(255, 0xFA, 0xF9, 0xF5);

        private enum DragMode { None, Move, FrameHandle, ListPress, ListDrag } // ListPress: 名前を押した(まだ動かしていない)

        private readonly int monitorIndex;
        private readonly Rectangle bounds;  // モニターの範囲(仮想スクリーン座標, 物理ピクセル)
        private readonly uint dpi;
        private readonly float scale;
        private readonly Selection selection;

        private bool frameWasVisible;       // 前回の描画で枠がこのモニターにあったか
        private DragMode dragMode = DragMode.None;
        private FrameHandle dragHandle = FrameHandle.None;
        private Point dragStart;
        private Rectangle frameAtDragStart;

        private IntPtr memDC = IntPtr.Zero;
        private IntPtr hBitmap = IntPtr.Zero;
        private IntPtr oldBitmap = IntPtr.Zero;
        private Bitmap surface;             // hBitmapのピクセルを直接指すBitmap(PArgb)
        private IntPtr surfaceBits = IntPtr.Zero;
        private readonly FrozenScreen frozen; // 開いた瞬間に撮った, 止めた絵
        private bool live;                  // trueなら止めた絵を出さず, 動いている画面を透かして見せる
        // 試作: 描画にかかった時間
        public int PresentCount;
        public double PresentTotalMs;
        public double PresentMaxMs;
        public string FirstKey;             // 最初に届いたキーと時刻の報告用
        public event EventHandler FirstKeyArrived;

        public bool AllowClose;             // trueのときだけ閉じられる(Alt+F4で1枚だけ閉じないように)

        public event EventHandler CaptureRequested;
        public event EventHandler CancelRequested;
        public event EventHandler LiveToggleRequested;        // Space(止めた画面と動いている画面の切り替え)
        public event EventHandler SaveRequested;              // Ctrl+S(A3)
        public event RegionChosenHandler RegionChosen;        // 一覧の名前を押した, または数字キー(A4)
        public event RegionChosenHandler RegionHovered;       // 一覧の名前にカーソルが乗った(下見). 離れたらnull
        public event RegionChosenHandler RegionEditRequested;   // 一覧の名前をダブルクリックした(その場で名前を変える)
        public event RegionChosenHandler RegionDeleteRequested; // 一覧の名前の右端の×を押した
        public event RegionMovedHandler RegionMoved;            // 一覧の名前をドラッグして並べ替えた(fromの範囲をtoの位置へ)
        public event EventHandler PreviewCancelRequested;     // 下見をやめて元の枠に戻してほしい(キー操作や枠の操作の前)

        // 保存した範囲の一覧(A4, A7). 数字キーのため全フォームが持つが, 描くのはshowListのフォームだけ
        private readonly IList<SavedRegion> regions;
        private readonly bool showList;
        private List<int> rowItems;         // 1行に並べる範囲の番号
        private List<Rectangle> rowRects;   // その位置(ローカル座標). nullなら作り直す
        private Rectangle moreRect;         // 「+N」のチップ. 入りきるときはEmpty
        private List<int> menuItems = new List<int>(); // 「+N」で開く一覧の範囲の番号
        private bool menuOpen;
        private int menuScroll;             // 一覧の先頭に見えている項目
        private ListHit hover = ListHit.None;
        private int hoveredRegion = -1;     // 下見中の範囲の番号(RegionHoveredを出した相手)
        private int editingRegion = -1;     // その場で名前を変えている範囲の番号
        private int editTextWidth;          // 名前を変えている間の, 入力欄の幅
        private bool editingInMenu;         // 縦の一覧の項目を変えている(幅は変えない)
        private bool editActive;            // 名前を変え始めたとき, そのチップがクレイ色だったか(色はそのまま保つ)
        // 名前のドラッグでの並べ替え
        private int pressRegion = -1;       // 押した名前
        private bool pressFromMenu;
        private Point pressPoint;           // 押した位置(ローカル座標)
        private Point grabOffset;           // 押した位置の, 名前の左上からのずれ
        private Point listDragPos;          // ドラッグ中のカーソル(ローカル座標)
        private int dropBefore = -1;        // この番号の範囲の前に入れる(末尾ならCount). -1は入れない
        private bool dropInRow;             // 入れる場所が1行のほう(falseなら縦の一覧)
        private string editError;           // 名前を変えられない理由. 名前の下に吹き出しで出す

        public OverlayForm(int monitorIndex, Rectangle bounds, uint dpi, Selection selection, IList<SavedRegion> regions, bool showList, FrozenScreen frozen)
        {
            this.monitorIndex = monitorIndex;
            this.bounds = bounds;
            this.dpi = dpi;
            this.scale = dpi / 96f;
            this.selection = selection;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Wakudori";
            Location = bounds.Location;
            Size = bounds.Size;

            this.regions = regions;
            this.showList = showList;
            this.frozen = frozen;
            selection.Changed += OnSelectionChanged;
        }

        public int MonitorIndex { get { return monitorIndex; } }
        public float DpiScale { get { return scale; } }
        public float ChipFontPx { get { return ChipFont96 * scale; } }
        public Rectangle MonitorBounds { get { return bounds; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CreateSurface();
            Present();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // WinFormsが位置や大きさを変えている場合に備えて, 物理ピクセルで置き直す.
            Present();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                // 既定の処理(提案された大きさへの変更)をさせない. 位置と大きさは物理ピクセルで固定する.
                m.Result = IntPtr.Zero;
                if (surface != null) Present();
                return;
            }
            base.WndProc(ref m);
        }

        public void SetLive(bool value)
        {
            if (live == value) return;
            live = value;
            Present();
        }

        // 一覧の中身が変わった(保存, 名前の変更, 削除)ときに呼ぶ.
        public void RefreshRegions()
        {
            rowRects = null;
            hover = ListHit.None;
            hoveredRegion = -1;
            Present();
        }

        private bool HasList
        {
            get { return showList && regions != null && regions.Count > 0; }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            // 一覧があるフォームは, 選んだ名前の色が変わるので描き直す.
            if (HasList)
            {
                Present();
                return;
            }
            // 枠がこのモニターにある, または前回あった(消す必要がある)ときだけ描き直す.
            bool visible = selection.Frame.IntersectsWith(bounds);
            if (visible || frameWasVisible) Present();
        }

        // ---- 描画 ----

        private void CreateSurface()
        {
            if (surface != null) return; // ハンドルが作り直されても, 描画面は作り直さない
            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                memDC = NativeMethods.CreateCompatibleDC(screenDC);
                NativeMethods.BITMAPINFOHEADER bmi = new NativeMethods.BITMAPINFOHEADER();
                bmi.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.biWidth = bounds.Width;
                bmi.biHeight = -bounds.Height; // 上から下へ並ぶDIB
                bmi.biPlanes = 1;
                bmi.biBitCount = 32;
                bmi.biCompression = NativeMethods.BI_RGB;
                IntPtr bits;
                hBitmap = NativeMethods.CreateDIBSection(screenDC, ref bmi, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("CreateDIBSection failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                }
                surfaceBits = bits;
                oldBitmap = NativeMethods.SelectObject(memDC, hBitmap);
                surface = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
                surface.SetResolution(96f, 96f); // フォントの換算をDPIに左右されないようにする(拡大はscaleで行う)
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private int Scaled(int px96)
        {
            return Math.Max(1, (int)Math.Round(px96 * scale));
        }

        private void Render()
        {
            Rectangle frame = selection.Frame;
            bool visible = frame.IntersectsWith(bounds);
            frameWasVisible = visible;
            bool useFrozen = frozen != null && !live;

            if (useFrozen)
            {
                NativeMethods.GdiFlush();
                frozen.CopyTo(surfaceBits, bounds.Width * 4, visible ? ToLocal(frame) : Rectangle.Empty);
            }

            using (Graphics g = Graphics.FromImage(surface))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;

                if (!useFrozen) g.Clear(DimColor);

                if (visible)
                {
                    Rectangle local = ToLocal(frame);
                    if (!useFrozen)
                    {
                        using (SolidBrush inside = new SolidBrush(Color.FromArgb(InsideAlpha, 0, 0, 0)))
                        {
                            g.FillRectangle(inside, local);
                        }
                    }

                    DrawBorder(g, local);
                    DrawKnobs(g, local);
                }

                if (HasList) DrawRegionList(g);

            }
        }

        // 線の太さ. DPIに合わせて太くする(100%で1px, 150%で2px)
        private int BorderWidth
        {
            get { return Scaled(1); }
        }

        // 線はアイボリーの細い実線で, 範囲のすぐ外側の太さw画素に塗る(撮るときは選択画面を消すので画像には入らない).
        // 選んでも, カーソルを乗せても, 線の太さ・位置・色は変えない(変えると辺が動いたように見えるため).
        private void DrawBorder(Graphics g, Rectangle local)
        {
            int w = BorderWidth;
            g.CompositingMode = CompositingMode.SourceOver;
            using (SolidBrush line = new SolidBrush(LineColor))
            {
                // 上下の帯は角の画素を含み, 左右の帯は含まない(同じ画素を2回塗らないため).
                g.FillRectangle(line, local.Left - w, local.Top - w, local.Width + w * 2, w);
                g.FillRectangle(line, local.Left - w, local.Bottom, local.Width + w * 2, w);
                g.FillRectangle(line, local.Left - w, local.Top, w, local.Height);
                g.FillRectangle(line, local.Right, local.Top, w, local.Height);
            }
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // 選んだ辺(角)と, カーソルを乗せた辺(角)の外側に, つまみを出す(B12, 写真アプリのトリミング風).
        // 辺は中央に太い線, 角はL字. 選んだほうは濃く, 乗せただけのほうは半透明.
        // 四隅の丸は出さない. つまみは線の外側にあるので, 撮る範囲の中身は隠さない.
        private void DrawKnobs(Graphics g, Rectangle local)
        {
            FrameHandle sel = selection.SelectedHandle;
            FrameHandle hover = selection.HoverHandle;
            if (sel == FrameHandle.None && hover == FrameHandle.None) return;

            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half; // 塗りの四角を画素の境目にそろえる
            if (hover != FrameHandle.None && hover != sel) DrawKnob(g, local, hover, Color.FromArgb(KnobHoverAlpha, KnobColor));
            if (sel != FrameHandle.None) DrawKnob(g, local, sel, KnobColor);
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        private void DrawKnob(Graphics g, Rectangle local, FrameHandle h, Color color)
        {
            int th = Scaled(KnobThick96);
            int o = BorderWidth + Scaled(KnobGap96); // 範囲の端からつまみまでの距離
            int l = local.Left, t = local.Top, r = local.Right, b = local.Bottom;
            float radius = th / 2f;
            using (SolidBrush brush = new SolidBrush(color))
            {
                if (h == FrameHandle.TopLeft || h == FrameHandle.TopRight || h == FrameHandle.BottomRight || h == FrameHandle.BottomLeft)
                {
                    int len = Scaled(KnobCorner96);
                    int cx = MovesLeft(h) ? l - o - th : r + o;   // 縦の棒の左端
                    int cy = MovesTop(h) ? t - o - th : b + o;    // 横の棒の上端
                    int hx = MovesLeft(h) ? cx : cx + th - len;   // 横の棒の左端
                    int vy = MovesTop(h) ? cy : cy + th - len;    // 縦の棒の上端
                    FillRounded(g, brush, new RectangleF(hx, cy, len, th), radius);
                    FillRounded(g, brush, new RectangleF(cx, vy, th, len), radius);
                    return;
                }
                bool horizontal = h == FrameHandle.Top || h == FrameHandle.Bottom;
                int edge = horizontal ? local.Width : local.Height;
                int knob = Math.Max(th, Math.Min(Scaled(KnobLength96), (int)(edge * 0.4f)));
                if (h == FrameHandle.Top) FillRounded(g, brush, new RectangleF(l + (local.Width - knob) / 2, t - o - th, knob, th), radius);
                if (h == FrameHandle.Bottom) FillRounded(g, brush, new RectangleF(l + (local.Width - knob) / 2, b + o, knob, th), radius);
                if (h == FrameHandle.Left) FillRounded(g, brush, new RectangleF(l - o - th, t + (local.Height - knob) / 2, th, knob), radius);
                if (h == FrameHandle.Right) FillRounded(g, brush, new RectangleF(r + o, t + (local.Height - knob) / 2, th, knob), radius);
            }
        }

        private static void FillRounded(Graphics g, Brush brush, RectangleF rc, float radius)
        {
            using (GraphicsPath path = Theme.RoundedRect(rc, radius))
            {
                g.FillPath(brush, path);
            }
        }

        // ---- 保存した範囲の一覧(A4, A7) ----
        // モニターの上部の中央に, 名前をアイボリーのチップで1行だけ並べる(画面を覆いすぎないように).
        // 入りきらない分は「+N」のチップにまとめ, 押すと縦の一覧が開く. 先頭9個には数字キーの番号を付ける.
        // 今の枠が, 選んだ名前付きの範囲のまま(動かしていない)なら, そのチップをクレイ色にする(D5).

        private const int ChipHeight96 = 32;
        private const int ChipPadX96 = 14;
        private const int ChipGap96 = 8;
        private const int ChipTop96 = 24;
        private const int ChipFont96 = 14;
        private const int BadgeSize96 = 18;   // 数字の四角
        private const int MenuWidth96 = 260;
        private const int MenuItemHeight96 = 32;
        private const int MenuMaxItems = 12;  // これより多ければホイールでスクロール
        private const int DeleteZone96 = 24;  // 名前の右端の, ×を出す当たりの幅

        internal enum ListHitKind { None, Chip, More, MenuItem, Menu, Delete }

        internal struct ListHit
        {
            public ListHitKind Kind;
            public int Region; // Chip, MenuItemのとき範囲の番号
            public static readonly ListHit None = new ListHit();
            public ListHit(ListHitKind kind, int region) { Kind = kind; Region = region; }
            public bool SameAs(ListHit o) { return Kind == o.Kind && Region == o.Region; }
        }

        // チップの幅: 左の余白, 番号, 名前, 右端の×の場所. 名前を変えている間は, 入力欄の幅に合わせて伸び縮みする
        private int ChipWidth(Graphics g, Font font, int i)
        {
            int text = i == editingRegion && !editingInMenu ? editTextWidth : TextWidthOf(regions[i].Name, font);
            int w = Scaled(ChipPadX96) + text + Scaled(DeleteZone96);
            if (i < 9) w += Scaled(BadgeSize96) + Scaled(6);
            return w;
        }

        // 名前の文字の幅. その場で名前を変えるときの入力欄(InlineNameEditor.TextWidth)と同じ測り方にして,
        // 入力を始めたときにチップの幅が変わらないようにする.
        public static int MeasureNameWidth(string text, Font font, float scale)
        {
            Size sz = TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return sz.Width + (int)Math.Round(6 * scale);
        }

        private int TextWidthOf(string text, Font font)
        {
            return MeasureNameWidth(text, font, scale);
        }

        // 名前の文字を置く場所(番号の右から, ×の場所の手前まで)
        private Rectangle TextRect(Rectangle rc, int i, bool pill)
        {
            int x = rc.X + Scaled(pill ? ChipPadX96 : 10);
            if (i < 9) x += Scaled(BadgeSize96) + Scaled(6);
            return new Rectangle(x, rc.Y, Math.Max(1, rc.Right - Scaled(DeleteZone96) - x), rc.Height);
        }

        private Rectangle DeleteZone(Rectangle rc)
        {
            return new Rectangle(rc.Right - Scaled(DeleteZone96) - Scaled(2), rc.Y, Scaled(DeleteZone96) + Scaled(2), rc.Height);
        }

        // ---- その場で名前を変える(入力欄は別の小さな画面で, 名前の文字の場所にぴったり重ねる) ----

        // 名前を変え始める. 入力欄を置く場所(画面の座標)を返す. 見えていなければEmpty
        public Rectangle BeginEdit(SavedRegion region, int textWidth)
        {
            editingRegion = regions.IndexOf(region);
            editingInMenu = menuOpen && menuItems.Contains(editingRegion);
            editActive = IsActive(editingRegion);
            editTextWidth = textWidth;
            editError = null;
            rowRects = null;
            Present();
            return EditTextRect();
        }

        // 入力した文字の長さが変わった: チップを伸び縮みさせ, 入力欄の新しい場所を返す
        public Rectangle UpdateEditWidth(int textWidth)
        {
            if (editingRegion < 0) return Rectangle.Empty;
            editTextWidth = textWidth;
            editError = null;
            rowRects = null;
            Present();
            return EditTextRect();
        }

        public void SetEditError(string message)
        {
            if (editError == message) return;
            editError = message;
            Present();
        }

        public void EndEdit()
        {
            if (editingRegion < 0) return;
            editingRegion = -1;
            editError = null;
            rowRects = null;
            Present();
        }

        // 入力欄の色. チップと同じ(クレイ色のチップならクレイ色のまま入力する)
        public void GetEditColors(out Color back, out Color fore)
        {
            back = editActive ? Theme.AccentInteractive : (editingInMenu ? Theme.BgMuted : Theme.Bg);
            fore = editActive ? Color.White : Theme.Text;
        }

        private Rectangle EditTextRect()
        {
            if (editingRegion < 0) return Rectangle.Empty;
            bool pill;
            Rectangle rc = ItemRect(editingRegion, out pill);
            if (rc.IsEmpty) return Rectangle.Empty;
            Rectangle t = TextRect(rc, editingRegion, pill);
            if (pill) t.Width = Math.Min(t.Width, editTextWidth);
            t.Offset(bounds.Location);
            return t;
        }

        private void LayoutChips(Graphics g)
        {
            rowItems = new List<int>();
            rowRects = new List<Rectangle>();
            menuItems = new List<int>();
            moreRect = Rectangle.Empty;
            int h = Scaled(ChipHeight96), gap = Scaled(ChipGap96), top = Scaled(ChipTop96);
            int maxRow = Math.Max(h, bounds.Width - Scaled(64));
            using (Font font = Theme.Body(ChipFont96 * scale))
            {
                SizeF more = g.MeasureString("+" + regions.Count, font, PointF.Empty, StringFormat.GenericTypographic);
                int moreW = (int)Math.Ceiling(more.Width) + Scaled(ChipPadX96) * 2;
                int x = 0;
                for (int i = 0; i < regions.Count; i++)
                {
                    int w = Math.Min(maxRow - moreW - gap, ChipWidth(g, font, i));
                    int need = x + (rowItems.Count > 0 ? gap : 0) + w;
                    int reserve = i < regions.Count - 1 ? gap + moreW : 0; // 後ろが残るなら「+N」の場所を空けておく
                    if (menuItems.Count == 0 && need + reserve <= maxRow)
                    {
                        if (rowItems.Count > 0) x += gap;
                        rowItems.Add(i);
                        rowRects.Add(new Rectangle(x, top, w, h));
                        x += w;
                    }
                    else
                    {
                        menuItems.Add(i);
                    }
                }
                if (menuItems.Count > 0)
                {
                    if (rowItems.Count > 0) x += gap;
                    moreRect = new Rectangle(x, top, moreW, h);
                    x += moreW;
                }
                int offset = (bounds.Width - x) / 2;
                for (int k = 0; k < rowRects.Count; k++) rowRects[k] = new Rectangle(rowRects[k].X + offset, top, rowRects[k].Width, h);
                if (!moreRect.IsEmpty) moreRect.X += offset;
            }
            if (menuItems.Count == 0) menuOpen = false;
            menuScroll = Math.Max(0, Math.Min(menuScroll, menuItems.Count - Math.Min(MenuMaxItems, menuItems.Count)));
        }

        // 「+N」で開く一覧の外枠と, 見えている項目の数
        private Rectangle MenuRect(out int visible)
        {
            visible = Math.Min(MenuMaxItems, menuItems.Count);
            int w = Scaled(MenuWidth96), pad = Scaled(6);
            int hgt = visible * Scaled(MenuItemHeight96) + pad * 2;
            int x = Math.Max(Scaled(8), Math.Min(bounds.Width - w - Scaled(8), moreRect.Right - w));
            return new Rectangle(x, moreRect.Bottom + Scaled(6), w, hgt);
        }

        private Rectangle MenuItemRect(Rectangle menu, int row)
        {
            int pad = Scaled(6);
            return new Rectangle(menu.X + pad, menu.Y + pad + row * Scaled(MenuItemHeight96), menu.Width - pad * 2, Scaled(MenuItemHeight96));
        }

        private bool IsActive(int i)
        {
            return selection.RegionName != null && selection.RegionName == regions[i].Name;
        }

        private void DrawRegionList(Graphics g)
        {
            if (rowRects == null) LayoutChips(g);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearTypeは透明度つきの面だと汚くなる
            using (Font font = Theme.Body(ChipFont96 * scale))
            using (Font badgeFont = Theme.BodyStrong(12 * scale))
            using (StringFormat center = new StringFormat(StringFormat.GenericTypographic))
            using (StringFormat left = new StringFormat(StringFormat.GenericTypographic))
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                center.FormatFlags |= StringFormatFlags.NoWrap;
                center.Trimming = StringTrimming.EllipsisCharacter;
                left.LineAlignment = StringAlignment.Center;
                left.FormatFlags |= StringFormatFlags.NoWrap;
                left.Trimming = StringTrimming.EllipsisCharacter;

                for (int k = 0; k < rowItems.Count; k++)
                {
                    int i = rowItems[k];
                    if (dragMode == DragMode.ListDrag && i == pressRegion) continue; // ドラッグ中の名前はカーソルの位置に描く
                    bool hot = (hover.Kind == ListHitKind.Chip || hover.Kind == ListHitKind.Delete) && hover.Region == i;
                    DrawItem(g, rowRects[k], i, font, badgeFont, center, left, hot, true);
                }

                if (!moreRect.IsEmpty)
                {
                    bool hot = hover.Kind == ListHitKind.More || menuOpen;
                    using (GraphicsPath path = Theme.RoundedRect(moreRect, moreRect.Height / 2f))
                    using (SolidBrush b = new SolidBrush(hot ? Theme.BgMuted : Color.FromArgb(220, Theme.Bg)))
                    using (SolidBrush t = new SolidBrush(Theme.Text))
                    {
                        g.FillPath(b, path);
                        g.DrawString("+" + menuItems.Count, font, t, moreRect, center);
                    }
                }

                if (menuOpen && menuItems.Count > 0)
                {
                    int visible;
                    Rectangle menu = MenuRect(out visible);
                    using (GraphicsPath path = Theme.RoundedRect(menu, Scaled(10)))
                    using (SolidBrush b = new SolidBrush(Theme.Bg))
                    {
                        g.FillPath(b, path);
                    }
                    for (int row = 0; row < visible; row++)
                    {
                        int idx = menuScroll + row;
                        if (idx >= menuItems.Count) break;
                        int i = menuItems[idx];
                        if (dragMode == DragMode.ListDrag && i == pressRegion) continue;
                        bool hot = (hover.Kind == ListHitKind.MenuItem || hover.Kind == ListHitKind.Delete) && hover.Region == i;
                        DrawItem(g, MenuItemRect(menu, row), i, font, badgeFont, center, left, hot, false);
                    }
                    if (menuItems.Count > visible)
                    {
                        // スクロールできることを示す細い棒
                        int track = menu.Height - Scaled(12);
                        int barH = Math.Max(Scaled(16), track * visible / menuItems.Count);
                        int barY = menu.Y + Scaled(6) + (track - barH) * menuScroll / Math.Max(1, menuItems.Count - visible);
                        using (SolidBrush bar = new SolidBrush(Theme.Border))
                        {
                            g.FillRectangle(bar, menu.Right - Scaled(5), barY, Scaled(3), barH);
                        }
                    }
                }
                if (dragMode == DragMode.ListDrag && pressRegion >= 0) DrawListDrag(g, font, badgeFont, center, left);
            }
            if (editError != null && editingRegion >= 0) DrawEditError(g);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // ---- ドラッグでの並べ替え ----

        // カーソルの位置から, どの範囲の前に入れるかを決める. 1行の上下の帯か, 開いている縦の一覧の中だけ
        private int DropTarget(Point local, out bool inRow)
        {
            inRow = false;
            int visible = 0;
            Rectangle menu = menuOpen ? MenuRect(out visible) : Rectangle.Empty;
            if (menuOpen && menu.Contains(local))
            {
                int last = -1;
                for (int row = 0; row < visible; row++)
                {
                    int idx = menuScroll + row;
                    if (idx >= menuItems.Count) break;
                    Rectangle ir = MenuItemRect(menu, row);
                    if (local.Y < ir.Y + ir.Height / 2) return menuItems[idx];
                    last = menuItems[idx];
                }
                return last >= 0 ? last + 1 : -1;
            }
            if (rowRects.Count > 0)
            {
                Rectangle r0 = rowRects[0];
                int band = Scaled(16);
                if (local.Y >= r0.Top - band && local.Y <= r0.Bottom + band)
                {
                    inRow = true;
                    for (int k = 0; k < rowRects.Count; k++)
                    {
                        if (local.X < rowRects[k].X + rowRects[k].Width / 2) return rowItems[k];
                    }
                    return rowItems[rowItems.Count - 1] + 1;
                }
            }
            return -1;
        }

        // ドラッグ中: 入れる場所に細い棒を出し, 名前はカーソルについて動かす
        private void DrawListDrag(Graphics g, Font font, Font badgeFont, StringFormat center, StringFormat left)
        {
            using (SolidBrush bar = new SolidBrush(Theme.Bg))
            {
                int t = Scaled(3);
                if (dropBefore >= 0 && dropInRow)
                {
                    int x = -1;
                    for (int k = 0; k < rowItems.Count; k++)
                    {
                        if (rowItems[k] == dropBefore) { x = rowRects[k].X - Scaled(ChipGap96) / 2; break; }
                    }
                    if (x < 0) x = rowRects[rowRects.Count - 1].Right + Scaled(ChipGap96) / 2;
                    Rectangle r0 = rowRects[0];
                    g.FillRectangle(bar, x - t / 2, r0.Y - Scaled(2), t, r0.Height + Scaled(4));
                }
                else if (dropBefore >= 0 && menuOpen)
                {
                    int visible;
                    Rectangle menu = MenuRect(out visible);
                    int y = -1;
                    for (int row = 0; row < visible; row++)
                    {
                        int idx = menuScroll + row;
                        if (idx < menuItems.Count && menuItems[idx] == dropBefore) { y = MenuItemRect(menu, row).Y; break; }
                    }
                    if (y < 0) y = MenuItemRect(menu, Math.Min(visible, menuItems.Count - menuScroll)).Y;
                    using (SolidBrush ink = new SolidBrush(Theme.Text))
                    {
                        g.FillRectangle(ink, menu.X + Scaled(8), y - t / 2, menu.Width - Scaled(16), t);
                    }
                }
            }

            bool pill;
            Rectangle src = ItemRect(pressRegion, out pill);
            if (src.IsEmpty)
            {
                pill = !pressFromMenu;
                src = new Rectangle(0, 0, Scaled(MenuWidth96), Scaled(ChipHeight96));
            }
            Rectangle floating = new Rectangle(listDragPos.X - grabOffset.X, listDragPos.Y - grabOffset.Y, src.Width, src.Height);
            DrawItem(g, floating, pressRegion, font, badgeFont, center, left, true, pill);
        }

        // 名前を変えられない理由を, 名前の下にアイボリーの吹き出しで出す(文が途切れないよう, 長さに合わせる)
        private void DrawEditError(Graphics g)
        {
            bool pill;
            Rectangle rc = ItemRect(editingRegion, out pill);
            if (rc.IsEmpty) return;
            using (Font font = Theme.Body(13 * scale))
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                int padX = Scaled(12), padY = Scaled(7);
                int maxW = Math.Min(Scaled(480), bounds.Width - Scaled(16));
                SizeF ts = g.MeasureString(editError, font, maxW - padX * 2, fmt);
                int w = (int)Math.Ceiling(ts.Width) + padX * 2;
                int h = (int)Math.Ceiling(ts.Height) + padY * 2;
                int x = Math.Max(Scaled(8), Math.Min(bounds.Width - w - Scaled(8), rc.X));
                int y = rc.Bottom + Scaled(8);
                Rectangle box = new Rectangle(x, y, w, h);
                using (GraphicsPath path = Theme.RoundedRect(box, Scaled(8)))
                using (SolidBrush b = new SolidBrush(Theme.Bg))
                using (Pen border = new Pen(Theme.Border))
                using (SolidBrush t = new SolidBrush(Theme.Danger))
                {
                    g.FillPath(b, path);
                    g.DrawPath(border, path);
                    g.DrawString(editError, font, t, new RectangleF(x + padX, y + padY, w - padX * 2 + 1, h - padY * 2 + 1), fmt);
                }
            }
        }

        // チップ(pill=true)か, 縦の一覧の項目を1つ描く. 先頭9個には数字の四角を付ける.
        // カーソルが右端(×の場所)にあるときだけ×を出す. 名前を変えている間は, 白地に濃いふちで, 文字は入力欄が描く.
        private void DrawItem(Graphics g, Rectangle rc, int i, Font font, Font badgeFont, StringFormat center, StringFormat left, bool hot, bool pill)
        {
            bool editing = i == editingRegion;
            bool active = editing ? editActive : IsActive(i);
            if (editing) hot = editingInMenu; // 入力欄の色(GetEditColors)とそろえる
            Color back = active ? Theme.AccentInteractive : (hot ? Theme.BgMuted : (pill ? Theme.Bg : Color.Transparent));
            Color fore = active ? Color.White : Theme.Text;
            if (back.A > 0)
            {
                using (GraphicsPath path = Theme.RoundedRect(rc, pill ? rc.Height / 2f : Scaled(6)))
                using (SolidBrush b = new SolidBrush(back))
                {
                    g.FillPath(b, path);
                    if (editing)
                    {
                        // 入力中の印に, 文字と同じ色の細いふちを付ける
                        using (Pen pen = new Pen(Color.FromArgb(150, fore), Math.Max(1f, 1.2f * scale)))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
            }
            int x = rc.X + Scaled(pill ? ChipPadX96 : 10);
            if (i < 9)
            {
                int bs = Scaled(BadgeSize96);
                Rectangle badge = new Rectangle(x, rc.Y + (rc.Height - bs) / 2, bs, bs);
                using (GraphicsPath path = Theme.RoundedRect(badge, Scaled(4)))
                using (SolidBrush b = new SolidBrush(active ? Color.FromArgb(64, 255, 255, 255) : Color.FromArgb(26, Theme.Text)))
                using (SolidBrush t = new SolidBrush(fore))
                {
                    g.FillPath(b, path);
                    g.DrawString((i + 1).ToString(), badgeFont, t, badge, center);
                }
            }
            if (!editing)
            {
                using (SolidBrush t = new SolidBrush(fore))
                {
                    g.DrawString(regions[i].Name ?? "", font, t, TextRect(rc, i, pill), left);
                }
            }

            if (!editing && hover.Kind == ListHitKind.Delete && hover.Region == i)
            {
                Rectangle d = DeleteRect(rc, pill);
                using (SolidBrush b = new SolidBrush(active ? Theme.Bg : Theme.Text))
                using (Pen x2 = new Pen(active ? Theme.AccentInteractive : Theme.Bg, Math.Max(1.5f, 1.6f * scale)))
                {
                    g.FillEllipse(b, d);
                    float c = d.Width * 0.27f;
                    float cx = d.X + d.Width / 2f, cy = d.Y + d.Height / 2f;
                    g.DrawLine(x2, cx - c, cy - c, cx + c, cy + c);
                    g.DrawLine(x2, cx - c, cy + c, cx + c, cy - c);
                }
            }
        }

        // ×の丸(右端の×の場所の中央)
        private Rectangle DeleteRect(Rectangle rc, bool pill)
        {
            int d = Scaled(16);
            Rectangle z = DeleteZone(rc);
            int cx = z.X + (z.Width - (pill ? Scaled(4) : 0)) / 2;
            return new Rectangle(cx - d / 2, rc.Y + (rc.Height - d) / 2, d, d);
        }

        // 名前(チップか縦の一覧の項目)の場所. 見えていなければEmpty
        private Rectangle ItemRect(int region, out bool pill)
        {
            pill = true;
            if (rowRects == null) return Rectangle.Empty;
            for (int k = 0; k < rowItems.Count; k++) if (rowItems[k] == region) return rowRects[k];
            pill = false;
            if (!menuOpen) return Rectangle.Empty;
            int visible;
            Rectangle menu = MenuRect(out visible);
            for (int row = 0; row < visible; row++)
            {
                int idx = menuScroll + row;
                if (idx < menuItems.Count && menuItems[idx] == region) return MenuItemRect(menu, row);
            }
            return Rectangle.Empty;
        }

        private ListHit HitList(Point local)
        {
            if (!HasList || rowRects == null) return ListHit.None;
            if (menuOpen)
            {
                int visible;
                Rectangle menu = MenuRect(out visible);
                if (menu.Contains(local))
                {
                    for (int row = 0; row < visible; row++)
                    {
                        int idx = menuScroll + row;
                        Rectangle ir = MenuItemRect(menu, row);
                        if (idx < menuItems.Count && ir.Contains(local))
                        {
                            int r = menuItems[idx];
                            if (r != editingRegion && DeleteZone(ir).Contains(local)) return new ListHit(ListHitKind.Delete, r);
                            return new ListHit(ListHitKind.MenuItem, r);
                        }
                    }
                    return new ListHit(ListHitKind.Menu, -1);
                }
            }
            for (int k = 0; k < rowRects.Count; k++)
            {
                if (rowRects[k].Contains(local))
                {
                    // 右端の×の場所に入ったら×を出す(削除の当たり)
                    int r = rowItems[k];
                    if (r != editingRegion && DeleteZone(rowRects[k]).Contains(local)) return new ListHit(ListHitKind.Delete, r);
                    return new ListHit(ListHitKind.Chip, r);
                }
            }
            if (!moreRect.IsEmpty && moreRect.Contains(local)) return new ListHit(ListHitKind.More, -1);
            return ListHit.None;
        }

        // カーソルの下の項目が変わったら描き直し, 範囲の名前に乗ったら下見を頼む.
        private void SetListHover(ListHit h)
        {
            if (!hover.SameAs(h))
            {
                hover = h;
                Present();
            }
            int region = (h.Kind == ListHitKind.Chip || h.Kind == ListHitKind.MenuItem || h.Kind == ListHitKind.Delete) ? h.Region : -1;
            if (region == hoveredRegion) return;
            hoveredRegion = region;
            if (RegionHovered != null) RegionHovered(this, region >= 0 ? regions[region] : null);
        }

        private void OpenMenu()
        {
            if (menuOpen || menuItems.Count == 0) return;
            menuOpen = true;
            menuScroll = 0;
            Present();
        }

        private void CloseMenu()
        {
            if (!menuOpen || menuItems.Contains(editingRegion)) return; // 縦の一覧で名前を変えている間は閉じない
            menuOpen = false;
            Present();
        }

        // 「+N」にカーソルを乗せたら縦の一覧を開き, 「+N」と一覧から離れたら閉じる.
        private void UpdateMenuByHover(Point local, ListHit lh)
        {
            if (moreRect.IsEmpty) return;
            if (lh.Kind == ListHitKind.More) { OpenMenu(); return; }
            if (!menuOpen) return;
            int visible;
            Rectangle zone = Rectangle.Union(moreRect, MenuRect(out visible));
            zone.Inflate(Scaled(8), Scaled(8)); // 「+N」と一覧のすき間を通っても閉じない
            if (!zone.Contains(local)) CloseMenu();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!menuOpen) return;
            int visible;
            MenuRect(out visible);
            int max = Math.Max(0, menuItems.Count - visible);
            int next = Math.Max(0, Math.Min(max, menuScroll - Math.Sign(e.Delta) * 3));
            if (next == menuScroll) return;
            menuScroll = next;
            SetListHover(HitList(e.Location));
            Present();
        }

        private void Present()
        {
            if (surface == null || !IsHandleCreated) return;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Render();
            NativeMethods.GdiFlush();

            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                NativeMethods.POINT dst = new NativeMethods.POINT(bounds.X, bounds.Y);
                NativeMethods.SIZE size = new NativeMethods.SIZE(bounds.Width, bounds.Height);
                NativeMethods.POINT src = new NativeMethods.POINT(0, 0);
                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;
                NativeMethods.UpdateLayeredWindow(Handle, screenDC, ref dst, ref size, memDC, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
            }
            double ms = watch.Elapsed.TotalMilliseconds;
            PresentCount++;
            PresentTotalMs += ms;
            if (ms > PresentMaxMs) PresentMaxMs = ms;
        }

        private Rectangle ToLocal(Rectangle r)
        {
            return new Rectangle(r.X - bounds.X, r.Y - bounds.Y, r.Width, r.Height);
        }

        // e.Locationはクライアント座標(物理ピクセル). モニター左上からの位置と同じ.
        // マウスをキャプチャ中は, ほかのモニター上の位置も同じ座標系で届く.
        private Point ToVirtual(Point client)
        {
            return new Point(client.X + bounds.X, client.Y + bounds.Y);
        }

        // ---- マウス ----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 一覧の上の操作は, 枠の操作より優先する.
            ListHit lh = HitList(e.Location);
            if (e.Button != MouseButtons.Left) return;
            if (lh.Kind == ListHitKind.Delete)
            {
                // ×: その範囲を削除する(確認してから)
                if (RegionDeleteRequested != null) RegionDeleteRequested(this, regions[lh.Region]);
                return;
            }
            if (lh.Kind == ListHitKind.Chip || lh.Kind == ListHitKind.MenuItem)
            {
                SavedRegion region = regions[lh.Region];
                if (e.Clicks >= 2)
                {
                    // ダブルクリック: その場で名前を変える
                    if (RegionEditRequested != null) RegionEditRequested(this, region);
                    return;
                }
                // 押しただけでは決めない. 離したときに決める(動かしたらドラッグでの並べ替え).
                dragMode = DragMode.ListPress;
                pressRegion = lh.Region;
                pressFromMenu = lh.Kind == ListHitKind.MenuItem;
                pressPoint = e.Location;
                bool pill;
                Rectangle rc = ItemRect(lh.Region, out pill);
                grabOffset = new Point(e.X - rc.X, e.Y - rc.Y);
                Capture = true;
                return;
            }
            if (lh.Kind == ListHitKind.More)
            {
                OpenMenu();
                return;
            }
            if (lh.Kind == ListHitKind.Menu) return;
            CloseMenu();
            if (PreviewCancelRequested != null) PreviewCancelRequested(this, EventArgs.Empty);

            Point p = ToVirtual(e.Location);
            Rectangle frame = selection.Frame;

            FrameHandle hit = Selection.HitTest(frame, p, Scaled(Grip96));
            if (hit != FrameHandle.None)
            {
                // 辺・角をクリックすると選んだ状態になり, そのままドラッグすると辺を動かす(Macのように8点以外もつかめる).
                selection.SelectedHandle = hit;
                dragMode = DragMode.FrameHandle;
                dragHandle = hit;
            }
            else if (frame.Contains(p))
            {
                // 枠の内側をクリックするとハンドルの選択を外し, ドラッグすると枠全体を動かす.
                selection.SelectedHandle = FrameHandle.None;
                dragMode = DragMode.Move;
            }
            else
            {
                // 枠の外をクリックしても選択を外す. 枠の外のドラッグで範囲を選び直す操作は作らない(B3).
                selection.SelectedHandle = FrameHandle.None;
                return;
            }
            dragStart = p;
            frameAtDragStart = frame;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = ToVirtual(e.Location);
            int tdx = p.X - dragStart.X;
            int tdy = p.Y - dragStart.Y;

            if (dragMode == DragMode.ListPress)
            {
                if (Math.Abs(e.X - pressPoint.X) <= Scaled(6) && Math.Abs(e.Y - pressPoint.Y) <= Scaled(6)) return;
                // 動かしたら並べ替えを始める. 下見はやめて元の枠に戻す
                dragMode = DragMode.ListDrag;
                if (PreviewCancelRequested != null) PreviewCancelRequested(this, EventArgs.Empty);
                hoveredRegion = -1;
                hover = ListHit.None;
                Cursor = Cursors.SizeAll;
            }
            if (dragMode == DragMode.ListDrag)
            {
                listDragPos = e.Location;
                dropBefore = DropTarget(e.Location, out dropInRow);
                Present();
                return;
            }

            if (dragMode == DragMode.Move)
            {
                selection.MoveTo(new Point(frameAtDragStart.X + tdx, frameAtDragStart.Y + tdy), p);
            }
            else if (dragMode == DragMode.FrameHandle)
            {
                // ドラッグ開始からの移動量で「動かしたい辺の位置」を決め, 今の枠との差を渡す.
                // 端で止められても, マウスと辺の位置がずれていかない.
                Rectangle f = selection.Frame;
                int dx = 0, dy = 0;
                if (MovesLeft(dragHandle)) dx = (frameAtDragStart.Left + tdx) - f.Left;
                else if (MovesRight(dragHandle)) dx = (frameAtDragStart.Right + tdx) - f.Right;
                if (MovesTop(dragHandle)) dy = (frameAtDragStart.Top + tdy) - f.Top;
                else if (MovesBottom(dragHandle)) dy = (frameAtDragStart.Bottom + tdy) - f.Bottom;
                if (dx != 0 || dy != 0) selection.MoveEdges(dragHandle, dx, dy);
            }
            else
            {
                ListHit lh = HitList(e.Location);
                if (HasList && rowRects != null) UpdateMenuByHover(e.Location, lh);
                lh = HitList(e.Location);
                SetListHover(lh);
                if (lh.Kind != ListHitKind.None)
                {
                    selection.HoverHandle = FrameHandle.None;
                    Cursor c = lh.Kind == ListHitKind.Menu ? Cursors.Default : Cursors.Hand;
                    if (Cursor != c) Cursor = c;
                    return;
                }
                UpdateCursor(p);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragMode == DragMode.ListPress)
            {
                // 名前を押して離した: その範囲に決める(A4). 下見で動いていた枠がそのまま残る.
                // 縦の一覧は開いたままにする(続けてダブルクリックで名前を変えられるように).
                dragMode = DragMode.None;
                Capture = false;
                int r = pressRegion;
                pressRegion = -1;
                if (!pressFromMenu) menuOpen = false;
                hoveredRegion = -1;
                if (r >= 0 && r < regions.Count && RegionChosen != null) RegionChosen(this, regions[r]);
                Present();
                return;
            }
            if (e.Button == MouseButtons.Left && dragMode == DragMode.ListDrag)
            {
                dragMode = DragMode.None;
                Capture = false;
                int from = pressRegion, before = dropBefore;
                pressRegion = -1;
                dropBefore = -1;
                if (before >= 0)
                {
                    int to = before > from ? before - 1 : before;
                    if (to != from && RegionMoved != null) RegionMoved(this, from, to);
                }
                Present();
                return;
            }
            if (e.Button == MouseButtons.Left && dragMode != DragMode.None)
            {
                dragMode = DragMode.None;
                dragHandle = FrameHandle.None;
                Capture = false;
                UpdateCursor(ToVirtual(e.Location));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragMode == DragMode.ListPress || dragMode == DragMode.ListDrag) return; // 並べ替えの途中は何もしない
            if (dragMode == DragMode.None) selection.HoverHandle = FrameHandle.None;
            SetListHover(ListHit.None);
            CloseMenu();
        }

        private void UpdateCursor(Point p)
        {
            Rectangle frame = selection.Frame;
            FrameHandle hit = Selection.HitTest(frame, p, Scaled(Grip96));
            selection.HoverHandle = hit; // つかむ場所の予告
            Cursor c;
            switch (hit)
            {
                case FrameHandle.TopLeft:
                case FrameHandle.BottomRight: c = Cursors.SizeNWSE; break;
                case FrameHandle.TopRight:
                case FrameHandle.BottomLeft: c = Cursors.SizeNESW; break;
                case FrameHandle.Top:
                case FrameHandle.Bottom: c = Cursors.SizeNS; break;
                case FrameHandle.Left:
                case FrameHandle.Right: c = Cursors.SizeWE; break;
                default: c = frame.Contains(p) ? Cursors.SizeAll : Cursors.Default; break;
            }
            if (Cursor != c) Cursor = c;
        }

        private static bool MovesLeft(FrameHandle h) { return h == FrameHandle.TopLeft || h == FrameHandle.Left || h == FrameHandle.BottomLeft; }
        private static bool MovesRight(FrameHandle h) { return h == FrameHandle.TopRight || h == FrameHandle.Right || h == FrameHandle.BottomRight; }
        private static bool MovesTop(FrameHandle h) { return h == FrameHandle.TopLeft || h == FrameHandle.Top || h == FrameHandle.TopRight; }
        private static bool MovesBottom(FrameHandle h) { return h == FrameHandle.BottomLeft || h == FrameHandle.Bottom || h == FrameHandle.BottomRight; }

        // ---- キーボード ----

        // 矢印キーはフォームのフォーカス移動に使われてOnKeyDownまで届かないことがあるので, ここで受ける.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            int step = ctrl ? 10 : 1;

            if (FirstKey == null)
            {
                FirstKey = key.ToString();
                if (FirstKeyArrived != null) FirstKeyArrived(this, EventArgs.Empty);
            }

            // 数字キー1〜9: 一覧の先頭9個を呼び出す(A4)
            if ((keyData >= Keys.D1 && keyData <= Keys.D9) || (keyData >= Keys.NumPad1 && keyData <= Keys.NumPad9))
            {
                int n = keyData >= Keys.NumPad1 ? keyData - Keys.NumPad1 : keyData - Keys.D1;
                if (dragMode == DragMode.None && regions != null && n < regions.Count && RegionChosen != null)
                {
                    menuOpen = false;
                    hoveredRegion = -1;
                    RegionChosen(this, regions[n]);
                    Present();
                }
                return true;
            }

            // Enterは見えている枠(下見中なら下見の範囲)を撮る. それ以外のキーは, 下見をやめて元の枠に戻してから働く.
            if (key != Keys.Enter && PreviewCancelRequested != null) PreviewCancelRequested(this, EventArgs.Empty);

            if (keyData == Keys.Escape && menuOpen)
            {
                CloseMenu(); // まず「+N」の一覧を閉じる
                return true;
            }

            if (keyData == Keys.Space)
            {
                if (dragMode == DragMode.None && LiveToggleRequested != null) LiveToggleRequested(this, EventArgs.Empty);
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                // 今の枠に名前を付けて保存する(A3).
                if (dragMode == DragMode.None && SaveRequested != null) SaveRequested(this, EventArgs.Empty);
                return true;
            }

            switch (key)
            {
                case Keys.Left: Nudge(-step, 0); return true;
                case Keys.Right: Nudge(step, 0); return true;
                case Keys.Up: Nudge(0, -step); return true;
                case Keys.Down: Nudge(0, step); return true;
                case Keys.Enter:
                    if (CaptureRequested != null) CaptureRequested(this, EventArgs.Empty);
                    return true;
                case Keys.Escape:
                    // 辺・角を選んでいればまず選択を外す. 選んでいなければ閉じる.
                    if (selection.SelectedHandle != FrameHandle.None)
                    {
                        if (dragMode == DragMode.None) selection.SelectedHandle = FrameHandle.None;
                    }
                    else if (CancelRequested != null)
                    {
                        CancelRequested(this, EventArgs.Empty);
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Nudge(int dx, int dy)
        {
            if (dragMode != DragMode.None) return; // ドラッグ中は矢印キーを無視する
            // 動く辺を必ず見せる. カーソルの辺の予告は, マウスを動かすまで消す.
            selection.HoverHandle = FrameHandle.None;
            if (selection.SelectedHandle != FrameHandle.None)
            {
                selection.MoveEdges(selection.SelectedHandle, dx, dy);
            }
            else
            {
                selection.MoveBy(dx, dy);
            }
        }

        // ---- 終了 ----

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) // サインアウトなどは止めない
            {
                // Alt+F4などで1枚だけ閉じず, Escと同じく全体を終える.
                e.Cancel = true;
                if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                selection.Changed -= OnSelectionChanged;
                if (surface != null)
                {
                    surface.Dispose();
                    surface = null;
                }
            }
            if (memDC != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) NativeMethods.SelectObject(memDC, oldBitmap);
                NativeMethods.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
                hBitmap = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}
