using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using PLSignals;

namespace PLSignals.MP
{
    public static class MultiplayerManager
    {
        private static IServer? s_server;
        private static IClient? s_client;

        public static void StartServer()
        {
            try
            {
                s_server = MultiplayerAPI.Server;
                s_server.RegisterPacket<PLSettingsPacket>((packet, sender) =>
                {
                    // Forward settings to all other clients.
                    s_server.SendPacketToAll(packet, excludePlayer: sender);
                    // Apply on host too.
                    ApplySettings(packet);
                });

                // When a player connects, send them our current settings.
                s_server.OnPlayerConnected += PlayerConnected;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[PLSignals] Failure loading PLSignals server: {e.Message}");
            }
        }

        public static void StartClient()
        {
            try
            {
                s_client = MultiplayerAPI.Client;
                s_client.RegisterPacket<PLSettingsPacket>(packet =>
                {
                    ApplySettings(packet);
                });
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[PLSignals] Failure loading PLSignals client: {e.Message}");
            }
        }

        public static void BroadcastSettings()
        {
            var packet = new PLSettingsPacket
            {
                AlignSwitchesOnReserve = Main.Settings.AlignSwitchesOnReserve
            };

            if (MultiplayerAPI.Instance.IsHost && s_server != null)
            {
                s_server.SendPacketToAll(packet, excludeSelf: true);
            }
            else if (s_client != null)
            {
                s_client.SendPacketToServer(packet);
            }
        }

        private static void PlayerConnected(IPlayer player)
        {
            s_server?.SendPacketToPlayer(new PLSettingsPacket
            {
                AlignSwitchesOnReserve = Main.Settings.AlignSwitchesOnReserve
            }, player);
        }

        private static void ApplySettings(PLSettingsPacket packet)
        {
            Main.Settings.AlignSwitchesOnReserve = packet.AlignSwitchesOnReserve;
        }

        public static void Stop()
        {
            if (s_server != null)
            {
                s_server.OnPlayerConnected -= PlayerConnected;
                s_server = null;
            }
            s_client = null;
        }
    }
}