﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using static CommunicationChannel.Channel;

namespace CommunicationChannel
{
	/// <summary>
	/// This class is used for saving, updating the data in the queue list.
	/// </summary>
	internal class Spooler
	{
		internal Spooler(Channel channell)
		{
			Channel = channell;
			_queueListName = "ql" + Channel.MyId;
			_queueName = "q" + Channel.MyId + "-";
			LoadUnsendedData();
		}
		private readonly Channel Channel;
		private readonly string _queueListName;
		private readonly string _queueName;
		private const bool _persistentQuee = true;


		private void LoadUnsendedData()
		{
			var datas = new List<byte[]>();
			lock (_inQuee)
			{
				if (_persistentQuee && IsoStoreage.FileExists(_queueListName))
				{
					using (var stream = new IsolatedStorageFileStream(_queueListName, FileMode.Open, FileAccess.Read, IsoStoreage))
					{
						while (stream.Position < stream.Length)
						{
							var dataInt = new byte[4];
							stream.Read(dataInt, 0, 4);
							var progressive = BitConverter.ToInt32(dataInt, 0);
							if (IsoStoreage.FileExists(_queueName + progressive))
							{
								using (var stream2 = new IsolatedStorageFileStream(_queueName + progressive, FileMode.Open, FileAccess.Read, IsoStoreage))
								{
									var data = new byte[stream2.Length];
									stream2.Read(data, 0, (int)stream2.Length);
									datas.Add(data);
								}
                                IsoStoreage.DeleteFile(_queueName + progressive);
							}
						}
					}
                    IsoStoreage.DeleteFile(_queueListName);
				}
			}
			foreach (var data in datas)
				AddToQuee(data);
		}

		private int _progressive;
		private readonly List<Tuple<uint, int>> _inQuee = new List<Tuple<uint, int>>();  // Tuple<int, int> = Tuple<idData, progressive>
		/// <summary>
		/// Add the data to the spooler Queue.
		/// </summary>
		/// <param name="data">byte array</param>
		public void AddToQuee(byte[] data)
		{
			//_channell.Tcp.Connect();
			Queue.Add(data);
			if (_persistentQuee)
			{
				lock (_inQuee)
				{
					_inQuee.Add(Tuple.Create(Utility.DataId(data), _progressive));
					using (var stream = new IsolatedStorageFileStream(_queueName + _progressive, FileMode.Create, FileAccess.Write, IsoStoreage))
						stream.Write(data, 0, data.Length);
					_progressive += 1;
					SaveQueelist();
				}
			}
			if (Queue.Count == 1) //if the Queue is empty, the spooler is stopped, then re-enable the spooler
				SendNext(false);
		}

		/// <summary>
		/// Remove the data that from the spooler Queue.
		/// </summary>
		/// <param name="dataId"> Data id</param>
		public void RemovePersistent(uint dataId)
		{
			if (_persistentQuee)
			{
				lock (_inQuee)
				{
					var toRemove = _inQuee.Find(x => x.Item1 == dataId);
					if (toRemove != null)
					{
						var progressive = toRemove.Item2;
						_inQuee.Remove(toRemove);
						if (IsoStoreage.FileExists(_queueName + progressive))
                            IsoStoreage.DeleteFile(_queueName + progressive);
						SaveQueelist();
					}
				}
			}
		}

		private void SaveQueelist()
		{
			using (var stream = new IsolatedStorageFileStream(_queueListName, FileMode.Create, FileAccess.Write, IsoStoreage))
				foreach (var item in _inQuee)
					stream.Write(item.Item2.GetBytes(), 0, 4);
		}
		/// <summary>
		/// On send completed it remove the sent packet and insert in the spooler queue before closing the communication channnel.
		/// </summary>
		/// <param name="data">data</param>
		/// <param name="ex">exception</param>
		/// <param name="connectionIsLost">connection status</param>
		public void OnSendCompleted(byte[] data, Exception ex, bool connectionIsLost)
		{
			if (ex != null)
				Channel.Tcp.InvokeError(connectionIsLost ? ErrorType.LostConnection : ErrorType.SendDataError, ex.Message);
			if (connectionIsLost)
			{
#if DEBUG
				_sent.Remove(data);
#endif
				Queue.Insert(0, data);
				Channel.Tcp.Disconnect();
			}
		}
		//internal List<Tuple<uint, Action>> ExecuteOnConfirmReceipt = new List<Tuple<uint, Action>>();
		/// <summary>
		/// Confirm the receipt status on the sent data before sending the next message
		/// </summary>
		/// <param name="dataId"> data ID</param>
		public void OnConfirmReceipt(uint dataId)
		{
			var semaphore = Channel.Tcp.WaitConfirmationSemaphore;
			// Channel.Tcp.WaitConfirmationSemaphore = null;
			RemovePersistent(dataId);
			Channel.OnDataDeliveryConfirm?.Invoke(dataId);
			semaphore?.Release();
		}
#if DEBUG
		private readonly List<byte[]> _sent = new List<byte[]>();
#endif
		internal void SendNext(bool pause = true)
		{
			if (Channel.Tcp.Logged)
			{
				if (Channel.Tcp.IsConnected() && Queue.Count > 0)
				{
					var data = Queue[0];
					Queue.RemoveAt(0);

#if DEBUG
					if (_sent.Contains(data))
						Debugger.Break(); // send duplicate message!!
					_sent.Add(data);
#endif
					//if (pause)
					//	Thread.Sleep(1000);
					Channel.Tcp.ExecuteSendData(data);
				}
			}
		}
		internal int QueeCount => Queue.Count;
		internal readonly List<byte[]> Queue = new List<byte[]>();
	}

}