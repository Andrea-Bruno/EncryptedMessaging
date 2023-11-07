using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Threading;

namespace CommunicationChannel
{
    /// <summary>
    /// This class handle all the communication channel operation with server-side.
    /// </summary>
    public class Channel : IDisposable
    {
        /// <summary>
        /// Initialize the library
        /// </summary>
        /// <param name="serverAddress">Server Address</param>
        /// <param name="domain">A domain (also known as Network Id) corresponds to a membership group. Using the domain it is possible to divide the traffic on a server into TestNet, MainNet group (in order to isolate the message circuit within a given domain).</param>
        /// <param name="onMessageArrives">Event that is raised when a message arrives.</param>
        /// <param name="onDataDeliveryConfirm">Event that is generated when the router (server) has received the outgoing message, This element returns the message in raw format</param>
        /// <param name="myId">The identifier of the current user. Since the server system is focused on anonymity and security, there is no user list, it is a cryptographic id generated with a hashing algorithm</param>
        /// <param name="connectionTimeout">Used to remove the connection when not in use. However, mobile systems remove the connection when the application is in the background so it makes no sense to try to keep the connection always open. This also lightens the number of simultaneous server-side connections.</param>
        /// <param name="licenseActivator">OEM ID (ulong) and algorithm for the digital signature of the license activation. If present, this function will be called to digitally sign at the time of authentication. The digital signature must be put by the OEM who must have the activation licenses. The router will check if the license is valid upon connection.</param>
        /// <param name="onError">It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each tcp error, to notify the type of error and its description</param>
        public Channel(string serverAddress, int domain, Action<ulong, byte[]> onMessageArrives, Action<uint> onDataDeliveryConfirm, ulong myId, int connectionTimeout = Timeout.Infinite, Tuple<ulong, Func<byte[], byte[]>> licenseActivator = null, OnErrorEvent onError = null)
        {
            AntiDuplicate = new AntiDuplicate(myId);
            OnError = onError;
            LicenseActivator = licenseActivator;
            MyId = myId;
            Domain = domain;
            //ContextIsReady = contextIsReady;
            ConnectionTimeout = connectionTimeout;
            Tcp = new Tcp(this);
            CommandsForRouter = new CommandsForServer(this);
            Spooler = new Spooler(this);
            ServerUri = new UriBuilder(serverAddress).Uri; //new Uri(serverAddress);
            OnMessageArrives = onMessageArrives;
            OnDataDeliveryConfirm = onDataDeliveryConfirm;
            lock (Channels)
            {
                Channels.Add(this);
            }
        }

        /// <summary>
        /// License expired (the router did not authorize the connection)
        /// </summary>
        public bool LicenseExpired { get; internal set; }

        /// <summary>
        /// When was the last data reception (Utc)
        /// </summary>
        public DateTime LastIN { get { return _LastIN; } internal set { Tcp.KeepAliveRestart(); _LastIN = value; } } // KeepAliveRestart() = The arrival of the received data is the confirmation that the connection is still present. The data transmitted under WSL does not generate an error even if there is no more internet line
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
        internal static readonly IsolatedStorageFile IsoStoreage = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly | IsolatedStorageScope.Domain, null, null);
        internal readonly AntiDuplicate AntiDuplicate;
        internal readonly int ConnectionTimeout = Timeout.Infinite;
        internal readonly ulong MyId;
        internal readonly Spooler Spooler;
        internal readonly Tcp Tcp;
        /// <summary>
        /// class object to use command at server-side.
        /// </summary>
        public readonly CommandsForServer CommandsForRouter;
        internal readonly Action<uint> OnDataDeliveryConfirm; // uint parameter is dataId

        /// <summary>
        /// Use this command to re-establish the connection if it is disabled by the timer set with the initialization
        /// </summary>
        public static void ReEstablishConnection()
        {
            lock (Channels)
                foreach (var channel in Channels)
                {
                    lock (channel.Tcp.LockIsConnected)
                    {
                        channel.Tcp.Connect();
                    }
                }
        }
        /// <summary>
        /// checks the connection status.
        /// </summary>
        /// <returns>True or False</returns>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// server URL.
        /// </summary>
        public readonly Uri ServerUri;
        /// <summary>
        /// Server domain id.
        /// </summary>
        public readonly int Domain;
        internal void OnDataReceives(byte[] incomingData, bool directlyWithoutSpooler, out Tuple<ErrorType, string> error, out Protocol.Command command)
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
                Spooler.OnConfirmReceipt(dataId);
            }
            else if (command == Protocol.Command.Ping) // Pinging from the server does not reset the connection timeout, otherwise, if the pings occur frequently, the connection will never be closed
            {
                Debug.WriteLine("ping received!");
            }
            else if (command == Protocol.Command.Messages)
            {
                if (!directlyWithoutSpooler)
                {
                    //Send a confirmation of data received to the server
                    CommandsForRouter.DataReceivedConfirmation(incomingData);
                }
                var chatId = Converter.BytesToUlong(incomingData.Skip(1).Take(8));
                if (!SplitAllPosts(incomingData.Skip(9), out var posts))
                {
                    error = Tuple.Create(ErrorType.WrongDataLength, "SplitAllPosts");
                    return;
                }
                PostCounter++;
                LastPostParts = posts.Count;
                System.Threading.Tasks.Task.Run(() =>
                {

                    //    new Thread(() =>
                    //{

                    lock (OnMessageArrives)
                    {
                        posts.ForEach(post =>
                        {
                            if (directlyWithoutSpooler == false && AntiDuplicate.AlreadyReceived(post))
                            {
                                DuplicatePost++;
#if DEBUG && !TEST
                                    Debugger.Break();
#endif
                            }
                            else
                            {
                                OnMessageArrives?.Invoke(chatId, post);
                            }
                        });
                    }
                    //}).Start();

                });
            }
            error = null;
        }
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
                    System.Threading.Tasks.Task.Run(() => RefreshLogError?.Invoke(ErrorLog));
            }
            if (OnError != null)
                System.Threading.Tasks.Task.Run(() => OnError?.Invoke(errorId, description));
        }

        /// <summary>
        /// Provides the base for enumerations to represent errors.
        /// </summary>
        public enum ErrorType
        {
#pragma warning disable
            Working,
            ConnectionFailure,
            WrongDataLength,
            LostConnection,
            SendDataError,
            CommandNotSupported,
            ConnectionClosed
#pragma warning restore
        }
        /// <summary>
        /// Delegate used to report errors on TCP communication
        /// </summary>
        /// <param name="errorId">Error type</param>
        /// <param name="description">Error descrtiption</param>
        public delegate void OnErrorEvent(ErrorType errorId, string description);

        /// <summary>
        /// It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each tcp error, to notify the type of error and its description
        /// </summary>
        private readonly OnErrorEvent OnError;

        internal void ConnectionChange(bool status)
        {
            if (IsConnected != status)
            {
                IsConnected = status;
                if (OnRouterConnectionChange != null)
                    System.Threading.Tasks.Task.Run(() => OnRouterConnectionChange?.Invoke(IsConnected));
            }
        }

        /// <summary>
        /// Function that acts as an event and will be called when the connection was successful and the client is logged into the router (return true), or when the connection with the router is lost (return false). You can set this action as an event.
        /// You can set this action as an event.
        /// </summary>
        public Action<bool> OnRouterConnectionChange;

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
        /// TCP client exists
        /// </summary>
        public bool ClientExists => Tcp.Client != null;
        /// <summary>
        /// TCP client connection status 
        /// </summary>
        public bool ClientConnected => Tcp.Client != null && Tcp.Client.Connected;
        /// <summary>
        /// Client log in status
        /// </summary>
        public bool Logged => Tcp.Logged;

        /// <summary>
        /// Number of bytes in the queue
        /// </summary>
        public int QueeCount => Spooler.QueeCount;
        /// <summary>
        /// 
        /// </summary>
        public int LastPostParts;
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
        /// Number of failure connnection
        /// </summary>
        public ulong KeepAliveFailures { get; internal set; }
        //=================================================================================================

        private bool _Internet = false;
        private bool Internet
        {
            set
            {
                if (_Internet != value)
                {
                    _Internet = value;
                    if (value)
                        Tcp.Connect();
                    else
                        Tcp.Disconnect(false);
                }
            }
        }

        private static bool _InternetAccess;

        /// <summary>
        /// Check internet access.
        /// </summary>
        public static bool InternetAccess
        {
            get => _InternetAccess;
            set
            {
                _InternetAccess = value;
                lock (Channels)
                {
                    Channels.ForEach(channel => channel.Internet = _InternetAccess);
                }
            }
        }

        internal ErrorType Status;
        internal readonly Action<ulong, byte[]> OnMessageArrives;
        private static bool SplitAllPosts(byte[] data, out List<byte[]> posts)
        {
            posts = new List<byte[]>();
            var p = 0;
            if (data.Length > 0)
            {
                do
                {
                    var len = Converter.BytesToInt(data.Skip(p).Take(4));
                    p += 4;
                    if (len + p > data.Length)
                    {
                        //Unexpected data length
                        return false;
                    }
                    var post = new byte[len];
                    Buffer.BlockCopy(data, p, post, 0, len);
                    posts.Add(post); // post format: [1] version, [2][3][4][5] UNIX timestamp, [7] data type 
                    p += len;
                } while (p < data.Length);
            }
            return p == data.Length;
        }
        /// <summary>
        /// The Dispose method is primarily implemented to release unmanaged resources. When working with instance members that are IDisposable implementations, it's common to cascade Dispose calls. There are additional reasons for implementing Dispose, for example, to free memory that was allocated, remove an item that was added to a collection, or signal the release of a lock that was acquired.
        /// </summary>
        public void Dispose()
        {
            OnRouterConnectionChange = null;
            RefreshLogError = null;
            Tcp.Dispose();
        }
        //		public static void OnConnectivityChange(bool internetAccess) => InternetAccess = internetAccess;
    }
}