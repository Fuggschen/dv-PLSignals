using Signals.Game;
using UnityModManagerNet;

namespace PLSignals
{
    public class PLSignalsSettings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Align Switches on Reserve", Tooltip = "When enabled, reserving a signal will physically flip misaligned switches.\nWhen disabled, reservation only extends up to the first misaligned switch.")]
        public bool AlignSwitchesOnReserve = true;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
            Main.MpBroadcastSettings();
        }
    }
}
