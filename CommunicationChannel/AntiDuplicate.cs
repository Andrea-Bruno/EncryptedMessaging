using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;

namespace CommunicationChannel
{
	/// <summary>
	/// In TCP connections, theoretically the connection could drop while the packet transmission is in progress, or the bidirectional connection could be interrupted by the system in one direction only: In iOS it happens that when the app is in the background the outgoing communication is interrupted while the incoming one remains active, and the device cannot confirm that it has received the packets, so that the server, having no acknowledgment of receipt, resends the packets that have actually already been received.
	/// </summary>
	public class AntiDuplicate
	{
        /// <summary>
        /// Constructor of class
        /// </summary>
        /// <param name="id">Allows the use of multiple instances</param>
        public AntiDuplicate(ulong id)
		{
			HashFile = id.ToString("x16") + "_hashs.bin";
            Load();
		}
		private readonly List<byte[]> HashList = new List<byte[]>();
		private readonly string HashFile;
		private void Load()
		{
			if (Channel.IsoStorage.FileExists(HashFile))
				using (var stream = new IsolatedStorageFileStream(HashFile, FileMode.Open, FileAccess.Read, Channel.IsoStorage))
					for (var i = 0; i < (int)stream.Length; i += 4)
					{
						var data = new byte[stream.Length];
						stream.Read(data, i, 4);
						HashList.Add(data);
					}
		}
		/// <summary>
		///	Check if the data has already been received
		/// </summary>
		/// <param name="data">Data packet</param>
		/// <returns>True or False</returns>
		internal bool AlreadyReceived(byte[] data)
		{
			var alreadyReceived = false;
			var hash = Utility.FastHash(data);
			lock (HashList)
			{
				foreach (var item in HashList)
				{
					if (hash.SequenceEqual(item))
					{
						alreadyReceived = true;
						break;
					}
				}
				if (!alreadyReceived)
				{
					if (HashList.Count >= 20)
						HashList.RemoveAt(0);
					HashList.Add(hash);
				}
			}
			if (!alreadyReceived)
				Save();
			return alreadyReceived;
		}
		private void Save()
		{
			lock (HashList)
				using (var stream = new IsolatedStorageFileStream(HashFile, FileMode.Create, FileAccess.Write, Channel.IsoStorage))
				{
                    foreach (var item in HashList)
                        stream.Write(item, 0, 4);
					stream.Flush();
					stream.Close();
				}


        }
	}
}
