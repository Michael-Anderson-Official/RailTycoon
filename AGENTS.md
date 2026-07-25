# AGENTS.md

Claude Codeでの作業から引き継ぎ。READMEに無い「実務上のやり方」をここにまとめる。
コード自体のWHY(設計判断の理由)はソース中のコメントに書き込み済みなので、ここでは
繰り返さない。

## プロジェクト概要

Unity 6 (6000.3.20f1)製、京王線モデルの鉄道経営シミュレーション。シーン・UI・メッシュは
全てコード生成(prefab無し)。決定的な固定tickシミュレーション(`Bootstrap.TickSeconds`)。
Unityエディタ本体: `C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe`

## 2026-07-25時点の状況

- `master`と`gh-pages`は同期済み(masterの内容がそのまま公開ページに反映されている)。
  以前あった「UI刷新が未デプロイ」「wasm-opt異常終了で失敗した生成物が残っている」
  という積み残しは解消済み
- `wasm-opt`異常終了・Brotliの`not enough memory`は、このPCの空きメモリ逼迫
  (物理7.65GBで空き1GB前後)による一時的な失敗。**コード起因ではない**ので、
  失敗しても中途生成物はコミットせずビルドを再実行すれば通る
- 残っている実機確認: UI/UX刷新以降のブラウザ実操作(下記「変更→検証」の4項)。
  特に起動トースト表示中にオンボーディングのアクションを押せるか

## 変更→検証→コミットの手順

1. コード変更後、**必ず**以下を実行してから次に進む(1つでも失敗したら原因を直してから
   進む。テストを削って通すことはしない):
```
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults "Logs/edittest.xml" -logFile "Logs/edittest.log"
```
   結果は`Logs/edittest.xml`の`<test-run ... failed="0" ...>`で確認する(現在160件PASS
   +1件Ignore)。
2. 起動経路とコード生成UIを触る変更ではPlayModeも必ず実行する(現在3件PASS):
```
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults "Logs/playtest.xml" -logFile "Logs/playtest.log"
```
3. レガシーバッチも合わせて実行する(NUnit化される前からある手動チェック。
   Debug.Logで"PASS"/"done"を出し、失敗時は非ゼロ終了):
```
Unity.exe -batchmode -nographics -projectPath . -executeMethod TrackTest.Run -logFile "Logs/tracktest.log" -quit
Unity.exe -batchmode -nographics -projectPath . -executeMethod BlockTest.Run -logFile "Logs/blocktest.log" -quit
```
4. UI/WebGL変更は自動テストだけで完了扱いにしない。ローカルHTTP配信したWebGLを
   ChromeでPC 1440×900、スマホ縦390×844、スマホ横844×390相当で開き、次を実操作する:
   - canvasがviewport全体を使い、セーフエリア・上部HUD・下部ナビが切れない
   - 起動トースト表示中でもオンボーディングのアクションを押せる
   - 長い一覧と番線設定を最後までスクロールでき、閉じるボタンが画面内にある
   - モード変更、駅建設、線路の始点/終点選択、確認ダイアログがタップで進む
5. 検証用に一時的なEditorスクリプト(`Assets/Editor/XxxProbe.cs`)を作って
   `-executeMethod`で単発確認するのは良いやり方(このセッションでも多用した)。
   ただし**コミット前に必ず削除する**(`.cs`と`.cs.meta`の両方)。
6. `Logs/`配下に生成した一時ログ(`.xml`/`.log`)もコミット前に削除する
   (Logs自体はリポジトリ内にあるが、テスト実行のたびに増える一時ファイルは残さない)。
7. コミットメッセージは日本語、詳細に。「ゲーム仕様上の変更」(プレイヤーに見える挙動)と
   「技術的変更」(型・内部構造等)を分けて明記する。型変更等があった場合、
   「変更なし」とだけ書かず具体的に書く(過去に「意図的に変更した挙動: なし」とだけ
   書いて指摘を受けた経緯あり)。

## デフォルト公開方針(ユーザー承認済み)

コード変更タスクは、必要なテストとCodexレビューが成功したら追加確認を挟まず
`origin/master`へpushし、続けてWebGLを再ビルドして`gh-pages`へデプロイする。
リモート先行・分岐、テスト失敗、ビルド失敗、秘密情報や意図しないファイルの混入を
検出した場合だけ公開を止めて報告する。下記の安全手順とforce push禁止は常に守る。

## GitHub push(origin/master)の安全手順

このリポジトリはGitHub上で公開されている(Michael-Anderson-Official/RailTycoon)。
pushは以下の順序を省略・入れ替えせず必ず守る:
1. テスト(上記)が全て通ることを確認
2. `git diff`で意図しないファイル混入が無いか確認
3. コミット
4. `git fetch origin`
5. `git rev-list --left-right --count origin/master...master`で分岐確認
   (左側=remote側のみが1以上なら、pushせず停止して報告する)
6. `git log --oneline origin/master..master`で意図しないコミット・生成物・秘密情報を点検
7. 安全なら`git push origin master`
8. push後`git rev-parse HEAD`と`git rev-parse origin/master`が一致することを確認

**force push禁止**(`--force-with-lease`も含め、明示指示がない限り)。remote側が
先行/分岐していたら、勝手にmerge/rebase/pullせず停止して報告する。

## WebGLビルド→デプロイ(gh-pages)は別工程・自動連携なし

`master`へpushしても、実際に遊べるGitHub Pages側(`gh-pages`ブランチ)は**自動更新されない**。
コード変更を公開ページへ反映するには、`master`をpushした後に毎回以下を行う:

1. WebGLビルド(**時間がかかる: 数分〜10分程度**。バックグラウンドの監視タスクが
   途中で強制終了されることがあるため、下記のように完全に切り離して実行すること):
```bash
nohup "/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -executeMethod WebGLBuild.Run -logFile "Logs/webglbuild.log" -quit > /dev/null 2>&1 < /dev/null &
disown
```
   その後、`tasklist | grep Unity.exe`が消えるまで別の監視コマンドで待つ。
   低メモリ環境ではBrotli圧縮が「not enough memory」で失敗することがあるが、
   `wasm-opt`異常終了も含め一時的な問題がある。失敗後の`Builds/WebGL`は一部だけ
   更新されている可能性があるためコミットせず、そのままビルドを再実行する。
   ログ末尾の`Build Finished, Result: Success`と`WebGLBuild: Succeeded`を両方確認する。
2. `Builds/WebGL`は**それ自体が独立したgitリポジトリ**(リモート未設定)で、その
   `master`ブランチの内容がそのままGitHub Pages(`gh-pages`ブランチ)の中身になる。
   ビルド後、変更されたファイル(`Build/*.unityweb`等)だけをコミットし、
   明示URLで`gh-pages`へpushする:
```bash
cd Builds/WebGL
git add -A && git commit -m "WebGL build: <変更内容> (master <コミットハッシュ>)"
git fetch https://github.com/Michael-Anderson-Official/RailTycoon.git gh-pages
git rev-list --left-right --count FETCH_HEAD...master   # 0\t1ならfast-forward安全
git push https://github.com/Michael-Anderson-Official/RailTycoon.git master:gh-pages
```
3. push後、`git ls-remote <url> gh-pages`のハッシュが`git rev-parse HEAD`と一致することを
   確認する。

## Codexレビュー

ユーザーはCodexによる実装後レビューを毎回入れる運用を希望している(利用上限に
達している場合はスキップしてよいが、その旨を明示すること)。
**2026-07-25にstop前の自動レビューゲートを有効化済み**(`/codex:setup --enable-review-gate`)。
以後は作業の区切りで自動的にレビューが走る。`c617fea`の全面変更は
複数回レビューし、Materialリーク、運転台高さ、トースト入力、横画面モーダル、
縦画面の全体表示FOVを修正した後に「actionable regressionなし」を確認済み。
`3113829`も追加レビューで指摘なし。これらより前のコミット群(台車追従修正〜
通過駅対応)は、履歴単位のレビューとしては未実施。

## Codexレビューの実行方法(この環境での注意)

`--scope branch`は**この環境では動かない**。Codexのサンドボックスが`git diff`用の
pwshを起動できず(`CreateProcessAsUserW failed: 5 アクセスが拒否されました`)、
`Starting Codex review thread`のまま10分以上進まない。

動くのは`--scope working-tree`のみ。既にコミット済みの変更をレビューしたい場合は、
その差分を一時ブランチ上で未コミット状態として再現してから掛ける:

```bash
git checkout -b review-tmp <レビュー基点>
git checkout <レビュー対象> -- <対象ファイル…>   # 差分が未コミットの状態になる
node ".../codex-companion.mjs" review --scope working-tree > Logs/review.txt 2>&1
git checkout -- Assets/ && git checkout master && git branch -D review-tmp
```

その他:
- 出力を`| tail`へ通すと終了まで何も見えない。ファイルへリダイレクトすること
- ラッパーがkillされても`codex.exe`は孤児として残り、再試行のたびに積み上がって
  空きメモリ(この環境は1〜2GB)を圧迫する。中断したら
  `Get-Process codex | Where-Object {$_.WorkingSet64 -gt 50MB} | Stop-Process -Force`
  で掃除してから再試行する

## アーキテクチャ早見表

- `Train.cs`: 列車の状態機械(Dwell/Run)、発着処理(`TryDepart`/`Arrive`)、経路構築
  (`BuildLeg`/`BuildMultiLeg`)、台車追従(`PlaceCarsStatic`)
- `TrackSegment.cs`: 駅間の閉塞(`TryEnter`/`Leave`)、`TrackNetwork`(駅・線路・列車の
  台帳、`Reachable`/`FindPath`)
- `Station.cs` / `StationLayout.cs`: 駅の面数・番線構成、番線予約(`TryReserve*`)、
  ホーム縁(乗降モード)
- `RailDimensions.cs`: 軌間・レール頭頂・車体幅・ホーム離隔・床高さ・車輪/台車高さの
  共通寸法。駅・線路・車両の数値を変更するときはここを起点にし、各クラスへ直書きしない
- `RailKit.cs`: メッシュ生成共通部品、曲線生成(`SmoothConnectPath`=PI法の駅間カーブ、
  `HermitePath`=フォールバック)
- `SaveLoad.cs`: JSONセーブ(PlayerPrefs/WebGLはIndexedDB)。バージョン付きスキーマ
  (現在v4)、v1からの多段migration、ロード前の全件Validate→Apply(トランザクショナル)
- `BuildController.cs` / `UIController.cs`: 建設・経路構築のモード管理とuGUI
- `CameraRig.cs`: 地図パン/ズーム/回転、駅フォーカス、FOVとaspectを考慮した全路線表示、
  前面展望
- `WebGLBuild.cs`: WebGLビルドと、全画面・safe-area対応HTML/CSSの後処理

## UI/UX・実寸の非退行ルール

- 京王線モデルの軌間は1372mm。車輪・レール・車体・ホームは`RailDimensions`の同じ
  座標基準で組む
- 現在のゲーム内目標値は車体/ホーム水平隙間80mm、車両床がホームより10mm高い状態。
  駅構内道床はホームへ食い込まない専用幅を使う
- 主要タップ領域は高さ54以上。縦画面は下部シート、横画面は左右パネルとし、長い内容は
  `ScrollRect`へ入れる。固定高モーダルを画面高より大きくしない
- 破壊操作(駅撤去・系統廃止・初期化)は共通確認ダイアログを通す。ダブルタップ確認や
  常設の危険ボタンへ戻さない
- トーストは入力を遮らず、オンボーディング表示中はその上へ退避させる
- UIは見た目のスクリーンショットだけでなく、トースト表示中のタップや縦横切替後の
  実操作まで確認する

## 未解決: 「走行経路がレール上にある」保証がテストで守られていない

走行経路と描画レールは中心線を単一の出所にして統一済み(実測で最大距離0.000m)。
しかし**この保証を守る自動テストが無い**。経緯:

- 最初に書いたテストは、比較対象を`Station.TrackCentreLocal`/`TrackSegment.SideCentre`
  (=走行経路と同じ出所)にしていたため循環しており、描画側だけが壊れた場合を
  検出できない。Codexレビューで妥当な指摘を受けた
- 生成されたレールメッシュ(`TrackWork/Rail`と segment の`Rail`)の頂点と
  突き合わせる版へ書き換えたところ、最寄りレールまで25.5mという想定外の値が出た。
  `RailKit.MeshGO`は`SetParent(parent,false)`でローカル原点に置くため
  `TransformPoint`で正しいはずだが、原因を特定できていない
- 不完全なテストをmasterへ残さない判断で、テストは入れていない

**次にやること**: メッシュ頂点ベースの検証を成立させる。25.5mが
テスト側の不備なのか、実際に経路がレールから外れる箇所があるのかを先に切り分ける
(`OnRailsProbe`相当の使い捨てバッチで中心線比較は0.000mだったので、テスト側の
不備の可能性が高いが、確認していない)。

## 未解決: 列車が渡り線の描画形状を辿っていない

渡り線の描画はS字の分岐へ作り直した(`RailKit.CrossoverPath`)が、列車が渡る際の
通り道はリード区間内の線形補間のままで、描画された分岐を厳密には辿っていない
(実測で両レールの中間を通るため最大1.725m離れる)。
`CrossoverPath`を走行経路側からも使えば解消できるはず。

## 直近の変更(新しい順)

- ホーム端を線路の収束に合わせて絞り、駅を建てる位置に当たり判定を追加
  (既存駅・既設線路との重なりを建設時に拒否)
- 駅間の線路が途中の駅のホームを貫通する問題を建設時に防止(`3b4a2ef`)。
  再現条件は「間に別の駅があるのに両側の駅を1本の線路で直結した場合」
- 起動トーストをオンボーディングの上へ退避し、初回アクションを即操作可能化(`3113829`)
- UI/UXを全面刷新。セーフエリア対応HUD、下部ナビ、段階式の駅/線路/系統操作、
  スクロール一覧、カメラツール、確認ダイアログ、初心者ガイドを追加(`c617fea`)
- `RailDimensions`で1372mm軌間、ホーム隙間、床高さ、車輪/台車位置を統一。駅構内道床の
  ホーム食い込み、停車時の大きな隙間、台車の二重移動、運転台視点を修正(`c617fea`)
- 駅ホームを多層化し、上屋・点字ブロック・駅名標・ベンチ・駅舎ガラス等を追加
- 線路建設時にバラスト軌道／スラブ軌道を選択可能化、セーブデータにも保存
- 系統作成に駅検索UI追加＋通過駅(スキップストップ)対応(FindPath/BuildMultiLeg/
  SaveLoad v4)
- 駅間カーブをPI法(接線交点)による滑らかな線形に変更、継ぎ目の隙間解消
- 駅間の線路を直線→曲線化、渡り線ルート修正、台車の個別追従化
- 線路タップのタッチ直後ゴーストクリック対策
- ホーム長・両数のずれ修正

いずれも`git log --oneline`で追跡可能。各コミットメッセージに検証内容を明記している。
