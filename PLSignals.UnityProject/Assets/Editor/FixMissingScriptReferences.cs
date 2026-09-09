using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to repair missing script references in prefabs and scenes
/// after .meta files have been deleted and regenerated with new GUIDs.
///
/// Works by building a mapping of script fileIDs to their current DLL/asset GUIDs,
/// then scanning all prefabs and scenes for m_Script references that use a
/// different GUID for the same fileID, and replacing them.
///
/// Usage: Tools > DV Signals > Fix Missing Script References
/// </summary>
public class FixMissingScriptReferences
{
	private static readonly Regex ScriptReferencePattern =
		new Regex(@"m_Script: \{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: (\d+)\}", RegexOptions.Compiled);

	[MenuItem("Tools/DV Signals/Fix Missing Script References")]
	public static void Execute()
	{
		// ------------------------------------------------------------------
		// Step 1: Build a mapping of fileID -> guid from all known scripts
		// ------------------------------------------------------------------
		var fileIdToGuid = new Dictionary<long, string>();
		int scriptCount = 0;

		// Load MonoScript objects from the AssetDatabase. This finds scripts
		// in .cs files and also scripts inside DLLs when loaded as sub-assets.
		string[] allAssetGuids = AssetDatabase.FindAssets("t:Script");
		HashSet<string> loadedAssetPaths = new HashSet<string>();

		foreach (string assetGuid in allAssetGuids)
		{
			string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
			MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
			loadedAssetPaths.Add(assetPath);

			if (monoScript == null)
				continue;

			if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
					monoScript, out string guid, out long localFileId))
			{
				if (!fileIdToGuid.ContainsKey(localFileId))
				{
					fileIdToGuid[localFileId] = guid;
					scriptCount++;
				}
			}
		}

		// Load scripts from DLLs: FindAssets("t:Script") may return only
		// the first MonoScript per DLL (or none at all), so we always
		// enumerate all MonoScript sub-assets from every DLL explicitly.
		string assetsRoot = Application.dataPath; // .../Assets
		string[] dllFiles = Directory.GetFiles(assetsRoot, "*.dll", SearchOption.AllDirectories);

		foreach (string dllFilePath in dllFiles)
		{
			// Convert absolute path to relative asset path (e.g. "Assets/Plugins/...")
			string assetPath = "Assets" + dllFilePath.Substring(assetsRoot.Length).Replace('\\', '/');

			Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
			if (subAssets == null || subAssets.Length == 0)
				continue;

			foreach (Object subAsset in subAssets)
			{
				MonoScript monoScript = subAsset as MonoScript;
				if (monoScript == null)
					continue;

				if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
						monoScript, out string guid, out long localFileId))
				{
					if (!fileIdToGuid.ContainsKey(localFileId))
					{
						fileIdToGuid[localFileId] = guid;
						scriptCount++;
						Debug.Log(
							$"[FixScripts] DLL script: fileID={localFileId}, " +
							$"guid={guid}, class={monoScript.GetClass()?.Name ?? "?"}");
					}
				}
			}
		}

		Debug.Log($"[FixScripts] Built script mapping: {scriptCount} scripts registered.");
		foreach (var kvp in fileIdToGuid.OrderBy(k => k.Key))
		{
			Debug.Log($"[FixScripts]   fileID={kvp.Key} -> guid={kvp.Value}");
		}

		// ------------------------------------------------------------------
		// Step 2: Scan prefabs & scenes, fix mismatched script GUIDs
		// ------------------------------------------------------------------
		var assetPaths = new List<string>();

		string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
		foreach (string guid in prefabGuids)
			assetPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

		string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
		foreach (string guid in sceneGuids)
			assetPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

		int totalReferences = 0;
		int fixedReferences = 0;
		int unresolvedReferences = 0;
		int modifiedAssets = 0;

		foreach (string assetPath in assetPaths)
		{
			string yaml = File.ReadAllText(assetPath);
			bool modified = false;

			string newYaml = ScriptReferencePattern.Replace(yaml, match =>
			{
				totalReferences++;

				long fileID = long.Parse(match.Groups[1].Value);
				string currentGuid = match.Groups[2].Value;
				string typeValue = match.Groups[3].Value;

				if (fileIdToGuid.TryGetValue(fileID, out string correctGuid))
				{
					if (currentGuid != correctGuid)
					{
						modified = true;
						fixedReferences++;
						Debug.Log(
							$"[FixScripts] Fixing '{assetPath}': " +
							$"fileID={fileID}, guid {currentGuid} -> {correctGuid}");
						return $"m_Script: {{fileID: {fileID}, guid: {correctGuid}, type: {typeValue}}}";
					}
				}
				else
				{
					unresolvedReferences++;
					Debug.LogWarning(
						$"[FixScripts] No matching script found for fileID={fileID} " +
						$"in '{assetPath}' — script may have been removed entirely.");
				}

				return match.Value;
			});

			if (modified)
			{
				File.WriteAllText(assetPath, newYaml);
				modifiedAssets++;
			}
		}

		if (modifiedAssets > 0)
			AssetDatabase.Refresh();

		// ------------------------------------------------------------------
		// Step 3: Report
		// ------------------------------------------------------------------
		string summary =
			$"Script Reference Repair Complete:\n" +
			$"• {scriptCount} scripts mapped\n" +
			$"• {assetPaths.Count} assets scanned " +
			$"({prefabGuids.Length} prefabs, {sceneGuids.Length} scenes)\n" +
			$"• {totalReferences} script references checked\n" +
			$"• {fixedReferences} references fixed in {modifiedAssets} assets\n" +
			$"• {unresolvedReferences} references could not be resolved";

		Debug.Log($"[FixScripts] {summary}");
		EditorUtility.DisplayDialog("Fix Missing Script References", summary, "OK");
	}
}