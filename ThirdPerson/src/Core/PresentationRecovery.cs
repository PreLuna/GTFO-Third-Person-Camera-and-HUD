namespace PreLuna.ThirdPerson.Core
{
    internal readonly record struct PresentationWorld(WorldContext Context,long Fps,long Camera,long Culler,
        long Holder,long ItemCamera,long HolderCamera,long ItemAlign);
    internal readonly record struct PresentationBinding(PresentationWorld World,long Item);

    // A fresh binding must settle after a release. The recovery state holds only
    // identity values, so waiting never extends a Unity object's lifetime.
    internal sealed class PresentationRecovery
    {
        PresentationBinding candidate;
        int firstFrame=-1,lastFrame=-1;
        float firstTime;

        internal bool Observe(PresentationBinding binding,int frame,float time)
        {
            if(lastFrame<0||binding!=candidate||(frame!=lastFrame&&frame!=lastFrame+1))
            {candidate=binding;firstFrame=frame;firstTime=time;}
            lastFrame=frame;
            return frame-firstFrame>=2&&time-firstTime>=.075f;
        }

        internal void Reset(){candidate=default;firstFrame=lastFrame=-1;}
    }
}
