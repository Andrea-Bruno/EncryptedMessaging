using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunicationChannel;

namespace EncryptedMessaging
{
    /// <summary>
    /// This class allows all technical operations on chat contacts. The contact system is based on cryptographic key pairs (public or private). A contact can represent a single person or a group, in fact a contact is made up of the collection of all the public keys of the contacts, this set of keys with a cryptographic calculation generates the ChatId (an id that identifies the contact that the group of conversation). Contacts formed by a single person is actually a group between two users who communicate with each other, for this reason there are no groups with less than two public cryptographic keys. In cryptography, the public key is used to send an encrypted message to whoever owns the private key (the only one that can allow the message to be decrypted, this makes the messaging system extremely secure).
    /// Each user therefore has a public and private key(the public one is used by interlocutors to encrypt messages, the private one to decrypt incoming messages). The contact also has an ID that is calculated with a computational operation on the public key, so whoever knows the public keys of a contact can also trace his ID.Since there may be groups with the same members but with a different theme, the group name also contributes to the computation of the chat ID.
    /// </summary>
    public class ContactConverter
    {
        /// <summary>
        /// Set the context to readonly.
        /// </summary>
        /// <param name="context">Context</param>
        public ContactConverter(Context context) => _context = context;
        private readonly Context _context;

        /// <summary>
        /// From the public key he obtains the user ID, a unique number represented by 8 bytes (ulong)
        /// For privacy reasons this algorithm is not reversible: From the public key we can obtain the user ID but it is not possible to trace the public key by having the user ID
        /// </summary>
        /// <param name="publicKey"></param>
        /// <returns></returns>
        public static ulong GetUserId(byte[] publicKey)
        {
            var hashBytes = CryptoServiceProvider.ComputeHash(publicKey);
            return Converter.BytesToUlong(hashBytes);
        }

        /// <summary>
        /// This function obtains the list of participants from a string that represents everyone's public key
        /// </summary>
        /// <param name="publicKeys">string that represents everyone's public key</param>
        /// <param name="participants">The list of public keys of the chat participants</param>
        /// <returns>Boolean</returns>



        public bool PublicKeysToParticipants(string publicKeys, out List<byte[]> participants)
        {
            participants = new List<byte[]>();
            try
            {
                var keyLen = 44;
                if (publicKeys.Length == 0 || (publicKeys.Length % keyLen) != 0)
                    return false;
                var nParticipants = publicKeys.Length / keyLen;
                var keys = new List<string>();
                for (var n = 0; n < nParticipants; n++)
                {
                    keys.Add(publicKeys.Substring(keyLen * n, keyLen));
                }
                foreach (var key in keys)
                    participants.Add(Convert.FromBase64String(key));
            }
            catch (Exception)
            {
                return false;
            }
            NormalizeParticipants(ref participants);
            return true;
        }

        /// <summary>
        /// Boolean check for validating key.
        /// </summary>
        /// <param name="participants">The list of public keys of the chat participants</param>
        /// <param name="publicKeys">Public Key</param>
        /// <param name="removeMyKey">Remove Key</param>
        /// <returns></returns>
        public bool ParticipantsToPublicKeys(List<byte[]> participants, out string publicKeys, bool removeMyKey = false)
        {
            var participantsClone = participants.ToList(); // We use a clone to prevent errors on other threads interacting with the collection at the same time
            NormalizeParticipants(ref participantsClone, removeMyKey);
            publicKeys = "";
            foreach (var participant in participantsClone)
            {
                var key = Convert.ToBase64String(participant);
                if (ValidateKey(key))
                    publicKeys += key;
                else
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Calculate the hash id of the contact. For groups, the name also comes into play in the computation because there can be groups with the same participants but different names
        /// </summary>
        /// <param name="participants">The list of public keys of the chat participants. Participants must be at least 2 (the two people speaking to each other)</param>
        /// <param name="name">The name parameter must only be passed for groups, because there are groups with the same members but different names. The communication between you and another is made up of 2 participants, as groups we mean chats with more than 2 participants: If there are more than 2 participants, the name is mandatory in order to distinguish different groups but with the same members.</param>
        /// <returns>Unisgned Integer</returns>
        public static ulong ParticipantsToChatId(IEnumerable<byte[]> participants, string name = null)
        {
            var participantsClone = participants.ToList(); // So there is no error if the list is changed externally during the sort process
            var table = ParticipantListToIdTable(participantsClone);
            return UserIdsToChatId(table.Select(x => x.UserId), name);
            // Old algorithm:
            //var pts = Array.Empty<byte>();
            //if (participantsClone.Count > 2)
            //    pts = name.GetBytes();
            //participantsClone.ForEach(x => pts = pts.Combine(x));
            //var hashBytes = CryptoServiceProvider.ComputeHash(pts);
            //return Converter.BytesToUlong(hashBytes.Take(8));
        }

        /// <summary>
        /// Get chat id from a list of chat members' user ids
        /// </summary>
        /// <param name="Ids">The ids of the chat members</param>
        /// <param name="name">The name parameter must only be passed for groups, because there are groups with the same members but different names. The communication between you and another is made up of 2 participants, as groups we mean chats with more than 2 participants: If there are more than 2 participants, the name is mandatory in order to distinguish different groups but with the same members.</param>
        /// <returns></returns>
        public static ulong UserIdsToChatId(IEnumerable<ulong> Ids, string name = null)
        {
            var result = 0ul;
            var count = 0;
            foreach (var id in Ids)
            {
                result ^= id;
                count++;
            }
            // Groups with more than 2 members must have a name so that different groups with the same members can be distinguished
            if (count > 2 && name != null)
                result ^= GetUserId(name.GetBytes());
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="participants">The list of public keys of the chat participants</param>
        /// <returns></returns>
        public static bool ValidateKeys(List<byte[]> participants)
        {
            if (participants == null)
                return false;
            try
            {
                using var csp = new CryptoServiceProvider();
                foreach (var key in participants)
                {
                    csp.ImportCspBlob(key);
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        /// <summary>
        /// Boolean check for partipants public keys.
        /// </summary>
        /// <param name="keys">Key</param>
        /// <returns>Boolean</returns>
        public bool ValidateKeys(string keys) => PublicKeysToParticipants(keys, out var participants) && ValidateKeys(participants);

        /// <summary>
        /// This will check if the basekey is valid , if not key is converted from base 64 key.
        /// </summary>
        /// <param name="base64Key"></param>
        /// <returns>key</returns>
        public static bool ValidateKey(string base64Key)
        {
            if (base64Key == null)
                return false;
            if (base64Key.Length != 44)
                return false;
            try
            {
                var key = Convert.FromBase64String(base64Key);
                return ValidateKey(key);
            }
            catch (FormatException ex)
            {
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Validates the key provided.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static bool ValidateKey(byte[] key)
        {
            if (key == null)
                return false;
            try
            {
                using var csp = new CryptoServiceProvider();
                csp.ImportCspBlob(key);
                return csp.IsValid();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Remove they key if it is not null and assign a new Public key Binary for empty values.
        /// </summary>
        /// <param name="participants">The list of public keys of the chat participants</param>
        /// <param name="removeMyKey">Byte array</param>
        public void NormalizeParticipants(ref List<byte[]> participants, bool removeMyKey = false)
        {
            var myKey = _context.My.GetPublicKeyBinary();
            var list = participants.FindAll(x => !x.SequenceEqual(myKey)); // remove my key
            participants.Clear();
            while (list.Count > 0) // remove duplicate
            {
                var notExists = participants.Find(x => x.SequenceEqual(list[0])) == null;
                if (notExists)
                {
                    participants.Add(list[0]);
                }
                list.RemoveAt(0);
            }
            if (!removeMyKey) // my contact has been removed but it is not required
            {
                participants.Add(myKey);
            }
            SortParticipants(ref participants);
        }

        /// <summary>
        /// Sort the list of participants according to their ID, and returns the table of the correspondence between the public key and the participant's ID
        /// </summary>
        /// <param name="participants">The list of public keys of the chat participants</param>
        public static void SortParticipants(ref List<byte[]> participants)
        {
            var table = ParticipantListToIdTable(participants);
            participants.Clear();
            participants.AddRange(table.OrderBy(o => o.UserId).Select(x => x.PublicKey).ToList());
        }

        public static List<ParticipantIdTable> ParticipantListToIdTable(List<byte[]> participants)
        {
            var table = new List<ParticipantIdTable>();
            participants.ForEach(x => table.Add(new ParticipantIdTable(x)));
            return table;
        }

        public class ParticipantIdTable
        {
            public ParticipantIdTable(byte[] publicKey)
            {
                PublicKey = publicKey;
                UserId = GetUserId(publicKey);

            }
            public byte[] PublicKey;
            public ulong UserId;
        }

        /// <summary>
        /// Change the partipants to their specific User Ids.
        /// </summary>
        /// <param name="participants">Partipants</param>
        /// <param name="context">Context</param>
        /// <returns>User Id</returns>
        public static List<ulong> ParticipantsToUserIds(List<byte[]> participants, Context context)
        {
            var participantsClone = participants.ToList(); // We use a clone to prevent errors on other threads interacting with the collection at the same time
            context.ContactConverter.NormalizeParticipants(ref participantsClone);
            var list = new List<ulong>();
            foreach (var participant in participantsClone)
                list.Add(GetUserId(participant));
            return list;
        }
    }
}