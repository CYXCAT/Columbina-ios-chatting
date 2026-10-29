#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Companion.EditorTools
{
    // Xcode project setup for the companion app: permissions, Sign in with Apple, the GoogleSignIn
    // Swift package + its Info.plist keys, Swift plugins, and native voice frameworks.
    public static class IOSPostBuild
    {
        const string GoogleSignInPackage = "https://github.com/google/GoogleSignIn-iOS.git";
        const string GoogleSignInVersion = "10.1.0";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var config = LoadConfig();

            var projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            var framework = proj.GetUnityFrameworkTargetGuid();
            var main = proj.GetUnityMainTargetGuid();

            foreach (var f in new[] { "OpenGLES.framework", "MetalKit.framework", "GLKit.framework", "VideoToolbox.framework",
                                      "Network.framework", "Speech.framework", "AuthenticationServices.framework" })
                proj.AddFrameworkToProject(framework, f, false);
            proj.AddBuildProperty(framework, "OTHER_LDFLAGS", "-ObjC");
            proj.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
            proj.SetBuildProperty(framework, "DEFINES_MODULE", "YES");
            proj.SetBuildProperty(main, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");

            // CompanionAuth.swift lives in UnityFramework, so the package product is linked there.
            var package = proj.AddRemotePackageReferenceAtVersionUpToNextMajor(GoogleSignInPackage, GoogleSignInVersion);
            proj.AddRemotePackageFrameworkToProject(framework, "GoogleSignIn", package, false);
            proj.WriteToFile(projPath);

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSMicrophoneUsageDescription", "Your voice is used to talk with your companion.");
            plist.root.SetString("NSSpeechRecognitionUsageDescription", "Speech is converted to text on your device so your companion can understand you.");

            if (config != null && !string.IsNullOrEmpty(config.googleIosClientId))
            {
                plist.root.SetString("GIDClientID", config.googleIosClientId);
                if (!string.IsNullOrEmpty(config.googleServerClientId))
                    plist.root.SetString("GIDServerClientID", config.googleServerClientId);
                var urlTypes = plist.root["CFBundleURLTypes"]?.AsArray() ?? plist.root.CreateArray("CFBundleURLTypes");
                var schemes = urlTypes.AddDict().CreateArray("CFBundleURLSchemes");
                schemes.AddString(CompanionConfig.ReversedClientId(config.googleIosClientId));
            }
            else
            {
                Debug.LogWarning("IOSPostBuild: CompanionConfig.googleIosClientId is empty; Google sign-in will not work in this build.");
            }
            plist.WriteToFile(plistPath);

            var entitlements = Path.GetFileName(PlayerSettings.productName) + ".entitlements";
            var caps = new ProjectCapabilityManager(projPath, entitlements, null, main);
            caps.AddSignInWithApple();
            caps.WriteToFile();
        }

        static CompanionConfig LoadConfig()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(CompanionConfig));
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<CompanionConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
