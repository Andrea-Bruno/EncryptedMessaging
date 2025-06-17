using CommunicationChannel.DataConnection;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static CommunicationChannel.Channel;
using static CommunicationChannel.CommandsForServer;

namespace CommunicationChannel.DataIO
{
    /// <summary>
    /// This class is used for establishing link connection and communicating with the server.
    /// </summary>
    internal partial class DataIO : IDisposable
    {
        internal DataIO(Channel channel)
        {
            Channel = channel;
            TryReconnection = new Timer(OnTryReconnection);
            TimerAutoDisconnect = new Timer((o) => Disconnect(false));
            TimerKeepAlive = new Timer(OnTimerKeepAlive);
        }

        private void SetNewClient()
        {
            if (Channel.TypeOfConnection == ConnectivityType.Pipe)
                Client = new NamedPipeClientConnection();
            else
                Client = new TcpClientConnection();


        }

        internal readonly Channel Channel;
        internal const int MaxDataLength = 16000000; //16 MB - max data length enable to received the server
        internal IDataConnection Client;

        ///// <summary>
        ///// Send the data, which will be parked in the spooler, cannot be forwarded immediately: If there is a queue or if there is no Internet line the data will be parked.
        ///// </summary>
        ///// <param name="data">Data to be sent</param>
        //public void SendData(byte[] data)
        //{
        //    Channel.DataIO.ExecuteSendData(data);
        //}

        // private const int TimeOutMs = 10000; // Default value
        private const int TimeOutMs = TimerIntervalCheckConnection - 1000; // Experimental value: Some time cannot connect, I have increase this value
        private const double LimitMbps = 0.01;
        internal AutoResetEvent WaitConfirmationSemaphore = new AutoResetEvent(false);

        internal List<(byte[], DataFlags)> _sendQueue = new List<(byte[], DataFlags)>();
        /// <summary>
        /// Send data to server (router) without going through the spooler
        /// </summary>
        /// <param name="data">Data to be sent</param>
        /// <param name="flag">Indicates some settings that the server/router will have to interpret</param>
        internal void ExecuteSendData(byte[] data, DataFlags flag = DataFlags.None)
        {
            if (data.Length > MaxDataLength)
            {
                Debugger.Break(); // Data too long, sending not supported. Investigate what is sending this data!
                return;
            }

#if DEBUG && !TEST
            if (_sendQueue.Count > 5)
                Debugger.Break(); // Queue is too big, investigate why!
#endif
            lock (_sendQueue)
                if (flag == DataFlags.None)
                {
                    _sendQueue.Add((data, flag));
                }
                else
                {
                    _sendQueue.Insert(0, (data, flag));
                }
            ProcessSendQueue();
        }

        private Task TaskSendQueue;

        internal void ProcessSendQueue()
        {
            bool TryDequeue([NotNullWhen(true)] out (byte[], DataFlags) element)
            {
                lock (_sendQueue)
                {
                    if (_sendQueue.Count > 0)
                    {
                        element = _sendQueue[0];
                        _sendQueue.RemoveAt(0);
                        return true;
                    }
                }
                element = default;
                return false;
            }
            if (TaskSendQueue != null || _sendQueue.Count == 0)
                return;

            TaskSendQueue = Task.Run(() =>
            {
                var error = ErrorType.None;

                while (TryDequeue(out var item) && error == ErrorType.None)
                {
                    var data = item.Item1;
                    var flag = item.Item2;
                    error = _ExecuteSendData(data, flag);
                    if (error != ErrorType.None) // if sen data error  for non-direct data transmission without spoilers
                    {
                        InvokeError(ErrorType.SendDataError, error.ToString());
                        Disconnect();

                        // Re-enters the data into the spooler
                        if (flag == DataFlags.None)
                        {
                            lock (_sendQueue)
                            {
                                _sendQueue.Insert(0, (data, flag));
                            }
                        }
                        break; // exit from while
                    }
                    if (flag == DataFlags.None)
                    {
                        // This is data that requires confirmation from the receipt!
                        // Do not send next data: Wait for confirmation before sending next messages, if the message requires a confirmation response
                        break;
                    }
                }
                TaskSendQueue = null;
            });
        }


        private int LoginCounter;

        private ErrorType _ExecuteSendData(byte[] data, DataFlags flag = DataFlags.None)
        {
            var command = (Protocol.Command)data[0];
            var dataLength = data.Length;
            if (!Logged)
            {
                var sendEvenWithoutLogin = dataLength > 0 && (command == Protocol.Command.ConnectionEstablished || command == Protocol.Command.DataReceivedConfirmation);
                if (!sendEvenWithoutLogin)
                {
                    throw new Exception("You cannot send data before login is done!"); // Don't send any data before login is done!
                }
            }
#if (DEBUG)
            if (command == Protocol.Command.ConnectionEstablished)
            {
                LoginCounter++;
                if (LoginCounter > 1)
                {
                    Debugger.Break();
                }
            }
#endif

            if (!IsConnected())
            {
                return ErrorType.ConnectionClosed;
            }
            else
            {
                Channel.LastCommandOUT = command;
                if (command != Protocol.Command.Ping)
                    SuspendAutoDisconnectTimer();
                var waitConfirmation = flag == DataFlags.None && command != Protocol.Command.DataReceivedConfirmation;
                try
                {
                    var stream = Client.GetStream();
                    uint bit32 = flag == DataFlags.DirectlyWithoutSpooler ? 0b10000000_00000000_00000000_00000000U : 0;
                    int timeoutMs = DataTimeout(data.Length);
                    if (waitConfirmation)
                        WaitConfirmationSemaphore.Reset();
                    stream.Write(((uint)dataLength | bit32).GetBytes(), 0, 4);
                    Channel.LastOUT = DateTime.UtcNow;
                    var watch = Stopwatch.StartNew();
                    stream.Write(data, 0, data.Length);
                    timeoutMs = timeoutMs - (int)watch.ElapsedMilliseconds; // remaining timeout
                    timeoutMs = timeoutMs < 0 ? 0 : timeoutMs;

#if DEBUG && !TEST
                    timeoutMs = Timeout.Infinite;
#endif

                    if (waitConfirmation && !WaitConfirmationSemaphore.WaitOne(timeoutMs))
                    {
                        // Timeout!
                        if (command == Protocol.Command.ConnectionEstablished)
                        {
                            Channel.LicenseExpired = true;
                            Console.WriteLine(DateTime.UtcNow.ToString("G") + " Client id " + Channel.MyId + " Unable to connect to router:");
                            Console.WriteLine("Has the license expired?");
                            Console.WriteLine("The router is off-line?");
                        }
#if DEBUG && !TEST
                        Debugger.Break(); // Timeout! license expired or router off-line
#endif
                        return ErrorType.LoginTimeout;
                    }
                    if (command != Protocol.Command.Ping)
                        ResumeAutoDisconnectTimer();
                }
                catch (Exception ex)
                {
                    return ErrorType.SendDataError;
                }
            }
            return ErrorType.None;
        }


        internal bool Logged;

        private void OnConnected()
        {
            Channel.LicenseExpired = false;
            TryReconnection.Change(Timeout.Infinite, Timeout.Infinite); // Stop check if connection is lost
            ResumeAutoDisconnectTimer();
            BeginRead(Client);
            var login = Channel.CommandsForRouter.CreateCommand(Protocol.Command.ConnectionEstablished, null, null, Channel.MyId); // log in
            watchLogin.Restart();
            ExecuteSendData(login, DataFlags.DirectlyWithoutSpooler);
        }


        internal void OnLoggedCompleted()
        {

            watchLogin.Stop();
#if DEBUG && !TSET
            var alletsLimitMs = Client is TcpClientConnection ? 500 : 15000;
            if (watchLogin.Elapsed.TotalMilliseconds > alletsLimitMs)
                Debugger.Break();
#endif        

            var timer = new System.Timers.Timer(LoginSleepTime);
            timer.Elapsed += (sender, e) =>
            {
                timer.Dispose();
                Logged = true;
                Channel.ConnectionChange(true);
                ProcessSendQueue();
            };
            timer.AutoReset = false;
            timer.Start();
        }


        Stopwatch watchLogin = new Stopwatch();

        const int LoginSleepTime = 1500;

        private void StartLinger(out Exception exception)
        {
            exception = null;
            try
            {
                int? port = null;
                string address = null;
                if (Channel.ServerUri.Scheme == Uri.UriSchemeHttp || Channel.ServerUri.Scheme == Uri.UriSchemeHttps)
                {
                    var addresses = Dns.GetHostAddresses(Channel.ServerUri.Host).Reverse().ToArray();
                    port = Channel.ServerUri.Port;
                    address = addresses[0].ToString();
                }
                else
                {
                    address = Channel.ServerUri.Host;
                    if (Channel.ServerUri.Host.Contains(":"))
                        port = Channel.ServerUri.Port;
                }

                SetNewClient();

                var watch = Stopwatch.StartNew();
                if (!Client.Connect(address, port, TimeOutMs)) // ms timeout
                {
                    watch.Stop();
                    if (watch.Elapsed.TotalMilliseconds >= TimeOutMs)
                        exception = new Exception("Unable to connect to router: Probably firewall on router on port " + port + " or the router does not run");
                    else
                        exception = new Exception("Failed to connect");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
            }
            catch (Exception ex)
            {
                if (ex is SocketException se)
                {
                    exception = new Exception("Socket exception: " + se.SocketErrorCode.ToString());
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else if (ex.HResult == -2147467259)
                {
                    exception = new Exception("Wrong entry point! There is no DNS/IP association with the specified entry point.");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else if (ex.HResult == -2146233088)
                {
                    exception = new Exception("The router is off or unreachable");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else
                {
                    exception = ex;
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
            }
        }

        /// <summary>
        /// Start reading the stream by reading the first 4 bytes which contain the length of the data packet to read
        /// </summary>
        /// <param name="client"></param>
        private void BeginRead(IDataConnection client)
        {
            Stream stream;
            try
            {
                if (client == null)
                {
                    Disconnect();
                    return;
                }

                stream = client.GetStream();
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
                return;
            }

            try
            {
                var stateObject = Tuple.Create(stream, client);
                Tuple<Stream, byte[], IDataConnection> state = Tuple.Create(stream, First4bytes, client);
                stream.BeginRead(First4bytes, 0, 4, OnReadLength, state);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
            }
        }

        private byte[] First4bytes = new byte[4];


        void OnReadLength(IAsyncResult result)
        {
            var state = (Tuple<Stream, byte[], IDataConnection>)result.AsyncState;
            var stream = state.Item1;
            var client = state.Item3;

            var bytesRead = 0;
            try
            {
                bytesRead = stream.EndRead(result);
            }
            catch (Exception ex)
            {
                if (ex.HResult == -2146232798)
                    Channel.OnTcpError(ErrorType.ConnectionClosed, "The timer has closed the connection");
                else
                    Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Debug.WriteLine(client.IsConnected.ToString());
                Disconnect();
                return;
            }

            if (bytesRead != 4)
            {
                Channel.OnTcpError(ErrorType.WrongDataLength, "BeginRead: bytesRead != 4");
                Disconnect();
                return;
            }
            else
            {
                var firstUint = BitConverter.ToUInt32(First4bytes, 0);
                var dataLength = (int)(0B00111111_11111111_11111111_11111111U & firstUint);
                if (dataLength > MaxDataLength)
                {
                    Channel.OnTcpError(ErrorType.WrongDataLength, "Data length over the allowed limit");
                    Disconnect();
                    return;
                }

                var directlyWithoutSpooler = (firstUint & 0b10000000_00000000_00000000_00000000U) != 0;
                DataFlags flag = DataFlags.None;
                if (directlyWithoutSpooler)
                    flag = DataFlags.DirectlyWithoutSpooler;
                ReadBytes(dataLength, stream, flag);
            }
        }

        /// <summary>
        /// Read incoming data packet
        /// </summary>
        /// <param name="lengthIncomingData">Length of incoming packet</param>
        /// <param name="stream">Data stream for reading incoming data</param>
        /// <param name="flag">Information on the type of data interpretation (for more information see the description of the items in the enumerator)</param>
        private void ReadBytes(int lengthIncomingData, Stream stream, DataFlags flag)
        {
            Protocol.Command command = default;
            byte[] data;
            try
            {
                data = new byte[lengthIncomingData];
                var readDataLen = 0;
                Debug.WriteLine("Start download" + lengthIncomingData);
                var watch = Stopwatch.StartNew();
                var first = true;
                int remaining;
                var timeOutAt = DateTime.UtcNow.AddMilliseconds(DataTimeout(lengthIncomingData));
                while (readDataLen < lengthIncomingData)
                {
                    int partialLength;
                    if (first == true)
                        partialLength = 1;
                    else
                    {
                        partialLength = lengthIncomingData - readDataLen;
                        if (partialLength > 65536)
                            partialLength = 65536;
                    }

                    remaining = lengthIncomingData - readDataLen;
                    if (partialLength > remaining)
                        partialLength = remaining;
                    //if (stream.CanTimeout)
                    //{
                    //    stream.ReadTimeout = DataTimeout(partialLength);
                    //}

                    readDataLen += stream.Read(data, readDataLen, partialLength); //Avoid asynchronous method to overload the operation with asynchronous mode management
                    if (first == true && readDataLen > 0)
                    {
                        first = false;
                        if (!Enum.IsDefined(typeof(Protocol.Command), data[0]))
                            throw new Exception("Command not supported");
                        command = (Protocol.Command)data[0];
                        Channel.LastCommandIN = command;
                        if (command != Protocol.Command.Ping)
                            SuspendAutoDisconnectTimer();
                    }

                    Channel.LastIN = DateTime.UtcNow;
#if !DEBUG || TEST
                    if (DateTime.UtcNow >= timeOutAt)
                        throw new Exception("Data read timeout");
#endif
                }

                Debug.WriteLine("End download" + lengthIncomingData);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
                return;
            }

            Channel.OnDataReceives(data, flag, out var error, out var _);
            if (error != null)
            {
#if DEBUG && !TEST
                Debugger.Break();
#endif
                Channel.OnTcpError(error.Item1, error.Item2);
                Disconnect();
                return;
            }

            if (command != Protocol.Command.Ping)
                ResumeAutoDisconnectTimer();
            BeginRead(Client);
            //new Task(() => BeginRead(Client)).Start();
        }

        /// <summary>
        /// Calculate a reasonable timeout (in milliseconds) for transmitting a data packet, based on the length of the transmitted data.
        /// </summary>
        /// <param name="dataLength">The length of the data on which to calculate the timeout</param>
        /// <returns>Milliseconds</returns>
        private static int DataTimeout(int dataLength)
        {
            var mb = (double)dataLength / 1000000;
            return 15000 + Convert.ToInt32(mb / LimitMbps);
        }

        internal void InvokeError(ErrorType errorId, string description) => Channel.OnTcpError(errorId, description);

        /// <summary>
        /// Establish the connection and start the spooler
        /// </summary>
        /// <returns>Returns true if the connection is successful, false if there is an error</returns>
        /// <summary>
        /// Establish the connection and start the spooler
        /// </summary>
        /// <returns>Returns true if the connection is successful, false if there is an error</returns>
        internal bool Connect()
        {
            Exception exception = null;
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection); // antiConcurrentConnection++;                
                if (!_disposed && !IsConnected() && Channel.Connectivity)
                {
                    StartLinger(out exception);
                    if (exception != null)
                    {
                        Channel.OnTcpError(ErrorType.ConnectionFailure, exception.Message);
                    }
                    else
                    {
                        OnConnected();
                        KeepAliveStart();
                    }
                }
                Interlocked.Decrement(ref antiConcurrentConnection); // antiConcurrentConnection--;       
            }
            if (exception != null)
                Disconnect();
            return exception == null;
        }

        /// <summary>
        /// Used to make a connection if the communication link breaks.
        /// </summary>
        /// <param name="tryConnectAgain"></param>
        public void Disconnect(bool tryConnectAgain = true)
        {
#if (DEBUG)
            LoginCounter = 0; // Reset login counter
#endif
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection); // antiConcurrentConnection++;       
                if (Client != null) // Do not disconnect again
                {
                    Logged = false;
                    KeepAliveStop();
                    SuspendAutoDisconnectTimer();
                    Client?.Disconnect();
                    Client?.Dispose();
                    Client = null;
                    Channel.ConnectionChange(false);
                }
                _sendQueue.Clear();
                if (tryConnectAgain && !_disposed)
                    TryReconnection.Change(TimerIntervalCheckConnection, Timeout.Infinite); // restart check if connection is lost
                Interlocked.Decrement(ref antiConcurrentConnection); // antiConcurrentConnection--;       
            }
        }

        int antiConcurrentConnection = 0;

        /// <summary>
        /// Find if the socket is connected to the remote host.
        /// </summary>
        /// <returns></returns>
        public bool IsConnected()
        {
            return Client != null && Client.IsConnected && Channel.Connectivity; //According to the specifications, the property _client.Connected returns the connection status based on the last data transmission. The server may not be connected even if this property returns true
        }


    }
}