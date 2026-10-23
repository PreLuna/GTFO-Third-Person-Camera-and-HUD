using System;
using System.Globalization;
using System.Text;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudReadoutValue(HudValueState State,string Text,double? Amount=null)
    { public bool Available=>State==HudValueState.Available&&Amount.HasValue; }
    public readonly record struct HudDetailRow(string Label,string Name,string Value,HudValueState State);
    public readonly record struct HudPersonalPresentation
    {
        public bool Active { get; init; }
        public HudPersonalOptions Options { get; init; }
        public HudEquipmentSlot Selected { get; init; }
        public HudValueState SelectionState { get; init; }
        public HudReadoutValue Health { get; init; }
        public HudReadoutValue Infection { get; init; }
        public double HealthSampleAt { get; init; }
        public double InfectionSampleAt { get; init; }
        public string HealthChange { get; init; }
        public string InfectionChange { get; init; }
        public float ChangeAlpha { get; init; }
        public float? PreviousHealthFraction { get; init; }
        public bool ShowInfection { get; init; }
        public string Life { get; init; }
        public string FocusLabel { get; init; }
        public bool FocusVisible { get; init; }
        public string FocusStatus { get; init; }
        public string MainLabel { get; init; }
        public string ReserveLabel { get; init; }
        public string ToolName { get; init; }
        public string ToolStatus { get; init; }
        public string OtherWeaponLabel { get; init; }
        public string OtherWeaponName { get; init; }
        public string OtherWeaponValue { get; init; }
        public string ItemLabel { get; init; }
        public string ItemName { get; init; }
        public string ItemValue { get; init; }
        public HudDetailRow Primary { get; init; }
        public HudDetailRow Secondary { get; init; }
        public HudDetailRow Tool { get; init; }
        public HudDetailRow Pack { get; init; }
        public HudDetailRow Consumable { get; init; }
        public HudDetailRow Heavy { get; init; }
        public HudDetailRow Carry { get; init; }
    }
    public static class HudReadoutFormatting
    {
        public static string State(HudValueState state,bool weapon=false)=>state switch{
            HudValueState.NotEquipped=>weapon?"未装备":"未携带",HudValueState.Unsupported=>"不支持",
            HudValueState.Unreadable=>"不可读",HudValueState.Invalid=>"异常",HudValueState.Expired=>"已过期",HudValueState.Unknown=>"未读取",_=>""};
        public static string Percent(double amount,int decimals)
        {
            if(!double.IsFinite(amount)||amount<0||amount>double.MaxValue/100)return "—";
            decimals=Math.Clamp(decimals,0,2);double percent=amount*100;
            double rounded=Math.Round(percent,decimals,MidpointRounding.AwayFromZero);
            string format=decimals==0?"0":"0."+new string('0',decimals);
            if(amount>0&&rounded==0)return "<"+Math.Pow(10,-decimals).ToString(format,CultureInfo.InvariantCulture)+"%";
            return percent>1e12?percent.ToString("0.###E+0",CultureInfo.InvariantCulture)+"%":rounded.ToString(format,CultureInfo.InvariantCulture)+"%";
        }
        public static string Change(double difference,int decimals)=>difference==0?"":(difference>0?"+":"−")+Percent(Math.Abs(difference),decimals);
        public static string Name(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return "—";
            var result=new StringBuilder(Math.Min(128,value.Length));
            for(int i=0;i<value.Length&&result.Length<128;i++)
            {
                char c=value[i];if(char.IsControl(c)){if(result.Length>0&&result[result.Length-1]!=' ')result.Append(' ');continue;}
                if(char.IsHighSurrogate(c)){if(i+1<value.Length&&char.IsLowSurrogate(value[i+1])&&result.Length<127){result.Append(c).Append(value[++i]);}continue;}
                if(!char.IsLowSurrogate(c))result.Append(c);
            }
            return result.ToString().Trim();
        }
    }
    // Pure mapping. The caller supplies current lifecycle versions, not versions copied from an unvalidated result.
    public static class HudPersonalMapper
    {
        readonly record struct Equipment(HudEquipmentSlot Slot,ObjectIdentity Identity,HudValueState State,string Name,
            HudReadoutValue Magazine,HudReadoutValue Reserve,HudReadoutValue Supply,HudReadoutValue Count,string Deployment)
        {
            public string Ammo=>State==HudValueState.Available?Magazine.Text+" / "+Reserve.Text:HudReadoutFormatting.State(State,true);
            public string Item=>State==HudValueState.Available?Name+" · "+Count.Text:HudReadoutFormatting.State(State);
        }
        readonly struct Reader
        {
            readonly HudRuntimeContext current;
            readonly HudContentSnapshot snapshot;
            readonly HudContentPolicy policy;
            readonly long epoch,revision;
            readonly int decimals;
            public ObjectIdentity Player=>current.ExpectedBinding.LocalPlayer.Agent;
            public Reader(in HudRuntimeContext c,in HudContentSnapshot s,long e,long r,in HudContentPolicy p,int precision)
            {current=c;snapshot=s;epoch=e;revision=r;policy=p;decimals=precision;}
            public bool Read<T>(in HudValue<T> field,ObjectIdentity source,HudFreshness freshness,out T value,out HudValueState state) where T:struct,IHudContentValue
                =>HudContentRules.TryRead(current,snapshot.Header,field,source,freshness,epoch,revision,policy,out value,out state);
            public HudReadoutValue Quantity(in HudValue<HudQuantity> field,ObjectIdentity source,HudInformationId id,string suffix="")
            {
                if(!HudContentRules.TryReadQuantity(current,snapshot.Header,field,source,id,epoch,revision,policy,out var q,out var state))return Missing(state);
                bool fraction=q.Unit==HudUnit.Fraction;
                string text=fraction?HudReadoutFormatting.Percent(q.Amount,decimals):q.Amount.ToString("0",CultureInfo.InvariantCulture)+suffix;
                return text=="—"?Missing(HudValueState.Invalid):new(HudValueState.Available,text,q.Amount);
            }
            public Equipment Gear(in HudEquipmentSnapshot gear,HudEquipmentSlot expected)
            {
                HudValueState state=HudValueState.Invalid;ObjectIdentity identity=default;var id=default(HudEquipmentIdentity);
                bool has=gear.Slot==expected&&Read(gear.Identity,gear.Identity.State==HudValueState.Available?gear.Identity.Stamp.Source:Player,HudFreshness.Identity,out id,out state);
                if(has)
                {
                    // Validate both the opaque source and slot; a stale value in the same slot cannot identify another item.
                    if(id.Instance!=gear.Identity.Stamp.Source||id.Slot!=expected){has=false;state=HudValueState.Invalid;}else identity=id.Instance;
                }
                string deployment="";
                if(expected==HudEquipmentSlot.Tool&&Read(gear.Deployment,has?identity:Player,HudFreshness.Live,out var deployed,out _))
                    deployment=deployed.Kind==HudDeploymentKind.Deployed?"已部署":deployed.Kind==HudDeploymentKind.Recovering?"回收中":"携带中";
                if(!has)return new(expected,default,state,HudReadoutFormatting.State(state),Missing(state),Missing(state),Missing(state),Missing(state),deployment);
                string name=Read(gear.Name,identity,HudFreshness.Identity,out var title,out var nameState)?HudReadoutFormatting.Name(title.Text):Role(expected)+" · "+HudReadoutFormatting.State(nameState);
                var count=expected==HudEquipmentSlot.ResourcePack?Quantity(gear.Uses,identity,HudInformationId.ResourcePackUses,"次")
                    :Quantity(gear.ItemCount,identity,HudInformationId.ConsumableCount,"个");
                return new(expected,identity,HudValueState.Available,name,Quantity(gear.Magazine,identity,HudInformationId.Magazine),
                    Quantity(gear.ReserveRounds,identity,HudInformationId.ReserveRounds),Quantity(gear.SupplyRatio,identity,
                    expected==HudEquipmentSlot.Tool?HudInformationId.ToolResource:HudInformationId.SupplyRatio),count,deployment);
            }
        }
        static HudReadoutValue Missing(HudValueState state)=>new(state,state==HudValueState.NotEquipped?"未携带":state==HudValueState.Unsupported?"不支持":"—");
        static float? Fraction(in HudReadoutValue value)=>value.Available?(float)Math.Clamp(value.Amount!.Value,0,1):null;
        static HudDetailRow Row(string label,in Equipment gear,string value)=>new(label,gear.State==HudValueState.Available?gear.Name:"—",gear.State==HudValueState.Available?value:HudReadoutFormatting.State(gear.State),gear.State);
        static HudDetailRow ToolRow(in Equipment tool)
        {
            var row=Row("工具",tool,tool.Supply.Text);
            return !string.IsNullOrEmpty(tool.Deployment)&&tool.Deployment!="携带中"
                ?row with{Value=(tool.State==HudValueState.Available?tool.Supply.Text:"—")+" · "+tool.Deployment}:row;
        }
        static bool IsGear(HudEquipmentSlot slot)=>slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary or HudEquipmentSlot.Melee or HudEquipmentSlot.Tool;
        public static string Role(HudEquipmentSlot slot)=>slot switch{HudEquipmentSlot.Primary=>"主武器",HudEquipmentSlot.Secondary=>"副武器",HudEquipmentSlot.Melee=>"近战",
            HudEquipmentSlot.Tool=>"工具",HudEquipmentSlot.ResourcePack=>"补给",HudEquipmentSlot.Consumable=>"消耗品",HudEquipmentSlot.Heavy=>"重型物品",HudEquipmentSlot.Carry=>"搬运",HudEquipmentSlot.EmptyHands=>"空手",_=>"装备"};
        public static bool WantsDetails(in HudRuntimeContext current,in HudContentSnapshot snapshot,long epoch,long revision,in HudContentPolicy policy,in HudPersonalOptions raw)
        {
            var o=raw.Sanitized();if(!o.Enabled||!o.AutoExpandSelected||!o.FocusSelected)return false;
            var r=new Reader(current,snapshot,epoch,revision,policy,o.PercentDecimals);
            return r.Read(snapshot.Personal.Selected,r.Player,HudFreshness.Live,out var selection,out _)
                && (selection.Slot==HudEquipmentSlot.Tool?o.Tools:selection.Slot is HudEquipmentSlot.ResourcePack or HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy or HudEquipmentSlot.Carry?o.Items:false);
        }
        public static HudPanelView Map(in HudRuntimeContext current,in HudContentSnapshot snapshot,long epoch,long revision,
            in HudContentPolicy policy,in HudPersonalOptions raw,bool expanded)
        {
            var o=raw.Sanitized();var reader=new Reader(current,snapshot,epoch,revision,policy,o.PercentDecimals);
            if(!o.Enabled)return Unavailable(o,HudValueState.Unknown);
            var decision=HudContentRules.EvaluateData(current,snapshot.Header,epoch,revision,policy);
            if(!decision.Allowed)return Unavailable(o,decision.SharedReason==HudDenyReason.ResultExpired?HudValueState.Expired:HudValueState.Invalid);
            var p=snapshot.Personal;
            var health=reader.Quantity(p.Health,reader.Player,HudInformationId.Health);
            var infection=reader.Quantity(p.Infection,reader.Player,HudInformationId.Infection);
            var main=reader.Gear(p.Primary,HudEquipmentSlot.Primary);var special=reader.Gear(p.Secondary,HudEquipmentSlot.Secondary);
            var tool=reader.Gear(p.Tool,HudEquipmentSlot.Tool);var pack=reader.Gear(p.ResourcePack,HudEquipmentSlot.ResourcePack);
            var consumable=reader.Gear(p.Consumable,HudEquipmentSlot.Consumable);var heavy=reader.Gear(p.Heavy,HudEquipmentSlot.Heavy);
            var carry=reader.Gear(p.Carry,HudEquipmentSlot.Carry);var melee=reader.Gear(p.Melee,HudEquipmentSlot.Melee);
            bool selected=reader.Read(p.Selected,reader.Player,HudFreshness.Live,out var selection,out var selectionState);
            var slot=selected?selection.Slot:HudEquipmentSlot.Unknown;
            string life=reader.Read(p.Life,reader.Player,HudFreshness.Live,out var lifeValue,out _)?lifeValue.Kind switch{HudLifeKind.Downed=>"已倒地",HudLifeKind.Dead=>"已死亡",_=>""}:"";
            Equipment focus=slot switch{HudEquipmentSlot.Primary=>main,HudEquipmentSlot.Secondary=>special,HudEquipmentSlot.Tool=>tool,
                HudEquipmentSlot.ResourcePack=>pack,HudEquipmentSlot.Consumable=>consumable,HudEquipmentSlot.Heavy=>heavy,HudEquipmentSlot.Carry=>carry,HudEquipmentSlot.Melee=>melee,_=>default};
            bool module=slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary or HudEquipmentSlot.Melee?o.Weapons:slot==HudEquipmentSlot.Tool?o.Tools:o.Items;
            bool focusSelection=o.FocusSelected&&module;
            if(!o.FocusSelected&&o.Weapons){focus=main;slot=HudEquipmentSlot.Primary;selected=true;}
            string focusName=selected?slot==HudEquipmentSlot.EmptyHands?"空手":focus.State==HudValueState.Available?focus.Name:Role(slot)+" · "+HudReadoutFormatting.State(focus.State,IsGear(slot)):"装备 · "+HudReadoutFormatting.State(selectionState);
            string primary="—",reserve="—",mainLabel="状态",reserveLabel="",status="",supply="—";
            if(selected&&(focusSelection||!o.FocusSelected&&o.Weapons))
            {
                if(slot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary)
                {primary=focus.State==HudValueState.NotEquipped?"未装备":focus.Magazine.Text;reserve=focus.State==HudValueState.NotEquipped?"":focus.Reserve.Text;mainLabel=focus.State==HudValueState.NotEquipped?"装备状态":"弹匣";reserveLabel=focus.State==HudValueState.NotEquipped?"":"备用";supply=o.SupplyRatio?focus.Supply.Text:"";status=QuantityStatus(focus.Magazine,focus.Reserve);}
                else if(slot==HudEquipmentSlot.Tool)
                {primary=focus.State==HudValueState.NotEquipped?"未装备":focus.Supply.Text;reserve=focus.State==HudValueState.NotEquipped?"":string.IsNullOrEmpty(focus.Deployment)?"—":focus.Deployment;mainLabel=focus.State==HudValueState.NotEquipped?"工具状态":"工具资源";reserveLabel=focus.State==HudValueState.NotEquipped?"":"部署状态";supply=o.SupplyRatio?focus.Supply.Text:"";status=QuantityStatus(focus.Supply);}
                else if(slot is HudEquipmentSlot.ResourcePack or HudEquipmentSlot.Consumable or HudEquipmentSlot.Heavy)
                {primary=focus.Count.Text;reserve=focus.State==HudValueState.NotEquipped?"":focus.State==HudValueState.Available?"携带中":"—";mainLabel=focus.State==HudValueState.NotEquipped?"物品状态":slot==HudEquipmentSlot.ResourcePack?"使用次数":"物品数量";reserveLabel=focus.State==HudValueState.NotEquipped?"":"物品状态";status=QuantityStatus(focus.Count);}
                else if(slot is HudEquipmentSlot.Carry or HudEquipmentSlot.Melee or HudEquipmentSlot.EmptyHands)
                {primary=slot==HudEquipmentSlot.Carry?"搬运中":slot==HudEquipmentSlot.Melee?"近战":"空手";reserve="";mainLabel="当前状态";reserveLabel="";if(slot!=HudEquipmentSlot.EmptyHands&&focus.State!=HudValueState.Available)primary="—";}
            }
            else if(selected){focusName=Role(slot);status="";}
            if(selected&&focus.State==HudValueState.NotEquipped)status=HudReadoutFormatting.State(focus.State,IsGear(slot));
            var other=slot==HudEquipmentSlot.Secondary?main:special;
            var item=ChooseItem(slot,pack,consumable,heavy,carry);
            string itemText=item.State==HudValueState.Available?item.Slot==HudEquipmentSlot.Carry?item.Name:item.Item:HudReadoutFormatting.State(item.State);
            var info=new HudPersonalPresentation{Active=true,Options=o,Selected=selected?selection.Slot:HudEquipmentSlot.Unknown,SelectionState=selectionState,
                Health=health,Infection=infection,HealthSampleAt=p.Health.Stamp.CapturedAtSeconds,InfectionSampleAt=p.Infection.Stamp.CapturedAtSeconds,
                ShowInfection=o.Infection&&(!infection.Available||infection.Amount>0||expanded),Life=life,
                FocusLabel=o.FocusSelected?Role(selected?selection.Slot:HudEquipmentSlot.Unknown):"主武器摘要",FocusVisible=selected?module||!o.FocusSelected&&o.Weapons:o.Weapons||o.Tools||o.Items,FocusStatus=status,
                MainLabel=mainLabel,ReserveLabel=reserveLabel,ToolName=tool.State==HudValueState.Available?tool.Name:"—",ToolStatus=tool.Deployment,
                OtherWeaponLabel=Role(other.Slot),OtherWeaponName=other.State==HudValueState.Available?other.Name:"—",OtherWeaponValue=other.Ammo,ItemLabel=Role(item.Slot),
                ItemName=item.State==HudValueState.Available?item.Name:"—",ItemValue=item.State==HudValueState.Available?item.Slot==HudEquipmentSlot.Carry?"搬运中":item.Count.Text:HudReadoutFormatting.State(item.State),
                Primary=Row("主武器",main,main.Ammo),Secondary=Row("副武器",special,special.Ammo),Tool=ToolRow(tool),
                Pack=Row("补给",pack,pack.Count.Text),Consumable=Row("消耗品",consumable,consumable.Count.Text),Heavy=Row("重型物品",heavy,heavy.Count.Text),Carry=Row("搬运",carry,carry.State==HudValueState.Available?"搬运中":HudReadoutFormatting.State(carry.State))};
            return new(health.Text,infection.Text,focusName,primary,reserve,tool.Supply.Text,itemText,other.Ammo,supply,Fraction(health),Fraction(tool.Supply)){Personal=info};
        }
        static string QuantityStatus(HudReadoutValue a,HudReadoutValue? b=null)
        {var state=!a.Available?a.State:b.HasValue&&!b.Value.Available?b.Value.State:HudValueState.Available;return HudReadoutFormatting.State(state);}
        static Equipment ChooseItem(HudEquipmentSlot selected,Equipment pack,Equipment consumable,Equipment heavy,Equipment carry)
        {
            if(selected==HudEquipmentSlot.ResourcePack)return pack;if(selected==HudEquipmentSlot.Consumable)return consumable;
            if(selected==HudEquipmentSlot.Heavy)return heavy;if(selected==HudEquipmentSlot.Carry)return carry;
            if(carry.State==HudValueState.Available)return carry;if(heavy.State==HudValueState.Available)return heavy;
            if(pack.State==HudValueState.Available)return pack;if(consumable.State==HudValueState.Available)return consumable;
            // A temporarily unreadable bag must not look like an explicitly empty one.
            if(pack.State!=HudValueState.NotEquipped)return pack;if(consumable.State!=HudValueState.NotEquipped)return consumable;
            if(heavy.State!=HudValueState.NotEquipped)return heavy;return carry;
        }
        public static HudPanelView Unavailable(in HudPersonalOptions raw,HudValueState state)
        {
            var o=raw.Sanitized();if(!o.Enabled)o=o with{Health=false,Infection=false,Weapons=false,Tools=false,Items=false};
            var missing=Missing(state);string status=HudReadoutFormatting.State(state);
            var row=new HudDetailRow("","—",status,state);
            return HudPanelView.Empty with{Weapon="装备 · "+status,Personal=new(){Active=true,Options=o,SelectionState=state,Health=missing,Infection=missing,
                ShowInfection=o.Infection,FocusLabel="装备",FocusVisible=o.Weapons||o.Tools||o.Items,FocusStatus=status,MainLabel="状态",ReserveLabel="",ToolName=status,ToolStatus="",
                OtherWeaponLabel="副武器",OtherWeaponName=status,OtherWeaponValue="—",ItemLabel="携带",ItemName="—",ItemValue=status,Primary=row with{Label="主武器"},Secondary=row with{Label="副武器"},
                Tool=row with{Label="工具"},Pack=row with{Label="补给"},Consumable=row with{Label="消耗品"},Heavy=row with{Label="重型物品"},Carry=row with{Label="搬运"}}};
        }
    }
}
