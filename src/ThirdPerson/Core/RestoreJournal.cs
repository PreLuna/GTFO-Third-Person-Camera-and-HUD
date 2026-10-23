using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Core
{
    public sealed class RestoreJournal : IDisposable
    {
        readonly List<Action> undo=new();
        bool disposed;
        public void Add(Action restore)
        { if(disposed)throw new ObjectDisposedException(nameof(RestoreJournal));undo.Add(restore??throw new ArgumentNullException(nameof(restore))); }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            List<Exception> errors=null;
            for(int i=undo.Count-1;i>=0;i--)try{undo[i]();}catch(Exception e){(errors??=new()).Add(e);}
            undo.Clear();
            if(errors!=null)throw new AggregateException("Third-person restoration failed",errors);
        }
    }
    public sealed class PrototypeState
    {
        public bool Requested { get; private set; }
        public bool Faulted { get; private set; }
        public int Generation { get; private set; }
        public FallbackReason Reason { get; private set; }=FallbackReason.Disabled;
        public bool CanRender => Requested&&!Faulted&&Reason==FallbackReason.None;
        public void Request(bool value) { Requested=value;if(!value)Reason=FallbackReason.Disabled; }
        public void Bind() { Generation++; }
        public void Evaluate(FallbackReason reason) { Reason=Faulted?FallbackReason.Fault:!Requested?FallbackReason.Disabled:reason; }
        public void Fail() { Faulted=true;Reason=FallbackReason.Fault; }
        public void ResetFault() { Faulted=false;Reason=FallbackReason.Disabled; }
    }
}
