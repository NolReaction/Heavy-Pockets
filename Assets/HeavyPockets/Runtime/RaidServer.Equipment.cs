#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
using System;

namespace HeavyPockets
{
    public sealed partial class RaidServer
    {
        private void SendGear(Player player)
        {
            Send(player.Id, new RaidEvent
            {
                Kind = RaidEventKind.Gear, Gear = loot.Gear(player.Id), ActiveGear = player.ActiveGear
            });
        }

        private void GearCommand(Player player, RaidCommand command, byte[] bytes)
        {
            bool selection = command == RaidCommand.SelectGear;
            if (bytes.Length != (selection ? 6 : 14)) return;
            uint operation = BitConverter.ToUInt32(bytes, 1);
            if (operation <= player.LastTransfer) { Status(player, RaidStatus.Stale, operation); return; }
            player.LastTransfer = operation;
            if (player.Exited) { Status(player, RaidStatus.AlreadyExited, operation); return; }
            if (player.ActionCredits < 1f) { Status(player, RaidStatus.Denied, operation); return; }
            player.ActionCredits -= 1f;
            if (selection)
            {
                var slot = (GearLocation)bytes[5];
                if (!GearCatalog.Selectable(slot) || !loot.HasGear(player.Id, slot))
                {
                    Status(player, RaidStatus.Denied, operation);
                    return;
                }
                player.ActiveGear = slot;
                Status(player, RaidStatus.Placed, operation);
                SendGear(player);
                return;
            }

            var from = (GearLocation)bytes[9];
            var to = (GearLocation)bytes[10];
            bool crate = from == GearLocation.Crate || to == GearLocation.Crate;
            if (bytes[13] > 1 || (bytes[11] == 255) != (bytes[12] == 255) ||
                (crate && !SeesContents(player, player.Container)))
            {
                Status(player, RaidStatus.Denied, operation);
                return;
            }
            RaidStatus status = loot.MoveGear(player.Id, player.Search.Identified, BitConverter.ToUInt32(bytes, 5),
                from, to, bytes[11] == 255 ? -1 : bytes[11], bytes[12] == 255 ? -1 : bytes[12], bytes[13] != 0, player.Container);
            Status(player, status, operation);
            if (status != RaidStatus.Placed) return;
            if (player.ActiveGear == from && !loot.HasGear(player.Id, from)) player.ActiveGear = 0;
            if (from == GearLocation.Bag || to == GearLocation.Bag) SendInventory(player, RaidEventKind.Bag);
            if (crate)
                foreach (Player viewer in players.Values)
                    if (SeesContents(viewer, player.Container))
                        SendInventory(viewer, RaidEventKind.Crate);
            // Last snapshot in the ordered transaction; lets the client retire its drag preview.
            SendGear(player);
        }
    }
}
#endif
