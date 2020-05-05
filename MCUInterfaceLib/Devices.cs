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
	#region Commands
	/// <summary>
	/// Execution error that takes place on the device itself and is returned as an error message
	/// </summary>
	public class CommandExecutionException : Exception
	{
		public CommandExecutionException(IMcuCommand i, string message = "N/A")
		{
			Command = i;
			ErrorMessage = message;
		}
		public IMcuCommand Command { get; }
		public string ErrorMessage { get; }
	}
	/// <summary>
	/// Simple Schema for dealing with string-commands: split and convert
	/// </summary>
	[DataContract(Namespace = "MDC/MCUI")]
	public struct SimpleSchema
	{
		public enum SupportedTypes
		{
			Boolean,
			Integer,
			Float,
			String,
			None
		}

		public SimpleSchema(string splitter, bool reverse, SupportedTypes convert_to, int[] arguments, SupportedTypes arg_type, string err)
		{
			//ArgumentsToTake = new int[take.Length];
			//take.CopyTo(ArgumentsToTake, 0);
			SplitEachElement = splitter == null ? null : string.Copy(splitter);
			//SplitFragmentsToTake = new int[take_fragments.Length];
			//take_fragments.CopyTo(SplitFragmentsToTake, 0);
			ConvertEachElementTo = convert_to;
			if (arguments == null)
			{
				CommandArgumentsToInclude = null;
			}
			else
			{
				CommandArgumentsToInclude = new int[arguments.Length];
				arguments.CopyTo(CommandArgumentsToInclude, 0);
			}
			ConvertArgumentsTo = arg_type;
			ErrorDesignator = err;
			ElementAliases = null;
			ReverseSplit = reverse;
		}
		public SimpleSchema(string splitter, bool reverse, SupportedTypes convert_to, int[] arguments, SupportedTypes arg_type,
			string err, Dictionary<string, string> alias)
			: this(splitter, reverse, convert_to, arguments, arg_type, err)
		{
			ElementAliases = alias.ToDictionary(x => x.Key, x => x.Value);
		}


		//int[] ArgumentsToTake { get; }
		[DataMember]
		public string SplitEachElement { get; private set; }
		//int[] SplitFragmentsToTake { get; }
		[DataMember]
		public SupportedTypes ConvertEachElementTo { get; private set; }
		[DataMember]
		public int[] CommandArgumentsToInclude { get; private set; }
		[DataMember]
		public SupportedTypes ConvertArgumentsTo { get; private set; }
		[DataMember]
		public string ErrorDesignator { get; private set; }
		[DataMember]
		public Dictionary<string, string> ElementAliases { get; private set; }
		[DataMember]
		public bool ReverseSplit { get; private set; }
	}
	/// <summary>
	/// Basic Command features: id, abstract transaction creator and response parser
	/// </summary>
	public interface IMcuCommand
	{
		string Name { get; }
		Transaction CreateTransaction(IMcuDevice dev, object[] vals);
		KeyValuePair<object[], object[]> ParseResponse(object[] arguments, object[] response);
	}
	public interface IEncodedCommand : IMcuCommand
	{
		Transaction CreateTransaction(IEncodedDevice dev, string[] vals);
		KeyValuePair<object[], object[]> ParseResponse(string[] arguments, string[] response);
	}
	/// <summary>
	/// Universal MCU command class, built around the idea of transaction class that spits out string from UART (not bytes)
	/// </summary>
	[DataContract(Namespace = "MDC/MCUI")]
	public class StringCommand : IEncodedCommand
	{
		public static string DefaultPlaceholder { get; set; } = "{0}";

		[DataMember]
		public string Placeholder { get; private set; } = DefaultPlaceholder;
		[DataMember]
		public string Name { get; private set; }
		[DataMember]
		public string Command { get; private set; }
		[DataMember]
		public string[] Arguments { get; private set; }
		[DataMember]
		public int[] Variable { get; private set; }
		[DataMember]
		public SimpleSchema ResponseSchema { get; private set; }

		public StringCommand(string name, string cmd, string[] args, int[] var, SimpleSchema schema)
		{
			Name = name;
			Command = cmd;
			Arguments = args ?? new string[] { };
			Variable = var ?? new int[] { };
			ResponseSchema = schema;
		}
		/// <summary>
		/// Workaround for XmlSerializer
		/// </summary>
		private StringCommand() { }

		private string[] FormatArgumets(params string[] values)
		{
			if ((values ?? new string[] { }).Length < Variable.Length) throw new ArgumentException(
					"Размер списка аргументов должен быть больше или равен количеству переменных."
				);

			string[] res = Arguments.ToArray();
			int i = 0;
			foreach (var item in Variable)
			{
				res[item] = res[item].Replace(Placeholder, values[i++]);
			}
			return res;
		}
		public Transaction CreateTransaction(IMcuDevice dev, object[] vals)
		{
			Transaction res;
			try
			{
				res = CreateTransaction((IEncodedDevice)dev, vals == null ? null : vals.Cast<string>().ToArray());
			}
			catch (InvalidCastException)
			{
				throw new NotImplementedException("Devices that don't implement IEncodedDevice are not supported by StringCommand");
			}
			return res;
		}
		public Transaction CreateTransaction(IEncodedDevice dev, string[] vals)
		{
			return new Transaction(Command, FormatArgumets(vals), dev, false, this);
		}
		public KeyValuePair<object[], object[]> ParseResponse(object[] arguments, object[] response)
		{
			return ParseResponse(arguments.Cast<string>().ToArray(), response.Cast<string>().ToArray());
		}
		public KeyValuePair<object[], object[]> ParseResponse(string[] arguments, string[] response)
		{
			if ((response ?? new string[] { }).Length > 0)
			{
				if (response[0] == ResponseSchema.ErrorDesignator)
					throw new CommandExecutionException(this, string.Join(", ", response, 1, response.Length - 1));
			}
			object[] args = null;
			if ((ResponseSchema.CommandArgumentsToInclude ?? new int[] { }).Length != 0)
			{
				List<object> temp = new List<object>(ResponseSchema.CommandArgumentsToInclude.Length);
				foreach (var item in ResponseSchema.CommandArgumentsToInclude)
				{
					temp.Add(Converter(arguments[item], ResponseSchema.ConvertArgumentsTo));
				}
				args = temp.ToArray();
			}
			if (ResponseSchema.SplitEachElement == null)
			{
				return new KeyValuePair<object[], object[]>(args, response.Select(x => Converter(x, ResponseSchema.ConvertEachElementTo)).ToArray());
			}
			string[][] res;
			if (ResponseSchema.SplitEachElement == "")
			{
				res = response.Select(x => x.ToCharArray().Select(y => new string(new char[] { y })).ToArray()).ToArray();
			}
			else
			{
				res = response.Select(x =>
					x.Split(new string[] { ResponseSchema.SplitEachElement }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
			}
			if (ResponseSchema.ReverseSplit)
			{
				res = res.Select(x => x.Reverse().ToArray()).ToArray();
			}
			return new KeyValuePair<object[], object[]>(args, res.Select(x => x.Select(y => Converter(y, ResponseSchema.ConvertEachElementTo)).ToArray()).ToArray());
		}
		private object Converter(string str, SimpleSchema.SupportedTypes t)
		{
			object res = null;
			if (ResponseSchema.ElementAliases != null)
			{
				foreach (var item in ResponseSchema.ElementAliases)
				{
					str = str.Replace(item.Key, item.Value);
				}
			}
			var @switch = new Dictionary<SimpleSchema.SupportedTypes, Action> {
				{ SimpleSchema.SupportedTypes.String, () => { res = str; } },
				{ SimpleSchema.SupportedTypes.Integer, () => { res = int.Parse(str); } },
				{ SimpleSchema.SupportedTypes.Float, () => { res = float.Parse(str); } },
				{ SimpleSchema.SupportedTypes.Boolean, () =>
					{
						bool p;
						if (bool.TryParse(str, out p))
						{
							res = p;
						}
						else
						{
							int i;
							if (int.TryParse(str, out i))
							{
								res = (i > 0);
							}
							else
							{
								throw new ArrayTypeMismatchException("Can not convert element '" + str + "' into boolean.");
							}
						}
					}
				}
			};
			@switch[t]();
			return res;
		}
	}
	/// <summary>
	/// A wrapper around List with indexing by name added. [Serializable], 'cause this is a collection without properties.
	/// </summary>
	[Serializable]
	public class CommandSet : List<IMcuCommand>, IXmlSerializable
	{
		/// <summary>
		/// A wrapper for LINQ Where. Throws ArgumentOutOfRangeException.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public IMcuCommand this[string name]
		{
			get
			{
				IMcuCommand res = null;
				try
				{
					res = this.Where(x => x.Name == name).First();
				}
				catch (InvalidOperationException)
				{
					throw new ArgumentOutOfRangeException(string.Format("This set does not contain command called '{0}'.", name));
				}
				return res;
			}
		}
		public string Serialize()
		{
			XmlSerializer s = new XmlSerializer(GetType());
			StringBuilder res = new StringBuilder(Count * 1000);
			using (XmlWriter w = XmlWriter.Create(res, new XmlWriterSettings() { CheckCharacters = true, Indent = true }))
			{
				s.Serialize(w, this);
			}
			return res.ToString();
		}

		public static CommandSet Deserialize(string xml)
		{
			XmlSerializer s = new XmlSerializer(typeof(CommandSet));
			return (CommandSet)s.Deserialize(new StringReader(xml));
		}

		public System.Xml.Schema.XmlSchema GetSchema()
		{
			return null;
		}
		public void WriteXml(XmlWriter writer)
		{
			foreach (IMcuCommand cmd in this)
			{
				writer.WriteStartElement(typeof(IMcuCommand).Name);
				writer.WriteAttributeString("Type", cmd.GetType().FullName);
				Type t = cmd.GetType();
				DataContractSerializer xmlSerializer = new DataContractSerializer(t);
				xmlSerializer.WriteObject(writer, cmd);
				writer.WriteEndElement();
			}
		}
		public void ReadXml(XmlReader reader)
		{
			reader.ReadStartElement(); //Probably not the best solution, but how do we know the name of the property contained in some other class?
			while (reader.IsStartElement(typeof(IMcuCommand).Name))
			{
				Type type = Type.GetType(reader.GetAttribute("Type"));
				DataContractSerializer serial = new DataContractSerializer(type);
				reader.ReadStartElement(typeof(IMcuCommand).Name);
				Add((IMcuCommand)serial.ReadObject(reader));
				reader.ReadEndElement();
			}
			reader.ReadEndElement();
		}
	}
	[Serializable]
	public class CommandScript<T> : List<KeyValuePair<string, T[]>>, IXmlSerializable
	{
		public CommandScript(string name)
		{
			Name = name;
		}
		/// <summary>
		/// For serialization.
		/// </summary>
		private CommandScript() { }

		public string Name { get; private set; }
		//public T Placeholder { get; set; }

		[XmlIgnore]
		public Action ProgressCallbackInvoker { get; set; }

		public void Add(string name, T[] values)
		{
			Add(new KeyValuePair<string, T[]>(name, values));
		}

		public System.Xml.Schema.XmlSchema GetSchema()
		{
			return null;
		}

		private const string XmlElement = "CommandScriptElement";
		private const string NameElement = "Name";
		private const string PlaceholderElement = "Placeholder";
		public void WriteXml(XmlWriter writer)
		{
			DataContractSerializer xmlSerializer = new DataContractSerializer(typeof(KeyValuePair<string, T[]>));
			foreach (var item in this)
			{
				writer.WriteStartElement(XmlElement);
				xmlSerializer.WriteObject(writer, item);
				writer.WriteEndElement();
			}
			//DataContractSerializer auxSerializer = new DataContractSerializer(typeof(T));
			writer.WriteStartElement(NameElement);
			writer.WriteString(Name);
			writer.WriteEndElement();
			//writer.WriteStartElement(PlaceholderElement);
			//auxSerializer.WriteObject(writer, Placeholder);
			//writer.WriteEndElement();
		}
		public void ReadXml(XmlReader reader)
		{
			DataContractSerializer xmlSerializer = new DataContractSerializer(typeof(KeyValuePair<string, T[]>));
			reader.ReadStartElement(); //Probably not the best solution, but how do we know the name of the property contained in some other class?
			while (reader.IsStartElement(XmlElement))
			{
				reader.ReadStartElement();
				Add((KeyValuePair<string, T[]>)xmlSerializer.ReadObject(reader));
				reader.ReadEndElement();
			}
			//DataContractSerializer auxSerializer = new DataContractSerializer(typeof(T));
			if (reader.IsStartElement(NameElement))
			{
				reader.ReadStartElement();
				Name = reader.ReadString();
				reader.ReadEndElement();
			}
			/*if (reader.IsStartElement(PlaceholderElement))
			{
				reader.ReadStartElement();
				Placeholder = (T)auxSerializer.ReadObject(reader);
				reader.ReadEndElement();
			}*/
			reader.ReadEndElement();
		}
	}
	#endregion

	/// <summary>
	/// Single transaction (usable only once). Automatically manages event subscriptions.
	/// </summary>
	public class Transaction
	{
		public event EventHandler OnCompleted;
		public event EventHandler OnTimeout;
		public event EventHandler<CommandReceivedEventAgrs> OnUnexpectedResponse;
		public event EventHandler<LogEventArgs> OnLogActivity;

		protected System.Timers.Timer _tim;
		protected object _lock = new object();
		protected int _lock_timeout = 5000;
		protected byte[][] _resp = null;
		protected bool _completed = false;
		protected bool _processing = false;

		#region Properties        
		public bool Completed
		{
			get
			{
				lock (_lock)
				{
					return _completed;
				}
			}
		}
		public bool Sent { get; private set; }
		public bool TimedOut { get; private set; } = false;
		public byte[] Command { get; }
		public byte[] ArgSeparator { get; }
		public byte[] CmdSeparator { get; }
		public byte[][] Arguments { get; }
		public byte[][] Response
		{
			get
			{
				byte[][] res;
				lock (_lock)
				{
					res = _resp.Select(x => x.ToArray()).ToArray();
				}
				return res;
			}
			private set
			{
				lock (_lock)
				{
					_resp = value;
				}
			}
		}
		public Encoding CurrentEncoding { get; }
		public IMcuCommand CurrentCommand { get; }
		public IMcuDevice Device { get; private set; }
		#endregion

		public Transaction(string cmd, string[] args, IEncodedDevice dev, bool sent = false, IMcuCommand current = null)
			: this(dev.CurrentEncoding.GetBytes(cmd), args == null ? null : args.Select(x => dev.CurrentEncoding.GetBytes(x)).ToArray(), dev, sent)
		{
			CurrentCommand = current;
		}
		public Transaction(byte[] cmd, byte[][] args, IMcuDevice dev, bool sent = false)
		{
			Command = cmd.ToArray();
			dev.OnCommandReceived += Dev_OnCommandReceived;
			Sent = sent;
			try
			{
				CurrentEncoding = ((IEncodedDevice)dev).CurrentEncoding;
			}
			catch (InvalidCastException)
			{ }
			ArgSeparator = dev.ArgSeparator.ToArray();
			if (args != null) Arguments = args.Select(x => x.ToArray()).ToArray();
			CmdSeparator = dev.CmdSeparator.ToArray();
			Device = dev;
			_tim = new System.Timers.Timer(dev.ReceiveTimeout) { AutoReset = false, Enabled = sent };
			_tim.Elapsed += new System.Timers.ElapsedEventHandler((o, ea) =>
			{
				_completed = true;
				TimedOut = true;
				UnsubscribeListener();
				OnTimeout?.Invoke(this, new EventArgs());
			});
		}
		~Transaction()
		{
			UnsubscribeListener();
			if (OnCompleted != null)
				foreach (var d in OnCompleted.GetInvocationList())
					OnCompleted -= (d as EventHandler);
			if (OnTimeout != null)
				foreach (var d in OnTimeout.GetInvocationList())
					OnTimeout -= (d as EventHandler);
		}

		private void Dev_OnCommandReceived(object sender, CommandReceivedEventAgrs e)
		{
			if (TrySetResponse(e.Command, e.Arguments))
			{
				UnsubscribeListener();
			}
			else
			{
				RaiseOnLogActivity("Command not recognized: '{0}'. Invoking OnUnexpectedResponse.", CurrentEncoding.GetString(e.Command));
				Thread invoker = new Thread(() => { OnUnexpectedResponse?.Invoke(this, e); });
				invoker.Start();
			}
		}
		private void UnsubscribeListener()
		{
			Device.OnCommandReceived -= Dev_OnCommandReceived;
		}
		protected void RaiseOnLogActivity(string format, params object[] args)
		{
			OnLogActivity?.Invoke(this, new LogEventArgs(string.Format(format, args)));
		}

		public void SetSent()
		{
			if (!Sent)
			{
				Sent = true;
				_tim.Start();
			}
			else
			{
				throw new InvalidOperationException("This transaction has already been sent!");
			}
		}
		public bool TrySetResponse(byte[] cmd, byte[][] args)
		{
			if (Completed) return false;
			if (!Monitor.TryEnter(_lock, _lock_timeout)) throw new TimeoutException("Unable to get access to transaction properties in time.");
			bool b = false;
			if (CurrentEncoding == null)
			{
				b = cmd.SequenceEqual(Command);
			}
			else
			{
				b = (CurrentEncoding.GetString(Command) == CurrentEncoding.GetString(cmd));
			}
			if (b)
			{
				RaiseOnLogActivity("Command recognized, invoking OnCompleted.");
				_tim.Stop();
				UnsubscribeListener();
				Response = (args == null ? null : args.Select(x => x.ToArray()).ToArray());
				_processing = true;
			}
			Monitor.Exit(_lock);
			if (b)
			{
				OnCompleted?.Invoke(this, new EventArgs());
				_completed = true;                                   //Not completed till the last event handler return!
				_processing = false;
			}
			return b;
		}
		public string GetResponseString()
		{
			return CurrentEncoding.GetString(ArrayTools.JoinArray(Response, ArgSeparator).ToArray());
		}
		public KeyValuePair<object[], object[]> GetResponseValue()
		{
			if (!Completed && !_processing) throw new InvalidOperationException("Транзакция ещё не завершилась.");
			if (CurrentCommand == null) throw new InvalidOperationException("Для транзакции не задан объект команды.");
			if (CurrentEncoding == null)
			{
				return CurrentCommand.ParseResponse(Arguments, Response);
			}
			else
			{
				return CurrentCommand.ParseResponse(
					Arguments == null ? null : Arguments.Select(x => CurrentEncoding.GetString(x)).ToArray(),
					Response.Select(x => CurrentEncoding.GetString(x)).ToArray());
			}
		}
	}

	#region Devices
	/// <summary>
	/// For devices that can be operated only in single transaction mode 
	/// </summary>
	public interface ITransactionDevice : IEncodedDevice
	{
		void BeginTransaction(IEncodedCommand cmd, params string[] values);
		void BeginTransaction(string knownName, params string[] values);
		bool WaitForTransaction();
		bool PerformTransaction(IEncodedCommand cmd, params string[] values);
		bool PerformTransaction(string knownName, params string[] values);
		bool ExecuteScript(CommandScript<string> script, bool ignoreNonexistent = true, bool suppressExecutionExceptions = true);
		CommandSet KnownCommands { get; }
		Transaction CurrentTransaction { get; }
		CommandScript<string> InitScript { get; }
		CommandScript<string> UpdateScript { get; }

		event EventHandler OnTransactionCompleted;
		event EventHandler OnTransactionTimeout;
		event EventHandler<CommandReceivedEventAgrs> OnUnexpectedResponse;
	}
	/// <summary>
	/// For devices that include IHardwareEntities
	/// </summary>
	public interface IHardwareDevice : IMcuDevice
	{
		List<IHardwareEntity> Hardware { get; }
	}
	/// <summary>
	/// Ensures basic features required for interaction with any device (connected through serial interface) are present.
	/// Connect/disconnect, send/receive, presence.
	/// </summary>
	public interface IMcuDevice
	{
		void SendCommand(byte[] cmd);
		void Connect(string port_name);
		void Disconnect();
		void DetectPresence();

		bool Present { get; }
		string Name { get; }
		int ReceiveTimeout { get; }
		byte[] CmdSeparator { get; }
		byte[] ArgSeparator { get; }

		event EventHandler<CommandReceivedEventAgrs> OnCommandReceived;
		event EventHandler OnPresenceDetected;
		event EventHandler OnReset;
	}
	public interface IEncodedDevice : IMcuDevice
	{
		Encoding CurrentEncoding { get; }
	}

	/// <summary>
	/// Base class for simple devices that are capable of sending and receiving only one command at a time 
	/// (but with multiple arbitrary arguments), command is designated by an arrow of bytes,
	/// command designator and arguments are separated by ArgSeparator, command ends with a CmdSeparator 
	/// </summary>
	[DataContract(Namespace = "MDC/MCUI")]
	public class SimpleDevice : ITransactionDevice, IHardwareDevice
	{
		//Is used in multiple places to prevent cross-thread access to byte[] properties 
		//(for example prevent changes to PresenceAnsw while the answer is processed) etc
		[DataMember(IsRequired = true)]
		protected readonly object arrPropLock = new object();
		//Is used in presence detection.
		[DataMember(IsRequired = true)]
		protected readonly object tranLock = new object();
		//Probably not required.
		[DataMember(IsRequired = true)]
		protected readonly object errLock = new object();
		//Used as a flag for script execution only.
		[DataMember(IsRequired = true)]
		protected readonly object scriptLock = new object();

		//Events
		public event EventHandler OnPresenceDetected;
		public event EventHandler OnReset;
		public event EventHandler<LogEventArgs> OnLogActivity;
		public event EventHandler OnTransactionCompleted;
		public event EventHandler OnTransactionTimeout;
		public event EventHandler<CommandReceivedEventAgrs> OnCommandReceived;
		public event EventHandler<LogEventArgs> OnTerminalActivity;
		public event EventHandler<ExceptionEventArgs> OnError;
		public event EventHandler<CommandReceivedEventAgrs> OnUnexpectedResponse;

		//Fields 
		private bool _retry = false;
		[DataMember(Name = "CommandSeparator")]
		private byte[] _cmd_sep;
		[DataMember(Name = "ArgumentSeparator")]
		private byte[] _arg_sep;
		[DataMember(Name = "PresenceCommand")]
		private byte[] _pres_cmd;
		[DataMember(Name = "PresenceAnswer")]
		private byte[] _pres_ans;
		private Transaction _tran;

		protected bool _present;
		protected List<byte> _data;
		protected Exception _err;
		protected bool _firstDetection;
		protected bool _ignoreNextUnexpectedPresence;

		//Properties
		#region Properties   
		[DataMember(Name = "Name")]
		private readonly string _name;
		public string Name { get { return _name; } }
		[DataMember(Name = "Port")]
		private readonly ComPort _port;
		public ComPort Port { get { return _port; } }
		[DataMember]
		public CommandSet KnownCommands { get; private set; } = new CommandSet();
		public bool Com0comCompatibilityMode { get; set; }
		[DataMember]
		public int SendTimeout { get; set; } = 5000;
		[DataMember]
		public int ReceiveTimeout { get; set; } = 10000;
		[DataMember]
		public int LockTimeout { get; set; } = 11000;
		private string _encodingName = "";
		[DataMember]
		private string EncodingSerializedName
		{
			get
			{
				return CurrentEncoding == null ? "" : CurrentEncoding.WebName;
			}
			set
			{
				_encodingName = value;
			}
		}
		public Encoding CurrentEncoding { get; set; }
		[DataMember]
		public int MaxBufferSize { get; set; } = 256;
		public bool Present
		{
			get
			{
				return _present;
			}
			protected set
			{
				if (_present != value)
				{
					_present = value;
					if (_present)
					{
						OnPresenceDetected?.Invoke(this, new EventArgs());
					}
				}
			}
		}
		[DataMember]
		public bool RetryToDetect
		{
			get
			{
				return _retry;
			}
			set
			{
				if (_retry != value)
				{
					_retry = value;
					if (_retry)
					{
						OnTransactionTimeout += RetryDetection;
					}
					else
					{
						OnTransactionTimeout -= RetryDetection;
					}
				}
			}
		}
		[DataMember]
		public int RetryDelay
		{
			get; set;
			/*get { return _retryDelay; }
			set
			{
				//_retryDelay = value < (SendTimeout + ReceiveTimeout) ? (SendTimeout + ReceiveTimeout) : value;
				//_retryTimer.Interval = _retryDelay;
			}*/
		} = 1000;
		public byte[] CmdSeparator
		{
			get
			{
				return SafeGetArrayProp(_cmd_sep, arrPropLock);
			}
			set
			{
				lock (arrPropLock)
				{
					_cmd_sep = value;
				}
			}
		}
		private string _cmdSepString;
		[DataMember]
		public string CommandSeparatorString
		{
			get
			{
				return CurrentEncoding == null ? null : CurrentEncoding.GetString(CmdSeparator).Escape();
			}
			protected set
			{
				_cmdSepString = value;
			}
		}
		public byte[] ArgSeparator
		{
			get
			{
				return SafeGetArrayProp(_arg_sep, arrPropLock);
			}
			set
			{
				lock (arrPropLock)
				{
					_arg_sep = value;
				}
			}
		}
		private string _argSepString;
		[DataMember]
		public string ArgumentSeparatorString
		{
			get
			{
				return CurrentEncoding == null ? null : CurrentEncoding.GetString(ArgSeparator).Escape();
			}
			protected set
			{
				_argSepString = value;
			}
		}
		public byte[] PresenceCmd
		{
			get
			{
				return SafeGetArrayProp(_pres_cmd, arrPropLock);
			}
			set
			{
				lock (arrPropLock)
				{
					_pres_cmd = value;
				}
			}
		}
		private string _presCmdString;
		[DataMember]
		public string PresenceCommandString
		{
			get
			{
				return CurrentEncoding == null ? null : CurrentEncoding.GetString(PresenceCmd).Escape();
			}
			protected set
			{
				_presCmdString = value;
			}
		}
		public byte[] PresenceAnsw
		{
			get
			{
				return SafeGetArrayProp(_pres_ans, arrPropLock);
			}
			set
			{
				lock (arrPropLock)
				{
					_pres_ans = value;
				}
			}
		}
		private string _presAnswString;
		[DataMember]
		public string PresenceAnswerString
		{
			get
			{
				return CurrentEncoding == null ? null : CurrentEncoding.GetString(PresenceAnsw).Escape();
			}
			protected set
			{
				_presAnswString = value;
			}
		}
		public byte[] BufferedData
		{
			get
			{
				byte[] res;
				lock (arrPropLock)
				{
					res = _data.ToArray();
				}
				return res;
			}
		}
		public Transaction CurrentTransaction
		{
			get
			{
				Transaction res;
				lock (tranLock)
				{
					res = _tran;
				}
				return res;
			}
			private set
			{
				lock (tranLock)
				{
					if (_tran != null)
					{
						_tran.OnCompleted -= RaiseOnCompleted;
						_tran.OnTimeout -= RaiseOnTimeout;
						_tran.OnUnexpectedResponse -= RaiseOnUnexpected;
						_tran.OnLogActivity -= _tran_OnLogActivity;
					}
					_tran = value;
					if (_tran != null)
					{
						VerboseLog("CurrentTransaction changed to '{0}'", _tran.CurrentCommand == null ? "NotACommand" : _tran.CurrentCommand.Name);
						_tran.OnCompleted += RaiseOnCompleted;
						_tran.OnTimeout += RaiseOnTimeout;
						_tran.OnUnexpectedResponse += RaiseOnUnexpected;
						if (EnableVerboseLog) _tran.OnLogActivity += _tran_OnLogActivity;
					}
				}
			}
		}
		public Exception LastError
		{
			get
			{
				Exception err;
				lock (errLock)
				{
					err = _err;
					_err = null;
				}
				return err;
			}
		}
		[DataMember]
		public CommandScript<string> InitScript { get; set; }
		[DataMember]
		public CommandScript<string> UpdateScript { get; set; }
		[DataMember]
		public bool EnableVerboseLog { get; set; } = false;
		public List<IHardwareEntity> Hardware { get; private set; }
		#endregion

		//Constructors
		public SimpleDevice(string name, byte[] cmd_sep, byte[] arg_sep, byte[] pres_cmd, byte[] pres_ans, ComPort port)
		{
			_name = name;
			_port = port;
			CmdSeparator = cmd_sep;
			ArgSeparator = arg_sep;
			PresenceCmd = pres_cmd;
			PresenceAnsw = pres_ans;
			InitializeNonSerializedObjects(new StreamingContext());
		}
		public SimpleDevice(string name, byte[] cmd_sep, byte[] arg_sep, byte[] pres_cmd, byte[] pres_ans,
			int baud_rate, int databits, Parity parity, StopBits stopbits, Handshake handshake)
			: this(name, cmd_sep, arg_sep, pres_cmd, pres_ans, new ComPort(baud_rate, databits, parity, stopbits, handshake))
		{ }
		public SimpleDevice(string name, Encoding encoding, string cmd_sep, string arg_sep, string pres_cmd, string pres_ans, ComPort port)
			: this(name, encoding.GetBytes(cmd_sep), encoding.GetBytes(arg_sep), encoding.GetBytes(pres_cmd), encoding.GetBytes(pres_ans), port)
		{
			CurrentEncoding = encoding;
		}
		public SimpleDevice(string name, Encoding encoding, string cmd_sep, string arg_sep, string pres_cmd, string pres_ans,
			int baud_rate, int databits, Parity parity, StopBits stopbits, Handshake handshake)
			: this(name, encoding, cmd_sep, arg_sep, pres_cmd, pres_ans, new ComPort(baud_rate, databits, parity, stopbits, handshake))
		{ }
		~SimpleDevice()
		{
			Disconnect();
		}
		[OnDeserialized]
		private void InitializeNonSerializedObjects(StreamingContext context)
		{
			Port.DataReceived += DataReceived;
			OnUnexpectedResponse += CheckUnexpectedForPresence;
			if (InitScript == null) InitScript = new CommandScript<string>(Name + "_InitScript");
			if (UpdateScript == null) UpdateScript = new CommandScript<string>(Name + "_UpdateScript");
			_data = new List<byte>();
			Hardware = new List<IHardwareEntity>();
			_present = false;
			_ignoreNextUnexpectedPresence = true;
			_firstDetection = false;
			if ((_encodingName ?? "").Length > 0)
			{
				try
				{
					CurrentEncoding = Encoding.GetEncoding(_encodingName);
				}
				catch (ArgumentException ex)
				{
					throw new ArgumentException(@"Invalid encoding name or unsupported encoding specified. 
To set CurrentEncoding to null during deserialization use an empty string.", ex);
				}
				if (_presAnswString != null) PresenceAnsw = CurrentEncoding.GetBytes(_presAnswString.UnEscape());
				if (_presCmdString != null) PresenceCmd = CurrentEncoding.GetBytes(_presCmdString.UnEscape());
				if (_cmdSepString != null) CmdSeparator = CurrentEncoding.GetBytes(_cmdSepString.UnEscape());
				if (_argSepString != null) ArgSeparator = CurrentEncoding.GetBytes(_argSepString.UnEscape());
			}
		}

		//Methods
		/// <summary>
		/// Opens port (safe)
		/// </summary>
		/// <param name="port_name"></param>
		public void Connect(string port_name)
		{
			Disconnect();
			Port.Open(port_name);
			_firstDetection = true;
		}
		/// <summary>
		/// Closes port (safe)
		/// </summary>
		public void Disconnect()
		{
			if (Port.IsOpen) Port.Close();
		}
		/// <summary>
		/// Starts a transaction. Waits for previous one to finish first, hence can throw TimeoutException.
		/// </summary>
		/// <param name="t"></param>
		public void BeginTransaction(Transaction t)
		{
			if (t.Device != this) throw new ArgumentException("This transaction is not meant to be executed on this device.");
			WaitForTransaction();
			if (!Monitor.TryEnter(arrPropLock, LockTimeout))
				throw new TimeoutException("Lock timeout exceeded (BeginTransaction).");
			VerboseLog("Starting transaction '{0}'.", t.CurrentCommand == null ? "NotACommand" : t.CurrentCommand.Name);
			Exception err = null;
			byte[] cmd = null;
			try
			{
				var l = ArrayTools.JoinArray(new byte[][] { t.Command, ArrayTools.JoinArray(t.Arguments, t.ArgSeparator).ToArray() },
					t.ArgSeparator).ToList();
				l.AddRange(t.CmdSeparator);
				cmd = l.ToArray();
			}
			catch (Exception ex)
			{
				err = ex;
			}
			finally
			{
				Monitor.Exit(arrPropLock);
			}
			if (err != null)
			{
				RaiseOnError(this, err, "Error during transaction initialization.");
				CurrentTransaction = null;
				return;
			}
			try
			{
				Port.Send(cmd);
				t.SetSent();
				OnTerminalActivity?.Invoke(this, new LogEventArgs(cmd, CurrentEncoding));
			}
			catch (Exception ex)
			{
				RaiseOnError(this, ex, "Error during sending transaction.");
				CurrentTransaction = null;
				return;
			}
			CurrentTransaction = t;
			VerboseLog("Transaction sent: '{0}'", t.CurrentCommand == null ? "NotACommand" : t.CurrentCommand.Name);
		}
		public void BeginTransaction(string commandName, params string[] values)
		{
			BeginTransaction(KnownCommands[commandName].CreateTransaction(this, values));
		}
		public void BeginTransaction(IEncodedCommand cmd, params string[] values)
		{
			BeginTransaction(cmd.CreateTransaction(this, values));
		}
		/// <summary>
		/// Combines BeginTransaction() and WaitForTransaction().
		/// </summary>
		/// <param name="commandName">Name from CommandSet</param>
		/// <param name="values"></param>
		/// <returns>True = timed out, False = truly completed</returns>
		public bool PerformTransaction(string commandName, params string[] values)
		{
			BeginTransaction(commandName, values);
			return WaitForTransaction();
		}
		public bool PerformTransaction(IEncodedCommand cmd, params string[] values)
		{
			BeginTransaction(cmd, values);
			return WaitForTransaction();
		}
		/// <summary>
		/// Executes BeginTransaction in a new thread (exceptions are still caught through OnError).
		/// </summary>
		/// <param name="t"></param>
		public void BeginTransactionAsync(Transaction t)
		{
			SafeAsyncExecute(() => { BeginTransaction(t); });
		}
		public void BeginTransactionAsync(string commandName, params string[] values)
		{
			SafeAsyncExecute(() => { BeginTransaction(commandName, values); });
		}
		public void BeginTransactionAsync(IEncodedCommand cmd, params string[] values)
		{
			SafeAsyncExecute(() => { BeginTransaction(cmd, values); });
		}
		/// <summary>
		/// Wrapper for Port.DiscardBuffers(). Not sure if needed on real hardware,
		/// but on emulators such as com0com such thing are sometimes required.
		/// </summary>
		/// <param name="clearTransaction">true: clear CurrentTransaction (to disable automatic reconnect)</param>
		public void Flush(bool clearTransaction = false)
		{
			try
			{
				if (clearTransaction) CurrentTransaction = null;
				Port.DiscardBuffers();
			}
			catch (Exception ex)
			{
				RaiseOnError(this, ex, "Ошибка при очистке порта.");
			}
			finally
			{
				if (!Monitor.TryEnter(arrPropLock, LockTimeout))
					throw new TimeoutException("Превышено время доступа к свойствам во время очистки порта.");
				try
				{
					_data.Clear();
				}
				catch (Exception exc)
				{
					RaiseOnError(this, exc, "Ошибка при очистке данных порта.");
				}
				finally
				{
					Monitor.Exit(arrPropLock);
				}
			}
		}
		/// <summary>
		/// Starts presence detection transaction and enables internal response check for expected answer
		/// </summary>
		public void DetectPresence(bool resetCurrent)
		{
			WaitForTransaction();
			if (_firstDetection && Com0comCompatibilityMode)
			{
				_firstDetection = false;
				_ignoreNextUnexpectedPresence = true;
				OnTransactionCompleted += ProcessCom0comCompatibility;
			}
			try
			{
				if (Monitor.TryEnter(tranLock, LockTimeout))
				{
					Port.DiscardBuffers();
					if (resetCurrent) _present = false;
					OnTransactionCompleted += CheckTransactionForPresence;
					try
					{
						BeginTransaction(new Transaction(PresenceCmd, null, this));
					}
					catch (Exception ex)
					{
						OnTransactionCompleted -= CheckTransactionForPresence;
						RaiseOnError(this, ex, "Can't send presence command.");
					}
				}
				else
				{
					RaiseOnError(this, null, "Timeout during waiting for exclusive access to Transaction during sending Presence command.");
				}
			}
			finally
			{
				try
				{
					Monitor.Exit(tranLock);
				}
				catch (SynchronizationLockException)
				{ }
			}
		}
		public void DetectPresence()
		{
			DetectPresence(true);
		}
		/// <summary>
		/// Direct sending. Waits for CurrentTransaction.Completed (can throw Timeout), but does not handle exceptions
		/// </summary>
		/// <param name="cmd">All command bytes without CmdSeparator</param>
		public void SendCommand(byte[] cmd)
		{
			if (cmd == null) return;
			List<byte> c = new List<byte>(cmd);
			c.AddRange(CmdSeparator);
			cmd = c.ToArray();
			if (WaitForTransaction())
			{
				throw new TimeoutException("Время ожидаия при отправке команды превышено.");
			}
			else
			{
				Port.Send(cmd);
				OnTerminalActivity?.Invoke(this, new LogEventArgs(cmd, CurrentEncoding));
			}
		}
		public void SendCommand(string cmd)
		{
			SendCommand(CurrentEncoding.GetBytes(cmd));
		}
		/// <summary>
		/// Waits for CurrentTransaction to complete. Transaction has a built-in timeout.
		/// </summary>
		/// <returns>Whether or not the transaction has timed out.</returns>
		public bool WaitForTransaction()
		{
			VerboseLog("Trying to acquire scriptLock...");
			//Don't interfere with script execution
			lock (scriptLock)  //Probably it's safe, because script execution is inherently timeout-ed through Transaction
			{
				VerboseLog("Waiting for current transaction to complete...");
				while (CurrentTransaction != null && !CurrentTransaction.Completed) Thread.Sleep(10);
				VerboseLog("Finished waiting for current transaction to complete.");
				return CurrentTransaction == null ? false : CurrentTransaction.TimedOut;
			}
		}
		/// <summary>
		/// Calls PerformTransaction() for all command in the script.
		/// Throws ArgumentException on nonexistent command and Exception on error during performing transaction.
		/// </summary>
		/// <param name="script"></param>
		/// <param name="skipNonexistent">Does not suppress logging (OnError).</param>
		/// <param name="suppressExecutionExceptions">Does not suppress logging.</param>
		/// <returns>True if a transaction timed out (returns immediately). False if all of the transactions completed OK.</returns>
		public virtual bool ExecuteScript(CommandScript<string> script,
			bool skipNonexistent = true, bool suppressExecutionExceptions = true)
		{
			VerboseLog("Executing script '{0}'.", script.Name);
			bool retry = RetryToDetect;
			bool timedOut = false;
			bool lockTaken = false;
			try
			{
				if (!Monitor.TryEnter(scriptLock, LockTimeout))
				{
					RaiseOnError(this, new TimeoutException("Can't lock on scriptLock. Script execution has to be canceled."));
					return true;
				}
				lockTaken = true;
				RetryToDetect = false;
				foreach (var item in script)
				{
					try
					{
						if (PerformTransaction(item.Key, item.Value))
						{
							timedOut = true;
							break;
						}
					}
					catch (ArgumentOutOfRangeException)
					{
						RaiseOnError(this, null, string.Format("Unknown command '{0}' inside script '{1}'.", item.Key, script.Name));
						if (!skipNonexistent) throw new ArgumentException(string.Format("Command '{0}' not found in current CommadSet.", item.Key));
					}
					catch (Exception ex)
					{
						ex = new Exception("Error during performing transaction.", ex);
						RaiseOnError(this, ex.InnerException, ex.Message);
						if (!suppressExecutionExceptions) throw ex;
					}
					try
					{
						if (script.ProgressCallbackInvoker != null) SafeAsyncExecute(script.ProgressCallbackInvoker);
					}
					catch (Exception ex)
					{
						RaiseOnError(this, ex, "Error during progress callback invocation.");
					}
				}
				if (timedOut)
				{
					if (retry) SafeAsyncExecute(() => { RetryDetection(this, new EventArgs()); });
					return true;
				}
			}
			finally
			{
				RetryToDetect = retry;
				if (lockTaken) Monitor.Exit(scriptLock);
				VerboseLog("Script '{0}' executed.", script.Name);
			}
			return false;
		}

		protected void RaiseOnError(object sender, Exception inner, string msg = "")
		{
			if (Monitor.TryEnter(errLock, LockTimeout))
			{
				try
				{
					_err = new Exception(msg, inner);
				}
				catch (Exception)
				{ }
				finally
				{
					Monitor.Exit(errLock);
				}

				OnError?.Invoke(sender, new ExceptionEventArgs(_err));
			}
			else
			{
				OnError?.Invoke(sender, new ExceptionEventArgs(new Exception("Превышено время ожидания объекта ошибки! " + msg, inner)));
			}
		}
		protected void RaiseOnCompleted(object sender, EventArgs e)
		{
			OnTransactionCompleted?.Invoke(sender, e);
		}
		protected void RaiseOnTimeout(object sender, EventArgs e)
		{
			OnTransactionTimeout?.Invoke(sender, e);
		}
		protected void RaiseOnUnexpected(object sender, CommandReceivedEventAgrs e)
		{
			OnUnexpectedResponse?.Invoke(sender, e);
		}
		protected void RaiseOnLogActivity(string format, params object[] args)
		{
			OnLogActivity?.Invoke(this, new LogEventArgs(string.Format(format, args)));
		}
		protected void VerboseLog(string format, params object[] args)
		{
			if (EnableVerboseLog) RaiseOnLogActivity(format, args);
		}
		protected void SafeAsyncExecute(Action method)
		{
			Thread thrCmd = new Thread(() =>
			{
				try
				{
					method.Invoke();
				}
				catch (Exception ex)
				{
					RaiseOnError(this, ex, "В отдельном потоке возникло исключение.");
				}
			});
			thrCmd.Start();
		}
		protected T[] SafeGetArrayProp<T>(T[] a, object lockObj)
		{
			T[] res = null;
			Exception err = null;
			if (!Monitor.TryEnter(lockObj, LockTimeout)) throw new TimeoutException("Превышено время ожидания доступа к свойству.");
			try
			{
				res = new T[a.Length];
				a.CopyTo(res, 0);
			}
			catch (Exception ex)
			{
				err = ex;
			}
			finally
			{
				Monitor.Exit(lockObj);
			}
			if (err != null) throw err;
			return res;
		}

		private void DataReceived(byte[] data)
		{
			if (!Monitor.TryEnter(arrPropLock, LockTimeout))
			{
				RaiseOnError(this, null, "Превышено время ожидания доступа к свойствам (DataReceived).");
				return;
			}
			try
			{
				_data.AddRange(data);
				if (_data.Count > MaxBufferSize)
				{
					_data.RemoveRange(0, _data.Count - MaxBufferSize);
					RaiseOnError(this, new OverflowException(), "Ошибка при получении данных.");
				}
				try
				{
					OnTerminalActivity?.Invoke(this, new LogEventArgs(data, CurrentEncoding));
				}
				catch (Exception ex)
				{
					RaiseOnError(this, ex, "Ошибка внутри делегата терминала.");
				}
				List<byte> command = new List<byte>();
				int ind;
				while (true)
				{
					ArrayTools.TrimArray(CmdSeparator, _data, 1);                //Tidy it a bit (remove line feed fragments)
					ind = ArrayTools.IndexOfRow(CmdSeparator, _data);
					if (ind < 0) break;
					command = ArrayTools.SubArray(_data, 0, ind).ToList();    //If we have received the whole command
					ind += CmdSeparator.Length;
					_data.RemoveRange(0, _data.Count < ind ? _data.Count : ind); //Copy it and remove from the buffer
					ArrayTools.TrimArray(CmdSeparator, command);                //Tidy it a bit (remove line feed fragments)
					if (command.Count == 0) continue;
					/*if (IgnoreNextResponse)
					{
						IgnoreNextResponse = false;
						continue;
					}*/
					byte[][] res = ArrayTools.SplitArray(ArgSeparator, command);      //Separate command designator and arguments
					try
					{
						var ea = new CommandReceivedEventAgrs(res[0], res.Skip(1).ToArray());
						bool unexp = CurrentTransaction == null || CurrentTransaction.Completed;
						VerboseLog("Invoking OnCommandReceived, unexp = {0}.", unexp);
						OnCommandReceived?.Invoke(this, ea); //Pass it on to the user 
						if (unexp) RaiseOnUnexpected(this, ea);
					}
					catch (Exception ex)
					{
						RaiseOnError(this, ex, "Ошибка внутри делегата OnCommandRecevied.");
					}
				}
			}
			catch (Exception ex)
			{
				RaiseOnError(this, ex, "Ошибка при разборе данных.");
				Flush();
			}
			finally
			{
				Monitor.Exit(arrPropLock);
			}
		}
		private void CheckTransactionForPresence(object sender, EventArgs e)
		{
			try
			{
				OnTransactionCompleted -= CheckTransactionForPresence;
				Transaction t = (sender as Transaction);
				if (t.Response != null) Present = (ArrayTools.IndexOfRow(PresenceAnsw, t.Response[0].ToList()) > -1);
			}
			catch (Exception ex)
			{
				Present = false;
				RaiseOnError(this, ex, "Ошибка при разборе ответа присутствия.");
			}
		}
		private void CheckUnexpectedForPresence(object sender, CommandReceivedEventAgrs e)
		{
			if (_ignoreNextUnexpectedPresence) return;
			if (ArrayTools.IndexOfRow(PresenceAnsw, e.Arguments[0].ToList()) > -1)
			{
				if (Present)
				{
					Present = false;
					OnReset?.Invoke(this, new EventArgs());
				}
				else
				{
					Present = true;
				}
			}
		}
		private void RetryDetection(object sender, EventArgs e)
		{
			if (Com0comCompatibilityMode)
			{
				_ignoreNextUnexpectedPresence = true;
				if (!OnTransactionCompleted.GetInvocationList().Contains((EventHandler)ProcessCom0comCompatibility))
				{
					OnTransactionCompleted += ProcessCom0comCompatibility;
				}
			}
			SafeAsyncExecute(() =>
			{
				Thread.Sleep(RetryDelay);
				lock (arrPropLock)             //Make sure we are not processing possible presence now
				{
					//if (!Present)
					//{
					SafeAsyncExecute(() => { DetectPresence(); });
					//}
				}
			});
		}
		private void ProcessCom0comCompatibility(object sender, EventArgs e)
		{
			Transaction t = sender as Transaction;
			if (t == null) return;
			if (!t.Command.SequenceEqual(PresenceCmd))
			{
				OnTransactionCompleted -= ProcessCom0comCompatibility;
				_ignoreNextUnexpectedPresence = false;
			}
		}
		private void _tran_OnLogActivity(object sender, LogEventArgs e)
		{
			var t = (sender as Transaction);
			string name = "null";
			if (t != null) name = t.CurrentCommand == null ? "NotACommand" : t.CurrentCommand.Name;
			VerboseLog("Transaction '{0}' reported: {1}", name, e.Text);
		}
	}
	#endregion
}
