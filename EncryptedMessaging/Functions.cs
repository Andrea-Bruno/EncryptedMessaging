using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EncryptedMessaging.Resources;
using NBitcoin;
using System.Runtime.InteropServices;
using System.Net.Http;

namespace EncryptedMessaging
{
    /// <summary>
    /// 
    /// </summary>
    public static class Functions
    {
        /// <summary>
        /// Get if internet connection is active and working
        /// </summary>
        /// <returns>true or false</returns>
        public static bool IsInternetAvailable()
        {
            // this version don't work in iOS (mobile)
            byte[] addressBytes = { 1, 1, 1, 1 }; // Cloudflare
            IPAddress[] ipsDns = { GetDnsAddress(), new IPAddress(addressBytes) };
            using (Ping ping = new Ping())
            {
                foreach (IPAddress ipDns in ipsDns)
                {
                    if (ipDns != null)
                    {
                        try
                        {
                            PingReply reply = ping.Send(ipDns, 1000);
                            if (reply.Status == IPStatus.Success)
                                return true;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Alternative Method for iOS
                string[] domains = { "apple.com", "icloud.com" }; // Cloudflare
                using (var client = new MyWebClient())
                {
                    foreach (var domain in domains)
                    {
                        try
                        {
                            using (client.OpenRead("http://" + domain))
                                return true;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return false;
        }

        private static bool PingHost(Uri address)
        {

            try
            {
                using (var client = new MyWebClient())
                using (client.OpenRead(address))
                    return true;
            }
            catch
            {
                return false;
            }

            // this version don't work in iOS
            //var pingable = false;
            //Ping pinger = null;
            //try
            //{
            //	pinger = new Ping();
            //	PingReply reply = pinger.Send(nameOrAddress);
            //	pingable = reply.Status == IPStatus.Success;
            //}
            //catch (PingException)
            //{
            //	// Discard PingExceptions and return false;
            //}
            //finally
            //{
            //	if (pinger != null)
            //	{
            //		pinger.Dispose();
            //	}
            //}
            //return pingable;
        }



        /// <summary>
        /// Get DNS Service IP
        /// </summary>
        /// <returns>IPAddress</returns>
        public static IPAddress GetDnsAddress()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties properties = ni.GetIPProperties();
                    foreach (IPAddress dns in properties.DnsAddresses)
                    {
                        return dns;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// encrypt the user input for the password.
        /// </summary>
        /// <param name="input">user input</param>
        /// <param name="password">Byte array</param>
        /// <returns></returns>
        public static byte[] Encrypt(byte[] input, byte[] password)
        {
            var pdb = new PasswordDeriveBytes(password, new byte[] { 0x43, 0x87, 0x23, 0x72 });
            var ms = new MemoryStream();
            var aes = new AesManaged();
            aes.Key = pdb.GetBytes(aes.KeySize / 8);
            aes.IV = pdb.GetBytes(aes.BlockSize / 8);
            var cs = new CryptoStream(ms,
                aes.CreateEncryptor(), CryptoStreamMode.Write);
            cs.Write(input, 0, input.Length);
            cs.Close();
            return ms.ToArray();
        }

        /// <summary>
        /// Decrypt the password
        /// </summary>
        /// <param name="input">User input</param>
        /// <param name="password">Byte array</param>
        /// <returns></returns>
        public static byte[] Decrypt(byte[] input, byte[] password)
        {
            var pdb = new PasswordDeriveBytes(password, new byte[] { 0x43, 0x87, 0x23, 0x72 });
            var ms = new MemoryStream();
            var aes = new AesManaged();
            aes.Key = pdb.GetBytes(aes.KeySize / 8);
            aes.IV = pdb.GetBytes(aes.BlockSize / 8);
            var cs = new CryptoStream(ms,
                aes.CreateDecryptor(), CryptoStreamMode.Write);
            cs.Write(input, 0, input.Length);
            cs.Close();
            return ms.ToArray();
        }

        public static int BytesCompare(IList<byte> x, IList<byte> y)
        {
            for (var index = 0; index < Math.Min(x.Count, y.Count); index++)
            {
                var result = x[index].CompareTo(y[index]);
                if (result != 0) return result;
            }
            return x.Count.CompareTo(y.Count);
        }

        /// <summary>
        /// Set the date to relative if same return null.
        /// </summary>
        /// <param name="date">Instant in time</param>
        /// <returns></returns>
        public static string DateToRelative(DateTime date)
        {
            if (date == DateTime.MinValue)
                return null;
            var timeSpan = DateTime.UtcNow - date;
            return timeSpan.TotalDays >= 2 ? ((int)timeSpan.TotalDays).ToString(CultureInfo.InvariantCulture) + " " + Dictionary.Days + " " + Dictionary.Ago
                     : timeSpan.TotalDays >= 1 ? ((int)timeSpan.TotalDays).ToString(CultureInfo.InvariantCulture) + " " + Dictionary.Day + " " + Dictionary.Ago
                     : timeSpan.TotalHours >= 2 ? ((int)timeSpan.TotalHours).ToString(CultureInfo.InvariantCulture) + " " + Dictionary.Hours + " " + Dictionary.Ago
                     : timeSpan.TotalHours >= 1 ? ((int)timeSpan.TotalHours).ToString(CultureInfo.InvariantCulture) + " " + Dictionary.Hour + " " + Dictionary.Ago
                     : timeSpan.TotalMinutes >= 5 ? ((int)timeSpan.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " " + Dictionary.Minutes + " " + Dictionary.Ago
                     : timeSpan.TotalMinutes >= 0 ? Dictionary.JustNow : null;
        }

        /// <summary>
        /// split the data and create new.
        /// </summary>
        /// <param name="data">Combined packages</param>
        /// <param name="offset">The offset where to start</param>
        /// <param name="pointer"></param>
        /// <returns></returns>
        public static List<byte[]> SplitDataWithZeroEnd(byte[] data, int offset, out int pointer)
        {
            var datas = new List<byte[]>();
            int len = data[offset];
            do
            {
                offset += 1;
                var part = new byte[len];
                Buffer.BlockCopy(data, offset, part, 0, len);
                datas.Add(part);
                offset += len;
                if (offset < data.Length)
                {
                    len = data[offset];
                    if (len == 0)
                        offset++;
                }
                else
                {
                    len = 0;
                }
            } while (len != 0);
            pointer = offset;
            return datas;
        }


        /// <summary>
        /// Divide merged data packets with join function
        /// </summary>
        /// <param name="data">Combined packages</param>
        /// <param name="lenAsByte">Use the same value used with the join function</param>
        /// <param name="offset">The offset where to start</param>
        /// <returns></returns>
        public static List<byte[]> SplitData(byte[] data, bool lenAsByte = true, int offset = 0)
        {
            var datas = new List<byte[]>();
            while (offset < data.Length)
            {
                int len;
                if (lenAsByte)
                {
                    len = data[offset];
                    offset++;
                }
                else
                {
                    len = BitConverter.ToInt32(data, offset);
                    offset += 4;
                }
                var part = new byte[len];
                Buffer.BlockCopy(data, offset, part, 0, len);
                datas.Add(part);
                offset += len;
            }
            return datas;
        }


        /// <summary>
        /// Join data packets
        /// </summary>
        /// <param name="lenAsByte">If true, packets must be smaller than 256 bytes</param>
        /// <param name="values">packages to join</param>
        /// <returns></returns>
        public static byte[] JoinData(bool lenAsByte, params byte[][] values)
        {
            var data = Array.Empty<byte>();
            if (values != null)
                foreach (var value in values)
                {
                    var v = value ?? Array.Empty<byte>();
#if DEBUG
                    if (lenAsByte && value.Length >= 256)
                        Debugger.Break(); // It is not allowed to send data greater than 255 bytes
#endif
                    data = lenAsByte ? data.Combine(new[] { (byte)v.Length }, v) : data.Combine(v.Length.GetBytes(), v);
                }
            return data;
        }

        /// <summary>
        /// Validate the passphrase, if wrong return false.
        /// </summary>
        /// <param name="passphrase"></param>
        /// <returns></returns>
        public static bool PassphraseValidation(string passphrase)
        {
            try
            {
                passphrase = passphrase.Trim();
                passphrase = passphrase.Replace(",", " ");
                passphrase = Regex.Replace(passphrase, @"\s+", " ");
                var words = passphrase.Split(' ');
                if (words.Length >= 12)
                {
                    passphrase = passphrase.ToLower();
                    var mnemo = new Mnemonic(passphrase, Wordlist.English);
                    return mnemo.IsValidChecksum;
                }

                if (words.Length == 1)
                {
                    return (Convert.FromBase64String(passphrase).Length == 32);
                }
            }
            catch (Exception) { }
            return false;

        }

        /// <summary>
        /// Converts the value of a specified Unicode character to its uppercase equivalent using specified culture-specific formatting information.
        /// </summary>
        /// <param name="text"></param>
        /// <returns> The uppercase equivalent of c, modified according to culture, or the unchanged value of c if c is already uppercase, has no uppercase equivalent, or is notalphabetic.</returns>
        public static string FirstUpper(string text)
        {
            var value = "";
            if (string.IsNullOrEmpty(text)) return value;
            var last = false;
            foreach (var c in text)
            {
                if (char.IsLetter(c))
                {
                    if (!last)
                        value += char.ToUpper(c, CultureInfo.InvariantCulture);
                    else
                        value += c;
                    last = true;
                }
                else
                {
                    last = false;
                    value += c;
                }
            }
            return value;
        }


        /// <summary>
        /// Split arrays of incoming data
        /// </summary>
        /// <param name="data">Array of data to split</param>
        /// <param name="smallValue">If true, the format supports values no larger than 256 bytes</param>
        /// <returns>Key value collection</returns>
        public static Dictionary<byte, byte[]> SplitIncomingData(byte[] data, bool smallValue)
        {
            var values = SplitData(data, smallValue);
            var keyValue = new Dictionary<byte, byte[]>();
            if (values?.Count > 0)
            {
                //Read key value
                var n = 0;
                do
                {
                    var key = values[n][0];
                    n++;
                    var value = values[n];
                    n++;
                    keyValue.Add(key, value);
                } while (n < values.Count);
            }
            return keyValue;
        }

        /// <summary>
        /// Convert byte array to its equivalent string representation that is encoded with uppercase hex characters.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string ToHex(this byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// Convert hex character to its equivalent byte array.
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        public static byte[] HexToBytes(this string hex)
        {
            var NumberChars = hex.Length;
            var bytes = new byte[NumberChars / 2];
            for (var i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        /// <summary>
        /// Convert byte array to base 64 url.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string ToBase64Url(this byte[] bytes)
        {
            var returnValue = Convert.ToBase64String(bytes).TrimEnd(padding).Replace('+', '-').Replace('/', '_');
            return returnValue;
        }

        /// <summary>
        /// Convert from base 64 url to byte array
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static byte[] FromBase64Url(this string text)
        {
            var incoming = text.Replace('_', '/').Replace('-', '+');
            switch (text.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            var bytes = Convert.FromBase64String(incoming);
            //string originalText = Encoding.ASCII.GetString(bytes);
            return bytes;
        }

        private static readonly char[] padding = { '=' };

        /// <summary>
        /// Convert byte to firebase token
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string BitesToFirebaseToken(byte[] bytes)
        {
            var array1 = new byte[bytes.Length - 105];
            var array2 = new byte[105];
            Array.Copy(bytes, 0, array1, 0, bytes.Length - 105);
            Array.Copy(bytes, bytes.Length - 105, array2, 0, 105);
            var part1 = ToBase64Url(array1);
            part1 = part1.Substring(0, part1.Length - 1);
            var part2 = ToBase64Url(array2);
            var result = part1 + ":" + part2;
            return result;
        }

        /// <summary>
        /// Convert firebase token to byte array.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public static byte[] FirebaseTokenToBytes(string token)
        {
            var parts = token.Split(':');
            var part1 = parts[0];
            var part2 = parts[1];
            var array1 = FromBase64Url(part1 + "0");
            var array2 = FromBase64Url(part2);
            var result = new byte[array1.Length + array2.Length];
            array1.CopyTo(result, 0);
            array2.CopyTo(result, array1.Length);
            return result;
        }


        private class MyWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri uri)
            {
                var w = base.GetWebRequest(uri);
                w.Timeout = 2 * 1000;
                return w;
            }
        }


        private static bool _disallowTrySwitchOnConnectivity;
        internal static void TrySwitchOnConnectivity()
        {
            if (Context.CurrentConnectivity == false && _disallowTrySwitchOnConnectivity == false)
            {
                if (IsInternetAvailable())
                    Context.OnConnectivityChange(true);
                else
                    _disallowTrySwitchOnConnectivity = true;
            }
            if (Context.CurrentConnectivity == true)
                _disallowTrySwitchOnConnectivity = false;
        }

        internal static Guid CallerAssemblyId()
        {
            Assembly callerAssembly = null;
            var thisAssembly = Assembly.GetExecutingAssembly();
            // return the first assembly in the stack, outside of this
            var sf = new StackTrace().GetFrames().FirstOrDefault(x => { callerAssembly = x.GetMethod()?.DeclaringType?.Assembly; return callerAssembly != thisAssembly; });
            return callerAssembly.ManifestModule.ModuleVersionId;
        }

        /// <summary>
        /// Run a command in the operating system environment
        /// </summary>
        /// <param name="command">Command to execute</param>
        /// <param name="parameters">Parameters</param>
        /// <param name="useShellExecute">True if the shell should be used when starting the process; false if the process should be created directly from the executable file.</param>
        /// <returns>Result of command if the command generates an output</returns>
        public static string ExecuteCommand(string command, string parameters, bool useShellExecute = false)
        {
            Process process = new Process();
            process.StartInfo.FileName = command;
            process.StartInfo.Arguments = parameters; // Note the /c command (*)
            process.StartInfo.UseShellExecute = useShellExecute;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.RedirectStandardOutput = !useShellExecute;
            process.StartInfo.CreateNoWindow = true;
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            //    process.StartInfo.Verb = "runas";
            process.Start();
            if (useShellExecute)
            {
                process.WaitForExit();
                return null;
            }
            string output = process.StandardOutput.ReadToEnd();
            return output;
        }
    }
}
