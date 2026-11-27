# Wakudori(枠撮り)

同じ範囲を, 何度でも撮れるWindows用スクリーンショットツール.

<a href="https://maonthemoon.github.io/wakudori/">
  <picture>
    <source media="(max-width: 767px) and (prefers-color-scheme: dark)" srcset="docs/images/demo-narrow-dark.webp">
    <source media="(max-width: 767px)" srcset="docs/images/demo-narrow-light.webp">
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/demo-dark.webp">
    <img src="docs/images/demo-light.webp" alt="Wakudoriの動くデモ: 前回の範囲をそのまま撮る, 名前を付けて保存する, 呼び出す, 枠を合わせる, 一覧を整える">
  </picture>
</a>

[ダウンロード](https://github.com/maonthemoon/wakudori/releases/latest) | [紹介のページ](https://maonthemoon.github.io/wakudori/)(5つの場面を選んで見られます) | [マニュアル](https://maonthemoon.github.io/wakudori/manual.html)

ホットキーを押すと, 前に撮った範囲がそのまま出ます. あとは`Enter`で撮るだけ. よく撮る範囲は, 名前を付けて残せます.

## 強み

Windows標準のスクリーンショット(Snipping Toolなど)では, 撮るたびに範囲を選び直します. 手間がかかり, 前の画像と少しずつずれます. Wakudoriは, 一度決めた範囲を使い回します.

- **前と同じ範囲は, `Enter`だけ**: 前に撮った範囲に, はじめから枠が出ています. 準備は要らず, 2枚目からもうドラッグしません.
- **よく撮る範囲は, 名前で呼び出す**: `Ctrl+S`で名前を付けると, 撮る画面の上に並びます. 乗せれば下見, クリックか数字キー`1`〜`9`で呼び出しです.
- **いつ撮っても, 位置も大きさも同じ**: 一度だけ, 矢印キーで1pxずつ合わせておけば, 来週も同じ範囲です. 先週の画像と, そのまま見比べられます.
- **ファイル名で, 何の画像か分かる**: 保存した範囲で撮ると, `20261126_091500_KPI.png`のように日時の後ろに範囲の名前が付きます.

モニター全体の撮影(`F`)と, 撮ってすぐの書き込みや黒塗り(`E`)もできます. 撮った画像は, クリップボードとPNGの両方に出ます.

試すのも簡単です. 約130KBのexe 1つで動き, インストールも管理者権限も要らず, 通信もしません.

## 向いている作業, 向いていない作業

| 向いている | 向いていない |
|---|---|
| 毎週のレポートに, ダッシュボードの同じグラフを貼る | 動かしたウィンドウを追いかけて撮る(範囲は画面上の位置で覚えます) |
| 設定や修正の前後を, 同じ範囲で撮って見比べる | スクロールした先まで1枚に撮る, ウィンドウを選んで撮る |
| 手順書の画像を, 大きさをそろえて撮る | 矢印や文字を入れる, 文字を抽出する, 録画する |
| 1つの画面から, 決まった何か所かを続けて撮る | 2つのモニターにまたがる範囲を撮る |

向いていない作業には, 標準のSnipping Toolなどを使い分けてください. ホットキーを分ければ, 一緒に使えます([違いの表](https://maonthemoon.github.io/wakudori/#compare)).

## インストール

動作環境はWindows(Windows 11とWindows 10 22H2で動作を確認)です. 必要な.NET Framework 4.8は, Windowsに標準で入っています.

1. [Releases](https://github.com/maonthemoon/wakudori/releases/latest)から`Wakudori-x.y.z.zip`をダウンロードし, 好きな場所(例: `ドキュメント\Wakudori`)に展開します.
2. 展開した中の`bin\Wakudori.exe`を起動します. 署名していないので, 「WindowsによってPCが保護されました」と出たら「詳細情報」→「実行」を押します.
3. ホットキーの登録画面が出たら, 使うキー(例: `Ctrl+Shift+S`)を押して「登録」を押します. Wakudoriはタスクトレイに常駐します.

PCの起動時に起動するかは, 初めて起動したときに聞かれます. あとからでも, トレイの「設定」で切り替えられます.

### gitを使う場合

cloneして, `bin\Wakudori.exe`を起動します(最新のリリースと同じexeです). SmartScreenの警告は出ない見込みです(実機では未確認).

```sh
git clone https://github.com/maonthemoon/wakudori.git
```

新しい版にするときは, トレイのメニューで「終了」してから`git pull`し, もう一度起動します(起動している間はexeを上書きできないため).

### アンインストール

トレイの「設定」で「PC起動時に起動」をオフにしてから, トレイの「終了」を選びます. 展開した(またはcloneした)フォルダーと`%APPDATA%\Wakudori`を消します.

## 使い方

1. ホットキーを押し, 枠を合わせて`Enter`で撮ります.
2. 次からは, ホットキー → `Enter`で同じ範囲を撮れます.
3. よく撮る範囲は`Ctrl+S`で名前を付けます. 次からは, ホットキー → 数字キー → `Enter`です.

よく使う操作です. すべての操作は[マニュアル](https://maonthemoon.github.io/wakudori/manual.html#capture)にあります(トレイのメニューからも開けます).

| 操作 | 動き |
|---|---|
| `Enter`, `A` | 撮る(`A`は既定の撮影キー. 設定画面で変えられる) |
| `1`〜`9`, 名前をクリック | 保存した範囲を呼び出す(名前に乗せると下見) |
| `Ctrl+S` | 今の枠に名前を付けて保存する |
| 枠の外をドラッグ | 新しい枠を引く(離しても撮らない) |
| 辺・角をドラッグ, 内側をドラッグ | 大きさを変える, 枠全体を動かす |
| 辺・角をクリックして矢印キー | その辺を1px動かす(`Ctrl`+矢印キーは10px) |
| `E` | 撮って書き込む(ドラッグでペン, `Shift`+ドラッグで黒塗り) |
| `F` / `Shift+F` | カーソルのあるモニター全体を撮る / 撮って書き込む |
| `Space` | 止めた画面と動いている画面を切り替える |
| `Esc` | 撮らずに閉じる(辺を選んでいれば選択を外す) |

名前はダブルクリックで変え, ドラッグで並べ替え, 右端の×で削除します. 書き込む前の画像も保存フォルダーに残るので, 隠した画像を渡すときは`_edit`の付いたほうを渡してください.

### トレイのメニューと設定

トレイアイコンを右クリックすると, 「撮影」「設定」「画像保存フォルダーを開く」「マニュアル」「終了」が並びます.

「設定」では, ホットキー, 撮影キー, 画像の保存先, 古い画像の自動整理と日数, PC起動時の自動起動を変えられます. 変更はその場で反映されます. `settings.json`を手で編集するときは, 設定画面の下にある「設定フォルダーを開く」からファイルを探せます. ホットキーを手で編集した場合は, Wakudoriを起動し直すと反映されます.

### 保存されるもの

| 場所 | 中身 |
|---|---|
| `ピクチャ\Screenshots\` | 撮った画像. `yyyyMMdd_HHmmss_<範囲の名前>.png`(名前のない範囲は`yyyyMMdd_HHmmss.png`) |
| `%APPDATA%\Wakudori\settings.json` | ホットキー(`hotkey`), 画像の保存先(`saveDir`), 撮影キー(`captureKey`), 古い画像の自動整理(`autoDelete`), 日数(`autoDeleteDays`) |
| `%APPDATA%\Wakudori\error.log` | 想定外のエラーの記録. このPCに残すだけで, どこにも送らない(エラーが起きたときだけできる) |
| `%APPDATA%\Wakudori\regions.json` | 保存した範囲と前回の範囲. 手で編集してもよい(選択画面を開くたびに読み直す) |

「古い画像をごみ箱へ移す」(`autoDelete`)は初めはオフです. オンにすると, Wakudoriが撮って記録した画像だけを, 撮影から指定の日数(`autoDeleteDays`, 既定30日)がたったらごみ箱へ移します. 設定画面でオンにするときに対象の画像があれば, 移す枚数を表示して確認します. この版より前の画像や, 移した・名前を変えた・直した画像は消しません.

撮影の記録は, オン・オフにかかわらず, 指定の日数がたつと`captures.log`から外します. ごみ箱へ移せなかった画像の記録は次回のために残します. ネットワーク上や外付けのドライブなど, ごみ箱のない場所の画像は消しません.

## 困ったとき

[よくある質問](https://maonthemoon.github.io/wakudori/manual.html#faq)を見てください. 不具合や要望は[Issues](https://github.com/maonthemoon/wakudori/issues)で知らせてください. `error.log`を添えるときは, フォルダーの場所やユーザー名が入っていないか, 先に中身を確かめてください.

## 開発者向け

プルリクエストを送る前に, [Issues](https://github.com/maonthemoon/wakudori/issues)で相談してください.

### ビルド

SDKのインストールは不要です. Windows標準の`csc.exe`(.NET Framework 4.8)でビルドします.

1. このリポジトリをcloneするか, ZIPでダウンロードして展開します.
2. `build.cmd`をダブルクリックします.
3. `out\Wakudori.exe`ができます(`bin\Wakudori.exe`はリリースした版なので上書きしません).

C# 5の文法で書いています(`csc.exe`がC# 5までのため).

配る用のZIPは`powershell -ExecutionPolicy Bypass -File tools\package.ps1`で`dist\`に作ります. `v`で始まるタグをpushすると, [GitHub Actions](.github/workflows/build.yml)がビルドとテストを行ってReleasesに置き, `bin\Wakudori.exe`も差し替えます(手で更新しません). バージョンは[`src/AssemblyInfo.cs`](src/AssemblyInfo.cs)で決めます.

### テスト

WinFormsに依存しない部分(枠の計算, ファイル名, 出力とエラーの記録, 保存した画像の記録, ホットキーの文字列, `regions.json`の読み書き, 書き込みの並び, 設定の更新と表示)のテストです. .NET 8 SDKがあれば, Windows以外でも動きます.

```sh
dotnet run --project tests/SelectionTests
dotnet run --project tests/OutputTests
dotnet run --project tests/CaptureLogTests
dotnet run --project tests/CleanupTests
dotnet run --project tests/HotkeyTests
dotnet run --project tests/RegionTests
dotnet run --project tests/MarkTests
dotnet run --project tests/SettingsTests
```

`tests/e2e.ps1`は, ビルドしたexeを実際に動かし, キーで撮れるかを確かめます. `%APPDATA%\Wakudori\settings.json`を書き換えるので, GitHub ActionsのWindows(`e2e`のjob)だけで動かします.

### 画像の作り直し

- アイコン: [`tools/make_icon.py`](tools/make_icon.py)で`app.ico`を作ります. トレイのアイコン([`src/Theme.cs`](src/Theme.cs)の`CreateAppIconHandle`)も同じ絵に直します.
- READMEの動く画像: [`tools/make-demo.py`](tools/make-demo.py)で, 紹介のページのデモを`docs/images/`に書き出します.
- `docs/`のHTMLは, 別に置いた元のファイルから[`tools/build-manual.sh`](tools/build-manual.sh)で作ります(元のファイルはこのリポジトリに入っていません).

## ライセンス

[MIT](LICENSE) © 2026 maonthemoon
