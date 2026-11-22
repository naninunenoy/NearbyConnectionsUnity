# Nearby Connections for Unity

Unity 6.3 の Android プレイヤーから Google Nearby Connections を使う UPM パッケージです。公開・探索、確認コードによる相互承認、バイト列の送受信、切断を提供します。接続方式は 1 対 1 の `PointToPoint` と、ホストに複数の参加者を接続する `Star` です。

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
