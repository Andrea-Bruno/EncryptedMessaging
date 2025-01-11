using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;

namespace CommunicationChannel.DataConnection
{
    /// <summary>
    /// Implementation of IDataConnection for TCP client connections.
    /// </summary>
    public class TcpClientConnection : IDataConnection
    {
        private TcpClient _client;

        /// <summary>
        /// Establishes a connection to the specified address and port.
        /// </summary>
        /// <param name="address">The address to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        /// <param name="timeOutMs">The timeout for the connection in milliseconds. Default is 0.</param>
        /// <returns>True if the connection is successful, otherwise false.</returns>
        public bool Connect(string address, int? port = null, int timeOutMs = default)
        {
            int portInt = port ?? 0;
            _client = new TcpClient
            {
                LingerState = new LingerOption(true, 0), // Close the connection immediately after the Close() method
            };
            return _client.ConnectAsync(address, portInt).Wait(timeOutMs); // ms timeout
        }

        /// <summary>
        /// Disconnects the current TCP connection.
        /// </summary>
        public void Disconnect()
        {
            _client?.Close();
            _client?.Dispose();
        }

        /// <summary>
        /// Gets a value indicating whether the TCP connection is currently established.
        /// </summary>
        public bool IsConnected => _client?.Connected ?? false;

        /// <summary>
        /// Disposes the TCP connection, releasing all resources.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// Gets the stream associated with the current TCP connection.
        /// </summary>
        /// <returns>The stream for the current TCP connection.</returns>
        public Stream GetStream()
        {
           return _client.GetStream();
        }
    }
}
