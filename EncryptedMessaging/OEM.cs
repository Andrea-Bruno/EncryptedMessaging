using System;
using NBitcoin;
using NBitcoin.RPC;

namespace EncryptedMessaging
{
    /// <summary>
    /// Class that is used to authenticate licenses
    /// </summary>
    public class OEM
    {
        /// <summary>
        /// This initializer allows you to set the OEM's digital signature algorithm for license verification. Theoretically it is possible to set an algorithm that works with API remotely directly at the OEM making validation an intrinsically safe procedure.
        /// </summary>
        /// <param name="signLogin"></param>
        /// <param name="idOEM">The OEM license id</param>
        public OEM(Func<byte[], byte[]> signLogin, ulong idOEM)
        {
            IdOEM = idOEM;
            SignLogin = signLogin;
        }

        /// <summary>
        /// This initializer creates a license authenticator using the OEM private key. OEM private key sharing is not an intrinsically secure system, it is preferable that signatures are remotely placed directly by the OEM. When the device connects, authentication will be requested via the OEM's digital signature and the validity of the license will be checked. If the license has expired or the OEM is invalid, the connection will be denied.
        /// </summary>
        /// <param name="licenseOEM">The OEM private key to activate the licenses</param>
        public OEM(string licenseOEM)
        {
            var licenseOEMBytes = Convert.FromBase64String(licenseOEM);
            var privateKey = new Key(licenseOEMBytes);
            var pubKey = privateKey.PubKey;
            IdOEM = BitConverter.ToUInt64(pubKey.ToBytes(), 0);
            SignLogin = hash266 => { var hash = new uint256(hash266); var sign = privateKey.Sign(hash); return sign.ToDER(); };
        }

        /// <summary>
        /// Calculate the license ID having the private key
        /// </summary>
        /// <param name="licenseOEM"></param>
        /// <returns></returns>
        static public ulong GetIdOEM(string licenseOEM)
        {
            if (string.IsNullOrEmpty(licenseOEM))
                return 0;
            var licenseOEMBytes = Convert.FromBase64String(licenseOEM);
            var privateKey = new Key(licenseOEMBytes);
            var pubKey = privateKey.PubKey;
            return BitConverter.ToUInt64(pubKey.ToBytes(), 0);
        }

        /// <summary>
        /// Use this initiator to initialize a client that does not require license authentication but can only communicate with authenticated device servers. For example, in a Cloud system the license may be mandatory only for servers, while clients do not need it.
        /// </summary>
        /// <param name="idOEM">The OEM id</param>
        public OEM(ulong idOEM)
        {
            IdOEM = idOEM;
        }

        /// <summary>
        /// The OEM license id
        /// </summary>
        public readonly ulong IdOEM;

        /// <summary>
        /// Function for authentication on the router. The function digitally signs the OEM to authenticate access and create and activate the license on the router.
        /// </summary>
        public readonly Func<byte[], byte[]> SignLogin;
    }
}
