using System;
using System.Numerics;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public enum HudPromptKind { None, Life, Health, Infection, Hit, Magazine, Reserve, Tool, Pack, Consumable, Heavy, Action, Team }
    public readonly record struct HudPrompt(HudPromptKind Kind, string Text, int Priority, float? Progress=null);
    public readonly record struct HudPromptSet(HudPrompt First, HudPrompt Second, HudPrompt Third, int Count)
    {
        public HudPrompt this[int index] => index switch{0=>First,1=>Second,2=>Third,_=>default};
    }
    // Fixed scratch storage, rebuilt from current validated conditions. Nothing waits behind another prompt.
    public sealed class HudAlerts
    {
        readonly HudPrompt[] candidates=new HudPrompt[12];
        int count;
        public void Reset(){Array.Clear(candidates,0,candidates.Length);count=0;}
        void Add(HudPromptKind kind,string text,int priority,float? progress=null)
        {
            if(count>=candidates.Length)throw new InvalidOperationException("Prompt capacity exceeded.");
            candidates[count++]=new(kind,text,priority,progress);
        }
        readonly struct Reader
        {
            readonly HudRuntimeContext current;readonly HudContentSnapshot snapshot;readonly HudContentPolicy policy;readonly long epoch,revision;
            public ObjectIdentity Player=>current.ExpectedBinding.LocalPlayer.Agent;
            public Reader(in HudRuntimeContext c,in HudContentSnapshot s,long e,long r,in HudContentPolicy p)
            {current=c;snapshot=s;epoch=e;revision=r;policy=p;}
            public bool Read<T>(in HudValue<T> field,ObjectIdentity source,HudFreshness age,out T value) where T:struct,IHudContentValue
                =>HudContentRules.TryRead(current,snapshot.Header,field,source,age,epoch,revision,policy,out value,out _);
            public bool Number(in HudValue<HudQuantity> field,ObjectIdentity source,HudUnit unit,out double value)
            {bool ok=HudContentRules.TryReadQuantity(current,snapshot.Header,field,source,unit,HudFreshness.Live,epoch,revision,policy,out var q,out _);value=q.Amount;return ok;}
            public bool Gear(in HudEquipmentSnapshot gear,HudEquipmentSlot slot,out ObjectIdentity identity)
            {
                identity=default;
                if(gear.Slot!=slot||!Read(gear.Identity,gear.Identity.Stamp.Source,HudFreshness.Identity,out var id)
                    ||id.Slot!=slot||id.Instance!=gear.Identity.Stamp.Source)return false;
                identity=id.Instance;return true;
            }
        }
        public HudPanelView Apply(in HudRuntimeContext current,in ThirdPersonDisplayFrame display,bool hasContent,in HudContentSnapshot snapshot,
            long epoch,long revision,in HudContentPolicy policy,in HudPanelView view,in HudAlertOptions raw,in HudPanelBehavior behavior)
        {
            Reset();var o=raw.Sanitized();var p=view.Personal;
            if(!o.Enabled||!p.Active||!p.Options.Enabled||!hasContent||!HudEligibility.EvaluateCurrent(current).Allowed
                ||!HudContentRules.EvaluateData(current,snapshot.Header,epoch,revision,policy).Allowed)
                return view with{Prompts=default};
            var r=new Reader(current,snapshot,epoch,revision,policy);var s=snapshot.Personal;var b=behavior.Sanitized();
            var teamPrompt=view.Team.Prompt;
            if(teamPrompt.Priority>0)Add(teamPrompt.Kind,teamPrompt.Text,teamPrompt.Priority);
            bool alive=r.Read(s.Life,r.Player,HudFreshness.Live,out var life)&&life.Kind==HudLifeKind.Alive;
            if(o.StateFeedback)
            {
                if(life.Kind is HudLifeKind.Downed or HudLifeKind.Dead)Add(HudPromptKind.Life,life.Kind==HudLifeKind.Downed?"已倒地":"已死亡",100);
                if(p.Options.Health&&p.Health.Available&&p.Health.Amount<=b.LowHealth)Add(HudPromptKind.Health,"生命危急",95);
                else if(p.Options.Health&&!string.IsNullOrEmpty(p.HealthChange))Add(HudPromptKind.Health,"生命 "+p.HealthChange,p.HealthChange.StartsWith("−",StringComparison.Ordinal)?80:30);
                if(p.Options.Infection&&p.Infection.Available&&p.Infection.Amount>=b.HighInfection)Add(HudPromptKind.Infection,"感染过高",88);
                else if(p.Options.Infection&&!string.IsNullOrEmpty(p.InfectionChange))Add(HudPromptKind.Infection,"感染 "+p.InfectionChange,70);
            }
            if(alive&&o.HitFeedback&&r.Read(s.Hit,r.Player,HudFreshness.Live,out var hit)
                &&hit.OccurredAtSeconds<=current.NowSeconds&&current.NowSeconds-hit.OccurredAtSeconds<o.HitSeconds)
            {
                string direction="";
                // Directions require this committed display's camera; data eligibility alone never grants one.
                if(HudEligibility.EvaluateDisplay(current,display,HudDisplayUsage.ScreenComposition,HudValidityPolicy.Default).Allowed)
                    direction=Direction(hit.TowardSource.ToVector(),display.Camera.WorldToView);
                Add(HudPromptKind.Hit,"受击"+(direction.Length==0?"":" · "+direction),90);
            }
            if(alive&&o.ResourceWarnings)
            {
                if(p.Options.Weapons){Weapon(r,s.Primary,HudEquipmentSlot.Primary,"主武器",o);Weapon(r,s.Secondary,HudEquipmentSlot.Secondary,"副武器",o);}
                if(p.Options.Tools&&r.Gear(s.Tool,HudEquipmentSlot.Tool,out var tool)&&r.Number(s.Tool.SupplyRatio,tool,HudUnit.Fraction,out double amount)&&amount<=o.LowToolRatio)
                    Add(HudPromptKind.Tool,amount==0?"工具资源耗尽":"工具资源不足",50);
                if(p.Options.Items){Item(r,s.ResourcePack,HudEquipmentSlot.ResourcePack,"补给包",HudPromptKind.Pack);Item(r,s.Consumable,HudEquipmentSlot.Consumable,"消耗品",HudPromptKind.Consumable);Item(r,s.Heavy,HudEquipmentSlot.Heavy,"重型物品",HudPromptKind.Heavy);}
            }
            if(alive&&o.Actions&&r.Read(s.Selected,r.Player,HudFreshness.Live,out var selected)
                &&ModuleEnabled(selected.Slot,p.Options)&&r.Read(s.Action.Action,r.Player,HudFreshness.Action,out var action))
            {
                var gear=Selected(s,selected.Slot);
                bool matches=selected.Slot==HudEquipmentSlot.EmptyHands?s.Action.Equipment==default
                    :r.Gear(gear,selected.Slot,out var item)&&s.Action.Equipment==item;
                if(matches)
                {
                    string text=action.Kind switch{HudActionKind.Reloading=>"换弹中",HudActionKind.MeleeCharging=>"近战蓄力中",HudActionKind.UsingItem=>"物品使用中",HudActionKind.Deploying=>"部署中",HudActionKind.Carrying=>"搬运中",_=>""};
                    float? progress=null;
                    if(text.Length>0&&r.Read(s.Action.Progress,r.Player,HudFreshness.Action,out var q)&&q.Unit==HudUnit.Fraction&&q.Amount<=1)progress=(float)q.Amount;
                    if(text.Length>0)Add(HudPromptKind.Action,text,60,progress);
                }
            }
            // Stable selection by severity then insertion order. A suppressed transient expires at its original time.
            HudPrompt Take()
            {
                int best=-1;for(int i=0;i<count;i++)if(candidates[i].Priority>0&&(best<0||candidates[i].Priority>candidates[best].Priority))best=i;
                if(best<0)return default;var result=candidates[best];candidates[best]=default;return result;
            }
            int visible=Math.Min(count,o.MaximumVisible);
            var prompts=new HudPromptSet(visible>0?Take():default,visible>1?Take():default,visible>2?Take():default,visible);
            return view with{Prompts=prompts};
        }
        void Weapon(in Reader r,in HudEquipmentSnapshot gear,HudEquipmentSlot slot,string label,in HudAlertOptions o)
        {
            if(!r.Gear(gear,slot,out var id))return;
            bool empty=r.Number(gear.Magazine,id,HudUnit.Rounds,out double clip)&&clip==0;
            bool low=r.Number(gear.ReserveRounds,id,HudUnit.Rounds,out double reserve)&&reserve<=o.LowReserveRounds;
            // One combined prompt per weapon, so an empty magazine doesn't bury its reserve condition.
            if(empty)Add(HudPromptKind.Magazine,label+"弹匣空"+(low?(reserve==0?" · 备用耗尽":" · 备用不足"):""),85);
            else if(low)Add(HudPromptKind.Reserve,label+(reserve==0?"备用耗尽":"备用不足"),55);
        }
        void Item(in Reader r,in HudEquipmentSnapshot gear,HudEquipmentSlot slot,string label,HudPromptKind kind)
        {
            if(!r.Gear(gear,slot,out var id))return;
            var field=slot==HudEquipmentSlot.ResourcePack?gear.Uses:gear.ItemCount;
            if(r.Number(field,id,slot==HudEquipmentSlot.ResourcePack?HudUnit.Uses:HudUnit.Items,out double count)&&count==0)Add(kind,label+"耗尽",40);
        }
        static bool ModuleEnabled(HudEquipmentSlot slot,in HudPersonalOptions o)=>slot switch{
            HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary or HudEquipmentSlot.Melee=>o.Weapons,
            HudEquipmentSlot.Tool=>o.Tools,HudEquipmentSlot.ResourcePack or HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy or HudEquipmentSlot.Carry=>o.Items,_=>true};
        static HudEquipmentSnapshot Selected(in HudPersonalSnapshot s,HudEquipmentSlot slot)=>slot switch{
            HudEquipmentSlot.Primary=>s.Primary,HudEquipmentSlot.Secondary=>s.Secondary,HudEquipmentSlot.Melee=>s.Melee,
            HudEquipmentSlot.Tool=>s.Tool,HudEquipmentSlot.ResourcePack=>s.ResourcePack,HudEquipmentSlot.Consumable=>s.Consumable,
            HudEquipmentSlot.Heavy=>s.Heavy,HudEquipmentSlot.Carry=>s.Carry,_=>default};
        public static string Direction(Vector3 towardSource,Matrix4x4 worldToView)
        {
            // Unity view space looks down -Z. Ignore pitch so an overhead source is not a false front hit.
            towardSource.Y=0;
            if(!float.IsFinite(towardSource.X)||!float.IsFinite(towardSource.Z)||Math.Max(Math.Abs(towardSource.X),Math.Abs(towardSource.Z))<.0001f)return "";
            towardSource/=Math.Max(Math.Abs(towardSource.X),Math.Abs(towardSource.Z));
            var v=Vector3.TransformNormal(towardSource,worldToView);
            if(!float.IsFinite(v.X)||!float.IsFinite(v.Z)||Math.Max(Math.Abs(v.X),Math.Abs(v.Z))<.0001f)return "";
            return Math.Abs(v.X)>Math.Abs(v.Z)?v.X>0?"右侧":"左侧":v.Z<0?"前方":"后方";
        }
    }
}
