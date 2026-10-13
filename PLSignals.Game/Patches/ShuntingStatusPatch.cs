using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using System.Reflection;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// Entry/exit signals must not show shunting aspects for normal reservations, so their
    /// "Shunting Allowed" flag defaults to <see langword="false"/> and is only raised by the SHUNT option.
    /// </summary>
    internal static class ShuntingStatusHelper
    {
        private static readonly FieldInfo? s_shuntingAllowedField =
            typeof(SignalBase).GetField("_shuntingAllowed", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Like <see cref="VirtualReservationHelper.IsOurPackActive"/>, but also <see langword="true"/>
        /// during signal creation, before <see cref="SignalManager.Running"/> is set.
        /// </summary>
        internal static bool IsOurPackActiveForCreation()
        {
            var pack = SignalManager.CurrentPack;
            return pack != null && pack.ModId == VirtualReservationHelper.ModId;
        }

        /// <summary>
        /// True for signals instantiated from our entry/exit prefabs, including merged
        /// controllers and the "Old" pack slots.
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

                // Clones are named "<prefab>(Clone)"; accept the plain name too.
                return instanceName == slotName || instanceName == slotName + "(Clone)";
            }
        }

        /// <summary>
        /// Sets the flag without firing change events, so signal creation doesn't spam multiplayer packets.
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
    /// MP join-state sync treats the entry/exit default (<see langword="false"/>) as default,
    /// so active shunt reservations are synchronised to joining players.
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