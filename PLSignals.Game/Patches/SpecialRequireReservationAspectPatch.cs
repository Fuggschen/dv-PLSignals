using HarmonyLib;
using Signals.Game;
using Signals.Game.Aspects;
using Signals.Game.Railway;

namespace PLSignals.Patches
{
    /// <summary>
    /// Keeps virtually reserved shunting signals from showing STOP via
    /// <see cref="SpecialRequireReservationAspect.MeetsConditions"/>. Only active while the PLSignals pack is selected.
    /// </summary>
    [HarmonyPatch(typeof(SpecialRequireReservationAspect), nameof(SpecialRequireReservationAspect.MeetsConditions))]
    internal static class SpecialRequireReservationAspectPatch
    {
        private static void Postfix(SpecialRequireReservationAspect __instance, ref bool __result)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            // Only the "showing STOP" case is overridden.
            if (!__result) return;

            if (!__instance.Signal.IsShunting) return;

            if (VirtualReservationHelper.VirtualReservations.Contains(__instance.Signal))
            {
                __result = false;
            }
        }
    }
}
