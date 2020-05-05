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
    /// The only absolutely necessary thing for WMI publishing is a unique identifier
    /// EnableWmiStateChangedEvent is required for internal WMI events' mechanics
    /// </summary>
    public interface IWmiHardwareEntity
    {
        string Hwid { get; }
        bool EnableWmiStateChangedEvent { get; }
    }
    public interface IWmiSettableEntity : IWmiHardwareEntity
    {
        event EventHandler<SetRequestedEventAgrs> OnSetRequested;
    }
    /// <summary>
    /// In conjunction with type only internal index and friendly name are necessary for an abstract entity
    /// </summary>
    public interface IHardwareEntity
    {
        int Index { get; }
        string Name { get; }
        event EventHandler<StateChangedEventArgs> OnStateChanged;
    }
    /// <summary>
    /// Base class for general (non-WMI yet) entity (pin, sensor etc)
    /// </summary>
    /// <typeparam name="T">Any type that implements IComparable (required for StateChanged event)</typeparam>
    public abstract class HardwareBase<T> : IHardwareEntity where T : struct, IComparable
    {
        //Events
        //public event EventHandler<StateChangedEventArgs<T>> OnStateChanged;
        public event EventHandler<StateChangedEventArgs> OnStateChanged;

        //Private fields
        private T _cur;
        protected Action revert = null;

        //Constructors
        public HardwareBase(int index, string name)
        {
            Index = index;
            Name = name;
        }
        ~HardwareBase()
        {
            UnsubscribeListeners();
        }

        //Properties
        #region Properties
        public int Index { get; protected set; }
        public string Name { get; protected set; }
        public T CurrentState
        {
            get
            {
                return _cur;
            }
            protected set
            {
                if (value.CompareTo(_cur) != 0)
                {
                    T b = _cur;
                    revert = new Action(() => { CurrentState = b; });
                    _cur = value;
                    OnStateChanged?.Invoke(this, new StateChangedEventArgs(b));
                }
            }
        }
        #endregion

        //Methods
        public virtual void SetCurrentState(T state)
        {
            CurrentState = state;
        }
        public void UnsubscribeListeners()
        {
            if (OnStateChanged != null)
            {
                List<Delegate> d = new List<Delegate>(OnStateChanged.GetInvocationList());
                for (int i = 0; i < d.Count; i++)
                {
                    OnStateChanged -= (EventHandler<StateChangedEventArgs>)d[i];
                }
            }
        }
        /// <summary>
        /// For non-readonly properties only
        /// </summary>
        public void RevertLastPropertyChange()
        {
            if (revert != null) revert.Invoke();
        }
    }
    public abstract class WmiHardwareBase<T> : HardwareBase<T>, IWmiHardwareEntity where T : struct, IComparable
    {
        public WmiHardwareBase(int index, string name, string hwid) : base(index, name)
        {
            Index = index;
            Name = name;
            Hwid = hwid;
        }

        public string Hwid { get; }

        public bool EnableWmiStateChangedEvent { get; set; } = true;
        public override void SetCurrentState(T state)
        {
            T v = CurrentState;
            base.SetCurrentState(state);
            if (EnableWmiStateChangedEvent)
                if (v.CompareTo(CurrentState) != 0) WmiOnStateChangedEvent.Fire(this);
        }
    }
    /// <summary>
    /// WMI-capable object that represents a binary input (button).
    /// </summary>
    [ManagementEntity]
    public class Input : WmiHardwareBase<bool>
    {
        public Input(int index, string name, IMcuDevice dev) : base(index, name, WmiProvider.GetHwid(index, name, typeof(Input), dev.Name))
        { }
        public Input(int index, string name) : base(index, name, WmiProvider.GetHwid(index, name, typeof(Input)))
        { }
        [ManagementBind]
        public Input([ManagementName("Index")] int index, [ManagementName("Name")] string name,
            [ManagementName("Hwid")] string hwid) : base(index, name, hwid)
        { }

        #region WMI Properties

        [ManagementKey]
        public new string Name
        {
            get
            {
                return base.Name;
            }
        }
        [ManagementKey]
        public new int Index
        {
            get
            {
                return base.Index;
            }
        }
        [ManagementKey]
        [Browsable(false)]
        public new string Hwid
        {
            get { return base.Hwid; }
        }
        private bool _inverted = false;
        [ManagementProbe]
        public bool Inverted
        {
            get { return _inverted; }
            set
            {
                bool b = _inverted;
                _inverted = value;
                revert = new Action(() => { _inverted = b; });
            }
        }
        private bool _normal = false;
        [ManagementProbe]
        public bool NormalState
        {
            get { return _normal; }
            set
            {
                bool b = _normal;
                _normal = value;
                revert = new Action(() => { _normal = b; });
            }
        }
        private int _mapped = -1;
        [ManagementProbe]
        public int MappedTo
        {
            get { return _mapped; }
            set
            {
                int b = _mapped;
                _mapped = value;
                revert = new Action(() => { _mapped = b; });
            }
        }
        [ManagementProbe]
        public new bool CurrentState
        {
            get
            {
                return base.CurrentState;
            }
        }
        [ManagementProbe]
        public bool Alarm
        {
            get
            {
                return CurrentState != NormalState;
            }
        }

        #endregion
    }
    /// <summary>
    /// WMI-capable object that represents an output pin (essentially a key)
    /// </summary>
    [ManagementEntity]
    public class Output : WmiHardwareBase<bool>, IWmiSettableEntity
    {
        public Output(int index, string name, IMcuDevice dev) : base(index, name, WmiProvider.GetHwid(index, name, typeof(Output), dev.Name))
        { }
        public Output(int index, string name) : base(index, name, WmiProvider.GetHwid(index, name, typeof(Output)))
        { }
        [ManagementBind]
        public Output([ManagementName("Index")] int index, [ManagementName("Name")] string name,
            [ManagementName("Hwid")] string hwid) : base(index, name, hwid)
        { }

        #region WMI Properties

        [ManagementKey]
        public new string Name
        {
            get
            {
                return base.Name;
            }
        }
        [ManagementKey]
        public new int Index
        {
            get
            {
                return base.Index;
            }
        }
        [ManagementKey]
        [Browsable(false)]
        public new string Hwid
        {
            get { return base.Hwid; }
        }
        private bool _inverted = false;
        [ManagementProbe]
        public bool Inverted
        {
            get { return _inverted; }
            set
            {
                bool b = _inverted;
                _inverted = value;
                revert = new Action(() => { _inverted = b; });
            }
        }
        private bool _override = false;
        [ManagementProbe]
        public bool ManualOverride
        {
            get { return _override; }
            set
            {
                bool b = _override;
                _override = value;
                revert = new Action(() => { _override = b; });
            }
        }
        private bool _initial = false;
        [ManagementProbe]
        public bool InitialState
        {
            get { return _initial; }
            set
            {
                bool b = _initial;
                _initial = value;
                revert = new Action(() => { _initial = b; });
            }
        }
        [ManagementProbe]
        public new bool CurrentState
        {
            get
            {
                return base.CurrentState;
            }
        }
        #endregion

        [ManagementTask]
        public void SetCurrentState(bool state, string sender)
        {
            var e = new SetRequestedEventAgrs(state, sender);
            OnSetRequested?.Invoke(this, e);
            if (e.Allowed)
            {
                SetCurrentState(state);
            }
        }
        public event EventHandler<SetRequestedEventAgrs> OnSetRequested;
    }
    /// <summary>
    /// WMI-capable object that represents a floating point input (a temperature or voltage sensor)
    /// </summary>
    [ManagementEntity]
    public class TemperatureProbe : WmiHardwareBase<float>
    {
        public TemperatureProbe(int index, string name, IMcuDevice dev)
            : base(index, name, WmiProvider.GetHwid(index, name, typeof(TemperatureProbe), dev.Name))
        { }
        public TemperatureProbe(int index, string name) : base(index, name, WmiProvider.GetHwid(index, name, typeof(TemperatureProbe)))
        { }
        [ManagementBind]
        public TemperatureProbe([ManagementName("Index")] int index, [ManagementName("Name")] string name,
            [ManagementName("Hwid")] string hwid) : base(index, name, hwid)
        { }

        #region WMI Properties
        [ManagementKey]
        [Browsable(false)]
        public new string Hwid
        {
            get { return base.Hwid; }
        }
        [ManagementKey]
        public new string Name
        {
            get
            {
                return base.Name;
            }
        }
        [ManagementKey]
        public new int Index
        {
            get
            {
                return base.Index;
            }
        }
        [ManagementProbe]
        public new float CurrentState
        {
            get
            {
                return base.CurrentState;
            }
        }
        private float _limit;
        [ManagementProbe]
        public float HigherLimit
        {
            get { return _limit; }
            set
            {
                float b = _limit;
                _limit = value;
                revert = new Action(() => { _limit = b; });
            }
        }
        [ManagementProbe]
        public bool Alarm
        {
            get
            {
                return CurrentState > HigherLimit;
            }
        }
        private int _mapped = -1;
        [ManagementProbe]
        public int MappedTo
        {
            get { return _mapped; }
            set
            {
                int b = _mapped;
                _mapped = value;
                revert = new Action(() => { _mapped = b; });
            }
        }
        #endregion
    }
    /// <summary>
    /// For automatic addition/removal of delegates to/from new/old items.
    /// </summary>
    /// <typeparam name="T">Implements IHardwareEntity</typeparam>
    public class HardwareList<T> : BindingList<T> where T : IHardwareEntity
    {
        public event EventHandler<HardwareEntityRemovalEventArgs> OnItemRemoved;

        public HardwareList() : base()
        { }
        public HardwareList(IList<T> original) : base(original)
        { }

        protected override void RemoveItem(int index)
        {
            HardwareEntityRemovalEventArgs e = new HardwareEntityRemovalEventArgs(this[index]);
            Action a = new Action(() => { OnItemRemoved?.Invoke(this, e); });
            if (UIThreadInvoker != null)
            {
                UIThreadInvoker.Invoke(a);
            }
            else
            {
                a.Invoke();
            }
            if (!e.Cancel) base.RemoveItem(index);
        }

        protected override void OnListChanged(ListChangedEventArgs e)
        {
            Action a = new Action(() => { base.OnListChanged(e); });
            if (UIThreadInvoker != null)
            {
                UIThreadInvoker.Invoke(a);
            }
            else
            {
                a.Invoke();
            }
        }
        protected override void OnAddingNew(AddingNewEventArgs e)
        {
            Action a = new Action(() => { base.OnAddingNew(e); });
            if (UIThreadInvoker != null)
            {
                UIThreadInvoker.Invoke(a);
            }
            else
            {
                a.Invoke();
            }
        }

        public Action<Delegate> UIThreadInvoker { get; set; }
    }
}
