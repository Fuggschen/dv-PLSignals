using MPAPI.Interfaces.Packets;

namespace PLSignals.MP
{
    public class PLSettingsPacket : IPacket
    {
        public bool AlignSwitchesOnReserve { get; set; } = true;
    }
}