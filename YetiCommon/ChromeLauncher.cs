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

using System;

namespace YetiCommon
{
    public class ChromeLauncher
    {
        protected readonly BackgroundProcess.Factory _backgroundProcessFactory;
        protected readonly Lazy<SdkConfig> _sdkConfig;

        public ChromeLauncher(BackgroundProcess.Factory backgroundProcessFactory,
                              SdkConfig.Factory sdkConfigFactory)
        {
            _backgroundProcessFactory = backgroundProcessFactory;
            _sdkConfig = new Lazy<SdkConfig>(sdkConfigFactory.LoadOrDefault);
        }

        public void StartChrome(string url, string workingDirectory)
        {
            string profileDirectory = string.IsNullOrEmpty(SdkConfig.ChromeProfileDir)
                ? "Default"
                : SdkConfig.ChromeProfileDir;

            StartProcess(workingDirectory, $"start chrome \"{url}\"", "--new-window",
                         $"--profile-directory=\"{profileDirectory}\"");
        }

        protected void
            StartProcess(string workingDirectory, string command, params string[] args) =>
            _backgroundProcessFactory.Create(YetiConstants.Command,
                                             $"/c \"{command} {string.Join(" ", args)}\"",
                                             workingDirectory).Start();

        protected SdkConfig SdkConfig => _sdkConfig.Value;
    }
}