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
			/// 0 represents that connection is established
			/// </summary>
			ConnectionEstablished = 0,
			/// <summary>
			/// 1 represents that data is received by server
			/// </summary>
			DataReceivedConfirmation = 1,
			/// <summary>
			/// 2 represents that server is pinged
			/// </summary>
			Ping = 2,
            /// <summary>
            /// 3 send the router a message for another client, or a data packet to the router if indicated by the appropriate flags
            /// </summary>
            SetNewPost = 3,
            /// <summary>
            /// 4 represents messages of another client arriving from the router
            /// </summary>
            Messages = 4,
		}
	}
}
