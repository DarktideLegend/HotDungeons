using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons.Entity
{
    internal class TarLandblock
    {
        // must kill 50 mobs to be added to the 
        public uint MobKills { get; private  set; } = 0;

        public readonly uint MaxMobKills = 20;

        public bool Active { get; private set; } = true;

        public double TarXpModifier
        {
            get
            {
                // Calculate TarXpModifier based on the ratio of MobKills to MaxMobKills
                double ratio = (double)MobKills / MaxMobKills;
                return Math.Max(0.1, 1.0 - (0.9 * ratio)); // Ensure TarXpModifier is never less than 0.1
            }
        }

        private  TimeSpan DeactivateInterval { get; set; } = TimeSpan.FromHours(3);

        public  DateTime LastDeactivateCheck { get; private set; } = DateTime.MinValue;

        public DateTime LastRiftCreation = DateTime.MinValue;

        public TimeSpan TimeRemaining => (LastDeactivateCheck + DeactivateInterval) - DateTime.UtcNow;

        internal void AddMobKill()
        {
            if (!Active && TimeRemaining.TotalMilliseconds <= 0)
            {
                Active = true;
                MobKills = 1;
                return;
            } 

            if (Active)
            {
                if (++MobKills >= MaxMobKills)
                {
                    LastDeactivateCheck = DateTime.UtcNow;
                    Active = false;
                }
            }
        }
    }
}
