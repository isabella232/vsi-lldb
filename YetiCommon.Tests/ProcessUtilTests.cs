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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YetiCommon;

namespace YetiCommon.Tests
{
    [TestFixture]
    public class ProcessUtilTests
    {
        [TestCase("\"foo\"", "foo")]
        [TestCase("\"foo bar\"", "foo bar")]
        [TestCase("\"foo\\bar\"", "foo\\bar")]
        [TestCase("\"\\\\foo\"", "\\\\foo")]
        [TestCase("\"foo\\\\\"", "foo\\")]
        [TestCase("\"foo\\\\\\\\\"", "foo\\\\")]
        [TestCase("\"foo\\\"bar\"", "foo\"bar")]
        [TestCase("\"foo\\\\\\\"bar\"", "foo\\\"bar")]
        [TestCase("\"\\\"foo\\\"\"", "\"foo\"")]
        [TestCase("\"\\\" \\file.txt\"", "\" \\file.txt")]
        public void QuoteArgument(string expected, string argument)
        {
            Assert.AreEqual(expected, ProcessUtil.QuoteArgument(argument));
        }

        [TestCase("'foo'", "foo")]
        [TestCase("'foo bar'", "foo bar")]
        [TestCase("'\\\\foo'", "\\\\foo")]
        [TestCase("'foo\\'", "foo\\")]
        [TestCase("'foo\\\\'", "foo\\\\")]
        [TestCase("'foo'\\''bar'", "foo'bar")]
        [TestCase("'foo'\\''\\\"bar'", "foo'\"bar")]
        public void QuoteAndEscapeArgumentForSsh(string expected, string argument)
        {
            Assert.AreEqual(expected, ProcessUtil.QuoteAndEscapeArgumentForSsh(argument));
        }
    }
}
