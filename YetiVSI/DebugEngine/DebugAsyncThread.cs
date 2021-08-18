// Copyright 2020 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using DebuggerApi;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Diagnostics;
using System.IO;
using YetiCommon.CastleAspects;
using YetiVSI.DebugEngine.AsyncOperations;
using YetiVSI.DebugEngine.Interfaces;

namespace YetiVSI.DebugEngine
{
    public interface IDebugThread : IDebugThread2, IDebugThread157, IDecoratorSelf<IDebugThread>
    {
        // Returns the underlying LLDB thread object.
        RemoteThread GetRemoteThread();
    }

    public interface IDebugThreadFactory
    {
        IDebugThread Create(AD7FrameInfoCreator ad7FrameInfoCreator,
                            StackFramesProvider.StackFrameCreator stackFrameCreator,
                            RemoteThread lldbThread, IGgpDebugProgram debugProgram);

        /// <summary>
        /// Creator for unit tests.
        /// </summary>
        /// <param name="stackFramesProvider"></param>
        /// <param name="lldbThread"></param>
        /// <returns></returns>
        IDebugThread CreateForTesting(StackFramesProvider stackFramesProvider,
                                      RemoteThread lldbThread);
    }

    public class DebugAsyncThread : SimpleDecoratorSelf<IDebugThread>, IDebugThread
    {
        public class Factory : IDebugThreadFactory
        {
            readonly ITaskExecutor _taskExecutor;
            readonly FrameEnumFactory _frameEnumFactory;

            [Obsolete("This constructor only exists to support mocking libraries.", error: true)]
            protected Factory()
            {
            }

            public Factory(ITaskExecutor taskExecutor, FrameEnumFactory frameEnumFactory)
            {
                _taskExecutor = taskExecutor;
                _frameEnumFactory = frameEnumFactory;
            }

            public virtual IDebugThread Create(AD7FrameInfoCreator ad7FrameInfoCreator,
                                               StackFramesProvider.StackFrameCreator
                                                   stackFrameCreator, RemoteThread lldbThread,
                                               IGgpDebugProgram debugProgram) =>
                new DebugAsyncThread(_taskExecutor, _frameEnumFactory,
                                     new StackFramesProvider(
                                         lldbThread, stackFrameCreator, debugProgram,
                                         ad7FrameInfoCreator, debugProgram), lldbThread);

            public virtual IDebugThread CreateForTesting(StackFramesProvider stackFramesProvider,
                                                         RemoteThread lldbThread) =>
                new DebugAsyncThread(_taskExecutor, _frameEnumFactory, stackFramesProvider,
                                     lldbThread);
        }

        // Main thread required to provide name and ID for debugger to fully attach
        readonly uint _id;
        readonly RemoteThread _remoteThread;
        readonly FrameEnumFactory _frameEnumFactory;
        readonly ITaskExecutor _taskExecutor;
        readonly StackFramesProvider _stackFramesProvider;

        protected DebugAsyncThread(ITaskExecutor taskExecutor, FrameEnumFactory frameEnumFactory,
                                   StackFramesProvider stackFramesProvider, RemoteThread lldbThread)
        {
            _remoteThread = lldbThread;
            _id = (uint) lldbThread.GetThreadId();
            _taskExecutor = taskExecutor;
            _frameEnumFactory = frameEnumFactory;
            _stackFramesProvider = stackFramesProvider;
        }

        public RemoteThread GetRemoteThread() => _remoteThread;

        #region IDebugThread2 functions

        public int CanSetNextStatement(IDebugStackFrame2 stackFrameOrigin,
                                       IDebugCodeContext2 codeContextDestination)
        {
            stackFrameOrigin.GetThread(out IDebugThread2 threadOrigin);
            if (threadOrigin == null)
            {
                return VSConstants.E_FAIL;
            }

            threadOrigin.GetThreadId(out uint threadIdOrigin);
            if (threadIdOrigin != _id)
            {
                return VSConstants.S_FALSE;
            }

            var contextInfosDestination = new CONTEXT_INFO[1];
            int result = codeContextDestination.GetInfo(
                enum_CONTEXT_INFO_FIELDS.CIF_ADDRESS | enum_CONTEXT_INFO_FIELDS.CIF_FUNCTION,
                contextInfosDestination);
            if (result != VSConstants.S_OK)
            {
                return result;
            }

            string functionNameOrigin;
            if (!DebugEngineUtil.GetAddressFromString(contextInfosDestination[0].bstrAddress,
                                                      out ulong addressPc))
            {
                return VSConstants.E_FAIL;
            }

            if (stackFrameOrigin is IDebugStackFrame stackFrameOriginCast)
            {
                stackFrameOriginCast.GetNameWithSignature(out functionNameOrigin);
            }
            else
            {
                stackFrameOrigin.GetName(out functionNameOrigin);
            }

            if (addressPc != _remoteThread.GetFrameAtIndex(0).GetPC() &&
                contextInfosDestination[0].bstrFunction != functionNameOrigin)
            {
                return VSConstants.S_FALSE;
            }

            return VSConstants.S_OK;
        }

        public virtual int EnumFrameInfo(enum_FRAMEINFO_FLAGS fieldSpec, uint radix,
                                         out IEnumDebugFrameInfo2 frameInfoEnum)
        {
            frameInfoEnum = _frameEnumFactory.Create(_stackFramesProvider, fieldSpec, Self);
            return VSConstants.S_OK;
        }

        public int GetName(out string name)
        {
            name = _remoteThread.GetName();
            return VSConstants.S_OK;
        }

        public int GetProgram(out IDebugProgram2 program)
        {
            program = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetThreadId(out uint id)
        {
            id = _id;
            return VSConstants.S_OK;
        }

        // Retreives requested information about this thread.
        // fields specifies what information should be included in the output THREADPROPERTIES.
        public int GetThreadProperties(enum_THREADPROPERTY_FIELDS fields,
                                       THREADPROPERTIES[] threadProperties)
        {
            var properties = new THREADPROPERTIES();
            if ((enum_THREADPROPERTY_FIELDS.TPF_ID & fields) != 0)
            {
                properties.dwThreadId = _id;
                properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_ID;
            }

            if ((enum_THREADPROPERTY_FIELDS.TPF_SUSPENDCOUNT & fields) != 0)
            {
                // TODO: add info to properties
                properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_SUSPENDCOUNT;
            }

            if ((enum_THREADPROPERTY_FIELDS.TPF_STATE & fields) != 0)
            {
                // TODO: add info to properties
                properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_STATE;
            }

            if ((enum_THREADPROPERTY_FIELDS.TPF_PRIORITY & fields) != 0)
            {
                // TODO: add info to properties
                properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_PRIORITY;
            }

            if ((enum_THREADPROPERTY_FIELDS.TPF_NAME & fields) != 0)
            {
                properties.bstrName = _remoteThread.GetName();
                properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_NAME;
            }

            if ((enum_THREADPROPERTY_FIELDS.TPF_LOCATION & fields) != 0)
            {
                var frame = _remoteThread.GetFrameAtIndex(0);
                if (frame != null)
                {
                    properties.bstrLocation = frame.GetFunctionName();
                    properties.dwFields |= enum_THREADPROPERTY_FIELDS.TPF_LOCATION;
                }
            }

            threadProperties[0] = properties;
            return VSConstants.S_OK;
        }

        public int Resume(out uint suspendCount)
        {
            suspendCount = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int SetNextStatement(IDebugStackFrame2 stackFrame, IDebugCodeContext2 codeContext)
        {
            int result = CanSetNextStatement(stackFrame, codeContext);
            if (result != VSConstants.S_OK)
            {
                return VSConstants.E_FAIL;
            }

            uint line;
            string filePath;
            codeContext.GetDocumentContext(out IDebugDocumentContext2 documentContext);
            if (documentContext != null)
            {
                documentContext.GetName(enum_GETNAME_TYPE.GN_FILENAME, out filePath);
                var beginPosition = new TEXT_POSITION[1];
                var endPosition = new TEXT_POSITION[1];
                documentContext.GetStatementRange(beginPosition, endPosition);
                line = beginPosition[0].dwLine + 1;
                Trace.WriteLine($"Settings next statement to {filePath} line {line}.");
            }
            else
            {
                var process = _remoteThread.GetProcess();
                if (process == null)
                {
                    Trace.WriteLine("Error: Failed to obtain process." +
                                    " Unable to set next statement");
                    return VSConstants.E_FAIL;
                }

                var target = process.GetTarget();
                if (target == null)
                {
                    Trace.WriteLine("Error: Failed to obtain target." +
                                    " Unable to set next statement");
                    return VSConstants.E_FAIL;
                }

                var address = target.ResolveLoadAddress(codeContext.GetAddress());
                if (address == null)
                {
                    Trace.WriteLine("Error: Failed to obtain address." +
                                    " Unable to set next statement");
                    return VSConstants.E_FAIL;
                }

                var lineEntry = address.GetLineEntry();
                if (lineEntry == null)
                {
                    Trace.WriteLine("Error: Failed to obtain line entry." +
                                    " Unable to set next statement");
                    return VSConstants.E_FAIL;
                }

                filePath = Path.Combine(lineEntry.Directory, lineEntry.FileName);
                line = lineEntry.Line;
                Trace.WriteLine($"Settings next statement to {address} at {filePath} line {line}");
            }

            SbError error = _remoteThread.JumpToLine(filePath, line);
            if (error.Fail())
            {
                Trace.WriteLine(error.GetCString());
                return VSConstants.E_FAIL;
            }

            return VSConstants.S_OK;
        }

        public int Suspend(out uint suspendCount)
        {
            suspendCount = 0;
            return VSConstants.E_NOTIMPL;
        }

        #region Uncalled IDebugThread2 functions

        public int GetLogicalThread(IDebugStackFrame2 stackFrame,
                                    out IDebugLogicalThread2 logicalThread)
        {
            logicalThread = null;
            return VSConstants.E_NOTIMPL;
        }

        public int SetThreadName(string name)
        {
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #endregion

        public int GetAllFramesAsync(enum_FRAMEINFO_FLAGS dwFlags, uint dwFlagsEx, uint radix,
                                     IAsyncDebugGetFramesCompletionHandler pCompletionHandler,
                                     out IAsyncDebugEngineOperation ppDebugOperation)
        {
            ppDebugOperation = new AsyncGetStackFramesOperation(Self, _stackFramesProvider, dwFlags,
                                                                pCompletionHandler, _taskExecutor);
            return VSConstants.S_OK;
        }
    }
}