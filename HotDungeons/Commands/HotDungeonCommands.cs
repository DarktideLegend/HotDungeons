using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics.Combat;
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
            HandleCheckRiftsNew(session, paramters);
        }

        [CommandHandler("rifts", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckRiftsNew(Session session, params string[] paramters)
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

        [CommandHandler("tar", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckTar(Session session, params string[] paramters)
        {
            var player = session.Player;


            var id = player.Location.LandblockId.Raw;
            var lb = $"{id:X8}".Substring(0, 4);

            if (RiftManager.HasActiveRift(lb))
            {
                var message = $"This landblock is a rift, it does not have tar";
                session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
                return;
            }

            var tarLandblock = TarManager.GetTarLandblock(lb);

            if (player != null && tarLandblock != null)
            {
                if (tarLandblock.Active)
                {
                    var message = $"The current tar xp modifier for this landblock is {tarLandblock.TarXpModifier}, when this landblock reaches 0.1 and is inside a dungeon, it may be eligible to be upgraded to a Rift!";
                    session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
                } else
                {
                    var message = $"The current tar xp modifier for this landblock is {tarLandblock.TarXpModifier}, it will be resetting in {TarManager.FormatTimeRemaining(tarLandblock)}";
                    session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
                }

            } else 
            {
                var message = $"This landblock has not been hunted yet!";
                session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
        }
    }
}
