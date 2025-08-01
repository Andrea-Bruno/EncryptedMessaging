﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Threading;
using System.Threading.Tasks;
using static CommunicationChannel.CommandsForServer;

namespace CommunicationChannel
{
    /// <summary>
    /// This class handle all the communication channel operation with server-side.
    /// </summary>
    public class Channel : IDisposable, IChannel
    {
        /// <summary>
        /// Create a new instance
        /// </summary>
        /// <param name="serverAddress">Server Address</param>
        /// <param name="domain">A domain (also known as Network Id) corresponds to a membership group. Using the domain it is possible to divide the traffic on a server into TestNet, MainNet group (in order to isolate the message circuit within a given domain).</param>
        /// <param name="onMessageArrives">Event that is raised when a message arrives.</param>
        /// <param name="onDataDeliveryConfirm">Event that is generated when the router (server) has received the outgoing message, This element returns the message in raw format</param>
        /// <param name="myId">The identifier of the current user. Since the server system is focused on anonymity and security, there is no user list, it is a cryptographic id generated with a hashing algorithm</param>
        /// <param name="connectionTimeout">Used to remove the connection when not in use. However, mobile systems remove the connection when the application is in the background so it makes no sense to try to keep the connection always open. This also lightens the number of simultaneous server-side connections.</param>
        /// <param name="licenseActivator">OEM ID (ulong) and algorithm for the digital signature of the license activation. If present, this function will be called to digitally sign at the time of authentication. The digital signature must be put by the OEM who must have the activation licenses. The router will check if the license is valid upon connection.</param>
        /// <param name="onError">It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each data IO error (TCP, Pipe, etc..), to notify the type of error and its description</param>
        public Channel(bool? hasConnectivity, string serverAddress, int domain, Action<ulong, byte[]> onMessageArrives, Action<uint> onDataDeliveryConfirm, ulong myId, int connectionTimeout = Timeout.Infinite, Tuple<ulong, Func<byte[], byte[]>> licenseActivator = null, OnErrorEvent onError = null)
        {
            AntiDuplicate = new AntiDuplicate(myId);
            OnError = onError;
            LicenseActivator = licenseActivator;
            MyId = myId;
            Domain = domain;
            ConnectionTimeout = connectionTimeout;
            DataIO = new DataIO.DataIO(this);
            CommandsForRouter = new CommandsForServer(this);
            ServerUri = new UriBuilder(serverAddress).Uri;
            if (ServerUri.Scheme.ToLower() != "pipe")
            {
                if (!serverAddress.EndsWith(":80") && ServerUri.Port == 80)
                {
                    ServerUri = new UriBuilder(ServerUri) { Port = 5222 }.Uri;
                }
            }
            OnMessageReceived = onMessageArrives;
            OnDataDeliveryConfirm = onDataDeliveryConfirm;
            lock (Channels)
            {
                Channels.Add(this);
            }
            HasConnectivity = hasConnectivity ?? true; // If not specified, assume connectivity is available by default

            InternetAccess = true;

        }

        /// <summary>
        /// Indicates whether the channel has connectivity. This property is used to determine if the channel can establish connections and communicate with the router.
        /// </summary>
        public bool HasConnectivity
        {
            get
            {
                if (ServerUri.Scheme.StartsWith("pipe"))
                    return PipeAccess;
                else
                    return InternetAccess;
            }
            internal set
            {
                if (ServerUri.Scheme.StartsWith("pipe"))
                    PipeAccess = value;
                else
                    InternetAccess = value;
            }
        }

        /// <summary>
        /// Send data to the server/router.
        /// Sends a data packet that the server/router will resend to its destination.
        /// </summary>
        /// <param name="chatId">chat to which data belong to</param>
        /// <param name="dataToSend">data</param>
        /// <param name="directlyWithoutSpooler"> if you want to send directly without spooler make it true else false </param>
        public void SendPostToServer(ulong chatId, byte[] dataToSend, bool directlyWithoutSpooler = false) => CommandsForRouter.SendCommandToServer(Protocol.Command.Data, dataToSend, chatId, dataFlags: directlyWithoutSpooler ? DataFlags.DirectlyWithoutSpooler : DataFlags.None);

        /// <summary>
        /// Sends a data packet addressed to the router/server. This data packet will be interpreted by the router based on the function that is passed to the router when it is initialized. If no function is passed during initialization, sending data to the router will have no effect.
        /// </summary>
        /// <param name="dataToSend"></param>
        public void SendRouterData(byte[] dataToSend)
        {
            CommandsForRouter.SendCommandToServer(Protocol.Command.RouterData, dataToSend, null, dataFlags: DataFlags.DirectlyWithoutSpooler);
        }

        /// <summary>
        /// License expired (the router did not authorize the connection)
        /// </summary>
        public bool LicenseExpired { get; internal set; }

        /// <summary>
        /// When was the last data reception (Utc)
        /// </summary>
        public DateTime LastIN
        {
            get { return _LastIN; }
            internal set
            {
                // DataIO.KeepAliveRefresh();
                _LastIN = value;
            }
        } // KeepAliveRestart() = The arrival of the received data is the confirmation that the connection is still present. The data transmitted under WSL does not generate an error even if there is no more internet line
        /// <summary>
        /// The last command that was received from the router
        /// </summary>
        public Protocol.Command LastCommandIN { get; internal set; }
        private DateTime _LastIN = DateTime.UtcNow;

        /// <summary>
        /// When was the last data transmission (Utc)
        /// </summary>
        public DateTime LastOUT { get; internal set; }
        /// <summary>
        /// The last command that was sent to the router
        /// </summary>
        public Protocol.Command LastCommandOUT { get; internal set; }
        /// <summary>
        /// The last time KeepAlive was performed for checking the data communication channel (Utc)
        /// </summary>
        public DateTime LastKeepAliveCheck { get; internal set; }

        /// <summary>
        /// the last moment in which there was data transmission (in reception or transmission). Utc value.
        /// </summary>
        public DateTime LastCommunication => LastIN > LastOUT ? LastIN : LastOUT;

        internal readonly Tuple<ulong, Func<byte[], byte[]>> LicenseActivator;
        private static readonly List<Channel> Channels = new List<Channel>();
        internal readonly Func<bool> ContextIsReady = null;
        internal static readonly IsolatedStorageFile IsoStorage = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly | IsolatedStorageScope.Domain, null, null);
        internal readonly AntiDuplicate AntiDuplicate;
        internal readonly int ConnectionTimeout = Timeout.Infinite;
        internal readonly ulong MyId;
        //internal readonly Spooler Spooler;
        internal readonly DataIO.DataIO DataIO;
        /// <summary>
        /// class object to use command at server-side.
        /// </summary>
        public readonly CommandsForServer CommandsForRouter;
        internal readonly Action<uint> OnDataDeliveryConfirm; // uint parameter is dataId

        /// <summary>
        /// Use this command to re-establish the connection if it is disabled by the timer set with the initialization
        /// </summary>
        public void ReEstablishConnection()
        {
            lock (Channels)
                foreach (var channel in Channels)
                {
                    channel.DataIO.Connect();
                }
        }
        /// <summary>
        /// checks the connection status.
        /// </summary>
        /// <returns>True or False</returns>
        public bool IsConnected => DataIO.IsConnected();

        /// <summary>
        /// server URL.
        /// </summary>
        public readonly Uri ServerUri;

        /// <summary>
        /// Indicates the type of connection that is established with the router (this depends on the entry point specified)
        /// </summary>
        public ConnectivityType TypeOfConnection => ServerUri.Scheme.Equals(nameof(ConnectivityType.Pipe), StringComparison.OrdinalIgnoreCase) ? ConnectivityType.Pipe : Channel.ConnectivityType.Internet;

        private ConcurrentQueue<(byte[], DataFlags, ulong)> _dataReceivesQueue = new ConcurrentQueue<(byte[], DataFlags, ulong)>();

        /// <summary>
        /// Server domain id.
        /// </summary>
        public readonly int Domain;
        internal void OnDataReceives(byte[] incomingData, DataFlags flag, out Tuple<ErrorType, string> error, out Protocol.Command command)
        {
            command = default;
            if (incomingData.Length == 0)
            {
                error = Tuple.Create(ErrorType.WrongDataLength, "incomingData.Length == 0");
                return;
            }
            if (!Enum.IsDefined(typeof(Protocol.Command), incomingData[0]))
            {
                error = Tuple.Create(ErrorType.CommandNotSupported, "Command id=" + incomingData[0]);
                return;
            }
            command = (Protocol.Command)incomingData[0];
            if (command == Protocol.Command.DataReceivedConfirmation)
            {
                if (incomingData.Length != 5)
                {
                    error = Tuple.Create(ErrorType.WrongDataLength, "incomingData.Length != 5");
                    return;
                }
                var dataId = BitConverter.ToUInt32(incomingData, 1);
                OnDataDeliveryConfirm?.Invoke(dataId);
                DataIO.WaitConfirmationSemaphore.Set();
                DataIO.ProcessSendQueue();
            }
            else if (command == Protocol.Command.Ping) // Pinging from the server does not reset the connection timeout, otherwise, if the pings occur frequently, the connection will never be closed
            {
                LastPingReceived = DateTime.UtcNow;
                DataIO.KeepAliveRefresh();
                Debug.WriteLine("ping received!");
            }
            else if (command == Protocol.Command.Data)
            {
                if (flag == DataFlags.None)
                {
                    //Send a confirmation of data received to the server
                    CommandsForRouter.DataReceivedConfirmation(incomingData);
                }
                var chatId = Converter.BytesToUlong(incomingData.Skip(1).Take(8));
                var post = incomingData.Skip(9);

                PostCounter++;

#if DEBUG && !TEST
                if (_dataReceivesQueue.Count > 20)
                    Debugger.Break(); // Queue is too big, investigate why!
#endif

                if (false)
                {
                    lock (_dataReceivesQueue)
                    {
                        _OnDataReceives(post, flag, chatId);
                    }
                }
                else
                {
                    _dataReceivesQueue.Enqueue((post, flag, chatId));
                    ProcessDataReceivesQueue();
                }
            }
            else if (command == Protocol.Command.ConnectionEstablished)
            {
                DataIO.OnLoggedCompleted();
            }
            else if (command == Protocol.Command.RouterData)
            {
                OnDataRouterReceived?.Invoke(incomingData);
            }
            error = null;
        }

        private Task TaskDataReceivesQueue;

        private void ProcessDataReceivesQueue()
        {
            if (TaskDataReceivesQueue != null || _dataReceivesQueue.Count == 0)
                return;
            TaskDataReceivesQueue = Task.Run(() =>
            {
                try
                {
                    while (_dataReceivesQueue.TryDequeue(out var item))
                    {
                        try
                        {
                            var posts = item.Item1;
                            var flag = item.Item2;
                            var chatId = item.Item3;
                            _OnDataReceives(posts, flag, chatId);
                        }
                        catch (Exception ex)
                        {
                            Debugger.Break();
                        }
                    }
                }
                finally
                {
                    TaskDataReceivesQueue = null;
                }
            });
        }

        private void _OnDataReceives(byte[] post, DataFlags flag, ulong chatId)
        {
            if (flag == DataFlags.None && AntiDuplicate.AlreadyReceived(post))
            {
                DuplicatePost++;
#if DEBUG && !TEST
                Debugger.Break();
#endif
            }
            else
            {
                OnMessageReceived?.Invoke(chatId, post);
            }
        }

        internal DateTime LastPingReceived;

        internal void OnTcpError(ErrorType errorId, string description)
        {
            //manage TCP error here
            Status = errorId;
            StatusDescription = description;
            //if (errorId != Tcp.ErrorType.Working)
            //	Debugger.Break();
            if (LogError)
            {
                ErrorLog += DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss") + " " + Status + ": " + description + "\r\n";

                if (RefreshLogError != null)
                {
                    Task.Run(() => RefreshLogError?.Invoke(ErrorLog));
                }
            }
            if (OnError != null)
            {
                Task.Run(() => OnError?.Invoke(errorId, description));
            }
        }

        /// <summary>
        /// Provides the base for enumerations to represent errors.
        /// </summary>
        public enum ErrorType
        {
#pragma warning disable
            None,
            Working,
            ConnectionFailure,
            WrongDataLength,
            LostConnection,
            SendDataError,
            CommandNotSupported,
            ConnectionClosed,
            LoginTimeout,
#pragma warning restore
        }
        /// <summary>
        /// Delegate used to report errors on TCP communication
        /// </summary>
        /// <param name="errorId">Error type</param>
        /// <param name="description">Error description</param>
        public delegate void OnErrorEvent(ErrorType errorId, string description);

        /// <summary>
        /// It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each tcp error, to notify the type of error and its description
        /// </summary>
        private readonly OnErrorEvent OnError;

        internal void ConnectionChange(bool status)
        {
            if (OnRouterConnectionChange != null)
            {
                Task.Run(() => OnRouterConnectionChange?.Invoke(IsConnected));
            }
        }

        /// <summary>
        /// Function that acts as an event and will be called when the connection was successful and the client is logged into the router (return true), or when the connection with the router is lost (return false). You can set this action as an event.
        /// You can set this action as an event.
        /// </summary>
        public Action<bool> OnRouterConnectionChange { get; set; }

        /// <summary>
        /// Set this true if you want a ErrorLog
        /// </summary>
        public readonly bool LogError = true; // Set this true if you want a ErrorLog

        //=========================== Data exposed for diagnostic use =====================================
        /// <summary>
        /// Action to refresh error log.
        /// </summary>
        public event Action<string> RefreshLogError;

        internal string StatusDescription; //is multi line text 
        /// <summary>
        /// Data client exists
        /// </summary>
        public bool ClientExists => DataIO.Client != null;

        /// <summary>
        /// Client log in status
        /// </summary>
        public bool Logged => DataIO.Logged;

        /// <summary>
        /// Number of elements in the out queue (data packets waiting to be sent)
        /// </summary>
        public int QueueCount => DataIO._sendQueue.Count;
        /// <summary>
        /// Number of posts
        /// </summary>

        public int PostCounter;
        /// <summary>
        /// Number of duplicate post
        /// </summary>
        public int DuplicatePost;

        /// <summary>
        ///  Display all the error logs
        /// </summary>
        public string ErrorLog;
        /// <summary>
        /// Number of failure connection
        /// </summary>
        public ulong KeepAliveFailures { get; internal set; }
        //=================================================================================================

        private bool _Connectivity = false;
        internal bool Connectivity
        {
            get => _Connectivity;
            set
            {
                if (_Connectivity != value)
                {
                    _Connectivity = value;
                    if (value)
                        DataIO.Connect();
                    else
                        DataIO.Disconnect(false);
                }
            }
        }

        /// <summary>
        /// Type of connectivity
        /// </summary>
        public enum ConnectivityType
        {
            /// <summary>
            /// Internet connection
            /// </summary>
            Internet,
            /// <summary>
            /// Pipe bidirectional connection
            /// </summary>
            Pipe
        }

        private static bool _InternetAccess;

        /// <summary>
        /// Status Internet access.
        /// </summary>
        public static bool InternetAccess
        {
            get => _InternetAccess;
            set
            {
                _InternetAccess = value;
                lock (Channels)
                {
                    Channels.ForEach(channel =>
                    {
                        if (channel.ServerUri.Scheme.StartsWith("http"))
                            channel.Connectivity = _InternetAccess;
                    });
                }
            }
        }

        private static bool _PipeAccess;

        /// <summary>
        /// Status Pipe access.
        /// </summary>
        public static bool PipeAccess
        {
            get => _PipeAccess;
            set
            {
                _PipeAccess = value;
                lock (Channels)
                {
                    Channels.ForEach(channel =>
                    {
                        if (channel.ServerUri.Scheme.StartsWith("pipe"))
                            channel.Connectivity = _PipeAccess;
                    });
                }
            }
        }

        internal ErrorType Status;
        public Action<ulong, byte[]> OnMessageReceived { get; set; }

        /// <summary>
        /// Programmable event that is executed when receiving network data generated by the router/server (these are not messages from devices connected to the netrokr). The method that is set here must interpret this type of messages and handle them.
        /// </summary>
        public Action<byte[]> OnDataRouterReceived { get; set; }

        /// <summary>
        /// The Dispose method is primarily implemented to release unmanaged resources. When working with instance members that are IDisposable implementations, it's common to cascade Dispose calls. There are additional reasons for implementing Dispose, for example, to free memory that was allocated, remove an item that was added to a collection, or signal the release of a lock that was acquired.
        /// </summary>
        public void Dispose()
        {
            _dataReceivesQueue.Clear();
            OnRouterConnectionChange = null;
            OnDataRouterReceived = null;
            RefreshLogError = null;
            DataIO.Dispose();
        }
        //		public static void OnConnectivityChange(bool internetAccess) => InternetAccess = internetAccess;
    }
}