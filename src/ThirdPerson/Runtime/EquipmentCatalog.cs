using System.Collections.Generic;
using GameData;

namespace PreLuna.ThirdPerson.Runtime
{
    internal static class EquipmentCatalog
    {
        // Called only by the explicit in-level F8 snapshot, after game data initialization.
        // Listing data is not evidence that the corresponding gear was wielded or fired.
        internal static object Capture()
        {
            var archetypes=new List<object>();
            foreach(var a in GameDataBlockBase<ArchetypeDataBlock>.GetAllBlocks())
                archetypes.Add(new{id=a.persistentID,a.name,fireMode=a.FireMode.ToString(),pellets=a.ShotgunBulletCount,burst=a.BurstShotCount,
                    aimSpread=a.AimSpread,hipSpread=a.HipFireSpread,aimTransition=a.AimTransitionTime,runtimeVerified=false});
            var categories=new List<object>();
            foreach(var g in GameDataBlockBase<GearCategoryDataBlock>.GetAllBlocks())
                categories.Add(new{id=g.persistentID,g.name,g.BaseItem,semi=g.SemiArchetype,burst=g.BurstArchetype,auto=g.AutoArchetype,semiBurst=g.SemiBurstArchetype,melee=g.MeleeArchetype,runtimeVerified=false});
            var gear=new List<object>();
            foreach(var g in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
                gear.Add(new{id=g.persistentID,g.name,type=g.Type.ToString(),runtimeVerified=false});
            var items=new List<object>();
            foreach(var i in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
                items.Add(new{id=i.persistentID,i.name,i.inventorySlot,i.FPSSettings,runtimeVerified=false});
            return new{archetypes,categories,gear,items,note="Data enumeration only; support is checked against the actual wielded native class and FPS settings."};
        }
    }
}
