using DV;
using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// Adds a SHUNT option to the Comms Radio reservation menu (60s reservation, marks a shunt move).
    /// <c>CommsRadioSignalReserver</c> is internal to the framework, so all member access goes through reflection.
    /// </summary>
    internal static class CommsRadioShuntOption
    {
        /// <summary>Duration used for SHUNT reservations, in seconds.</summary>
        private const float ShuntDuration = 60f;

        private static readonly Type s_radioType = AccessTools.TypeByName("Signals.Game.CommsRadioSignalReserver")!;
        private static readonly float[] s_durations = (float[])AccessTools.Field(s_radioType, "s_durations")!.GetValue(null)!;

        /// <summary>Index of the SHUNT entry in the option cycle, right after the framework durations.</summary>
        private static readonly int ShuntIndex = s_durations.Length;
        /// <summary>Option count: the framework durations plus SHUNT.</summary>
        private static readonly int OptionCount = s_durations.Length + 1;
        /// <summary>The framework duration slot used for SHUNT reservations, falling back to the first slot.</summary>
        private static readonly int ShuntDurationIndex = Math.Max(Array.IndexOf(s_durations, ShuntDuration), 0);
        private static readonly FieldInfo s_durationIndexField = AccessTools.Field(s_radioType, "_durationIndex")!;
        private static readonly FieldInfo s_signalField = AccessTools.Field(s_radioType, "_signal")!;
        private static readonly FieldInfo s_activeField = AccessTools.Field(s_radioType, "_active")!;
        private static readonly FieldInfo s_displayOverridenField = AccessTools.Field(s_radioType, "_displayOverriden")!;
        private static readonly FieldInfo s_reservationLockedField = AccessTools.Field(s_radioType, "_reservationLocked")!;
        private static readonly FieldInfo s_displayField = AccessTools.Field(s_radioType, "Display")!;
        private static readonly MethodInfo s_setDisplayToSignalMethod = AccessTools.Method(s_radioType, "SetDisplayToSignal")!;

        private sealed class RadioState
        {
            public bool ShuntSelected;
        }

        private static readonly ConditionalWeakTable<object, RadioState> s_states = new ConditionalWeakTable<object, RadioState>();

        private static RadioState GetState(object instance) => s_states.GetValue(instance, _ => new RadioState());

        internal static MethodBase GetMethod(string name) => AccessTools.Method(s_radioType, name)!;

        private static int GetDurationIndex(object instance) => (int)s_durationIndexField.GetValue(instance)!;
        private static void SetDurationIndex(object instance, int value) => s_durationIndexField.SetValue(instance, value);
        private static SignalBase? GetSignal(object instance) => (SignalBase?)s_signalField.GetValue(instance);
        private static bool GetActive(object instance) => (bool)s_activeField.GetValue(instance)!;
        private static bool GetDisplayOverriden(object instance) => (bool)s_displayOverridenField.GetValue(instance)!;
        private static bool GetReservationLocked(object instance) => (bool)s_reservationLockedField.GetValue(instance)!;
        private static CommsRadioDisplay? GetDisplay(object instance) => (CommsRadioDisplay?)s_displayField.GetValue(instance);
        private static void InvokeSetDisplayToSignal(object instance) => s_setDisplayToSignalMethod.Invoke(instance, null);

        /// <summary>
        /// Cycles the reservation options (5 durations plus SHUNT); returns <see langword="false"/>
        /// when handled here so the original method is skipped.
        /// </summary>
        internal static bool CyclePrefix(object __instance, ref bool __result, int direction)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return true;

            if (GetReservationLocked(__instance))
            {
                __result = false;
                return false;
            }

            var state = GetState(__instance);
            var current = state.ShuntSelected ? ShuntIndex : GetDurationIndex(__instance);
            var next = (current + direction + OptionCount) % OptionCount;

            state.ShuntSelected = next == ShuntIndex;
            SetDurationIndex(__instance, state.ShuntSelected ? ShuntDurationIndex : next);
            InvokeSetDisplayToSignal(__instance);

            __result = true;
            return false;
        }

        /// <summary>
        /// Registers a pending SHUNT attempt so the tracker can enable shunting on success.
        /// </summary>
        internal static void OnUsePrefix(object __instance)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;
            if (!GetState(__instance).ShuntSelected) return;
            if (GetReservationLocked(__instance) || !GetActive(__instance) || GetDisplayOverriden(__instance)) return;

            var signal = GetSignal(__instance);
            if (signal != null)
            {
                ShuntReservationTracker.MarkShuntAttempt(signal);
            }
        }

        /// <summary>
        /// Drops the pending SHUNT attempt on synchronous failure. MP requests stay pending:
        /// the radio is locked and the result arrives later.
        /// </summary>
        internal static void OnUsePostfix(object __instance)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;
            if (GetReservationLocked(__instance)) return;

            var signal = GetSignal(__instance);
            if (signal != null)
            {
                ShuntReservationTracker.CancelShuntAttempt(signal);
            }
        }

        /// <summary>
        /// Replaces the duration line with "SHUNT" while the SHUNT option is selected.
        /// </summary>
        internal static void SetDisplayToSignalPostfix(object __instance)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;
            if (!GetState(__instance).ShuntSelected) return;

            // Keep success / failure / reserving messages on screen.
            if (GetDisplayOverriden(__instance)) return;

            var display = GetDisplay(__instance);
            if (display == null) return;

            var signal = GetSignal(__instance);

            if (signal == null)
            {
                display.SetContentAndAction("Signal: None\nSHUNT", string.Empty);
                return;
            }

            var action = global::Signals.Game.Localization.Radio.Reserve;
            var id = signal.Id.ToString();

            if (TrackReserver.HasReservation(signal, out var time))
            {
                display.SetContentAndAction(time > 0
                    ? $"Signal: {id}\nSHUNT\nReserved for {time:F0} seconds"
                    : $"Signal: {id}\nSHUNT\nReserved", action);
            }
            else
            {
                display.SetContentAndAction($"Signal: {id}\nSHUNT", action);
            }
        }
    }

    /// <summary>
    /// Extends the duration cycle (button A) with the SHUNT option.
    /// </summary>
    [HarmonyPatch]
    internal static class CommsRadioShuntButtonAPatch
    {
        private static MethodBase TargetMethod() => CommsRadioShuntOption.GetMethod("ButtonACustomAction");

        private static bool Prefix(object __instance, ref bool __result)
        {
            return CommsRadioShuntOption.CyclePrefix(__instance, ref __result, -1);
        }
    }

    /// <summary>
    /// Extends the duration cycle (button B) with the SHUNT option.
    /// </summary>
    [HarmonyPatch]
    internal static class CommsRadioShuntButtonBPatch
    {
        private static MethodBase TargetMethod() => CommsRadioShuntOption.GetMethod("ButtonBCustomAction");

        private static bool Prefix(object __instance, ref bool __result)
        {
            return CommsRadioShuntOption.CyclePrefix(__instance, ref __result, 1);
        }
    }

    /// <summary>
    /// Registers SHUNT reservation attempts before the reservation is made.
    /// </summary>
    [HarmonyPatch]
    internal static class CommsRadioShuntOnUsePatch
    {
        private static MethodBase TargetMethod() => CommsRadioShuntOption.GetMethod("OnUse");

        private static void Prefix(object __instance)
        {
            CommsRadioShuntOption.OnUsePrefix(__instance);
        }

        private static void Postfix(object __instance)
        {
            CommsRadioShuntOption.OnUsePostfix(__instance);
        }
    }

    /// <summary>
    /// Shows "SHUNT" instead of the duration while the SHUNT option is selected.
    /// </summary>
    [HarmonyPatch]
    internal static class CommsRadioShuntDisplayPatch
    {
        private static MethodBase TargetMethod() => CommsRadioShuntOption.GetMethod("SetDisplayToSignal");

        private static void Postfix(object __instance)
        {
            CommsRadioShuntOption.SetDisplayToSignalPostfix(__instance);
        }
    }
}