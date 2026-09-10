using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace PLSignals;

public static class Main
{
	internal static PLSignalsSettings Settings = null!;
	internal static UnityModManager.ModEntry ModEntry = null!;

	// Unity Mod Manage Wiki: https://wiki.nexusmods.com/index.php/Category:Unity_Mod_Manager
	private static bool Load(UnityModManager.ModEntry modEntry)
	{
		ModEntry = modEntry;
		Settings = UnityModManager.ModSettings.Load<PLSignalsSettings>(modEntry);
		modEntry.OnGUI = OnGUI;
		modEntry.OnSaveGUI = OnSaveGUI;

		Harmony? harmony = null;

		try
		{
			harmony = new Harmony(modEntry.Info.Id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			// Other plugin startup logic
		}
		catch (Exception ex)
		{
			modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
			harmony?.UnpatchAll(modEntry.Info.Id);
			return false;
		}

		return true;
	}

	private static void OnGUI(UnityModManager.ModEntry modEntry)
	{
		Settings.Draw(modEntry);
	}

	private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
	{
		Settings.Save(modEntry);
	}
}