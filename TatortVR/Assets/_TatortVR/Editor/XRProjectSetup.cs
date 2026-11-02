using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.XR.CoreUtils.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Configures the project for Meta Quest 3 (OpenXR, Android) and builds the base test scene.
    /// Menu: Tools/TatortVR/Projekt einrichten, or batchmode -executeMethod TatortVR.EditorTools.XRProjectSetup.Run
    /// </summary>
    public static class XRProjectSetup
    {
        const string k_Root = "Assets/_TatortVR";
        const string k_ScenePath = k_Root + "/Scenes/Tatort_Basis.unity";
        const string k_RigPrefabName = "XR Origin (XR Rig)";

        [MenuItem("Tools/TatortVR/Projekt einrichten")]
        public static void Run()
        {
            ConfigurePlayer();
            ConfigureXR();
            ConfigureUrpForQuest();
            BuildScene();
            AssetDatabase.SaveAssets();
            ValidateProject();
            Debug.Log("[TatortSetup] Project setup finished");
        }

        // Runs the rules from Project Settings > XR Plug-in Management > Project Validation,
        // applies the automatic fixes and logs whatever is left.
        [MenuItem("Tools/TatortVR/XR-Projektvalidierung")]
        public static void ValidateProject() => RunValidation(applyFixes: true);

        // Only logs the open issues; fixes can open progress bars or dialogs, which remote tools cannot handle.
        public static void ReportValidationIssues() => RunValidation(applyFixes: false);

        static void RunValidation(bool applyFixes)
        {
            if (Application.isBatchMode && File.Exists(k_ScenePath))
                EditorSceneManager.OpenScene(k_ScenePath);

            var platformRules = (Dictionary<BuildTargetGroup, List<BuildValidationRule>>)typeof(BuildValidator)
                .GetProperty("PlatformRules", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);
            if (platformRules == null)
            {
                Debug.LogWarning("[TatortValidation] Could not read XR project validation rules");
                return;
            }

            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!platformRules.TryGetValue(group, out var rules))
                    continue;

                if (applyFixes)
                    BuildValidator.FixIssues(rules.Where(r => IsFailing(r) && r.FixItAutomatic && r.FixIt != null).ToList());
                var remaining = rules.Where(IsFailing).ToList();
                foreach (var rule in remaining)
                    Debug.Log($"[TatortValidation] {group} {(rule.Error ? "ERROR" : "Warning")}{(rule.FixItAutomatic ? " (auto-fix)" : "")}: [{rule.Category}] {rule.Message}");
                Debug.Log($"[TatortValidation] {group}: {remaining.Count} open issue(s)");
            }

            if (!applyFixes)
                return;
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        static bool IsFailing(BuildValidationRule rule) =>
            rule.IsRuleEnabled() && rule.CheckPredicate != null && !rule.CheckPredicate();

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "TatortVR";
            PlayerSettings.productName = "TatortVR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "de.uni.tatortvr");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Less generated C++: faster builds that fit into 8 GB of RAM.
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // XRI reads controller input through the Input System package; the legacy Input Manager is not needed.
            var playerSettings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var inputHandler = playerSettings.FindProperty("activeInputHandler");
            if (inputHandler != null && inputHandler.intValue == 0)
            {
                inputHandler.intValue = 1;
                playerSettings.ApplyModifiedProperties();
            }
        }

        static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }

            // Android = standalone build on the Quest 3; Standalone = Play mode in the editor via Quest Link / Steam Link.
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!perTarget.HasSettingsForBuildTarget(group))
                    perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group))
                    perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

                var general = perTarget.SettingsForBuildTarget(group);
                general.InitManagerOnStart = true;
                if (!XRPackageMetadataStore.AssignLoader(general.Manager,typeof(OpenXRLoader).FullName, group))
                    Debug.LogError($"[TatortSetup] Could not assign OpenXR loader for {group}");
                EditorUtility.SetDirty(general);
            }

            EnableOpenXRFeatures(BuildTargetGroup.Android, "MetaQuestFeature", "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile");
            // Touch Plus is the Quest 3 controller; runtimes without that profile fall back to Oculus Touch.
            EnableOpenXRFeatures(BuildTargetGroup.Standalone, "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile");
        }

        static void EnableOpenXRFeatures(BuildTargetGroup group, params string[] featureTypeNames)
        {
            // OpenXR's package settings asset is internal and normally created when its settings page is first opened.
            typeof(UnityEditor.XR.OpenXR.Features.FeatureHelpers).Assembly
                .GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings")
                ?.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null);

            // OpenXR only creates settings for platforms whose build module is installed.
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogError($"[TatortSetup] No OpenXR settings for {group}; is its build support module installed?");
                return;
            }

            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            foreach (var typeName in featureTypeNames)
            {
                var feature = settings.GetFeatures().FirstOrDefault(f => f.GetType().Name == typeName);
                if (feature == null)
                {
                    Debug.LogWarning($"[TatortSetup] OpenXR feature {typeName} not available for {group}");
                    continue;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }

            EditorUtility.SetDirty(settings);
        }

        // Quest renders on a mobile GPU: MSAA instead of post AA, no HDR buffer.
        static void ConfigureUrpForQuest()
        {
            for (var i = 0; i < QualitySettings.count; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset urp && urp.name.Contains("Mobile"))
                {
                    urp.msaaSampleCount = 4;
                    urp.supportsHDR = false;
                    EditorUtility.SetDirty(urp);
                }
            }
        }

        static void BuildScene()
        {
            if (File.Exists(k_ScenePath))
            {
                Debug.Log($"[TatortSetup] {k_ScenePath} already exists, leaving it untouched");
                return;
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Directory.CreateDirectory(k_Root + "/Scenes");
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (RenderSettings.skybox == null)
                RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");

            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            TatortEnvironmentBuilder.Build(scene);

            var rigPath = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Samples" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == k_RigPrefabName);
            if (rigPath == null)
            {
                Debug.LogError("[TatortSetup] XR Origin prefab not found, import the XRI Starter Assets sample first");
            }
            else
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(rigPath), scene);
                rig.transform.SetPositionAndRotation(TatortEnvironmentBuilder.StartPosition, Quaternion.identity);
                // Standing experience: use the real floor so eye height matches the user's height.
                rig.GetComponent<Unity.XR.CoreUtils.XROrigin>().RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
            }

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
        }
    }
}
