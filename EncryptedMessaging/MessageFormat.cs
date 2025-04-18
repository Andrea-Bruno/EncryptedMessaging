﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using EncryptedMessaging.Resources;
using SecureStorage;
using static CommunicationChannel.Converter;

namespace EncryptedMessaging
{
    //========================================================================================================
    //Here are all the functions necessary to process messages in binary format, create messages and read them
    //========================================================================================================
    /// <summary>
    ///Here are all the functions necessary to process messages in binary format, create messages and read them.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// Message properties.
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="contact">Recipient</param>
        /// <param name="type">Type</param>
        /// <param name="author">Sender</param>
        /// <param name="creation">Date/time of sending</param>
        /// <param name="data">Byte array</param>
        /// <param name="receptionTime">Date/time of delivery</param>
        /// <param name="postId">Unsigned long integer</param>
        /// <param name="encrypted">Boolean</param>
        /// <param name="chatId">Unsigned long integer</param>
        /// <param name="authorId">Unsigned long integer</param>
        public Message(Context context, Contact contact, MessageFormat.MessageType type, byte[] author, DateTime creation, byte[] data, DateTime receptionTime, ulong postId, bool encrypted, ulong chatId, ulong authorId)
        {
            Context = context;
            Contact = contact;
            Author = author;
            Creation = creation;
            ReceptionTime = receptionTime;
            PostId = postId;
            Encrypted = encrypted;
            ChatId = chatId;
            _AuthorId = authorId;
            if (type == MessageFormat.MessageType.ReplyToMessage)
            {
                Type = (MessageFormat.MessageType)data[0];
                ReplyToPostId = BitConverter.ToUInt64(data, 1);
                Data = Bytes.Skip(data, 9);
            }
            else
            {
                Type = type;
                Data = data;
            }
        }
        /// <summary>
        /// Context implementation.
        /// </summary>
        public  Context Context { get; private set; }
        /// <summary>
        /// The chat group.
        /// </summary>
        public Contact Contact { get; set; }
        /// <summary>
        /// The public key of the author.
        /// </summary>
        public MessageFormat.MessageType Type { get; internal set; }
        private readonly ulong _AuthorId;
        /// <summary>
        /// The author Id NOTE: This value is setting only in not encrypted message!
        /// </summary>
        public ulong AuthorId
        {
            get
            {
                if (!Encrypted)
                    return _AuthorId;
                var author = Context.Contacts.GetParticipant(Author);
                if (author == null)
                    return (uint)author.UserId;
                return 0;
            }
        }
        /// <summary>
        /// Unique identification number of the chat.
        /// </summary>
        public ulong ChatId { get; private set; }
        internal readonly byte[] Author;
        /// <summary>
        /// This array is the identifier of the chat participant.
        /// If you want to get the corresponding contact, you have to use the function Contacts.GetParticipant(Author), If the contact is not in the address book you receive a null value
        /// </summary>
        public byte[] GetAuthor() => Author;
        /// <summary>
        /// If the sender is present in the address book, it returns its name, otherwise it invents a name.
        /// If the message is encrypted this value is null, then use AuthorId to know the author.
        /// </summary>
        /// <returns></returns>
        public string AuthorName() => Context.Contacts.GetParticipantName(Author);
        /// <summary>
        /// Creation date and time UTC format
        /// </summary>
        public DateTime Creation { get; internal set; }
        /// <summary>
        /// Reception date and time UTC format
        /// </summary>
        public DateTime ReceptionTime { get; internal set; }
        internal byte[] Data;
        /// <summary>
        /// If different from null it indicates that this is a reply post previously sent in the group that the contact represents 
        /// </summary>
        public ulong? ReplyToPostId { get; internal set; }
        /// <summary>
        /// Unique identifier that indicates the message in a chat. Use this property in all contexts where you need to identify a message, for example, when replying to a specific message, you are referring to the message with the ID of the message you want to reply to.
        /// </summary>
        public ulong PostId { get; }
        /// <summary>
        /// Returns the message data, however it may return null if the private key is changed or if I contact is deleted
        /// If you don't need messages when rendering the UI, but you think you need them in the future, don't use this function but use GetDataFunction to get the data when you need it (so you will save the ram memory)
        /// </summary>
        /// <returns></returns>
        public byte[] GetData()
        {
            //To save memory, the data is eliminated every time the message is rendered on the visual interface
            //For some types of data, it may be necessary to obtain them again even after rendering on the UI, for example the audio files when the user decides to listen to them
            var savable = MessageFormat.MessageDescription.ContainsKey(Type) && Contact?.IsVisible == true;
            var output = Data;
            if (output == null && savable)
            {
                var chatId = Contact.ChatId;
                var post = Context.Repository.ReadPost(ReceptionTime, chatId);
#if DEBUG
                if (post == null)
                {
                    Debugger.Break();
                    post = Context.Repository.ReadPost(ReceptionTime, chatId);
                }
#endif
                if (Context.MessageFormat.ReadDataPost(post, chatId, ReceptionTime, out var message))
                    output = message.Data;
            }
            if (savable)
                Data = null; //flush memory
            return output;
        }
        /// <summary>
        /// Read the commands and parameters sent by a sub application via <see cref="Messaging.SendCommandToSubApplication(Contact, ushort, ushort, bool, bool, byte[][])"/>
        /// </summary>
        /// <param name="appId">Sub application Id (plugin Id)</param>
        /// <param name="command">Id of the command used in the protocol of the sub application</param>
        /// <param name="parameters">The parameters that were sent with the command</param>
        /// <returns>False if the received message is not of the SubApplicationCommandWithParameters type</returns>
        public bool GetSubApplicationCommandWithParameters(out ushort appId, out ushort command, out List<byte[]> parameters)
        {
            if (Type != MessageFormat.MessageType.SubApplicationCommandWithParameters)
            {
                appId = default;
                command = default;
                parameters = default;
                return false;
            }
            GetSubApplicationCommandWithData(out appId, out command, out var data);
            parameters = Functions.SplitData(data, false);
            return true;
        }
        /// <summary>
        /// Read the commands and data sent by a sub application via <see cref="Messaging.SendCommandToSubApplication(Contact, ushort, ushort, bool, bool, byte[])"/>
        /// </summary>
        /// <param name="appId">Sub application Id (plugin Id)</param>
        /// <param name="command">Id of the command used in the protocol of the sub application</param>
        /// <param name="data">The data that was sent with the command</param>
        /// <returns>False if the received message is not of the SubApplicationCommandWithData type</returns>
        public bool GetSubApplicationCommandWithData(out ushort appId, out ushort command, out byte[] data)
        {
            if (Type != MessageFormat.MessageType.SubApplicationCommandWithData && Type != MessageFormat.MessageType.SubApplicationCommandWithParameters)
            {
                appId = default;
                command = default;
                data = default;
                return false;
            }
            var messageData = GetData();
            appId = BitConverter.ToUInt16(messageData, messageData.Length - 4);
            command = BitConverter.ToUInt16(messageData, messageData.Length - 2);
            data = new byte[messageData.Length - 4];
            Array.Copy(messageData, 0, data, 0, data.Length);
            return true;
        }

        /// <summary>
        /// Read the commands sent by a sub application via <see cref="Messaging.SendCommandToSubApplication(Contact, ushort, ushort, bool, bool, byte[])"/> or <see cref="Messaging.SendCommandToSubApplication(Contact, ushort, ushort, bool, bool, byte[][])"/>
        /// It is an alternative method to the methods GetSubApplicationCommandWithData and GetSubApplicationCommandWithParameters
        /// </summary>
        /// <param name="appId">Sub application Id (plugin Id)</param>
        /// <param name="command">Id of the command used in the protocol of the sub application</param>
        /// <param name="parameters">The parameters that were sent with the command</param>
        /// <returns>False if the received message is not of the SubApplicationCommandWithData type</returns>
        public bool GetSubApplicationCommand(out ushort appId, out ushort command, out List<byte[]> parameters)
        {
            if (Type == MessageFormat.MessageType.SubApplicationCommandWithParameters)
            {
                return (GetSubApplicationCommandWithParameters(out appId, out command, out parameters));
            }
            byte[] data;
            if (GetSubApplicationCommandWithData(out appId, out command, out data))
            {
                parameters = new List<byte[]> { data };
                return true;
            }
            parameters = new List<byte[]>();
            return false;
        }

        /// <summary>
        /// Read messages generated by <see cref="Messaging.ShareEncryptedContent(Contact, string, byte[], string, string)"/> , containing shared and encrypted material on the server. It is used to share Videos or other huge files which cannot be sent with the messaging protocol
        /// </summary>
        /// <param name="contentType">Three characters describing the type of content being shared. Use the three characters of the file expansion, for example: MP4, DOC, PDF, ISO, etc ...</param>
        /// <param name="privateKey">The private key to decrypt the content.</param>
        /// <param name="description">Literal description of content</param>
        /// <param name="serverUrl">The URL (max 256 char) of the server where the shared content resides. The name of the file is not necessary because it is obtained from the private key. If this value is allowed, then the server will be the default one</param>
        public void GetShareEncryptedContentData(out string contentType, out byte[] privateKey, out string description, out string serverUrl)
        {
            if (Type != MessageFormat.MessageType.ShareEncryptedContent)
            {
                TypeNotValidError();
            }
            var data = GetData();
            var dataArray = Functions.SplitData(data);
            contentType = Encoding.ASCII.GetString(dataArray[0]);
            privateKey = dataArray[1];
            description = Encoding.Unicode.GetString(dataArray[2]);
            serverUrl = Encoding.ASCII.GetString(dataArray[2]);
        }

        private void TypeNotValidError() => throw new ArgumentException("This method can be used only for commands of type SubApplication (" + Type + " is not accepted)"); // An unsuitable method was used to read this type of message: Look in the stack trace, what caused this error!: 

        /// <summary>
        /// If you need to access the data after the UI has been drawn, you can get this function and memorize it in your event
        ///  For example, if you have to show a video, or if you have to play audio when the user clicks on the message in the chat
        /// </summary>
        /// <returns></returns>
        public Func<byte[]> GetDataFunction()
        {
            byte[] action() => GetData();
            return action;
        }
        /// <summary>
        /// Delete the post. 
        /// </summary>
        /// <param name="alsoDeleteRemote">Boolean</param>
        public void Delete(bool alsoDeleteRemote = true)
        {
            Context.Repository.DeletePost(ReceptionTime, Contact);
            //bool isLast = false
            //if (isLast)
            //{
            //	Contact.LastMessagePreview = Resources.Dictionary.MessageDeleted;
            //	Contact.LastMessageTime = DateTime.UtcNow;
            //	Contact.Save();
            //}
            if (alsoDeleteRemote)
            {
                Context.Messaging.DeleteMessage(PostId, Contact);
            }
        }

        /// <summary>
        /// Indicates whether this message was forwarded with encryption
        /// </summary>
        public bool Encrypted { get; private set; }

        /// <summary>
        /// Translation set for the chat with the recipent.
        /// </summary>
        public string Translation
        {
            get
            {
                var author = Context.Contacts.GetContactByUserID(AuthorId);
                if (author?.TranslationOfMessages == true)
                    return Context.SecureStorage.Values.Get("t" + PostId, null);

                return null;
            }
            set => Context.SecureStorage.Values.Set("t" + PostId, value);
        }
    }

    /// <summary>
    /// Message settings set for the recpients.
    /// </summary>
    public class MessageFormat
    {
        /// <summary>
        /// Set context for the message format.
        /// </summary>
        /// <param name="context"></param>
        public MessageFormat(Context context)
        {
            _context = context;
        }
        private readonly Context _context;

        /// <summary>
        /// It is a numerator indicating the data type of the message. It is used to indicate what the transmitted or received data refers to. Each message can contain the data in a byte array, and the typo allows you to correctly reconstruct the message by taking the data.
        /// </summary>
        public enum MessageType : byte
        {
            // NOTE: If you add a new type and want to make it visible also in the messages view, then you must also add its description in the _messageDescription function 
            /// <summary>Text in unicode format</summary>
            Text,
            /// <summary>An image</summary>
            Image,
            /// <summary>An audio message mp3</summary>
            Audio,
            /// <summary>A contact to add to the address book</summary>
            Contact,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            AudioCall,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            VideoCall,
            /// <summary>Geographic location</summary>            
            Location,
            /// <summary>PDF document</summary>
            PdfDocument,
            /// <summary>Used to notify that messages have been read</summary>
            LastReading,
            /// <summary>Asks to delete a message</summary>
            Delete,
            /// <summary>Array of bytes[], Each bytes cannot exceed 256 bytes - Use the Functions.SplitIncomingData method to get the array</summary>
            SmallData,
            /// <summary>Array of bytes[] - Use the Functions.SplitIncomingData method to get the array</summary>
            Data,
            /// <summary>Used to send the name with which the contract is registered in my address book, so I can have notifications with the name used locally in my contacts</summary>
            NameChange,
            /// <summary>Used to check the status of the contact</summary>
            ContactStatus,
            /// <summary>Message type that is used in form</summary>
            Binary,
            /// <summary>Used to send simple information (See the InformType enumerator for a list of the information it passes)</summary>
            Inform,
            /// <summary>Contact stored on th phone </summary>
            PhoneContact,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            StartAudioGroupCall,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            StartVideoGroupCall,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            EndCall,
            /// <summary>Obsolete</summary>
            [Obsolete("Is deprecated, to be replaced with SubApplicationCommandWithData")]
            DeclinedCall,
            /// <summary>
            /// Used to send commands for sub applications (plugins, modules, additional features): Each sub application has an ID and a command that must be sent with the messaging protocol.
            /// See: "<see cref="Messaging.SendCommandToSubApplication(EncryptedMessaging.Contact, ushort, ushort, bool, bool, byte[])"/>"
            /// </summary>
            SubApplicationCommandWithData,
            /// <summary>
            /// Used to send commands for sub applications (plugins, modules, additional features): Each sub application has an ID and a command that must be sent with the messaging protocol.
            /// See: "<see cref="Messaging.SendCommandToSubApplication(EncryptedMessaging.Contact, ushort, ushort, bool, bool, byte[][])"/>"
            /// </summary>
            SubApplicationCommandWithParameters,
            /// <inheritdoc cref="Messaging.ShareEncryptedContent(EncryptedMessaging.Contact, string, byte[], string, string)"/>
            ShareEncryptedContent,
            /// <summary>Used internally to indicate that the message is a reply to another previous message</summary>
            ReplyToMessage,
        }
        /// <summary>
        /// Used to notify an event
        /// </summary>
        public enum InformType : byte
        {
            /// <summary>This info is sent to the contact to inform me that my avatar has been changed</summary>
            AvatarHasUpdated
        }

        private static Dictionary<MessageType, string> MessageDescriptionInitializer()
        {
            // If you want to add new items visible to the view, you need to add their description here 
            // NOTE: Please do not add descriptions, for post types that should not be visible in the view !!
            var dictionary = new Dictionary<MessageType, string>
            {
                #pragma warning disable
                { MessageType.Text, Dictionary.TextMessage },
                { MessageType.Audio, "🔊 " + Dictionary.Audio },
                { MessageType.Image, "📷 " + Dictionary.Image },
                { MessageType.Contact, "💬 " + Dictionary.NewContact },
                { MessageType.AudioCall, "📞 " + Dictionary.AudioCall},
                { MessageType.VideoCall, "👨 " + Dictionary.VideoCall },
                { MessageType.Location, "📍 " + Dictionary.Position },
                { MessageType.PdfDocument, "📃 " + Dictionary.Document + " PDF" },
                { MessageType.PhoneContact, "👨 " + Dictionary.Contact },
                { MessageType.StartAudioGroupCall, "📞 " + Dictionary.AudioCall},
                { MessageType.StartVideoGroupCall, "📞 " + Dictionary.VideoCall},
                { MessageType.EndCall, Dictionary.CallEnded},
                { MessageType.DeclinedCall, Dictionary.CallDeclined},
                { MessageType.ShareEncryptedContent, "⚓ " + Dictionary.EncryptedSharedContent},
                { MessageType.ReplyToMessage, Dictionary.TextMessage},
                #pragma warning restore
            };
            return dictionary;
        }
        /// <summary>
        /// Useful for identifying which messages have a visual effect in the chat
        /// </summary>
        internal static readonly Dictionary<MessageType, string> MessageDescription = MessageDescriptionInitializer();

        /// <summary>
        /// Reads a post in binary format with the encrypted data and turns it into a clear message
        /// </summary>
        /// <param name="dataPost">Post in binary format</param>
        /// <param name="chatId">Chat Id</param>
        /// <param name="receptionTime">Reception Time UTC format</param>
        /// <param name="message"></param>
        /// <param name="isNewPost"></param>
        public bool ReadDataPost(byte[] dataPost, ulong chatId, DateTime receptionTime, out Message message, bool isNewPost = false)
        {
            // [0] version, [1][2][3][4] Unix timestamp, [5] data type, [6] n. participants, [7...] 8 byte for each participants (is the IDs)
            message = null;
            if (dataPost == null || dataPost.Length == 0)
            {
                return false;
            }

            var version = dataPost[0] & 0b01111111;
            var encrypted = (dataPost[0] & 0b10000000) == 0;
            if (version <= 1)
            {
#if RELEASE
				try
				{
#endif
                var postId = Repository.PostId(dataPost);
                var timestamp = BytesToInt(Bytes.Skip(dataPost, 1).Take(4));
                var dateAndTime = FromUnixTimestamp(timestamp);
                var type = (MessageType)dataPost[5];
                var contact = _context.Contacts.GetContact(chatId);
                if (encrypted && contact == null && type != MessageType.Contact) // if type = MessageType.Contact then a contact has been received to add to the address book
                {
                    return false;
                }
                var participants = contact?.Participants;

                // [6]=N.Participants
                var nParticipants = (int)dataPost[6]; // The list of ids is not mandatory, it is sent only when chat has been opened, nor the first message
                                                      // Add userId for each participant

                var pointer = 7;
                List<ulong> idParticipants = null;

                if (nParticipants != 0)
                {
                    idParticipants = new List<ulong>();
                    for (var i = 0; i < nParticipants; i++)
                    {
                        var ulongBytes = new byte[8];
                        Buffer.BlockCopy(dataPost, pointer, ulongBytes, 0, 8);
                        idParticipants.Add(BytesToUlong(ulongBytes));
                        pointer += 8;
                    }
                }
                byte[] author = null;
                ulong authorId;
                byte authorIndex;
                if (!encrypted)
                {
                    authorIndex = Bytes.Skip(dataPost, pointer).Take(1)[0];
                    pointer++;
                    authorId = idParticipants[authorIndex];
                    //author = participants[authorIndex];
                    var len = dataPost.Length - pointer;
                    var data = Bytes.Skip(dataPost, pointer).Take(len);
                    message = new Message(_context, contact, type, author, dateAndTime, data, receptionTime, postId, encrypted, chatId, authorId);
                    //if (MessageDescription.ContainsKey(message.Type))
                    //	contact.SetLastMessagePreview(message);
                    return true;
                }
                if (DecryptPassword(dataPost, ref pointer, participants, idParticipants, out var password))
                {
                    var len = dataPost.Length - pointer;
                    var encryptedData = Bytes.Skip(dataPost, pointer).Take(len);
                    var dataElement = Cryptography.Decrypt(encryptedData, password);
                    if (dataElement == null) return false;
                    int signatureLength;
                    int dataLen;
                    if (version == 0)
                    {
                        signatureLength = _signatureLength;
                        dataLen = dataElement.Length - (signatureLength + 1); // +1 is a Byte of authorIndex
                    }
                    else
                    {
                        // 1 byte Index participant of signature + signature + 1 bite signature length
                        signatureLength = dataElement[dataElement.Length - 1];
                        dataLen = dataElement.Length - (signatureLength + 2); // +1 is a Byte of authorIndex, +1 is a Byte of authorIndex signatureLength
                    }
                    var data = dataElement.Take(dataLen);
                    var hashData = CryptoServiceProvider.ComputeHash(data);
                    authorIndex = Bytes.Skip(dataElement, dataLen).Take(1)[0];
                    authorId = idParticipants[authorIndex];
                    var signatureOfData = Bytes.Skip(dataElement, dataLen + 1).Take(signatureLength);

                    ContactMessage newContact = null;
                    if (type == MessageType.Contact && isNewPost)
                    {
                        newContact = new ContactMessage(data);
                        if (participants == null)
                        {
                            // I have received a contact
                            participants = newContact.GetParticipantsKeys(_context);
                        }
                    }

                    // Validate author
#if DEBUG
                    if (authorIndex > participants.Count - 1)
                    {
                        Debugger.Break();
                        Debug.WriteLine(type);
                        Debug.WriteLine(contact?.Name);
                    }
#endif
                    var participant = participants[authorIndex];

                    using (var csp = new CryptoServiceProvider(participant))
                    {
                        if (csp.VerifyHash(hashData, signatureOfData))
                            author = participant;
                    }

                    if (author == null)
                        // Block written by an impostor
                        return false;
                    if (type == MessageType.Contact)
                        if (isNewPost) // // isNew prevent a loop and a stack overflow error (add a contact only if I received a new message)
                            if (ContactConverter.ParticipantsToChatId(participants, newContact.Name) == chatId) //The new contact is the sender of this new post
                            {
                                var myKey = _context.My.GetPublicKeyBinary(); ;
                                if (participants.Find(x => x.SequenceEqual(myKey)) != null) // Check if I'm a member of this chat group
                                {
                                    var receivedContact = _context.Contacts.AddContact(newContact, Contacts.SendMyContact.None);
                                    if (contact == null && receivedContact.ChatId == chatId)
                                        contact = receivedContact;
                                }
                            }

                    if (contact == null) return false;
                    message = new Message(_context, contact, type, author, dateAndTime, data, receptionTime, postId, encrypted, chatId, authorId);
                    if (MessageDescription.ContainsKey(message.Type))
                        contact.SetLastMessagePreview(message);
                    return true;
                }
#if RELEASE
				}
				catch (Exception)
				{
				}
#endif
            }
            else
            {
                Debugger.Break(); //The dataPost version is not supported
            }
            return false;
        }
        private const int _signatureLength = 70;

        /// <summary>
        /// This feature creates an encrypted post for chat participants. It must therefore be sent to the server in order to be distributed to all.
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="data">Message data</param>
        /// <param name="participants">Public keys of each participant</param>
        /// <param name="creationDate">Return current time GTM</param>
        /// <param name="replyToPostId">The post Id property of the message you want to reply to</param>
        /// <param name="encrypted">if you need to send a message to someone who does not have the sender's contact in the address book, or the data is already encrypted, it is possible with this parameter to delete the encryption. Users are not allowed to receive unencrypted messages, this function is specific for messages to servers or cloud systems</param>		
        /// <returns>Data post</returns>
        public byte[] CreateDataPost(MessageType type, byte[] data, List<byte[]> participants, out DateTime creationDate, ulong? replyToPostId, bool encrypted = true)
        {
            return CreateDataPostInternal(type, data, replyToPostId, participants, null, out creationDate, encrypted);
        }
        /// <summary>
        /// This feature creates an unencrypted post for chat participants. It must therefore be sent to the server in order to be distributed to all.
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="data">Message data</param>
        /// <param name="usersId">User ID of each participant</param>
        /// <param name="creationDate">Return current time GTM</param>
        /// <param name="replyToPostId">The post Id property of the message you want to reply to</param>
        /// <returns>Data post</returns>
        public byte[] CreateDataPostUnencrypted(MessageType type, byte[] data, ulong[] usersId, out DateTime creationDate, ulong? replyToPostId)
        {
            return CreateDataPostInternal(type, data, replyToPostId, null, usersId, out creationDate, false);
        }

        private byte[] CreateDataPostInternal(MessageType type, byte[] data, ulong? replyToPostId, List<byte[]> participants, ulong[] usersId, out DateTime creationDate, bool encrypted = true)
        {
            //try
            //{
            // Specifications of the anonymous data format (Version 0):
            // byte 0 (version): Indicates the version of the technical specification, if this parameter changes everything in the data package it can follow other specifications
            // byte 0 bit n.8 (0b10000000): This bit indicates that the message is not encrypted 
            // byte 1,2,3,4 UNIX Timestamp
            // byte 5: Indicates the type of data that contains this block: Text, Image, Audio (in the future also new implementations).
            // byte 6: Number of participants in this chat
            // follow 8 byte for each participant with the each userId 
            // Global Encrypted Password: Variable length data that contains the encrypted password for each participant in the chat room. The length of this data depends on the number of participants. For this purpose, see the EncryptPasswordForParticipants function. The protocol includes more than 2 participants in a chat room. Each participant can decrypt the password using his private key.
            // Encrypted data: This is the real data (message, photo, audio, etc.), encrypted according to an algorithm contained in the Cryptography.Encrypt class. The encryption is made with an XOR between the original data and a random data generated with a repetitive hash that starting from the password.
            var version = 1;
            if (!encrypted)
                version |= 0b10000000; // This bit indicates that the message is not encrypted
            var unixTimestamp = ToUnixTimestamp(DateTime.UtcNow);
            creationDate = FromUnixTimestamp(unixTimestamp);
            if (replyToPostId != null) //Replication messages have some additional information that is added to the beginning of the data (message type and the id of the post being replied to)
            {
                data = new[] { (byte)type }.Combine(GetBytes((ulong)replyToPostId), data);
                type = MessageType.ReplyToMessage;
            }
            // [0]=version, [1][2][3][4]=timestamp, [5]=data type
            var postData = new[] { (byte)version }.Combine(GetBytes(unixTimestamp), new[] { (byte)type });
            if (participants != null)
            {
                usersId = new ulong[participants.Count];
                for (var i = 0; i < participants.Count; i++)
                    usersId[i] = ContactConverter.GetUserId(participants[i]);
            }
            // [6]=N.Participants
            postData = postData.Combine(new[] { Convert.ToByte(usersId.Length) });
            // add userId for each participant: The list of ids is not mandatory, it is sent only when chat has been opened, nor the first message
            // To maximize privacy the user ID does not match the public key, the public key is not sent to the server
            foreach (var id in usersId)
                postData = postData.Combine(GetBytes(id));

            //usersId.ForEach((id) => postData = postData.Combine(GetBytes(id)));
            var myId = _context.My.Id; ;
            byte authorIndex = 0;
            for (byte i = 0; i < usersId.Length; i++)
            {
                if (usersId[i] == myId)
                    authorIndex = i;
            }
            if (!encrypted)
            {
                postData = postData.Combine(new[] { authorIndex }, data);
            }
            else
            {
                var password = Guid.NewGuid().ToByteArray().Combine(Guid.NewGuid().ToByteArray()); //32 byte password
                var globalPassword = EncryptPasswordForParticipants(password, participants);
                var hashData = CryptoServiceProvider.ComputeHash(data);

                var signatureOfData = _context.My.Csp.SignHash(hashData);

                // 1 byte Index participant of signature + signature + 1 bite signature length

                var encryptedData = Cryptography.Encrypt(data.Combine(new[] { authorIndex }, signatureOfData, new[] { (byte)signatureOfData.Length }), password);
                postData = postData.Combine(globalPassword, encryptedData);
            }
            return postData;

            //}
            //catch (Exception ex)
            //{
            //	Debugger.Break();
            //}
            //creationDate = DateTime.MinValue;
            //return null;
        }

        private static byte[] EncryptPasswordForParticipants(byte[] password, List<byte[]> participants)
        {
            // ========================RESULT================================
            // [len ePass1] + [ePass1] + [len ePass2] + [ePass2] + ... + [0] 
            // ==============================================================
            var result = Array.Empty<byte>();
            var csp = new CryptoServiceProvider();
            foreach (var publicKey in participants)
            {
                csp.ImportCspBlob(publicKey);
                var encryptedPassword = csp.Encrypt(password);

                var lanPass = (byte)encryptedPassword.Length;
                var len = new[] { lanPass };
                result = result.Combine(len, encryptedPassword);
            }
            result = result.Combine(new byte[] { 0 });
            return result;
        }

        private bool DecryptPassword(byte[] data, ref int pointer, List<byte[]> participants, List<ulong> idParticipant, out byte[] password)
        {
            // If participants is null then it means that the message comes from in unlisted contact.
            // This is only allowed if the message type is a contact
            password = null;
            try
            {
                // START ==== Obtain all password encrypted ====
                var encryptedPasswords = new List<byte[]>();
                encryptedPasswords = Functions.SplitDataWithZeroEnd(data, pointer, out pointer);
                // END  ==== Obtain all password encrypted ====
                var positionOfKey = -1;
                if (participants != null)
                {
                    var myKey = _context.My.Csp.ExportCspBlob(false);
                    positionOfKey = participants.FindIndex(x => x.SequenceEqual(myKey));
                }
                else if (idParticipant != null)
                {
                    // I received a contact to add to the address book
                    var myId = _context.My.Id;
                    positionOfKey = idParticipant.FindIndex(x => x == myId);
                }
                else
                {
                    return false;
                }
                if (positionOfKey == -1)
                {
                    // I'm not in this chat. I don't have a private key, so I can't decrypt it! Maybe my private key has been changed by the user!
                    return false;
                }
                var ePassword = encryptedPasswords[positionOfKey];
                password = _context.My.Csp.Decrypt(ePassword);
            }
            catch (Exception ex)
            {
                // if there is a error here, please check the dimension of array participants (>=2), if your private key is a valid private key?, you have changed the private key?
                Debug.WriteLine(ex.Message);
                return false;
            }
            return true;
        }
    }

}