namespace HotDungeons
{
    public class Settings
    {
        // how often to elect a new bonus dungeon (if there is one to elect)
        public uint DungeonCheckInterval { get; set; } = 60;

        // The max xp modifier amount for an elected dungeon
        public float MaxBonusXp { get; set; } = 4.0f;
        // Your settings here
    }
}