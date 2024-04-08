using ACE.Entity;
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

        internal static void ProcessCreaturesDeath(string currentLb, Landblock landblock, Player killer, out double returnValue)
        {
            returnValue = 1.0; // Default value in case no calculation is performed

            if (RiftManager.HasActiveRift(currentLb))
                return;


            if (TarLandblocks.TryGetValue(currentLb, out TarLandblock tarLandblock))
            {
                tarLandblock.AddMobKill();
                returnValue = tarLandblock.MobKills; // Assigning the number of mob kills as the return value
            }
            else
            {
                tarLandblock = new TarLandblock();
                TarLandblocks.Add(currentLb, tarLandblock);
                tarLandblock.AddMobKill();
                returnValue = tarLandblock.MobKills; // Assigning the number of mob kills as the return value
            }

            returnValue = tarLandblock.TarXpModifier;

            if (!tarLandblock.Active && DungeonRepository.Landblocks.TryGetValue(currentLb, out DungeonLandblock dungeon))
            {

                if (tarLandblock.RiftTimeRemaining.TotalMilliseconds > 0)
                    return;

                if (RiftManager.TryAddRift(currentLb, killer, dungeon, out Rift rift))
                {
                    var objects = landblock.GetAllWorldObjectsForDiagnostics();
                    var players = objects.Where(wo => wo is Player).ToList();

                    foreach (var player in players)
                    {
                        if (player != null)
                        {
                            ModManager.Log(player.Location.ToString());
                            var newPosition = new Position(player.GetPosition(ACE.Entity.Enum.Properties.PositionType.DungeonSurface)).InFrontOf(-10.0f); ;
                            WorldManager.ThreadSafeTeleport(player as Player, newPosition, false);
                        }
                    }

                    tarLandblock.LastRiftActivateCheck = DateTime.UtcNow;
                }
            }
        }

        public static string FormatTimeRemaining(TimeSpan timeRemaining)
        {
            if (timeRemaining.TotalSeconds < 1)
            {
                return "less than a second";
            }
            else if (timeRemaining.TotalMinutes < 1)
            {
                return $"{timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
            }
            else if (timeRemaining.TotalHours < 1)
            {
                return $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} and {timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
            }
            else
            {
                return $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")}, {timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")}, and {timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
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
