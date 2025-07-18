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
        internal Spooler(Channel channel)
        {
            Channel = channel;
            _queueListName = "ql" + Channel.MyId;
            _queueName = "q" + Channel.MyId + "-";
            LoadNotTransmittedData();
        }
        private readonly Channel Channel;
        private readonly string _queueListName;
        private readonly string _queueName;
        private const bool _persistentQueue = true;


        private void LoadNotTransmittedData()
        {
            var datas = new List<byte[]>();
            lock (_inQueue)
            {
                if (_persistentQueue && IsoStorage.FileExists(_queueListName))
                {
                    using (var stream = new IsolatedStorageFileStream(_queueListName, FileMode.Open, FileAccess.Read, IsoStorage))
                    {
                        while (stream.Position < stream.Length)
                        {
                            var dataInt = new byte[4];
                            stream.Read(dataInt, 0, 4);
                            var progressive = BitConverter.ToInt32(dataInt, 0);
                            if (IsoStorage.FileExists(_queueName + progressive))
                            {
                                using (var stream2 = new IsolatedStorageFileStream(_queueName + progressive, FileMode.Open, FileAccess.Read, IsoStorage))
                                {
                                    var data = new byte[stream2.Length];
                                    stream2.Read(data, 0, (int)stream2.Length);
                                    datas.Add(data);
                                }
                                IsoStorage.DeleteFile(_queueName + progressive);
                            }
                        }
                    }
                    IsoStorage.DeleteFile(_queueListName);
                }
            }
#if DEBUG && !TEST
            if (datas.Count > 5)
                Debugger.Break(); // Too many data in the queue list
#endif
            foreach (var data in datas)
                AddToQueue(data);
        }

        private int _progressive;
        private readonly List<Tuple<uint, int>> _inQueue = new List<Tuple<uint, int>>();  // Tuple<int, int> = Tuple<idData, progressive>
        /// <summary>
        /// Add the data to the spooler Queue.
        /// </summary>
        /// <param name="data">byte array</param>
        public void AddToQueue(byte[] data)
        {
            //_Channel.Tcp.Connect();
            Queue.Add(data);
            if (_persistentQueue)
            {
                lock (_inQueue)
                {
                    _inQueue.Add(Tuple.Create(Utility.DataId(data), _progressive));
                    using (var stream = new IsolatedStorageFileStream(_queueName + _progressive, FileMode.Create, FileAccess.Write, IsoStorage))
                        stream.Write(data, 0, data.Length);
                    _progressive += 1;
                    SaveQueueList();
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
            if (_persistentQueue)
            {
                lock (_inQueue)
                {
                    var toRemove = _inQueue.Find(x => x.Item1 == dataId);
                    if (toRemove != null)
                    {
                        var progressive = toRemove.Item2;
                        _inQueue.Remove(toRemove);
                        if (IsoStorage.FileExists(_queueName + progressive))
                            IsoStorage.DeleteFile(_queueName + progressive);
                        SaveQueueList();
                    }
                }
            }
        }

        private void SaveQueueList()
        {
            using var stream = new IsolatedStorageFileStream(_queueListName, FileMode.Create, FileAccess.Write, IsoStorage);
            foreach (var item in _inQueue)
                stream.Write(item.Item2.GetBytes(), 0, 4);
        }
        /// <summary>
        /// On send completed it remove the sent packet and insert in the spooler queue before closing the communication channnel.
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="connectionIsLost">connection status</param>
        public void OnSendCompleted(byte[] data, bool connectionIsLost)
        {
            if (connectionIsLost)
            {
#if DEBUG && !TEST
                var crc = BitConverter.ToInt64(data, data.Length - 8);
                _sent.Remove(crc);
#endif
                Queue.Insert(0, data);
                Channel.DataIO.Disconnect();
            }
            else
            {
                SendNext();
            }
        }

#if DEBUG && !TEST
        private readonly List<long> _sent = new List<long>();
#endif
        internal void SendNext(bool pause = true)
        {
            if (Channel.DataIO.Logged)
            {
                if (Channel.DataIO.IsConnected() && Queue.Count > 0)
                {
                    var data = Queue[0];
                    Queue.RemoveAt(0);

#if DEBUG && !TEST
                    var crc = BitConverter.ToInt64(data, data.Length - 8);
                    if (_sent.Contains(crc))
                        Debugger.Break(); // send duplicate message ??
                    _sent.Add(crc);
#endif
                    //if (pause)
                    //	Thread.Sleep(1000);
                    Channel.DataIO.ExecuteSendData(data);
                }
            }
        }
        internal int QueueCount => Queue.Count;
        internal readonly List<byte[]> Queue = new List<byte[]>();
    }

}