using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PreLuna.ThirdPerson.Diagnostics
{
    public sealed class NativeAddressAssessment
    {
        public string Status { get; set; }
        public long RVA { get; set; }
        public int Aliases { get; set; }
        public bool CanObserve => Status == "unique-address";
    }
    public sealed class P1MethodAddressGuard
    {
        readonly Dictionary<int, long> tokens = new();
        readonly Dictionary<long, int> aliases = new();
        public bool Available { get; private set; }
        public string Error { get; private set; }
        public string DatabaseSHA256 { get; private set; }
        public static P1MethodAddressGuard Load(string path)
        {
            var result = new P1MethodAddressGuard();
            try
            {
                var bytes = File.ReadAllBytes(path);
                result.DatabaseSHA256 = Convert.ToHexString(SHA256.HashData(bytes));
                if (bytes.Length < 20) throw new InvalidDataException("Missing database header.");
                if (BitConverter.ToUInt32(bytes, 0) != 0x4D544D55u || BitConverter.ToInt32(bytes, 4) != 1) throw new InvalidDataException("Unsupported UMTM database version; observation disabled.");
                int assemblyCount = BitConverter.ToInt32(bytes, 8), count = BitConverter.ToInt32(bytes, 12), offset = BitConverter.ToInt32(bytes, 16);
                if (assemblyCount < 1 || count < 1 || offset < 20 || (long)offset + (long)count * 16 > bytes.Length) throw new InvalidDataException("Unsupported database layout.");
                int moduleIndex = -1;
                using var reader = new BinaryReader(new MemoryStream(bytes));
                reader.BaseStream.Position = 20;
                for (int i = 0; i < assemblyCount; i++) if (reader.ReadString().StartsWith("Modules-ASM,", StringComparison.Ordinal)) moduleIndex = i;
                if (moduleIndex < 0 || reader.BaseStream.Position > offset) throw new InvalidDataException("Missing assembly or invalid name table.");
                for (int i = 0; i < count; i++)
                {
                    long address = BitConverter.ToInt64(bytes, offset + i * 8);
                    if (address <= 0) continue;
                    result.aliases.TryGetValue(address, out int occurrences); result.aliases[address] = occurrences + 1;
                    int tokenOffset = checked(offset + count * 8 + i * 8);
                    if (BitConverter.ToInt32(bytes, tokenOffset + 4) == moduleIndex) result.tokens[BitConverter.ToInt32(bytes, tokenOffset)] = address;
                }
                result.Available = true;
            }
            catch (Exception error) { result.Error = error.GetBaseException().Message; result.Available = false; }
            return result;
        }
        public NativeAddressAssessment Inspect(string assemblyName, int token)
        {
            if (!Available) return new NativeAddressAssessment { Status = "database-unavailable" };
            if (assemblyName != "Modules-ASM") return new NativeAddressAssessment { Status = "assembly-unverified" };
            if (!tokens.TryGetValue(token, out long address)) return new NativeAddressAssessment { Status = "address-missing" };
            int occurrences = aliases[address];
            return new NativeAddressAssessment { Status = occurrences == 1 ? "unique-address" : "shared-native-address", RVA = address, Aliases = occurrences };
        }
    }
}
