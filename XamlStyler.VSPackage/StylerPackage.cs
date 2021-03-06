﻿using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using XamlStyler.Core;
using XamlStyler.Core.Options;

namespace XamlStyler.VSPackage
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    // This attribute is used to register the informations needed to show the this package
    // in the Help/About dialog of Visual Studio.
    // This attribute is needed to let the shell know that this package exposes some menus.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof (PackageOptions), "Xaml Styler", "General", 101, 106, true)]
    [ProvideProfile(typeof (PackageOptions), "Xaml Styler", "Xaml Styler Settings", 106, 107, true,
        DescriptionResourceID = 108)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(GuidList.GUID_XAML_STYLER_VS_PACKAGE_PKG_STRING)]
    public sealed class StylerPackage : Package //, IDTExtensibility2
    {
        private DTE _dte;
        private Events _events;
        private CommandEvents _fileSaveAll;
        private CommandEvents _fileSaveSelectedItems;
        private IVsUIShell _uiShell;

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require
        /// any Visual Studio service because at this point the package object is created but
        /// not sited yet inside Visual Studio environment. The place to do all the other
        /// initialization is the Initialize method.
        /// </summary>
        public StylerPackage()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", ToString()));
        }

        #region Methods

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initilaization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", ToString()));
            base.Initialize();

            _dte = GetService(typeof (DTE)) as DTE;

            if (_dte == null)
            {
                throw new NullReferenceException("DTE is null");
            }

            _uiShell = GetService(typeof (IVsUIShell)) as IVsUIShell;

            // Initialize command events listeners
            _events = _dte.Events;

            // File.SaveSelectedItems command
            _fileSaveSelectedItems = _events.CommandEvents["{5EFC7975-14BC-11CF-9B2B-00AA00573819}", 331];
            _fileSaveSelectedItems.BeforeExecute +=
                OnFileSaveSelectedItemsBeforeExecute;

            // File.SaveAll command
            _fileSaveAll = _events.CommandEvents["{5EFC7975-14BC-11CF-9B2B-00AA00573819}", 224];
            _fileSaveAll.BeforeExecute +=
                OnFileSaveAllBeforeExecute;

            //Initialize menu command
            // Add our command handlers for menu (commands must exist in the .vsct file)
            var menuCommandService = GetService(typeof (IMenuCommandService)) as OleMenuCommandService;

            if (null != menuCommandService)
            {
                // Create the command for the menu item.
                var menuCommandId = new CommandID(GuidList.GuidXamlStylerVsPackageCmdSet,
                                                  (int) PkgCmdIdList.CMDID_BEAUTIFY_XAML);
                var menuItem = new MenuCommand(MenuItemCallback, menuCommandId);
                menuCommandService.AddCommand(menuItem);
            }
        }

        private bool IsFormatableDocument(Document document)
        {
            return !document.ReadOnly && document.Language == "XAML";
        }

        private void OnFileSaveSelectedItemsBeforeExecute(string guid, int id, object customIn, object customOut,
                                                          ref bool cancelDefault)
        {
            Document document = _dte.ActiveDocument;

            if (IsFormatableDocument(document))
            {
                var options = GetDialogPage(typeof (PackageOptions)).AutomationObject as IStylerOptions;

                if (options.BeautifyOnSave)
                {
                    Execute(document);
                }
            }
        }

        private void OnFileSaveAllBeforeExecute(string guid, int id, object customIn, object customOut,
                                                ref bool cancelDefault)
        {
            foreach (Document document in _dte.Documents)
            {
                if (IsFormatableDocument(document))
                {
                    var options = GetDialogPage(typeof (PackageOptions)).AutomationObject as IStylerOptions;

                    if (options.BeautifyOnSave)
                    {
                        Execute(document);
                    }
                }
            }
        }

        private void Execute(Document document)
        {
            if (!IsFormatableDocument(document))
            {
                return;
            }

            Properties xamlEditorProps = _dte.Properties["TextEditor", "XAML"];

            var stylerOptions = GetDialogPage(typeof (PackageOptions)).AutomationObject as IStylerOptions;

            stylerOptions.IndentSize = Int32.Parse(xamlEditorProps.Item("IndentSize").Value.ToString());
            stylerOptions.IndentWithTabs = (bool) xamlEditorProps.Item("InsertTabs").Value;

            StylerService styler = StylerService.CreateInstance(stylerOptions);

            var textDocument = (TextDocument) document.Object("TextDocument");

            TextPoint currentPoint = textDocument.Selection.ActivePoint;
            int originalLine = currentPoint.Line;
            int originalOffset = currentPoint.LineCharOffset;

            EditPoint startPoint = textDocument.StartPoint.CreateEditPoint();
            EditPoint endPoint = textDocument.EndPoint.CreateEditPoint();

            string xamlSource = startPoint.GetText(endPoint);
            xamlSource = styler.Format(xamlSource);

            startPoint.ReplaceText(endPoint, xamlSource, 0);

            if (originalLine <= textDocument.EndPoint.Line)
            {
                textDocument.Selection.MoveToLineAndOffset(originalLine, originalOffset);
            }
            else
            {
                textDocument.Selection.GotoLine(textDocument.EndPoint.Line);
            }
        }

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            try
            {
                _uiShell.SetWaitCursor();

                Document document = _dte.ActiveDocument;

                if (IsFormatableDocument(document))
                {
                    Execute(document);
                }
            }
            catch (Exception ex)
            {
                string title = string.Format("Error in {0}:", GetType().Name);
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}\r\n\r\nIf this deems a malfunctioning of styler, please kindly submit an issue at http://xamlstyler.codeplex.com.",
                    ex.Message);

                ShowMessageBox(title, message);
            }
        }

        private void ShowMessageBox(string title, string message)
        {
            Guid clsid = Guid.Empty;
            int result;

            _uiShell.ShowMessageBox(
                0,
                ref clsid,
                title,
                message,
                String.Empty,
                0,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                OLEMSGICON.OLEMSGICON_INFO,
                0, // false
                out result);
        }

        #endregion Methods
    }
}