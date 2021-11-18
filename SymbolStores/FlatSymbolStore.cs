// Copyright 2021 Google LLC
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

﻿using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using YetiCommon;
using YetiCommon.Logging;

namespace SymbolStores
{
    /// <summary>
    /// Interface that allows FlatSymbolStore to be mocked in tests
    /// </summary>
    public interface IFlatSymbolStore : ISymbolStore
    {
    }

    /// <summary>
    /// Represents a flat directory containing symbol files
    /// </summary>
    public class FlatSymbolStore : SymbolStoreBase, IFlatSymbolStore
    {
        readonly IFileSystem _fileSystem;
        readonly IBinaryFileUtil _binaryFileUtil;
        [JsonProperty("Path")]
        string _path;

        public FlatSymbolStore(IFileSystem fileSystem, IBinaryFileUtil binaryFileUtil, string path)
            : base(false, false)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(
                    Strings.FailedToCreateFlatStore(Strings.PathNullOrEmpty));
            }

            _fileSystem = fileSystem;
            _binaryFileUtil = binaryFileUtil;
            _path = path;
        }

#region SymbolStoreBase functions

        public override async Task<IFileReference> FindFileAsync(string filename, BuildId buildId,
                                                                 bool isDebugInfoFile,
                                                                 TextWriter log,
                                                                 bool forceLoad)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException(Strings.FilenameNullOrEmpty, nameof(filename));
            }

            string filepath;

            try
            {
                filepath = Path.Combine(_path, filename);
            }
            catch (ArgumentException e)
            {
                await log.WriteLogAsync(
                    Strings.FailedToSearchFlatStore(_path, filename, e.Message));
                return null;
            }

            if (!_fileSystem.File.Exists(filepath))
            {
                await log.WriteLogAsync(Strings.FileNotFound(filepath));
                return null;
            }

            if (buildId != BuildId.Empty)
            {
                try
                {
                    BuildId actualBuildId = await _binaryFileUtil.ReadBuildIdAsync(filepath);
                    if (actualBuildId != buildId)
                    {
                        await log.WriteLogAsync(
                            Strings.BuildIdMismatch(filepath, buildId, actualBuildId));
                        return null;
                    }
                }
                catch (BinaryFileUtilException e)
                {
                    await log.WriteLogAsync(e.Message);
                    return null;
                }
            }

            await log.WriteLogAsync(Strings.FileFound(filepath));
            return new FileReference(_fileSystem, filepath);
        }

        public override Task<IFileReference> AddFileAsync(IFileReference source, string filename,
                                                          BuildId buildId, TextWriter log) =>
            throw new NotSupportedException(Strings.CopyToFlatStoreNotSupported);

        public override bool DeepEquals(ISymbolStore otherStore) =>
            otherStore is FlatSymbolStore other && _path == other._path;

#endregion
    }
}
