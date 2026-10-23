using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public enum HudVisualState { Exploration, Combat, Danger }
    public readonly record struct HudPanelVisual(HudVisualState State, float Emphasis, bool Expanded, bool Aiming)
    { public string Label => State==HudVisualState.Danger?"危险":State==HudVisualState.Combat?"战斗":"探索"; }
    // Tracks only value changes of the same owner/equipment, never enemy presence or estimated action progress.
    public sealed class HudPanelVisualController
    {
        ContextStamp context;
        LocalViewBinding binding;
        long eligibility;
        ObjectIdentity weapon;
        double health, clip, combatUntil, at=double.NaN;
        bool hasHealth,hasClip;
        float emphasis;
        public void Reset() { context=default;binding=default;eligibility=0;weapon=default;hasHealth=hasClip=false;combatUntil=0;at=double.NaN;emphasis=0; }
        public HudPanelVisual Update(in HudRuntimeContext current, bool hasSnapshot, in HudContentSnapshot snapshot,
            in HudContentPolicy policy, in HudPanelOptions raw)
        {
            var options=raw.Sanitized();
            if(!HudEligibility.EvaluateCurrent(current).Allowed) { Reset();return default; }
            if(context!=current.ExpectedContext||binding!=current.ExpectedBinding||eligibility!=current.CurrentView.EligibilityEpoch) Reset();
            context=current.ExpectedContext;binding=current.ExpectedBinding;eligibility=current.CurrentView.EligibilityEpoch;
            bool danger=false,valid=false;
            if(hasSnapshot && HudContentRules.EvaluateData(current,snapshot.Header,snapshot.Header.WorkEpoch,snapshot.Header.ContentRevision,policy).Allowed)
            {
                var owner=current.ExpectedBinding.LocalPlayer.Agent;
                if(ReadQuantity(current,snapshot,snapshot.Personal.Health,owner,HudInformationId.Health,policy,out var hp))
                {
                    valid=true; danger=hp.Amount<=options.Behavior.LowHealth;
                    if(hasHealth&&hp.Amount<health) combatUntil=current.NowSeconds+options.Behavior.CombatHoldSeconds;
                    health=hp.Amount;hasHealth=true;
                } else hasHealth=false;
                if(ReadQuantity(current,snapshot,snapshot.Personal.Infection,owner,HudInformationId.Infection,policy,out var infection)) { valid=true;danger|=infection.Amount>=options.Behavior.HighInfection; }
                var equipped=default(HudEquipmentSnapshot);
                if(Read(current,snapshot,snapshot.Personal.Selected,owner,HudFreshness.Live,policy,out var selected))
                    equipped=selected.Slot==HudEquipmentSlot.Primary?snapshot.Personal.Primary:selected.Slot==HudEquipmentSlot.Secondary?snapshot.Personal.Secondary:default;
                if(Read(current,snapshot,equipped.Identity,equipped.Identity.Stamp.Source,HudFreshness.Identity,policy,out var id)
                    && id.Instance==equipped.Identity.Stamp.Source && id.Slot==equipped.Slot
                    && ReadQuantity(current,snapshot,equipped.Magazine,id.Instance,HudInformationId.Magazine,policy,out var rounds))
                {
                    if(hasClip&&weapon==id.Instance&&rounds.Amount!=clip) combatUntil=current.NowSeconds+options.Behavior.CombatHoldSeconds;
                    weapon=id.Instance;clip=rounds.Amount;hasClip=true;
                } else hasClip=false;
            } else { hasHealth=hasClip=false; }
            var state=danger&&valid?HudVisualState.Danger:current.NowSeconds<combatUntil?HudVisualState.Combat:HudVisualState.Exploration;
            float target=state==HudVisualState.Exploration?0:1;
            float dt=double.IsFinite(at)&&current.NowSeconds>=at?(float)Math.Min(.25,current.NowSeconds-at):0;
            emphasis=options.Behavior.ReduceMotion||options.Behavior.TransitionSeconds<=0||!double.IsFinite(at)?target
                :emphasis+(target-emphasis)*(1-MathF.Exp(-dt/options.Behavior.TransitionSeconds));
            at=current.NowSeconds;
            return new(state,emphasis,options.Layout.Expanded,current.CurrentView.Mode==ThirdPersonMode.ShoulderAim);
        }
        static bool ReadQuantity(in HudRuntimeContext c,in HudContentSnapshot s,in HudValue<HudQuantity> field,ObjectIdentity owner,
            HudInformationId id,in HudContentPolicy p,out HudQuantity value) => HudContentRules.TryReadQuantity(c,s.Header,field,owner,id,
                s.Header.WorkEpoch,s.Header.ContentRevision,p,out value,out _);
        static bool Read<T>(in HudRuntimeContext c,in HudContentSnapshot s,in HudValue<T> field,ObjectIdentity owner,HudFreshness freshness,
            in HudContentPolicy p,out T value) where T:struct,IHudContentValue => HudContentRules.TryRead(c,s.Header,field,owner,freshness,
                s.Header.WorkEpoch,s.Header.ContentRevision,p,out value,out _);
    }
    public readonly record struct HudExpandState(bool Expanded, bool WasHeld)
    {
        public HudExpandState Update(bool eligible, bool held, bool toggle) => !eligible?default
            :new(toggle?(held&&!WasHeld?!Expanded:Expanded):held,held);
    }
}
