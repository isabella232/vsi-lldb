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

﻿using System.Linq;
using System.Threading.Tasks;
using YetiVSI.DebugEngine.Interfaces;

namespace YetiVSI.DebugEngine.Variables
{
    public class ExpandVariableInformation : VariableInformationDecorator
    {
        public class Factory
        {
            readonly ITaskExecutor _taskExecutor;

            public Factory(ITaskExecutor taskExecutor)
            {
                _taskExecutor = taskExecutor;
            }

            public IVariableInformation Create(IVariableInformation varInfo, int index) =>
                new ExpandVariableInformation(
                    _taskExecutor.Run(async () => await GetChildAsync(varInfo, index)));

            static async Task<IVariableInformation> GetChildAsync(
                IVariableInformation varInfo, int index)
            {
                var children = (await varInfo.GetChildAdapter().GetChildrenAsync(index, 1));
                return children.FirstOrDefault() ??
                    new ErrorVariableInformation(varInfo.DisplayName,
                                                 "<out-of-bounds child index in 'expand()' format specifier>");
            }
        }

        ExpandVariableInformation(IVariableInformation varInfo) : base(varInfo)
        {
        }

        public override IVariableInformation GetCachedView() =>
            new ExpandVariableInformation(VarInfo.GetCachedView());
    }
}