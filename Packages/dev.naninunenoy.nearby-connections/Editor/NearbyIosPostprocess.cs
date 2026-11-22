#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace NearbyConnections.Editor
{
    internal static class NearbyIosPostprocess
    {
        [PostProcessBuild(100)]
        private static void Configure(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string[] guids = AssetDatabase.FindAssets("t:NearbyIosSettings", new[] { "Assets" });
            if (guids.Length == 0) throw new BuildFailedException("Create Nearby iOS Build Settings and register the service IDs used by the app.");
            if (guids.Length != 1)
                throw new BuildFailedException("Use exactly one Nearby iOS Build Settings asset in Assets.");
            var settings = AssetDatabase.LoadAssetAtPath<NearbyIosSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (settings == null || settings.ServiceIds == null || settings.ServiceIds.Length == 0)
                throw new BuildFailedException("Configure at least one Nearby service ID in iOS Build Settings.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in settings.ServiceIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new BuildFailedException("Nearby service IDs cannot be empty.");
                names.Add(NearbyIosSettings.BonjourService(id));
            }
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            SetIfMissing(plist.root, "NSBluetoothAlwaysUsageDescription", settings.BluetoothUsage);
            SetIfMissing(plist.root, "NSLocalNetworkUsageDescription", settings.LocalNetworkUsage);
            SetIfMissing(plist.root, "NSLocationWhenInUseUsageDescription", settings.LocationUsage);
            PlistElementArray services;
            if (plist.root.values.TryGetValue("NSBonjourServices", out var existing))
            {
                services = existing as PlistElementArray;
                if (services == null) throw new BuildFailedException("NSBonjourServices must be an array.");
                foreach (var value in services.values)
                    if (value is PlistElementString entry) names.Remove(entry.value);
            }
            else services = plist.root.CreateArray("NSBonjourServices");
            foreach (string name in names) services.AddString(name);
            plist.WriteToFile(plistPath);
            ConfigureProject(path);
        }

        private static void ConfigureProject(string path)
        {
            const string revision = "15241799d2e26722128f6f9815951eb1d40b6110";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NearbyIosSettings).Assembly);
            if (package == null) throw new BuildFailedException("Could not locate the Nearby Connections package.");
            var source = Path.Combine(package.resolvedPath, "Editor/Native~/NearbyBridge.swift");
            var relative = "Libraries/NearbyConnections/NearbyBridge.swift";
            var destination = Path.Combine(path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string target = project.GetUnityFrameworkTargetGuid();
            string file = project.FindFileGuidByProjectPath(relative);
            if (string.IsNullOrEmpty(file))
            {
                file = project.AddFile(relative, relative, PBXSourceTree.Source);
                project.AddFileToBuild(target, file);
            }
            project.SetBuildProperty(target, "SWIFT_VERSION", "5.0");
            const string marker = "NEARBY_UNITY_PACKAGE_REVISION";
            var previousRevision = project.GetBuildPropertyForAnyConfig(target, marker);
            if (string.IsNullOrEmpty(previousRevision))
            {
                string dependency = project.AddRemotePackageReferenceAtRevision("https://github.com/google/nearby.git", revision);
                project.AddRemotePackageFrameworkToProject(target, "NearbyConnections", dependency, false);
                project.SetBuildProperty(target, marker, revision);
            }
            else if (previousRevision != revision)
                throw new BuildFailedException("The existing Nearby Swift package revision differs. Export to a clean Xcode directory.");
            project.WriteToFile(projectPath);
        }

        private static void SetIfMissing(PlistElementDict root, string key, string description)
        {
            if (root.values.ContainsKey(key)) return;
            if (string.IsNullOrWhiteSpace(description))
                throw new BuildFailedException("Provide a usage description for " + key + ".");
            root.SetString(key, description);
        }
    }
}
#endif
