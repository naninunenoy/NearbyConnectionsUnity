#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

internal static class DemoAlbumIosBuild
{
    [PostProcessBuild(101)]
    private static void ConfigureAlbum(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        const string usage = "NSPhotoLibraryUsageDescription";
        if (!plist.root.values.ContainsKey(usage))
            plist.root.SetString(usage, "Choose a photo to share with nearby chat participants.");
        const string limitedAccessAlert = "PHPhotoLibraryPreventAutomaticLimitedAccessAlert";
        if (!plist.root.values.ContainsKey(limitedAccessAlert))
            plist.root.SetBoolean(limitedAccessAlert, true);
        plist.WriteToFile(plistPath);

        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Photos.framework", false);
        project.WriteToFile(projectPath);
    }
}
#endif
