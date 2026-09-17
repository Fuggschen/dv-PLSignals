using System;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using PLSignals;

namespace PLSignals.MP
{
    public static class Bootstrap
    {
        public static void Initialize()
        {
            if (!MultiplayerAPI.IsMultiplayerLoaded)
                return;

            var impl = new MultiplayerManager();
            PLMPUtil.Register(impl);

            UnityEngine.Debug.Log("[PLSignals] PLSignals.MP: Bootstrap initialized.");
        }
    }

    public class MultiplayerManager : IPLMPIntegration
    {
        private static PLSettingsPacket CreatePacket()
        {
            return new PLSettingsPacket
            {
                AlignSwitchesOnReserve = Main.Settings.AlignSwitchesOnReserve
            };
        }

        private IServer? s_server;
        private IClient? s_client;

        public void StartServer()
        {
            try
            {
                s_server = MultiplayerAPI.Server;
                s_server.RegisterPacket<PLSettingsPacket>((packet, sender) =>
                {
                    s_server.SendPacketToAll(packet, excludePlayer: sender);
                    ApplySettings(packet);
                });

                s_server.OnPlayerConnected += PlayerConnected;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[PLSignals] Failure loading PLSignals server: {e.Message}");
            }
        }

        public void StartClient()
        {
            try
            {
                s_client = MultiplayerAPI.Client;
                s_client.RegisterPacket<PLSettingsPacket>(packet =>
                {
                    ApplySettings(packet);
                });
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[PLSignals] Failure loading PLSignals client: {e.Message}");
            }
        }

        public void BroadcastSettings(bool alignSwitchesOnReserve)
        {
            var packet = new PLSettingsPacket
            {
                AlignSwitchesOnReserve = alignSwitchesOnReserve
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

        private void PlayerConnected(IPlayer player)
        {
            s_server?.SendPacketToPlayer(CreatePacket(), player);
        }

        private static void ApplySettings(PLSettingsPacket packet)
        {
            Main.Settings.AlignSwitchesOnReserve = packet.AlignSwitchesOnReserve;
        }

        public void Stop()
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