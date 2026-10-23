using System;
using System.Diagnostics;

namespace PreLuna.ThirdPerson.Core
{
    public struct FrameSample
    {
        public int Frame;
        public double FrameMs,SolveMs,RestoreMs;
        public long MeasuredAllocatedBytes,PhysicsQueries;
        public int MainRenders,AuxiliaryRenders;
        public bool Requested,Eligible;
    }
    public sealed class PerformanceSession
    {
        FrameSample[] samples;
        FrameSample current;
        bool pending;
        long queries;
        public int Count {get;private set;}
        public int Capacity=>samples?.Length??0;
        public bool Active {get;private set;}
        public void Start(int capacity)
        {samples=new FrameSample[Math.Clamp(capacity,60,7200)];Count=0;pending=false;Active=true;}
        public void BeginFrame(int frame,double frameMs,bool requested,bool eligible,long totalQueries)
        {
            if(!Active)return;
            Commit(totalQueries);
            if(!Active)return;
            current=new(){Frame=frame,FrameMs=double.IsFinite(frameMs)?Math.Clamp(frameMs,0,10000):0,Requested=requested,Eligible=eligible};
            queries=totalQueries;pending=true;
        }
        void Commit(long totalQueries)
        {
            if(!pending)return;
            current.PhysicsQueries=Math.Max(0,totalQueries-queries);samples[Count++]=current;pending=false;
            if(Count>=samples.Length)Active=false;
        }
        public void Stop(long totalQueries){if(Active)Commit(totalQueries);Active=false;}
        public void Render(bool auxiliary){if(!Active||!pending)return;if(auxiliary)current.AuxiliaryRenders++;else current.MainRenders++;}
        public Section Measure(bool restore=false)=>Active&&pending?new(this,restore):default;
        public FrameSample[] Export(){var result=new FrameSample[Count];if(Count>0)Array.Copy(samples,result,Count);return result;}
        public readonly struct Section:IDisposable
        {
            readonly PerformanceSession session;
            readonly bool restore;
            readonly long ticks,bytes;
            internal Section(PerformanceSession session,bool restore){this.session=session;this.restore=restore;ticks=Stopwatch.GetTimestamp();bytes=GC.GetAllocatedBytesForCurrentThread();}
            public void Dispose()
            {
                if(session==null)return;
                double ms=(Stopwatch.GetTimestamp()-ticks)*1000.0/Stopwatch.Frequency;
                if(restore)session.current.RestoreMs+=ms;else session.current.SolveMs+=ms;
                session.current.MeasuredAllocatedBytes+=Math.Max(0,GC.GetAllocatedBytesForCurrentThread()-bytes);
            }
        }
    }
}
