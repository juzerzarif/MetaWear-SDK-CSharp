﻿using MbientLab.MetaWear.Core;
using static MbientLab.MetaWear.Impl.Module;

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace MbientLab.MetaWear.Impl {
    [DataContract]
    class LoggedDataConsumer : DeviceDataConsumer {
        [DataMember] private readonly Dictionary<byte, Queue<byte[]>> logEntries= new Dictionary<byte, Queue<byte[]>>();
        [DataMember] private readonly List<byte> orderedIds = new List<byte>();

        internal LoggedDataConsumer(DataTypeBase source) : base(source) {
        }

        internal void addId(byte id) {
            orderedIds.Add(id);
            logEntries.Add(id, new Queue<byte[]>());
        }

        public void remove(IModuleBoardBridge bridge, bool sync) {
            Logging logging = bridge.GetModule<ILogging>() as Logging;
            orderedIds.ForEach(id => logging.Remove(id, sync));
        }

        internal void register(Dictionary<byte, LoggedDataConsumer> loggers) {
            orderedIds.ForEach(id => loggers.Add(id, this));
        }

        internal void handleLogMessage(IModuleBoardBridge bridge, byte logId, DateTime timestamp, byte[] data, Action<LogDownloadError, byte, DateTime, byte[]> errorHandler) {
            if (handler == null) {
                errorHandler?.Invoke(LogDownloadError.UNHANDLED_LOG_DATA, logId, timestamp, data);
                return;
            }

            if (logEntries.TryGetValue(logId, out var logEntry)) {
                logEntry.Enqueue(data);
            } else {
                errorHandler?.Invoke(LogDownloadError.UNKNOWN_LOG_ENTRY, logId, timestamp, data);
            }

            if (logEntries.Values.Aggregate(true, (acc, e) => acc && e.Count != 0)) {
                byte[] merged = new byte[source.attributes.length()];
                int offset = 0;

                orderedIds.Aggregate(new List<byte[]>(), (acc, id) => {
                    logEntries.TryGetValue(id, out var cached);
                    acc.Add(cached.Dequeue());
                    return acc;
                }).ForEach(entry => {
                    Array.Copy(entry, 0, merged, offset, Math.Min(entry.Length, source.attributes.length() - offset));
                    offset += entry.Length;
                });

                call(source.createData(true, bridge, merged, timestamp));
            }
        }

        public override void enableStream(IModuleBoardBridge bridge) { }

        public override void disableStream(IModuleBoardBridge bridge) { }

        public override void addDataHandler(IModuleBoardBridge bridge) { }
    }

    [DataContract]
    class TimeReference {
        public byte resetUid;
        public long tick;
        public DateTime timestamp;

        public TimeReference(byte resetUid, long tick, DateTime timestamp) {
            this.timestamp = timestamp;
            this.tick = tick;
            this.resetUid = resetUid;
        }
    }

    [DataContract]
    class Logging : ModuleImplBase, ILogging {
        private const double TICK_TIME_STEP = (48.0 / 32768.0) * 1000.0;
        private const byte LOG_ENTRY_SIZE = 4, REVISION_EXTENDED_LOGGING = 2;
        internal const byte ENABLE = 1,
            TRIGGER = 2,
            REMOVE = 3,
            TIME = 4,
            LENGTH = 5,
            READOUT = 6, READOUT_NOTIFY = 7, READOUT_PROGRESS = 8,
            REMOVE_ENTRIES = 9, REMOVE_ALL = 0xa,
            CIRCULAR_BUFFER = 0xb,
            READOUT_PAGE_COMPLETED = 0xd, READOUT_PAGE_CONFIRM = 0xe;

        [DataMember] private TimeReference latestReference;
        [DataMember] private Dictionary<byte, TimeReference> logReferenceTicks= new Dictionary<byte, TimeReference>();
        [DataMember] private Dictionary<byte, LoggedDataConsumer> dataLoggers= new Dictionary<byte, LoggedDataConsumer>();
        [DataMember] private Dictionary<byte, uint> lastTimestamp= new Dictionary<byte, uint>();
        [DataMember] private Dictionary<byte, uint> rollbackTimestamps;

        private TimedTask<byte> createLoggerTask;
        private TimedTask<byte[]> queryLogConfigTask;
        private TimedTask<bool> queryTimeTask;

        public Logging(IModuleBoardBridge bridge) : base(bridge) {
        }

        public override void tearDown() {
            bridge.sendCommand(new byte[] { (byte)LOGGING, REMOVE_ALL });
            dataLoggers.Clear();
        }

        public override void disconnected() {
            foreach (var e in lastTimestamp) {
                rollbackTimestamps[e.Key] = e.Value;
            }
            if (downloadTask != null) {
                downloadTask.SetCanceled();
                downloadTask = null;
            }
        }

        private void processLogData(byte[] logEntry, int offset) {
            byte logId = (byte)(logEntry[0 + offset] & 0x1f), resetUid = (byte)((logEntry[0 + offset] & ~0x1f) >> 5);

            uint tick = BitConverter.ToUInt32(logEntry, 1 + offset);
            if (!rollbackTimestamps.TryGetValue(resetUid, out uint rollback) || rollback < tick) {
                var timestamp = computeTimestamp(resetUid, tick);
                
                byte[] logData = new byte[LOG_ENTRY_SIZE];
                Array.Copy(logEntry, 5 + offset, logData, 0, LOG_ENTRY_SIZE);

                if (dataLoggers.TryGetValue(logId, out var logger)) {
                    logger.handleLogMessage(bridge, logId, timestamp, logData, errorHandler);
                } else {
                    errorHandler?.Invoke(LogDownloadError.UNKNOWN_LOG_ENTRY, logId, timestamp, logData);
                }
            }
        }
        
        protected override void init() {
            queryLogConfigTask = new TimedTask<byte[]>();
            createLoggerTask = new TimedTask<byte>();

            if (rollbackTimestamps == null) {
                rollbackTimestamps = new Dictionary<byte, uint>();
            }

            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, Util.setRead(TRIGGER)), response => queryLogConfigTask.SetResult(response));
            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, TRIGGER), response => createLoggerTask.SetResult(response[2]));
            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, READOUT_NOTIFY), response => {
                processLogData(response, 2);

                if (response.Length == 20) {
                    processLogData(response, 11);
                }
            });
            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, READOUT_PROGRESS), response => {
                uint nEntriesLeft = BitConverter.ToUInt32(response, 2);

                if (nEntriesLeft == 0) {
                    rollbackTimestamps.Clear();
                    downloadTask.SetResult(true);
                    downloadTask = null;
                } else {
                    updateHandler?.Invoke(nEntriesLeft, nLogEntries);
                }
            });
            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, Util.setRead(TIME)), response => {
                // if in the middle of a log download, don't update the reference
                // rollbackTimestamps var is cleared after readout progress hits 0
                if (rollbackTimestamps.Count == 0) {
                    uint tick = BitConverter.ToUInt32(response, 2);
                    byte resetUid = (response.Length > 6) ? response[6] : (byte)0xff;

                    latestReference = new TimeReference(resetUid, tick, DateTime.Now);
                    if (resetUid != 0xff) {
                        logReferenceTicks[resetUid] = latestReference;
                    }
                }

                if (queryTimeTask != null) {
                    queryTimeTask.SetResult(true);
                    queryTimeTask = null;
                }
            });
            bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, Util.setRead(LENGTH)), response => {
                int payloadSize = response.Length - 2;
                nLogEntries = BitConverter.ToUInt32(response, 2);

                if (nLogEntries == 0) {
                    rollbackTimestamps.Clear();
                    downloadTask.SetResult(true);
                    downloadTask = null;
                } else {
                    updateHandler?.Invoke(nLogEntries, nLogEntries);

                    uint nEntriesNotify = nUpdates == 0 ? 0 : (uint) (nLogEntries * (1.0 / nUpdates));
                    // In little endian, [A, B, 0, 0] is equal to [A, B]
                    byte[] command = new byte[payloadSize + sizeof(uint)];

                    Array.Copy(response, 2, command, 0, payloadSize);
                    Array.Copy(BitConverter.GetBytes(nEntriesNotify), 0, command, payloadSize, sizeof(uint));
                    
                    bridge.sendCommand(LOGGING, READOUT, command);
                }
            });

            if (bridge.lookupModuleInfo(LOGGING).revision >= REVISION_EXTENDED_LOGGING) {
                bridge.addRegisterResponseHandler(Tuple.Create((byte)LOGGING, READOUT_PAGE_COMPLETED), response => bridge.sendCommand(new byte[] { (byte)LOGGING, READOUT_PAGE_CONFIRM }));
            }
        }

        public void ClearEntries() {
            bridge.sendCommand(new byte[] { (byte) LOGGING, REMOVE_ENTRIES, 0xff, 0xff, 0xff, 0xff });
        }

        private uint nLogEntries, nUpdates;
        private TaskCompletionSource<bool> downloadTask;
        private Action<uint, uint> updateHandler;
        private Action<LogDownloadError, byte, DateTime, byte[]> errorHandler;

        public Task DownloadAsync(uint nUpdates, Action<uint, uint> updateHandler, Action<LogDownloadError, byte, DateTime, byte[]> errorHandler) {
            if (downloadTask != null) {
                return downloadTask.Task;
            }

            this.nUpdates = nUpdates;
            this.updateHandler = updateHandler;
            this.errorHandler = errorHandler;

            if (bridge.lookupModuleInfo(LOGGING).revision >= REVISION_EXTENDED_LOGGING) {
                bridge.sendCommand(new byte[] { (byte) LOGGING, READOUT_PAGE_COMPLETED, 1 });
            }
            bridge.sendCommand(new byte[] { (byte)LOGGING, READOUT_NOTIFY, 1 });
            bridge.sendCommand(new byte[] { (byte)LOGGING, READOUT_PROGRESS, 1 });
            bridge.sendCommand(new byte[] { (byte)LOGGING, Util.setRead(LENGTH) });

            downloadTask = new TaskCompletionSource<bool>();
            return downloadTask.Task;
        }

        public Task DownloadAsync(uint nUpdates, Action<uint, uint> updateHandler) {
            return DownloadAsync(nUpdates, updateHandler, null);
        }

        public Task DownloadAsync(Action<LogDownloadError, byte, DateTime, byte[]> errorHandler) {
            return DownloadAsync(0, null, errorHandler);
        }

        public Task DownloadAsync() {
            return DownloadAsync(0, null, null);
        }

        public void Start(bool overwrite = false) {
            bridge.sendCommand(new byte[] { (byte)LOGGING, CIRCULAR_BUFFER, (byte)(overwrite ? 1 : 0) });
            bridge.sendCommand(new byte[] { (byte)LOGGING, ENABLE, 1 });
        }

        public void Stop() {
            bridge.sendCommand(new byte[] { (byte)LOGGING, ENABLE, 0 });
        }

        internal async Task QueryTimeAsync() {
            queryTimeTask = new TimedTask<bool>();
            await queryTimeTask.Execute("Failed to receive current time tick within {0}ms", bridge.TimeForResponse, 
                () => bridge.sendCommand(new byte[] { (byte)LOGGING, Util.setRead(TIME) }));
        }

        internal void Remove(byte id, bool sync) {
            dataLoggers.Remove(id);
            if (sync) {
                bridge.sendCommand(new byte[] { (byte)LOGGING, REMOVE, id });
            }
        }
        internal async Task<Queue<LoggedDataConsumer>> CreateLoggersAsync(Queue<DataTypeBase> producers) {
            LoggedDataConsumer nextLogger = null;
            var result = new Queue<LoggedDataConsumer>();
            try {
                while (producers.Count != 0) {
                    nextLogger = new LoggedDataConsumer(producers.Dequeue());
                    byte[] eventConfig = nextLogger.source.eventConfig;

                    var nReqLogIds = (byte)((nextLogger.source.attributes.length() - 1) / LOG_ENTRY_SIZE + 1);
                    int remainder = nextLogger.source.attributes.length();

                    for (byte i = 0; i < nReqLogIds; i++, remainder -= LOG_ENTRY_SIZE) {
                        int entrySize = Math.Min(remainder, LOG_ENTRY_SIZE), entryOffset = LOG_ENTRY_SIZE * i + nextLogger.source.attributes.offset;

                        byte[] command = new byte[6];
                        command[0] = (byte)LOGGING;
                        command[1] = TRIGGER;
                        command[2 + eventConfig.Length] = (byte)(((entrySize - 1) << 5) | entryOffset);
                        Array.Copy(eventConfig, 0, command, 2, eventConfig.Length);

                        var id = await createLoggerTask.Execute("Did not receive logger id within {0}ms", bridge.TimeForResponse, () => bridge.sendCommand(command));
                        nextLogger.addId(id);
                    }

                    nextLogger.register(dataLoggers);
                    result.Enqueue(nextLogger);
                }
                return result;
            } catch (TimeoutException e) {
                while (result.Count != 0) {
                    result.Dequeue().remove(bridge, true);
                }
                nextLogger?.remove(bridge, true);
                throw e;
            }
        }

        internal DateTime computeTimestamp(byte resetUid, uint tick) {
            if (!logReferenceTicks.TryGetValue(resetUid, out var reference)) {
                reference = latestReference;
            }

            if (lastTimestamp.TryGetValue(resetUid, out uint previous) && previous > tick) {
                var offset = (tick - previous + (previous - reference.tick)) * TICK_TIME_STEP;
                reference.timestamp = reference.timestamp.AddMilliseconds((long) (offset));
                reference.tick = tick;
                
                if (rollbackTimestamps.ContainsKey(resetUid)) {
                    rollbackTimestamps[resetUid] = tick;
                }
            }

            lastTimestamp[resetUid] = tick;
            return reference.timestamp.AddMilliseconds((long)((tick - reference.tick) * TICK_TIME_STEP));
        }

        internal async Task<ICollection<LoggedDataConsumer>> queryActiveLoggersAsync() {
            var nRemainingLoggers = new Dictionary<DataTypeBase, byte>();
            var placeholder = new Dictionary<Tuple<byte, byte, byte>, byte>();
            ICollection <DataTypeBase> producers = bridge.aggregateDataSources();
            DataTypeBase guessLogSource(Tuple<byte, byte, byte> key, byte offset, byte length) {
                List<DataTypeBase> possible = new List<DataTypeBase>();

                foreach (DataTypeBase it in producers) {
                    if (it.eventConfig[0] == key.Item1 && it.eventConfig[1] == key.Item2 && it.eventConfig[2] == key.Item3) {
                        possible.Add(it);
                        if (it.components != null) {
                            possible.AddRange(it.components);
                        }
                    }
                }

                DataTypeBase original = null;
                bool multipleEntries = false;
                foreach (DataTypeBase it in possible) {
                    if (it.attributes.length() > 4) {
                        original = it;
                        multipleEntries = true;
                    }
                }

                if (multipleEntries) {
                    if (offset == 0 && length > LOG_ENTRY_SIZE) {
                        return original;
                    }
                    if (!placeholder.ContainsKey(key)) {
                        if (length == LOG_ENTRY_SIZE) {
                            placeholder.Add(key, length);
                            return original;
                        }
                    } else {
                        placeholder[key] += length;
                        if (placeholder[key] == original.attributes.length()) {
                            placeholder.Remove(key);
                        }
                        return original;
                    }
                }

                foreach (DataTypeBase it in possible) {
                    if (it.attributes.offset == offset && it.attributes.length() == length) {
                        return it;
                    }
                }
                return null;
            }

            for (byte i = 0; i < bridge.lookupModuleInfo(LOGGING).extra[0]; i++) {
                var response = await queryLogConfigTask.Execute("Querying log configuration (id = " + i + ") timed out after {0}ms", bridge.TimeForResponse,
                    () => bridge.sendCommand(new byte[] { (byte)LOGGING, Util.setRead(TRIGGER), i }));

                if (response.Length > 2) {
                    byte offset = (byte)(response[5] & 0x1f), length = (byte)(((response[5] >> 5) & 0x3) + 1);
                    var source = guessLogSource(Tuple.Create(response[2], response[3], response[4]), offset, length);
                    var dataprocessor = bridge.GetModule<IDataProcessor>() as DataProcessor;

                    var state = Util.clearRead(response[3]) == DataProcessor.STATE;
                    if (response[2] == (byte)DATA_PROCESSOR && (response[3] == DataProcessor.NOTIFY || state)) {
                        var chain = await dataprocessor.pullChainAsync(response[4]);
                        var first = chain.First();
                        var type = first.source != null ?
                                guessLogSource(Tuple.Create(first.source[0], first.source[1], first.source[2]), first.offset, first.length) :
                                dataprocessor.activeProcessors[first.id].Item2.source;

                        while (chain.Count() != 0) {
                            var current = chain.Pop();
                            var currentConfigObj = DataProcessorConfig.from(bridge.getFirmware(), bridge.lookupModuleInfo(DATA_PROCESSOR).revision, current.config);
                            var next = type.transform(currentConfigObj);

                            next.Item1.eventConfig[2] = current.id;
                            if (next.Item2 != null) {
                                next.Item2.eventConfig[2] = current.id;
                            }
                            if (!dataprocessor.activeProcessors.ContainsKey(current.id)) {
                                dataprocessor.activeProcessors.Add(current.id, Tuple.Create(next.Item2, new NullEditor(currentConfigObj, type, bridge) as EditorImplBase));
                            }

                            type = next.Item1;
                        }

                        source = state ? dataprocessor.lookupProcessor(response[4]).Item1 : type;
                    }

                    if (!nRemainingLoggers.ContainsKey(source) && source.attributes.length() > LOG_ENTRY_SIZE) {
                        nRemainingLoggers.Add(source, (byte)Math.Ceiling((float)(source.attributes.length() / LOG_ENTRY_SIZE)));
                    }

                    LoggedDataConsumer logger = null;
                    foreach (LoggedDataConsumer it in dataLoggers.Values) {
                        if (it.source.eventConfig.SequenceEqual(source.eventConfig) && it.source.attributes.Equals(source.attributes)) {
                            logger = it;
                            break;
                        }
                    }

                    if (logger == null || (offset != 0 && !nRemainingLoggers.ContainsKey(source))) {
                        logger = new LoggedDataConsumer(source);
                    }
                    logger.addId(i);
                    dataLoggers.Add(i, logger);

                    if (nRemainingLoggers.TryGetValue(source, out var count)) {
                        byte remaining = (byte)(count - 1);
                        nRemainingLoggers[source] = remaining;
                        if (remaining < 0) {
                            nRemainingLoggers.Remove(source);
                        }
                    }
                }
            }

            List<LoggedDataConsumer> orderedLoggers = new List<LoggedDataConsumer>();
            foreach (var k in dataLoggers.Keys.OrderBy(d => d)) {
                if (!orderedLoggers.Contains(dataLoggers[k])) {
                    orderedLoggers.Add(dataLoggers[k]);
                }
            }

            return orderedLoggers;
        }
    }
}
