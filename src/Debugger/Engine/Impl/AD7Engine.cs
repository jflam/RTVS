using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Common.Core;
using Microsoft.R.Debugger.Engine.PortSupplier;
using Microsoft.R.Host.Client;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using static System.FormattableString;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.R.Debugger.Engine {
    [ComVisible(true)]
    [Guid(DebuggerGuids.DebugEngineCLSIDString)]
    public sealed class AD7Engine : IDebugEngine2, IDebugEngineLaunch2, IDebugProgram3, IDebugSymbolSettings100 {
        private IDebugEngine2 IDebugEngine2 => this;
        private IDebugProgram2 IDebugProgram2 => this;

        private IDebugEventCallback2 _events;
        private RDebugPortSupplier.DebugProgram _program;
        private Guid _programId;
        private bool _firstContinue = true;
        private bool? _sentContinue = null;
        private volatile DebugBrowseEventArgs _currentBrowseEventArgs;
        private readonly object _browseLock = new object();

        internal bool IsDisposed { get; private set; }
        internal bool IsConnected { get; private set; }
        internal DebugSession DebugSession { get; private set; }
        internal AD7Thread MainThread { get; private set; }

        [Import]
        private IRSessionProvider RSessionProvider { get; set; }

        [Import]
        private IDebugSessionProvider DebugSessionProvider { get; set; }

        public AD7Engine() {
            var compModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            if (compModel == null) {
                throw new InvalidOperationException(Invariant($"{typeof(AD7Engine).FullName} requires {nameof(IComponentModel)} global service"));
            }

            compModel.DefaultCompositionService.SatisfyImportsOnce(this);
        }

        public void Dispose() {
            if (IsDisposed) {
                return;
            }

            var rSession = DebugSession.RSession;
            DebugSession.Browse -= Session_Browse;
            DebugSession.RSession.AfterRequest -= RSession_AfterRequest;
            DebugSession.RSession.Disconnected -= RSession_Disconnected;

            _events = null;
            _program = null;

            MainThread.Dispose();
            MainThread = null;

            DebugSession = null;
            RSessionProvider = null;
            DebugSessionProvider = null;

            IsDisposed = true;

            ExitBrowserAsync(rSession).SilenceException<MessageTransportException>().DoNotWait();
        }

        private void ThrowIfDisposed() {
            if (IsDisposed) {
                throw new ObjectDisposedException(nameof(AD7Engine));
            }
        }

        private async Task ExitBrowserAsync(IRSession session) {
            using (var inter = await session.BeginInteractionAsync(isVisible: true)) {
                // Check if this is still the same prompt as the last Browse prompt that we've seen.
                // If it isn't, then session has moved on already, and there's nothing for us to exit.
                DebugBrowseEventArgs currentBrowseDebugEventArgs;
                lock (_browseLock) {
                    currentBrowseDebugEventArgs = _currentBrowseEventArgs;
                }

                if (currentBrowseDebugEventArgs != null && currentBrowseDebugEventArgs.Contexts == inter.Contexts) {
                    await inter.RespondAsync("Q\n");
                }
            }
        }

        internal void Send(IDebugEvent2 eventObject, string iidEvent, IDebugProgram2 program, IDebugThread2 thread) {
            var events = _events;
            if (events == null) {
                return;
            }

            uint attributes;
            var riidEvent = new Guid(iidEvent);
            Marshal.ThrowExceptionForHR(eventObject.GetAttributes(out attributes));

            if ((attributes & (uint)enum_EVENTATTRIBUTES.EVENT_STOPPING) != 0 && thread == null) {
                throw new InvalidOperationException("A thread must be provided for a stopping event");
            }

            try {
                Marshal.ThrowExceptionForHR(events.Event(this, null, program, thread, eventObject, ref riidEvent, attributes));
            } catch (InvalidCastException) {
                // COM object has gone away.
            }
        }

        internal void Send(IDebugEvent2 eventObject, string iidEvent) {
            Send(eventObject, iidEvent, this, MainThread);
        }

        int IDebugEngine2.Attach(IDebugProgram2[] rgpPrograms, IDebugProgramNode2[] rgpProgramNodes, uint celtPrograms, IDebugEventCallback2 pCallback, enum_ATTACH_REASON dwReason) {
            ThrowIfDisposed();

            if (rgpPrograms.Length != 1) {
                throw new ArgumentException("Zero or more than one programs", "rgpPrograms");
            }

            _program = rgpPrograms[0] as RDebugPortSupplier.DebugProgram;
            if (_program == null) {
                throw new ArgumentException("rgpPrograms[0] must be an " + nameof(RDebugPortSupplier.DebugProgram), "rgpPrograms");
            }

            Marshal.ThrowExceptionForHR(_program.GetProgramId(out _programId));

            _events = pCallback;
            DebugSession = DebugSessionProvider.GetDebugSessionAsync(_program.Session).GetResultOnUIThread();
            MainThread = new AD7Thread(this);
            IsConnected = true;

            // Enable breakpoint instrumentation.
            DebugSession.EnableBreakpoints(true).GetResultOnUIThread();

            // Send notification after acquiring the session - we need it in case there were any breakpoints pending before
            // the attach, in which case we'll immediately get breakpoint creation requests as soon as we send these, and
            // we will need the session to process them.
            AD7EngineCreateEvent.Send(this);
            AD7ProgramCreateEvent.Send(this);
            Send(new AD7LoadCompleteEvent(), AD7LoadCompleteEvent.IID);

            // Register event handlers after notifying VS that debug engine has loaded. This order is important because
            // we may get a Browse event immediately, and we want to raise a breakpoint notification in response to that
            // to pause the debugger - but it will be ignored unless the engine has reported its creation.
            // Also, AfterRequest must be registered before Browse, so that we never get in a situation where we get
            // Browse but not AfterRequest that follows it because of a race between raising and registration.
            DebugSession.RSession.AfterRequest += RSession_AfterRequest;
            DebugSession.RSession.Disconnected += RSession_Disconnected;

            // If we're already at the Browse prompt, registering the handler will result in its immediate invocation.
            // We want to handle that fully before we process any following AfterRequest event to avoid concurrency issues
            // where we pause and never resume, so hold the lock while adding the handler. 
            lock (_browseLock) {
                DebugSession.Browse += Session_Browse;
            }

            return VSConstants.S_OK;
        }

        int IDebugEngine2.CauseBreak() {
            ThrowIfDisposed();
            DebugSession.Break()
                .SilenceException<MessageTransportException>()
                .SilenceException<RException>()
                .DoNotWait();
            return VSConstants.S_OK;
        }

        int IDebugEngine2.ContinueFromSynchronousEvent(IDebugEvent2 pEvent) {
            ThrowIfDisposed();

            if (pEvent is AD7ProgramDestroyEvent) {
                Dispose();
            }

            return VSConstants.S_OK;
        }

        int IDebugEngine2.CreatePendingBreakpoint(IDebugBreakpointRequest2 pBPRequest, out IDebugPendingBreakpoint2 ppPendingBP) {
            ppPendingBP = new AD7PendingBreakpoint(this, pBPRequest);
            return VSConstants.S_OK;
        }

        int IDebugEngine2.DestroyProgram(IDebugProgram2 pProgram) {
            ThrowIfDisposed();
            return DebuggerConstants.E_PROGRAM_DESTROY_PENDING;
        }

        int IDebugEngine2.EnumPrograms(out IEnumDebugPrograms2 ppEnum) {
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.GetEngineId(out Guid pguidEngine) {
            ThrowIfDisposed();
            pguidEngine = DebuggerGuids.DebugEngine;
            return VSConstants.S_OK;
        }

        int IDebugEngine2.RemoveAllSetExceptions(ref Guid guidType) {
            // TODO
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.RemoveSetException(EXCEPTION_INFO[] pException) {
            // TODO
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.SetException(EXCEPTION_INFO[] pException) {
            // TODO
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngine2.SetLocale(ushort wLangID) {
            ThrowIfDisposed();
            return VSConstants.S_OK;
        }

        int IDebugEngine2.SetMetric(string pszMetric, object varValue) {
            ThrowIfDisposed();
            return VSConstants.S_OK;
        }

        int IDebugEngine2.SetRegistryRoot(string pszRegistryRoot) {
            ThrowIfDisposed();
            return VSConstants.S_OK;
        }

        int IDebugEngineLaunch2.CanTerminateProcess(IDebugProcess2 pProcess) {
            ThrowIfDisposed();
            return VSConstants.S_FALSE;
        }

        int IDebugEngineLaunch2.LaunchSuspended(string pszServer, IDebugPort2 pPort, string pszExe, string pszArgs, string pszDir, string bstrEnv, string pszOptions, enum_LAUNCH_FLAGS dwLaunchFlags, uint hStdInput, uint hStdOutput, uint hStdError, IDebugEventCallback2 pCallback, out IDebugProcess2 ppProcess) {
            ppProcess = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngineLaunch2.ResumeProcess(IDebugProcess2 pProcess) {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugEngineLaunch2.TerminateProcess(IDebugProcess2 pProcess) {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.Attach(IDebugEventCallback2 pCallback) {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.CanDetach() {
            ThrowIfDisposed();
            return VSConstants.S_OK;
        }

        int IDebugProgram2.CauseBreak() {
            ThrowIfDisposed();
            return IDebugEngine2.CauseBreak();
        }

        int IDebugProgram2.Detach() {
            ThrowIfDisposed();

            try {
                // Disable breakpoint instrumentation.
                DebugSession.EnableBreakpoints(false).GetResultOnUIThread();
            } finally {
                // Detach should never fail, even if something above didn't work.
                Send(new AD7ProgramDestroyEvent(0), AD7ProgramDestroyEvent.IID);
            }

            return VSConstants.S_OK;
        }

        private int Continue(IDebugThread2 pThread) {
            ThrowIfDisposed();

            if (_firstContinue) {
                _firstContinue = false;
            } else {
                // If _sentContinue is true, then this is a dummy Continue issued to notify the
                // debugger that user has explicitly entered something at the Browse prompt, and
                // we don't actually need to issue the command to R debugger.

                Task continueTask = null;
                lock (_browseLock) {
                    if (_sentContinue != true) {
                        _sentContinue = true;
                        continueTask = DebugSession.Continue();
                    }
                }

                if (continueTask != null) {
                    continueTask.GetResultOnUIThread();
                }
            }

            return VSConstants.S_OK;
        }

        int IDebugProgram2.Continue(IDebugThread2 pThread) {
            return Continue(pThread);
        }

        int IDebugProgram2.EnumCodeContexts(IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum) {
            ThrowIfDisposed();

            string fileName;
            Marshal.ThrowExceptionForHR(pDocPos.GetFileName(out fileName));

            var start = new TEXT_POSITION[1];
            var end = new TEXT_POSITION[1];
            Marshal.ThrowExceptionForHR(pDocPos.GetRange(start, end));

            var addr = new AD7MemoryAddress(this, fileName, (int)start[0].dwLine);
            ppEnum = new AD7CodeContextEnum(new[] { addr });
            return VSConstants.S_OK;
        }

        int IDebugProgram2.EnumCodePaths(string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety) {
            ppEnum = null;
            ppSafety = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.EnumModules(out IEnumDebugModules2 ppEnum) {
            // TODO
            ppEnum = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.EnumThreads(out IEnumDebugThreads2 ppEnum) {
            ThrowIfDisposed();
            ppEnum = new AD7ThreadEnum(new[] { MainThread });
            return VSConstants.S_OK;
        }

        int IDebugProgram2.Execute() {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetDebugProperty(out IDebugProperty2 ppProperty) {
            ppProperty = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetDisassemblyStream(enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream) {
            ppDisassemblyStream = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetENCUpdate(out object ppUpdate) {
            ppUpdate = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetEngineInfo(out string pbstrEngine, out Guid pguidEngine) {
            ThrowIfDisposed();
            pbstrEngine = "R";
            pguidEngine = DebuggerGuids.DebugEngine;
            return VSConstants.S_OK;
        }

        int IDebugProgram2.GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes) {
            ppMemoryBytes = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetName(out string pbstrName) {
            ThrowIfDisposed();
            pbstrName = null;
            return VSConstants.S_OK;
        }

        int IDebugProgram2.GetProcess(out IDebugProcess2 ppProcess) {
            ppProcess = null;
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.GetProgramId(out Guid pguidProgramId) {
            ThrowIfDisposed();
            pguidProgramId = _programId;
            return VSConstants.S_OK;
        }

        int IDebugProgram2.Step(IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT Step) {
            ThrowIfDisposed();

            Task step;
            switch (sk) {
                case enum_STEPKIND.STEP_OVER:
                    step = DebugSession.StepOverAsync();
                    break;
                case enum_STEPKIND.STEP_INTO:
                    step = DebugSession.StepIntoAsync();
                    break;
                case enum_STEPKIND.STEP_OUT:
                    step = DebugSession.StepOutAsync();
                    break;
                default:
                    return VSConstants.E_NOTIMPL;
            }

            step.ContinueWith(t => {
                Send(new AD7SteppingCompleteEvent(), AD7SteppingCompleteEvent.IID);
            });

            return VSConstants.S_OK;
        }

        int IDebugProgram2.Terminate() {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram2.WriteDump(enum_DUMPTYPE DUMPTYPE, string pszDumpUrl) {
            return VSConstants.E_NOTIMPL;
        }

        int IDebugProgram3.Attach(IDebugEventCallback2 pCallback) {
            return IDebugProgram2.Attach(pCallback);
        }

        int IDebugProgram3.CanDetach() {
            return IDebugProgram2.CanDetach();
        }

        int IDebugProgram3.CauseBreak() {
            return IDebugProgram2.CauseBreak();
        }

        int IDebugProgram3.Continue(IDebugThread2 pThread) {
            return IDebugProgram2.Continue(pThread);
        }

        int IDebugProgram3.Detach() {
            return IDebugProgram2.Detach();
        }

        int IDebugProgram3.EnumCodeContexts(IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum) {
            return IDebugProgram2.EnumCodeContexts(pDocPos, out ppEnum);
        }

        int IDebugProgram3.EnumCodePaths(string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety) {
            return IDebugProgram2.EnumCodePaths(pszHint, pStart, pFrame, fSource, out ppEnum, out ppSafety);
        }

        int IDebugProgram3.EnumModules(out IEnumDebugModules2 ppEnum) {
            return IDebugProgram2.EnumModules(out ppEnum);
        }

        int IDebugProgram3.EnumThreads(out IEnumDebugThreads2 ppEnum) {
            return IDebugProgram2.EnumThreads(out ppEnum);
        }

        int IDebugProgram3.Execute() {
            return IDebugProgram2.Execute();
        }

        int IDebugProgram3.ExecuteOnThread(IDebugThread2 pThread) {
            ThrowIfDisposed();
            DebugSession.CancelStep();
            return Continue(pThread);
        }

        int IDebugProgram3.GetDebugProperty(out IDebugProperty2 ppProperty) {
            return IDebugProgram2.GetDebugProperty(out ppProperty);
        }

        int IDebugProgram3.GetDisassemblyStream(enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream) {
            return IDebugProgram2.GetDisassemblyStream(dwScope, pCodeContext, out ppDisassemblyStream);
        }

        int IDebugProgram3.GetENCUpdate(out object ppUpdate) {
            return IDebugProgram2.GetENCUpdate(out ppUpdate);
        }

        int IDebugProgram3.GetEngineInfo(out string pbstrEngine, out Guid pguidEngine) {
            return IDebugProgram2.GetEngineInfo(out pbstrEngine, out pguidEngine);
        }

        int IDebugProgram3.GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes) {
            return IDebugProgram2.GetMemoryBytes(out ppMemoryBytes);
        }

        int IDebugProgram3.GetName(out string pbstrName) {
            return IDebugProgram2.GetName(out pbstrName);
        }

        int IDebugProgram3.GetProcess(out IDebugProcess2 ppProcess) {
            return IDebugProgram2.GetProcess(out ppProcess);
        }

        int IDebugProgram3.GetProgramId(out Guid pguidProgramId) {
            pguidProgramId = _programId;
            return VSConstants.S_OK;
        }

        int IDebugProgram3.Step(IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT Step) {
            return IDebugProgram2.Step(pThread, sk, Step);
        }

        int IDebugProgram3.Terminate() {
            return IDebugProgram2.Terminate();
        }

        int IDebugProgram3.WriteDump(enum_DUMPTYPE DUMPTYPE, string pszDumpUrl) {
            return IDebugProgram2.WriteDump(DUMPTYPE, pszDumpUrl);
        }

        int IDebugSymbolSettings100.SetSymbolLoadState(int bIsManual, int bLoadAdjacentSymbols, string bstrIncludeList, string bstrExcludeList) {
            ThrowIfDisposed();
            return VSConstants.S_OK;
        }

        private void Session_Browse(object sender, DebugBrowseEventArgs e) {
            lock (_browseLock) {
                _currentBrowseEventArgs = e;
                _sentContinue = false;
            }

            // If we hit a breakpoint or completed a step, we have already reported the stop from the corresponding handlers.
            // Otherwise, this is just a random Browse prompt, so raise a dummy breakpoint event with no breakpoints to stop.
            if (e.BreakpointsHit.Count == 0 && !e.IsStepCompleted) {
                var bps = new AD7BoundBreakpointEnum(new IDebugBoundBreakpoint2[0]);
                var evt = new AD7BreakpointEvent(bps);
                Send(evt, AD7BreakpointEvent.IID);
            }
        }

        private void RSession_AfterRequest(object sender, RRequestEventArgs e) {
            bool? sentContinue;
            lock (_browseLock) {
                var browseEventArgs = _currentBrowseEventArgs;
                if (browseEventArgs == null || browseEventArgs.Contexts != e.Contexts) {
                    // This AfterRequest does not correspond to a Browse prompt, or at least not one
                    // that we have seen before (and paused on), so there's nothing to do.
                    return;
                }

                _currentBrowseEventArgs = null;
                sentContinue = _sentContinue;
                _sentContinue = true;
            }

            if (sentContinue == false) {
                // User has explicitly typed something at the Browse prompt, so tell the debugger that
                // we're moving on by issuing a dummy Continue request to switch it to the running state.
                var vsShell = (IVsUIShell)Package.GetGlobalService(typeof(SVsUIShell));
                Guid group = VSConstants.GUID_VSStandardCommandSet97;
                object arg = null;
                var ex = Marshal.GetExceptionForHR(vsShell.PostExecCommand(ref group, (uint)VSConstants.VSStd97CmdID.Start, 0, ref arg));
                Trace.Assert(ex == null);
            }
        }

        private void RSession_Disconnected(object sender, EventArgs e) {
            IsConnected = false;
            Send(new AD7ProgramDestroyEvent(0), AD7ProgramDestroyEvent.IID);
        }
    }
}
