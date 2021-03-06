// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using Microsoft.Common.Core;
using Microsoft.Languages.Core.Text;
using Microsoft.Languages.Editor.Extensions;
using Microsoft.R.Components.Extensions;
using Microsoft.R.Editor;
using Microsoft.R.Editor.Document;
using Microsoft.R.Editor.Formatting;
using Microsoft.R.Editor.Settings;
using Microsoft.VisualStudio.R.Package.Shell;
using Microsoft.VisualStudio.R.Package.Utilities;
using Microsoft.VisualStudio.R.Packages.R;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.VisualStudio.R.Package.Expansions {
    /// <summary>
    /// Text view client that manages insertion of snippets
    /// </summary>
    public sealed class ExpansionClient : IVsExpansionClient {
        private static readonly string[] AllStandardSnippetTypes = { "Expansion", "SurroundsWith" };
        private static readonly string[] SurroundWithSnippetTypes = { "SurroundsWith" };

        private readonly IVsExpansionManager _expansionManager;
        private readonly IExpansionsCache _cache;

        private IVsExpansionSession _expansionSession;
        private int _currentFieldIndex = -1;
        private int _markerCount;

        class Marker : TextRange {
            public IVsTextStreamMarker StreamMarker { get; }
            public Marker(IVsTextStreamMarker m, int start, int length) :
                base(start, length) {
                StreamMarker = m;
            }
        }
        public ExpansionClient(ITextView textView, ITextBuffer textBuffer, IVsExpansionManager expansionManager, IExpansionsCache cache) {
            TextView = textView;
            TextBuffer = textBuffer;
            _expansionManager = expansionManager;
            _cache = cache;
        }

        public ITextBuffer TextBuffer { get; }
        public ITextView TextView { get; }

        internal IVsExpansionSession Session => _expansionSession;

        public bool IsEditingExpansion() {
            return _expansionSession != null;
        }

        internal bool IsCaretInsideSnippetFields() {
            if (!IsEditingExpansion() || TextView.Caret.InVirtualSpace) {
                return false;
            }

            var rPoint = TextView.MapDownToR(TextView.Caret.Position.BufferPosition);
            if (rPoint.HasValue) {
                var markers = GetFieldMarkers();
                return markers.GetItemContaining(rPoint.Value.Position) >= 0 || markers.GetItemAtPosition(rPoint.Value.Position) >= 0;
            }
            return false;
        }

        public int InvokeInsertionUI(int invokationCommand) {
            if ((_expansionManager != null) && (TextView != null)) {
                // Set the allowable snippet types and prompt text according to the current command.
                string[] snippetTypes = null;
                string promptText = "";
                if (invokationCommand == (uint)VSConstants.VSStd2KCmdID.INSERTSNIPPET) {
                    snippetTypes = AllStandardSnippetTypes;
                    promptText = Resources.InsertSnippet;
                } else if (invokationCommand == (uint)VSConstants.VSStd2KCmdID.SURROUNDWITH) {
                    snippetTypes = SurroundWithSnippetTypes;
                    promptText = Resources.SurrondWithSnippet;
                }

                return _expansionManager.InvokeInsertionUI(
                    TextView.GetViewAdapter<IVsTextView>(),
                    this,
                    RGuidList.RLanguageServiceGuid,
                    snippetTypes,
                    (snippetTypes != null) ? snippetTypes.Length : 0,
                    0,
                    null, // Snippet kinds
                    0,    // Length of snippet kinds
                    0,
                    promptText,
                    "\t");
            }
            return VSConstants.E_UNEXPECTED;
        }

        public int GoToNextExpansionField() {
            int hr = VSConstants.E_FAIL;
            if (!TextView.IsStatementCompletionWindowActive()) {
                hr = Session.GoToNextExpansionField(0);
                if (VSConstants.S_OK != hr) {
                    var index = _currentFieldIndex < _markerCount - 1 ? _currentFieldIndex + 1 : 0;
                    hr = PositionCaretInField(index);
                }
            }
            return hr;
        }

        public int GoToPreviousExpansionField() {
            int hr = VSConstants.E_FAIL;
            if (!TextView.IsStatementCompletionWindowActive()) {
                hr = Session.GoToPreviousExpansionField();
                if (VSConstants.S_OK != hr) {
                    var index = _currentFieldIndex > 0 ? _currentFieldIndex - 1 : _markerCount - 1;
                    hr = PositionCaretInField(index);
                }
            }
            return hr;
        }

        public int EndExpansionSession(bool leaveCaretWhereItIs) => Session.EndCurrentExpansion(leaveCaretWhereItIs ? 1 : 0);

        /// <summary>
        /// Inserts a snippet based on a shortcut string.
        /// </summary>
        public int StartSnippetInsertion(out bool snippetInserted) {
            int hr = VSConstants.E_FAIL;
            snippetInserted = false;

            // Get the text at the current caret position and
            // determine if it is a snippet shortcut.
            if (!TextView.Caret.InVirtualSpace) {
                SnapshotPoint caretPoint = TextView.Caret.Position.BufferPosition;

                var textBuffer = GetTargetBuffer();
                var expansion = textBuffer.GetBufferAdapter<IVsExpansion>();

                Span span;
                var shortcut = TextView.GetItemBeforeCaret(out span, x => true);
                VsExpansion? exp = _cache.GetExpansion(shortcut);

                var ts = TextSpanFromViewSpan(span);
                if (exp.HasValue && ts.HasValue) {
                    // Insert into R buffer
                    hr = expansion.InsertNamedExpansion(exp.Value.title, exp.Value.path, ts.Value, this, RGuidList.RLanguageServiceGuid, 0, out _expansionSession);
                    // If EndExpansion was called before InsertExpansion returned, so set _expansionSession
                    // to null to indicate that there is no active expansion session. This can occur when 
                    // the snippet inserted doesn't have any expansion fields.
                    if (_expansionSession != null) {
                        PositionCaretInField(0);
                    }
                    snippetInserted = ErrorHandler.Succeeded(hr);
                }
            }
            return hr;
        }

        #region IVsExpansionClient
        public int EndExpansion() {
            _expansionSession = null;
            return VSConstants.S_OK;
        }

        public int FormatSpan(IVsTextLines vsTextLines, TextSpan[] ts) {
            int hr = VSConstants.S_OK;
            int startPos = -1;
            int endPos = -1;
            if (ErrorHandler.Succeeded(vsTextLines.GetPositionOfLineIndex(ts[0].iStartLine, ts[0].iStartIndex, out startPos)) &&
                ErrorHandler.Succeeded(vsTextLines.GetPositionOfLineIndex(ts[0].iEndLine, ts[0].iEndIndex, out endPos))) {
                var textBuffer = vsTextLines.ToITextBuffer();
                RangeFormatter.FormatRange(TextView, textBuffer, TextRange.FromBounds(startPos, endPos), REditorSettings.FormatOptions, VsAppShell.Current);
            }
            return hr;
        }

        public int GetExpansionFunction(MSXML.IXMLDOMNode xmlFunctionNode, string bstrFieldName, out IVsExpansionFunction pFunc) {
            pFunc = null;
            return VSConstants.S_OK;
        }

        public int IsValidKind(IVsTextLines pBuffer, TextSpan[] ts, string bstrKind, out int pfIsValidKind) {
            pfIsValidKind = 1;
            return VSConstants.S_OK;
        }

        public int IsValidType(IVsTextLines pBuffer, TextSpan[] ts, string[] rgTypes, int iCountTypes, out int pfIsValidType) {
            pfIsValidType = 1;
            return VSConstants.S_OK;
        }

        public int OnAfterInsertion(IVsExpansionSession pSession) {
            return VSConstants.S_OK;
        }

        public int OnBeforeInsertion(IVsExpansionSession pSession) {
            return VSConstants.S_OK;
        }

        public int OnItemChosen(string pszTitle, string pszPath) {
            int hr = VSConstants.E_FAIL;
            if (!TextView.Caret.InVirtualSpace) {
                var span = new Span(TextView.Caret.Position.BufferPosition, 0);
                var ts = TextSpanFromViewSpan(span);
                if (ts.HasValue) {
                    var expansion = GetTargetBuffer().GetBufferAdapter<IVsExpansion>();
                    hr = expansion.InsertNamedExpansion(pszTitle, pszPath, ts.Value, this, RGuidList.RLanguageServiceGuid, 0, out _expansionSession);
                    // If EndExpansion was called before InsertNamedExpansion returned, so set _expansionSession
                    // to null to indicate that there is no active expansion session. This can occur when 
                    // the snippet inserted doesn't have any expansion fields.
                    if (_expansionSession != null) {
                        PositionCaretInField(0);
                    }
                }
            }
            return hr;
        }

        public int PositionCaretForEditing(IVsTextLines pBuffer, TextSpan[] ts) {
            return PositionCaretInField(0);
        }
        #endregion

        private ITextBuffer GetTargetBuffer() {
            if (TextView.IsRepl()) {
                var document = REditorDocument.FindInProjectedBuffers(TextView.TextBuffer);
                return document?.TextBuffer;
            }
            return TextView.TextBuffer;
        }

        private Span? SpanFromViewSpan(Span span) {
            var textBuffer = GetTargetBuffer();
            if (TextView.IsRepl()) {
                // Map it down to R buffer
                var start = TextView.MapDownToR(new SnapshotPoint(TextView.TextBuffer.CurrentSnapshot, span.Start));
                var end = TextView.MapDownToR(new SnapshotPoint(TextView.TextBuffer.CurrentSnapshot, span.End));
                if (!start.HasValue || !end.HasValue) {
                    return null;
                }
                return Span.FromBounds(start.Value, end.Value);
            }
            return span;
        }

        /// <summary>
        /// Converts view span to TextSpan structure in the R buffer.
        /// TextSpan structure is used in legacy IVs* interfaces
        /// </summary>
        private TextSpan? TextSpanFromViewSpan(Span span) {
            var textBuffer = GetTargetBuffer();
            if (TextView.IsRepl()) {
                // Map it down to R buffer
                var start = TextView.MapDownToR(new SnapshotPoint(TextView.TextBuffer.CurrentSnapshot, span.Start));
                var end = TextView.MapDownToR(new SnapshotPoint(TextView.TextBuffer.CurrentSnapshot, span.End));
                if (!start.HasValue || !end.HasValue) {
                    return null;
                }
                span = Span.FromBounds(start.Value, end.Value);
            }
            return span.ToTextSpan(textBuffer);
        }

        private int PositionCaretInField(int index) {
            var markers = GetFieldMarkers();
            if (index >= 0 && index < markers.Count) {
                SelectMarker(markers, index);
                return VSConstants.S_OK;
            }
            return VSConstants.E_FAIL;
        }

        private void SelectMarker(TextRangeCollection<Marker> markers, int selectIndex) {
            if (_currentFieldIndex >= 0 && _currentFieldIndex < markers.Count) {
                // Unselect marker. It changes style and no longer tracks editing.
                markers[_currentFieldIndex].StreamMarker.SetType(15); // 'Unselected legacy snippet field'
                markers[_currentFieldIndex].StreamMarker.SetBehavior(
                    (uint)(MARKERBEHAVIORFLAGS2.MB_INHERIT_FOREGROUND | MARKERBEHAVIORFLAGS2.MB_DONT_DELETE_IF_ZEROLEN)); // 48
            }

            if (selectIndex >= 0 && selectIndex < markers.Count) {
                _currentFieldIndex = selectIndex;
                var marker = markers[selectIndex];

                // Flip stream marker into selected state. 
                // It will change style and new behavior will cause it to expand with editing
                marker.StreamMarker.SetType(16); // 'Selected legacy snippet field'
                marker.StreamMarker.SetBehavior(
                     (uint)(MARKERBEHAVIORFLAGS2.MB_INHERIT_FOREGROUND | MARKERBEHAVIORFLAGS2.MB_DONT_DELETE_IF_ZEROLEN) |
                     (uint)(MARKERBEHAVIORFLAGS.MB_LEFTEDGE_LEFTTRACK | MARKERBEHAVIORFLAGS.MB_RIGHTEDGE_RIGHTTRACK)); // 54

                var viewPoint = TextView.MapUpToView(new SnapshotPoint(GetTargetBuffer().CurrentSnapshot, marker.Start));
                if (viewPoint.HasValue) {
                    TextView.Selection.Select(
                        new SnapshotSpan(TextView.TextBuffer.CurrentSnapshot,
                            new Span(viewPoint.Value.Position, marker.Length)),
                        isReversed: false);
                    TextView.Caret.MoveTo(viewPoint.Value + marker.Length);
                }
            }
        }

        private int GetCurrentFieldIndex() {
            var rPosition = TextView.MapDownToR(TextView.Caret.Position.BufferPosition);
            var markers = GetFieldMarkers();
            var index = markers.GetItemAtPosition(rPosition.Value);
            if (index < 0) {
                index = markers.GetItemAtPosition(rPosition.Value);
            }
            return index;
        }

        private TextRangeCollection<Marker> GetFieldMarkers() {
            var markers = new List<Marker>();

            TextSpan[] pts = new TextSpan[1];
            ErrorHandler.ThrowOnFailure(Session.GetSnippetSpan(pts));
            TextSpan snippetSpan = pts[0];

            // Convert text span to stream positions
            int snippetStart, snippetEnd;
            var vsTextLines = GetTargetBuffer().GetBufferAdapter<IVsTextLines>();
            ErrorHandler.ThrowOnFailure(vsTextLines.GetPositionOfLineIndex(snippetSpan.iStartLine, snippetSpan.iStartIndex, out snippetStart));
            ErrorHandler.ThrowOnFailure(vsTextLines.GetPositionOfLineIndex(snippetSpan.iEndLine, snippetSpan.iEndIndex, out snippetEnd));

            var textStream = (IVsTextStream)vsTextLines;

            IVsEnumStreamMarkers enumMarkers;
            if (VSConstants.S_OK == textStream.EnumMarkers(snippetStart, snippetEnd - snippetStart, 0, (uint)(ENUMMARKERFLAGS.EM_ALLTYPES | ENUMMARKERFLAGS.EM_INCLUDEINVISIBLE | ENUMMARKERFLAGS.EM_CONTAINED), out enumMarkers)) {
                IVsTextStreamMarker curMarker;
                while (VSConstants.S_OK == enumMarkers.Next(out curMarker)) {
                    int curMarkerPos;
                    int curMarkerLen;
                    if (VSConstants.S_OK == curMarker.GetCurrentSpan(out curMarkerPos, out curMarkerLen)) {
                        int markerType;
                        if (VSConstants.S_OK == curMarker.GetType(out markerType)) {
                            if (markerType == (int)MARKERTYPE2.MARKER_EXSTENCIL || markerType == (int)MARKERTYPE2.MARKER_EXSTENCIL_SELECTED) {
                                markers.Add(new Marker(curMarker, curMarkerPos, curMarkerLen));
                            }
                        }
                    }
                }
            }
            _markerCount = markers.Count;
            return new TextRangeCollection<Marker>(markers);
        }
    }
}
