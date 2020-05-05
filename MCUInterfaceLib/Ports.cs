using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management.Instrumentation;
using System.Reflection;
using System.Text;
using System.Threading;
using StatisitcsLib;
using StatisitcsLib.CryptoAnalysis;
using System.Xml.Serialization;
using System.Xml;
using System.Runtime.Serialization;

namespace MCUI
{
    /// <summary>
    /// Copied somewhere from Codeproject, takes into account some peculiarities of .NET SerialPort.
    /// Modified a bit (additional features exposed).
    /// </summary>
    [DataContract]
    public sealed class ComPort
    {
        /// <summary>
        /// A workaround existing implementation for serialization.
        /// </summary>
        SerialPort _portForProps
        {
            get
            {
                if (_serialPort == null) _serialPort = new SerialPort();
                return _serialPort;
            }
        }
        /// <summary>
        /// Only this property is meant to be used outside serialized properties
        /// </summary>
        SerialPort _serialPort = new SerialPort();
        Thread _readThread;
        volatile bool _keepReading;

        [DataMember]
        public string Name;
        [DataMember]
        public int Rate;
        [DataMember]
        public Parity Parity;
        [DataMember]
        public int Databits;
        [DataMember]
        public StopBits Stopbits;
        [DataMember]
        public Handshake Handshake;

        //For possibly less CPU-intense continuous port polling
        [DataMember]
        public int ReadAttemptDelay { get; set; } = 1; //mS

        public ComPort(int baud_rate, int databits, Parity parity, StopBits stopbits, Handshake handshake)
        {
            Rate = baud_rate;
            Databits = databits;
            Parity = parity;
            Stopbits = stopbits;
            Handshake = handshake;
            SetupNonSerializedObjects(new StreamingContext());
        }
        [OnDeserialized]
        private void SetupNonSerializedObjects(StreamingContext context)
        {
            _readThread = null;
            _keepReading = false;
        }

        //begin Observer pattern
        public delegate void ByteEventHandler(byte[] param);
        public delegate void StringEventHandler(string param);
        public StringEventHandler StatusChanged;
        public ByteEventHandler DataReceived;
        //end Observer pattern

        private void StartReading()
        {
            if (!_keepReading)
            {
                _keepReading = true;
                _readThread = new Thread(ReadPort);
                _readThread.Start();
            }
        }

        public void DiscardBuffers()
        {
            try
            {
                _serialPort.DiscardOutBuffer();
                _serialPort.DiscardInBuffer();
            }
            catch (InvalidOperationException) { }
        }

        private void StopReading()
        {
            if (_keepReading)
            {
                _keepReading = false;
                _readThread.Join(); //block until exits
                _readThread = null;
            }
        }

        /// <summary> Get the data and pass it on. </summary>
        private void ReadPort()
        {
            while (_keepReading)
            {
                if (_serialPort.IsOpen)
                {
                    byte[] readBuffer = new byte[_serialPort.ReadBufferSize + 1];
                    try
                    {
                        // If there are bytes available on the serial port,
                        // Read returns up to "count" bytes, but will not block (wait)
                        // for the remaining bytes. If there are no bytes available
                        // on the serial port, Read will block until at least one byte
                        // is available on the port, up until the ReadTimeout milliseconds
                        // have elapsed, at which time a TimeoutException will be thrown.
                        int count = _serialPort.Read(readBuffer, 0, _serialPort.ReadBufferSize);
                        byte[] SerialIn = readBuffer.Take(count).ToArray();
                        DataReceived(SerialIn);
                    }
                    catch (TimeoutException) { Thread.Sleep(ReadAttemptDelay); }
                    catch (Exception) { }
                }
                else
                {
                    TimeSpan waitTime = new TimeSpan(0, 0, 0, 0, 50);
                    Thread.Sleep(waitTime);
                }
            }
        }

        /// <summary> Open the serial port with current settings. </summary>
        public void Open(string PortName)
        {
            Close();

            try
            {
                _serialPort.PortName = PortName;
                _serialPort.BaudRate = Rate;
                _serialPort.Parity = Parity;
                _serialPort.DataBits = Databits;
                _serialPort.StopBits = Stopbits;
                _serialPort.Handshake = Handshake;

                // Set the read/write timeouts
                _serialPort.ReadTimeout = 50;
                _serialPort.WriteTimeout = 50;

                _serialPort.Open();
                DiscardBuffers();
                StartReading();
            }
            catch (IOException)
            {
                StatusChanged(String.Format("{0} does not exist", PortName));
            }
            catch (UnauthorizedAccessException)
            {
                StatusChanged(String.Format("{0} already in use", PortName));
            }
            catch (Exception ex)
            {
                StatusChanged(String.Format("{0}", ex.ToString()));
            }

            // Update the status
            if (_serialPort.IsOpen)
            {
                string p = _serialPort.Parity.ToString().Substring(0, 1);   //First char
                string h = _serialPort.Handshake.ToString();
                if (_serialPort.Handshake == Handshake.None)
                    h = "no handshake"; // more descriptive than "None"

                StatusChanged(String.Format("{0}: {1} bps, {2}{3}{4}, {5}",
                    _serialPort.PortName, _serialPort.BaudRate,
                    _serialPort.DataBits, p, (int)_serialPort.StopBits, h));
            }
            else
            {
                StatusChanged(String.Format("{0} already in use", PortName));
            }
        }

        /// <summary> Close the serial port. </summary>
        public void Close()
        {
            StopReading();
            if (_serialPort.IsOpen) DiscardBuffers();
            _serialPort.Close();
            StatusChanged("Connection closed.");
        }

        /// <summary> Get the status of the serial port. </summary>
        public bool IsOpen
        {
            get
            {
                return _serialPort.IsOpen;
            }
        }

        /// <summary> Get a list of the available ports. Already opened ports
        /// are not returned. </summary>
        public string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>Send data to the serial port. </summary>
        /// <param name="data">An string containing the data to send. </param>
        public void Send(byte[] data)
        {
            if (IsOpen)
            {
                try
                {
                    _serialPort.Write(data, 0, data.Length);
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    StatusChanged("Sending failed.");
                }
            }
        }
        [DataMember]
        public int ReadBufferSize
        {
            get
            {
                return _portForProps.ReadBufferSize;
            }
            set
            {
                _portForProps.ReadBufferSize = value;
            }
        }
        [DataMember]
        public int WriteBufferSize
        {
            get
            {
                return _portForProps.WriteBufferSize;
            }
            set
            {
                _portForProps.WriteBufferSize = value;
            }
        }
        [DataMember]
        public bool Dtr
        {
            get
            {
                return _portForProps.DtrEnable;
            }
            set
            {
                _portForProps.DtrEnable = value;
            }
        }
    }
}
