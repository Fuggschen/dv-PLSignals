using HarmonyLib;
using Signals.Game;
using Signals.Game.Aspects;
using Signals.Game.Railway;

namespace PLSignals.Patches
{
    /// <summary>
    /// Patches <see cref="SpecialRequireReservationAspect.MeetsConditions"/> so that
    /// shunting signals that are virtually reserved don't show STOP.
    /// Only active when the PLSignals pack is the currently selected signal pack.
    /// </summary>
    [HarmonyPatch(typeof(SpecialRequireReservationAspect), nameof(SpecialRequireReservationAspect.MeetsConditions))]
    internal static class SpecialRequireReservationAspectPatch
    {
        private static void Postfix(SpecialRequireReservationAspect __instance, ref bool __result)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            // Only override when the original result was true (showing STOP because no reservation).
            if (!__result) return;

            // Only apply to shunting signals that are virtually reserved.
            if (!__instance.Signal.IsShunting) return;

            if (VirtualReservationHelper.VirtualReservations.Contains(__instance.Signal))
            {
                __result = false;
            }
        }
    }
}
