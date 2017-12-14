﻿using MbientLab.MetaWear.Core;
using static MbientLab.MetaWear.Impl.Module;

using System;
using MbientLab.MetaWear.Core.SensorFusionBosch;
using MbientLab.MetaWear.Sensor;
using MbientLab.MetaWear.Data;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MbientLab.MetaWear.Impl {
    [KnownType(typeof(QuaternionDataType))]
    [KnownType(typeof(EulerAnglesDataType))]
    [KnownType(typeof(FusedAccelerationDataType))]
    [KnownType(typeof(CorrectedAccelerationDataType))]
    [KnownType(typeof(CorrectedAngularVelocityDataType))]
    [KnownType(typeof(CorrectedBFieldDataType))]
    [DataContract]
    class SensorFusionBosch : ModuleImplBase, ISensorFusionBosch {
        internal static string createIdentifier(DataTypeBase dataType) {
            switch (dataType.eventConfig[1]) {
                case CORRECTED_ACC:
                    return "corrected-acceleration";
                case CORRECTED_ROT:
                    return "corrected-angular-velocity";
                case CORRECTED_MAG:
                    return "corrected-magnetic-field";
                case QUATERNION:
                    return "quaternion";
                case EULER_ANGLES:
                    return "euler-angles";
                case GRAVITY_VECTOR:
                    return "gravity";
                case LINEAR_ACC:
                    return "linear-acceleration";
                default:
                    return null;
            }
        }

        private const byte ENABLE = 1, MODE = 2, OUTPUT_ENABLE = 3,
            CORRECTED_ACC = 4, CORRECTED_ROT = 5, CORRECTED_MAG = 6,
            QUATERNION = 7, EULER_ANGLES = 8, GRAVITY_VECTOR = 9, LINEAR_ACC = 0xa;

        [DataContract]
        private class QuaternionDataType : DataTypeBase {
            internal QuaternionDataType() : base(SENSOR_FUSION, QUATERNION, new DataAttributes(new byte[] { 4, 4, 4, 4 }, 1, 0, true)) { }

            internal QuaternionDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) : 
                base (input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new QuaternionDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new QuaternionData(bridge, this, timestamp, data);
            }
        }

        [DataContract]
        private class EulerAnglesDataType : DataTypeBase {
            internal EulerAnglesDataType() : base(SENSOR_FUSION, EULER_ANGLES, new DataAttributes(new byte[] { 4, 4, 4, 4 }, 1, 0, true)) { }

            internal EulerAnglesDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) :
                base(input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new EulerAnglesDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new EulerAnglesData(bridge, this, timestamp, data);
            }
        }

        [DataContract]
        private class FusedAccelerationDataType : DataTypeBase {
            private class AccelerationFloatData : DataBase {
                public AccelerationFloatData(IModuleBoardBridge bridge, DataTypeBase datatype, DateTime timestamp, byte[] bytes) :
                    base(bridge, datatype, timestamp, bytes) { }

                public override Type[] Types => new Type[] { typeof(Acceleration) };

                public override T Value<T>() {
                    var type = typeof(T);

                    if (type == typeof(Acceleration)) {
                        return (T)Convert.ChangeType(new Acceleration(
                            BitConverter.ToSingle(bytes, 0) / Scale,
                            BitConverter.ToSingle(bytes, 4) / Scale,
                            BitConverter.ToSingle(bytes, 8) / Scale), type);
                    }
                    return base.Value<T>();
                }
            }

            internal FusedAccelerationDataType(byte register) : base(SENSOR_FUSION, register, new DataAttributes(new byte[] { 4, 4, 4 }, 1, 0, true)) { }

            internal FusedAccelerationDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) : 
                base(input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new FusedAccelerationDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new AccelerationFloatData(bridge, this, timestamp, data);
            }

            public override float scale(IModuleBoardBridge bridge) {
                return 9.80665f;
            }
        }

        [DataContract]
        private abstract class CorrectedSensorDataType : DataTypeBase {
            internal CorrectedSensorDataType(byte register) : 
                base(SENSOR_FUSION, register, new DataAttributes(new byte[] { 4, 4, 4, 1 }, 1, 0, true)) { }

            internal CorrectedSensorDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) : 
                base(input, module, register, id, attributes) { }
        }

        [DataContract]
        private class CorrectedAccelerationDataType : CorrectedSensorDataType {
            private class CorrectedAccelerationData : DataBase {
                internal CorrectedAccelerationData(IModuleBoardBridge bridge, DataTypeBase datatype, DateTime timestamp, byte[] bytes) :
                    base(bridge, datatype, timestamp, bytes) {
                }

                public override Type[] Types => new Type[] { typeof(CorrectedAcceleration) };

                public override T Value<T>() {
                    var type = typeof(T);

                    if (type == typeof(CorrectedAcceleration)) {
                        return (T)Convert.ChangeType(new CorrectedAcceleration(
                            BitConverter.ToSingle(bytes, 0) / Scale,
                            BitConverter.ToSingle(bytes, 4) / Scale,
                            BitConverter.ToSingle(bytes, 8) / Scale,
                            bytes[12]), type);
                    }
                    return base.Value<T>();
                }
            }

            internal CorrectedAccelerationDataType() : base(CORRECTED_ACC) { }

            internal CorrectedAccelerationDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) : 
                base(input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new CorrectedAccelerationDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new CorrectedAccelerationData(bridge, this, timestamp, data);
            }

            public override float scale(IModuleBoardBridge bridge) {
                return 1000f;
            }
        }

        [DataContract]
        private class CorrectedAngularVelocityDataType : CorrectedSensorDataType {
            private class CorrectedAngularVelocityData : DataBase {
                internal CorrectedAngularVelocityData(IModuleBoardBridge bridge, DataTypeBase datatype, DateTime timestamp, byte[] bytes) :
                    base(bridge, datatype, timestamp, bytes) {
                }

                public override Type[] Types => new Type[] { typeof(CorrectedAngularVelocity) };

                public override T Value<T>() {
                    var type = typeof(T);

                    if (type == typeof(CorrectedAngularVelocity)) {
                        return (T)Convert.ChangeType(new CorrectedAngularVelocity(
                            BitConverter.ToSingle(bytes, 0),
                            BitConverter.ToSingle(bytes, 4),
                            BitConverter.ToSingle(bytes, 8),
                            bytes[12]), type);
                    }
                    return base.Value<T>();
                }
            }

            internal CorrectedAngularVelocityDataType() : base(CORRECTED_ROT) { }

            internal CorrectedAngularVelocityDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) :
                base(input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new CorrectedAngularVelocityDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new CorrectedAngularVelocityData(bridge, this, timestamp, data);
            }
        }

        [DataContract]
        private class CorrectedBFieldDataType : CorrectedSensorDataType {
            private class CorrectedMagneticFieldData : DataBase {
                internal CorrectedMagneticFieldData(IModuleBoardBridge bridge, DataTypeBase datatype, DateTime timestamp, byte[] bytes) :
                    base(bridge, datatype, timestamp, bytes) {
                }

                public override Type[] Types => new Type[] { typeof(CorrectedMagneticField) };

                public override T Value<T>() {
                    var type = typeof(T);

                    if (type == typeof(CorrectedMagneticField)) {
                        return (T)Convert.ChangeType(new CorrectedMagneticField(
                            BitConverter.ToSingle(bytes, 0) / Scale,
                            BitConverter.ToSingle(bytes, 4) / Scale,
                            BitConverter.ToSingle(bytes, 8) / Scale,
                            bytes[12]), type);
                    }
                    return base.Value<T>();
                }
            }

            internal CorrectedBFieldDataType() : base(CORRECTED_MAG) { }

            internal CorrectedBFieldDataType(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) :
                base(input, module, register, id, attributes) { }

            public override DataTypeBase copy(DataTypeBase input, Module module, byte register, byte id, DataAttributes attributes) {
                return new CorrectedBFieldDataType(input, module, register, id, attributes);
            }

            public override IData createData(bool logData, IModuleBoardBridge bridge, byte[] data, DateTime timestamp) {
                return new CorrectedMagneticFieldData(bridge, this, timestamp, data);
            }

            public override float scale(IModuleBoardBridge bridge) {
                return 1000000f;
            }
        }

        private class SensorFusionAsyncDataProducer : AsyncDataProducer {
            internal readonly byte mask;

            internal SensorFusionAsyncDataProducer(DataTypeBase datatype, byte mask, IModuleBoardBridge bridge) : base(datatype, bridge) {
                this.mask = mask;
            }

            public override void Start() {
                (bridge.GetModule<ISensorFusionBosch>() as SensorFusionBosch).dataEnableMask |= mask;
            }

            public override void Stop() {
                (bridge.GetModule<ISensorFusionBosch>() as SensorFusionBosch).dataEnableMask &= (byte)(~mask & 0xff);
            }
        }

        [DataMember] private Mode mode = Mode.Ndof;
        [DataMember] private byte dataEnableMask;
        [DataMember] private DataTypeBase corrAccType, corrAngVelType, corrBFieldType, quaternionType, eulerAnglesType, gravityType, linAccType;

        private TimedTask<byte[]> readConfigTask;

        private IAsyncDataProducer correctedAcc = null, correctedAngularVel = null, correctedMag = null, 
            quaternion = null, eulerAngles = null, 
            gravity = null, linearAcc = null;

        public IAsyncDataProducer CorrectedAcceleration {
            get {
                if (correctedAcc == null) {
                    correctedAcc = new SensorFusionAsyncDataProducer(corrAccType, 0x1, bridge);
                }
                return correctedAcc;
            }
        }
        public IAsyncDataProducer CorrectedAngularVelocity {
            get {
                if (correctedAngularVel == null) {
                    correctedAngularVel = new SensorFusionAsyncDataProducer(corrAngVelType, 0x2, bridge);
                }
                return correctedAngularVel;
            }
        }
        public IAsyncDataProducer CorrectedMagneticField {
            get {
                if (correctedMag == null) {
                    correctedMag = new SensorFusionAsyncDataProducer(corrBFieldType, 0x4, bridge);
                }
                return correctedMag;
            }
        }
        public IAsyncDataProducer Quaternion {
            get {
                if (quaternion == null) {
                    quaternion = new SensorFusionAsyncDataProducer(quaternionType, 0x8, bridge);
                }
                return quaternion;
            }
        }
        public IAsyncDataProducer EulerAngles {
            get {
                if (eulerAngles == null) {
                    eulerAngles = new SensorFusionAsyncDataProducer(eulerAnglesType, 0x10, bridge);
                }
                return eulerAngles;
            }
        }
        public IAsyncDataProducer Gravity {
            get {
                if (gravity == null) {
                    gravity = new SensorFusionAsyncDataProducer(gravityType, 0x20, bridge);
                }
                return gravity;
            }
        }
        public IAsyncDataProducer LinearAcceleration {
            get {
                if (linearAcc == null) {
                    linearAcc = new SensorFusionAsyncDataProducer(linAccType, 0x40, bridge);
                }
                return linearAcc;
            }
        }

        public SensorFusionBosch(IModuleBoardBridge bridge) : base(bridge) {
            corrAccType = new CorrectedAccelerationDataType();
            corrAngVelType = new CorrectedAngularVelocityDataType();
            corrBFieldType = new CorrectedBFieldDataType();
            quaternionType = new QuaternionDataType();
            eulerAnglesType = new EulerAnglesDataType();
            gravityType = new FusedAccelerationDataType(GRAVITY_VECTOR);
            linAccType = new FusedAccelerationDataType(LINEAR_ACC);
        }

        internal override void aggregateDataType(ICollection<DataTypeBase> collection) {
            collection.Add(corrAccType);
            collection.Add(corrAngVelType);
            collection.Add(corrBFieldType);
            collection.Add(quaternionType);
            collection.Add(eulerAnglesType);
            collection.Add(gravityType);
            collection.Add(linAccType);
        }

        protected override void init() {
            readConfigTask = new TimedTask<byte[]>();
            bridge.addRegisterResponseHandler(Tuple.Create((byte)SENSOR_FUSION, Util.setRead(MODE)), response => readConfigTask.SetResult(response));
        }

        public void Configure(Mode mode = Mode.Ndof, AccRange ar = AccRange._16g, GyroRange gr = GyroRange._2000dps,
                object[] accExtra = null, object[] gyroExtra = null) {
            bridge.sendCommand(new byte[] {(byte) SENSOR_FUSION, MODE, (byte) ((byte) mode + 1),(byte) ((byte) ar | (((byte) gr + 1) << 4)) });

            var accelerometer = bridge.GetModule<IAccelerometerBosch>();
            var gyro = bridge.GetModule<IGyroBmi160>();
            var magnetometer = bridge.GetModule<IMagnetometerBmm150>();

            Action<Sensor.AccelerometerBmi160.OutputDataRate> configAcc = (odr) => {
                if (accelerometer is IAccelerometerBmi160) {
                    var casted = accelerometer as IAccelerometerBmi160;

                    Func<Sensor.AccelerometerBosch.DataRange, Action<Sensor.AccelerometerBmi160.FilterMode>> configure = 
                            range => filter => casted.Configure(odr: odr, range: range, filter: filter);
                    var partial = configure((Sensor.AccelerometerBosch.DataRange) ar);
                    
                    if (accExtra != null) {
                        foreach(Object it in accExtra) {
                            if (it is Sensor.AccelerometerBmi160.FilterMode) {
                                partial((Sensor.AccelerometerBmi160.FilterMode) it);
                                break;
                            }
                        }
                    } else {
                        partial(Sensor.AccelerometerBmi160.FilterMode.Normal);
                    }
                }
            };
            Action configGyro = () => {
                Func<Sensor.GyroBmi160.DataRange, Action<Sensor.GyroBmi160.FilterMode>> configure = 
                        range => filter => gyro.Configure(odr: Sensor.GyroBmi160.OutputDataRate._100Hz, range: range, filter: filter);
                var partial = configure((Sensor.GyroBmi160.DataRange) gr);

                if (gyroExtra != null) {
                    foreach (Object it in gyroExtra) {
                        if (it is Sensor.GyroBmi160.FilterMode) {
                            partial((Sensor.GyroBmi160.FilterMode)it);
                            break;
                        }
                    }
                } else {
                    partial(Sensor.GyroBmi160.FilterMode.Normal);
                }
            };

            this.mode = mode;
            switch (mode) {
                case Mode.Ndof:
                    configAcc(Sensor.AccelerometerBmi160.OutputDataRate._100Hz);
                    configGyro();
                    magnetometer.Configure(odr: Sensor.MagnetometerBmm150.OutputDataRate._25Hz);
                    break;
                case Mode.ImuPlus:
                    configAcc(Sensor.AccelerometerBmi160.OutputDataRate._100Hz);
                    configGyro();
                    break;
                case Mode.Compass:
                    configAcc(Sensor.AccelerometerBmi160.OutputDataRate._25Hz);
                    magnetometer.Configure(odr: Sensor.MagnetometerBmm150.OutputDataRate._25Hz);
                    break;
                case Mode.M4g:
                    configAcc(Sensor.AccelerometerBmi160.OutputDataRate._50Hz);
                    magnetometer.Configure(odr: Sensor.MagnetometerBmm150.OutputDataRate._25Hz);
                    break;
            }
        }

        public void Start() {
            var accelerometer = bridge.GetModule<IAccelerometerBosch>();
            var gyro = bridge.GetModule<IGyroBmi160>();
            var magnetometer = bridge.GetModule<IMagnetometerBmm150>();

            switch (mode) {
                case Mode.Ndof:
                    accelerometer.Acceleration.Start();
                    gyro.AngularVelocity.Start();
                    magnetometer.MagneticField.Start();
                    accelerometer.Start();
                    gyro.Start();
                    magnetometer.Start();
                    break;
                case Mode.ImuPlus:
                    accelerometer.Acceleration.Start();
                    gyro.AngularVelocity.Start();
                    accelerometer.Start();
                    gyro.Start();
                    break;
                case Mode.Compass:
                    accelerometer.Acceleration.Start();
                    magnetometer.MagneticField.Start();
                    accelerometer.Start();
                    magnetometer.Start();
                    break;
                case Mode.M4g:
                    accelerometer.Acceleration.Start();
                    magnetometer.MagneticField.Start();
                    accelerometer.Start();
                    magnetometer.Start();
                    break;
            }

            bridge.sendCommand(new byte[] { (byte)SENSOR_FUSION, OUTPUT_ENABLE, dataEnableMask, 0x00 });
            bridge.sendCommand(new byte[] { (byte)SENSOR_FUSION, ENABLE, 0x1 });
        }

        public void Stop() {
            var accelerometer = bridge.GetModule<IAccelerometerBosch>();
            var gyro = bridge.GetModule<IGyroBmi160>();
            var magnetometer = bridge.GetModule<IMagnetometerBmm150>();

            bridge.sendCommand(new byte[] { (byte)SENSOR_FUSION, ENABLE, 0x0 });
            bridge.sendCommand(new byte[] { (byte)SENSOR_FUSION, OUTPUT_ENABLE, 0x00, 0x7f });

            switch (mode) {
                case Mode.Ndof:
                    accelerometer.Stop();
                    gyro.Stop();
                    magnetometer.Stop();
                    accelerometer.Acceleration.Stop();
                    gyro.AngularVelocity.Stop();
                    magnetometer.MagneticField.Stop();
                    break;
                case Mode.ImuPlus:
                    accelerometer.Stop();
                    gyro.Stop();
                    accelerometer.Acceleration.Stop();
                    gyro.AngularVelocity.Stop();
                    break;
                case Mode.Compass:
                    accelerometer.Stop();
                    magnetometer.Stop();
                    accelerometer.Acceleration.Stop();
                    magnetometer.MagneticField.Stop();
                    break;
                case Mode.M4g:
                    accelerometer.Stop();
                    magnetometer.Stop();
                    accelerometer.Acceleration.Stop();
                    magnetometer.MagneticField.Stop();
                    break;
            }
        }

        public async Task PullConfigAsync() {
            var response = await readConfigTask.Execute("Did not receive sensor fusion config within {0}ms", bridge.TimeForResponse, 
                () => bridge.sendCommand(new byte[] { (byte)SENSOR_FUSION, Util.setRead(MODE) }));
            mode = (Mode)response[2];
        }
    }
}
