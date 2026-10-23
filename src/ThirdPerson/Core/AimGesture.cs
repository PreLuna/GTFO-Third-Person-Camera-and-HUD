using System;

namespace PreLuna.ThirdPerson.Core
{
    // A pair is consumed once. A held button across a context switch must be released.
    public sealed class AimGesture
    {
        InputContext context;
        bool bound,held,armed,pending;
        double deadline;
        public ViewMode Mode {get;private set;}=ViewMode.Exploration;
        public bool PendingExit=>pending;
        public bool WantsAim=>Mode is ViewMode.ShoulderAim or ViewMode.FirstPersonAim;
        public void Cancel(){bound=false;held=false;armed=false;pending=false;Mode=ViewMode.Exploration;}
        public void Sample(InputContext next,bool pressed,double now,double window)
        {
            if(!double.IsFinite(now)){Cancel();return;}
            window=double.IsFinite(window)?Math.Clamp(window,.12,.5):.25;
            if(!bound||next!=context)
            {Cancel();context=next;bound=true;held=pressed;return;}
            if(!next.CanControl){Cancel();return;}
            // Boundary belongs to the first click's expiry, not the second click.
            if(armed&&now>=deadline){if(pending)Mode=ViewMode.Exploration;armed=false;pending=false;}
            bool down=pressed&&!held;held=pressed;
            if(!down)return;
            if(armed)
            {Mode=ViewMode.FirstPersonAim;armed=false;pending=false;return;}
            armed=true;deadline=now+window;
            if(Mode==ViewMode.Exploration)Mode=ViewMode.ShoulderAim;
            else pending=true;
        }
    }
}
