using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using System.Collections.Generic;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// Tracks shunt reservations (Comms Radio "SHUNT" option) and keeps the
    /// "Shunting Allowed" flag in sync with their lifetime:
    /// a successful SHUNT reservation raises the flag, and it is restored to its
    /// previous value as soon as that reservation ends (expiry, cancellation, or a
    /// normal train reservation taking over).
    /// </summary>
    internal static class ShuntReservationTracker
    {
        /// <summary>
        /// Signals currently shunt-enabled by us, mapped to the flag value to restore
        /// when their reservation ends.
        /// </summary>
        private static readonly Dictionary<SignalBase, bool> s_shuntReservations = new Dictionary<SignalBase, bool>();

        /// <summary>
        /// Signals whose next reservation was requested as a SHUNT reservation, but has
        /// not succeeded yet.
        /// </summary>
        private static readonly HashSet<SignalBase> s_pendingShunt = new HashSet<SignalBase>();

        /// <summary>
        /// Nested <see cref="TrackReserver.UpdateReservation"/> calls per signal. Block
        /// updates renew existing reservations by clearing and re-reserving them, which
        /// must not be treated as a new train movement.
        /// </summary>
        private static readonly Dictionary<SignalBase, int> s_renewing = new Dictionary<SignalBase, int>();

        internal static void Initialize()
        {
            TrackReserver.ReservationMade += OnReservationMade;
            TrackReserver.ReservationCleared += OnReservationCleared;
            MultiplayerIntegration.OnReservationRequestResultReceived += OnReservationRequestResult;
        }

        /// <summary>
        /// Registers that the given signal is about to be reserved with the SHUNT option,
        /// so a successful reservation enables shunting on it.
        /// </summary>
        internal static void MarkShuntAttempt(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            s_pendingShunt.Add(signal);
        }

        /// <summary>
        /// Drops a pending SHUNT reservation attempt that did not succeed.
        /// </summary>
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
        /// Drops all shunt state. Called when all reservations are dropped at once
        /// (which fires no individual events).
        /// </summary>
        internal static void ClearAll()
        {
            foreach (var item in s_shuntReservations)
            {
                // No aspect updates: this runs during teardown, when definitions may
                // already be destroyed.
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
                // Any other reservation is a normal train movement: make sure no stale
                // shunt override is left behind.
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
            // Multiplayer reservation attempts are asynchronous: a failed one must not
            // leak into the next reservation of the same signal.
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
    /// Marks reservation renewals, so the tracker does not treat the clear + re-reserve
    /// performed by <see cref="TrackReserver.UpdateReservation"/> as a new train movement.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.UpdateReservation))]
    internal static class ShuntReservationRenewalPatch
    {
        private static void Prefix(SignalBase signal)
        {
            ShuntReservationTracker.BeginRenewal(signal);
        }

        private static void Postfix(SignalBase signal)
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