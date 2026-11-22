# NearbyConnectionsUnity 実装計画

## 目的と初版の範囲

Google Nearby Connections を Unity の共通 C# API から使える UPM パッケージを作る。対象は iOS・Android のフォアグラウンドでの端末探索、相互承認、バイト列の双方向送受信、切断。初版の複数人サンプルはホスト1台と参加者2台の `Star` とする。この台数は最初の検証条件で、製品の最大接続数は実測で決める。

ゲームの位置同期、ホスト移行、再接続の方針、MessagePack、Unity Netcode 連携、ファイル・ストリーム転送は初版の対象外。パッケージは受け取ったバイト列を利用側に渡す。ゲーム側はプレイヤー ID、通信プロトコル、データの意味を定義できる。

| Strategy | 接続構成 | 導入順 |
| --- | --- | --- |
| `PointToPoint` | 1対1 | 最初の疎通確認 |
| `Star` | ホスト1台が複数の参加者に直接接続 | 初版の複数人サンプル |
| `Cluster` | 各端末が複数の相手に直接接続 | 混在実機で検証できた段階で公開。接続先の選択と中継は利用側が担当 |

## 最初の判断点：共有 Wi-Fi なしで成立するか

Google は iOS・Android 間の通信を案内している。iOS の現行セットアップ資料には BLE と Hotspot の記載があり、リポジトリの README には Wi-Fi LAN のみという記述も残る。ビルドに固定した SDK と実機の組み合わせで、探索・接続・データ転送をそれぞれ確認する。Android は Play 開発者サービスが使える端末を対象にする。

| 条件 | 検証内容 |
| --- | --- |
| 同一 Wi-Fi LAN、WAN 切断 | 最初の比較基準。iOS と Android で公開側と探索側を入れ替えて疎通 |
| 共有 Wi-Fi なし、モバイルデータなし、Wi-Fi と Bluetooth はオン | 外部ルーターや手動テザリングなしで、発見→相互承認→双方向転送まで成立するか |
| 一方の端末が手動でテザリング | 別条件として確認。利用者操作、モバイルデータ依存、接続の向きを記録 |
| iOS 同士、Android 同士 | 同じ SDK 設定で比較し、混在端末固有の失敗を切り分け |
| 3台の混在構成 | `Star` でホストを iOS と Android の両方で確認。参加者1台の切断後も残る接続で送受信できること |

ネイティブの小さなサンプルで先に測る。各条件で端末・OS・SDK の版、Android の Play 開発者サービス版、権限、strategy、公開側、成功した段階、接続時間、切断理由を記録する。判別できれば実際の通信方式も記録する。共有 Wi-Fi なしが成立しなくても LAN 対応の汎用ラッパーは進められる。その場合、ルーターなしを必須とするゲームでは別の通信方式の PoC が必要になる。

## リポジトリ構成と配布物

開発は1つのリポジトリにまとめる。Unity・Android・iOS の3つに作業を分け、他の Unity プロジェクトには UPM パッケージ1つを配布する。以下のディレクトリ名は実装時の案。

| 場所 | 役割 |
| --- | --- |
| `UnityProject/` | パッケージをローカル参照する検証用 Unity プロジェクト。実機のビルドと動作確認に使う |
| `Native/Android/` | Kotlin の Gradle ライブラリ。Nearby SDK を呼び出すブリッジを AAR にビルドし、配布パッケージへ組み込む |
| `Native/iOS/` | ネイティブ単体の疎通を確認する Xcode プロジェクト。配布する Swift と C ABI のブリッジはパッケージ側に置き、必要に応じて検証用ターゲットからも同じソースを参照する |
| `Packages/dev.naninunenoy.nearby-connections/` | 配布単位。C# 共通 API、Android の AAR、iOS のブリッジソース、ビルド処理、利用サンプルを含む |

利用アプリの Android ビルドでは Gradle が AAR と SDK 依存を解決する。iOS ビルドでは Unity が生成する Xcode プロジェクトで Swift ブリッジと Nearby SDK をビルドする。最初の通信方式の検証には、両 OS の公式ネイティブサンプルも使える。

## C# API の契約案

`NearbyClient.Create(NearbyOptions)` で初期化する。設定は `ServiceId` と `Strategy` を含む。通信相手同士で両方を一致させる。初版はアプリ内に有効なクライアントを1つとし、接続先は複数持てる。iOS の LAN 探索で使う Service ID はビルド時設定にも登録する。

| API・通知 | 契約 |
| --- | --- |
| `StartAdvertisingAsync(name)` / `StopAdvertisingAsync()`、`StartDiscoveryAsync()` / `StopDiscoveryAsync()` | 公開と探索を個別に操作する。停止しても確立済み接続は維持する |
| `EndpointFound` / `EndpointLost` | 探索で見えた端末の変化。接続状態は別に管理する |
| `RequestConnectionAsync(endpointId, localName)` | SDK が要求を受け付けた時点で完了。接続結果は別イベントで返す |
| `ConnectionInitiated`、`AcceptConnectionAsync` / `RejectConnectionAsync` | 両側に確認コードと要求 ID を渡す。承認・拒否は要求 ID 単位で一度だけ処理する |
| `ConnectionResult` / `Disconnected` | 成功・拒否・失敗と、確立後の切断を区別する。接続済み端末の一覧も取得できるようにする |
| `SendBytesAsync(endpointId, bytes)` / `BytesReceived` | 送信メソッドの完了は SDK の受付。受信イベントに送信元とメッセージ単位のバイト列を渡す。転送結果を公開する場合は送信 ID で対応づける |
| `DisconnectAsync(endpointId)` / `ShutdownAsync()` | 1接続の切断と全体終了。全体終了は探索・公開・接続と保留中の操作を片付ける |

- C# の状態は探索・公開の状態と接続先ごとの状態を分ける。発見の消失は接続済み端末を消さない。接続結果でのみ送信可能にする。端末 ID は永続 ID として保存しない。
- 公開 API とイベントは Unity のメインスレッドにそろえる。SDK のコールバックはネイティブ側でコピーしてキューに入れ、Unity 側で読み取る。処理件数とバイト数に上限を設け、あふれた場合は通知して対象接続を終了する。終了済みセッションの遅延通知は世代 ID で破棄する。
- 送信バッファは呼び出し時に取り込み、受信バッファは呼び出し側に所有権を渡す。共通ペイロード上限は固定 SDK の小さい方を実機で確認して定数化する。データはバイナリのまま橋渡しする。
- SDK から判断できる範囲で `PermissionDenied`、`UnsupportedPlatform`、`ServiceUnavailable`、`InvalidState`、`EndpointNotFound`、`PayloadTooLarge`、`NativeFailure` などに変換する。診断用に元のエラーコードや iOS のエラードメインも残す。
- サンプルは両画面のコードを利用者が照合して承認する。通常のシーン遷移ではクライアントをアプリ側が保持する。バックグラウンドに移行したら初版は通信を終了する方針とし、停止処理が中断されても復帰時に状態をリセットできるようにする。

## プラットフォーム別の実装

| 層 | 実装方針 |
| --- | --- |
| Unity | 共通の状態管理と API を `Runtime` に置き、プラットフォーム別アダプターを組み立て時に選ぶ。`Editor` にビルド処理、`Samples~` に接続サンプルを置く。Editor ではメモリ内のテスト実装を明示的に選ぶ |
| Android | Kotlin の薄いブリッジを AAR にし、C# の `AndroidJavaObject` から呼ぶ。`ConnectionsClient` の結果を共通イベントへ変換。AAR とは別に `play-services-nearby` 等の Maven 依存を Gradle に追加する。SDK と Android の版に応じた Manifest、実行時権限、縮小ビルドでのブリッジの保持を確認する |
| iOS | Swift の `ConnectionManager`、`Advertiser`、`Discoverer` を使い、C ABI と `DllImport("__Internal")` で接続する。送信はポインター＋長さから SDK 所有の領域にコピーし、受信を読み出す際は C# 側のバッファへ書く。Swift Package のリビジョンを固定し、生成 Xcode プロジェクトでブリッジをコンパイルするターゲットに依存をリンクする |
| iOS ビルド設定 | ビルド後処理で Xcode プロジェクトを更新し、必要な利用目的文字列と `NSBonjourServices` をアプリの Info.plist に追加する。Bonjour 名は登録済み Service ID から公式仕様で生成し、既存設定を維持する。Hotspot 等の追加 capability と署名は PoC で採用した通信方式に合わせる |

Android の依存追加方法と iOS の Swift ソース登録・リンク方法は、対象 Unity バージョンのクリーンなプロジェクトで早めに試す。同じ出力先への再ビルドで設定が二重にならないことも確認する。Unity、Xcode、iOS SDK リビジョン、Android の依存バージョン・min/target SDK は PoC の結果を記録してから固定する。iOS のビルドと署名には macOS と Xcode の環境を使う。

## 実装順と完了条件

| 段階 | 作業 | 完了条件 |
| --- | --- | --- |
| 0. 技術検証 | ネイティブのサンプルで通信条件を確認。別途、空の Unity プロジェクトで Android AAR と iOS C ABI から固定値を返す | 対象 SDK の組み合わせ、成功するネットワーク条件、両 OS のビルド手順を記録できた |
| 1. 2台の最小実装 | UPM の骨格、公開 API、両 OS のアダプター、コード照合画面、バイナリのエコーを実装 | iOS・Android の役割を入れ替え、発見から切断まで通る。切断後に同じアプリで再接続できる |
| 2. 複数人 | 接続ごとの状態、`Star` のサンプルと実機検証。`Cluster` は同一端末が複数の直接接続を持つ構成を検証 | `Star` で参加者2台が各自送受信でき、片方の切断がもう片方に影響しない。検証できた strategy を対応表に記載できた |
| 3. 異常系と計測 | 権限拒否、コード不一致からの拒否、途中切断、終了と再初期化、バックグラウンド復帰を確認 | 操作が保留のまま残らず、不正な状態で送信できない。連番付き小ペイロードを双方向に毎秒10・20・30回送る例で、RTT の中央値と p95、欠落、受信キューの深さを記録した |
| 4. 配布 | Git URL でインストールできる UPM、接続サンプル、セットアップ手順と実機対応表を用意 | 新規 Unity プロジェクトに追加し、手順に沿って両 OS の IL2CPP ビルドを作成し、実機で疎通できた |

測定の RTT は送信側の単調時計でエコー応答を測る。計測値と実際のゲームの合格基準は分けて記録する。実機対応表には検証日、SDK バージョン、台数、役割、環境、結果を載せる。

## 参照資料

- [Google Nearby Connections の接続方式](https://developers.google.com/nearby/connections/strategies)
- [Android のセットアップと権限](https://developers.google.com/nearby/connections/android/get-started)
- [Android ConnectionsClient の API 仕様](https://developers.google.com/android/reference/com/google/android/gms/nearby/connection/ConnectionsClient)
- [iOS のセットアップ、権限、Bonjour](https://developers.google.com/nearby/connections/swift/get-started)
- [Swift の接続管理](https://developers.google.com/nearby/connections/swift/manage-connections)・[データ転送](https://developers.google.com/nearby/connections/swift/exchange-data)
- [google/nearby の通信方式の実装状況](https://github.com/google/nearby/blob/main/connections/README.md)
- [Unity の iOS 用ネイティブプラグイン](https://docs.unity3d.com/ja/current/Manual/ios-native-plugin-create.html)
