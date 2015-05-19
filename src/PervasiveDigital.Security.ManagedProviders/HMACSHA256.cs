using System;
using Microsoft.SPOT;
using System.Text;

namespace PervasiveDigital.Security.ManagedProviders
{
    public class HMACSHA256
    {
        private byte[] _key = new byte[64];

        public HMACSHA256()
        {
            var r = new Random();
            r.NextBytes(_key);
        }

        public HMACSHA256(string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            Initialize(keyBytes);
        }

        public HMACSHA256(byte[] key)
        {
            Initialize(key);
        }

        private void Initialize(byte[] keyData)
        {
            Array.Clear(_key,0,_key.Length);
            // If the key is too long, use the hash of the key
            if (keyData.Length>_key.Length)
            {
                var hashAlgo = new Sha256();
                hashAlgo.AddData(keyData, 0, (uint)keyData.Length);
                keyData = hashAlgo.GetHash();
            }
            Array.Copy(keyData, _key, keyData.Length);
        }

        public byte[] Key
        {
            get { return _key; }
        }

        public byte[] ComputeHash(string data)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            return ComputeHash(dataBytes);
        }

        public byte[] ComputeHash(byte[] data)
        {
            byte[] ipad = new byte[64];
            byte[] opad = new byte[64];

            var hash = new Sha256();
            Array.Copy(_key, ipad, _key.Length);
            Array.Copy(_key, opad, _key.Length);

            for (int i = 0; i < 64 ; ++i)
            {
                ipad[i] ^= 0x36;
                opad[i] ^= 0x5c;
            }

            hash.AddData(ipad, 0, (uint)ipad.Length);
            if (data.Length>0)
                hash.AddData(data, 0, (uint)data.Length);
            var ipadHash = hash.GetHash();

            hash = new Sha256();

            hash.AddData(opad, 0, (uint)opad.Length);
            hash.AddData(ipadHash, 0, (uint)ipadHash.Length);

            return hash.GetHash();
        }
    }
}
