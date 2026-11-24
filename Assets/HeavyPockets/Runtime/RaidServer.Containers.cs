#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidServer
    {
        private bool SeesContents(Player player, byte container) => player.Viewing && player.Searching &&
            !player.Exited && player.Container == container && RaidMap.CanOpen(player.Position,player.Motion,container);

        private void OpenContainer(Player player, byte id)
        {
            if (!RaidMap.CanOpen(player.Position,player.Motion,id))
            {
                Status(player, RaidMap.WithinReach(player.Position,player.Motion.Height,id) ? RaidStatus.Blocked : RaidStatus.OutOfRange);
                return;
            }
            player.Container = id;
            player.Viewing = true;
            player.Searching = false;
            double now = WorldMechanisms.Now;
            loot.Unlatch(id,now);
            SendVisibleLids(player);
            Status(player,RaidStatus.Opened);
            UpdateContainerSearch(player,0f,false);
        }

        // Lids are public physical state. A passing observer receives one timestamp per
        // open case in range and no information about its owner, contents or search progress.
        private void SendVisibleLids(Player observer)
        {
            for (byte id=0; id<RaidContainers.Count; id++)
            {
                int bit = 1 << id;
                if ((observer.RevealedLids & bit) != 0 || !loot.HasOpened(id,out double start)) continue;
                var center = RaidContainers.BoundsFor(id).center;
                if ((observer.Position - new Vector2(center.x,center.z)).sqrMagnitude > 32f*32f) continue;
                observer.RevealedLids |= (byte)bit;
                Send(observer.Id,new RaidEvent { Kind = RaidEventKind.ContainerLid, ContainerId = id, StartedAt = start });
            }
        }

        private void UpdateContainerSearch(Player player,float delta,bool snapshot)
        {
            if (snapshot) SendVisibleLids(player);
            if (!player.Viewing) return;
            byte id = player.Container;
            if (!player.Searching)
            {
                if (!loot.HasOpened(id,out double start) || WorldMechanisms.Now - start < RaidContainers.LidSeconds) return;
                if (!loot.Generated(id))
                {
                    loot.GenerateCrate(id);
                    if (id == 0) Debug.Log("HEAVY POCKETS CRATE GENERATED after authorized open");
                }
                player.Searching = true;
                SendInventory(player,RaidEventKind.Crate);
            }
            else if (player.Search.Advance(loot,delta,id)) SendInventory(player,RaidEventKind.Crate);
        }
    }
}
#endif
