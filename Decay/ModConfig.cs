
namespace Decay
{
    public class ModConfig
    {
        public bool IsEnableDecay { get; set; } = true;
        public float DecayMultiplier { get; set; } = 1.0f;

        public int VegetableDays { get; set; } = 4;
        public int FruitDays { get; set; } = 6;
        public int GreensDays { get; set; } = 2;
        public int EggDays { get; set; } = 4;
        public int CookingDays { get; set; } = 3;
        public int FishDays { get; set; } = 2;
        public int FlowerDays { get; set; } = 5;
        public int MilkDays { get; set; } = 3;
        public int MeatDays { get; set; } = 2;
    }
}
