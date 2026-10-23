using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // Feedback is a separately labelled historical change. It never substitutes for a current readout.
    public sealed class HudPersonalFeedback
    {
        struct Track
        {
            public bool Known;
            public double Value,SampleAt,Delta,Previous,Until;
            public long Sequence;
            public void Observe(in HudReadoutValue next,double sampleAt,long sequence,double now,float seconds,double maximumGap)
            {
                if(!next.Available||!double.IsFinite(sampleAt)||sampleAt>now){this=default;return;}
                double value=next.Amount!.Value;
                if(Known&&(sequence<Sequence||sampleAt-SampleAt>maximumGap)){this=default;}
                if(Known&&sampleAt>SampleAt&&sequence>Sequence&&value!=Value)
                {Previous=Value;Delta=value-Value;Until=now+seconds;}
                Known=true;Value=value;SampleAt=sampleAt;Sequence=sequence;
                if(now>=Until){Delta=0;Previous=0;}
            }
            public readonly float Alpha(double now,float seconds)=>Delta==0?0:(float)Math.Clamp((Until-now)/seconds,0,1);
        }
        ContextStamp context;
        LocalViewBinding binding;
        long eligibility,epoch,revision;
        Track health,infection;
        public void Reset(){context=default;binding=default;eligibility=epoch=revision=0;health=infection=default;}
        public HudPanelView Apply(in HudRuntimeContext current,in HudContentHeader header,in HudPanelView view,in HudPersonalOptions raw,in HudContentPolicy policy)
        {
            var o=raw.Sanitized();var p=view.Personal;
            if(!o.Enabled||!o.ValueFeedback||!p.Active||!HudEligibility.EvaluateCurrent(current).Allowed)
            {Reset();return view;}
            if(context!=current.ExpectedContext||binding!=current.ExpectedBinding||eligibility!=current.CurrentView.EligibilityEpoch
                ||epoch!=header.WorkEpoch||revision!=header.ContentRevision)Reset();
            context=current.ExpectedContext;binding=current.ExpectedBinding;eligibility=current.CurrentView.EligibilityEpoch;
            epoch=header.WorkEpoch;revision=header.ContentRevision;
            health.Observe(o.Health?p.Health:default,p.HealthSampleAt,header.Origin.Input.Sequence,current.NowSeconds,o.FeedbackSeconds,policy.LiveAgeSeconds);
            infection.Observe(o.Infection?p.Infection:default,p.InfectionSampleAt,header.Origin.Input.Sequence,current.NowSeconds,o.FeedbackSeconds,policy.LiveAgeSeconds);
            float ha=health.Alpha(current.NowSeconds,o.FeedbackSeconds),ia=infection.Alpha(current.NowSeconds,o.FeedbackSeconds);
            return view with{Personal=p with{HealthChange=ha>0?HudReadoutFormatting.Change(health.Delta,o.PercentDecimals):"",
                InfectionChange=ia>0?HudReadoutFormatting.Change(infection.Delta,o.PercentDecimals):"",ChangeAlpha=Math.Max(ha,ia),
                PreviousHealthFraction=ha>0?(float)Math.Clamp(health.Previous,0,1):null,
                ShowInfection=p.ShowInfection||o.Infection&&ia>0}};
        }
    }
}
