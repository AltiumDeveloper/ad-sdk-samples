using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;

namespace AdSamples.PortalSessionSignIn
{
    /// <summary>
    /// Signs Altium Designer in to the Altium portal with a session ID obtained outside AD (for example from a
    /// browser session), instead of the normal sign-in flow: IDXPAltiumPortal.LoginBySessionID. The portal object
    /// comes from the client, through the GetDXPAltiumPortal() extension method.
    /// A session ID is a credential: it is never returned, logged or written anywhere by this sample.
    /// </summary>
    internal static class PortalSessionSignInCommands
    {
        [HotCommand("Login", "Sign in to the Altium portal with a session ID")]
        public static string Login(CommandContext ctx)
        {
            IDXPAltiumPortal portal = Portal();

            // Nothing to do while signed in. The shared shim has no per-command GetState that could disable the
            // command, so the check is made here.
            if (portal.LoggedIn())
                return "Already signed in to the Altium portal; nothing done.\n" + DescribeState(portal);

            // Parameter first; from the menu (no parameter) ask for it in a small dialog.
            // Never on the pipe: a modal dialog there would block AD for everyone.
            string sessionId = (ctx.Get("SessionId") ?? string.Empty).Trim();
            if (sessionId.Length == 0 && ctx.IsInteractive)
            {
                string entered = SessionIdDialog.Ask();
                if (entered == null)
                    return "Cancelled; not signed in.";
                sessionId = entered.Trim();
            }
            if (sessionId.Length == 0)
                throw new UserException("No session ID given. Pass it as a parameter: SessionId=<session ID>.");

            ctx.Log("Signing in with the session ID (the ID itself is not logged)...");
            try
            {
                // argWaitForResult = true. Whether this blocks until the sign-in has finished,
                // and what happens with an invalid ID, is not documented (see README Notes).
                portal.LoginBySessionID(true, sessionId);
            }
            catch (COMException ex)
            {
                throw new UserException($"LoginBySessionID failed: {ex.Message} (0x{ex.HResult:X8}).\n" + DescribeState(portal));
            }

            return "LoginBySessionID returned.\n" + DescribeState(portal);
        }

        [HotCommand("LoginState", "Show the Altium portal sign-in state")]
        public static string LoginState(CommandContext ctx)
        {
            return DescribeState(Portal());
        }

        private static IDXPAltiumPortal Portal()
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            return client.GetDXPAltiumPortal()
                ?? throw new UserException("The Altium portal interface is not available in this Altium Designer session.");
        }

        // The state readable without exposing a credential: PortalSessionHandle() and Password() are deliberately
        // not read. LoggedIn, LoggingIn and IsBusy are methods in the .NET API.
        private static string DescribeState(IDXPAltiumPortal portal)
        {
            bool loggedIn = portal.LoggedIn();
            string user = loggedIn ? $", user name '{portal.UserName()}', full name '{portal.FullName()}'" : string.Empty;
            return $"LoggedIn={loggedIn}, LoggingIn={portal.LoggingIn()}, IsBusy={portal.IsBusy()}{user}";
        }

        /// <summary>A dialog built in code: one text box, OK and Cancel.</summary>
        private sealed class SessionIdDialog : Form
        {
            private readonly TextBox _sessionId = new TextBox
            {
                Left = 100, Top = 15, Width = 420,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                // Masked: the ID is a credential.
                UseSystemPasswordChar = true,
            };

            private SessionIdDialog()
            {
                Text = "Sign in with a session ID";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new System.Drawing.Size(535, 90);

                var label = new Label { Text = "Session ID:", Left = 12, Top = 18, AutoSize = true };
                var ok = new Button { Text = "OK", Left = 355, Top = 52, Width = 80 };
                var cancel = new Button { Text = "Cancel", Left = 440, Top = 52, Width = 80, DialogResult = DialogResult.Cancel };

                // An empty (after trimming) ID keeps the dialog open.
                ok.Click += (_, _) =>
                {
                    if (_sessionId.Text.Trim().Length == 0)
                    {
                        MessageBox.Show(this, "Enter a session ID.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _sessionId.Focus();
                        return;
                    }
                    DialogResult = DialogResult.OK;
                };

                Controls.AddRange(new Control[] { label, _sessionId, ok, cancel });
                AcceptButton = ok;
                CancelButton = cancel;
            }

            /// <summary>The entered ID, or null when the user cancelled.</summary>
            public static string Ask()
            {
                using var dialog = new SessionIdDialog();
                return dialog.ShowDialog() == DialogResult.OK ? dialog._sessionId.Text : null;
            }
        }
    }
}
