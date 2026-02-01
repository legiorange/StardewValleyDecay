using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Menus;
using StardewValley.Objects;
using System.Globalization;
using SObject = StardewValley.Object;

namespace Decay
{
    public class ModEntry : Mod
    {
        public const string DataKey = "legiorange.Decay/Progress";

        private static readonly Dictionary<int, int> CategoryDecayDays = new Dictionary<int, int>
        {
            { SObject.VegetableCategory, 4 },
            { SObject.FruitsCategory, 6 },
            { SObject.GreensCategory, 2 },
            { SObject.EggCategory, 4 },
            { SObject.CookingCategory, 3 },
            { SObject.FishCategory, 2 },
            { SObject.flowersCategory, 5 },
    
            // 新增补充
            { SObject.MilkCategory, 3 },       // 牛奶
            { SObject.meatCategory, 2 },       // 肉类 (主要是虫肉或 MOD 物品)
        };

        // 缓存常用的颜色和矩形，减少每帧分配
        private readonly Color _barBackgroundColor = Color.Black * 0.4f;
        private readonly Rectangle _sourceRect = new Rectangle(0, 256, 60, 60);

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Display.RenderedHud += this.OnRenderedHud;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            // 1. 玩家背包处理
            this.ProcessItems(Game1.player.Items, false, Game1.player.currentLocation);

            // 2. 优化位置遍历：直接使用 Values 避开 Pairs 的 KeyValuePair 分配
            foreach (GameLocation loc in Game1.locations)
            {
                foreach (var obj in loc.Objects.Values)
                {
                    if (obj is Chest chest)
                    {
                        // 冰箱判断：Fridge 属性或特定 ID
                        bool isCooling = chest.fridge.Value || chest.QualifiedItemId == "(BigCraftable)216";
                        this.ProcessItems(chest.Items, isCooling, loc);
                    }
                }
            }
        }
        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, MachineData>().Data;
                    // (BC)20 是回收机
                    if (data.TryGetValue("(BC)20", out var machine))
                    {
                        var rottenRule = new MachineOutputRule
                        {
                            Id = "RottenToDeluxeGrow",
                            Triggers = new List<MachineOutputTriggerRule>
                    {
                        new MachineOutputTriggerRule
                        {
                            Id = "ItemIn",
                            RequiredItemId = "(O)747",
                            RequiredCount = 50
                        }
                    },
                            OutputItem = new List<MachineItemOutput>
                    {
                        new MachineItemOutput
                        {
                            ItemId = "(O)466",
                            MinStack = 1,  // 修正：使用 MinStack 代替 Amount
                            MaxStack = 1   // 修正：使用 MaxStack 代替 Amount
                        }
                    },
                            MinutesUntilReady = 100
                        };

                        // 确保 OutputRules 列表不为空
                        if (machine.OutputRules == null)
                            machine.OutputRules = new List<MachineOutputRule>();

                        // 插入到最前面以确保优先匹配规则
                        machine.OutputRules.Insert(0, rottenRule);
                    }
                });
            }
        }
        private void ProcessItems(IList<Item> items, bool isFridge, GameLocation loc)
        {
            if (items == null || items.Count == 0) return;

            string season = Game1.currentSeason;
            bool isOutdoors = loc?.IsOutdoors ?? false;
            bool isCellar = loc?.Name?.Contains("Cellar") ?? false;
            bool isGreenhouse = loc != null && loc.IsGreenhouse;

            for (int i = 0; i < items.Count; i++)
            {
                // 使用模式匹配简化代码并提高效率
                if (items[i] is SObject { bigCraftable: { Value: false } } obj &&
                    CategoryDecayDays.TryGetValue(obj.Category, out int days))
                {
                    float mult = 1.0f;
                    if (isFridge) mult *= 0.5f;

                    // 逻辑优化：将固定的分类判断移出复杂条件
                    if (obj.Category == SObject.FishCategory && !isFridge) mult *= 1.5f;

                    if (!isGreenhouse)
                    {
                        if (season == "summer")
                        {
                            if (obj.Category == SObject.FishCategory || obj.Category == SObject.CookingCategory) days = 1;
                            else mult *= 1.5f;
                        }
                        else if (season == "winter" && isOutdoors && !Game1.isSnowing) mult *= 0.25f;
                    }

                    if (isCellar) mult *= 0.9f;

                    // 字符串包含检查略慢，但在 DayStarted 中执行频率低，可接受
                    if (obj.Name.Contains("Smoked Fish")) { days = 15; mult = isFridge ? 0.13f : 1.0f; }
                    // 特殊处理：干货保质期延长

                    
                    // 使用 InvariantCulture 避免不同语言系统下的解析错误（如逗号小数点问题）
                    float p = 0;
                    if (obj.modData.TryGetValue(DataKey, out string val))
                        float.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out p);

                    p += (100f / days) * mult;

                    if (p >= 100)
                    {
                        // 替换为变质物品，保持堆叠数量
                        items[i] = ItemRegistry.Create("(O)747", obj.Stack);
                    }
                    else
                    {
                        // 限制精度存储，节省 ModData 空间
                        obj.modData[DataKey] = Math.Clamp(p, 0f, 100f).ToString("0.0", CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            // 增加性能短路：如果菜单打开或不在游玩状态，直接返回
            if (!Context.IsWorldReady || Game1.eventUp || Game1.activeClickableMenu != null) return;

            // 获取当前选中的物品（缓存引用）
            var activeObj = Game1.player.ActiveObject;
            if (activeObj == null || !CategoryDecayDays.ContainsKey(activeObj.Category)) return;

            float p = 0;
            if (activeObj.modData.TryGetValue(DataKey, out string v))
                float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out p);

            // UI 绘制优化
            int bW = 200;
            int bH = 20;
            int pX = 28;
            int pY = Game1.uiViewport.Height - 120;

            // 预先检查鼠标位置，避免复杂的包含计算
            bool isHovering = new Rectangle(pX, pY, bW, bH).Contains(Game1.getOldMouseX(), Game1.getOldMouseY());

            // 绘制背景框
            IClickableMenu.drawTextureBox(e.SpriteBatch, Game1.menuTexture, _sourceRect, pX - 16, pY - 45, bW + 32, bH + 60, Color.White, 1f, false);

            // 绘制进度条背景
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(pX, pY, bW, bH), _barBackgroundColor);

            // 绘制进度条填充
            Color barColor = Color.Lerp(Color.LimeGreen, Color.Red, p / 100f);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(pX, pY, (int)(bW * (p / 100f)), bH), barColor);
            int freshnessPercent = 100 - (int)p;
            string text;

            if (isHovering) {
                text = this.Helper.Translation.Get("bar.freshness", new { percent = freshnessPercent });
            }
            else
            {
                text = activeObj.DisplayName;
            }

            //string text = isHovering ? $"{100 - (int)p}% 新鲜度" : activeObj.DisplayName;

            Utility.drawTextWithShadow(e.SpriteBatch, text, Game1.smallFont, new Vector2(pX, pY - 36), Game1.textColor);
        }
    }
}