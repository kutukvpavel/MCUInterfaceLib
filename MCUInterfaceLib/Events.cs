using System;
using System.Text;

//TODO: Move hardware-related functionality here, instead of leaving it to inherited classes

namespace MCUI
{
	public class HardwareEntityRemovalEventArgs : EventArgs
	{
		public HardwareEntityRemovalEventArgs(IHardwareEntity e)
		{
			RemovedEntity = e; 
		}
		public IHardwareEntity RemovedEntity { get; }
		public bool Cancel { get; set; } = false;
	}
	/// <summary>
	/// Hardware entity state changed (used in HardwareBase).
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class StateChangedEventArgs<T> : EventArgs
	{
		public StateChangedEventArgs(T oldState)
		{
			OldState = oldState;
		}
		public T OldState { get; }
	}
	/// <summary>
	/// Non-typed version
	/// </summary>
	public class StateChangedEventArgs : EventArgs
	{
		public StateChangedEventArgs(object oldState)
		{
			OldState = oldState;
		}
		public object OldState { get; }
	}
	/// <summary>
	/// State change requested (intended only for WMI usage)
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class SetRequestedEventAgrs : EventArgs
	{
		public SetRequestedEventAgrs(object req, string sender)
		{
			RequestedState = req;
			Sender = sender;
		}
		public object RequestedState { get; }
		public bool Allowed { get; set; } = false;
		public string Sender { get; }
	}
	//public delegate void StateChangedEventHandler<T>(object sender, StateChangedEventArgs<T> e);
	/// <summary>
	/// For sensors that have some kind of alarm trigger (e.g. temperature)
	/// </summary>
	public class AlarmEventArgs : EventArgs
	{
		public AlarmEventArgs(IHardwareEntity entity)
		{
			Entity = entity;
		}
		public IHardwareEntity Entity { get; }
	}
	/// <summary>
	/// Is fired when a possibly meaningful sequence is found in serial data stream. Used in SimpleDevice.
	/// </summary>
	public class CommandReceivedEventAgrs : EventArgs
	{
		public CommandReceivedEventAgrs(byte[] cmd, byte[][] args)
		{
			Command = cmd;
			Arguments = args;
		}
		public byte[] Command { get; }
		public byte[][] Arguments { get; }
	}
	/// <summary>
	/// Provides bytes flowing inside (in or out) a serial stream that are to be printed in a terminal window.
	/// </summary>
	public class LogEventArgs : EventArgs
	{
		public LogEventArgs(string txt)
		{
			Text = txt;
		}
		public LogEventArgs(byte[] txt, Encoding enc)
		{
			Text = enc.GetString(txt);
		}
		public string Text { get; }
	}
	/// <summary>
	/// General-purpose class for raising error-related events
	/// </summary>
	public class ExceptionEventArgs : EventArgs
	{
		public ExceptionEventArgs(Exception exc)
		{
			Exception = exc;
		}
		public Exception Exception { get; }
	}
}
