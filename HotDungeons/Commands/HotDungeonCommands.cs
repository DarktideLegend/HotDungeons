using ACE.Server.Network.GameMessages.Messages;
using HotDungeons.Dungeons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Commands
{
    internal class HotDungeonCommands
    {
        [CommandHandler("active-rifts", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckRifts(Session session, params string[] paramters)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat($"\n<Active Rift List>", ChatMessageType.System));
            foreach (var rift in RiftManager.ActiveRifts.Values.ToList())
            {
                var at = rift.Coords.Length > 0 ? $"at {rift.Coords}" : "";
                var message = $"Rift {rift.Name} is active {at}";
                session.Network.EnqueueSend(new GameMessageSystemChat($"\n{message}", ChatMessageType.System));
            }
            session.Network.EnqueueSend(new GameMessageSystemChat($"\nTime Remaining: {RiftManager.FormatTimeRemaining()}", ChatMessageType.System));
        }

        [CommandHandler("hot-dungeons", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckDungeons(Session session, params string[] paramters)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat($"\n<Active Dungeon List>", ChatMessageType.System));
            var dungeon = DungeonManager.CurrentHotSpot;
            if (dungeon != null)
            {
                var at = dungeon.Coords.Length > 0 ? $"at {dungeon.Coords}" : "";
                var message = $"Rift {dungeon.Name} is active {at}";
                session.Network.EnqueueSend(new GameMessageSystemChat($"\n{message}", ChatMessageType.System));
                session.Network.EnqueueSend(new GameMessageSystemChat($"\nTime Remaining: {DungeonManager.FormatTimeRemaining()}", ChatMessageType.System));

            }
            else
                session.Network.EnqueueSend(new GameMessageSystemChat($"\nNo Hotspots at this time", ChatMessageType.System));

        }

    }
}
