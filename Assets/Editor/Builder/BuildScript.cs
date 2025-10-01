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
	private const string BUILD_TARGET = "buildTarget"; // New argument for BuildTarget
	private const string BUILD_VERSION = "buildVersion"; // Build Version on Device
	private const string BUILD_SUFFIX = "buildSuffix"; // Differenciator
	private const string BUILD_COMMIT_HASH = "commitHash"; // Commit where the Build was created
	private const string BUILD_ID = "buildId"; // Used to create the folder when the Build will be created (Used with Jenkins Job Number)

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
			// Unity built-in flags that should be excluded from custom arguments
			HashSet<string> unityBuiltInFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"batchmode", "quit", "nographics", "projectPath", "executeMethod", "logFile", "silent-crashes"
			};

			string commands = Environment.CommandLine;
			return commands.Split(' ')
				.Where(arg => arg.StartsWith($"{BuildScript.COMMAND_DELIMITER}"))
				.Select(arg => arg.TrimStart(BuildScript.COMMAND_DELIMITER))
				.Where(arg => arg.Contains('=')) // Only include arguments with key=value format
				.Select(arg => arg.Split(new[] { '=' }, 2)) // Split only on first '=' to handle values with '='
				.Where(parts => !unityBuiltInFlags.Contains(parts[0])) // Filter out Unity built-in flags
				.GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase) // Group by key to handle duplicates
				.ToDictionary(
					group => group.Key,
					group => group.Last()[1], // Take last value if duplicates exist
					StringComparer.OrdinalIgnoreCase);
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

		if(!Enum.TryParse(args.GetArgument(BUILD_TARGET), out BuildTarget buildTarget))
		{
			Debug.LogError("Error trying to parse Build Target");
			return;
		}

		BuildParameters buildParameters = new BuildParameters()
		{
			buildTarget = buildTarget,
			buildVersion = args.GetArgument(BuildScript.BUILD_VERSION),
			buildIdentifier = args.GetArgument(BuildScript.BUILD_COMMIT_HASH),
			buildSuffix = args.GetArgument(BuildScript.BUILD_SUFFIX),
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

			// Return the path where addressables are built
			string buildPath = settings.RemoteCatalogBuildPath.GetValue(settings);
			Debug.Log($"Builder :: Addressables generated at: {buildPath}");
			return buildPath;
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
		Directory.CreateDirectory(destinationDir);

		foreach (string file in Directory.GetFiles(sourceDir))
		{
			string fileName = Path.GetFileName(file);
			string destFile = Path.Combine(destinationDir, fileName);
			File.Copy(file, destFile, true);
		}

		foreach (string subDir in Directory.GetDirectories(sourceDir))
		{
			string dirName = Path.GetFileName(subDir);
			string destSubDir = Path.Combine(destinationDir, dirName);
			CopyDirectory(subDir, destSubDir);
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
