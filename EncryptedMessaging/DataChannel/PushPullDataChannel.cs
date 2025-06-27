using CommunicationChannel;
using System;
using System.IO;
using System.Net;
using static CommunicationChannel.Channel;
using static CommunicationChannel.CommandsForServer;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Net.Http;
namespace EncryptedMessaging.DataChannel
{
    internal class PushPullDataChannel : ChannelBase, IDisposable, IChannel
    {
        /// <param name="hasConnectivity">Indicates the presence of internet necessary for the channel to establish the connection.</param>
        /// <param name="serverAddress">Server Address</param>
        /// <param name="domain">A domain (also known as Network Id) corresponds to a membership group. Using the domain it is possible to divide the traffic on a server into TestNet, MainNet group (in order to isolate the message circuit within a given domain).</param>
        /// <param name="onMessageArrives">Event that is raised when a message arrives.</param>
        /// <param name="onDataDeliveryConfirm">Event that is generated when the router (server) has received the outgoing message, This element returns the message in raw format</param>
        /// <param name="myId">The identifier of the current user. Since the server system is focused on anonymity and security, there is no user list, it is a cryptographic id generated with a hashing algorithm</param>
        /// <param name="connectionTimeout">Used to remove the connection when not in use. However, mobile systems remove the connection when the application is in the background so it makes no sense to try to keep the connection always open. This also lightens the number of simultaneous server-side connections.</param>
        /// <param name="licenseActivator">OEM ID (ulong) and algorithm for the digital signature of the license activation. If present, this function will be called to digitally sign at the time of authentication. The digital signature must be put by the OEM who must have the activation licenses. The router will check if the license is valid upon connection.</param>
        /// <param name="onError">It is used as an event to handle the reporting of errors to the host. If set in the initialization phase, this delegate will be called at each data IO error (TCP, Pipe, etc..), to notify the type of error and its description</param>   
        public PushPullDataChannel(bool? hasConnectivity, string serverAddress, int domain, Action<ulong, byte[]> onMessageArrives, Action<uint> onDataDeliveryConfirm, ulong myId, int connectionTimeout = Timeout.Infinite, Tuple<ulong, Func<byte[], byte[]>> licenseActivator = null, OnErrorEvent onError = null)
        {
            OnError = onError;
            MyId = myId;
            Domain = domain;
            ServerUri = new UriBuilder(serverAddress).Uri;
            ServerUri = new UriBuilder(ServerUri) { Port = 5223 }.Uri;
            OnMessageReceived = onMessageArrives;
            OnDataDeliveryConfirm = onDataDeliveryConfirm;
            ConnectionTimeout = connectionTimeout;
            lock (Instances)
                Instances.Add(this);
            HasConnectivity = hasConnectivity ?? true; // If not specified, assume connectivity is available by default
            if (HasConnectivity)
                Connect();
        }

        private int ConnectionTimeout;

        static readonly List<PushPullDataChannel> Instances = new List<PushPullDataChannel>();

        /// <summary>
        /// Use this command to re-establish the connection if it is disabled by the timer set with the initialization
        /// </summary>
        public override void ReEstablishConnection()
        {
            lock (Instances)
                foreach (var instance in Instances)
                {
                    instance.Reconnect();
                }
        }

        private void Reconnect()
        {
            _delayCts.Cancel();
        }

        #region IO methods

        private string Segment(DataFlags? dataFlags = null)
        {
            // Segment specification
            // 8 bytes [userId]
            // 4 bytes [domainId]
            // 1 bytes [dataFlags] - POST only

            var segmentBytes = new byte[dataFlags == null ? 12 : 13];
            Buffer.BlockCopy(MyId.GetBytes(), 0, segmentBytes, 0, 8);
            Buffer.BlockCopy(Domain.GetBytes(), 0, segmentBytes, 8, 4);
            if (dataFlags != null)
            {
                segmentBytes[12] = (byte)dataFlags.Value;
            }
            return segmentBytes.ToHex();
        }

        private async Task<byte[]> DownloadAsync(Uri url)
        {
            bool isTimeoud = false;
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(ConnectionTimeout)
            };
            try
            {
                var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                OnConnected(response);
                Timer timer = null;
                if (ConnectionTimeout != Timeout.Infinite)
                {
                    timer = new Timer(state =>
                    {
                        try
                        {
                            isTimeoud = true;
                            httpClient.Dispose();
                            response.Dispose();
                        }
                        finally
                        {
                            timer.Dispose();
                        }
                    }, null, ConnectionTimeout, Timeout.Infinite);
                }
                var result = await response.Content.ReadAsByteArrayAsync();
                timer?.Dispose();
                return result;
            }
            catch (Exception ex)
            {
                if (isTimeoud)
                {
                    throw new TimeoutException("The connection has timed out.", ex);
                }
                throw;
            }
        }

        private void OnConnected(HttpResponseMessage response)
        {
            IsConnected = true;
            WaitConnection?.Set();
        }

        #endregion
        private Task _downloadLoopTask; // Strong reference to prevent garbage collection

        private CancellationTokenSource _delayCts = new CancellationTokenSource();

        /// <summary>
        /// Connect data input
        /// </summary>
        private void Connect()
        {
            lock (this)
            {
                if (_downloadLoopTask == null) // exit if is already connected
                {
                    WaitConnection?.Set();
                    WaitConnection = new ManualResetEvent(false);
                    _downloadLoopTask = Task.Run(async () =>
                    {
                        var targetUrl = new Uri(ServerUri, Segment());
                        while (!Disposed)
                        {
                            try
                            {
                                byte[] data = await DownloadAsync(targetUrl);
                                OnDataReceives(data);
                            }
                            catch (Exception ex)
                            {

                                if (ex.HResult == -2147467259)
                                    Debugger.Break(); // The router is off-line or not reachable
                                WaitConnection?.Set();
                                IsConnected = false;
                                if (ex.HResult == -2146233083) // The connection was terminated by ConnectionTimeout setting
                                {
                                    _downloadLoopTask = null;
                                    break; // The connection has been closed, no need to retry
                                }
                                else
                                    OnError?.Invoke(ErrorType.ConnectionFailure, ex.Message);
                                try
                                {
                                    var old = _delayCts;
                                    _delayCts = new CancellationTokenSource();
                                    await Task.Delay(60000, _delayCts.Token);
                                    old?.Dispose();
                                }
                                catch (TaskCanceledException)
                                {
                                }
                            }
                        }
                    });
                    WaitConnection?.WaitOne(); // Wait for the connection to be established or failure
                }
            }
        }

        private ManualResetEvent WaitConnection;

        /// <summary>
        /// POST data to server
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataFlags"></param>
        /// <returns></returns>
        internal override void SendDataToRouter(byte[] data, DataFlags dataFlags)
        {
            Connect();
            var targetUrl = new Uri(ServerUri, Segment(dataFlags));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(targetUrl);
            request.Method = "POST";
            request.ContentType = "application/octet-stream";
            request.ContentLength = data.Length;
            bool succssful = true;
            int attempts = 0;
            const int maxAttempts = 5;
            while (attempts < maxAttempts)
            {
                try
                {
                    attempts++;
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(data, 0, data.Length);
                    }
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            succssful = false;
                            Debugger.Break();
                        }
                        using (Stream responseStream = response.GetResponseStream())
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            responseStream.CopyTo(memoryStream);
                            var res = memoryStream.ToArray();

                        }
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (attempts < maxAttempts)
                    {
                        Thread.Sleep(200 * attempts);
                    }
                    else
                    {
                        OnError?.Invoke(ErrorType.SendDataError, ex.Message);
                        succssful = false;
                        Debugger.Break();
                    }

                }
            }
            if (succssful)
            {
                LastOUT = DateTime.Now;
                LastCommandOUT = (Protocol.Command)data[0];
                var dataId = BitConverter.ToUInt32(data, 1);
                OnDataDeliveryConfirm?.Invoke(dataId);
            }
        }

        private bool Disposed;

        public override void Dispose()
        {
            if (!Disposed)
            {
                lock (Instances)
                    Instances.Remove(this);
            }
            Disposed = true;

        }
    }
}
