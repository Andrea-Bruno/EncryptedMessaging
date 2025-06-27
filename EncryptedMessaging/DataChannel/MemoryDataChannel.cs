using CommunicationChannel;
using System;
using static CommunicationChannel.Channel;
using static CommunicationChannel.CommandsForServer;
using System.Diagnostics;
using System.Collections.Generic;
namespace EncryptedMessaging.DataChannel
{

    /// <summary>
    /// When the router (the server machine), is physically the same machine as the client, through this class the client-server communications are passed as memory references in order to eliminate latency and not create real unnecessary connections.
    /// </summary>
    public class MemoryDataChannel : ChannelBase, IDisposable, IChannel
    {
        /// <param name="domain">A domain (also known as Network Id) corresponds to a membership group. Using the domain it is possible to divide the traffic on a server into TestNet, MainNet group (in order to isolate the message circuit within a given domain).</param>
        /// <param name="onMessageArrives">Event that is raised when a message arrives.</param>
        /// <param name="onDataDeliveryConfirm">Event that is generated when the router (server) has received the outgoing message, This element returns the message in raw format</param>
        /// <param name="myId">The identifier of the current user. Since the server system is focused on anonymity and security, there is no user list, it is a cryptographic id generated with a hashing algorithm</param>
        /// <param name="onError">It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each data IO error (TCP, Pipe, etc..), to notify the type of error and its description</param>
        public MemoryDataChannel(int domain, Action<ulong, byte[]> onMessageArrives, Action<uint> onDataDeliveryConfirm, ulong myId, OnErrorEvent onError = null)
        {
            OnError = onError;
            MyId = myId;
            Domain = domain;
            OnMessageReceived = onMessageArrives;
            OnDataDeliveryConfirm = onDataDeliveryConfirm;
            Instances ??= new Dictionary<ulong, MemoryDataChannel>();
            lock (Instances)
                Instances.Add(myId, this);
            IsConnected = IsAvailable;
        }

        /// <summary>
        /// When the router (the server machine), is physically the same machine as the client, through this class the client-server communications are passed as memory references in order to eliminate latency and not create real unnecessary connections.
        /// </summary>
        static public bool IsAvailable => RouterDataInput != null;

        /// <summary>
        /// Indicates whether the channel is available for use.
        /// </summary>
        public override bool HasConnectivity => IsAvailable;

        static Dictionary<ulong, MemoryDataChannel> Instances;

        /// <summary>
        /// Returns true if a client with the specified ID exists
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        static public bool ClientIsOnline(ulong userId) => Instances?.ContainsKey(userId) == true;

        /// <summary>
        /// Use this command to re-establish the connection if it is disabled by the timer set with the initialization
        /// </summary>
        public override void ReEstablishConnection()
        {
            // Intentionally left blank – no reconnection required
        }

        #region IO methods

        /// <summary>
        /// POST data to server
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataFlags"></param>
        /// <returns></returns>
        internal override void SendDataToRouter(byte[] data, DataFlags dataFlags)
        {
            try
            {
                // Put in input the data to router
                RouterDataInput(data, (byte)dataFlags, MyId, Domain);
                LastOUT = DateTime.Now;
                LastCommandOUT = (Protocol.Command)data[0];
                var dataId = BitConverter.ToUInt32(data, 1);
                OnDataDeliveryConfirm?.Invoke(dataId);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ErrorType.SendDataError, ex.Message);
                Debugger.Break();
            }
        }

        public static bool ReceiveDataFromRouter(int domainId, ulong userId, byte[] data)
        {
            if (Instances != null && Instances.TryGetValue(userId, out var instance))
            {
                if (instance.Domain == domainId)
                {
                    instance.OnDataReceives(data);
                }
            }
            return false;
        }

        #endregion


        public delegate void RouterInput(byte[] data, byte dataFlags, ulong userId, int domainId);

        public static RouterInput RouterDataInput;

        private bool Disposed;

        public override void Dispose()
        {
            if (!Disposed)
            {
                lock (Instances)
                    Instances.Remove(MyId);
            }
            Disposed = true;

        }
    }
}
