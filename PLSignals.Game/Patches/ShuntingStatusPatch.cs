using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using System.Reflection;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// Helpers for the per-signal "Shunting Allowed" flag.
    ///
    /// Entry and exit signals of our pack must not show shunting aspects for normal
    /// train reservations, so their flag defaults to <see langword="false"/>. It is
    /// only raised by the SHUNT reservation option (see <see cref="ShuntReservationTracker"/>).
    /// </summary>
    internal static class ShuntingStatusHelper
    {
        private static readonly FieldInfo? s_shuntingAllowedField =
            typeof(SignalBase).GetField("_shuntingAllowed", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Checks if our pack is the active pack. Unlike
        /// <see cref="VirtualReservationHelper.IsOurPackActive"/>, this also returns
        /// <see langword="true"/> while signals are being created, when
        /// <see cref="SignalManager.Running"/> is not set yet.
        /// </summary>
        internal static bool IsOurPackActiveForCreation()
        {
            var pack = SignalManager.CurrentPack;
            return pack != null && pack.ModId == VirtualReservationHelper.ModId;
        }

        /// <summary>
        /// Checks if the signal was instantiated from one of our pack's entry or exit
        /// signal prefabs. Also covers merged controllers (which are re-instantiated
        /// from the same prefabs) and the "Old" pack slots.
        /// </summary>
        internal static bool IsEntryOrExit(SignalBase signal)
        {
            var pack = SignalManager.CurrentPack;
            if (pack == null) return false;

            var definition = signal.Controller.Definition;
            if (definition == null) return false;

            var name = definition.gameObject.name;

            return SlotMatches(pack.EntrySignal, name) ||
                   SlotMatches(pack.ExitSignal, name) ||
                   SlotMatches(pack.ExitPassengerSignal, name) ||
                   SlotMatches(pack.ExitMainlineSignal, name) ||
                   SlotMatches(pack.OldEntrySignal, name) ||
                   SlotMatches(pack.OldExitSignal, name) ||
                   SlotMatches(pack.OldExitPassengerSignal, name) ||
                   SlotMatches(pack.OldExitMainlineSignal, name);

            static bool SlotMatches(SignalControllerDefinition? slot, string instanceName)
            {
                if (slot == null) return false;

                var slotName = slot.gameObject.name;

                // Instantiated clones are named "<prefab>(Clone)", but accept the plain
                // name as well in case naming ever changes.
                return instanceName == slotName || instanceName == slotName + "(Clone)";
            }
        }

        /// <summary>
        /// Applies the default shunting flag (<see langword="false"/> for entry/exit
        /// signals) without firing change events, so signal creation does not spam
        /// multiplayer packets.
        /// </summary>
        internal static void ApplyDefault(SignalBase signal)
        {
            if (s_shuntingAllowedField == null || !IsEntryOrExit(signal)) return;

            s_shuntingAllowedField.SetValue(signal, false);
        }
    }

    /// <summary>
    /// Applies the default "Shunting Allowed" flag right after a signal is created.
    /// </summary>
    [HarmonyPatch]
    internal static class SignalConstructorShuntingDefaultPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(typeof(SignalBase),
                new[] { typeof(BasicSignalController), typeof(SignalDefinition), typeof(bool) })!;
        }

        private static void Postfix(SignalBase __instance)
        {
            if (!ShuntingStatusHelper.IsOurPackActiveForCreation()) return;

            ShuntingStatusHelper.ApplyDefault(__instance);
        }
    }

    /// <summary>
    /// Makes the multiplayer join-state sync treat the entry/exit default
    /// (<see langword="false"/>) as the default state, so non-default shunting flags
    /// (e.g. an active shunt reservation) are synchronised to joining players.
    /// </summary>
    [HarmonyPatch(typeof(SignalBase), "get_IsDefaultShuntingAllowed")]
    internal static class IsDefaultShuntingAllowedPatch
    {
        private static void Postfix(SignalBase __instance, ref bool __result)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            __result = __instance.ShuntingAllowed == !ShuntingStatusHelper.IsEntryOrExit(__instance);
        }
    }
}