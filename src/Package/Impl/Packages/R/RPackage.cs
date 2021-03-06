﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using Microsoft.Common.Core;
using Microsoft.Common.Core.IO;
using Microsoft.Languages.Editor.Tasks;
using Microsoft.R.Components.ContentTypes;
using Microsoft.R.Components.Settings;
using Microsoft.R.Components.Settings.Mirrors;
using Microsoft.R.Debugger;
using Microsoft.R.Debugger.PortSupplier;
using Microsoft.R.Support.Help;
using Microsoft.VisualStudio.InteractiveWindow.Shell;
using Microsoft.VisualStudio.ProjectSystem.FileSystemMirroring.Package.Registration;
using Microsoft.VisualStudio.ProjectSystem.FileSystemMirroring.Shell;
using Microsoft.VisualStudio.R.Package;
using Microsoft.VisualStudio.R.Package.DataInspect;
using Microsoft.VisualStudio.R.Package.DataInspect.Office;
using Microsoft.VisualStudio.R.Package.Definitions;
using Microsoft.VisualStudio.R.Package.Expansions;
using Microsoft.VisualStudio.R.Package.Help;
using Microsoft.VisualStudio.R.Package.History;
using Microsoft.VisualStudio.R.Package.Logging;
using Microsoft.VisualStudio.R.Package.Options.R;
using Microsoft.VisualStudio.R.Package.Options.R.Editor;
using Microsoft.VisualStudio.R.Package.Packages;
using Microsoft.VisualStudio.R.Package.ProjectSystem;
using Microsoft.VisualStudio.R.Package.ProjectSystem.PropertyPages;
using Microsoft.VisualStudio.R.Package.ProjectSystem.PropertyPages.Settings;
using Microsoft.VisualStudio.R.Package.RClient;
using Microsoft.VisualStudio.R.Package.Repl;
using Microsoft.VisualStudio.R.Package.Shell;
using Microsoft.VisualStudio.R.Package.Telemetry;
using Microsoft.VisualStudio.R.Package.ToolWindows;
using Microsoft.VisualStudio.R.Package.Utilities;
using Microsoft.VisualStudio.R.Package.Wpf;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.R.Packages.R {
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#7002", "#7003", RtvsProductInfo.VersionString, IconResourceID = 400)]
    [Guid(RGuidList.RPackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideEditorExtension(typeof(REditorFactory), RContentTypeDefinition.FileExtension, 0x32, NameResourceID = 106)]
    [ProvideLanguageExtension(RGuidList.RLanguageServiceGuidString, RContentTypeDefinition.FileExtension)]
    [ProvideEditorFactory(typeof(REditorFactory), 200, CommonPhysicalViewAttributes = 0x2, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
    [ProvideEditorLogicalView(typeof(REditorFactory), VSConstants.LOGVIEWID.TextView_string)]
    [ProvideLanguageService(typeof(RLanguageService), RContentTypeDefinition.LanguageName, 106, ShowSmartIndent = true,
        ShowMatchingBrace = true, MatchBraces = true, MatchBracesAtCaret = true, ShowCompletion = true, EnableLineNumbers = true,
        EnableFormatSelection = true, DefaultToInsertSpaces = true, RequestStockColors = true)]
    [ShowBraceCompletion(RContentTypeDefinition.LanguageName)]
    [LanguageEditorOptions(RContentTypeDefinition.LanguageName, 2, true, true)]
    [ProvideLanguageEditorOptionPage(typeof(REditorOptionsDialog), RContentTypeDefinition.LanguageName, "", "Advanced", "#20136")]
    [ProvideKeyBindingTable(RGuidList.REditorFactoryGuidString, 200)]
    [ProvideProjectFileGenerator(typeof(RProjectFileGenerator), RGuidList.CpsProjectFactoryGuidString, FileExtensions = RContentTypeDefinition.RStudioProjectExtensionNoDot, DisplayGeneratorFilter = 300)]
    [DeveloperActivity(RContentTypeDefinition.LanguageName, RGuidList.RPackageGuidString, sortPriority: 40)]
    [ProvideCpsProjectFactory(RGuidList.CpsProjectFactoryGuidString, RContentTypeDefinition.LanguageName)]
    [ProvideOptionPage(typeof(RToolsOptionsPage), "R Tools", "Advanced", 20116, 20136, true)]
    //[ProvideOptionPage(typeof(PackageSourceOptionsPage), "R Tools", "Package Sources", 20116, 20135, true)]
    [ProvideObject(typeof(RunPropertyPage))]
    [ProvideObject(typeof(SettingsPropertyPage))]
    [ProvideInteractiveWindow(RGuidList.ReplInteractiveWindowProviderGuidString, Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Bottom, Window = ToolWindowGuids80.Outputwindow, DocumentLikeTool = true)]
    [ProvideToolWindow(typeof(PlotDeviceWindowPane), MultiInstances = true, Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer, Transient = true)]
    [ProvideToolWindow(typeof(PlotHistoryWindowPane), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.Toolbox)]
    [ProvideToolWindow(typeof(HelpWindowPane), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.PropertiesWindow)]
    [ProvideToolWindow(typeof(HistoryWindowPane), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer)]
    [ProvideToolWindow(typeof(ConnectionManagerWindowPane), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer)]
    [ProvideToolWindow(typeof(PackageManagerWindowPane), Style = VsDockStyle.MDI)]
    [ProvideDebugEngine(RContentTypeDefinition.LanguageName, DebuggerGuids.DebugEngineString, typeof(AD7Engine), SupportsAttach = true)]
    [ProvideToolWindow(typeof(VariableWindowPane), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer)]
    [ProvideToolWindow(typeof(VariableGridWindowPane), MultiInstances = true, Style = VsDockStyle.MDI, Transient = true)]
    [ProvideDebugLanguage(RContentTypeDefinition.LanguageName, DebuggerGuids.LanguageGuidString, "{D67D5DB8-3D44-4105-B4B8-47AB1BA66180}", DebuggerGuids.DebugEngineString, DebuggerGuids.CustomViewerString)]
    [ProvideDebugPortSupplier("R Interactive sessions", typeof(RDebugPortSupplier), DebuggerGuids.PortSupplierString, typeof(RDebugPortPicker))]
    [ProvideComClass(typeof(RDebugPortPicker))]
    [ProvideComClass(typeof(AD7CustomViewer))]
    [ProvideNewFileTemplates(RGuidList.MiscFilesProjectGuidString, RGuidList.RPackageGuidString, "#106", @"Templates\NewItem\")]
    [ProvideCodeExpansions(RGuidList.RLanguageServiceGuidString, false, 0,
                           RContentTypeDefinition.LanguageName, @"Snippets\SnippetsIndex.xml")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "analysis", @"Snippets\analysis")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "datasets", @"Snippets\datasets")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "distributions", @"Snippets\distributions")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "flow", @"Snippets\flow")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "graphics", @"Snippets\graphics")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "operators", @"Snippets\operators")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "rodbc", @"Snippets\rodbc")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-analysis", @"Snippets\mrs-analysis")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-chunking", @"Snippets\mrs-chunking")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-computeContext", @"Snippets\mrs-computeContext")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-data", @"Snippets\mrs-data")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-distributed", @"Snippets\mrs-distributed")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-graphics", @"Snippets\mrs-graphics")]
    [ProvideCodeExpansionPath(RContentTypeDefinition.LanguageName, "mrs-transforms", @"Snippets\mrs-transforms")]
    internal class RPackage : BasePackage<RLanguageService>, IRPackage {
        public const string OptionsDialogName = "R Tools";
        private IPackageIndex _packageIndex;

        public static IRPackage Current { get; private set; }

        protected override void Initialize() {
            Current = this;

            VsAppShell.EnsureInitialized();
            if (IsCommandLineMode()) {
                return;
            }

            VsWpfOverrides.Apply();
            CranMirrorList.Download();

            using (var p = Current.GetDialogPage(typeof(RToolsOptionsPage)) as RToolsOptionsPage) {
                p?.LoadSettings();
            }

            RtvsTelemetry.Initialize(_packageIndex, VsAppShell.Current.ExportProvider.GetExportedValue<IRSettings>());
            base.Initialize();

            ProjectIconProvider.LoadProjectImages();
            LogCleanup.DeleteLogsAsync(DiagnosticLogs.DaysToRetain);

            BuildFunctionIndex();
            AdviseExportedWindowFrameEvents<ActiveWpfTextViewTracker>();
            AdviseExportedWindowFrameEvents<VsActiveRInteractiveWindowTracker>();
            AdviseExportedDebuggerEvents<VsDebuggerModeTracker>();

            IdleTimeAction.Create(ExpansionsCache.Load, 200, typeof(ExpansionsCache), VsAppShell.Current);
            IdleTimeAction.Create(() => RtvsTelemetry.Current.ReportConfiguration(), 5000, typeof(RtvsTelemetry), VsAppShell.Current);
        }

        protected override void Dispose(bool disposing) {
            SavePackageIndex();

            LogCleanup.Cancel();
            ProjectIconProvider.Close();
            CsvAppFileIO.Close(new FileSystem());

            RtvsTelemetry.Current.Dispose();

            using (var p = Current.GetDialogPage(typeof(RToolsOptionsPage)) as RToolsOptionsPage) {
                p?.SaveSettings();
            }

            base.Dispose(disposing);
        }

        protected override IEnumerable<IVsEditorFactory> CreateEditorFactories() {
            return new List<IVsEditorFactory>() {
                new REditorFactory(this)
            };
        }

        protected override IEnumerable<IVsProjectGenerator> CreateProjectFileGenerators() {
            yield return new RProjectFileGenerator();
        }

        protected override IEnumerable<MenuCommand> CreateMenuCommands() {
            return PackageCommands.GetCommands(VsAppShell.Current.ExportProvider);
        }

        protected override object GetAutomationObject(string name) {
            if (name == OptionsDialogName) {
                DialogPage page = GetDialogPage(typeof(REditorOptionsDialog));
                return page.AutomationObject;
            }

            return base.GetAutomationObject(name);
        }

        public T FindWindowPane<T>(Type t, int id, bool create) where T : ToolWindowPane {
            return FindWindowPane(t, id, create) as T;
        }

        protected override int CreateToolWindow(ref Guid toolWindowType, int id) {
            var toolWindowFactory = VsAppShell.Current.ExportProvider.GetExportedValue<RPackageToolWindowProvider>();
            return toolWindowFactory.TryCreateToolWindow(toolWindowType, id) ? VSConstants.S_OK : base.CreateToolWindow(ref toolWindowType, id);
        }

        protected override WindowPane CreateToolWindow(Type toolWindowType, int id) {
            var toolWindowFactory = VsAppShell.Current.ExportProvider.GetExportedValue<RPackageToolWindowProvider>();
            return toolWindowFactory.CreateToolWindow(toolWindowType, id) ?? base.CreateToolWindow(toolWindowType, id);
        }

        private bool IsCommandLineMode() {
            var shell = VsAppShell.Current.GetGlobalService<IVsShell>(typeof(SVsShell));
            if (shell != null) {
                object value;
                shell.GetProperty((int)__VSSPROPID.VSSPROPID_IsInCommandLineMode, out value);
                return value is bool && (bool)value;
            }
            return false;
        }

        private void BuildFunctionIndex() {
            _packageIndex = VsAppShell.Current.ExportProvider.GetExportedValue<IPackageIndex>();
            _packageIndex.BuildIndexAsync().DoNotWait();
        }

        private void SavePackageIndex() {
            _packageIndex?.WriteToDisk();
            _packageIndex?.Dispose();
        }
    }
}
