using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PreLuna.ThirdPerson.Core
{
    public sealed record PluginRecord(string Path,string Id,string Name,string Version);
    public sealed class PluginInventory
    {
        public readonly List<PluginRecord> Plugins=new();
        public readonly List<string> Errors=new();
        public static PluginInventory Scan(IEnumerable<string> paths)
        {
            var result=new PluginInventory();
            foreach(var path in paths)
            {
                try
                {
                    using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
                    if(!pe.HasMetadata)continue;
                    var reader=pe.GetMetadataReader();
                    foreach(var handle in reader.TypeDefinitions)foreach(var attrHandle in reader.GetTypeDefinition(handle).GetCustomAttributes())
                    {
                        var attr=reader.GetCustomAttribute(attrHandle);
                        if(attr.Constructor.Kind!=HandleKind.MemberReference)continue;
                        var parent=reader.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent;
                        if(parent.Kind!=HandleKind.TypeReference)continue;
                        var type=reader.GetTypeReference((TypeReferenceHandle)parent);
                        if(reader.GetString(type.Namespace)!="BepInEx"||reader.GetString(type.Name)!="BepInPlugin")continue;
                        var blob=reader.GetBlobReader(attr.Value);
                        if(blob.ReadUInt16()!=1)throw new InvalidDataException("Invalid plugin attribute");
                        result.Plugins.Add(new(Path.GetFullPath(path),blob.ReadSerializedString(),blob.ReadSerializedString(),blob.ReadSerializedString()));
                    }
                }
                catch(Exception e){result.Errors.Add(Path.GetFileName(path)+": "+e.GetType().Name);}
            }
            return result;
        }
        public string ValidateSingle(string id,string loadedPath)
        {
            int count=0;bool own=false;
            foreach(var plugin in Plugins)if(string.Equals(plugin.Id,id,StringComparison.OrdinalIgnoreCase)){count++;own|=string.Equals(plugin.Path,Path.GetFullPath(loadedPath),StringComparison.OrdinalIgnoreCase);}
            if(count>1)return "duplicate-plugin-id";
            if(count!=1||!own)return "loaded-plugin-not-in-inventory";
            return Errors.Count>0?"inventory-incomplete":null;
        }
    }
}
