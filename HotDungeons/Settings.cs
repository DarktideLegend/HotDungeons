namespace HotDungeons
{
    public class Settings
    {
        public bool EnableHotDungeons { get; set; } = false;

        // how often to elect a new bonus dungeon (if there is one to elect)
        public uint DungeonCheckInterval { get; set; } = 60;

        public uint RiftCheckInterval { get; set; } = 60;

        public float RiftMaxBonusXp { get; set; } = 4.0f;

        // The max xp modifier amount for an elected dungeon
        public float MaxBonusXp { get; set; } = 4.0f;
        // Your settings here
    }
}