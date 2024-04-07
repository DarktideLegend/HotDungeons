using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using HotDungeons.Dungeons.Entity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons
{


    internal class TarManager
    {

        private static readonly object lockObject = new object();

        private static Dictionary<string, TarLandblock> TarLandblocks = new Dictionary<string, TarLandblock>();

        internal static void ProcessCreaturesDeath(string currentLb, Landblock landblock, Player killer)
        {
            if (RiftManager.HasActiveRift(currentLb))
                return;


            if (TarLandblocks.TryGetValue(currentLb, out TarLandblock tarLandblock)) 
            {
                tarLandblock.AddMobKill();
            } else
            {
                tarLandblock = new TarLandblock();
                TarLandblocks.Add(currentLb, tarLandblock);
                tarLandblock.AddMobKill();
            }

            if (!tarLandblock.Active && DungeonRepository.Landblocks.TryGetValue(currentLb, out DungeonLandblock dungeon))
            {
                if(RiftManager.TryAddRift(currentLb, killer, dungeon, out Rift rift))
                {
                    WorldManager.ThreadSafeTeleport(killer, rift.DropPosition, false);
                }
            }


        }

        public static string FormatTimeRemaining(TarLandblock tarlandblock)
        {
            if (tarlandblock.TimeRemaining.TotalSeconds < 1)
            {
                return "less than a second";
            }
            else if (tarlandblock.TimeRemaining.TotalMinutes < 1)
            {
                return $"{tarlandblock.TimeRemaining.Seconds} seconds";
            }
            else
            {
                return $"{tarlandblock.TimeRemaining.Minutes} minutes and {tarlandblock.TimeRemaining.Seconds} seconds";
            }
        }

        internal static TarLandblock? GetTarLandblock(string lb)
        {
            if (TarLandblocks.TryGetValue(lb, out TarLandblock tarLandblock))
            {
                return tarLandblock;
            }
            else
                return null;
        }
    }

}
