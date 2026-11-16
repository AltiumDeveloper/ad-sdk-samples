using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using DXP;
using Point = System.Drawing.Point; // DXP also has a Point type

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the Preferences page lives here, not in the hot-reloaded implementation,
    /// because AD keeps the page, its form and its data manager after the call that created them returned.
    ///
    /// The SDK lesson is the protected extension contract of DXP.ServerModule (RegisterOptionsPages,
    /// RegisterOptionsPageCreator, ReceiveNotificationImpl) and DXP.OptionsForm (the *Impl overrides). None of these
    /// members is listed in the API reference; they were read from the Altium.SDK assembly metadata
    /// and the compiler accepts the overrides below. AD calls RegisterOptionsPages and ReceiveNotificationImpl.
    ///
    /// The page also needs an OptionsPage block in Installation\PreferencesOptionsPage.ins: its FormName ("General")
    /// is the name passed to RegisterOptionsPageCreator.
    /// </summary>
    public partial class PluginServerModule
    {
        // The framework's constructor calls RegisterViews(client) once, after the base constructor. This sample has
        // no panel (no Panel.cs), so the partial method is free: it loads the cached preference
        // (PreferenceStore.Load()) and publishes the shim's state to the implementation.
        partial void RegisterViews(IClient client)
        {
            PreferenceStore.ModuleCreated = DateTime.Now;
            PreferenceStore.Load();
            PreferenceStore.Publish();
        }

        // Called by DXP.ServerModule (not documented; once, when the server module is created; counted in
        // PreferenceStore.PagesRegistered). The name must match FormName of the .ins OptionsPage block.
        protected override void RegisterOptionsPages()
        {
            PreferenceStore.PagesRegistered++;
            RegisterOptionsPageCreator(GeneralOptionsForm.PageName, CreateGeneralPage);
        }

        // AD wraps the form in a DXP.OptionsPage (public constructor OptionsPage(OptionsForm)).
        private IOptionsPage CreateGeneralPage()
        {
            PreferenceStore.PagesCreated++;
            return new OptionsPage(new GeneralOptionsForm());
        }

        // DXP.ServerModule.ReceiveNotification itself is not virtual (CS0506, DocumentOpenSaveNotification), but the
        // protected ReceiveNotificationImpl is. Overriding it adds no COM-visible member, unlike a separate
        // notification handler object (which can make AD show "Member not found" dialogs).
        protected override void ReceiveNotificationImpl(INotification argNotification)
        {
            try
            {
                base.ReceiveNotificationImpl(argNotification);
                PreferenceStore.NotificationsReceived++;
                if (argNotification == null)
                    return;

                // A system notification whose code is the one the page returns from
                // GetNotificationCodeImpl. 1023 is the sample's own number, not an SDK constant.
                if (string.Equals(argNotification.GetNotificationType(), NotificationType.SystemNotification, StringComparison.OrdinalIgnoreCase) &&
                    argNotification.GetNotificationCode() == PreferenceStore.NotificationCodePreferencesChanged)
                {
                    PreferenceStore.ChangeNotifications++;
                    PreferenceStore.Save("notification " + PreferenceStore.NotificationCodePreferencesChanged);
                }
            }
            catch (Exception ex)
            {
                // Never let an exception escape into AD's notification dispatch.
                PreferenceStore.RecordError("ReceiveNotificationImpl", ex);
            }
        }
    }

    /// <summary>
    /// The cached preference and the counters the Status command reports. State is
    /// static in the shim, so a hot reload of the implementation keeps it. The implementation reaches it only through
    /// delegates in AppDomain data (BCL types: the implementation cannot reference the shim assembly).
    /// </summary>
    internal static class PreferenceStore
    {
        /// <summary>Options are stored under the server name.</summary>
        public const string ServerName = SampleInfo.Name;
        public const string Section = "General";
        public const string Key = "Temperature";
        public const string DefaultTemperature = "100";

        /// <summary>The page's own notification code, not an SDK constant. The SDK's own SystemNotificationCode values end at 101.</summary>
        public const int NotificationCodePreferencesChanged = 1023;

        // AppDomain data keys (shared by every extension in AD: they carry the sample name). The implementation
        // repeats these strings.
        public const string StatusKey = SampleInfo.Name + ".Status";       // Func<string>
        public const string CachedKey = SampleInfo.Name + ".Cached";       // Func<string>
        public const string ReloadKey = SampleInfo.Name + ".Reload";       // Func<string>: re-read the store
        public const string WriteIniKey = SampleInfo.Name + ".WriteIni";   // Func<string, string>: folder -> file
        public const string ReadIniKey = SampleInfo.Name + ".ReadIni";     // Func<string, string>: file -> value

        public static string Temperature = DefaultTemperature;
        public static DateTime ModuleCreated;
        public static int PagesRegistered, PagesCreated, NotificationsReceived, ChangeNotifications;
        public static int SetStateCalls, GetStateCalls, DefaultCalls, Exports, Imports;
        public static string LastSave = "none", LastError = "none";

        /// <summary>Reads the stored value into the cache.</summary>
        public static void Load()
        {
            try
            {
                IOptionsReader reader = Utils.ServerOptionsReader(ServerName);
                Temperature = reader.ReadString(Section, Key, DefaultTemperature);
            }
            catch (Exception ex)
            {
                RecordError("Load", ex);
            }
        }

        /// <summary>
        /// Writes the cached value and flushes it (IOptionsWriter.Flush). Called on apply as well as on the
        /// notification, so the value is not lost if the notification never arrives.
        /// </summary>
        public static void Save(string reason)
        {
            IOptionsWriter writer = Utils.ServerOptionsWriter(ServerName);
            writer.WriteString(Section, Key, Temperature ?? "");
            writer.Flush();
            LastSave = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " (" + reason + ")";
        }

        public static void RecordError(string where, Exception ex) =>
            LastError = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + where + ": " + ex.GetType().Name + ": " + ex.Message;

        public static void Publish()
        {
            AppDomain.CurrentDomain.SetData(StatusKey, (Func<string>)Status);
            AppDomain.CurrentDomain.SetData(CachedKey, (Func<string>)(() => Temperature));
            AppDomain.CurrentDomain.SetData(ReloadKey, (Func<string>)(() => { Load(); return Temperature; }));
            AppDomain.CurrentDomain.SetData(WriteIniKey, (Func<string, string>)(folder => GeneralDataManager.WriteIni(Path.Combine(folder, GeneralDataManager.FileName))));
            AppDomain.CurrentDomain.SetData(ReadIniKey, (Func<string, string>)GeneralDataManager.ReadIni);
        }

        private static string Status() => string.Join(Environment.NewLine, new List<string>
        {
            "Server module created: " + ModuleCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "RegisterOptionsPages calls: " + PagesRegistered,
            "Pages created (Preferences opened on the page): " + PagesCreated,
            "Page calls: SetStateControls " + SetStateCalls + ", GetStateControls " + GetStateCalls + ", SetDefaultState " + DefaultCalls,
            "Notifications received: " + NotificationsReceived + " (system code " + NotificationCodePreferencesChanged + ": " + ChangeNotifications + ")",
            "Data manager: exports " + Exports + ", imports " + Imports,
            "Last save: " + LastSave,
            "Last error: " + LastError,
        });
    }

    /// <summary>
    /// The page's controls. Built in code, no designer file. AD drives the page through the *Impl methods.
    /// </summary>
    internal sealed class GeneralOptionsForm : OptionsForm
    {
        /// <summary>= RegisterOptionsPageCreator name = FormName in the .ins.</summary>
        public const string PageName = "General";

        private readonly TextBox _textTemperature;
        private string _shown = "";

        public GeneralOptionsForm()
        {
            Name = "OptionsPage_General";
            ClientSize = new Size(300, 300);
            KeyPreview = true;
            Controls.Add(new Label { AutoSize = true, Location = new Point(12, 18), Text = "Temperature:" });
            _textTemperature = new TextBox { Location = new Point(112, 15), Size = new Size(125, 22), Name = "textTemperature" };
            Controls.Add(_textTemperature);
        }

        // AD -> form: show the cached value.
        protected override void SetStateControlsImpl()
        {
            PreferenceStore.SetStateCalls++;
            _textTemperature.Text = PreferenceStore.Temperature;
            ClearModified();
        }

        // Form -> cache, when the user applies the dialog. The value is also saved here (see PreferenceStore.Save).
        protected override void GetStateControlsImpl()
        {
            PreferenceStore.GetStateCalls++;
            PreferenceStore.Temperature = _textTemperature.Text;
            try { PreferenceStore.Save("page apply"); }
            catch (Exception ex) { PreferenceStore.RecordError("GetStateControlsImpl", ex); }
        }

        protected override void ClearModified() => _shown = _textTemperature.Text;

        protected override void SetDefaultStateImpl()
        {
            PreferenceStore.DefaultCalls++;
            _textTemperature.Text = PreferenceStore.DefaultTemperature;
        }

        protected override bool GetModifiedImpl() => _shown != _textTemperature.Text;

        protected override int GetNotificationCodeImpl() => PreferenceStore.NotificationCodePreferencesChanged;

        protected override IOptionsFormDataManager CreateDataManager() => new GeneralDataManager();
    }

    /// <summary>
    /// Preferences export/import: AD calls it from its own Preferences Save/Load UI.
    /// The ini is written and read with managed code, without kernel32's private-profile functions.
    /// </summary>
    internal sealed class GeneralDataManager : OptionsFormDataManager
    {
        public const string FileName = "MyPref_General.ini";
        private const string IniSection = "MyPref_General";

        protected override void SaveSettingsToFilesImpl(IStrings settingsFiles)
        {
            try
            {
                PreferenceStore.Exports++;
                var folder = GlobalVars.Client?.GetClientAPI()?.SpecialFolder_ExportPreferences();
                // Without the client API there is no export folder: skip rather than write to a relative path.
                if (string.IsNullOrEmpty(folder))
                    return;
                var file = WriteIni(Path.Combine(folder, FileName));
                settingsFiles.Add(file);
            }
            catch (Exception ex)
            {
                PreferenceStore.RecordError("SaveSettingsToFilesImpl", ex);
            }
        }

        protected override void LoadSettingsFromFilesImpl(IStrings settingsFiles, string documentsInstallPath)
        {
            try
            {
                PreferenceStore.Imports++;
                for (int i = 0; i < settingsFiles.GetCount(); ++i)
                {
                    string file = settingsFiles.GetItem(i);
                    if (!string.Equals(Path.GetFileName(file), FileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var value = ReadIni(file);
                    if (value != null)
                        PreferenceStore.Temperature = value;
                    break;
                }
            }
            catch (Exception ex)
            {
                PreferenceStore.RecordError("LoadSettingsFromFilesImpl", ex);
            }
        }

        /// <summary>Writes the cached value as "[MyPref_General] Temperature=..." and returns the file.</summary>
        public static string WriteIni(string file)
        {
            File.WriteAllLines(file, new[] { "[" + IniSection + "]", PreferenceStore.Key + "=" + PreferenceStore.Temperature });
            return file;
        }

        /// <summary>The Temperature value of the ini, or null when the file, section or key is missing.</summary>
        public static string ReadIni(string file)
        {
            if (!File.Exists(file))
                return null;
            bool inSection = false;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                    inSection = string.Equals(line.Trim('[', ']'), IniSection, StringComparison.OrdinalIgnoreCase);
                else if (inSection && line.StartsWith(PreferenceStore.Key + "=", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(PreferenceStore.Key.Length + 1);
            }
            return null;
        }
    }
}
