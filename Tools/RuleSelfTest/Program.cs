using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// M11 · 命令行自测入口。
    ///
    /// <code>
    /// dotnet run --project Tools/RuleSelfTest                 # 默认 10000 局
    /// dotnet run --project Tools/RuleSelfTest -- 200 12345    # 局数 / 种子基
    /// dotnet run --project Tools/RuleSelfTest -- 1 1 --trace  # 打一局完整事件流
    /// </code>
    ///
    /// 这是里程碑 M0「内核可自证」的验收工具 —— <b>全程不需要打开 Unity</b>。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // 某些终端不支持设置编码，忽略即可
            }

            int games = 10000;
            int seedBase = 20260916;
            bool trace = false;

            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--trace" || args[i] == "-t")
                {
                    trace = true;
                }
                else
                {
                    positional.Add(args[i]);
                }
            }

            if (positional.Count > 0)
            {
                int.TryParse(positional[0], out games);
            }

            if (positional.Count > 1)
            {
                int.TryParse(positional[1], out seedBase);
            }

            if (games <= 0)
            {
                games = 10000;
            }

            Console.WriteLine("魔法乱斗 1.3 · 规则内核自测（M11）");
            Console.WriteLine("局数 = " + games + "，种子基 = " + seedBase);
            Console.WriteLine(new string('=', 64));

            var report = new List<string>();
            bool ok = true;

            ok &= RuleAssertions.RunDataAssertions(report);
            Prune(report);

            Console.WriteLine();
            if (trace)
            {
                Console.WriteLine("── 首局事件流（seed " + seedBase + "）──");
            }

            var mass = RuleAssertions.RunMassGames(games, seedBase, trace);
            bool massOk = mass.Exceptions == 0 && mass.Violations.Count == 0 && mass.Failures.Count == 0;
            ok &= massOk;

            ok &= RuleAssertions.RunRuleAssertions(report);
            Prune(report);

            // M15：规则改动（免疫光环消耗后不再需要补一张防御牌）单独跑一遍 ——
            // 万局随机对局里 AI 默认不用光环，这条路径在统计里永远不会被走到。
            ok &= AuraScenario.Run(report);
            Prune(report);

            // 2026-09-20：β「防御时力量 +A」（DefPlus）从录入起就没有结算点 ——
            // 这类「引擎自洽、卡面白写」的缺陷同样在万局统计里无声，只能脚本化逼出来。
            ok &= BetaDefenseScenario.Run(report);
            Prune(report);

            // 2026-09-21：雷云 / 狂躁蘑菇的「随机查看对方一张手牌」由引擎自动抽
            // 改成玩家在一排牌背里点一张（ChoosePeekCard）。同样属于「改了接口但
            // 统计里无声」的改动 —— 而且多了一条新风险：选项文案若带上牌名，
            // 效果就从「随机查看」变成「定向查看」。
            ok &= PeekScenario.Run(report);
            Prune(report);

            // 2026-09-22：磁暴 / 充能 / 电弧的「选择送入冷却的手牌」。界面（M27 选牌弹窗）
            // 完全靠引擎给的 MinSelect / MaxSelect 决定「还能不能再放一张」「确认键亮不亮」——
            // 这两个数字错了就只表现为「点第二张毫无反应」，万局统计里全无声。
            // 实测到的真实缺陷：MaxSelect 曾被写成「效果参数 A」而不是「候选牌数」，
            // 于是充能（A = 1）永远只能选 1 张。
            ok &= CoolHandScenario.Run(report);
            Prune(report);

            // 2026-09-22：漩涡改成「从冷却区当中选、强制选择一张」。界面上「强制」只有两个
            // 来源：MinSelect / MaxSelect 与「有没有 Skip 选项」—— 写错了就只表现为
            // 「确认键该亮不亮」「能一张都不选」，万局统计里同样无声。
            ok &= RemoveFromGameScenario.Run(report);
            Prune(report);

            // 2026-09-23：地震（i）从「无条件减速」改成「若这是你的最后两张手牌，减速」。
            // 条件判反 / 判在错的时机 / 归类落到 ① 阶段，在万局统计里全是无声的 ——
            // 表现只有「该减速时不减速」或「不该减速却减速」，只能脚本化把局面逼出来。
            ok &= EarthquakeScenario.Run(report);
            Prune(report);

            // 2026-09-23：瀑流（y）的「同一张卡不能被加速第 2 次」。
            // 旧实现的排除名单**每处理一个效果就清一次**，于是这条规则只在
            // 「每损失 1 点生命」那一串内部生效，第 1 条 α 加速过的牌在第 2 条 α 里
            // 又回到候选表 —— 表现只有「同一张牌被加速了两次」，统计里全无声。
            // 对照组（湍流：「加速 ×2 可落在同一张」）防止改过头。
            ok &= TorrentScenario.Run(report);
            Prune(report);

            // 2026-09-25：雪崩（an）从「可选的单效果」改成双效果
            // （① 强制区域减速：双方冷却区中剩余冷却 = 1 的牌全体 +1，不弹决策；
            //   ② 快速回填：本牌进冷却区时 −1）。
            // 「少弹了一个决策」与「快速回填没结算」在万局统计里都不会变红 ——
            // 前者不影响事件流自洽，后者只是冷却多 1，只能脚本化把局面钉死。
            ok &= AvalancheScenario.Run(report);
            Prune(report);

            // 2026-09-25：模仿（x）的复制条件从「基础冷却 = 3」放宽成「≤ 3」。
            // 卡面文字（CardLibrary）与候选过滤（BattleEngine.IssueCopyTarget）是同一个
            // 事实的两处表达，只改一处就是「卡面写着能复制、界面里一个候选都没有」，
            // 万局统计里同样无声。
            ok &= MimicScenario.Run(report);
            Prune(report);

            Console.WriteLine();
            Console.WriteLine("── 万局统计 ──");
            Console.WriteLine("  局数            : " + mass.Games);
            Console.WriteLine("  玩家(先手) 胜   : " + mass.PlayerWins + "  (" + Percent(mass.PlayerWins, mass.Games) + ")");
            Console.WriteLine("  AI(后手)   胜   : " + mass.AiWins + "  (" + Percent(mass.AiWins, mass.Games) + ")");
            Console.WriteLine("  平局            : " + mass.Draws);
            Console.WriteLine("  异常            : " + mass.Exceptions);
            Console.WriteLine("  平均回合数      : " + (mass.Games > 0 ? (mass.TotalTurns / (double)mass.Games).ToString("F2") : "-"));
            Console.WriteLine("  最长回合数      : " + mass.MaxTurns);
            Console.WriteLine("  事件总数        : " + mass.TotalEvents);
            Console.WriteLine("  光环断言覆盖    : 进攻牌(α) " + mass.AttackAuraChecked
                              + " 张 / 防御牌(β) " + mass.DefenseAuraChecked + " 张");

            if (mass.Failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("⚠ 异常样本：");
                for (int i = 0; i < mass.Failures.Count; i++)
                {
                    Console.WriteLine("  · " + mass.Failures[i]);
                }
            }

            if (mass.Violations.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("⚠ 不变量违规样本：");
                for (int i = 0; i < mass.Violations.Count; i++)
                {
                    Console.WriteLine("  · " + mass.Violations[i]);
                }
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 64));
            Console.WriteLine(ok
                ? "结果：全部通过 ✅   —— 里程碑 M0「内核可自证」达成"
                : "结果：存在失败项 ❌");
            Console.WriteLine("（失败项 ⚠：" + (ok ? "无" : "见上方 FAIL / 违规样本") + "）");

            return ok ? 0 : 1;
        }

        private static void Prune(List<string> report)
        {
            if (report.Count == 0)
            {
                return;
            }

            for (int i = 0; i < report.Count; i++)
            {
                Console.WriteLine(report[i]);
            }

            report.Clear();
        }

        private static string Percent(int part, int total)
        {
            if (total <= 0)
            {
                return "-";
            }

            return (part * 100.0 / total).ToString("F1") + "%";
        }
    }
}
