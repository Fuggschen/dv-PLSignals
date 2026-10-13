using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace PLSignals;

public static class Main
{
	public static PLSignalsSettings Settings = null!;
	internal static UnityModManager.ModEntry ModEntry = null!;

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
		}
		catch (Exception ex)
		{
			modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
			harmony?.UnpatchAll(modEntry.Info.Id);
			return false;
		}

		PLMPUtil.LoadIntegration(modEntry);
		Patches.ShuntReservationTracker.Initialize();

		return true;
	}

	internal static void Log(string message)
	{
		ModEntry.Logger.Log(message);
	}

	private static void OnGUI(UnityModManager.ModEntry modEntry)
	{
		// Disable settings when MP is running and not host (mirrors framework behavior).
		UnityEngine.GUI.enabled = !Signals.Game.MultiplayerIntegration.IsMpRunning || Signals.Game.MultiplayerIntegration.IsHost;
		Settings.Draw(modEntry);
		UnityEngine.GUI.enabled = true;
	}

	private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
	{
		Settings.Save(modEntry);
		PLMPUtil.BroadcastSettings(Settings.AlignSwitchesOnReserve);
	}
}