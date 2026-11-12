using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inventor;
using StepExport.UI;

namespace StepExport
{
    /// <summary>
    /// Inventor add-in entry point: one "STEP" button on the 3D Model and Sheet Metal tabs of the part ribbon, the Place Views tab of the drawing ribbon and
    /// the Assemble tab of the assembly ribbon. It opens a panel docked in Inventor's left-hand area to tick parts and
    /// assemblies in a model tree and export them to STEP, IFC and the other 3D formats Inventor writes (and the drawing to PDF, DWG, DXF), in the folder of the open document.
    /// </summary>
    [ComVisible(true)]
    [Guid(ClientId)]
    [ProgId("StepExport.StandardAddInServer")]
    public class StandardAddInServer : ApplicationAddInServer
    {
        public const string ClientId = "9ade277e-e5bf-4a79-8aa0-aae1d658ec18";
        private const string PanelName = "StepExport.Panel";

        private Inventor.Application _app;
        private ButtonDefinition _button;
        private UserInterfaceEvents _uiEvents;

        public object Automation => null;

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            _app = addInSiteObject.Application;
            string clientGuid = "{" + ClientId + "}";
            UI.Theme.Detect(_app);   // picks the light or dark icon set before anything is drawn

            _button = _app.CommandManager.ControlDefinitions.AddButtonDefinition(
                "STEP", "StepExport.Start", CommandTypesEnum.kFileOperationsCmdType, clientGuid,
                "Export assemblies and parts to STEP",
                "STEP Export\nTick assemblies and parts in a model tree and export them to STEP, IFC, glTF, OBJ, STL, DWG and more in the folder of the open document, named with the order number and the date. In a drawing the assembly of sheet 1 is ticked.",
                Icons.CreateStep(16), Icons.CreateStep(32), ButtonDisplayEnum.kDisplayTextInLearningMode);
            _button.OnExecute += OnExecute;

            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += OnResetRibbon;

            DockHost.Init(_app, clientGuid);
            AddToRibbon();
        }

        public void Deactivate()
        {
            try { _uiEvents.OnResetRibbonInterface -= OnResetRibbon; } catch { }
            try { _button.OnExecute -= OnExecute; } catch { }
            try { ExportCommand.CloseActive(); } catch { }
            try { DockHost.Shutdown(); } catch { }
            try { _button?.Delete(); } catch { }
            _button = null;
            _uiEvents = null;
            _app = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID) { }

        // ----- ribbon -------------------------------------------------------------------------------------------

        private void OnResetRibbon(NameValueMap context) => AddToRibbon();

        private void AddToRibbon()
        {
            AddTo("Part", "id_TabModel");
            AddTo("Part", "id_TabSheetMetal");
            AddTo("Assembly", "id_TabAssemble");
            AddTo("Drawing", "id_TabPlaceViews");
        }

        private void AddTo(string ribbonName, string tabName)
        {
            try
            {
                RibbonTab tab = _app.UserInterfaceManager.Ribbons[ribbonName].RibbonTabs[tabName];
                RibbonPanel panel;
                try { panel = tab.RibbonPanels[PanelName]; }
                catch { panel = tab.RibbonPanels.Add("STEP", PanelName, "{" + ClientId + "}", "", false); }
                try { panel.CommandControls.AddButton(_button, true, true, "", false); } catch { /* already added */ }
            }
            catch (Exception ex) { Core.Log.Write("Adding the button to " + ribbonName + "/" + tabName + " failed: " + ex.Message); }
        }

        // ----- command ------------------------------------------------------------------------------------------

        private void OnExecute(NameValueMap context)
        {
            string error = ExportCommand.Start(_app);
            if (error != null)
                MessageBox.Show(new InventorWindow(_app.MainFrameHWND), error, "STEP Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
