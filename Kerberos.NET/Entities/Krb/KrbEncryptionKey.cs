﻿using Kerberos.NET.Crypto;

namespace Kerberos.NET.Entities
{
    public partial class KrbEncryptionKey
    {
        public KerberosKey AsKey(KeyUsage? usage = null)
        {
            return new KerberosKey(this) { Usage = usage };
        }

        public KeyUsage Usage { get; set; }

        public static KrbEncryptionKey Generate(EncryptionType type)
        {
            var crypto = CryptoService.CreateTransform(type);

            return new KrbEncryptionKey
            {
                EType = type,
                KeyValue = crypto.GenerateKey()
            };
        }
    }
}
