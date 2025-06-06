using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunicationChannel;
using SecureStorage;
using static CommunicationChannel.Channel;
// Our mission is to exacerbate the concept of security in messaging and create something conceptually new and innovative from a technical point of view.
// Top-level encrypted communication (there is no backend , there is no server-side contact list, there is no server but a simple router, the theory is that if the server does not exist then the server cannot be hacked, the communication is anonymous, the IDs are derived from a hash of the public keys, therefore in no case it is possible to trace who originates the messages, the encryption key is changed for each single message, and a system of digital signatures guarantees the origin of the messages and prevents attacks "men in de middle").
// We use different concepts introduced with Bitcoin technology and the library itself: there are no accounts, the account is simply a pair of public and private keys, groups are also supported, the group ID is derived from a hash computed through the public keys of the members, since the hash process is irreversible, the level of anonymity is maximum).
// The publication of the source wants to demonstrate the genuineness of the concepts we have adopted! Thanks for your attention!
namespace EncryptedMessaging
{
    /// <summary>
    /// Indicates the operating mode, there are several flags to configure a custom server or client use for each situation
    /// </summary>    
    [Flags]
    public enum Modality
    {
        /// <summary>
        /// Indicates whether the TCP connection should expire (true), or be maintained stably. Mobile devices cannot keep the TCP connection permanently because the operating system can close it when the application runs in the backgrount.
        /// </summary>
        StayConnected = 1,
        /// <summary>
        /// Save contacts permanently. It is the typical client mode, the contacts that are added will be saved and can be reloaded when the application is restarted.
        /// This parameter in combination with RemoveUnusedContacts will ensure that when the session expires the contact will only be removed from the list (to free up memory), but will still remain in the storage ready to be reloaded when needed.
        /// </summary>
        SaveContacts = 2,
        /// <summary>
        /// Load contacts at startup: Indicates whether the contacts are to be loaded into the address book ad startup of application (pre load typical functions for client application).
        /// Whether at startup it should load the saved contacts (create the contacts directory), it is a typical mode for client systems which have an interface with the contacts book and are ready to communicate with them.
        /// </summary>
        LoadContacts = 4,
        /// <summary>
        /// It is a server mode to communicate with a contact, receiving it from him, when there is no more communication a timeout deletes him from the address book to free up memory. A server may need to initiate communications with a large number of contacts, in order not to occupy memory these are dynamically generated thanks to the client who sends his contact before establishing a communication: This operation is called "login", and expires with a timer. Conceptually is a similar mechanism to web browsing sessions.
        /// </summary>
        RemoveUnusedContacts = 8,
        /// <summary>
        /// This is the preconfigured mode to function as a Client
        /// </summary>
        Client = SaveContacts | LoadContacts,
        /// <summary>
        /// This is the preconfigured mode to function as a Server
        /// </summary>
        Server = StayConnected | RemoveUnusedContacts,
    }
    /// <summary>
    /// It is the context in which the messaging application runs. Under this instance you will find all the fundamental elements of the library
    /// </summary>
    public class Context : IDisposable
    {
        /// <summary>
        /// This method initializes the network.
        /// You can join the network as a node, and contribute to decentralization, or hook yourself to the network as an external user.
        /// To create a node, set the MyAddress parameter with your web address.If MyAddress is not set then you are an external user.
        /// </summary>
        /// <param name="entryPoint">The entry point server, to access the network</param>
        /// <param name="networkName">The name of the infrastructure. For tests we recommend using "testnet"</param>
        /// <param name="multipleChatModes">If this mode is enabled there will be multiple chat rooms simultaneously, all archived messages will be preloaded with the initialization of this library, this involves a large use of memory but a better user experience. Otherwise, only one char room will be managed at a time, archived messages will be loaded only when you enter the chat, this mode consumes less memory.</param>
        /// <param name="privateKeyOrPassphrase"></param>
        /// <param name="modality">Indicates if a server is initialized: The server has some differences compared to the device which are: It does not store contacts and posts (contacts are acquired during the session and when it expires they are deleted, so the contacts are not even synchronized on the cloud, there is no is the backup and restore that instead occurs for applications on devices).</param>
        /// <param name="connectivity">True if network is available (Pipe or Internet, depend of type of Entry Point)</param>
        /// <param name="invokeOnMainThread">Method that starts the main thread: Actions that have consequences with updating the user interface must run on the main thread otherwise they cause a crash</param>
        /// <param name="getSecureKeyValue">System secure function to read passwords and keys saved with the corresponding set function</param>
        /// <param name="setSecureKeyValue">System secure function for saving passwords and keys</param>
        /// <param name="getFirebaseToken">Function to get FirebaseToken (the function is passed and not the value, so as not to block the main thread as this sometimes takes a long time). FirebaseToken is used by firebase, to send notifications to a specific device. The sender needs this information to make the notification appear to the recipient.</param>
        /// <param name="getAppleDeviceToken">Function to get AppleDeviceToken (the function is passed and not the value, so as not to block the main thread as this sometimes takes a long time). In ios AppleDeviceToken is used to generate notifications for the device. Whoever sends the encrypted message needs this data to generate a notification on the device of who will receive the message.</param>
        /// <param name="cloudPath">Specify the location of the cloud directory (where it saves and reads files), if you don't want to use the system one. The cloud is used only in server mode</param>
        /// <param name="licenseActivator">If present, set up a license authentication system. There are several methods to authenticate the license in this regard see the class initializer notes of this parameter.</param>
        /// <param name="instanceId">If you want to initialize multiple instances of this component, a unique id must be assigned, if not assigned a progressive id will be assigned automatically. This value allows the recovery of the saved key when the application is restarted</param>
        public Context(string entryPoint, string networkName = "testnet", bool multipleChatModes = false, string privateKeyOrPassphrase = null, Modality modality = Modality.Client, bool? connectivity = null, Action<Action> invokeOnMainThread = null, Func<string, string> getSecureKeyValue = null, Storage.SetKeyValueSecure setSecureKeyValue = null, Func<string> getFirebaseToken = null, Func<string> getAppleDeviceToken = null, string cloudPath = null, OEM licenseActivator = null, string instanceId = null)
        {
            Session = new Dictionary<string, object>();
            try
            {
                EntryPoint = new UriBuilder(entryPoint).Uri;
            }
            catch (Exception inner)
            {
                var ex = new Exception("Invalid or empty entry point", inner);
                Console.WriteLine(ex.Message);
                throw ex;
            }
            Contexts.Add(this);
            if (instanceId == null)
            {
                var guid = Functions.CallerAssemblyId();
                if (!Counter.TryGetValue(guid, out var counter))
                    Counter[guid] = 0;
                Counter[guid]++;
                instanceId = guid + counter.ToString(CultureInfo.InvariantCulture);
            }
            // instanceId = Instances.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (connectivity != null)
                tmpConnectivity = connectivity;

            //Cloud.ReceiveCloudCommands.SetCustomPath(cloudPath, isServer);
            var runtimePlatform = Contact.RuntimePlatform.Undefined;

            var platform = Environment.OSVersion.Platform;
            if (platform == PlatformID.Win32Windows || platform == PlatformID.Win32NT || platform == PlatformID.WinCE || platform == PlatformID.Xbox)
            {
                var framework = RuntimeInformation.FrameworkDescription;
                if (Modality.HasFlag(Modality.StayConnected) && (framework.Contains(" 3.") || framework.Contains(" 5.")))
                {
                    Debug.WriteLine("You probably want to use this library for a server application but the StayConnected modality is not active");
                    Debugger.Break();
                }
                runtimePlatform = Contact.RuntimePlatform.Windows;
            }
            else if (platform == PlatformID.Unix || platform == PlatformID.MacOSX)
            {
                var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties().ToString().ToLower(CultureInfo.InvariantCulture);
                if (ipGlobalProperties.Contains(".android"))
                    runtimePlatform = Contact.RuntimePlatform.Android;
                else if (getAppleDeviceToken != null)
                    runtimePlatform = Contact.RuntimePlatform.iOS;
                else
                    runtimePlatform = Contact.RuntimePlatform.Unix;
            }
            RuntimePlatform = runtimePlatform;
            Modality = modality;
#if DEBUG
            var canKeepConnection = runtimePlatform != Contact.RuntimePlatform.Android && runtimePlatform != Contact.RuntimePlatform.iOS;
            if (modality.HasFlag(Modality.StayConnected) && !canKeepConnection)
                Debugger.Break(); // These mobile devices are not able to maintain the TCP connection, the operating system interrupts it when the application is in the backgrount
            if (cloudPath != null && modality.HasFlag(Modality.StayConnected))
                Debugger.Break(); // Set up cloud path functions for server applications only
#endif                
            SessionTimeout = modality.HasFlag(Modality.RemoveUnusedContacts) ? DefaultServerSessionTimeoutMs : Timeout.Infinite;
            NetworkId = Converter.BytesToInt(Encoding.ASCII.GetBytes(networkName));
            InvokeOnMainThread = invokeOnMainThread ?? ThreadSafeCalls;
            MessageFormat = new MessageFormat(this);
#if DEBUG_A
            privateKeyOrPassphrase = privateKeyOrPassphrase ?? PassPhrase_A;
#elif DEBUG_B
            privateKeyOrPassphrase = privateKeyOrPassphrase ?? PassPhrase_B;
#endif
            My = new My(this, getFirebaseToken, getAppleDeviceToken);

            SecureStorage = new Storage(instanceId, getSecureKeyValue, setSecureKeyValue);

            if (!string.IsNullOrEmpty(privateKeyOrPassphrase))
                My.SetPrivateKey(privateKeyOrPassphrase);

#if DEBUG_RAM
            //SecureStorage.Destroy();
#endif
            Setting = new Setting(this);
            Repository = new Repository(this);
            ContactConverter = new ContactConverter(this);

            Messaging = new Messaging(this, multipleChatModes);

            Contacts = new Contacts(this);

            Tuple<ulong, Func<byte[], byte[]>> license = null;
            if (licenseActivator != null)
                license = new Tuple<ulong, Func<byte[], byte[]>>(licenseActivator.IdOEM, licenseActivator.SignLogin);
            // *1* // If you change this value, it must also be changed on the server	
            Channel = new Channel(entryPoint, NetworkId, Messaging.ExecuteOnDataArrival, Messaging.OnDataDeliveryConfirm, My.Id, modality.HasFlag(Modality.StayConnected) ? Timeout.Infinite : 120 * 1000, license, ExecuteOnErrorChannel)
            {
                OnRouterConnectionChange = InvokeOnRouterConnectionChange
            };
            IsRestored = !string.IsNullOrEmpty(privateKeyOrPassphrase);
            if (!IsDisposed)
                ThreadPool.QueueUserWorkItem(RunAfterInstanceCreate);
        }


        public Action<byte[]> OnDataRouter { set { Channel.OnDataRouter = value; } }

        public bool LicenseExpired { get { return Channel != null && Channel.LicenseExpired; } }

        /// <summary>
        /// A temporary key and value registry made available to the host to temporarily store values
        /// </summary>
        public Dictionary<string, object> Session { get; private set; }
        static private bool? tmpConnectivity;
        /// <summary>
        /// Delegate for the action to be taken when messages arrive
        /// </summary>
        /// <param name="message">Message</param>
        public delegate void OnMessageArrived(Message message);
        /// <summary>
        /// Delegate that runs automatically when messages are received. On systems that have a stable connection (server or desktop), this event can be used to generate notifications.
        /// Note: Only messages that have viewable content in the chat trigger this event
        /// </summary>
        public event OnMessageArrived OnNotification;
        internal void OnNotificationInvoke(Message message)
        {
            OnNotification?.Invoke(message);
        }
        /// <summary>
        /// Communication event manager. Set up an action for this handler to get feedback on communication errors
        /// </summary>
        public OnErrorEvent OnCommunicationErrorEvent; // { get => Channel.OnError; set => Channel.OnError = value; }

        private void ExecuteOnErrorChannel(Channel.ErrorType errorType, string description)
        {
            OnCommunicationErrorEvent?.Invoke(errorType, description);
        }

        /// <summary>
        /// Function that acts as an event and will be called when the connection was successful and the client is logged into the router (return true), or when the connection with the router is lost (return false). You can set this action as an event.
        /// </summary>
        public Action<bool> OnRouterConnectionChange { private get; set; }
        // public Action<bool> OnRouterConnectionChange { set => Channel.OnRouterConnectionChange = value; }

        private void InvokeOnRouterConnectionChange(bool statusConnection)
        {
            if (statusConnection)
                Messaging.SendMessagesInQueue();
            else
                Contacts.Logout();
            OnRouterConnectionChange?.Invoke(statusConnection);
        }

        /// <summary>
        /// Function delegated with the event that creates the message visible in the user interface. This function will then be called whenever a message needs to be drawn in the chat. Server-type host systems that don't have messages to render in chat probably don't need to set this action
        /// </summary>
        /// <param name="message">The message to render in the chat view</param>
        /// <param name="isMyMessage">True if you call it to render my message</param>
        public delegate void ViewMessageUi(Message message, bool isMyMessage);
        /// <summary>
        /// It is the function delegate who writes a message in the chat. This function must be set when the App() class is initialized in the common project.
        /// </summary>
        public event ViewMessageUi ViewMessage;
        internal void ViewMessageInvoke(Message message, bool isMyMessage)
        {
            ViewMessage?.Invoke(message, isMyMessage);
        }

        //public delegate void OnContactEventAction(Message message);

        /// <summary>
        /// This delegate allows you to set up a event that will be called whenever a system message arrives. Messages that have a graphical display in the chat do not trigger this event.
        /// Use OnMessageArrived to intercept incoming messages that have a content display in the chat
        /// </summary>
        public event Action<Message> OnContactEvent;
        internal void OnContactEventInvoke(Message message)
        {
            if (OnContactEvent != null)
                if (!Modality.HasFlag(Modality.LoadContacts))
                    try
                    {
                        OnContactEvent.Invoke(message);
                    }
                    catch (Exception ex)
                    {
                        Debugger.Break(); // investigate                            
                    }
                else
                    InvokeOnMainThread(() =>
                    {
                        try
                        {
                            OnContactEvent.Invoke(message);
                        }
                        catch (Exception ex)
                        {
                            Debugger.Break(); // investigate                            

                        }
                    });
        }
        /// <summary>
        /// Event that is raised to inform when someone has read a sent message
        /// </summary>
        /// <param name="contact">Contact (group or single user))</param>
        /// <param name="participantId">ID of participant who has read</param>
        /// <param name="lastRadTime">When the last reading took place</param>
        public delegate void LastReadedTimeChangeEvent(Contact contact, ulong participantId, DateTime lastRadTime);
        /// <summary>
        /// Event that is performed when a contact reads a message that has been sent
        /// </summary>              
        public event LastReadedTimeChangeEvent OnLastReadedTimeChange;
        internal void OnLastReadedTimeChangeInvoke(Contact contact, ulong participantId, DateTime lastRadTime)
        {
            if (OnLastReadedTimeChange != null)
                InvokeOnMainThread(() => OnLastReadedTimeChange.Invoke(contact, participantId, lastRadTime));
        }
        /// <summary>
        /// Delegate for the event that notifies when messages are sent
        /// </summary>
        /// <param name="contact">Contact (group or single user)</param>
        /// <param name="deliveredTime">When message was delivered.</param>
        /// <param name="isMy">Boolean</param>
        public delegate void MessageDeliveredEvent(Contact contact, DateTime deliveredTime, bool isMy);

        /// <summary>
        /// Event that occurs when a message has been sent. Use this event to notify the host application when a notification needs to be sent to the recipient.
        /// </summary>
        public event MessageDeliveredEvent OnMessageDelivered;

        internal void OnMessageDeliveredInvoke(Contact contact, DateTime deliveredTime, bool isMy)
        {
            if (OnMessageDelivered != null)
                InvokeOnMainThread(() => OnMessageDelivered?.Invoke(contact, deliveredTime, isMy));

        }

        /// <summary>
        /// thread-safe calls
        /// </summary>
        /// <param name="action">trigger event</param>
        private void ThreadSafeCalls(Action action)
        {
#if DEBUG // In debug mode errors will stop execution at the point of error to facilitate diagnosis.
            action.Invoke();
#else
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
            }
#endif
        }

#if DEBUG_A
        internal const string PassPhrase_A = "team grief spoil various much amount average erode item ketchup keen path"; // It is a default key for debugging only, used for testing, it does not affect security
        internal const string PubK_B = "AjRuC/k3zaUe0eXbqTyDTllvND1MCRkqwXThp++OodKw";
#elif DEBUG_B
		internal const string PassPhrase_B = "among scan notable siren begin gentle swift move melody album borrow october"; // It is a default key for debugging only, used for testing, it does not affect security
		internal const string PubK_A = "A31zN58YQFk78iIGE0hJKtht4gUVwF+fCeOMxV2NEsOH";
#endif

        private readonly bool IsRestored;

        /// <summary>
        /// The type of connection that is used to connect to the router
        /// </summary>
        public ConnectivityType ConnectivityType => EntryPoint.Scheme.Equals(nameof(ConnectivityType.Pipe), StringComparison.OrdinalIgnoreCase) ? ConnectivityType.Pipe : ConnectivityType.Internet;

        /// <summary>
        /// What to do when the context instance has been created and released
        /// Do not put instructions here that send messages (otherwise the application crashes due to isReady which will remain false)
        /// </summary>
        /// <param name="obj"></param>
        private void RunAfterInstanceCreate(object obj)
        {
#if DEBUG
            AfterInstanceThread = Thread.CurrentThread;
#endif
            Contacts.LoadContacts(IsRestored);
#if DEBUG
            AfterInstanceThread = null;
#endif
            if (!OnConnectivityChangeIsAssigned) // Static variable that prevents multiple assignments
            {
                OnConnectivityChangeIsAssigned = true;
                NetworkChange.NetworkAvailabilityChanged += (sender, e) => OnConnectivityChange(e.IsAvailable, ConnectivityType.Internet);
            }
            IsInitialized = true;
            lock (Contexts)
            {
                if (ConnectivityType == ConnectivityType.Pipe)
                    SetConnectivity(tmpConnectivity ?? CurrentConnectivity[ConnectivityType.Pipe] ?? false, ConnectivityType.Pipe);
                else
                    SetConnectivity(tmpConnectivity ?? CurrentConnectivity[ConnectivityType.Internet] ?? NetworkInterface.GetIsNetworkAvailable(), ConnectivityType.Internet);
            }
            if (!IsRestored)
                My.CheckUpdateTheNotificationKeyToMyContacts();
            else if (CloudManager != null)
                Contacts.RestoreMyContactFromCloud();

            OnContextIsInitialized?.Invoke(this);
        }
        private bool IsInitialized;
#if DEBUG
        internal static Thread AfterInstanceThread;
#endif

        /// <summary>
        /// Function that is called when the context has been fully initialized.
        /// If you want to automate something after context initialization, you can do so by assigning an action to this value!
        /// </summary>
        public static event Action<Context> OnContextIsInitialized;

        //internal bool IsReady
        //{
        //    get => Messaging != null && Messaging.SendMessageQueue != null;
        //    private set => Messaging.SendMessagesInQueue();
        //}

        private static readonly List<Context> Contexts = new List<Context>();

        internal static readonly Dictionary<ConnectivityType, bool?> CurrentConnectivity = new Dictionary<ConnectivityType, bool?>
                            {
                                { ConnectivityType.Pipe, null },
                                { ConnectivityType.Internet, null }
                            };
        /// <summary>
        /// The entry point server, to access the network
        /// </summary>
        public Uri EntryPoint { get; private set; }
        static private bool OnConnectivityChangeIsAssigned;
        /// <summary>
        /// Function that must be called whenever the host system has a change of state on the connection. This parameter must be set when starting the application.
        /// If it is not set, the libraries do not know if there are changes in the state of the Internet/Pipe connection, and the messages could remain in the queue without being sent.
        /// </summary>
        /// <param name="connectivity">Network connection status true or false</param>
        /// <param name="connectivityType">The type of connection that is used to connect to the router</param>
        public static void OnConnectivityChange(bool connectivity, ConnectivityType connectivityType)
        {
            lock (Contexts)
            {
                SetConnectivity(connectivity, connectivityType);
            }
        }

        private static void SetConnectivity(bool connectivity, ConnectivityType connectivityType)
        {
            Task.Run(() =>
            {
                lock (CurrentConnectivity)
                {

                    // If state hasn't changed, exit early
                    //if (CurrentConnectivity[connectivityType] == connectivity)
                    //    return;
                    bool isChanged = false;
                    if (CurrentConnectivity[connectivityType] != connectivity)
                    {
                        // Update connectivity state
                        CurrentConnectivity[connectivityType] = connectivity;
                        isChanged = true;
                    }
                    // Set access properties based on connection type
                    if (connectivityType == ConnectivityType.Pipe)
                    {
                        PipeAccess = connectivity;
                    }
                    else // Internet
                    {
                        InternetAccess = connectivity && CheckRealInternetConnectivity();
                        InstancedTimeUtc = DateTime.UtcNow;
                    }
                    if (!isChanged)
                        return;
                    // Notify all affected context instances
                    NotifyContexts(connectivityType, connectivity);
                }
            });
        }

        private static bool CheckRealInternetConnectivity()
        {
            // On Android, use simple connectivity check
            if (Functions.CurrentPlatform == Contact.RuntimePlatform.Android)
                return Functions.IsInternetAvailable();

            // For other platforms, verify by getting GMT time
            Time.GetCurrentTimeGMT(out var error);
            if (error != null)
                return true;

            // Fallback to basic connectivity check
            return Functions.IsInternetAvailable();
        }

        private static void NotifyContexts(ConnectivityType connectivityType, bool connectivity)
        {
            lock (Contexts)
            {
                foreach (var context in Contexts)
                {
                    // Only notify contexts using this connection type when connectivity is lost
                    if (context.ConnectivityType == connectivityType && !connectivity)
                    {
                        context.OnCommunicationErrorEvent?.Invoke(ErrorType.ConnectionFailure, connectivityType == ConnectivityType.Internet ? "No or unstable Internet connection!" : "Pipe connection failed!");
                    }
                }
            }

            // For Internet connections, schedule recheck if disconnected
            if (connectivityType == ConnectivityType.Internet && !connectivity)
            {
                CheckInternetConnectivity.Change(60000, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Indicates when the object was instantiated
        /// </summary>
        public static DateTime InstancedTimeUtc { get; private set; }
        private static Timer CheckInternetConnectivity = new Timer((o) => { OnConnectivityChange(true, ConnectivityType.Internet); }, null, Timeout.Infinite, Timeout.Infinite);
        private static readonly Dictionary<Guid, int> Counter = new Dictionary<Guid, int>();
        internal readonly Contact.RuntimePlatform RuntimePlatform;

        /// <summary>
        /// Setting settings for library operation
        /// </summary>
        public Setting Setting { get; private set; }
        /// <summary>
        /// Provides methods to save objects and variables in an encrypted and secure way. You are encouraged to use the features provided to save all the data generated by the application in order to increase privacy and infromatic security.
        /// </summary>
        public Storage SecureStorage { get; private set; }
        internal readonly ContactConverter ContactConverter;
        internal readonly Repository Repository;
        internal readonly MessageFormat MessageFormat;
        /// <summary>
        /// Provides the functionality for sending messages, in various formats
        /// </summary>
        public Messaging Messaging { get; private set; }
        /// <summary>
        /// Server mode is the exclusive mode for working without saving posts to the repositories. This is how server applications must be initialized. This property returns the status of the server mode.
        /// </summary>
        public Modality Modality { get; }
        internal static readonly int DefaultServerSessionTimeoutMs = (int)new TimeSpan(0, 20, 0).TotalMilliseconds;
        /// <summary>
        /// Session timeout in milliseconds
        /// </summary>
        internal readonly int SessionTimeout;

        /// <summary>
        /// The salient information about this client and the account
        /// </summary>
        public My My { get; private set; }

        /// <summary>
        /// Contact book
        /// </summary>
        public Contacts Contacts { get; private set; }
        //public delegate void AlertMessage(string text);
        //public delegate bool ShareTextMessage(string text);
        // Through this we can program an action that is triggered when a message arrives from a certain chat id

        internal readonly int NetworkId;
        [Obsolete("Do not use this object externally, its public access will be removed in the future!")]
        /// <summary>
        /// Provides access to the library instance for transporting data between devices and packet routing routers
        /// </summary>
        public readonly Channel Channel;
        /// <summary>
        /// The latest reception on the data channel (utc)
        /// </summary>
        public DateTime LastIN => Channel.LastIN;
        /// <summary>
        /// The latest protocol command reception on the data channel
        /// </summary>
        public Protocol.Command LastCommandIN => Channel.LastCommandIN;
        /// <summary>
        /// The last transmission on the data channel (utc)
        /// </summary>
        public DateTime LastOUT => Channel.LastOUT;
        /// <summary>
        /// The last protocol command transmission on the data channel
        /// </summary>
        public Protocol.Command LastCommandOUT => Channel.LastCommandOUT;
        /// <summary>
        /// The last time KeepAlive was performed for checking the data communication channel (Utc)
        /// </summary>
        public DateTime LastKeepAliveCheck => Channel.LastKeepAliveCheck;

        /// <summary>
        /// Number of failure connection
        /// </summary>
        public ulong KeepAliveFailures => Channel.KeepAliveFailures;
        /// <summary>
        /// Returns the current status of the connection with the router/server
        /// </summary>
        public bool IsConnected => Channel != null && Channel.IsConnected;
        /// <summary>
        /// Function that reactivates the connection when it is lost. Its use is designed for all those situations in which the connection could be interrupted, for example mobile applications can interrupt the connection when they are placed in the background. When the application returns to the foreground it is advisable to call this comondo to reactivate the connection.      
        /// If this method is not called, the mobile application returns to the foreground, it could stop working and stop receiving messages, while notifications could arrive anyway if routed with Firebase or other external services.
        /// </summary>
        /// <param name="iMSureThereIsConnection"></param>
        public static void ReEstablishInternetConnection(bool iMSureThereIsConnection = false)
        {
            if (iMSureThereIsConnection)
                Functions.TrySwitchOnInternetConnectivity();
            Channel.ReEstablishConnection();
        }

        /// <summary>
        /// Use this property to call the main thread when needed:
        /// The main thread must be used whenever the user interface needs to be updated, for example, any operation on an ObservableCollection that changes elements must be done by the main thread,  otherwise rendering on the graphical interface will generate an error.
        /// </summary>
        public Action<Action> InvokeOnMainThread { get; private set; }

        internal ICloudManager CloudManager;

        /// <summary>
        /// Set up a cloud during context initialization if you want to use cloud features to save avatarms, contacts and other data 
        /// </summary>
        /// <param name="cloudManager">The class that allows you to manage Cloud features</param>
        public void AddCloudManager(ICloudManager cloudManager)
        {
            var cloudAppId = BitConverter.ToUInt16(Encoding.ASCII.GetBytes("cloud"), 0);
            void OnMessageReceived(Message message)
            {
                if (message.Contact.ChatId == CloudManager.Cloud.ChatId) // Security filter
                {
                    if (message.Type == MessageFormat.MessageType.SubApplicationCommandWithData || message.Type == MessageFormat.MessageType.SubApplicationCommandWithParameters)
                    {
                        message.GetSubApplicationCommand(out var appId, out var command, out var parameters);
                        if (appId == cloudAppId)
                        {
                            cloudManager.OnCommand(command, parameters?.ToArray());
                        }
                    }
                }
            }
            void SendCommand(ushort command, byte[][] parameters) => Messaging.SendCommandToSubApplication(CloudManager.Cloud, cloudAppId, command, false, true, parameters);
            cloudManager.SendCommand = SendCommand;
            OnContactEvent += OnMessageReceived; // Intercept messages for the cloud client
            CloudManager = cloudManager;
        }
        private bool IsDisposed;
        /// <summary>
        /// Protected implementation of Dispose pattern.
        /// </summary>
        /// <param name="disposing">True to disposing</param>
        protected virtual void Dispose(bool disposing)
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;
            if (disposing)
            {
                this.OnRouterConnectionChange?.Invoke(false);
                this.OnRouterConnectionChange = null;
                this.OnCommunicationErrorEvent = null;
                this.OnContactEvent = null;
                this.OnLastReadedTimeChange = null;
                this.OnMessageDelivered = null;
                this.OnNotification = null;
                this.ViewMessage = null;
                SecureStorage.Dispose();
                Contacts.Dispose();
                Channel.Dispose();
                Contexts.Remove(this);
            }
        }
        /// <summary>
        /// Public implementation of Dispose pattern callable by consumers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
