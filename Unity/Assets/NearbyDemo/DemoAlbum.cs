using System;
using System.Threading.Tasks;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using UnityEngine;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
#endif

internal static class DemoAlbum
{
    private const int Limit = 18;
    public const int ThumbnailSide = 144;

    public static bool Available =>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        true;
#else
        false;
#endif

    public static bool HasAccess()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var permissions = PhotoPermissions();
        foreach (string permission in permissions)
            if (Permission.HasUserAuthorizedPermission(permission)) return true;
        return false;
#elif UNITY_IOS && !UNITY_EDITOR
        return DemoAlbum_HasAccess() != 0;
#else
        return false;
#endif
    }

    public static void RequestAccess()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Permission.RequestUserPermissions(PhotoPermissions());
#elif UNITY_IOS && !UNITY_EDITOR
        DemoAlbum_RequestAccess();
#endif
    }

    public static Task<string[]> List()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Android((album, context) => album.CallStatic<string[]>("list", context, Limit));
#elif UNITY_IOS && !UNITY_EDITOR
        return Task.Run(() =>
        {
            var buffer = new byte[4096];
            int length = DemoAlbum_List(buffer, buffer.Length, Limit);
            return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length).Split('\n') : Array.Empty<string>();
        });
#else
        return Task.FromResult(Array.Empty<string>());
#endif
    }

    public static Task<byte[]> Read(string id, int side)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Android((album, context) =>
        {
            var native = album.CallStatic<sbyte[]>("jpeg", context, id, side, DemoGroup.MaxPhotoBytes);
            if (native == null) return null;
            var bytes = new byte[native.Length];
            Buffer.BlockCopy(native, 0, bytes, 0, native.Length);
            return bytes;
        });
#elif UNITY_IOS && !UNITY_EDITOR
        return Task.Run(() =>
        {
            var buffer = new byte[DemoGroup.MaxPhotoBytes];
            int length = DemoAlbum_Jpeg(id, buffer, buffer.Length, side);
            if (length <= 0 || length > buffer.Length) return null;
            Array.Resize(ref buffer, length);
            return buffer;
        });
#else
        return Task.FromResult<byte[]>(null);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string[] PhotoPermissions()
    {
        using var version = new AndroidJavaClass("android.os.Build$VERSION");
        int sdk = version.GetStatic<int>("SDK_INT");
        const string images = "android.permission.READ_MEDIA_IMAGES";
        if (sdk >= 34) return new[] { images, "android.permission.READ_MEDIA_VISUAL_USER_SELECTED" };
        return new[] { sdk == 33 ? images : "android.permission.READ_EXTERNAL_STORAGE" };
    }

    private static Task<T> Android<T>(Func<AndroidJavaClass, AndroidJavaObject, T> action)
    {
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        var album = new AndroidJavaClass("com.example.nearbydemo.DemoAlbum");
        var context = activity.Call<AndroidJavaObject>("getApplicationContext");
        return Task.Run(() =>
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (album)
                using (context)
                    return action(album, context);
            }
            finally
            {
                AndroidJNI.DetachCurrentThread();
            }
        });
    }
#elif UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int DemoAlbum_HasAccess();
    [DllImport("__Internal")] private static extern void DemoAlbum_RequestAccess();
    [DllImport("__Internal")] private static extern int DemoAlbum_List([Out] byte[] destination, int capacity, int limit);
    [DllImport("__Internal")] private static extern int DemoAlbum_Jpeg(string id, [Out] byte[] destination, int capacity, int side);
#endif
}
