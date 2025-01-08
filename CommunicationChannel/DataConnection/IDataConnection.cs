using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace CommunicationChannel.DataConnection
{
    /// <summary>
    /// Interface for data connections, providing common methods for different types of connections.
    /// </summary>
    public interface IDataConnection : IDisposable
    {
        /// <summary>
        /// Establishes a connection to the specified address and port.
        /// </summary>
        /// <param name="address">The address to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        /// <param name="timeOutMs">The timeout for the connection in milliseconds. Default is 0.</param>
        /// <returns>True if the connection is successful, otherwise false.</returns>
        bool Connect(string address, int? port = null, int timeOutMs = default);

        /// <summary>
        /// Disconnects the current connection.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Gets a value indicating whether the connection is currently established.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the stream associated with the current connection.
        /// </summary>
        /// <returns>The stream for the current connection.</returns>
        System.IO.Stream GetStream();
    }

}
