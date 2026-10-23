#nullable enable
using System;
using Player;
using SNetwork;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    internal sealed partial class HudGameContentSource
    {
        readonly record struct Peer(long Owner, ulong Lookup, ObjectIdentity Agent, ObjectIdentity Model, bool Bot, HudPeerState State);
        readonly Peer[] peers = new Peer[4];
        readonly long[] peerRevisions = new long[4];
        readonly HudTeamMark[] peerMarks = new HudTeamMark[4];
        long markSequence;
        LocalViewBinding rosterBinding;
        bool teamEnabled = true, rosterReadable;
        public void SetTeamEnabled(bool enabled)
        { if (OnMainThread && enabled != teamEnabled) { teamEnabled = enabled; rosterReadable = false; ClearTeamTransient(); changeVersion++; } }
        static Peer ReadPeer(int slot)
        { try { return ReadPeerCore(slot); } catch { return new(-1, 0, default, default, false, HudPeerState.Unknown); } }
        static Peer ReadPeerCore(int slot)
        {
            var slots = SNet.Slots; var manager = PlayerManager.Current;
            if (slots == null || manager == null) throw new InvalidOperationException("Roster unavailable.");
            var owner = slots.GetPlayerInSlot(slot);
            var agent = manager.GetPlayerAgentInSlot(slot);
            if (owner == null && agent == null) return default;
            if (owner == null || agent != null && (Id(agent.Owner) != Id(owner) || agent.PlayerSlotIndex != slot))
                throw new InvalidOperationException("Roster ownership mismatch.");
            bool spawned = agent != null && !agent.IsBeingDestroyed && !agent.IsBeingDespawned && Key(agent.PlayerSyncModel).IsValid;
            var state = owner.HasTimedOut || owner.ConnectionStatus == ConnectionStatus.NotConnected && !owner.IsBot
                ? HudPeerState.Offline : !spawned || !owner.IsBot && (!owner.IsInGame || owner.IsOutOfSync
                    || owner.ConnectionStatus == ConnectionStatus.Connecting) ? HudPeerState.Synchronizing : HudPeerState.Connected;
            return new(Id(owner), owner.Lookup, spawned ? Key(agent) : default, spawned ? Key(agent!.PlayerSyncModel) : default, owner.IsBot, state);
        }
        // Bounded roster identity observation also invalidates cached snapshots between collection intervals.
        void ObserveTeam()
        {
            if (!teamEnabled) return;
            try
            {
                var c = readContext();
                if (!c.HasValue || !HudEligibility.EvaluateCurrent(c.Value).Allowed) return;
                bool readable = PlayerManager.Current != null && SNet.Slots != null;
                bool changed = rosterBinding != c.Value.ExpectedBinding || readable != rosterReadable;
                if (rosterBinding != c.Value.ExpectedBinding) { rosterBinding = c.Value.ExpectedBinding; ClearTeamTransient(); }
                rosterReadable = readable;
                for (int i = 0; i < 4; i++)
                {
                    Peer next = default;
                    if (readable && i != c.Value.ExpectedBinding.LocalPlayer.Slot) next = ReadPeer(i);
                    if (next == peers[i]) continue;
                    peers[i] = next; peerMarks[i] = default; peerRevisions[i] = checked(peerRevisions[i] + 1); changed = true;
                }
                if (changed) changeVersion = checked(changeVersion + 1);
            }
            catch { if (rosterReadable) { rosterReadable = false; changeVersion++; } }
        }
        HudTeamSnapshot CaptureTeam(in HudContentCaptureRequest request)
        {
            if (!teamEnabled) return default;
            ObserveTeam();
            if (!rosterReadable) return default;
            try
            {
                int local = request.Origin.View.Binding.LocalPlayer.Slot, mask = 0;
                for (int i = 0; i < 4; i++) if (i != local && peers[i].Owner != 0) mask |= 1 << i;
                var stamp = HudValueStamp.AtCapture(request, request.Origin.View.Binding.LocalPlayer.Agent);
                var a = CapturePeer(0, local, stamp); var b = CapturePeer(1, local, stamp);
                var c = CapturePeer(2, local, stamp); var d = CapturePeer(3, local, stamp);
                // A roster change during this capture invalidates the entire roster, never the personal fields.
                for (int i = 0; i < 4; i++) if (i != local && ReadPeer(i) != peers[i]) return default;
                return new(Known(new HudTeamRoster(local, mask), stamp), a, b, c, d);
            }
            catch { return default; }
        }
        HudTeammateSnapshot CapturePeer(int slot, int local, HudValueStamp baseStamp)
        {
            if (slot == local) return default;
            var peer = peers[slot];
            var stamp = baseStamp with { SubjectRevision = peerRevisions[slot] };
            var missing = Missing<HudQuantity>(HudValueState.Unreadable, stamp);
            var row = new HudTeammateSnapshot(slot, Missing<HudTeamIdentity>(peer.Owner == 0 ? HudValueState.NotEquipped : HudValueState.Unreadable, stamp),
                Missing<HudText>(HudValueState.Unreadable, stamp), Missing<HudConnection>(HudValueState.Unreadable, stamp), missing, missing,
                Missing<HudLife>(HudValueState.Unreadable, stamp), missing, missing, missing, Missing<HudTeamMark>(HudValueState.Unknown, stamp));
            if (peer.Owner == 0 || peer.State == HudPeerState.Unknown) return row;
            try
            {
                var slots = SNet.Slots; var manager = PlayerManager.Current;
                if (slots == null || manager == null) return row;
                var owner = slots.GetPlayerInSlot(slot);
                var agent = manager.GetPlayerAgentInSlot(slot);
                if (owner == null || Id(owner) != peer.Owner || ReadPeer(slot) != peer) return row;
                row = row with { Identity = Known(new HudTeamIdentity(peer.Owner, peer.Lookup, slot, peer.Agent, peer.Model, peer.Bot, stamp.SubjectRevision), stamp),
                    Connection = Known(new HudConnection(peer.State), stamp) };
                try { row = row with { Name = Known(new HudText(owner.NickName), stamp) }; } catch { }
                if (peer.State != HudPeerState.Connected || agent == null) return row;
                var damage = agent.Damage;
                if (damage != null && Key(damage.Owner) == peer.Agent)
                {
                    try { row = row with { Health = HudNumberRules.TryHealth(damage.Health, damage.HealthMax, out var q) ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp) }; } catch { }
                    try { row = row with { Infection = HudNumberRules.TryInfection(damage.Infection, out var q) ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp) }; } catch { }
                }
                try
                {
                    // Master-owned bots send locomotion; remote replicas consume m_locomotionData.
                    PlayerLocomotion.PLOC_State? state = agent.IsLocallyOwned ? agent.Locomotion?.m_currentStateEnum
                        : agent.Sync != null && Key(agent.Sync.m_agent) == peer.Agent ? agent.Sync.m_locomotionData.State : null;
                    if (state.HasValue) row = row with { Life = Known(new HudLife(state.Value == PlayerLocomotion.PLOC_State.Downed
                        ? HudLifeKind.Downed : agent.Alive ? HudLifeKind.Alive : HudLifeKind.Dead), stamp) };
                }
                catch { }
                PlayerBackpack? bag = null;
                PlayerBackpackManager.TryGetBackpack(owner, out bag);
                if (bag != null && Id(bag.Owner) == peer.Owner && bag.AmmoStorage != null)
                {
                    var storage = bag.AmmoStorage;
                    row = row with { PrimaryRatio = PeerRatio(storage, AmmoType.Standard, stamp),
                        SecondaryRatio = PeerRatio(storage, AmmoType.Special, stamp), ToolRatio = PeerRatio(storage, AmmoType.Class, stamp) };
                    PlayerBackpackManager.TryGetBackpack(owner, out var after);
                    if (Id(after) != Id(bag) || Id(after?.AmmoStorage) != Id(storage)) return row with { PrimaryRatio = missing, SecondaryRatio = missing, ToolRatio = missing };
                }
                if (ReadPeer(slot) != peer) return default;
                if (peerMarks[slot].IsValid) row = row with { Marked = Known(peerMarks[slot], stamp) };
                return row;
            }
            catch { return row; }
        }
        static HudValue<HudQuantity> PeerRatio(PlayerAmmoStorage storage, AmmoType type, HudValueStamp stamp)
        {
            try
            {
                var ammo = storage.GetInventorySlotAmmo(type);
                if (ammo != null) return HudNumberRules.TryFraction(ammo.RelInPack, out var q) ? Known(q, stamp) : Missing<HudQuantity>(HudValueState.Invalid, stamp);
            }
            catch { }
            return Missing<HudQuantity>(HudValueState.Unreadable, stamp);
        }
        internal void NotifyTeamPing(long marker)
        {
            if (!OnMainThread || !teamEnabled || marker == 0) return;
            try
            {
                var current = readContext();
                if (!current.HasValue || !HudEligibility.EvaluateCurrent(current.Value).Allowed) return;
                ObserveTeam(); if (!rosterReadable) return;
                for (int i = 0; i < 4; i++)
                {
                    if (peers[i].State != HudPeerState.Connected) continue;
                    var manager = PlayerManager.Current; if (manager == null) return;
                    var agent = manager.GetPlayerAgentInSlot(i); var nav = agent?.NavMarker;
                    if (nav != null && Key(nav.Player) == peers[i].Agent && Id(nav.m_marker) == marker)
                    { peerMarks[i] = new(checked(++markSequence), clock()); changeVersion++; return; }
                }
            }
            catch { }
        }
        void ClearTeamTransient() { Array.Clear(peerMarks, 0, peerMarks.Length); }
    }
}
