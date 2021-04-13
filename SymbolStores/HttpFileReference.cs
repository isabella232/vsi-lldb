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

﻿using System;
using System.Net.Http;
using System.IO.Abstractions;
using System.IO;
using System.Threading.Tasks;

namespace SymbolStores
{
    // Represents a symbol file that can be downloaded over HTTP.
    public sealed class HttpFileReference : IFileReference
    {
        readonly IFileSystem fileSystem;
        readonly HttpClient httpClient;

        public HttpFileReference(IFileSystem fileSystem, HttpClient httpClient, string url)
        {
            this.fileSystem = fileSystem;
            this.httpClient = httpClient;
            Location = url ?? throw new ArgumentNullException(nameof(url));
        }

#region IFileReference functions

        public async Task CopyToAsync(string destFilepath)
        {
            if (destFilepath == null)
            {
                throw new ArgumentNullException(nameof(destFilepath));
            }

            try
            {
                // Delete the file if it already exists.
                if (fileSystem.File.Exists(destFilepath))
                {
                    fileSystem.File.Delete(destFilepath);
                }

                var destDirectory = Path.GetDirectoryName(destFilepath);
                // If destFilePath does not have directory information (eg. if it's a relative path
                // to a file in the current directory) GetDirectoryName will return an empty
                // string.
                if (!string.IsNullOrEmpty(destDirectory))
                {
                    fileSystem.Directory.CreateDirectory(destDirectory);
                }

                // Download to a temp file and rename in order to avoid potentially leaving a
                // half-downloaded file in the case of an event such as a power failure.
                string tempFilepath = $"{destFilepath}.{Guid.NewGuid()}.temp";
                try
                {
                    using (var httpStream = await httpClient.GetStreamAsync(Location)) using (
                        var fileStream = fileSystem.File.Open(tempFilepath, FileMode.CreateNew))
                    {
                        httpStream.CopyTo(fileStream);
                    }
                    fileSystem.File.Move(tempFilepath, destFilepath);
                }
                finally
                {
                    fileSystem.File.Delete(tempFilepath);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is NotSupportedException || e is ArgumentException ||
                                      e is ObjectDisposedException || e is HttpRequestException)
            {
                throw new SymbolStoreException(e.Message, e);
            }
        }

        public bool IsFilesystemLocation => false;

        public string Location { get; private set; }

#endregion
    }
}
