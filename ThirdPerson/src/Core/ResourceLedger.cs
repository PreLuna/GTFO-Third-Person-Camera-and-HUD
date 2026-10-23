using System;
using System.Threading;

namespace PreLuna.ThirdPerson.Core
{
    public enum OwnedResource { VisualObject, Mesh, Material, RenderScope, Subscription }
    public static class ResourceLedger
    {
        static readonly long[] created=new long[5],released=new long[5];
        public static void Create(OwnedResource kind)=>Interlocked.Increment(ref created[(int)kind]);
        public static void Release(OwnedResource kind)=>Interlocked.Increment(ref released[(int)kind]);
        public static long Live(OwnedResource kind)=>Interlocked.Read(ref created[(int)kind])-Interlocked.Read(ref released[(int)kind]);
        public static object Snapshot()
        {
            var result=new object[5];
            foreach(OwnedResource kind in Enum.GetValues<OwnedResource>())result[(int)kind]=new{kind=kind.ToString(),created=Interlocked.Read(ref created[(int)kind]),released=Interlocked.Read(ref released[(int)kind]),live=Live(kind)};
            return result;
        }
    }
}
