using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AntiAddiction
{
    [StaticConstructorOnStartup]
    public class Main
    {
        static Main()
        {
            Harmony harmony = new Harmony(ModConstant.ModId);
            harmony.PatchAll();
        }
    }

    public class AntiAddictionComp : GameComponent
    {
        private long _nextNotificationMillisecond = 0;
        private long _readNow;
        private int _sleepStartHour = SleepTimeMod.SleepStartHour;

        private static readonly Action SaveGameAction = () =>
        {
            if (Current.ProgramState == ProgramState.Playing)
            {
                //打开保存界面
                Find.WindowStack.Add(new Dialog_SaveFileList_Save());
            }
        };

        public int GetSleepStartHour()
        {
            return _sleepStartHour;
        }

        public AntiAddictionComp(Game game)
        {
        }

        // 存档序列化
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _sleepStartHour, ModConstant.ModSettingKeySleepStartHour, SleepTimeMod.SleepStartHour, true);
            Scribe_Values.Look(ref _readNow, ModConstant.ModSettingKeyRealNow,  DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
        }

        public bool CheckTime()
        {
            int realHour = DateTime.Now.Hour;
            var sleepStartHour = _sleepStartHour;
            var sleepEndHour = sleepStartHour + 8; // 睡眠时间为8小时
            if (realHour >= sleepStartHour && realHour < sleepEndHour)
            {
                return true;
            }

            return false;
        }

        public override void GameComponentTick()
        {
            // 防止回溯
            var nowMillisecond = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_readNow > nowMillisecond)
            {
                var taggedString = I18Constant.GetTimeError.Translate(
                    DateTimeOffset.FromUnixTimeMilliseconds(nowMillisecond).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    DateTimeOffset.FromUnixTimeMilliseconds(_readNow).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                );
                Log.Error(taggedString);
                Find.WindowStack.Add(new Dialog_MessageBox(taggedString));
            }else
            {
                _readNow = nowMillisecond;
            }
            if (CheckTime() && nowMillisecond >= _nextNotificationMillisecond)
            {
                //获取当前显示时间的时间戳
                Find.TickManager.prePauseTimeSpeed = TimeSpeed.Normal;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                Find.TickManager.Pause(); // 暂停游戏
                Find.WindowStack.Add(new Dialog_MessageBox(I18Constant.TimeToSleep.Translate(), null, SaveGameAction));
                UpdateNextNotificationMillisecond();
            }
        }

        private void UpdateNextNotificationMillisecond()
        {
            //一分钟后再次提示
            long currentMillisecond = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _nextNotificationMillisecond = currentMillisecond + 60000; // 60000 毫秒 = 1 分钟
        }
    }

    [HarmonyPatch(typeof(TickManager), "CurTimeSpeed", MethodType.Setter)]
    public static class TickManagerCurTimeSpeedPatch
    {
        static bool Prefix(ref TimeSpeed value)
        {
            var antiAddictionComp = Current.Game.GetComponent<AntiAddictionComp>();
            if (antiAddictionComp.CheckTime() && value != TimeSpeed.Normal)
            {
                Find.TickManager.prePauseTimeSpeed = TimeSpeed.Normal;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                Find.TickManager.Pause(); // 暂停游戏
                Messages.Message(I18Constant.NotAllowedToPlay.Translate(), MessageTypeDefOf.CautionInput);
                return false; // 阻止原始 setter 执行
            }

            return true;
        }
    }

    public class SleepTimeMod : Mod
    {
        public static int SleepStartHour;

        public SleepTimeMod(ModContentPack content) : base(content)
        {
            GetSettings<SleepTimeSettings>(); // 自动加载存档数据
        }

        // 显示设置界面（含下拉框）
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            if (Current.ProgramState != ProgramState.Playing)
            {
                // 1. 睡眠起始时间下拉框
                listing.Label(I18Constant.SleepTimeStartSetting.Translate());
                if (listing.ButtonTextLabeled("", SleepStartHour.ToString("00") + ":00"))
                {
                    // 弹出时间选择菜单
                    FloatMenuUtility.MakeMenu(
                        GetHourOptions(),
                        hour => hour.ToString("00") + ":00",
                        hour => () => { SleepStartHour = hour; }
                    );
                }
            }
            else
            {
                var antiAddictionComp = Current.Game.GetComponent<AntiAddictionComp>();
                // 已存档，显示为灰色不可编辑状态
                listing.Label(I18Constant.SettingModifyNotAllowed.Translate()+": " + antiAddictionComp.GetSleepStartHour().ToString("00") + ":00"
                    , tooltip: I18Constant.SettingModifyNotAllowedTooltip.Translate());
            }

            listing.End();
        }

        // 生成选项
        private static int[] GetHourOptions()
        {
            // 生成从 0 到 23 的小时数组
            int[] hours = new int[24];
            for (int i = 0; i < 24; i++)
            {
                hours[i] = i;
            }

            return hours;
        }

        // Mod 设置名称
        public override string SettingsCategory() => I18Constant.SleepTimeSetting.Translate();
    }

    // 数据存储
    public class SleepTimeSettings : ModSettings
    {
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref SleepTimeMod.SleepStartHour, ModConstant.ModSettingKeySleepStartHour, 23);
        }
    }
}