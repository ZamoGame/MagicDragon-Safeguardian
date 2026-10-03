using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("MagicDragon Safeguardian Uninstaller")]
[assembly: AssemblyProduct("MagicDragon Safeguardian")]
[assembly: AssemblyCompany("HardcoreApe")]
[assembly: AssemblyCopyright("Copyright © HardcoreApe 2026")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

namespace MagicDragonSafeguardianUninstall
{
    internal sealed class ClassicUninstallTitleLabel : Control
    {
        internal ClassicUninstallTitleLabel()
        {
            BackColor = Color.Navy;
            ForeColor = Color.White;
            Font = new Font("MS Sans Serif", 15F, FontStyle.Bold, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, BackColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class UninstallForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly string productRoot;
        private readonly string installerScript;

        internal UninstallForm()
        {
            productRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagicDragon_Safeguardian");
            installerScript = Path.Combine(productRoot, "App", "Install.ps1");
            BuildInterface();
        }

        private void BuildInterface()
        {
            Text = "Uninstall MagicDragon Safeguardian";
            ClientSize = new Size(590, 330);
            MinimumSize = MaximumSize = new Size(590, 330);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Control;
            Font = new Font("MS Sans Serif", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Panel frame = new Panel();
            frame.Dock = DockStyle.Fill;
            frame.BorderStyle = BorderStyle.Fixed3D;
            Controls.Add(frame);

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 44;
            titleBar.BackColor = Color.Navy;
            titleBar.MouseDown += DragWindow;
            frame.Controls.Add(titleBar);

            PictureBox icon = new PictureBox();
            icon.Location = new Point(8, 6);
            icon.Size = new Size(32, 32);
            icon.SizeMode = PictureBoxSizeMode.StretchImage;
            icon.Image = Icon.ToBitmap();
            icon.BackColor = Color.Navy;
            icon.MouseDown += DragWindow;
            titleBar.Controls.Add(icon);

            ClassicUninstallTitleLabel title = new ClassicUninstallTitleLabel();
            title.Text = "Uninstall MagicDragon Safeguardian";
            title.Location = new Point(48, 6);
            title.Size = new Size(470, 30);
            title.MouseDown += DragWindow;
            titleBar.Controls.Add(title);

            Button x = ClassicButton("X", 42, 32);
            x.Font = new Font("MS Sans Serif", 11F, FontStyle.Bold);
            x.Location = new Point(536, 6);
            x.Click += delegate { Close(); };
            titleBar.Controls.Add(x);

            Label question = new Label();
            question.Text = "Remove MagicDragon Safeguardian? You will choose whether its data is kept.";
            question.Font = new Font("MS Sans Serif", 10F, FontStyle.Bold);
            question.Location = new Point(28, 72);
            question.Size = new Size(525, 52);
            frame.Controls.Add(question);

            GroupBox preserved = new GroupBox();
            preserved.Text = "Uninstall choices";
            preserved.Location = new Point(28, 130);
            preserved.Size = new Size(525, 105);
            frame.Controls.Add(preserved);
            Label details = new Label();
            details.Text = "NO: remove only the application and keep backups/data.\r\n" +
                "YES: remove the application and all MagicDragon data.\r\n" +
                "CANCEL: make no changes.";
            details.Location = new Point(18, 25);
            details.Size = new Size(485, 72);
            preserved.Controls.Add(details);

            Label signature = new Label();
            signature.Text = "Made by HardcoreApe 2026 ©";
            signature.Location = new Point(320, 245);
            signature.Size = new Size(232, 20);
            signature.TextAlign = ContentAlignment.TopRight;
            signature.ForeColor = SystemColors.GrayText;
            signature.Font = new Font("MS Sans Serif", 8F);
            frame.Controls.Add(signature);

            Button remove = ClassicButton("Choose & Uninstall", 150, 42);
            remove.Location = new Point(238, 270);
            remove.Click += delegate { RunUninstall(); };
            frame.Controls.Add(remove);
            Button cancel = ClassicButton("Cancel", 150, 42);
            cancel.Location = new Point(402, 270);
            cancel.Click += delegate { Close(); };
            frame.Controls.Add(cancel);

            ApplyCompactScale();
        }

        private void ApplyCompactScale()
        {
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            Scale(new SizeF(0.90F, 0.90F));
            ClientSize = new Size(531, 297);
            MinimumSize = MaximumSize = new Size(531, 297);
        }

        private Button ClassicButton(string text, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, height);
            button.Font = new Font("MS Sans Serif", 9F);
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = SystemColors.Control;
            return button;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private void RunUninstall()
        {
            if (!File.Exists(installerScript))
            {
                MessageBox.Show(this, "The installed cleanup script could not be found:\r\n" + installerScript,
                    "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult choice = MessageBox.Show(this,
                "Do you also want to delete all MagicDragon data?\r\n\r\n" +
                "YES — uninstall and delete backups, recovery copies, diagnostics, settings, and logs.\r\n\r\n" +
                "NO — uninstall the application but keep everything under Documents\\MagicDragon_Safeguardian and the local settings/history.\r\n\r\n" +
                "CANCEL — do not uninstall.",
                "Choose what to keep", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice == DialogResult.Cancel) return;
            bool deleteUserData = choice == DialogResult.Yes;
            if (deleteUserData && MessageBox.Show(this,
                "This permanently deletes all MagicDragon backups and related data for this Windows account.\r\n\r\nContinue?",
                "Confirm data deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Hide();
            string temporaryScript = Path.Combine(Path.GetTempPath(),
                "MagicDragon_Uninstall_" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.Copy(installerScript, temporaryScript, true);
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = "powershell.exe";
                start.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + temporaryScript + "\" -Uninstall" +
                    (deleteUserData ? " -DeleteUserData" : " -KeepUserData") +
                    " -UninstallerProcessId " + Process.GetCurrentProcess().Id + " -DeleteCleanupScript";
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                Process cleanup = Process.Start(start);
                if (cleanup == null) throw new InvalidOperationException("Windows could not start the cleanup process.");
                Application.Exit();
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temporaryScript)) File.Delete(temporaryScript); } catch { }
                Show();
                MessageBox.Show(this, ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm());
        }
    }
}
