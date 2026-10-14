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

            MultiplayerAPI.ServerStarted += _ => PLMPUtil.StartServer();
            MultiplayerAPI.ClientStarted += _ => PLMPUtil.StartClient();
            MultiplayerAPI.ServerStopped += impl.StopServer;
            MultiplayerAPI.ClientStopped += impl.StopClient;

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

        /// <summary>
        /// The client's own setting, restored when it leaves a session where the host's value applied.
        /// </summary>
        private bool? _localAlignSwitchesOnReserve;

        public void StartServer()
        {
            try
            {
                s_server = MultiplayerAPI.Server;
                // The host is the only authority for settings; client packets are ignored.
                s_server.RegisterPacket<PLSettingsPacket>((packet, sender) => { });

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
            if (!MultiplayerAPI.Instance.IsHost || s_server == null) return;

            var packet = new PLSettingsPacket
            {
                AlignSwitchesOnReserve = alignSwitchesOnReserve
            };

            s_server.SendPacketToAll(packet, excludeSelf: true);
        }

        private void PlayerConnected(IPlayer player)
        {
            s_server?.SendPacketToPlayer(CreatePacket(), player);
        }

        private void ApplySettings(PLSettingsPacket packet)
        {
            // The host's local client already has the value.
            if (MultiplayerAPI.Instance.IsHost) return;

            _localAlignSwitchesOnReserve ??= Main.Settings.AlignSwitchesOnReserve;
            Main.Settings.AlignSwitchesOnReserve = packet.AlignSwitchesOnReserve;
        }

        internal void StopServer()
        {
            if (s_server != null)
            {
                s_server.OnPlayerConnected -= PlayerConnected;
                s_server = null;
            }
        }

        internal void StopClient()
        {
            s_client = null;

            if (_localAlignSwitchesOnReserve.HasValue)
            {
                Main.Settings.AlignSwitchesOnReserve = _localAlignSwitchesOnReserve.Value;
                _localAlignSwitchesOnReserve = null;
            }
        }

        public void Stop()
        {
            StopServer();
            StopClient();
        }
    }
}
