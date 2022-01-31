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

using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.Diagnostics;
using YetiVSI.Util;

namespace YetiVSI
{
    // Prints messages to a Command Window.
    public class CommandWindowWriter
    {
        private JoinableTaskContext taskContext;
        private IVsCommandWindow commandWindow;

        public CommandWindowWriter(JoinableTaskContext taskContext,
            IVsCommandWindow commandWindow)
        {
            taskContext.ThrowIfNotOnMainThread();

            this.taskContext = taskContext;
            this.commandWindow = commandWindow;
        }

        // Outputs an error message to the logs and a Command Window (if one exists).
        public void PrintErrorMsg(string message)
        {
            taskContext.ThrowIfNotOnMainThread();

            Trace.WriteLine(message);
            commandWindow?.Print(message + Environment.NewLine);
        }

        public void PrintLine(string message)
        {
            taskContext.ThrowIfNotOnMainThread();

            Print(message + Environment.NewLine);
        }

        // Outputs a message to a Command Window (if one exists).
        public void Print(string message)
        {
            taskContext.ThrowIfNotOnMainThread();

            if (commandWindow == null)
            {
                Trace.WriteLine(
                    $"ERROR: No Command Window found to report message.{Environment.NewLine}" +
                    $"{message}");
            }
            else
            {
                commandWindow.Print(message);
            }
        }
    }
}