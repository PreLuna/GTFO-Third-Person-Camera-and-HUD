using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Panel;

namespace PreLuna.ThirdPersonHUD.Core
{
    public interface IHudOriginalTarget
    {
        ObjectIdentity Identity { get; }
        bool Alive { get; }
        bool Hidden { get; set; }
    }
    public interface IHudOriginalHudPort
    { bool TryGetTargets(HudOriginalGroup group,in LocalViewBinding binding,IHudOriginalTarget[] output,out int count); }
    public readonly record struct HudOriginalStatus(HudOriginalGroup Owned,HudOriginalGroup Conflicted,int OwnedTargets,int PendingRestores,long Writes,long Restores,long Errors);
    // A bounded compare-and-restore lease. Uses its own mask property, never enabled/active/text/config.
    public sealed class HudOriginalHud
    {
        sealed class Group
        {
            public readonly IHudOriginalTarget[] Targets=new IHudOriginalTarget[8];
            public readonly bool[] Owned=new bool[8];
            public int Count;public bool Conflict;
        }
        readonly Group[] groups={new(),new(),new(),new(),new(),new(),new(),new(),new(),new()};
        readonly IHudOriginalTarget[] scratch=new IHudOriginalTarget[8];
        readonly IHudOriginalHudPort port;
        readonly int thread=Environment.CurrentManagedThreadId;
        LocalViewBinding binding;ContextStamp context;
        long writes,restores,errors;
        bool recovering;
        public HudOriginalHud(IHudOriginalHudPort port){this.port=port;}
        public HudOriginalStatus Status
        {
            get{HudOriginalGroup owned=0,conflicted=0;int pending=0;for(int i=0;i<groups.Length;i++){var g=groups[i];if(g.Conflict)conflicted|=(HudOriginalGroup)(1<<i);for(int j=0;j<g.Count;j++)if(g.Owned[j]){owned|=(HudOriginalGroup)(1<<i);pending++;}}return new(owned,conflicted,pending,recovering?pending:0,writes,restores,errors);}
        }
        bool Release(Group g)
        {
            bool complete=true;
            for(int i=0;i<g.Count;i++)
            {
                if(!g.Owned[i])continue;
                try{var t=g.Targets[i];if(t.Alive&&t.Hidden){t.Hidden=false;restores++;}g.Owned[i]=false;}
                catch{errors++;complete=false;recovering=true;}
            }
            if(complete){Array.Clear(g.Targets,0,g.Count);Array.Clear(g.Owned,0,g.Count);g.Count=0;}return complete;
        }
        public void RestoreAll(){if(Environment.CurrentManagedThreadId!=thread)return;foreach(var g in groups)Release(g);if(Status.OwnedTargets==0)recovering=false;}
        public void ResetConflicts(){if(Environment.CurrentManagedThreadId!=thread)return;RestoreAll();foreach(var g in groups)g.Conflict=false;}
        public void RetainOnly(HudOriginalGroup permitted)
        {
            if(Environment.CurrentManagedThreadId!=thread)return;
            if(recovering){RestoreAll();return;}
            for(int i=0;i<groups.Length;i++)if((permitted&(HudOriginalGroup)(1<<i))==0)Release(groups[i]);
        }
        public void Apply(in HudRuntimeContext current,in ThirdPersonDisplayFrame display,bool rendered,HudOriginalGroup desired)
        {
            if(Environment.CurrentManagedThreadId!=thread)return;
            if(recovering){RestoreAll();return;}
            if(!rendered||!HudEligibility.EvaluateDisplay(current,display,HudDisplayUsage.ScreenComposition,HudValidityPolicy.Default).Allowed){RestoreAll();return;}
            if(binding!=current.ExpectedBinding||context!=current.ExpectedContext)
            {
                RestoreAll();if(Status.OwnedTargets!=0)return;
                binding=current.ExpectedBinding;context=current.ExpectedContext;foreach(var g in groups)g.Conflict=false;
            }
            for(int i=0;i<groups.Length;i++)
            {
                var g=groups[i];var flag=(HudOriginalGroup)(1<<i);
                if((desired&flag)==0||g.Conflict){Release(g);continue;}
                try
                {
                    Array.Clear(scratch,0,scratch.Length);
                    if(port==null||!port.TryGetTargets(flag,binding,scratch,out int count)||count<=0||count>8){Release(g);continue;}
                    bool same=count==g.Count;
                    for(int j=0;j<count;j++)
                    {
                        if(scratch[j]==null||!scratch[j].Alive||!scratch[j].Identity.IsValid)throw new InvalidOperationException("Invalid original HUD target.");
                        for(int k=0;k<j;k++)if(scratch[k].Identity==scratch[j].Identity)throw new InvalidOperationException("Duplicate original HUD target.");
                        same&=j<g.Count&&scratch[j].Identity==g.Targets[j].Identity;
                    }
                    if(!same)
                    {
                        if(!Release(g))continue;
                        g.Count=count;Array.Copy(scratch,g.Targets,count);
                        for(int j=0;j<count;j++)if(!g.Targets[j].Hidden)
                        {g.Owned[j]=true;g.Targets[j].Hidden=true;writes++;}
                    }
                    else for(int j=0;j<count;j++)if(g.Owned[j]&&!g.Targets[j].Hidden)
                    {g.Owned[j]=false;g.Conflict=true;}
                    if(g.Conflict)Release(g); // Another writer made it visible; yield instead of repeatedly hiding it.
                }
                catch{errors++;RestoreAll();return;}
                finally{Array.Clear(scratch,0,scratch.Length);}
            }
        }
    }
}
