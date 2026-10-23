using System.Numerics;

namespace PreLuna.ThirdPerson.Core
{
    public readonly record struct WorldContext(long Player,long Owner,long Floor,int Dimension,long Model);
    public sealed class ContextTracker
    {
        WorldContext previous;
        Vector3 position;
        bool bound;
        public bool Observe(WorldContext context,Vector3 body)
        {
            bool changed=bound&&(context!=previous||!ViewMath.Finite(body)||Vector3.DistanceSquared(position,body)>9);
            previous=context;position=body;bound=context.Player!=0;
            return changed;
        }
        public void Reset(){bound=false;previous=default;position=default;}
    }
}
