using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

class BuildScript {

	private const char COMMAND_DELIMITER = '-';

	// Custom Command Line Arguments (All these commands MUST have the COMMAND_DELIMITER as first char on the console to be read.
	// Note: buildTarget is handled by Unity's built-in -buildTarget flag and accessed via EditorUserBuildSettings.activeBuildTarget
	private const string BUILD_VERSION = "buildVersion"; // Build Version on Device
	private const string BUILD_SUFFIX = "buildSuffix"; // Differenciator
	private const string BUILD_COMMIT_HASH = "commitHash"; // Commit where the Build was created
	private const string BUILD_ID = "buildId"; // Used to create the folder when the Build will be created (Used with Jenkins Job Number)
	private const string BUILD_OUTPUT_PATH = "buildOutputPath"; // Base directory where builds will be output

	private const string GENERATE_ADDRESSABLES = "generateAddressables"; //If TRUE, compile Addressables (if empty or false, doesn't compile addressables)
	private const string DEVELOPMENT_BUILD = "developmentBuild"; // If TRUE, build will be a Development version (if empty or false, will be normal build)

	private const string DEBUG_MODE_SYMBOL = "DEBUG_MODE";

	// Build folder names (public for access by BuilderEditor)
	public const string FolderDevelopment = "Development";
	public const string FolderQA = "QA";
	public const string FolderRelease = "Release";
	private const string FOLDER_ADDRESSABLES = "ServerData";
	
	public class CommandLineArguments
	{
		private readonly Dictionary<string, string> _arguments;

		public CommandLineArguments()
		{
			_arguments = ParseCommandLineArgs();
		}

		private Dictionary<string, string> ParseCommandLineArgs()
		{
			// Unity built-in flags that should be excluded from custom arguments (these use space-separated values)
			HashSet<string> unityBuiltInFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"batchmode", "quit", "nographics", "projectPath", "executeMethod", "logFile", "silent-crashes"
			};

			string commands = Environment.CommandLine;

			// Parse both formats: -key=value (custom) and -key value (Unity built-in but not filtered)
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string[] args = commands.Split(' ');

			for (int i = 0; i < args.Length; i++)
			{
				if (!args[i].StartsWith($"{BuildScript.COMMAND_DELIMITER}"))
					continue;

				string arg = args[i].TrimStart(BuildScript.COMMAND_DELIMITER);

				// Handle -key=value format
				if (arg.Contains('='))
				{
					string[] parts = arg.Split(new[] { '=' }, 2);
					if (!unityBuiltInFlags.Contains(parts[0]))
					{
						result[parts[0]] = parts[1];
					}
				}
				// Handle -key value format (space-separated)
				else if (i + 1 < args.Length && !args[i + 1].StartsWith($"{BuildScript.COMMAND_DELIMITER}"))
				{
					if (!unityBuiltInFlags.Contains(arg))
					{
						result[arg] = args[i + 1];
						i++; // Skip the next argument since we consumed it as the value
					}
				}
			}

			return result;
		}

		public string GetArgument(string key)
		{
			return _arguments.TryGetValue(key, out var value) ? value : string.Empty;
		}

		public bool GetArgumentAsBool(string key)
		{
			return bool.TryParse(GetArgument(key), out var value) && value;
		}

		public override string ToString()
		{
			return string.Join(", ", _arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"));
		}
	}
	

	/// <summary>
	/// Entry point for Unity batch mode builds. Parses command line arguments and initiates the build process.
	/// Called via: unity-editor -executeMethod BuildScript.BuildBatchMode -buildTarget Android -buildVersion 1.0.0 ...
	/// </summary>
	public static void BuildBatchMode()
	{
		CommandLineArguments args = new CommandLineArguments();

		// Debug: Log full command line for troubleshooting
		Debug.Log($"Builder :: Full Command Line: {Environment.CommandLine}");
		Debug.Log($"Builder :: Parsed Arguments: {args.ToString()}");

		// Parse build target from command line arguments
		string buildTargetString = args.GetArgument("buildTarget");
		if (string.IsNullOrEmpty(buildTargetString))
		{
			Debug.LogError("Builder :: buildTarget argument is missing or empty!");
			return;
		}

		if (!Enum.TryParse(buildTargetString, true, out BuildTarget buildTarget))
		{
			Debug.LogError($"Builder :: Error parsing Build Target. Value provided: '{buildTargetString}'");
			return;
		}

		Debug.Log($"Builder :: Build Target Parsed: {buildTarget}");

		// Switch Unity's active build target
		UnityEditor.Build.NamedBuildTarget namedTarget = GetBuildTargetGroup(buildTarget);
		bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(namedTarget, buildTarget);
		if (!switched)
		{
			Debug.LogError($"Builder :: Failed to switch to build target: {buildTarget}");
			return;
		}
		Debug.Log($"Builder :: Successfully switched to build target: {buildTarget}");

		// Initialize platform-specific settings based on target
		IBuildPlatformSettings platformSettings = null;
		switch (buildTarget)
		{
			case BuildTarget.Android:
				platformSettings = new AndroidParameters();
				Debug.Log("Builder :: Using AndroidParameters for build");
				break;
			case BuildTarget.iOS:
				platformSettings = new iOSParameters();
				Debug.Log("Builder :: Using iOSParameters for build");
				break;
			default:
				Debug.LogWarning($"Builder :: No platform-specific settings for {buildTarget}");
				break;
		}

		// Get build output path from arguments, or use default fallback
		string buildOutputPath = args.GetArgument(BuildScript.BUILD_OUTPUT_PATH);
		if (string.IsNullOrEmpty(buildOutputPath))
		{
			buildOutputPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");
			Debug.Log($"Builder :: No buildOutputPath provided, using default: {buildOutputPath}");
		}

		BuildParameters buildParameters = new BuildParameters()
		{
			buildTarget = buildTarget,
			buildVersion = args.GetArgument(BuildScript.BUILD_VERSION),
			buildIdentifier = args.GetArgument(BuildScript.BUILD_COMMIT_HASH),
			buildSuffix = args.GetArgument(BuildScript.BUILD_SUFFIX),
			buildOutputPath = buildOutputPath,
			platformSpecificSettings = platformSettings,
			isDevelopmentBuild = args.GetArgumentAsBool(BuildScript.DEVELOPMENT_BUILD),
			generateAddressables = args.GetArgumentAsBool(BuildScript.GENERATE_ADDRESSABLES)
		};

		BuildScript.GenerateBuild(buildParameters);
	}

	private static UnityEditor.Build.NamedBuildTarget GetBuildTargetGroup(BuildTarget target)
	{
		switch (target)
		{
			case BuildTarget.Android:
				return UnityEditor.Build.NamedBuildTarget.Android;
			case BuildTarget.iOS:
				return UnityEditor.Build.NamedBuildTarget.iOS;
			default:
				return UnityEditor.Build.NamedBuildTarget.Android;
		}
	}

	/// <summary>
	/// Generates a Unity build with the specified parameters. Handles addressables generation,
	/// platform-specific settings, scripting defines, and build report generation.
	/// </summary>
	/// <param name="parameters">Build configuration parameters including target platform, version, and options.</param>
	public static void GenerateBuild(BuildParameters parameters)
	{
		PlayerSettings.bundleVersion = parameters.buildVersion;
		BuildPlayerOptions buildOptions = parameters.GetBuildOptions();

		PlayerSettings.GetScriptingDefineSymbols(GetBuildTargetGroup(parameters.buildTarget), out string[] defines);

		HashSet<string> buildSymbols = new HashSet<string>(defines);
		if (parameters.debugMode)
		{
			buildSymbols.Add(DEBUG_MODE_SYMBOL);
		}
		else
		{
			buildSymbols.Remove(DEBUG_MODE_SYMBOL);
		}
		
		Debug.Log($"Defined Symbols: {string.Join(",", buildSymbols)}");
			
		PlayerSettings.SetScriptingDefineSymbols(GetBuildTargetGroup(parameters.buildTarget), buildSymbols.ToArray());
		
		Debug.Log(parameters);

		if (parameters.platformSpecificSettings != null)
		{
			Debug.Log($"Builder :: Applying Settings of type {parameters.platformSpecificSettings.GetType()}");
		}

		parameters.ApplyPlatformModifiers();
		
		string addressablesPath = string.Empty;
		if (parameters.generateAddressables)
		{
			addressablesPath = GenerateAddressableAssets();
		}

		Debug.Log("Builder :: Building Player.");
		BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

		// Copy addressables to build output if they were generated
		if (!string.IsNullOrEmpty(addressablesPath) && report.summary.result == BuildResult.Succeeded)
		{
			CopyAddressablesToBuildOutput(addressablesPath, parameters.GetBuildDirectory());
		}

		if (parameters.saveBuildReport)
		{
			SaveBuildReport(report, parameters);
		}
		
		Debug.Log("Builder :: Build Status: " + report.summary.result);
		SaveParameters(parameters);
		
		if (!Application.isBatchMode && report.summary.result == BuildResult.Succeeded)
		{
			bool result = EditorUtility.DisplayDialog("Build Succeeded", "The project was built successfully.", "Open Folder", "Okay");
			if (result)
			{
				EditorUtility.RevealInFinder(buildOptions.locationPathName);
			}
		}
    }

	private static void SaveBuildReport(BuildReport report, BuildParameters parameters)
	{
		string data = JsonUtility.ToJson(report);
		string fileName = Path.Combine(parameters.GetBuildDirectory(), "BuildReport.json");

		using (StreamWriter streamWriter = new StreamWriter(fileName))
		{
			streamWriter.Write(data);
		}
	}

	private static void SaveParameters(BuildParameters parameters)
	{
		string data = JsonConvert.SerializeObject(parameters, new StringEnumConverter());
		string fileName = Path.Combine(parameters.GetBuildDirectory(), "BuildParameters.json");

		using (StreamWriter streamWriter = new StreamWriter(fileName))
		{
			streamWriter.Write(data);
		}
	}

	/// <summary>
	/// Generates addressable assets and returns the build path where they were generated.
	/// </summary>
	/// <returns>The path where addressables were built, or empty string if build failed.</returns>
	public static string GenerateAddressableAssets()
	{
		Debug.Log("Builder :: Generating Addressable Assets.");
		try
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogError("Builder :: AddressableAssetSettings not found.");
				return string.Empty;
			}

			IDataBuilder dataBuilder = settings.ActivePlayerDataBuilder;
			AddressableAssetSettings.CleanPlayerContent(dataBuilder);
			AddressableAssetSettings.BuildPlayerContent();

			// Try multiple path sources to get the addressables build path
			string buildPath = null;

			// Try 1: Remote catalog build path
			if (settings.RemoteCatalogBuildPath != null)
			{
				buildPath = settings.RemoteCatalogBuildPath.GetValue(settings);
			}

			// Try 2: Use BuildPath from profile settings (fallback)
			if (string.IsNullOrEmpty(buildPath) && settings.profileSettings != null)
			{
				var variableNames = settings.profileSettings.GetVariableNames();
				if (variableNames != null && variableNames.Contains("BuildPath"))
				{
					buildPath = settings.profileSettings.GetValueById(settings.activeProfileId, "BuildPath");
					Debug.Log("Builder :: Using BuildPath from profile settings as fallback.");
				}
			}

			// Try 3: Default ServerData folder in project root (last resort)
			if (string.IsNullOrEmpty(buildPath))
			{
				buildPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ServerData");
				Debug.Log("Builder :: Using default ServerData path as fallback.");
			}

			// Validate the path exists
			if (!string.IsNullOrEmpty(buildPath) && Directory.Exists(buildPath))
			{
				Debug.Log($"Builder :: Addressables generated at: {buildPath}");
				return buildPath;
			}
			else
			{
				Debug.LogWarning($"Builder :: Addressables path not found or invalid: {buildPath}");
				return string.Empty;
			}
		}
		catch (Exception ex)
		{
			Debug.LogError($"Builder :: Error generating Addressables: {ex.Message}");
			return string.Empty;
		}
	}

	/// <summary>
	/// Copies generated addressables to the build output directory.
	/// </summary>
	/// <param name="addressablesPath">Path where addressables were generated.</param>
	/// <param name="buildDirectory">Target build directory.</param>
	private static void CopyAddressablesToBuildOutput(string addressablesPath, string buildDirectory)
	{
		if (string.IsNullOrEmpty(addressablesPath) || !Directory.Exists(addressablesPath))
		{
			Debug.LogWarning($"Builder :: Addressables path not found or invalid: {addressablesPath}");
			return;
		}

		try
		{
			string targetPath = Path.Combine(buildDirectory, FOLDER_ADDRESSABLES);

			if (Directory.Exists(targetPath))
			{
				Directory.Delete(targetPath, true);
			}

			CopyDirectory(addressablesPath, targetPath);
			Debug.Log($"Builder :: Addressables copied to: {targetPath}");
		}
		catch (Exception ex)
		{
			Debug.LogError($"Builder :: Error copying Addressables: {ex.Message}");
		}
	}

	/// <summary>
	/// Recursively copies a directory and all its contents.
	/// </summary>
	private static void CopyDirectory(string sourceDir, string destinationDir)
	{
		if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(destinationDir))
		{
			Debug.LogError($"Builder :: Invalid directory paths. Source: '{sourceDir}', Destination: '{destinationDir}'");
			return;
		}

		if (!Directory.Exists(sourceDir))
		{
			Debug.LogWarning($"Builder :: Source directory does not exist: {sourceDir}");
			return;
		}

		Directory.CreateDirectory(destinationDir);

		// Copy files
		foreach (string file in Directory.GetFiles(sourceDir))
		{
			try
			{
				string fileName = Path.GetFileName(file);
				if (string.IsNullOrEmpty(fileName))
					continue;

				string destFile = Path.Combine(destinationDir, fileName);
				File.Copy(file, destFile, true);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"Builder :: Failed to copy file '{file}': {ex.Message}");
			}
		}

		// Copy subdirectories
		foreach (string subDir in Directory.GetDirectories(sourceDir))
		{
			try
			{
				DirectoryInfo dirInfo = new DirectoryInfo(subDir);
				string dirName = dirInfo.Name;

				if (string.IsNullOrEmpty(dirName))
					continue;

				string destSubDir = Path.Combine(destinationDir, dirName);
				CopyDirectory(subDir, destSubDir);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"Builder :: Failed to copy directory '{subDir}': {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Gets all scenes enabled in the build settings.
	/// </summary>
	/// <returns>Array of scene paths configured in EditorBuildSettings.</returns>
	public static string[] GetAvailableScenes()
	{
		int scenesAmount = EditorBuildSettings.scenes.Length;
		string[] scenes = new string[scenesAmount];
		for (int i = 0; i < scenesAmount; i++)
		{
			scenes[i] = EditorBuildSettings.scenes[i].path;
		}

		return scenes;
	}
	
	
}
