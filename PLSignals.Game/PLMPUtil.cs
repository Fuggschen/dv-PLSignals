using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

namespace PLSignals
{
    /// <summary>
    /// Multiplayer shim
    /// </summary>
    public static class PLMPUtil
    {
        const string MULTIPLAYER_MOD_ID = "Multiplayer";
        const string MPAPI_ASSEMBLY_NAME = "MultiplayerAPI";
        const string MP_INTEGRATION_DLL = "PLSignals.MP.dll";
        const string MP_INTEGRATION_BOOTSTRAP = "PLSignals.MP.Bootstrap";
        const string MP_INTEGRATION_INIT_METHOD = "Initialize";

        private static IPLMPIntegration integration = new NoOpIntegration();

        public static void Register(IPLMPIntegration impl)
        {
            integration = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public static void LoadIntegration(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModEntry multiplayer = UnityModManager.FindMod(MULTIPLAYER_MOD_ID);
            var mpapiAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == MPAPI_ASSEMBLY_NAME);

            var path = Path.Combine(modEntry.Path, MP_INTEGRATION_DLL);

            try
            {
                if (multiplayer?.Enabled == true && mpapiAssembly != null)
                {
                    if (!File.Exists(path))
                    {
                        modEntry.Logger.Warning($"{MP_INTEGRATION_DLL} was not found, multiplayer integration will be disabled.");
                        return;
                    }

                    var mpAssembly = Assembly.LoadFile(path);
                    var bootstrap = mpAssembly.GetType(MP_INTEGRATION_BOOTSTRAP);

                    if (bootstrap == null)
                    {
                        modEntry.Logger.Warning($"Failed to find {MP_INTEGRATION_BOOTSTRAP} in {MP_INTEGRATION_DLL}, multiplayer support will be disabled.");
                        return;
                    }

                    var init = bootstrap.GetMethod(MP_INTEGRATION_INIT_METHOD, BindingFlags.Public | BindingFlags.Static);
                    init?.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                modEntry.Logger.Warning($"Failed to load multiplayer integration.\r\n{ex.Message}\r\n{ex.StackTrace}");
            }
        }

        public static void BroadcastSettings(bool alignSwitchesOnReserve)
            => integration.BroadcastSettings(alignSwitchesOnReserve);

        public static void StartServer()
            => integration.StartServer();

        public static void StartClient()
            => integration.StartClient();

        public static void Stop()
            => integration.Stop();

        private class NoOpIntegration : IPLMPIntegration
        {
            public void BroadcastSettings(bool alignSwitchesOnReserve) { }
            public void StartServer() { }
            public void StartClient() { }
            public void Stop() { }
        }
    }
}