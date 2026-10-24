using System;
using System.Collections.Generic;

namespace HHCL.Core
{
    /// <summary>
    /// 中文搜索增强：Modrinth / CurseForge 上绝大多数项目标题是英文，
    /// 直接用中文搜几乎搜不到（实测"小地图"只有 2 条，而 Xaero 小地图是最热门模组之一）。
    /// 这里维护一份"中文关键词 → 英文关键词"的映射，
    /// 中文搜索结果太少时，自动用英文同义词补搜一批并合并去重。
    /// </summary>
    public static class SearchTerms
    {
        /// <summary>中文关键词 → 英文搜索词（按优先级排列，逗号分隔可给多个）。</summary>
        private static readonly (string Zh, string En)[] Map =
        {
            // 光影 / 材质
            ("光影", "shader"), ("光影包", "shader"), ("光影效果", "shader"), ("光影材质", "shader"),
            ("材质", "texture pack"), ("材质包", "resource pack"), ("纹理", "texture"), ("贴图", "texture"),
            ("皮肤", "skin"), ("披风", "cape"),
            // 地图 / 传送
            ("地图", "map"), ("小地图", "minimap"), ("大地图", "world map"), ("全图", "fullscreen map"),
            ("传送门", "portal"), ("传送", "teleport"), ("随机传送", "random teleport"),
            // 优化 / 性能
            ("优化", "optimization"), ("性能", "performance"), ("卡顿", "lag fix"), ("掉帧", "fps"),
            ("帧率", "fps"), ("内存", "memory optimization"), ("光影优化", "shader optimization"),
            ("渲染", "renderer"), ("光线追踪", "ray tracing"), ("体积光", "volumetric"),
            ("动画库", "animation library"), ("实体光", "entity lighting"), ("钠", "sodium"),
            // 界面
            ("界面", "gui"), ("物品栏", "inventory"), ("HUD", "hud"), ("状态栏", "status bar"),
            ("字体", "font"), ("图标", "icon"), ("提示", "tooltip"), ("搜索", "search"),
            // 玩法
            ("地图下载", "map downloader"), ("世界", "world"), ("生成", "generator"), ("群系", "biome"),
            ("生物", "mob"), ("怪物", "mob"), ("Boss", "boss"), ("武器", "weapon"), ("装备", "gear"),
            ("盔甲", "armor"), ("矿物", "ore"), ("附魔", "enchantment"), ("配方", "recipe"),
            ("合成", "crafting"), ("装饰", "decoration"), ("家具", "furniture"), ("方块", "block"),
            ("建筑", "building"), ("结构", "structure"), ("村庄", "village"), ("任务", "quest"),
            ("剧情", "story"), ("冒险", "adventure"), ("地牢", "dungeon"), ("Boss战", "boss battle"),
            ("载具", "vehicle"), ("飞船", "spaceship"), ("机械", "create"), ("铁路", "railway"),
            ("红石", "redstone"), ("刷怪", "mob spawner"), ("经验", "experience"), ("等级", "level"),
            // 联机 / 玩法相关
            ("联机", "multiplayer"), ("好友", "friends"), ("公会", "guild"), ("小队", "party"),
            ("小游戏", "minigame"), ("地图编辑", "world editor"), ("截图", "screenshot"),
            ("模组管理", "mod manager"), ("崩溃", "crash fix"), ("光影冲突", "shader fix"),
            // 依赖/工具
            ("前置", "library"), ("依赖", "library"), ("api", "api"), ("库", "library"),
            ("fabric", "fabric"), ("forge", "forge"), ("neoforge", "neoforge"), ("版本", "version"),
        };

        /// <summary>查询里是否含中文字符。</summary>
        public static bool HasChinese(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (c >= '\u4e00' && c <= '\u9fff') return true;
            return false;
        }

        /// <summary>
        /// 中文查询 → 英文补充搜索词（去重、限 3 个）。
        /// 例如"小地图"→ ["minimap", "world map"]；查不到映射时返回空列表。
        /// </summary>
        public static List<string> Expand(string query)
        {
            var result = new List<string>();
            if (!HasChinese(query)) return result;
            var q = (query ?? "").Trim();

            // 整句命中（例如"小地图"）
            foreach (var (zh, en) in Map)
            {
                if (q == zh) AddRange(result, en);
            }
            // 逐个中文词命中（查询更长时，例如"小地图 材质"）
            foreach (var (zh, en) in Map)
            {
                if (q.Contains(zh) && q != zh) AddRange(result, en);
            }
            return result;
        }

        private static void AddRange(List<string> list, string en)
        {
            foreach (var one in en.Split(','))
            {
                var t = one.Trim();
                if (t.Length == 0) continue;
                if (list.Contains(t)) continue;
                list.Add(t);
                if (list.Count >= 3) return;
            }
        }
    }
}