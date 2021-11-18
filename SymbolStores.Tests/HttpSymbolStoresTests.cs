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

using NUnit.Framework;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using YetiCommon;

namespace SymbolStores.Tests
{
    [TestFixture]
    class HttpSymbolStoresTests : SymbolStoreBaseTests
    {
        const string STORE_URL = "https://example.com/foo";
        const string STORE_URL_B = "https://example.net/bar";
        static readonly string URL_IN_STORE = $"{STORE_URL}/{FILENAME}/{BUILD_ID}/{FILENAME}";
        FakeHttpMessageHandler fakeHttpMessageHandler;

        HttpClient httpClient;

        public override void SetUp()
        {
            base.SetUp();

            fakeHttpMessageHandler = new FakeHttpMessageHandler();
            httpClient = new HttpClient(fakeHttpMessageHandler);
        }

        [TearDown]
        public void TearDown()
        {
            httpClient.Dispose();
        }

        [Test]
        public void Create_EmptyUrl()
        {
            Assert.Throws<ArgumentException>(
                () => new HttpSymbolStore(fakeFileSystem, httpClient, ""));
        }

        [Test]
        public async Task FindFile_EmptyBuildIdAsync()
        {
            var store = GetEmptyStore();

            var fileReference = await store.FindFileAsync(FILENAME, BuildId.Empty, true, log,
                                                          _forceLoad);

            Assert.Null(fileReference);
            StringAssert.Contains(
                Strings.FailedToSearchHttpStore(STORE_URL, FILENAME, Strings.EmptyBuildId),
                log.ToString());
        }

        [Test]
        public async Task FindFile_HttpRequestExceptionAsync()
        {
            var store = GetEmptyStore();
            fakeHttpMessageHandler.ExceptionMap[new Uri(URL_IN_STORE)] =
                new HttpRequestException("message");

            var fileReference = await store.FindFileAsync(FILENAME, BUILD_ID, true,
                                                          log, _forceLoad);

            Assert.Null(fileReference);
            StringAssert.Contains(Strings.FailedToSearchHttpStore(STORE_URL, FILENAME, "message"),
                                  log.ToString());
        }

        [Test]
        public async Task FindFile_WontSearchAgainAfterHttpRequestExceptionAsync()
        {
            var store = GetEmptyStore();
            fakeHttpMessageHandler.ExceptionMap[new Uri(URL_IN_STORE)] =
                new HttpRequestException("message");

            await store.FindFileAsync(FILENAME, BUILD_ID, true, TextWriter.Null,
                                      _forceLoad);
            var fileReference = await store.FindFileAsync(FILENAME, BUILD_ID, true,
                                                          log, false);
            Assert.Null(fileReference);
            StringAssert.Contains(Strings.DoesNotExistInHttpStore(FILENAME, STORE_URL),
                                  log.ToString());
        }

        [Test]
        public async Task FindFile_HeadRequestsNotSupportedAsync()
        {
            var store = await GetStoreWithFileAsync();
            fakeHttpMessageHandler.SupportsHeadRequests = false;

            var fileReference = await store.FindFileAsync(FILENAME, BUILD_ID, true,
                                                          log, _forceLoad);

            Assert.AreEqual(URL_IN_STORE, fileReference.Location);
            StringAssert.Contains(Strings.FileFound(URL_IN_STORE), log.ToString());
        }

        [Test]
        public async Task FindFile_ConnectionIsUnencryptedAsync()
        {
            var store = new HttpSymbolStore(fakeFileSystem, httpClient, "http://example.com/");

            await store.FindFileAsync(FILENAME, BUILD_ID, true, log, false);

            StringAssert.Contains(Strings.ConnectionIsUnencrypted("example.com"), log.ToString());
        }

        [Test]
        public async Task FindFile_WontSearchAgainAfterConnectionIsUnencryptedAsync()
        {
            var store = new HttpSymbolStore(fakeFileSystem, httpClient, "http://example.com/");

            await store.FindFileAsync(FILENAME, BUILD_ID, true, TextWriter.Null,
                                      _forceLoad);
            var fileReference = await store.FindFileAsync(FILENAME, BUILD_ID, true,
                                                          log, false);
            Assert.Null(fileReference);
            StringAssert.Contains(Strings.DoesNotExistInHttpStore(FILENAME, "http://example.com/"),
                                  log.ToString());
        }

        [Test]
        public void DeepEquals()
        {
            var storeA = new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL);
            var storeB = new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL);

            Assert.True(storeA.DeepEquals(storeB));
            Assert.True(storeB.DeepEquals(storeA));
        }

        [Test]
        public void DeepEquals_NotEqual()
        {
            var storeA = new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL);
            var storeB = new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL_B);

            Assert.False(storeA.DeepEquals(storeB));
            Assert.False(storeB.DeepEquals(storeA));
        }

#region SymbolStoreBaseTests functions

        protected override ISymbolStore GetEmptyStore()
        {
            return new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL);
        }

        protected override Task<ISymbolStore> GetStoreWithFileAsync()
        {
            fakeHttpMessageHandler.ContentMap[new Uri(URL_IN_STORE)] =
                Encoding.UTF8.GetBytes(BUILD_ID.ToHexString());

            return Task.FromResult<ISymbolStore>(
                new HttpSymbolStore(fakeFileSystem, httpClient, STORE_URL));
        }

#endregion
    }
}
