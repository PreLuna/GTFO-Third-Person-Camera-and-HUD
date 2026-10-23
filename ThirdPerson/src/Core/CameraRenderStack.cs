using System;
using System.Collections.Generic;

namespace PreLuna.ThirdPerson.Core
{
    public sealed class CameraRenderStack
    {
        readonly Stack<long> auxiliaries=new();
        public long Main {get;private set;}
        public int Frame {get;private set;}=-1;
        public bool Active=>Main!=0;
        public int AuxiliaryCount=>auxiliaries.Count;
        public void Open(long main,int frame)
        {
            if(Active)throw new InvalidOperationException("A main-camera render scope is already active");
            if(main==0)throw new ArgumentException("Missing main camera",nameof(main));
            Main=main;Frame=frame;
        }
        public bool BeginAuxiliary(long id)
        {if(!Active||id==Main)return false;auxiliaries.Push(id);return true;}
        public bool EndAuxiliary(long id)
        {
            if(!Active||id==Main||auxiliaries.Count==0)return false;
            if(auxiliaries.Peek()!=id)throw new InvalidOperationException("Auxiliary camera callbacks ended out of order");
            auxiliaries.Pop();return auxiliaries.Count==0;
        }
        public void ValidateMain()
        {if(auxiliaries.Count!=0)throw new InvalidOperationException("An auxiliary camera did not finish rendering");}
        public void Clear(){Main=0;Frame=-1;auxiliaries.Clear();}
    }
}
