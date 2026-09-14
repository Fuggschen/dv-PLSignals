using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace PLSignals;

public static class Main
{
	public static PLSignalsSettings Settings = null!;
	internal static UnityModManager.ModEntry ModEntry = null!;

	private static MethodInfo? s_mpStartServer;
	private static MethodInfo? s_mpStartClient;
	private static MethodInfo? s_mpSetHostStatus;
	private static MethodInfo? s_mpSetRunningStatus;
	private static MethodInfo? s_mpBroadcastSettings;
	private static MethodInfo? s_mpStop;
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

		// Load MP integration.
		LoadMpIntegration(modEntry);

		return true;
	}

	private static void LoadMpIntegration(UnityModManager.ModEntry modEntry)
	{
		try
		{
			var path = Path.Combine(modEntry.Path, "PLSignals.MP.dll");

			if (!File.Exists(path)) return;

			var assembly = Assembly.LoadFile(path);
			var type = assembly.GetType("PLSignals.MP.MultiplayerManager");

			if (type == null) return;

			s_mpStartServer = type.GetMethod("StartServer");
			s_mpStartClient = type.GetMethod("StartClient");
			s_mpSetHostStatus = type.GetMethod("SetHostStatus");
			s_mpSetRunningStatus = type.GetMethod("SetRunningStatus");
			s_mpBroadcastSettings = type.GetMethod("BroadcastSettings");
			s_mpStop = type.GetMethod("Stop");

			ModEntry.Logger.Log("PLSignals multiplayer integration loaded successfully.");
		}
		catch (Exception e)
		{
			ModEntry.Logger.Warning($"Could not load PLSignals multiplayer integration: {e.Message}");
		}
	}

	internal static void MpStartServer()
	{
		s_mpStartServer?.Invoke(null, null);
	}

	internal static void MpStartClient()
	{
		s_mpStartClient?.Invoke(null, null);
	}

	internal static void MpSetHostStatus(bool isHost)
	{
		s_mpSetHostStatus?.Invoke(null, new object[] { isHost });
	}

	internal static void MpSetRunningStatus(bool isRunning)
	{
		s_mpSetRunningStatus?.Invoke(null, new object[] { isRunning });
	}

	internal static void MpBroadcastSettings()
	{
		s_mpBroadcastSettings?.Invoke(null, null);
	}

	internal static void MpStop()
	{
		s_mpStop?.Invoke(null, null);
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
		s_mpBroadcastSettings?.Invoke(null, null);
	}
}
