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

	private const string GENERATE_ADRESSABLES = "generateAddressables"; //If TRUE, compile Addressables (if empty or false, doesn't compile addressables)
	private const string DEVELOPMENT_BUILD = "developmentBuild"; // If TRUE, build will be a Development version (if empty or false, will be normal build)

	private const string DEBUG_MODE_SYMBOL = "DEBUG_MODE";
	
	public class CommandLineArguments
	{
		private readonly Dictionary<string, string> _arguments;

		public CommandLineArguments()
		{
			_arguments = ParseCommandLineArgs();
		}

		private Dictionary<string, string> ParseCommandLineArgs()
		{
			string commands = Environment.CommandLine;
			return commands.Split(' ')
				.Where(arg => arg.StartsWith($"{BuildScript.COMMAND_DELIMITER}"))
				.Select(arg => arg.TrimStart(BuildScript.COMMAND_DELIMITER).Split('='))
				.ToDictionary(
					parts => parts[0],
					parts => parts.Length > 1 ? parts[1] : string.Empty,
					StringComparer.OrdinalIgnoreCase); // Case-insensitive keys
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
	
	public static string ExtractCommandArgument(string argumentName, string commands = null)
	{
		if (string.IsNullOrEmpty(commands))
		{
			commands = Environment.CommandLine;
		}

		string prefix = $"{COMMAND_DELIMITER}{argumentName}=";
		int startIndex = commands.IndexOf(prefix, StringComparison.Ordinal);

		if (startIndex >= 0)
		{
			startIndex += prefix.Length;
			int endIndex = commands.IndexOf(' ', startIndex);

			return endIndex > startIndex ? commands.Substring(startIndex, endIndex - startIndex) : commands.Substring(startIndex);
		}

		return string.Empty;
	}

	public static bool CheckCommandArgument(string command)
    {
		if (bool.TryParse(ExtractCommandArgument(command), out bool toReturn))
		{
			return toReturn;
		}

		return false;
    }

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
			generateAddressables = args.GetArgumentAsBool(BuildScript.GENERATE_ADRESSABLES)
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

		Debug.Log($"Builder :: Applying Settings of type {parameters.platformSpecificSettings.GetType()}");
		parameters.ApplyPlatformModifiers();
		
		if (parameters.generateAddressables)
		{
			Debug.Log("Builder :: Generating Addressable Assets.");
			GenerateAddressableAssets();
		}
		
		Debug.Log("Builder :: Building Player.");
		BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

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
		StreamWriter streamWriter;
		streamWriter = new StreamWriter(fileName);
		streamWriter.Write(data);
		streamWriter.Close();
	}
	private static void SaveParameters(BuildParameters parameters)
	{
		string data = JsonConvert.SerializeObject(parameters, new StringEnumConverter());
		string fileName = Path.Combine(parameters.GetBuildDirectory(), "BuildParameters.json");
		StreamWriter streamWriter = new StreamWriter(fileName);
		streamWriter.Write(data);
		streamWriter.Close();
	}

	public static void GenerateAddressableAssets()
	{
		Debug.Log("Builder :: Generating Addressable Assets.");
		IDataBuilder settings = AddressableAssetSettingsDefaultObject.Settings.ActivePlayerDataBuilder;
		AddressableAssetSettings.CleanPlayerContent(settings);
		AddressableAssetSettings.BuildPlayerContent();
	}

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
