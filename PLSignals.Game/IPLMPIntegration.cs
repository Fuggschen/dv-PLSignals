namespace PLSignals
{
    /// <summary>
    /// Interface for multiplayer integration.
    /// </summary>
    public interface IPLMPIntegration
    {
        void BroadcastSettings(bool alignSwitchesOnReserve);
        void StartServer();
        void StartClient();
        void Stop();
    }
}