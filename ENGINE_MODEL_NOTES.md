# Open Engine Simulator エンジンモデル作成メモ

2026-10-11。Steering Trainerに同梱している実装・定義から抽出した情報。
採用コア: zabayone/open-engine-sim、コミット `9abd876e8521a39ad3b423bf03a4b2325d4f8338`。
エンジン定義は実測車両の保証値ではなく、同梱スクリプトの値。

## 使う定義と入口

パスは `native/open-engine-sim/assets/` からの相対パス。

| 選択ID | エンジン | ファイル | エンジンnode | 計算Hz | 表示redline / 点火リミットrpm |
| --- | --- | --- | --- | --- | --- |
| 0（標準） | Honda B18C5 VTEC 直4 | engines/atg-video-1/05_honda_vtec.mr | honda_vtec_i4 | 20,000 | 8,400 / 9,400 |
| 1 | Toyota 2JZ 直6 | engines/atg-video-2/03_2jz.mr | t2jz | 10,000 | 6,000 / 6,500 |
| 2 | GM LS V8 | engines/atg-video-2/07_gm_ls.mr | ls_v8 | 10,000 | 6,500 / 6,800 |
| 3 | HR15DE（推定固定カム直4） | engines/steering-trainer/hr15de.mr | hr15de_i4 | 20,000 | 7,000 / 7,000 |

HR15DEは `native/EngineModels/hr15de.mr` からビルド時にコピーする。
入力値・調整後の値・確認結果は [HR15DE_MODEL.md](HR15DE_MODEL.md) に記録。
実車公表仕様と推測値は [車両メモ](../../memory/profile/マーチニスモS/車両・エンジン情報.md) を参照。

各ファイルの末尾に `public node main` がある。Hondaは `integra_type_r()` と
`integra_type_r_transmission()`、2JZは `supra_vehicle()` と `supra_transmission()` を使用。
ブリッピングはニュートラル・ダイノ無効の空ぶかしとして行うが、ラッパーでは
engine / vehicle / transmission の3オブジェクトを必要とする。

```text
import "engine_sim.mr"
units units()
constants constants()
impulse_response_library ir_lib()

// 配線、カム、ヘッド、クランク、気筒、吸排気、点火、車両、変速機を定義
public node main {
    set_engine(my_engine())
    set_vehicle(my_vehicle())
    set_transmission(my_transmission())
}
```

これは入口の骨格であり、この断片単体ではモデルを生成できない。
既存モデルをコピーし、構造を維持して数値から変更するのが確実。

## 作る前に用意する情報

| 部分 | 必要な情報・定義 |
| --- | --- |
| 基本構造 | 気筒数、バンク角、ボア、ストローク、気筒ごとのロッドジャーナル角、点火順序 |
| クランク・フライホイール | 質量、回転慣性、フライホイール質量・半径、摩擦トルク、上死点の角度 |
| ピストン | 質量、圧縮高さ、ピン位置、ピストン形状による容積補正、blowby |
| コンロッド | 長さ、質量、重心、慣性モーメント |
| バンク・圧縮比 | デッキ高さ、燃焼室容積、ピストン容積補正。圧縮比はこれらの幾何から整合させる |
| ヘッド | 吸排気ポートの流量対リフト表、ランナー容積・断面積、燃焼室容積 |
| カム | リフト、作用角、ローブ形状、中心角、進角、ベース円半径、気筒ごとのローブ角 |
| VTEC等 | 通常/高速用カム・ローブ、切替条件。既存のvtec_valvetrainとそのデフォルトも確認する |
| 吸気 | プレナム容積・断面積、ランナー長、流量、アイドル流量、閉スロットル位置、速度減衰 |
| 排気 | 管長、容積、一次管/出口流量、速度減衰、気筒への割当、音量、インパルス応答 |
| 燃焼 | fuelパラメータ（乱流効果、燃焼効率等）。省略値はライブラリ定義を確認する |
| 点火 | rpm対進角テーブル、配線と720度サイクル内の点火位相、rev_limit、limiter_duration |
| 始動 | starter_torque、starter_speed。正負の回転方向を既存定義に合わせる |
| 計算 | simulation_frequency、throttle_gamma、必要ならnoise/jitter等 |
| 付属 | vehicleとtransmission、ライブラリimport、音声IRファイルと依存ファイル |

トルク曲線だけではこのコアのモデルは作れない。気筒内の燃焼、吸排気、機構を定義する。
実測のトルク曲線、空ぶかし応答、アイドルrpmなどは作成後の調整・検証の基準になる。

## Honda定義から取り出した初期値

- ボア81 mm、ストローク87.2 mm、ロッド長5.430 inch、圧縮高さ1 inch。
- クランク質量35.5 lb、フライホイール10 lb。
- 合成回転慣性 `0.22986844776863666 * 0.5`（約0.114934 kg m²）。
- クランク摩擦 `1.0 * units.lb_ft`。
- ピストン303.5 g、コンロッド395.837 g、ロッド慣性0.0015884918028487504。
- ジャーナル角0 / 180 / 180 / 0度、バンク角0度。
- 吸気プレナム1.325 L、断面20 cm²、ランナー長7 inch。
- 吸気流量k_carb(800)、ランナーk_carb(250)、idle_flow_rate k_carb(0)、閉位置0.9989。
- 排気出口k_carb(1000)、一次管長10 inch、一次流量k_carb(200)、容積100 L。
- スターター70 lb ft、速度-500 rpm、throttle_gamma 2。
- `honda_vtec_head` の吸排気流量表、通常/高速カム4本、`timing_curve` の点火進角表は
  元ファイルをコピーしてから変更する。VTECヘッドや複数カムを省くと特性が変わる。
- HondaではVTEC切替条件を明示しないので `assets/es/objects/objects.mr` の
  `vtec_valvetrain` デフォルト（min_rpm 5,800 rpm等）を継承する。

`k_carb` と `k_28inH2O` は異なる基準圧の流量変換。
単位を省略してmm/inch、lb/kg、rpm/rad/s、degree/radianを混ぜないこと。

## アプリとの接続・設定の反映

`native/SteeringTrainerEngineBridge.cpp` の `EngineScripts[]` でモデルIDをパスに対応させる。
追加時はC#の `EngineNames`、タコメーター範囲・redline、設定保存のID検証も変更する。
ABIは `steering_engine_create(assets, engineId, flywheel, friction, sensitivity, openResponseSeconds, closeResponseSeconds)`。
モデルIDを加えたため古いC#と古いDLLを混在させない。

生成時は元スクリプトを一時ファイルへコピーし、以下を変更してコンパイルする。

- 摩擦: 元の `friction_torque` に摩擦倍率を掛ける。
- 2JZ/V8慣性: `crank_moment + flywheel_moment * 倍率 + other_moment`。
- Honda慣性: 元定義はフライホイール分が分離されていないため、合成回転慣性全体に倍率を掛ける。
  フライホイールだけを厳密に調整するモデルを作る場合は、慣性を各部に分けて定義し直す。
- HR15DE慣性: `0.097 + 0.083 * 倍率 + 0.0`。摩擦はN·m単位の追加クランク摩擦に掛ける。
- この置換は登録4モデル専用。新しい式・単位に変えた場合は置換コードも対応させる。
- ペダルは0～1、指数感度 `u^gamma` と一次遅れを適用し、定義のthrottle_gamma=2を
  `setSpeedControl(pow(effectiveThrottle, 1/nativeGamma))` で補償。Honda・2JZ・V8は2、HR15DEは1.5。
  開閉時定数は別に設定でき、0秒は追加遅れなし。異なるthrottle linkageでは補償を見直す。

回転方向はスターター・カム位相・点火とセットで確認する。コアの表示rpmは絶対値。
コアは定義のstarter_speedを反転してスターターに渡す。Hondaの定義値は負だが実始動方向は正、HR15DE等の定義値が正なら実始動方向は負になる。ブリッジは始動後の実角速度の符号を取得してリセットに使う。スターター指定値の符号をクランクへ直接コピーしない。練習のRPM保持はコアのdynamometer拘束を使用し、クランク速度の毎ステップ上書きを避ける。

モデル作成→点火ON→スターターON→回転上昇確認→スターターOFF→0.5秒回転維持確認の順。
6秒以内に始動を試み、スターターを切った後に400 rpm未満なら作成失敗として扱う。
始動中はユーザー入力を渡さない。モデル内のアイドル吸気設定で回転が決まる。
低摩擦モデル等でアイドルが高い場合はidle流量・閉スロットル位置・点火を見直す。

ステップ入力は20 ms単位（50 Hz）。内部計算はモデルのHzでsubstep、画面は約30 Hz。
音声バッファは読み捨てているため、この画面のエンジン音は再生しない。
設定保存先は `%LOCALAPPDATA%/SteeringTrainer/engine-sim-settings.json`。

## 作成後の確認

1. importとIRファイルが解決でき、mainから3オブジェクトが生成できる。
2. スターターを切っても自走し、閉スロットル時に想定アイドルへ収束する。
3. ペダルを踏むとrpmが上がり、戻すと遅れを経て落ちる。
4. リミッター、VTEC切替、慣性・摩擦変更時の応答が意図に合う。
5. 計算1ブロックの実行時間が20 ms以内に収まる。超える場合は実時間追従を確認する。
6. 各エンジンへの切替、再生成失敗、再起動時の選択復元を確認する。

`native/engine_bridge_smoke.cpp` は3エンジンの生成・空ぶかし・調整モデル再生成を確認する。
今回の実行では全3モデルと調整モデルが始動し、85%入力で基準回転から300 rpm以上上昇した。
無入力の初期回転はHonda約783、2JZ約1,086、V8約818 rpm。
100 ms分の計算時間はHonda約52～57 ms、2JZ約39～43 ms、V8約54～64 ms。
これは当PCでの短い確認の値で、UI等の負荷を含む常時性能の保証ではない。
本体ビルドは `native/BuildNative.ps1` を経由。CMakeの作業ディレクトリはASCIIパスを使用。
配布時は元のMIT LICENSEと著作権表記を維持する。固定コミットや依存ライセンスは
`native/OPEN_ENGINE_SIM_PIN.md` を参照する。
