using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). The notification object is handed to Altium Designer, which may keep it and
    /// call it after the command returned (asynchronous broadcast), so its class lives here, in the stable shim, not
    /// in the hot-reloadable implementation. The shim publishes two delegates in AppDomain data (BCL types only, so
    /// they cross the hot reload boundary): one that broadcasts, one that reads the last notification back.
    /// </summary>
    public partial class PluginServerModule
    {
        /// <summary>Func&lt;string type, bool synchronous, IDictionary&lt;string, object&gt;&gt;: creates and broadcasts a notification.</summary>
        public const string BroadcastKey = SampleInfo.Name + ".Broadcast";

        /// <summary>Func&lt;bool reset, IDictionary&lt;string, object&gt;&gt;: the last notification's snapshot (null: none yet).</summary>
        public const string LastKey = SampleInfo.Name + ".LastNotification";

        private static ValidateStateTransitionNotification _last;
        private static int _sequence;

        // Template hook OnCreated: called once, when Altium Designer creates the server module.
        // Publishes the broadcast functions the implementation calls.
        partial void OnCreated(IClient client)
        {
            AppDomain.CurrentDomain.SetData(BroadcastKey, (Func<string, bool, IDictionary<string, object>>)Broadcast);
            AppDomain.CurrentDomain.SetData(LastKey, (Func<bool, IDictionary<string, object>>)Last);
        }

        private static IDictionary<string, object> Broadcast(string type, bool synchronous)
        {
            var notification = new ValidateStateTransitionNotification(type ?? string.Empty, ++_sequence, synchronous);
            _last = notification;

            // IClient has an instance method BroadcastNotification(object) and an extension method taking
            // INotification. C# prefers the instance method. The client is re-queried, not kept.
            if (synchronous)
                GlobalVars.Client.BroadcastNotification(notification);
            else
                GlobalVars.Client.BroadcastNotificationAsynchronously(notification);

            // Synchronous: every subscriber has had its turn. Asynchronous: queued only, nobody has seen it yet.
            return notification.Snapshot();
        }

        private static IDictionary<string, object> Last(bool reset)
        {
            var snapshot = _last?.Snapshot();
            if (reset)
            {
                _last = null;
                _sequence = 0;
            }
            return snapshot;
        }
    }

    /// <summary>
    /// ValidateStateTransitionNotification: a BaseNotification that also implements
    /// IValidateStateTransitionNotification, the notification a vault state transition is validated with.
    ///
    /// Declaration: IValidateStateTransitionNotification is a dispatch-only interface (InterfaceIsIDispatch, IID of
    /// IDispatch itself) whose members carry the fixed DispIds 4710-4716 in Altium.SDK.Interfaces
    /// (read from the metadata, also as DispConsts.DXPIValidateStateTransitionNotificationSDK*DispId). DXP.BaseNotification
    /// is an AutoDispatch class whose INotification and SupportsService members repeat their interface DispIds
    /// (723, 734, 1851, 1023). This class follows the same form: AutoDispatch, and each interface member repeats
    /// its DispId, so a caller that invokes the interface through IDispatch finds it. A notification handler
    /// declared without DispIds can make AD report "Member not found".
    ///
    /// Every member records that it was called, so the sample can show whether anybody (AD or another extension)
    /// looked at the notification.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class ValidateStateTransitionNotification : BaseNotification, IValidateStateTransitionNotification
    {
        private readonly ConcurrentDictionary<string, int> _calls = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, int> _servicesAsked = new ConcurrentDictionary<string, int>();
        private readonly int _sequence;
        private readonly bool _synchronous;
        private readonly DateTime _created = DateTime.Now;

        // The getters default to true and "", and the setters
        // store what a subscriber sets, which is how a validator answers (Valid=false plus a Note).
        private volatile bool _valid = true;
        private volatile string _note = string.Empty;

        public ValidateStateTransitionNotification(string type, int sequence, bool synchronous) : base(type, null)
        {
            _sequence = sequence;
            _synchronous = synchronous;
        }

        [DispId(4710)]
        public object Internal_GetRevision() => Called<object>(nameof(Internal_GetRevision), null);

        [DispId(4711)]
        public object Internal_GetStateTransition() => Called<object>(nameof(Internal_GetStateTransition), null);

        [DispId(4712)]
        public object Internal_GetVault() => Called<object>(nameof(Internal_GetVault), null);

        [DispId(4713)]
        public bool GetValid() => Called(nameof(GetValid), _valid);

        [DispId(4714)]
        public void SetValid(bool argValue)
        {
            Called(nameof(SetValid), 0);
            _valid = argValue;
        }

        [DispId(4715)]
        public string GetNote() => Called(nameof(GetNote), _note);

        [DispId(4716)]
        public void SetNote(string argValue)
        {
            Called(nameof(SetNote), 0);
            _note = argValue ?? string.Empty;
        }

        // BaseNotification.GetNotificationType (DispId 723) and its siblings are sealed. BaseNotification also declares
        // these protected virtual *Impl methods (in the assembly metadata, not in the API reference). That the sealed
        // getters delegate to them: a broadcast makes AD call these overrides, so the counters go up.
        protected override string GetNotificationTypeImpl() => Called("GetNotificationType", base.GetNotificationTypeImpl());

        protected override int GetNotificationCodeImpl() => Called("GetNotificationCode", base.GetNotificationCodeImpl());

        protected override object GetNotificationDataImpl() => Called("GetNotificationData", base.GetNotificationDataImpl());

        /// <summary>
        /// Advertises the service a subscriber checks before it treats the object as an
        /// IValidateStateTransitionNotification. SupportsServiceImpl is protected virtual in
        /// DXP.SupportService, and DXP.ServiceNames is in Altium.SDK.Interfaces; neither is in the API reference.
        /// That the sealed SupportsService (DispId 1023) calls it is assumed.
        /// </summary>
        protected override bool SupportsServiceImpl(string argServiceName)
        {
            _servicesAsked.AddOrUpdate(argServiceName ?? string.Empty, 1, (_, n) => n + 1);
            return base.SupportsServiceImpl(argServiceName) ||
                   argServiceName == ServiceNames.ValidateStateTransitionNotificationServiceName;
        }

        /// <summary>The state for NotificationStatus, as BCL types. Reads fields directly, so it is not counted as a call.</summary>
        internal IDictionary<string, object> Snapshot() => new Dictionary<string, object>
        {
            ["Sequence"] = _sequence,
            ["Type"] = base.GetNotificationTypeImpl() ?? string.Empty,
            ["Mode"] = _synchronous ? "synchronous" : "asynchronous",
            ["Created"] = _created,
            ["Valid"] = _valid,
            ["Note"] = _note,
            ["Calls"] = _calls.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value),
            ["ServicesAsked"] = _servicesAsked.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value),
            ["ServiceName"] = ServiceNames.ValidateStateTransitionNotificationServiceName,
        };

        private T Called<T>(string member, T result)
        {
            _calls.AddOrUpdate(member, 1, (_, n) => n + 1);
            return result;
        }
    }
}
