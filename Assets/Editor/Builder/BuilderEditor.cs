using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class BuilderEditor : EditorWindow
{
    [MenuItem("Noar Utils/Builder")]
    public static void OpenBuilder()
    {
        BuilderEditor window = GetWindow<BuilderEditor>("Builder");
        window.Show();
    }

    private BuildTarget buildTarget = BuildTarget.Android;
    private string buildVersion;
    private string buildSuffix = null;
    private bool generateAddressableAssets = true;
    private bool developmentBuild = false;
    private string buildOutputPath = null;
    private bool saveBuildReport = true;
    private bool debugMode = true;
    private IBuildPlatformSettings platformSettings = null;

    private Vector2 scroll;
    
    private void OnEnable()
    {
        buildTarget = BuildTarget.Android;
        platformSettings = new AndroidParameters();
        buildOutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Builds");
        buildVersion = PlayerSettings.bundleVersion;
    }

    private void DefinePlatformSettings()
    {
        EditorGUI.BeginChangeCheck();
        buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", buildTarget);
        if (EditorGUI.EndChangeCheck())
        {
            switch (buildTarget)
            {
                case BuildTarget.Android:
                    platformSettings = new AndroidParameters();
                    break;
                case BuildTarget.iOS:
                    platformSettings = new iOSParameters();
                    break;
                default:
                    platformSettings = null;
                    break;
            }
        }
    }

    private void DisplayPresetsMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Debug"), false, SetDebugPreset);
        menu.AddItem(new GUIContent("Release"), false, SetReleasePreset);
        menu.AddItem(new GUIContent("Development"), false, SetDevelopmentPreset);
        
        menu.ShowAsContext();
    }
    
    private void SetDevelopmentPreset()
    {
        buildVersion = Regex.Replace(buildVersion, "[^0-9.]", "");
        buildVersion = buildVersion.Replace("99.", "").Replace("00.", "");
        buildVersion = "00." + buildVersion;

        debugMode = true;
        developmentBuild = true;
        generateAddressableAssets = true;
        saveBuildReport = false;
        if (platformSettings is AndroidParameters androidParameters)
        {
            androidParameters.targetArchitectures = AndroidArchitecture.ARM64;
            androidParameters.generateAab = false;
        }
    }

    private void SetDebugPreset()
    {
        buildVersion = Regex.Replace(buildVersion, "[^0-9.]", "");
        buildVersion = "99." + buildVersion;

        debugMode = true;
        generateAddressableAssets = true;
        saveBuildReport = false;
        if (platformSettings is AndroidParameters androidParameters)
        {
            androidParameters.targetArchitectures = AndroidArchitecture.ARM64;
            androidParameters.generateAab = false;
        }
    }

    private void SetReleasePreset()
    {
        debugMode = false;
        buildVersion = buildVersion.Replace("99.", "").Replace("00.", "");
        generateAddressableAssets = true;
        saveBuildReport = true;
        if (platformSettings is AndroidParameters androidParameters)
        {
            androidParameters.targetArchitectures = AndroidArchitecture.ARM64;
            androidParameters.generateAab = true;
        }
    }
    

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);

        if (GUILayout.Button("Presets", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            DisplayPresetsMenu();
        }
        EditorGUILayout.EndHorizontal();
        DefinePlatformSettings();
        buildVersion = EditorGUILayout.TextField("Build Version", buildVersion);
        buildSuffix = EditorGUILayout.TextField("Build Suffix", buildSuffix);
        generateAddressableAssets = EditorGUILayout.Toggle("Generate Addressable Assets", generateAddressableAssets);
        developmentBuild = EditorGUILayout.Toggle("Development Build", developmentBuild);
        saveBuildReport = EditorGUILayout.Toggle("Save Build Report", saveBuildReport);
        debugMode = EditorGUILayout.Toggle("Debug Mode", debugMode);

        EditorGUILayout.BeginHorizontal();
        buildOutputPath = EditorGUILayout.TextField("Build Output Path", buildOutputPath);
        if (GUILayout.Button("Select", GUILayout.Width(100)))
        {
            buildOutputPath = EditorUtility.OpenFolderPanel("Select Directory for the Build", "", "");
        }
        EditorGUILayout.EndHorizontal();

        if (platformSettings != null)
        {
            platformSettings.OnGUI();
        }

        if (GUILayout.Button("Generate Build"))
        {
            GenerateBuild();
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void GenerateBuild()
    {
        BuildParameters parameters = new BuildParameters()
        {
            buildTarget = buildTarget,
            buildVersion = buildVersion,
            buildOutputPath = buildOutputPath,
            buildSuffix = buildSuffix,
            buildIdentifier = "local" + ToEpoch(DateTime.Now),
            isDevelopmentBuild = developmentBuild,
            generateAddressables = generateAddressableAssets,
            platformSpecificSettings = platformSettings,
            saveBuildReport = false, // Build Report can't be serialized.
            debugMode = debugMode
        };

        BuildScript.GenerateBuild(parameters);
    }
    
    
    public static double ToEpoch(DateTime date)
    {
        DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        TimeSpan diff = date.ToUniversalTime() - origin;
        double toReturn = Math.Floor(diff.TotalSeconds);
        return toReturn <= 0 ? 0 : toReturn;
    }
}

public interface IBuildPlatformSettings
{
    void OnGUI();
    void ApplyTo(BuildPlayerOptions buildOptions);
    void ApplyPlatformModifiers();
    string GetExtension();
}

[Serializable]
public class AndroidParameters : IBuildPlatformSettings
{
    public AndroidArchitecture targetArchitectures = AndroidArchitecture.ARM64;
    public bool generateAab = false;

    public void OnGUI()
    {
        EditorGUILayout.BeginVertical("HelpBox");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Android Settings", EditorStyles.boldLabel);
        if (GUILayout.Button("Player Settings", EditorStyles.miniButton, GUILayout.Width(120)))
        {
            SettingsService.OpenProjectSettings("Project/Player");
        }
        EditorGUILayout.EndHorizontal();

        targetArchitectures = (AndroidArchitecture)EditorGUILayout.EnumPopup("Target Architectures", targetArchitectures);
        generateAab = EditorGUILayout.Toggle("Generate AAB", generateAab);
        
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField($"Min API: {PlayerSettings.Android.minSdkVersion}");
        EditorGUILayout.LabelField($"Target API: {PlayerSettings.Android.targetSdkVersion}");
        EditorGUILayout.LabelField($"Version Code: {VersionCode}");
        
        EditorGUILayout.EndVertical();
    }
    
    public void ApplyTo(BuildPlayerOptions options) {}

    private int VersionCode
    {
        get
        {
            string version = Regex.Replace(Application.version, "[^0-9.]", "");
            version = version.Replace(".", "");
            version = version.PadRight(4, '0');
            return int.Parse(version);
        }
    }

    public void ApplyPlatformModifiers()
    {
        PlayerSettings.Android.targetArchitectures = targetArchitectures;
        EditorUserBuildSettings.buildAppBundle = generateAab;
        PlayerSettings.Android.bundleVersionCode = VersionCode;
        PlayerSettings.Android.useCustomKeystore = generateAab;

        if (generateAab)
        {
            PlayerSettings.Android.keystoreName = "Store/masterhyperbeard.keystore";
            PlayerSettings.Android.keystorePass = "RAmX424HJQR9gumh";
            PlayerSettings.Android.keyaliasName = "hyperdev";
            PlayerSettings.Android.keyaliasPass = "XndF9EJfn5BFFaCD";
        }
    }

    public string GetExtension() => generateAab ? ".aab" : ".apk";
}

[Serializable]
public class iOSParameters : IBuildPlatformSettings
{
    public void OnGUI()
    {
    }

    public void ApplyTo(BuildPlayerOptions buildOptions)
    {
    }

    public void ApplyPlatformModifiers()
    {
    }

    public string GetExtension() => "ipa";
}

    
[Serializable]
public struct BuildParameters
{
    public BuildTarget buildTarget;
    public string buildVersion;
    public string buildSuffix;
    public bool generateAddressables;
    public bool isDevelopmentBuild;
    public string buildOutputPath;
    public string buildIdentifier;
    public IBuildPlatformSettings platformSpecificSettings;
    public bool saveBuildReport;
    public bool debugMode;

    public override string ToString()
    {
        return $@"Build Parameters ::::::::: \n
            - Game: {PlayerSettings.productName} \n
            - Version: {buildVersion} \n
            - Target: {buildTarget} |n
            - Suffix: {buildSuffix} \n
            - Output Path: {buildOutputPath} \n
            - Development: {isDevelopmentBuild} \n
            - Addressables: {generateAddressables} \n";
    }
    

    public BuildPlayerOptions GetBuildOptions()
    {
        BuildPlayerOptions buildOptions = new BuildPlayerOptions();
        buildOptions.scenes = BuildScript.GetAvailableScenes();
        buildOptions.target = buildTarget;
        buildOptions.options = isDevelopmentBuild ? BuildOptions.Development : BuildOptions.None;
        buildOptions.locationPathName = Path.Combine(GetBuildDirectory(), GetBuildName(true));

        if (platformSpecificSettings != null)
        {
            platformSpecificSettings.ApplyTo(buildOptions);
        }

        return buildOptions;
    }

    public string GetBuildDirectory()
    {
        string intermediateFolder = isDevelopmentBuild ? BuildScript.FolderDevelopment : debugMode ? BuildScript.FolderQA : BuildScript.FolderRelease;
        string buildDirectory = Path.Combine(buildOutputPath, intermediateFolder, GetBuildName(false));

        // Create directory if it doesn't exist (creates all nested folders)
        if (!Directory.Exists(buildDirectory))
        {
            Directory.CreateDirectory(buildDirectory);
            Debug.Log($"Builder :: Created build directory: {buildDirectory}");
        }

        return buildDirectory;
    }

    public string GetBuildName(bool includeExtension)
    {
        string productName = PlayerSettings.productName.Replace(" ", string.Empty);
        string version = "v" + PlayerSettings.bundleVersion;
        string suffix = !string.IsNullOrEmpty(buildSuffix) ? "_" + buildSuffix : string.Empty;
        string development = isDevelopmentBuild ? "_DEVELOPMENT" : string.Empty;
        string commit = !string.IsNullOrEmpty(buildIdentifier) ? "_" + buildIdentifier : string.Empty;
        string extension = platformSpecificSettings?.GetExtension() ?? string.Empty;

        string buildName = $"{productName}_{version}{suffix}{development}{commit}";
        if (includeExtension && !string.IsNullOrEmpty(extension))
        {
            buildName += extension;
        }

        return buildName;
    }

    public void ApplyPlatformModifiers()
    {
        if (platformSpecificSettings != null)
        {
            platformSpecificSettings.ApplyPlatformModifiers();
        }
    }
}