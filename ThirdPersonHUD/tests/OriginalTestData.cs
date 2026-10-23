using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using PreLuna.ThirdPersonHUD.Panel;

internal sealed class OriginalTestPort : IHudOriginalHudPort
{
    internal sealed class Target : IHudOriginalTarget
    {
        public ObjectIdentity Identity{get;set;}
        public bool Alive{get;set;}=true;
        public bool Value;
        public bool ThrowRead{get;set;}
        public bool ThrowBefore{get;set;}
        public bool ThrowAfter{get;set;}
        public int Writes;
        public bool Hidden{get=>ThrowRead?throw new Exception("read"):Value;set{if(ThrowBefore){ThrowBefore=false;throw new Exception("before");}Value=value;Writes++;if(ThrowAfter){ThrowAfter=false;throw new Exception("after");}}}
    }
    public readonly Target[] Targets=new Target[5];
    public readonly Target[] TeamTargets=new Target[4];
    public readonly Target Countdown=new(){Identity=new(7000,700)};
    public int Calls;public HudOriginalGroup Missing{get;set;}public bool Throw{get;set;}
    public OriginalTestPort(){for(int i=0;i<5;i++)Targets[i]=new(){Identity=new(5000+i,500+i)};for(int i=0;i<4;i++)TeamTargets[i]=new(){Identity=new(6000+i,600+i)};}
    public bool TryGetTargets(HudOriginalGroup group,in LocalViewBinding binding,IHudOriginalTarget[] output,out int count)
    {
        Calls++;if(Throw)throw new Exception("port");count=0;if((Missing&group)!=0)return false;
        if(group==HudOriginalGroup.Countdown){output[0]=Countdown;count=1;return true;}
        for(int i=0;i<5;i++)if(group==(HudOriginalGroup)(1<<i)){output[0]=Targets[i];count=1;return true;}
        for(int i=0;i<4;i++)if(group==(HudOriginalGroup)(32<<i)){output[0]=TeamTargets[i];count=1;return true;}return false;
    }
}
