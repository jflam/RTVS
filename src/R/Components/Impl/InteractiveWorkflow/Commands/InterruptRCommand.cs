﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.R.Components.Controller;
using Microsoft.R.Host.Client;

namespace Microsoft.R.Components.InteractiveWorkflow.Commands {
    public sealed class InterruptRCommand : IAsyncCommand {
        private readonly IRInteractiveWorkflow _interactiveWorkflow;
        private readonly IRSession _session;
        private readonly IDebuggerModeTracker _debuggerModeTracker;
        private volatile bool _enabled;

        public InterruptRCommand(IRInteractiveWorkflow interactiveWorkflow, IDebuggerModeTracker debuggerModeTracker) {
            _interactiveWorkflow = interactiveWorkflow;
            _session = interactiveWorkflow.RSession;
            _debuggerModeTracker = debuggerModeTracker;
            _session.Disconnected += OnDisconnected;
            _session.BeforeRequest += OnBeforeRequest;
            _session.AfterRequest += OnAfterRequest;
        }

        private void OnDisconnected(object sender, EventArgs e) {
            _enabled = false;
        }

        private void OnBeforeRequest(object sender, RBeforeRequestEventArgs e) {
            _enabled = e.Contexts.Count != 1; // Disable command only if prompt is in the top level
        }

        private void OnAfterRequest(object sender, RAfterRequestEventArgs e) {
            _enabled = true;
        }
        
        public CommandStatus Status {
            get {
                var status = CommandStatus.Supported;
                if (_interactiveWorkflow.ActiveWindow == null) {
                    status |= CommandStatus.Invisible;
                } else if (_session.IsHostRunning && _enabled && !_debuggerModeTracker.IsInBreakMode) {
                    status |= CommandStatus.Enabled;
                }
                return status;
            }
        }

        public async Task<CommandResult> InvokeAsync() {
            if (_enabled) {
                _interactiveWorkflow.Operations.ClearPendingInputs();
                await _session.CancelAllAsync();
                _enabled = false;
                return CommandResult.Executed;
            }

            return CommandResult.NotSupported;
        }
    }
}
