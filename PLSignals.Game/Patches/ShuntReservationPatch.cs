using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using System.Collections.Generic;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// Keeps the "Shunting Allowed" flag in sync with SHUNT reservations: raised on success,
    /// restored to the previous value once the reservation ends (expiry, cancel, or a normal one taking over).
    /// </summary>
    internal static class ShuntReservationTracker
    {
        /// <summary>
        /// Shunt-enabled signals → flag value to restore when their reservation ends.
        /// </summary>
        private static readonly Dictionary<SignalBase, bool> s_shuntReservations = new Dictionary<SignalBase, bool>();

        /// <summary>
        /// Signals with a SHUNT reservation requested but not yet made.
        /// </summary>
        private static readonly HashSet<SignalBase> s_pendingShunt = new HashSet<SignalBase>();

        /// <summary>
        /// Nested <see cref="TrackReserver.UpdateReservation"/> calls per signal: the renewal's
        /// clear + re-reserve must not count as a new train movement.
        /// </summary>
        private static readonly Dictionary<SignalBase, int> s_renewing = new Dictionary<SignalBase, int>();

        internal static void Initialize()
        {
            TrackReserver.ReservationMade += OnReservationMade;
            TrackReserver.ReservationCleared += OnReservationCleared;
            MultiplayerIntegration.OnReservationRequestResultReceived += OnReservationRequestResult;
        }

        /// <summary>
        /// Marks the signal's next reservation as SHUNT, so success enables shunting on it.
        /// </summary>
        internal static void MarkShuntAttempt(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            s_pendingShunt.Add(signal);
        }

        internal static void CancelShuntAttempt(SignalBase signal)
        {
            s_pendingShunt.Remove(signal);
        }

        internal static void BeginRenewal(SignalBase signal)
        {
            s_renewing.TryGetValue(signal, out var count);
            s_renewing[signal] = count + 1;
        }

        internal static void EndRenewal(SignalBase signal)
        {
            if (!s_renewing.TryGetValue(signal, out var count)) return;

            if (count <= 1)
            {
                s_renewing.Remove(signal);
            }
            else
            {
                s_renewing[signal] = count - 1;
            }
        }

        /// <summary>
        /// Drops all shunt state. Called when all reservations are dropped at once, which fires no individual events.
        /// </summary>
        internal static void ClearAll()
        {
            foreach (var item in s_shuntReservations)
            {
                // No aspect updates: definitions may already be destroyed during teardown.
                item.Key.SetShuntingStatus(item.Value);
            }

            s_shuntReservations.Clear();
            s_pendingShunt.Clear();
            s_renewing.Clear();
        }

        private static void OnReservationMade(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            // Renewals keep the current shunting state.
            if (s_renewing.ContainsKey(signal)) return;

            if (s_pendingShunt.Remove(signal))
            {
                EnableShunt(signal);
            }
            else
            {
                // Normal train movement: drop any stale shunt override.
                RevertShunt(signal);
            }
        }

        private static void OnReservationCleared(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            // Renewal in progress: the reservation comes right back.
            if (s_renewing.ContainsKey(signal)) return;

            // A shunt re-reserve is in flight: EnableShunt refreshes the state after it.
            if (s_pendingShunt.Contains(signal)) return;

            RevertShunt(signal);
        }

        private static void OnReservationRequestResult(int id, bool result)
        {
            // MP attempts are async; a failed one must not leak into the next reservation.
            if (result) return;

            SignalBase? failed = null;

            foreach (var signal in s_pendingShunt)
            {
                if (signal.Id == id)
                {
                    failed = signal;
                    break;
                }
            }

            if (failed != null)
            {
                s_pendingShunt.Remove(failed);
            }
        }

        private static void EnableShunt(SignalBase signal)
        {
            if (!s_shuntReservations.ContainsKey(signal))
            {
                s_shuntReservations[signal] = signal.ShuntingAllowed;
            }

            if (signal.SetShuntingStatus(true))
            {
                UpdateAspectIfAlive(signal);
            }
        }

        private static void RevertShunt(SignalBase signal)
        {
            if (!s_shuntReservations.TryGetValue(signal, out var previous)) return;

            s_shuntReservations.Remove(signal);

            if (signal.SetShuntingStatus(previous))
            {
                UpdateAspectIfAlive(signal);
            }
        }

        private static void UpdateAspectIfAlive(SignalBase signal)
        {
            // Definitions are destroyed when signals are torn down (world unload / pack switch).
            if (signal.Definition != null)
            {
                signal.UpdateAspect(false);
            }
        }
    }

    /// <summary>
    /// Marks the clear + re-reserve inside <see cref="TrackReserver.UpdateReservation"/> as a renewal,
    /// not a new train movement.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.UpdateReservation))]
    internal static class ShuntReservationRenewalPatch
    {
        private static void Prefix(SignalBase signal)
        {
            ShuntReservationTracker.BeginRenewal(signal);
        }

        // Finalizer so the counter is released even when the original throws.
        private static void Finalizer(SignalBase signal)
        {
            ShuntReservationTracker.EndRenewal(signal);
        }
    }

    /// <summary>
    /// Drops all shunt state when all reservations are cleared at once.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ClearAll))]
    internal static class ShuntReservationClearAllPatch
    {
        private static void Postfix()
        {
            ShuntReservationTracker.ClearAll();
        }
    }
}