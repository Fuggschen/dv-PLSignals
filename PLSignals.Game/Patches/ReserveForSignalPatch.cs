using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    internal static class VirtualReservationHelper
    {
        internal const string ModId = "PLSignals";

        /// <summary>
        /// Tracks which main signal spawned which virtual reservations.
        /// Key: main signal that was reserved, Value: list of virtually reserved shunting signals.
        /// </summary>
        internal static readonly Dictionary<SignalBase, List<SignalBase>> PropagationMap = new Dictionary<SignalBase, List<SignalBase>>();

        internal static readonly HashSet<SignalBase> VirtualReservations = new HashSet<SignalBase>();

        internal static readonly FieldInfo? s_signalsField =
            typeof(TrackReserver).GetField("s_signals", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo? s_clearRoutinesField =
            typeof(TrackReserver).GetField("s_clearRoutines", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo? s_timesField =
            typeof(TrackReserver).GetField("s_times", BindingFlags.NonPublic | BindingFlags.Static);

        internal static bool IsOurPackActive()
        {
            return SignalManager.Running && SignalManager.CurrentPack.ModId == ModId;
        }

        /// <summary>
        /// Adds a virtual reservation: the signal appears reserved (HasReservation returns true)
        /// without actually owning any tracks in s_reservations.
        /// </summary>
        internal static void AddVirtualReservation(SignalBase signal)
        {
            if (s_signalsField != null)
            {
                var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
                signals.Add(signal);
            }

            VirtualReservations.Add(signal);
        }

        /// <summary>
        /// Removes a virtual reservation, cleaning up s_signals, s_clearRoutines and s_times.
        /// </summary>
        internal static bool RemoveVirtualReservation(SignalBase signal)
        {
            if (!VirtualReservations.Remove(signal)) return false;

            if (s_signalsField != null)
            {
                var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
                signals.Remove(signal);
            }

            if (s_clearRoutinesField != null)
            {
                var routines = (Dictionary<SignalBase, Coroutine>)s_clearRoutinesField.GetValue(null)!;
                if (routines.TryGetValue(signal, out var coroutine))
                {
                    CoroutineManager.Instance.Stop(coroutine);
                    routines.Remove(signal);
                }
            }

            if (s_timesField != null)
            {
                var times = (Dictionary<SignalBase, float>)s_timesField.GetValue(null)!;
                times.Remove(signal);
            }

            return true;
        }

        /// <summary>
        /// Clears all virtual reservations associated with a specific main signal.
        /// Called when the main signal's reservation expires or is cleared.
        /// </summary>
        internal static void ClearVirtualReservationsFor(SignalBase mainSignal)
        {
            if (!PropagationMap.TryGetValue(mainSignal, out var virtuals)) return;

            foreach (var signal in virtuals)
            {
                RemoveVirtualReservation(signal);
                SignalsMod.Log($"Cleared virtual reservation for {signal.Name}");
            }

            PropagationMap.Remove(mainSignal);
        }

        /// <summary>
        /// Checks if a shunting signal's block is entirely contained within another signal's
        /// reservation, with matching track directions. This prevents merging branches from
        /// being incorrectly considered contained.
        /// </summary>
        internal static bool IsBlockContainedInReservation(SignalBase shuntingSignal)
        {
            var block = shuntingSignal.Block;
            if (block == null) return false;

            var shuntingTracks = block.Tracks;

            foreach (var trackInfo in shuntingTracks)
            {
                if (!TrackReserver.IsTrackReserved(trackInfo.Track, out var by) || by.Controller == shuntingSignal.Controller)
                {
                    return false;
                }

                var mainBlock = by.Block;
                if (mainBlock == null) return false;

                var mainTrackSet = new HashSet<TrackInfo>(mainBlock.Tracks);

                foreach (var st in shuntingTracks)
                {
                    if (!TrackReserver.IsTrackReserved(st.Track, out var by2) || by2.Controller == shuntingSignal.Controller)
                    {
                        return false;
                    }

                    if (!mainTrackSet.Contains(st))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Propagates virtual reservations from a reserved main signal to all downstream
        /// shunting signals. Walks the reserved track path in order and virtually reserves
        /// shunting signals until hitting an occupied track. Only reserves shunting signals
        /// whose tracks point in the same direction as the reserved path.
        /// </summary>
        internal static void PropagateToShuntingSignals(SignalBase reservedSignal)
        {
            var block = reservedSignal.Block;
            if (block == null) return;

            // Build a set of TrackInfo (track + direction) from the reserved block
            // so we can check both track identity and direction.
            var reservedTrackSet = new HashSet<TrackInfo>(block.Tracks);

            // Also build an ordered list for position-based sorting.
            var trackOrder = new Dictionary<RailTrack, int>();
            for (int i = 0; i < block.Tracks.Length; i++)
            {
                trackOrder[block.Tracks[i].Track] = i;
            }

            // Find overlapping shunting controllers and determine their order.
            var candidates = new List<(BasicSignalController Controller, SignalBase Shunting, int Order)>();

            foreach (var controller in SignalManager.Instance.AllControllers)
            {
                if (controller.ShuntingSignals.Length == 0) continue;

                foreach (var shunting in controller.ShuntingSignals)
                {
                    if (shunting.Block == null) continue;

                    var shuntingTracks = shunting.Block.Tracks;

                    // Check that ALL shunting tracks are in the reserved set with matching direction.
                    // This prevents reserving signals that point the wrong way.
                    bool allContained = true;
                    int earliestOrder = int.MaxValue;

                    foreach (var st in shuntingTracks)
                    {
                        if (!reservedTrackSet.Contains(st))
                        {
                            allContained = false;
                            break;
                        }

                        if (trackOrder.TryGetValue(st.Track, out var order) && order < earliestOrder)
                        {
                            earliestOrder = order;
                        }
                    }

                    if (!allContained) continue;

                    candidates.Add((controller, shunting, earliestOrder));
                }
            }

            // Sort by position along the reserved path (earliest first).
            candidates.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Build the propagation map entry for this main signal.
            if (!PropagationMap.ContainsKey(reservedSignal))
            {
                PropagationMap[reservedSignal] = new List<SignalBase>();
            }

            // Walk in order, stopping at the first occupied track.
            foreach (var (controller, shunting, _) in candidates)
            {
                // Check if the shunting signal's tracks are occupied.
                if (shunting.Block!.IsOccupied(CrossingCheckMode.WholeTrack))
                {
                    SignalsMod.Log($"Propagation stopped at {controller.Name} ({shunting.Name}): tracks occupied");
                    break;
                }

                // Virtually reserve this shunting signal.
                if (!TrackReserver.HasReservation(shunting))
                {
                    AddVirtualReservation(shunting);
                    PropagationMap[reservedSignal].Add(shunting);
                    SignalsMod.Log($"Virtually reserved shunting signal {shunting.Name} on {controller.Name}");
                }
            }
        }

        /// <summary>
        /// Clears all virtual reservations. Called when the signal system resets.
        /// </summary>
        internal static void ClearAll()
        {
            foreach (var signal in VirtualReservations.ToList())
            {
                RemoveVirtualReservation(signal);
            }

            PropagationMap.Clear();
        }
    }

    /// <summary>
    /// Patches <see cref="TrackReserver.ReserveForSignal(SignalBase)"/> to allow shunting signals
    /// to be reserved when their block is contained within a main signal's reservation
    /// (with matching directions), and to propagate reservations to downstream shunting signals.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ReserveForSignal), new[] { typeof(SignalBase) })]
    internal static class ReserveForSignalPatch
    {
        private static void Postfix(ref bool __result, SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            if (__result)
            {
                // Reservation succeeded for a main signal - propagate to downstream shunting signals.
                if (!signal.IsShunting)
                {
                    VirtualReservationHelper.PropagateToShuntingSignals(signal);
                }
                return;
            }

            if (!signal.IsShunting) return;

            // Check if the shunting signal's block is entirely contained within
            // another signal's reservation, with matching track directions.
            if (VirtualReservationHelper.IsBlockContainedInReservation(signal))
            {
                VirtualReservationHelper.AddVirtualReservation(signal);
                __result = true;
            }
        }
    }

    /// <summary>
    /// Patches <see cref="TrackReserver.ClearFromSignal"/> to properly clean up virtual
    /// reservations that were added for contained shunting signals.
    /// Also clears propagated virtual reservations when a main signal's reservation expires.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ClearFromSignal))]
    internal static class ClearFromSignalPatch
    {
        private static void Prefix(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            // If this is a virtual reservation, clean it up.
            VirtualReservationHelper.RemoveVirtualReservation(signal);

            // If this main signal had propagated virtual reservations, clear them too.
            VirtualReservationHelper.ClearVirtualReservationsFor(signal);
        }
    }

    /// <summary>
    /// Patches <see cref="TrackReserver.ClearAll"/> to also clear virtual reservations.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ClearAll))]
    internal static class ClearAllPatch
    {
        private static void Postfix()
        {
            VirtualReservationHelper.ClearAll();
        }
    }
}