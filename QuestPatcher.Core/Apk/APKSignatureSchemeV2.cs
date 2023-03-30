using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuestPatcher.Core.Apk
{
    public class APKSignatureSchemeV2
    {

        public class Signer
        {
            public class BlockSignedData
            {
                public class Digest
                {

                    public uint SignatureAlgorithmID { get; private set; }
                    public byte[] Data { get; private set; }

                    public Digest(uint signatureAlgorithmID, byte[] data)
                    {
                        SignatureAlgorithmID = signatureAlgorithmID;
                        Data = data;
                    }

                    public int Length()
                    {
                        return 4 + 4 + 4 + Data.Length;
                    }

                    public void Write(FileMemory memory)
                    {
                        memory.WriteUInt((uint) Length() - 4);
                        memory.WriteUInt(SignatureAlgorithmID);
                        memory.WriteUInt((uint) Data.Length);
                        memory.WriteBytes(Data);
                    }
                }

                public class AdditionalAttribute
                {

                    public uint ID { get; private set; }
                    public int Value { get; private set; }
                    public byte[]? Data { get; private set; }

                    public AdditionalAttribute(uint id, int value)
                    {
                        ID = id;
                        Value = value;
                        Data = null;
                    }

                    public AdditionalAttribute(uint id, byte[] value)
                    {
                        ID = id;
                        Value = value.Length;
                        Data = value;
                    }

                    public int Length()
                    {
                        return 4 + 4 + (Data?.Length ?? 4);
                    }

                    public void Write(FileMemory memory)
                    {
                        memory.WriteUInt((uint) Length() - 4);
                        memory.WriteUInt(ID);
                        if(Data == null)
                        {
                            memory.WriteInt(Value);
                        }
                        else
                        {
                            memory.WriteBytes(Data);
                        }
                    }
                }

                public List<Digest> Digests { get; private set; }
                public List<byte[]> Certificates { get; private set; }
                public List<AdditionalAttribute> AdditionalAttributes { get; private set; }

                public BlockSignedData()
                {
                    Digests = new List<Digest>();
                    Certificates = new List<byte[]>();
                    AdditionalAttributes = new List<AdditionalAttribute>();
                }

                public int Length()
                {
                    return 4 + Digests.Sum(value => value.Length()) + 4 + Certificates.Count * 4 + Certificates.Sum(value => value.Length) + 4 + AdditionalAttributes.Sum(value => value.Length());
                }

                public void Write(FileMemory memory)
                {
                    memory.WriteUInt((uint) Digests.Sum(value => value.Length()));
                    Digests.ForEach(value => value.Write(memory));

                    memory.WriteUInt((uint) (Certificates.Count * 4 + Certificates.Sum(value => value.Length)));
                    Certificates.ForEach(value => {
                            memory.WriteUInt((uint) value.Length);
                            memory.WriteBytes(value);
                        }
                    );

                    memory.WriteUInt((uint) AdditionalAttributes.Sum(value => value.Length()));
                    AdditionalAttributes.ForEach(value => value.Write(memory));
                }
            }

            public class BlockSignature
            {
                public uint SignatureAlgorithmID { get; private set; }
                public byte[] Data { get; private set; }

                public BlockSignature(uint signatureAlgorithmID, byte[] data)
                {
                    SignatureAlgorithmID = signatureAlgorithmID;
                    Data = data;
                }

                public int Length()
                {
                    return 4 + 4 + 4 + Data.Length;
                }

                public void Write(FileMemory memory)
                {
                    memory.WriteUInt((uint) Length() - 4);
                    memory.WriteUInt(SignatureAlgorithmID);
                    memory.WriteUInt((uint) Data.Length);
                    memory.WriteBytes(Data);
                }
            }

            public byte[]? SignedData { get; set; }
            public List<BlockSignature> Signatures { get; private set; }
            public byte[]? PublicKey { get; set; }

            public Signer() {
                SignedData = null;
                Signatures = new List<BlockSignature>();
                PublicKey = null;
            }

            public int Length()
            {
                return 4 + 4 + (SignedData?.Length ?? 0) + 4 + Signatures.Sum(value => value.Length()) + 4 + (PublicKey?.Length ?? 0);
            }

            public void Write(FileMemory memory)
            {
                memory.WriteUInt((uint) Length() - 4);
                if(SignedData == null)
                {
                    memory.WriteUInt(0);
                }
                else
                {
                    memory.WriteUInt((uint) SignedData.Length);
                    memory.WriteBytes(SignedData);
                }

                memory.WriteUInt((uint) (Signatures.Sum(value => value.Length())));
                Signatures.ForEach(value => value.Write(memory));

                if(PublicKey == null)
                {
                    memory.WriteUInt(0);
                }
                else
                {
                    memory.WriteUInt((uint) PublicKey.Length);
                    memory.WriteBytes(PublicKey);
                }
            }
        }

        public static readonly uint ID = 0x7109871a;

        public List<Signer> Signers { get; private set; }

        public APKSignatureSchemeV2()
        {
            Signers = new List<Signer>();
        }

        public void Write(FileMemory memory)
        {
            memory.WriteUInt((uint)Signers.Sum(value => value.Length()));
            Signers.ForEach(value => value.Write(memory));
        }

        public APKSigningBlock.IDValuePair ToIDValuePair()
        {
            using MemoryStream ms = new MemoryStream();
            using FileMemory memory = new FileMemory(ms);
            Write(memory);
            return new APKSigningBlock.IDValuePair(ID, ms.ToArray());
        }

    }
}
