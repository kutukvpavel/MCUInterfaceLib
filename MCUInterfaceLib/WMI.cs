using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management.Instrumentation;
using System.Reflection;

[assembly: WmiConfiguration(@"root\MCUI", HostingModel = ManagementHostingModel.Decoupled)]
[assembly: Instrumented(@"root\MCUI")]

namespace MCUI
{
	#region Installers
	/// <summary>
	/// For all WMI classes, but events
	/// </summary>
	[RunInstaller(true)]
	public class WmiDotNet35Installer : DefaultManagementInstaller { }
	/// <summary>
	/// For WMI events
	/// </summary>
	[RunInstaller(true)]
	public class WmiDotNet20Installer : DefaultManagementProjectInstaller { }
	#endregion

	/// <summary>
	/// Informs WMI listeners about a change of value.
	/// Listeners have to look it up themselves (WMI does not support generic types of classes).
	/// </summary>
	[InstrumentationClass(InstrumentationType.Event)]
	public class WmiOnStateChangedEvent
	{
		public WmiOnStateChangedEvent(string hwid)
		{
			Hwid = hwid;
		}
		public string Hwid { get; }
		public static void Fire(IWmiHardwareEntity obj)
		{
			Fire(obj.Hwid);
		}
		public static void Fire(string hwid)
		{
			if (WmiProvider.Instance.EnableEvents) Instrumentation.Fire(new WmiOnStateChangedEvent(hwid));
		}
	}
	[InstrumentationClass(InstrumentationType.Event)]
	public class WmiOnReadynessChangedEvent
	{
		public WmiOnReadynessChangedEvent(string deviceName, bool ready)
		{
			DeviceName = deviceName;
			Ready = ready;
		}
		public string DeviceName { get; }
		public bool Ready { get; }
		public static void Fire(IMcuDevice dev, bool ready)
		{
			Fire(dev.Name, ready);
		}
		public static void Fire(string deviceName, bool ready)
		{
			if (WmiProvider.Instance.EnableEvents) Instrumentation.Fire(new WmiOnReadynessChangedEvent(deviceName, ready));
		}
	}
	/*
	/// <summary>
	/// Informs WMI listeners that a sensor set off the alarm.
	/// Listeners have to look exact value up themselves (WMI does not support generic types of classes).
	/// Basically offers the same functionality that WmiOnStateChanged does, but type name is essential for WMI.
	/// </summary>
	[InstrumentationClass(InstrumentationType.Event)]
	public class WmiOnAlarmEvent
	{
		public WmiOnAlarmEvent(string hwid)
		{
			Hwid = hwid;
		}
		public string Hwid { get; }
		public static void Fire(IWmiHardwareEntity obj)
		{
			Fire(obj.Hwid);
		}
		public static void Fire(string hwid)
		{
			if (WmiProvider.Instance.EnableEvents) Instrumentation.Fire(new WmiOnAlarmEvent(hwid));
		}
	}
	*/
	/// <summary>
	/// Wrapper for Instrumentation/InstrumentationManager and ManagedInstaller.
	/// </summary>
	public sealed class WmiProvider
	{
		//Singleton
		private static readonly WmiProvider _instance = new WmiProvider();
		static WmiProvider() { }
		private WmiProvider() { }
		public static WmiProvider Instance
		{
			get
			{
				return _instance;
			}
		}
		/// <summary>
		/// Install running MCUI assembly
		/// </summary>
		public static void InstallAssembly()
		{
			InstallAssembly(Assembly.GetExecutingAssembly().Location);
		}
		/// <summary>
		/// Install any assembly
		/// </summary>
		/// <param name="location">Path (or UNC)</param>
		public static void InstallAssembly(string location)
		{
			System.Configuration.Install.ManagedInstallerClass.InstallHelper(new[] { location });
		}
		//Searches GAC for current assembly. Somehow installed WMI assemblies do not end up in GAC, so it's useless here.
		/*public static bool IsAssemblyInstalled(string location)
		{
			try
			{
				return Assembly.ReflectionOnlyLoad(Assembly.ReflectionOnlyLoadFrom(location).FullName).GlobalAssemblyCache;
			}
			catch
			{
				return false;
			}
		}
		public static bool IsAssemblyInstalled()
		{
			return IsAssemblyInstalled(Assembly.GetExecutingAssembly().Location);
		}*/
		/// <summary>
		/// Global flag that is checked internally by Wmi...Event classes.
		/// </summary>
		public bool EnableEvents { get; set; } = false;
		private Dictionary<string, IWmiHardwareEntity> _pub = new Dictionary<string, IWmiHardwareEntity>();
		private Dictionary<string, WmiReadynessInformer> _inform = new Dictionary<string, WmiReadynessInformer>();
		/// <summary>
		/// Returns internal list of published objects. Does not query WMI.
		/// </summary>
		public IEnumerable<IWmiHardwareEntity> Published
		{
			get
			{
				return _pub.Values;
			}
		}
		public IEnumerable<WmiReadynessInformer> Informers
		{
			get
			{
				return _inform.Values;
			}
		}
		/// <summary>
		/// Published object to WMI. Automatically recognizes .NET 2.0 and 3.5 classes (through reflection).
		/// Exceptions are thrown if the object has been already published or if the assembly is not installed.
		/// </summary>
		/// <param name="obj"></param>
		public void Publish(IWmiHardwareEntity obj)
		{
			if (Contains(obj)) throw new ArgumentException("This IHardware object is already published.", "obj.Hwid = " + obj.Hwid);
			if (obj.GetType().GetCustomAttributes(typeof(ManagementEntityAttribute), false).Any())
			{
				InstrumentationManager.Publish(obj);
			}
			else
			{
				Instrumentation.Publish(obj);
			}
			_pub.Add(obj.Hwid, obj);
		}
		/// <summary>
		/// See overload.
		/// </summary>
		/// <param name="entity"></param>
		/// <param name="deviceName"></param>
		/// <returns></returns>
		public static string GetHwid(IHardwareEntity entity, string deviceName = "")
		{
			return GetHwid(entity.Index, entity.Name, entity.GetType(), deviceName);
		}
		/// <summary>
		/// Simply combines TypeName_DeviceName_Index_Name. Spaces are replaced with underscores.
		/// </summary>
		/// <param name="Index">Device's internal index of entity</param>
		/// <param name="Name">Friendly name. Must not contain special characters (only underscores or spaces).</param>
		/// <param name="t">Exact type of the instance. Must not begin with an underscore.</param>
		/// <param name="deviceName">Required only if multiple devices are connected</param>
		/// <returns>Generated HWID. Warning: Name validation is not performed!</returns>
		public static string GetHwid(int Index, string Name, Type t, string deviceName = "")
		{
			return string.Format("{0}_{1}_{2}_{3}", t.Name, deviceName, Index, Name).Replace(' ', '_');
		}
		public void PublishInformer(WmiReadynessInformer obj)
		{
			if (ContainsInformer(obj))
				throw new ArgumentException("This informer has already been already published.", "obj.ApplicationName = " + obj.ApplicationName);
			if (obj.GetType().GetCustomAttributes(typeof(ManagementEntityAttribute), false).Any())
			{
				InstrumentationManager.Publish(obj);
			}
			else
			{
				Instrumentation.Publish(obj);
			}
			_inform.Add(obj.ApplicationName, obj);
		}
		/// <summary>
		/// Revokes a published object. ArgumentException is thrown if the object has not been published by means of this provider.
		/// </summary>
		/// <param name="obj">The object has to be the one published through this provider class.</param>
		/// <param name="force">Suppresses WMI exceptions (but not ArgumentException) and forcefully clears internal list.</param>
		public void Revoke(IWmiHardwareEntity obj, bool force = false)
		{
			if (!Contains(obj)) throw new ArgumentException("IHardware object with specified HWID has not been published through this provider.");
			try
			{
				if (obj.GetType().GetCustomAttributes(typeof(ManagementEntityAttribute), false).Any())
				{
					InstrumentationManager.Revoke(obj);
				}
				else
				{
					Instrumentation.Revoke(obj);
				}
			}
			catch (Exception ex)
			{
				if (!force)
				{
					throw ex;
				}
			}
			_pub.Remove(obj.Hwid);
		}
		public void RevokeInformer(WmiReadynessInformer obj, bool force = false)
		{
			if (!ContainsInformer(obj)) throw new ArgumentException("Informer with specified ApplicationName has not been published through this provider.");
			try
			{
				if (obj.GetType().GetCustomAttributes(typeof(ManagementEntityAttribute), false).Any())
				{
					InstrumentationManager.Revoke(obj);
				}
				else
				{
					Instrumentation.Revoke(obj);
				}
			}
			catch (Exception ex)
			{
				if (!force)
				{
					throw ex;
				}
			}
			_pub.Remove(obj.ApplicationName);
		}
		public void RevokeAllInformers(bool force = false)
		{
			int l = _inform.Count;
			for (int i = 0; i < l; i++)
			{
				RevokeInformer(_inform.ElementAt(0).Value, force);
			}
		}
		/// <summary>
		/// Simply a wrapper for Revoke()
		/// </summary>
		/// <param name="force"></param>
		public void RevokeAll(bool force = false)
		{
			int l = _pub.Count;
			for (int i = 0; i < l; i++)
			{
				Revoke(_pub.ElementAt(0).Value, force);
			}
		}
		/// <summary>
		/// Returns published instance from internal list. Exception is thrown if there is no such entity in the list.
		/// </summary>
		/// <param name="id">HWID</param>
		/// <returns></returns>
		public IWmiHardwareEntity this[string id]
		{
			get
			{
				return _pub[id];
			}
			set
			{
				_pub[id] = value;
			}
		}
		/// <summary>
		/// Utilizes HWID comparison (other properties are not required by IWmiHardwareEntity and thus are not explicitly compared, 
		/// though HWID-generation entirely depends on them and HWID comparison implies Name/Index comparison).
		/// </summary>
		/// <param name="entity"></param>
		/// <returns></returns>
		public bool Contains(IWmiHardwareEntity entity)
		{
			return Contains(entity.Hwid);
		}
		public bool ContainsInformer(WmiReadynessInformer informer)
		{
			return _inform.ContainsKey(informer.ApplicationName);
		}
		public bool ContainsInformer(string appName)
		{
			return _inform.ContainsKey(appName);
		}
		/// <summary>
		/// See overload.
		/// </summary>
		/// <param name="hwid"></param>
		/// <returns></returns>
		public bool Contains(string hwid)
		{
			return _pub.ContainsKey(hwid);
		}
	}
	[ManagementEntity]
	public class WmiReadynessInformer
	{
		[ManagementBind]
		public WmiReadynessInformer([ManagementName("ApplicationName")] string name)
		{
			ApplicationName = name;
		}
		[ManagementKey]
		public string ApplicationName { get; }
		private List<string> _notReady = new List<string>();
		[ManagementProbe]
		public string[] NotReadyDevices { get { return _notReady.ToArray(); } }

		public void SetReady(IMcuDevice dev)
		{
			int i = _notReady.IndexOf(dev.Name);
			if (i > -1)
			{
				_notReady.RemoveAt(i);
				WmiOnReadynessChangedEvent.Fire(dev, true);
			}
		}

		public void SetNotReady(IMcuDevice dev)
		{
			if (!_notReady.Contains(dev.Name))
			{
				_notReady.Add(dev.Name);
				WmiOnReadynessChangedEvent.Fire(dev, false);
			}
		}
	}
}
