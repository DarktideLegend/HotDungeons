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

        [CommandHandler("tar", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckTar(Session session, params string[] paramters)
        {
            var player = session.Player;


            var id = player.Location.LandblockId.Raw;
            var lb = $"{id:X8}".Substring(0, 4);


            var tarLandblock = TarManager.GetTarLandblock(lb);

            if (player != null && tarLandblock != null)
            {
                if (!tarLandblock.Active)
                {
                    
                    var message = $"This landblock has been deactivated for xp, it will reset in {TarManager.FormatTimeRemaining(tarLandblock)}";
                    session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
                } else
                {
                    var message = $"The current mob kills on this landblock is {tarLandblock.MobKills}, when this landblock reaches {tarLandblock.MaxMobKills}, it will be deactivated and xp cannot be earned from mob kills.";
                    session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
                }

            } else
            {
                var message = $"This landblock hasn't beent hunted.";
                session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
        }
    }
}
