﻿using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Languages.Editor.Controller;
using Microsoft.Languages.Editor.Shell;
using Microsoft.Languages.Editor.Test.Composition;
using Microsoft.Languages.Editor.Undo;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.Languages.Editor.Tests.Shell
{
    [ExcludeFromCodeCoverage]
    public class TestEditorShell : IEditorShell
    {
        private Thread _mainThread;

        private static IEditorShell _instance;
        private static object _lock = new object();

        public static IEditorShell Create()
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    var compositionService = TestCompositionCatalog.CompositionService;
                    var exportProvider = TestCompositionCatalog.ExportProvider;

                    _instance = new TestEditorShell(compositionService, exportProvider);
                }

                return _instance;
            }
        }

        private TestEditorShell(ICompositionService compositionService, ExportProvider exportProvider)
        {
            CompositionService = compositionService;
            ExportProvider = exportProvider;

            _mainThread = Thread.CurrentThread;
        }

        #region IEditorShell

#pragma warning disable 0067
        public event EventHandler<EventArgs> Idle;
        public event EventHandler<EventArgs> Terminating;
#pragma warning restore 0067

        public ICompositionService CompositionService { get; private set; }
        public ExportProvider ExportProvider { get; private set; }

        public ICommandTarget TranslateCommandTarget(ITextView textView, object commandTarget)
        {
            return commandTarget as ICommandTarget;
        }

        public object TranslateToHostCommandTarget(ITextView textView, object commandTarget)
        {
            return commandTarget;
        }

        public void DispatchOnUIThread(Action action, DispatcherPriority p)
        {
            Dispatcher disp = Dispatcher.FromThread(_mainThread);
            if (disp != null)
            {
                if (!disp.HasShutdownStarted)
                {
                    Dispatcher.FromThread(_mainThread).BeginInvoke(action, p);
                }
            }
            else
            {
                action();
            }
        }

        public ICompoundUndoAction CreateCompoundAction(ITextView textView, ITextBuffer textBuffer)
        {
            return new CompoundUndoAction(textView, textBuffer, false);
        }

        public int LocaleId
        {
            get { return 1033; }
        }

        public string UserFolder
        {
            get { return "."; }
        }

        public IServiceProvider ServiceProvider
        {
            get { return null; }
        }

        public Thread MainThread
        {
            get { return Thread.CurrentThread; }

        }

        public bool ShowHelp(string topic)
        {
            return true;
        }
        public void ShowErrorMessage(string msg)
        {
        }
        #endregion
    }
}