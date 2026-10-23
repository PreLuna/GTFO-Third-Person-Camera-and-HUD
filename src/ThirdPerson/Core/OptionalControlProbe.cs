using System;
using System.Reflection;

namespace PreLuna.ThirdPerson.Core
{
    public sealed class OptionalControlProbe
    {
        readonly Func<Type> resolve;
        FieldInfo current;
        PropertyInfo blocked;
        volatile bool dirty=true;
        bool invalid;
        public string Status {get;private set;}="unresolved";
        public OptionalControlProbe(Func<Type> resolve){this.resolve=resolve;}
        public void Invalidate()=>dirty=true;
        public bool Read()
        {
            try
            {
                if(dirty)
                {
                    dirty=false;current=null;blocked=null;invalid=false;
                    var type=resolve();
                    if(type==null){Status="absent";return false;}
                    current=type.GetField("Current",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                    blocked=type.GetProperty("BlocksBody",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    invalid=current==null||blocked?.PropertyType!=typeof(bool)||blocked.GetMethod==null||blocked.GetIndexParameters().Length!=0;
                    Status=invalid?"unsupported-api":"ready";
                }
                if(invalid)return true;
                if(current==null)return false;
                var value=current.GetValue(null);
                bool result=value!=null&&(bool)blocked.GetValue(value);
                Status="ready";return result;
            }
            catch {Status="probe-error";dirty=true;return true;}
        }
    }
}
