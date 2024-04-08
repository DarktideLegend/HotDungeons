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

        public readonly uint MaxMobKills = 100;

        public bool Active { get; private set; } = true;

        public double TarXpModifier
        {
            get
            {
                double ratio = (double)MobKills / MaxMobKills;
                return Math.Max(0.1, 1.0 - (0.9 * ratio)); // Ensure TarXpModifier is never less than 0.1
            }
        }

        private  TimeSpan DeactivateInterval { get; set; } = TimeSpan.FromHours(3);

        public  DateTime LastDeactivateCheck { get; private set; } = DateTime.MinValue;

        private  TimeSpan RiftActivateInterval { get; set; } = TimeSpan.FromHours(8);
        public  DateTime LastRiftActivateCheck { get; set; } = DateTime.MinValue;

        public TimeSpan TimeRemaining => (LastDeactivateCheck + DeactivateInterval) - DateTime.UtcNow;
        public TimeSpan RiftTimeRemaining => (LastRiftActivateCheck + RiftActivateInterval) - DateTime.UtcNow;

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
