using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// With "Align Switches on Reserve" off, truncates the reservation at the first misaligned switch instead of flipping it.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ReserveForSignal), new[] { typeof(SignalBase) })]
    internal static class TruncateReservationPatch
    {
        private static readonly FieldInfo? s_reservationsField =
            typeof(TrackReserver).GetField("s_reservations", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo? s_signalsField =
            typeof(TrackReserver).GetField("s_signals", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Truncated track set per reserved signal, used by propagation instead of the full block.
        /// </summary>
        internal static readonly Dictionary<SignalBase, HashSet<RailTrack>> TruncatedBlocks = new Dictionary<SignalBase, HashSet<RailTrack>>();

        private static bool Prefix(SignalBase signal, ref bool __result)
        {
            if (Main.Settings.AlignSwitchesOnReserve) return true;
            if (!SignalManager.Running || SignalManager.CurrentPack.ModId != "PLSignals") return true;

            signal.Controller.UpdateBlocks();

            var block = signal.Block;
            if (block == null)
            {
                __result = false;
                return false;
            }

            if (s_reservationsField == null || s_signalsField == null)
            {
                // Reflection failed, fall back to original.
                return true;
            }

            var reservations = (Dictionary<RailTrack, SignalBase>)s_reservationsField.GetValue(null)!;
            var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;

            var truncatedSet = ComputeTruncatedTracks(block);

            var includedJunctions = new HashSet<Junction>();
            foreach (var trackInfo in block.Tracks)
            {
                if (truncatedSet.Contains(trackInfo.Track) && trackInfo.IsJunctionTrack)
                {
                    includedJunctions.Add(trackInfo.Track.inJunction);
                }
            }

            foreach (var track in block.ExtraTracks)
            {
                // Extra tracks never extend past the truncation point.
                if (truncatedSet.Contains(track) && !track.isJunctionTrack)
                {
                    truncatedSet.Add(track);
                }
            }

            // Only the truncated tracks can conflict; a reservation past the misaligned switch never blocks this one.
            foreach (var track in truncatedSet)
            {
                if (reservations.TryGetValue(track, out var by) && by.Controller != signal.Controller)
                {
                    __result = false;
                    return false;
                }
            }

            if (signals.Contains(signal))
            {
                TrackReserver.ClearFromSignal(signal);
            }

            var hasTracks = false;

            foreach (var track in truncatedSet)
            {
                if (!reservations.ContainsKey(track))
                {
                    reservations.Add(track, signal);
                    hasTracks = true;
                }
            }

            if (!hasTracks)
            {
                __result = false;
                return false;
            }

            signals.Add(signal);
            TrackReserver.ReservationMade?.Invoke(signal);

            TruncatedBlocks[signal] = truncatedSet;

            __result = true;
            return false;
        }

        /// <summary>
        /// Tracks a reservation takes with switch alignment off: the walk stops before the first misaligned switch.
        /// </summary>
        internal static HashSet<RailTrack> ComputeTruncatedTracks(TrackBlock block)
        {
            var truncatedSet = new HashSet<RailTrack>();

            foreach (var trackInfo in block.Tracks)
            {
                if (trackInfo.IsJunctionTrack)
                {
                    var junction = trackInfo.Track.inJunction;

                    if (trackInfo.Direction == TrackDirection.In &&
                        junction.outBranches[junction.selectedBranch].track != trackInfo.Track)
                    {
                        // Stop before the misaligned switch track.
                        break;
                    }
                }

                truncatedSet.Add(trackInfo.Track);
            }

            return truncatedSet;
        }

        /// <summary>
        /// Removes the signal's track entries without stopping the expiry timer, mirroring
        /// <c>TrackReserver.ClearFromSignalInternal(signal, false)</c> so renewals keep the original duration.
        /// </summary>
        internal static void ClearTracksKeepTimer(SignalBase signal)
        {
            if (s_reservationsField == null || s_signalsField == null) return;

            var reservations = (Dictionary<RailTrack, SignalBase>)s_reservationsField.GetValue(null)!;
            var reservedBy = reservations.Where(x => x.Value == signal).ToList();

            // Mirrors the original: no reserved tracks means no event.
            if (reservedBy.Count == 0) return;

            foreach (var item in reservedBy)
            {
                reservations.Remove(item.Key);
            }

            var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
            signals.Remove(signal);
            TrackReserver.ReservationCleared?.Invoke(signal);
        }
    }

    /// <summary>
    /// Vanilla <c>TrackReserver.UpdateReservation</c> rejects on any full-block conflict, and a rejected
    /// update makes <c>BasicSignalController.Update</c> drop the reservation entirely. Conflicts past the
    /// truncation point must not count, or trimmed reservations die on every block update.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.UpdateReservation))]
    internal static class TruncateUpdateReservationPatch
    {
        private static bool Prefix(SignalBase signal, ref bool __result)
        {
            if (Main.Settings.AlignSwitchesOnReserve) return true;
            if (!SignalManager.Running || SignalManager.CurrentPack.ModId != "PLSignals") return true;

            var block = signal.Block;
            if (!TrackReserver.HasReservation(signal) || block == null)
            {
                __result = false;
                return false;
            }

            foreach (var track in TruncateReservationPatch.ComputeTruncatedTracks(block))
            {
                if (TrackChecker.IsReservedByAnother(track, signal))
                {
                    __result = false;
                    return false;
                }
            }

            // The renewal guard keeps shunt state intact and nests with ShuntReservationRenewalPatch regardless of patch order.
            ShuntReservationTracker.BeginRenewal(signal);

            try
            {
                TruncateReservationPatch.ClearTracksKeepTimer(signal);
                __result = TrackReserver.ReserveForSignal(signal);
            }
            finally
            {
                ShuntReservationTracker.EndRenewal(signal);
            }

            return false;
        }
    }

    /// <summary>
    /// Blocks switch alignment when the setting is off. AlignAllSwitches runs outside
    /// ReserveForSignal (CommsRadioSignalReserver calls it separately), so it needs its own guard.
    /// </summary>
    [HarmonyPatch(typeof(SignalBase), "AlignAllSwitches")]
    internal static class AlignAllSwitchesPatch
    {
        private static bool Prefix()
        {
            if (Main.Settings.AlignSwitchesOnReserve) return true;
            if (!SignalManager.Running || SignalManager.CurrentPack.ModId != "PLSignals") return true;

            return false;
        }
    }
}
