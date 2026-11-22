# Nearby Connections for Unity

Unity 6.3 から Google Nearby Connections を使う UPM パッケージです。iOS 対応は実装済みで、実機検証はこれからです。公開・探索、確認コードによる相互承認、バイト列の送受信、切断を提供します。接続方式は 1 対 1 の `PointToPoint` と、ホストに複数の参加者を接続する `Star` です。

## Android で試す

1. Unity Hub で `Unity/` を開きます。Unity 6000.3.29f1 の Android Build Support、SDK、NDK、OpenJDK を使います。パッケージはローカル参照で読み込まれます。
2. **File > Build Profiles** で Android に切り替え、`Assets/Scenes/SampleScene.unity` を含めて APK をビルドします。Gradle が Google Maven と Maven Central から依存関係を取得します。
3. Google Play 開発者サービスを利用できる Android 端末に APK を入れます。2 台で基本操作を試せます。Wi-Fi と Bluetooth をオンにし、アプリの権限を許可します。権限ダイアログが閉じたら、操作ボタンをもう一度押します。

## デモの操作

デモは `Assets/Scenes/SampleScene.unity` の 1 シーンです。上部の **Echo**、**Lobby**、**Shared scene** で表示を切り替えます。Echo とロビーは別の通信セッションなので、切り替えるときは **End session** を押します。Lobby と Shared scene は接続したまま行き来できます。

接続するときは 1 台で **Host**、ほかの端末で **Join** を押し、参加側でホストを選びます。両端末に表示された確認コードが一致したら、それぞれで **Codes match** を押します。

| タブ | 端末と方式 | 確認する操作 |
| --- | --- | --- |
| **Echo** | 2 台で `PointToPoint`。複数参加は `Star` | **Send echo** で返信のトークン、バイト数、往復時間を確認します。両側から送信できます。**Disconnect** で切断し、再接続も確認できます。 |
| **Lobby** | ホスト 1 台と参加者 1 台以上で `Star` | 参加者一覧と準備状態を確認し、テキストや写真を送ります。3 台あれば、参加者のメッセージがホスト経由でもう 1 台に届くことを確認できます。 |
| **Shared scene** | Lobby と同じ `Star` セッション | Lobby で全員が **Ready** を押し、ホストが **Start scene** を押します。任意の端末からタイルをタップしてブロックを動かし、**Change block color** で色を変えます。あとから参加した端末にも現在の位置と色が表示されます。 |

初めて写真を送る端末では、接続前に Lobby の **Enable photos** を押して写真へのアクセスを許可します。接続後に **Photo** から端末内にある最近の写真を選び、プレビューを確認して **Send photo** を押します。チャット上のサムネイルはタップで拡大できます。送信写真は長辺 768 px 以下、240 KiB 以下の JPEG に変換します。参加後のチャットを端末内に最大 24 件表示します。

Echo デモは MessagePack for C# `4.0.0-rc.1` で要求と返信をエンコードします。必要な DLL とソースジェネレーターはデモ用 Unity プロジェクトに同梱しています。旧形式の Echo とは互換性がないので、確認には両端末で同じ版のデモを使ってください。

`Unity/Assets/Plugins/MessagePack/` の DLL は NuGet 版です。MessagePack と SerializerFoundation `1.4.1` は `netstandard2.1` を使用しています。Microsoft の依存 DLL は `8.0.0`（System.Text.Json は `8.0.5`、System.Runtime.CompilerServices.Unsafe は `6.1.2`）です。

### デモの構成

- `DemoConnection` が Nearby クライアント、探索、接続承認、バイト列の送受信を扱います。
- `DemoEcho` と `DemoGroup` が MessagePack のメッセージとデモの状態を扱います。ロビーと共有シーンでは、ホストが参加者、準備状態、ブロックの位置と色を配信します。
- `NearbyDemo` が 3 つのタブとセッションをつなぎ、`DemoView`、`DemoGroupView`、`DemoAlbumView` が標準 uGUI で表示と入力を扱います。写真ライブラリへのアクセスはデモの `DemoAlbum` にあります。

## アプリへの組み込み

Package Manager の **Add package from disk** で `Packages/dev.naninunenoy.nearby-connections/package.json` を指定します。Android ブリッジはソースを同梱した `.androidlib` です。Unity の Gradle ビルドがブリッジをコンパイルし、`com.google.android.gms:play-services-nearby:19.5.1` を解決します。

- Android の最小 API レベルは 24 以上に設定します。この検証プロジェクトの最小 API レベルは 25 です。
- Unity メインスレッドで `NearbyClient.GetMissingAndroidPermissions()` を呼び、必要に応じて `UnityEngine.Android.Permission.RequestUserPermissions` を使います。権限を得てから `NearbyClient.Create` を呼び、両端末の `ServiceId` と `Strategy` をそろえます。
- `ConnectionInitiated` の確認コードを利用者に見せ、照合後に要求 ID で承認または拒否します。送信は `ConnectionResult` が `Connected` になったあとに開始できます。メソッドとイベントは Unity メインスレッドで扱います。
- `SendBytesAsync` の完了は SDK の送信受付です。相手への到達を確認する場合はサンプルと同様に応答を返します。1 メッセージの上限は `NearbyClient.MaxBytesPayloadSize` です。
- バックグラウンド移行時にセッションは終了します。復帰後はクライアントを作り直します。`Faulted` の端末 ID が `null` の場合もセッション終了を表します。

[Android の権限とセットアップ](https://developers.google.com/nearby/connections/android/get-started) · [Nearby Connections の API 仕様](https://developers.google.com/android/reference/com/google/android/gms/nearby/connection/ConnectionsClient)

## iOS で試す

iOS 向けのブリッジは実装済みですが、iOS ビルドと端末間通信はまだ検証できていません。現在は BLE と同一 Wi-Fi LAN を対象とし、Hotspot による接続は有効にしていません。

1. Unity Hub で `Unity/` を開き、Unity 6000.3.29f1 の iOS Build Support を使って iOS に切り替え、`Assets/Scenes/SampleScene.unity` をビルドします。
2. 生成された Xcode プロジェクトで署名と実機を設定します。ビルド時に Google Nearby Connections の Swift Package（リビジョン `15241799d2e26722128f6f9815951eb1d40b6110`）が必要です。Xcode はネットワークから依存関係を取得します。
3. iOS または Android の端末で、上の「デモの操作」に沿って接続します。実機ごとに Bluetooth とローカルネットワークの使用を許可してください。

利用アプリでは **Assets > Create > Nearby Connections > iOS Build Settings** から設定アセットを `Assets` 内に一つ作り、`Service Ids` に使用する全 Service ID を登録してください。利用目的の文言は用途に合わせて変更します。サンプルには設定済みです。iOS ビルド時に `Info.plist`、Swift ブリッジ、SDK 依存関係を設定します。既存の利用目的や Bonjour 設定は保持します。

接続方式や端末間の互換性、再接続は実機での確認が必要です。[Google の iOS セットアップ](https://developers.google.com/nearby/connections/swift/get-started)も参照してください。
