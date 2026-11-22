using System;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NearbyConnections.Editor
{
    /// <summary>iOS build settings for applications using Nearby Connections.</summary>
    public sealed class NearbyIosSettings : ScriptableObject
    {
        [Tooltip("Service IDs passed to NearbyOptions.ServiceId on iOS.")]
        public string[] ServiceIds = Array.Empty<string>();
        [TextArea] public string BluetoothUsage = "Bluetooth is used to find nearby devices.";
        [TextArea] public string LocalNetworkUsage = "The local network is used to connect to nearby devices.";
        [TextArea] public string LocationUsage = "Location access is used to connect to nearby Wi-Fi hotspots.";

        [MenuItem("Assets/Create/Nearby Connections/iOS Build Settings")]
        private static void CreateSettings()
        {
            var settings = CreateInstance<NearbyIosSettings>();
            ProjectWindowUtil.CreateAsset(settings, "NearbyIosSettings.asset");
        }

        // Nearby's Bonjour type is the first six SHA-256 bytes in uppercase hexadecimal.
        internal static string BonjourService(string serviceId)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(serviceId));
                var name = new StringBuilder("_");
                for (int i = 0; i < 6; i++) name.Append(hash[i].ToString("X2"));
                return name.Append("._tcp").ToString();
            }
        }
    }
}
