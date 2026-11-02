using System;
using MagicBrawl.Core;

internal static class Program
{
    private static int _seq;

    private static T Ev<T>(T e, int turn) where T : BattleEvent
    {
        e.Seq = ++_seq;
        e.TurnNumber = turn;
        return e;
    }

    private static void Main()
    {
        var builder = new BattleRecordBuilder(0);
        CardInstance snow = new CardInstance(CardLibrary.Get("a"), 0);      // 暴风雪
        CardInstance cloud = new CardInstance(CardLibrary.Get("aq"), 0);    // 雷云
        CardInstance iceStorm = new CardInstance(CardLibrary.Get("b"), 1);  // 冰风暴
        CardInstance freeze = new CardInstance(CardLibrary.Get("c"), 1);    // 凝固
        CardInstance shield = new CardInstance(CardLibrary.Get("al"), 1);   // 石盾

        builder.Observe(Ev(new RoundBeganEvent(), 1));
        builder.Observe(Ev(new TurnStartedEvent { Seat = 0 }, 1));
        builder.Observe(Ev(new AttackDeclaredEvent { Seat = 0, Card = snow }, 1));
        builder.Observe(Ev(new CooldownChangedEvent
        {
            Change = new CooldownChange { Card = cloud, From = 3, To = 2, Reason = "加速 −1" }
        }, 1));
        builder.Observe(Ev(new CooldownChangedEvent
        {
            Change = new CooldownChange { Card = shield, From = 3, To = 4, Reason = "区域减速" }
        }, 1));
        builder.Observe(Ev(new DefenseResolvedEvent
        {
            DefenderSeat = 1, Success = true, Cards = new System.Collections.Generic.List<CardInstance> { iceStorm }
        }, 1));

        builder.Observe(Ev(new TurnStartedEvent { Seat = 1 }, 1));
        builder.Observe(Ev(new AttackDeclaredEvent { Seat = 1, Card = freeze }, 1));
        builder.Observe(Ev(new CooldownChangedEvent
        {
            Change = new CooldownChange { Card = cloud, From = 2, To = 3, Reason = "减速 +1" }
        }, 1));
        builder.Observe(Ev(new DefenseResolvedEvent
        {
            DefenderSeat = 0, Success = true, Cards = new System.Collections.Generic.List<CardInstance> { cloud }
        }, 1));

        // 第 2 回合：多段 + 未防御
        builder.Observe(Ev(new RoundBeganEvent(), 2));
        builder.Observe(Ev(new TurnStartedEvent { Seat = 0 }, 2));
        builder.Observe(Ev(new AttackDeclaredEvent { Seat = 0, Card = soil = new CardInstance(CardLibrary.Get("t"), 0) }, 2));
        builder.Observe(Ev(new DefenseResolvedEvent
        {
            DefenderSeat = 1, Success = true, Cards = new System.Collections.Generic.List<CardInstance> { shield }
        }, 2));
        builder.Observe(Ev(new TurnStartedEvent { Seat = 0, IsComboFollowUp = true }, 2));
        builder.Observe(Ev(new AttackDeclaredEvent { Seat = 0, Card = soil }, 2));
        builder.Observe(Ev(new DefenseResolvedEvent { DefenderSeat = 1, Success = false, GaveUp = true }, 2));

        builder.Observe(Ev(new TurnStartedEvent { Seat = 1 }, 2));
        builder.Observe(Ev(new AttackDeclaredEvent { Seat = 1, Card = freeze }, 2));
        builder.Observe(Ev(new DamageTakenEvent { Seat = 1, Amount = 1, Source = "未出牌，进攻失败" }, 2));

        builder.Observe(Ev(new GameOverEvent { WinnerSeat = 0, Reason = "对方生命归零" }, 2));

        BattleRecord record = builder.Build();
        Console.WriteLine(BattleRecordText.ToPlainText(record));
        Console.WriteLine("--- 图例：" + BattleRecordText.Legend(false));
        Console.WriteLine("--- 第 2 回合 A 段数 = " + record.Rows[1].First.Hits.Count
                          + "，B 半场 NoPlay = " + record.Rows[1].Second.NoPlay);
        Console.WriteLine("--- 第 1 回合 A 效果 = " + BattleRecordText.Notes(record.Rows[0].First));
    }

    private static CardInstance soil;
}
