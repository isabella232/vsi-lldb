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

﻿using NSubstitute;
using NUnit.Framework;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using YetiCommon;
using System;
using System.Threading.Tasks;

namespace SymbolStores.Tests
{
    [TestFixture]
    class SymbolServerTests : SymbolStoreBaseTests
    {
        const string STORE_A_PATH = @"C:\a";
        const string STORE_B_PATH = @"C:\b";
        const string STORE_C_PATH = @"C:\c";

        ISymbolStore storeA;
        ISymbolStore storeB;
        ISymbolStore storeC;
        ISymbolStore invalidStore;
        SymbolServer symbolServer;

        public override void SetUp()
        {
            base.SetUp();

            storeA = new StructuredSymbolStore(fakeFileSystem, STORE_A_PATH);
            storeB = new StructuredSymbolStore(fakeFileSystem, STORE_B_PATH);
            storeC = new StructuredSymbolStore(fakeFileSystem, STORE_C_PATH);
            invalidStore = new StructuredSymbolStore(fakeFileSystem, INVALID_PATH);
            symbolServer = new SymbolServer();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Constructor_IsCache(bool isCache)
        {
            var store = new SymbolServer(isCache);
            Assert.AreEqual(isCache, store.IsCache);
        }

        [Test]
        public async Task FindFile_SingleStoreAsync()
        {
            await storeA.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);
            symbolServer.AddStore(storeA);

            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public async Task FindFile_FirstStoreAsync()
        {
            await storeA.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(storeB);
            symbolServer.AddStore(storeC);

            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public async Task FindFile_CascadeAsync()
        {
            await storeC.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(storeB);
            symbolServer.AddStore(storeC);

            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.NotNull(await storeA.FindFileAsync(FILENAME, BUILD_ID));
            Assert.NotNull(await storeB.FindFileAsync(FILENAME, BUILD_ID));
            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public async Task FindFile_SkipInvalidAsync()
        {
            await storeC.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(invalidStore);
            symbolServer.AddStore(storeC);

            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.NotNull(await storeA.FindFileAsync(FILENAME, BUILD_ID));
            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public async Task FindFile_NoStoresAsync()
        {
            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.Null(fileReference);
        }

        [Test]
        public async Task FindFile_NotFoundAsync()
        {
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(storeB);
            symbolServer.AddStore(storeC);

            var fileReference = await symbolServer.FindFileAsync(FILENAME, BUILD_ID);

            Assert.Null(fileReference);
        }

        [Test]
        public async Task AddFile_SingleStoreAsync()
        {
            symbolServer.AddStore(storeA);

            var fileReference =
                await symbolServer.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);

            Assert.NotNull(await storeA.FindFileAsync(FILENAME, BUILD_ID));
            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public async Task AddFile_MultipleStoresAsync()
        {
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(storeB);
            symbolServer.AddStore(storeC);

            var fileReference =
                await symbolServer.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);

            Assert.NotNull(await storeA.FindFileAsync(FILENAME, BUILD_ID));
            Assert.NotNull(await storeB.FindFileAsync(FILENAME, BUILD_ID));
            Assert.NotNull(await storeC.FindFileAsync(FILENAME, BUILD_ID));
            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public void AddFile_NoStores()
        {
            Assert.ThrowsAsync<SymbolStoreException>(
                () => symbolServer.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID));
        }

        [Test]
        public async Task AddFile_SkipInvalidStoresAsync()
        {
            symbolServer.AddStore(storeA);
            symbolServer.AddStore(invalidStore);
            symbolServer.AddStore(storeC);

            var fileReference =
                await symbolServer.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);

            Assert.NotNull(await storeA.FindFileAsync(FILENAME, BUILD_ID));
            Assert.NotNull(await storeC.FindFileAsync(FILENAME, BUILD_ID));
            Assert.AreEqual((await storeA.FindFileAsync(FILENAME, BUILD_ID)).Location,
                            fileReference.Location);
        }

        [Test]
        public void GetAllStores()
        {
            symbolServer.AddStore(storeA);
            var subServer = new SymbolServer();
            subServer.AddStore(storeB);
            symbolServer.AddStore(subServer);

            CollectionAssert.AreEqual(new[] { symbolServer, storeA, subServer, storeB },
                                      symbolServer.GetAllStores());
        }

        [Test]
        public void DeepEquals_NoStores()
        {
            var serverA = new SymbolServer();
            var serverB = new SymbolServer();

            Assert.True(serverA.DeepEquals(serverB));
            Assert.True(serverB.DeepEquals(serverA));
        }

        [Test]
        public void DeepEquals_WithStores()
        {
            var serverA = new SymbolServer();
            serverA.AddStore(storeA);
            serverA.AddStore(storeB);
            var serverB = new SymbolServer();
            serverB.AddStore(storeA);
            serverB.AddStore(storeB);

            Assert.True(serverA.DeepEquals(serverB));
            Assert.True(serverB.DeepEquals(serverA));
        }

        [Test]
        public void DeepEquals_DifferentOrder()
        {
            var serverA = new SymbolServer();
            serverA.AddStore(storeA);
            serverA.AddStore(storeB);
            var serverB = new SymbolServer();
            serverB.AddStore(storeB);
            serverB.AddStore(storeA);

            Assert.False(serverA.DeepEquals(serverB));
            Assert.False(serverB.DeepEquals(serverA));
        }

        [Test]
        public void DeepEquals_StoresNotEqual()
        {
            var serverA = new SymbolServer();
            serverA.AddStore(storeA);
            var serverB = new SymbolServer();
            serverB.AddStore(storeB);

            Assert.False(serverA.DeepEquals(serverB));
            Assert.False(serverB.DeepEquals(serverA));
        }

        [Test]
        public void DeepEquals_IsCacheMismatch()
        {
            var serverA = new SymbolServer();
            var serverB = new SymbolServer(isCache: true);

            Assert.False(serverA.DeepEquals(serverB));
            Assert.False(serverB.DeepEquals(serverA));
        }

        [Test]
        public void DeepEquals_DifferentLengths()
        {
            var serverA = new SymbolServer();
            serverA.AddStore(storeA);
            serverA.AddStore(storeB);
            var serverB = new SymbolServer();
            serverB.AddStore(storeA);

            Assert.False(serverA.DeepEquals(serverB));
            Assert.False(serverB.DeepEquals(serverA));
        }

#region SymbolStoreBaseTests functions

        protected override ISymbolStore GetEmptyStore()
        {
            symbolServer.AddStore(storeA);
            return symbolServer;
        }

        protected override async Task<ISymbolStore> GetStoreWithFileAsync()
        {
            symbolServer.AddStore(storeA);
            await symbolServer.AddFileAsync(sourceSymbolFile, FILENAME, BUILD_ID);
            return symbolServer;
        }

#endregion
    }
}
