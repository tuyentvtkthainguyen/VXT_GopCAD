using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(VXT.GopCAD.Commands))]
[assembly: ExtensionApplication(typeof(VXT.GopCAD.PluginApp))]

namespace VXT.GopCAD
{
    public class Commands
    {
        /// <summary>Mở hộp thoại gộp nhiều file DWG thành 1 file.</summary>
        [CommandMethod("VXT", "VXTGOP", CommandFlags.Session)]
        public void Gop()
        {
            string path = null;
            using (var f = new GopForm())
            {
                if (AcApp.ShowModalDialog(f) == System.Windows.Forms.DialogResult.OK && f.OpenAfter)
                    path = f.ResultPath;
            }
            if (path == null || !File.Exists(path)) return;
            try
            {
                AcApp.DocumentManager.Open(path, false);
            }
            catch (System.Exception ex)
            {
                AcApp.ShowAlertDialog("Không mở được file gộp:\n" + ex.Message);
            }
        }
    }

    /// <summary>Nạp khi AutoCAD khởi động: thêm nút vào Ribbon tab "VXT".</summary>
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            AcApp.Idle += OnIdle;
        }

        public void Terminate() { }

        private void OnIdle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            AcApp.Idle -= OnIdle;
            try { RibbonVXT.Add(); } catch { }
            // Đổi workspace thì Ribbon dựng lại -> thêm lại nút
            AcApp.SystemVariableChanged += (s, a) =>
            {
                if (a.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase))
                    try { RibbonVXT.Add(); } catch { }
            };
        }
    }

    internal static class RibbonVXT
    {
        private const string TabId = "VXT_TAB";
        private const string PanelId = "VXT_GOPCAD_PANEL";

        public static void Add()
        {
            var rc = ComponentManager.Ribbon;
            if (rc == null) return;

            var tab = rc.Tabs.FirstOrDefault(t => t.Id == TabId);
            if (tab == null)
            {
                tab = new RibbonTab { Title = "VXT", Id = TabId };
                rc.Tabs.Add(tab);
            }
            if (tab.Panels.Any(p => p.Source != null && p.Source.Id == PanelId)) return;

            var src = new RibbonPanelSource { Title = "Gộp CAD", Id = PanelId };
            src.Items.Add(new RibbonButton
            {
                Id = "VXT_BTN_GOP",
                Text = "Gộp\nfile DWG",
                ShowText = true,
                ShowImage = false,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "Gộp nhiều file DWG thành 1 file (lệnh VXTGOP)",
                CommandParameter = "\u0003\u0003VXTGOP ",
                CommandHandler = new RibbonCmd()
            });
            tab.Panels.Add(new RibbonPanel { Source = src });
        }
    }

    internal class RibbonCmd : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (parameter is RibbonButton b && b.CommandParameter is string cmd && doc != null)
                doc.SendStringToExecute(cmd, true, false, true);
        }
    }
}
