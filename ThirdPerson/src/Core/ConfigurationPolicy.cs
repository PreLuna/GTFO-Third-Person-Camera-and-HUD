using System;

namespace PreLuna.ThirdPerson.Core
{
    public enum HotkeyAction { None,Reset,Toggle,Snapshot,SwapShoulder,CyclePreset,Performance }
    public static class ConfigurationPolicy
    {
        public static float Number(float value,float fallback,float min,float max)=>float.IsFinite(value)?Math.Clamp(value,min,max):fallback;
        public static HotkeyAction Key(bool reset,bool toggle,bool snapshot,bool swap,bool cycle,bool performance=false)=>reset?HotkeyAction.Reset:toggle?HotkeyAction.Toggle:snapshot?HotkeyAction.Snapshot:performance?HotkeyAction.Performance:swap?HotkeyAction.SwapShoulder:cycle?HotkeyAction.CyclePreset:HotkeyAction.None;
    }
}
