# Nearby Connections for Unity

Unity 6.3 から Google Nearby Connections を使う UPM パッケージです。iOS 対応は実装済みで、実機検証はこれからです。公開・探索、確認コードによる相互承認、バイト列の送受信、切断を提供します。接続方式は 1 対 1 の `PointToPoint` と、ホストに複数の参加者を接続する `Star` です。

## Android で試す

1. Unity Hub で `Unity/` を開きます。Unity 6000.3.29f1 の Android Build Support、SDK、NDK、OpenJDK を使います。パッケージはローカル参照で読み込まれます。
2. **File > Build Profiles** で Android に切り替え、`Assets/Scenes/SampleScene.unity` を含めて APK をビルドします。Gradle が Google Maven と Maven Central から依存関係を取得します。
3. Google Play 開発者サービスを利用できる Android 端末 2 台に APK を入れます。Wi-Fi と Bluetooth をオンにして、アプリの権限を許可します。権限ダイアログが閉じたら操作ボタンをもう一度押します。
4. 両方で **Point to Point** を選び、片方で **Host (advertise)**、もう片方で **Join (discover)** を押します。参加側に表示されたホストを選びます。
5. 両画面のコードを照合し、一致したら両方で **Codes match — accept** を押します。接続後に **Send echo** を押すと、相手が返した 20 バイトの一致と往復時間を表示します。両端末から送信できます。
6. **Disconnect** を押して切断し、参加側から再接続できることを確認します。**End session** で公開・探索・接続をまとめて終了できます。

3 台で試す場合は全台で **Star** を選び、参加者 2 台を同じホストに接続します。各参加者と送受信し、片方を切断したあとに残りの参加者とも送受信します。共有 Wi-Fi の有無は別の試行として記録してください。

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
3. iOS または Android の端末で、上記 Android 手順と同じように公開・探索し、確認コードを照合して接続します。実機ごとに Bluetooth とローカルネットワークの使用を許可してください。

利用アプリでは **Assets > Create > Nearby Connections > iOS Build Settings** から設定アセットを `Assets` 内に一つ作り、`Service Ids` に使用する全 Service ID を登録してください。利用目的の文言は用途に合わせて変更します。サンプルには設定済みです。iOS ビルド時に `Info.plist`、Swift ブリッジ、SDK 依存関係を設定します。既存の利用目的や Bonjour 設定は保持します。

接続方式や端末間の互換性、再接続は実機での確認が必要です。[Google の iOS セットアップ](https://developers.google.com/nearby/connections/swift/get-started)も参照してください。
