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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DebuggerApi;
using Microsoft.VisualStudio;
using NSubstitute;
using NUnit.Framework;
using YetiVSI.DebugEngine;
using YetiVSI.Shared.Metrics;

namespace YetiVSI.Test.DebugEngine
{
    [TestFixture]
    class ModuleFileLoaderTests
    {
        const string BINARY_FILENAME = "test";
        const string PLATFORM_DIRECTORY = "/path/bin";
        const string LOAD_OUTPUT = "Load output.";
        readonly static string[] IMPORTANT_MODULES = new[]
        {
            "libc-2.24.so", "libc.so", "libc.so.6", "libc-2.24.so.6", "libBrokenLocale.so.1",
            "libutil.so", "libc++.so", "libc++abi.so", "libggp.so", "libvulkan.so",
            "libpulsecommon-12.0.so", "amdvlk64.so", "libdrm.so", "libdrm_amdgpu.so",
            "libidn.so", "libnettle.so",
        };

        ICancelable mockTask;
        SbFileSpec mockPlatformFileSpec;
        IModuleFileLoadMetricsRecorder fakeModuleFileLoadRecorder;
        ISymbolLoader mockSymbolLoader;
        IBinaryLoader mockBinaryLoader;
        ModuleFileLoader moduleFileLoader;
        IModuleSearchLogHolder mockModuleSearchLogHolder;

        [SetUp]
        public void SetUp()
        {
            mockTask = Substitute.For<ICancelable>();
            mockPlatformFileSpec = Substitute.For<SbFileSpec>();
            mockPlatformFileSpec.GetDirectory().Returns(PLATFORM_DIRECTORY);
            mockPlatformFileSpec.GetFilename().Returns(BINARY_FILENAME);
            mockSymbolLoader = Substitute.For<ISymbolLoader>();
            mockBinaryLoader = Substitute.For<IBinaryLoader>();
            mockModuleSearchLogHolder = new ModuleSearchLogHolder();
            moduleFileLoader = new ModuleFileLoader(mockSymbolLoader, mockBinaryLoader,
                                                    false, mockModuleSearchLogHolder);
            fakeModuleFileLoadRecorder = Substitute.For<IModuleFileLoadMetricsRecorder>();
        }

        // Need to assign test name since otherwise the autogenerated names would clash, e.g.
        // "LoadModuleFiles(0,System.Boolean[])" for the first two.
        [TestCase(VSConstants.S_OK, new bool[] {},
            TestName = "LoadModuleFilesSucceeds_NoModules")]
        [TestCase(VSConstants.S_OK, new[] { true },
            TestName = "LoadModuleFilesSucceeds_SymbolLoadSucceeds")]
        [TestCase(VSConstants.E_FAIL, new[] { false },
            TestName = "LoadModuleFilesFails_SymbolLoadFails")]
        [TestCase(VSConstants.S_OK, new[] { true, true, true },
            TestName = "LoadModuleFilesSucceeds_SymbolLoadSucceeds3Times")]
        [TestCase(VSConstants.E_FAIL, new[] { true, false, true },
            TestName = "LoadModuleFilesFails_SymbolLoadSucceedsFailsSucceeds")]
        [TestCase(VSConstants.E_FAIL, new[] { false, false, false },
            TestName = "LoadModuleFilesFails_SymbolLoadFails3Times")]
        public async Task LoadModuleFilesAsync(int expectedReturnCode, bool[] loadSymbolsSuccessValues)
        {
            List<SbModule> modules = loadSymbolsSuccessValues
                              .Select(loadSymbolsSuccessValue => CreateMockModule(
                                          isPlaceholder: true, loadBinarySucceeds: true,
                                          loadSymbolsSucceeds: loadSymbolsSuccessValue))
                              .ToList();

            Assert.AreEqual(
                expectedReturnCode,
                (await moduleFileLoader.LoadModuleFilesAsync(
                    modules, mockTask, fakeModuleFileLoadRecorder)).ResultCode);

            foreach (SbModule module in modules)
            {
                await AssertLoadBinaryReceivedAsync(module);
                await AssertLoadSymbolsReceivedAsync(module);
            }

            int symbolsLoadedAfter = loadSymbolsSuccessValues.Count(x => x);
            fakeModuleFileLoadRecorder.Received()
                .RecordAfterLoad(Arg.Is<DeveloperLogEvent.Types.LoadSymbolData>(
                x =>
                x.ModulesBeforeCount == modules.Count
                && x.ModulesCount == modules.Count
                && x.ModulesAfterCount == modules.Count
                && x.ModulesWithSymbolsLoadedBeforeCount == 0
                && x.ModulesWithSymbolsLoadedAfterCount == symbolsLoadedAfter
                && x.BinariesLoadedBeforeCount == 0
                && x.BinariesLoadedAfterCount == modules.Count
                ));
        }

        [Test]
        public async Task LoadModuleFilesWithInclusionSettingsAsync()
        {
            SbModule includedModule = CreateMockModule(
                true, loadBinarySucceeds: true,
                loadSymbolsSucceeds: true, name: "included");
            SbModule excludedModule = CreateMockModule(
                true, loadBinarySucceeds: true,
                loadSymbolsSucceeds: true, name: "excluded");
            var modules = new List<SbModule> { includedModule, excludedModule };

            bool useIncludeList = true;
            var includeList = new List<string>() { "included" };
            var settings =
                new SymbolInclusionSettings(useIncludeList, new List<string>(), includeList);

            Assert.That(
                (await moduleFileLoader.LoadModuleFilesAsync(
                    modules, settings, true, true, mockTask, fakeModuleFileLoadRecorder))
                    .ResultCode,
                Is.EqualTo(VSConstants.S_OK));

            await AssertLoadBinaryReceivedAsync(includedModule);
            await AssertLoadSymbolsReceivedAsync(includedModule);
            await AssertLoadBinaryNotReceivedAsync(excludedModule);
            await AssertLoadSymbolsNotReceivedAsync(excludedModule);
        }

        [Test]
        public async Task LoadModuleFilesWithExclusionSettingsAsync()
        {
            SbModule includedModule = CreateMockModule(
                true, loadBinarySucceeds: true,
                loadSymbolsSucceeds: true, name: "included");
            SbModule excludedModule = CreateMockModule(
                true, loadBinarySucceeds: true,
                loadSymbolsSucceeds: true, name: "excluded");
            var modules = new List<SbModule> { includedModule, excludedModule };

            bool useIncludeList = false;
            var excludeList = new List<string> { "excluded" };
            var settings =
                new SymbolInclusionSettings(useIncludeList, excludeList, new List<string>());

            Assert.That(
                (await moduleFileLoader.LoadModuleFilesAsync(
                    modules, settings, true, true, mockTask, fakeModuleFileLoadRecorder))
                    .ResultCode,
                Is.EqualTo(VSConstants.S_OK));

            await AssertLoadBinaryReceivedAsync(includedModule);
            await AssertLoadSymbolsReceivedAsync(includedModule);
            await AssertLoadBinaryNotReceivedAsync(excludedModule);
            await AssertLoadSymbolsNotReceivedAsync(excludedModule);
        }

        [Test]
        public async Task LoadModuleFiles_AlreadyLoadedAsync()
        {
            SbModule module = CreateMockModule(isPlaceholder: true, loadBinarySucceeds: true,
                                               loadSymbolsSucceeds: true);

            Assert.AreEqual(VSConstants.S_OK, (await moduleFileLoader.LoadModuleFilesAsync(
                new[] { module }, mockTask, fakeModuleFileLoadRecorder)).ResultCode);
        }

        [Test]
        public async Task LoadModuleFiles_CanceledDuringLoadSymbolsAsync()
        {
            List<SbModule> modules = new[] {
                CreateMockModule(isPlaceholder: false),
                CreateMockModule(isPlaceholder: false),
            }.ToList();

            // Both modules have binaries loaded, but without symbols, we will try to locate
            // the corresponding symbols using `_symbolLoader.LoadSymbolsAsync`. This operation
            // is preceded by the check whether `_task` was cancelled.
            mockTask.When(x => x.ThrowIfCancellationRequested())
                .Do(Callback
                        .First(x => { })
                        .ThenThrow(new OperationCanceledException()));

            Assert.ThrowsAsync<OperationCanceledException>(() =>
                moduleFileLoader.LoadModuleFilesAsync(modules, mockTask, fakeModuleFileLoadRecorder));

            await mockSymbolLoader.Received().LoadSymbolsAsync(
                modules[0], Arg.Any<TextWriter>(), Arg.Any<bool>(), Arg.Any<bool>());
            await mockSymbolLoader.DidNotReceive().LoadSymbolsAsync(
                modules[1], Arg.Any<TextWriter>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        [Test]
        public async Task LoadModuleFiles_CanceledDuringLoadBinariesAsync()
        {
            List<SbModule> modules = new[] {
                CreateMockModule(isPlaceholder: true),
                CreateMockModule(isPlaceholder: true),
            }.ToList();

            // Both modules are placeholders, we will try to find real binaries in our system
            // using `_binaryLoader.LoadBinaryAsync`. This operation is preceded by the check
            // whether `_task` was cancelled. 
            mockTask.When(x => x.ThrowIfCancellationRequested())
                .Do(Callback
                        .First(x => { })
                        .ThenThrow(new OperationCanceledException()));

            Assert.ThrowsAsync<OperationCanceledException>(
                () => moduleFileLoader
                    .LoadModuleFilesAsync(modules, mockTask, fakeModuleFileLoadRecorder));

            await mockBinaryLoader.Received().LoadBinaryAsync(
                modules[0], Arg.Any<TextWriter>());
            await mockBinaryLoader.DidNotReceive().LoadBinaryAsync(
                modules[1], Arg.Any<TextWriter>());
        }

        [Test]
        public async Task LoadModuleFiles_LoadBinariesFailsAsync()
        {
            SbModule module = CreateMockModule(isPlaceholder: true);
            mockBinaryLoader
                .LoadBinaryAsync(module, Arg.Any<TextWriter>())
                .Returns((module, false));
            Assert.AreEqual(VSConstants.E_FAIL, (await moduleFileLoader.LoadModuleFilesAsync(
                new[] { module }, mockTask, fakeModuleFileLoadRecorder)).ResultCode);

            await AssertLoadBinaryReceivedAsync(module);
            await mockSymbolLoader.DidNotReceiveWithAnyArgs()
                .LoadSymbolsAsync(module, null,true, false);
        }

        [Test]
        public async Task LoadModuleFiles_ReplacedPlaceholderModuleAsync()
        {
            SbModule placeholderModule = CreateMockModule(isPlaceholder:true);
            SbModule realModule = CreateMockModule(isPlaceholder: false, loadSymbolsSucceeds: true);
            mockBinaryLoader.LoadBinaryAsync(placeholderModule, Arg.Any<TextWriter>())
                .Returns(x => (realModule, true));
            mockSymbolLoader.LoadSymbolsAsync(realModule, Arg.Any<TextWriter>(),
                                              Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(Task.FromResult(true));
            SbModule[] modules = new[] { placeholderModule };

            Assert.AreEqual(VSConstants.S_OK, (await moduleFileLoader.LoadModuleFilesAsync(
                modules, mockTask, fakeModuleFileLoadRecorder)).ResultCode);

            await AssertLoadBinaryReceivedAsync(placeholderModule);
            await AssertLoadSymbolsReceivedAsync(realModule);
        }

        [Test]
        public async Task LoadModuleFiles_UnableToLoadImportantModuleForGameAttachAsync()
        {
            SbModule module = CreateMockModule(
                isPlaceholder: true, loadSymbolsSucceeds: true, name: IMPORTANT_MODULES.First());
            mockBinaryLoader
                .LoadBinaryAsync(module, Arg.Any<TextWriter>())
                .Returns((module, false));
            LoadModuleFilesResult result = await moduleFileLoader.LoadModuleFilesAsync(
                new[] { module }, null, true, true, mockTask, fakeModuleFileLoadRecorder);

            Assert.AreEqual(VSConstants.E_FAIL, result.ResultCode);
            Assert.AreEqual(false, result.SuggestToEnableSymbolStore);
        }

        [Test]
        public async Task LoadModuleFiles_UnableToLoadImportantModuleForCrashDumpAsync()
        {
            foreach (string moduleName in IMPORTANT_MODULES)
            {
                SbModule module = CreateMockModule(
                    isPlaceholder: true, loadSymbolsSucceeds: true, name: moduleName);
                mockBinaryLoader
                    .LoadBinaryAsync(module, Arg.Any<TextWriter>())
                    .Returns((module, false));
                var dumpModuleFileLoader = new ModuleFileLoader(mockSymbolLoader, mockBinaryLoader,
                                                        true,
                                                        mockModuleSearchLogHolder);
                LoadModuleFilesResult result = await dumpModuleFileLoader.LoadModuleFilesAsync(
                    new[] { module }, null, false, false, mockTask, fakeModuleFileLoadRecorder);

                Assert.AreEqual(VSConstants.E_FAIL, result.ResultCode, moduleName);
                Assert.AreEqual(true, result.SuggestToEnableSymbolStore, moduleName);
            }
        }

        [Test]
        public async Task LoadModuleFiles_UnableToLoadModuleForCrashDumpAsync()
        {
            SbModule module = CreateMockModule( true, loadSymbolsSucceeds: true);
            mockBinaryLoader
                .LoadBinaryAsync(module, Arg.Any<TextWriter>())
                .Returns((module, false));
            var dumpModuleFileLoader = new ModuleFileLoader(mockSymbolLoader, mockBinaryLoader,
                                                            true, mockModuleSearchLogHolder);
            LoadModuleFilesResult result = await dumpModuleFileLoader.LoadModuleFilesAsync(
                new[] { module }, null, false, false, mockTask, fakeModuleFileLoadRecorder);
             
            Assert.AreEqual(VSConstants.E_FAIL, result.ResultCode);
            Assert.AreEqual(false, result.SuggestToEnableSymbolStore);
        }

        [Test]
        public async Task LoadModuleFiles_DoNotShowSuggestionIfSymbolStoreEnabledAsync()
        {
            SbModule module = CreateMockModule(isPlaceholder: true, loadSymbolsSucceeds: true);
            mockBinaryLoader
                .LoadBinaryAsync(module, Arg.Any<TextWriter>())
                .Returns((module, false));
            var dumpModuleFileLoader = new ModuleFileLoader(mockSymbolLoader, mockBinaryLoader,
                                                            true,
                                                            mockModuleSearchLogHolder);
            LoadModuleFilesResult result = await dumpModuleFileLoader.LoadModuleFilesAsync(
                new[] { module }, null, true, true, mockTask, fakeModuleFileLoadRecorder);

            Assert.AreEqual(VSConstants.E_FAIL, result.ResultCode);
            Assert.AreEqual(false, result.SuggestToEnableSymbolStore);
        }

        [Test]
        public async Task GetSearchLogAsync()
        {
            SbModule module = CreateMockModule(isPlaceholder: true, loadSymbolsSucceeds: true);
            module.GetPlatformFileSpec().Returns(mockPlatformFileSpec);
            mockBinaryLoader.LoadBinaryAsync(module, Arg.Any<TextWriter>()).Returns(x =>
            {
                x.Arg<TextWriter>().WriteLine(LOAD_OUTPUT);
                return (module, false);
            });
            await moduleFileLoader.LoadModuleFilesAsync(new[] { module }, mockTask,
                fakeModuleFileLoadRecorder);

            StringAssert.Contains(LOAD_OUTPUT, mockModuleSearchLogHolder.GetSearchLog(module));
        }

        [Test]
        public void GetSearchLog_NoPlatformFileSpec()
        {
            var mockModule = Substitute.For<SbModule>();
            mockModule.GetPlatformFileSpec().Returns((SbFileSpec)null);

            Assert.AreEqual("", mockModuleSearchLogHolder.GetSearchLog(mockModule));
        }

        [Test]
        public void GetSearchLog_NoResult()
        {
            var mockModule = Substitute.For<SbModule>();
            mockModule.GetPlatformFileSpec().Returns(mockPlatformFileSpec);

            Assert.AreEqual("", mockModuleSearchLogHolder.GetSearchLog(mockModule));
        }

        async Task AssertLoadBinaryReceivedAsync(SbModule module)
        {
            await mockBinaryLoader.Received().LoadBinaryAsync(module, Arg.Any<TextWriter>());
        }

        async Task AssertLoadSymbolsReceivedAsync(SbModule module)
        {
            await mockSymbolLoader.Received().LoadSymbolsAsync(
                module, Arg.Any<TextWriter>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        async Task AssertLoadBinaryNotReceivedAsync(SbModule module)
        {
            await mockBinaryLoader.DidNotReceive().LoadBinaryAsync(module, Arg.Any<TextWriter>());
        }

        async Task AssertLoadSymbolsNotReceivedAsync(SbModule module)
        {
            await mockSymbolLoader.DidNotReceive().LoadSymbolsAsync(
                module, Arg.Any<TextWriter>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        /// <summary>
        /// Creates a new mock module, and configures mockBinaryLoader and mockSymbolLoader to
        /// return appropriate values for said module in the context of a call to LoadModuleFiles.
        /// The success values of LoadBinaries and LoadSymbols are directly determined by the values
        /// of |loadBinarySucceeds| and |loadSymbolsSucceeds|.
        /// </summary>
        SbModule CreateMockModule(bool isPlaceholder, bool loadBinarySucceeds = false,
                                  bool loadSymbolsSucceeds = false, string name = "some_name")
        {
            var module = Substitute.For<SbModule>();
            module.GetPlatformFileSpec().GetFilename().Returns(name);
            if (isPlaceholder)
            {
                module.GetNumSections().Returns(1ul);
                module.FindSection(".module_image").Returns(Substitute.For<SbSection>());
                mockBinaryLoader.LoadBinaryAsync(module, Arg.Any<TextWriter>())
                    .Returns(Task.FromResult((module, loadBinarySucceeds)));
            }
            
            mockSymbolLoader.LoadSymbolsAsync(module, Arg.Any<TextWriter>(),
                                               Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(Task.FromResult(loadSymbolsSucceeds));

            return module;
        }
    }
}
