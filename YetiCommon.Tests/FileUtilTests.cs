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

﻿using NUnit.Framework;
using System;

namespace YetiCommon.Tests
{
    [TestFixture]
    public class FileUtilTests
    {
        [TestCase(@"\\?\c:\Program Files\test data")]
        [TestCase("c:/src/test/../../ProgRam Files/test data/")]
        [TestCase(@"C:/Program Files/test data\")]
        [TestCase(@"\""c:/Program Files/test data\""")]
        // (internal)
        // [TestCase(@"C:\Progra~1\test data")]
        [TestCase(@"""c:/Program Files/test data\""")]
        [TestCase(@"\'c:/Program Files/test data\'")]
        [TestCase(@"'c:/Program Files/test data\'")]
        public void GetNormalizedPathReturnsSameValueForDifferentNotations(string path)
        {
            var normalized = FileUtil.GetNormalizedPath(path);
            Assert.That(normalized, Is.EqualTo(@"c:\program files\test data"));
        }

        [TestCase(@"C:\test", @"C:\", @"C:\test")]
        [TestCase(@"test", @"C:\", @"C:\test")]
        [TestCase(@"..\test", @"C:\test2", @"C:\test")]
        [TestCase(@"C:\test\..\test2", @"C:\test3", @"C:\test2")]
        public void GetFullPath(string path, string baseRoot, string expected)
        {
            Assert.AreEqual(expected, FileUtil.GetFullPath(path, baseRoot));
        }

        [TestCase(@"..\test", @"test2")]
        [TestCase(@"..\test", @"..\test2")]
        public void GetFullPathInvalidArgument(string path, string baseRoot)
        {
            Assert.Throws<ArgumentException>(() => FileUtil.GetFullPath(path, baseRoot));
        }
    }
}
