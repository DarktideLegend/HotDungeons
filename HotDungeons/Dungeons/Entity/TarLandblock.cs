using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons.Entity
{
    internal class TarLandblock
    {
        private object Lock = new Object();

        // must kill 50 mobs to be added to the 
        public uint MobKills { get; private  set; } = 0;

        public readonly uint MaxMobKills = 20;

        public bool Active { get; private set; } = true;

        private  TimeSpan DeactivateInterval { get; set; } = TimeSpan.FromMinutes(5);

        private  DateTime LastDeactivateCheck = DateTime.MinValue;

        public TimeSpan TimeRemaining => (LastDeactivateCheck + DeactivateInterval) - DateTime.UtcNow;

        internal void AddMobKill()
        {
            if (!Active && TimeRemaining.TotalMilliseconds <= 0)
            {
                lock (Lock)
                {
                    Active = true;
                    MobKills = 1;
                    return;
                }

            } 

            if (Active)
            {
                lock (Lock)
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
}
