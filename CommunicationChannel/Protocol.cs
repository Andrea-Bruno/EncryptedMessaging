namespace CommunicationChannel
{
    /// <summary>
    /// Class that defines the communication protocol commands
    /// </summary>
    public static class Protocol
	{
		/// <summary>
		/// Defines the protocols for the communication channel
		/// </summary>
		public enum Command : byte
		{
			/// <summary>
			/// Represents that connection is established
			/// </summary>
			ConnectionEstablished = 0,
			/// <summary>
			/// Represents that data is received by server
			/// </summary>
			DataReceivedConfirmation = 1,
			/// <summary>
			/// Represents that server is pinged
			/// </summary>
			Ping = 2,
            /// <summary>
            /// It is a packet of data to be addressed to a client
            /// </summary>
            Data = 3,
            /// <summary>
            /// Represents that data is sent to the router/server
            /// </summary>
            RouterData = 4,
        }
    }
}
