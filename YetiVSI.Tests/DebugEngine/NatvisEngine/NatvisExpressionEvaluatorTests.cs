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

﻿using DebuggerApi;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestsCommon.TestSupport;
using YetiVSI.DebugEngine;
using YetiVSI.DebugEngine.NatvisEngine;
using YetiVSI.DebugEngine.Variables;
using YetiVSI.Test.TestSupport;

namespace YetiVSI.Test.DebugEngine.NatvisEngine
{
    [TestFixture]
    class NatvisExpressionEvaluatorEvaluationPrecedenceTests
    {
        const string _memAddress = "0x0000000002260771";

        MediumTestDebugEngineFactoryCompRoot _compRoot;
        NLogSpy _nLogSpy;
        NatvisExpressionEvaluator _evaluator;
        LLDBVariableInformationFactory _varInfoFactory;

        [SetUp]
        public void SetUp()
        {
            _compRoot = new MediumTestDebugEngineFactoryCompRoot();
            _varInfoFactory = _compRoot.GetLldbVariableInformationFactory();

            _nLogSpy = _compRoot.GetNatvisDiagnosticLogSpy();
            _nLogSpy.Attach();

            _evaluator = _compRoot.GetNatvisExpressionEvaluator();
        }

        [TearDown]
        public void Detach()
        {
            _nLogSpy.Detach();
        }

        [Test]
        public async Task ClassMemberAccessAsync()
        {
            RemoteValueFake remoteValue = RemoteValueFakeUtil.CreateClass("MyType", "myType", "");
            remoteValue.AddChild(RemoteValueFakeUtil.CreateSimpleBool("value1", true));
            remoteValue.AddChild(RemoteValueFakeUtil.CreateSimpleInt("value2", 22));

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            IVariableInformation exprVarInfo =
                await _evaluator.EvaluateExpressionAsync("value1", varInfo,
                                                         new Dictionary<string, string>(), null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("value1"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("true"));
        }

        [Test]
        public async Task PointerMemberAccessAsync()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreatePointer("MyType*", "myType", _memAddress);

            remoteValue.AddChild(RemoteValueFakeUtil.CreateSimpleBool("value1", true));
            remoteValue.AddChild(RemoteValueFakeUtil.CreateSimpleInt("value2", 22));

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            IVariableInformation exprVarInfo =
                await _evaluator.EvaluateExpressionAsync("value2", varInfo,
                                                         new Dictionary<string, string>(), null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("value2"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("22"));
        }

        [Test]
        public async Task PointerMemberAccessEvaluationAsync()
        {
            RemoteValueFake pointerValue =
                RemoteValueFakeUtil.CreatePointer("MyType*", "pointer", _memAddress);

            RemoteValueFake classValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");

            pointerValue.SetDereference(classValue);
            classValue.AddValueFromExpression("field + 1",
                                              RemoteValueFakeUtil.CreateSimpleInt("result", 23));

            IVariableInformation varInfo = CreateVarInfo(pointerValue);
            IVariableInformation exprVarInfo = await _evaluator.EvaluateExpressionAsync(
                "field + 1", varInfo, new Dictionary<string, string>(), null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("result"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("23"));
        }

        [Test]
        public async Task ReferenceMemberAccessEvaluationAsync()
        {
            RemoteValueFake referenceValue =
                RemoteValueFakeUtil.CreateReference("MyType&", "reference", _memAddress);

            RemoteValueFake classValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");

            referenceValue.SetDereference(classValue);
            classValue.AddValueFromExpression("field + 1",
                                              RemoteValueFakeUtil.CreateSimpleInt("result", 23));

            IVariableInformation varInfo = CreateVarInfo(referenceValue);
            IVariableInformation exprVarInfo = await _evaluator.EvaluateExpressionAsync(
                "field + 1", varInfo, new Dictionary<string, string>(), null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("result"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("23"));
        }

        [Test]
        public async Task IndexerAccessAsync()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateSimpleIntArray("myArray", 3, 4, 5, 6);

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            IVariableInformation exprVarInfo =
                await _evaluator.EvaluateExpressionAsync("[3]", varInfo,
                                                         new Dictionary<string, string>(), null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("[3]"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("6"));
        }

        [Test]
        public async Task IndexerAccessWithTokenReplacementAsync()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateSimpleIntArray("myArray", 3, 4, 5, 6);

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            IVariableInformation exprVarInfo = await _evaluator.EvaluateExpressionAsync(
                "[$i]", varInfo, new Dictionary<string, string>()
                {
                    {"$i", "2"}
                }, null);

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("[2]"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("5"));
        }

        [Test]
        public async Task GlobalExpressionAsync()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");
            remoteValue.AddValueFromExpression("test1",
                                               RemoteValueFakeUtil.CreateSimpleInt("dummy", 66));

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            IVariableInformation exprVarInfo =
                await _evaluator.EvaluateExpressionAsync("test1", varInfo,
                                                         new Dictionary<string, string>(),
                                                         "result");

            Assert.That(exprVarInfo.DisplayName, Is.EqualTo("result"));
            Assert.That(await exprVarInfo.ValueAsync(), Is.EqualTo("66"));
        }

        [Test]
        public void TestGetExpressionValueBadExpression()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");

            IVariableInformation varInfo = CreateVarInfo(remoteValue);

            Assert.That(
                async () => await _evaluator.EvaluateExpressionAsync(
                    "test1", varInfo, new Dictionary<string, string>(), "result"),
                Throws.TypeOf<ExpressionEvaluationFailed>().With.Message.Contains("test1"));
        }

        IVariableInformation CreateVarInfo(RemoteValue remoteValue)
        {
            var remoteFrameDummy = Substitute.For<RemoteFrame>();
            return _varInfoFactory.Create(remoteFrameDummy, remoteValue);
        }
    }

    class NatvisExpressionEvaluationLldbEvalTests
    {
        MediumTestDebugEngineFactoryCompRoot _compRoot;
        NatvisExpressionEvaluator _evaluator;
        LLDBVariableInformationFactory _varInfoFactory;
        NLogSpy _nLogSpy;
        OptionPageGrid _optionPageGrid;
        VsExpressionCreator _vsExpressionCreator;

        [SetUp]
        public void SetUp()
        {
            _optionPageGrid = OptionPageGrid.CreateForTesting();
            _optionPageGrid.NatvisLoggingLevel = NatvisLoggingLevelFeatureFlag.VERBOSE;

            _compRoot =
                new MediumTestDebugEngineFactoryCompRoot(new YetiVSIService(_optionPageGrid));

            _varInfoFactory = _compRoot.GetLldbVariableInformationFactory();

            _nLogSpy = _compRoot.GetNatvisDiagnosticLogSpy();
            _nLogSpy.Attach();

            _vsExpressionCreator = new VsExpressionCreator();

            _evaluator = _compRoot.GetNatvisExpressionEvaluator();
        }

        [TearDown]
        public void TearDown()
        {
            _nLogSpy.Detach();
        }

        [TestCase(ExpressionEvaluationEngineFlag.LLDB_EVAL)]
        [TestCase(ExpressionEvaluationEngineFlag.LLDB_EVAL_WITH_FALLBACK)]
        public async Task UseLldbEvalAsync(ExpressionEvaluationEngineFlag engineFlag)
        {
            _optionPageGrid.ExpressionEvaluationEngine = engineFlag;

            RemoteValue mockVariable = CreateMockVariable();
            await _evaluator.EvaluateExpressionAsync("2 + 2", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.Received(1).EvaluateExpressionLldbEvalAsync(Arg.Is("2 + 2"));
        }

        [Test]
        public async Task LldbEvalDisabledAsync()
        {
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB;

            RemoteValue mockVariable = CreateMockVariable();
            await _evaluator.EvaluateExpressionAsync("expr", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionLldbEvalAsync(
                Arg.Any<string>());
            mockVariable.Received(1).GetValueForExpressionPath(Arg.Is(".expr"));
            await mockVariable.Received(1).EvaluateExpressionAsync(Arg.Is("expr"));
        }

        [TestCase(LldbEvalErrorCode.InvalidExpressionSyntax)]
        [TestCase(LldbEvalErrorCode.NotImplemented)]
        [TestCase(LldbEvalErrorCode.Unknown)]
        public async Task LldbEvalWithFallbackAsync(LldbEvalErrorCode lldbEvalErrorCode)
        {
            _optionPageGrid.ExpressionEvaluationEngine =
                ExpressionEvaluationEngineFlag.LLDB_EVAL_WITH_FALLBACK;

            RemoteValue mockVariable = CreateMockVariable();
            RemoteValue errorValue = RemoteValueFakeUtil.CreateLldbEvalError(lldbEvalErrorCode);

            mockVariable.EvaluateExpressionLldbEvalAsync(Arg.Any<string>()).Returns(errorValue);

            await _evaluator.EvaluateExpressionAsync("expr", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.Received(1).EvaluateExpressionLldbEvalAsync(Arg.Is("expr"));
            mockVariable.DidNotReceiveWithAnyArgs().GetValueForExpressionPath(Arg.Any<string>());
            await mockVariable.Received(1).EvaluateExpressionAsync(Arg.Is("expr"));
        }

        [TestCase(LldbEvalErrorCode.InvalidNumericLiteral)]
        [TestCase(LldbEvalErrorCode.InvalidOperandType)]
        [TestCase(LldbEvalErrorCode.UndeclaredIdentifier)]
        public void LldbEvalDontFallbackCommonErrors(LldbEvalErrorCode lldbEvalErrorCode)
        {
            _optionPageGrid.ExpressionEvaluationEngine =
                ExpressionEvaluationEngineFlag.LLDB_EVAL_WITH_FALLBACK;

            RemoteValue mockVariable = CreateMockVariable();
            RemoteValue errorValue =
                RemoteValueFakeUtil.CreateLldbEvalError(lldbEvalErrorCode, "the error message");

            mockVariable.EvaluateExpressionLldbEvalAsync(Arg.Any<string>()).Returns(errorValue);

            var exception = Assert.ThrowsAsync<ExpressionEvaluationFailed>(
                async () => await _evaluator.EvaluateExpressionAsync(
                    "2 + 2", CreateVarInfo(mockVariable), new Dictionary<string, string>(),
                    "result"));

            Assert.That(exception.Message, Does.Contain("the error message"));
        }

        [TestCase(LldbEvalErrorCode.InvalidExpressionSyntax)]
        [TestCase(LldbEvalErrorCode.InvalidNumericLiteral)]
        [TestCase(LldbEvalErrorCode.InvalidOperandType)]
        [TestCase(LldbEvalErrorCode.UndeclaredIdentifier)]
        [TestCase(LldbEvalErrorCode.NotImplemented)]
        [TestCase(LldbEvalErrorCode.Unknown)]
        public void LldbEvalWithoutFallback(LldbEvalErrorCode lldbEvalErrorCode)
        {
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB_EVAL;

            RemoteValue mockVariable = CreateMockVariable();
            RemoteValue errorValue =
                RemoteValueFakeUtil.CreateLldbEvalError(lldbEvalErrorCode, "the error message");

            mockVariable.EvaluateExpressionLldbEvalAsync(Arg.Any<string>()).Returns(errorValue);

            var exception = Assert.ThrowsAsync<ExpressionEvaluationFailed>(
                async () => await _evaluator.EvaluateExpressionAsync(
                    "expr", CreateVarInfo(mockVariable), new Dictionary<string, string>(),
                    "result"));

            mockVariable.DidNotReceiveWithAnyArgs().GetValueForExpressionPath(Arg.Any<string>());

            Assert.That(exception.Message, Does.Contain("the error message"));
        }

        [Test]
        public async Task DoNotUseLldbEvalWithScratchVariablesAsync()
        {
            _optionPageGrid.ExpressionEvaluationEngine =
                ExpressionEvaluationEngineFlag.LLDB_EVAL_WITH_FALLBACK;

            RemoteValue mockVariable = CreateMockVariable();

            await _evaluator.EvaluateExpressionAsync(
                "2 + var", CreateVarInfo(mockVariable),
                new Dictionary<string, string>() { { "var", "$var_0" } }, "result");

            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionLldbEvalAsync(
                Arg.Any<string>());
            await mockVariable.Received(1).EvaluateExpressionAsync(Arg.Is("2 + $var_0"));
        }

        [Test]
        public async Task DoNotFallbackToLldbIfThereAreScratchVarsAsync()
        {
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB_EVAL;

            RemoteValue mockVariable = CreateMockVariable();

            var exception = Assert.ThrowsAsync<ExpressionEvaluationFailed>(
                async () => await _evaluator.EvaluateExpressionAsync(
                    "2 + var", CreateVarInfo(mockVariable),
                    new Dictionary<string, string>() { { "var", "$var_0" } }, "result"));

            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionLldbEvalAsync(
                Arg.Any<string>());
            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionAsync(
                Arg.Any<string>());

            Assert.That(exception.Message, Does.Contain("Failed to evaluate expression"));
            Assert.That(exception.Message, Does.Contain("expression: 2 + $var_0"));
        }

        [Test]
        public async Task UseLldbEvalWithConstantSubstituteTokensAsync()
        {
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB_EVAL;

            RemoteValue mockVariable = CreateMockVariable();

            await _evaluator.EvaluateExpressionAsync(
                "($T1)3.14 + $i", CreateVarInfo(mockVariable),
                new Dictionary<string, string>() { { "$T1", "int" }, { "$i", "2" } }, "result");

            await mockVariable.Received(1).EvaluateExpressionLldbEvalAsync(Arg.Is("(int)3.14 + 2"));
        }

        [Test]
        public async Task ChangeLldbEvalFlagInRuntimeAsync()
        {
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB;

            RemoteValue mockVariable = CreateMockVariable();

            await _evaluator.EvaluateExpressionAsync("2 + 2", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionLldbEvalAsync(
                Arg.Any<string>());
            await mockVariable.Received(1).EvaluateExpressionAsync(Arg.Is("2 + 2"));

            mockVariable.ClearReceivedCalls();

            // Change the configuration flag
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB_EVAL;

            await _evaluator.EvaluateExpressionAsync("3 + 3", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.Received(1).EvaluateExpressionLldbEvalAsync(Arg.Is("3 + 3"));

            mockVariable.ClearReceivedCalls();

            // Change the configuration flag back to LLDB
            _optionPageGrid.ExpressionEvaluationEngine = ExpressionEvaluationEngineFlag.LLDB;

            await _evaluator.EvaluateExpressionAsync("4 + 4", CreateVarInfo(mockVariable),
                                                     new Dictionary<string, string>(), "result");

            await mockVariable.DidNotReceiveWithAnyArgs().EvaluateExpressionLldbEvalAsync(
                Arg.Any<string>());
            await mockVariable.Received(1).EvaluateExpressionAsync(Arg.Is("4 + 4"));
        }

        RemoteValue CreateMockVariable()
        {
            RemoteValue mockVariable = Substitute.For<RemoteValue>();
            mockVariable.TypeIsPointerType().Returns(false);
            mockVariable.GetValueForExpressionPath(Arg.Any<string>()).Returns((RemoteValue)null);
            return mockVariable;
        }

        IVariableInformation CreateVarInfo(RemoteValue value)
        {
            return _varInfoFactory.Create(Substitute.For<RemoteFrame>(), value);
        }
    }

    class NatvisExpressionEvaluatorVariableDeclarationTests
    {
        MediumTestDebugEngineFactoryCompRoot _compRoot;
        const string _memAddress = "0x0000000002260771";
        NatvisExpressionEvaluator _evaluator;
        LLDBVariableInformationFactory _varInfoFactory;
        NLogSpy _nLogSpy;

        [SetUp]
        public void SetUp()
        {
            _compRoot = new MediumTestDebugEngineFactoryCompRoot();
            _varInfoFactory = _compRoot.GetLldbVariableInformationFactory();

            _nLogSpy = _compRoot.GetNatvisDiagnosticLogSpy();
            _nLogSpy.Attach();

            _evaluator = _compRoot.GetNatvisExpressionEvaluator();
        }

        [TearDown]
        public void TearDown()
        {
            _nLogSpy.Detach();
        }

        [Test]
        public async Task DeclareSimpleTypeAsync()
        {
            RemoteValueFake remoteValue = RemoteValueFakeUtil.CreateSimpleChar("testVar", 'c');
            remoteValue.AddValueFromExpression("auto $test=22; $test",
                RemoteValueFakeUtil.CreateSimpleInt("$test", 22));

            var scopedNames = new Dictionary<string, string>() {{"test", "$test"}};
            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            await _evaluator.DeclareVariableAsync(varInfo, "test", "22", scopedNames);
        }

        [Test]
        public async Task DeclareVariableAsync()
        {
            RemoteValueFake remoteValue = RemoteValueFakeUtil.CreateSimpleChar("testVar", 'c');
            remoteValue.AddValueFromExpression($"auto $test=var1+var2; $test",
                RemoteValueFakeUtil.CreateSimpleInt("$test", 22));

            var scopedNames = new Dictionary<string, string>() {{"test", "$test"}};
            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            await _evaluator.DeclareVariableAsync(varInfo, "test", "var1+var2", scopedNames);
        }

        [Test]
        public async Task DeclareVariableWithMemberFieldAsync(
            [Values("m_var1+var2", "this->m_var1+var2")]
            string expression)
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");
            remoteValue.AddValueFromExpression($"auto $test=m_var1+var2; $test",
                                               RemoteValueFakeUtil.CreateSimpleInt("$test", 102));
            remoteValue.AddValueFromExpression("auto $test=this->m_var1+var2; $test",
                RemoteValueFakeUtil.CreateSimpleInt($"test", 102));

            var scopedNames = new Dictionary<string, string>() {{"test", "$test"}};
            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            await _evaluator.DeclareVariableAsync(varInfo, "test", expression, scopedNames);
        }

        [Test]
        public void DeclareVariableWithErrorRemoteValue()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");
            remoteValue.AddValueFromExpression($"auto $test=5; $test",
                                               RemoteValueFakeUtil.CreateError("declaration error"));

            IVariableInformation varInfo = CreateVarInfo(remoteValue);
            var exception = Assert.ThrowsAsync<ExpressionEvaluationFailed>(
                async () => await _evaluator.DeclareVariableAsync(varInfo, "test", "5",
                                                                  new Dictionary<string, string>()
                                                                      {{"test", "$test"}}));

            Assert.That(exception.Message, Does.Contain("test"));
            Assert.That(exception.Message, Does.Contain("5"));
            Assert.That(exception.Message, Does.Contain("declaration error"));

            string logOutput = _nLogSpy.GetOutput();
            Assert.That(logOutput, Does.Contain("test"));
            Assert.That(logOutput, Does.Contain("5"));
            Assert.That(logOutput, Does.Contain("declaration error"));
        }

        IVariableInformation CreateVarInfo(RemoteValue remoteValue)
        {
            var remoteFrameDummy = Substitute.For<RemoteFrame>();
            return _varInfoFactory.Create(remoteFrameDummy, remoteValue);
        }
    }

    class NatvisExpressionEvaluatorEvaluateExpressionTests
    {
        const string _memAddress = "0x0000000002260771";
        MediumTestDebugEngineFactoryCompRoot _compRoot;
        NatvisExpressionEvaluator _evaluator;
        LLDBVariableInformationFactory _varInfoFactory;
        RemoteFrame _remoteFrameMock;

        NLogSpy _nLogSpy;

        [SetUp]
        public void SetUp()
        {
            _compRoot = new MediumTestDebugEngineFactoryCompRoot();
            _varInfoFactory = _compRoot.GetLldbVariableInformationFactory();

            _nLogSpy = _compRoot.GetNatvisDiagnosticLogSpy();
            _nLogSpy.Attach();

            _evaluator = _compRoot.GetNatvisExpressionEvaluator();

            _remoteFrameMock = Substitute.For<RemoteFrame>();
        }

        [TearDown]
        public void TearDown()
        {
            _nLogSpy.Detach();
        }

        [Test]
        public async Task EvaluateSimpleExpressionAsync()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreateClass("MyType", "myType", "myValue");
            remoteValue.AddValueFromExpression("14",
                                         RemoteValueFakeUtil.CreateSimpleInt("tmp", 14));

            IVariableInformation varInfo = _varInfoFactory.Create(_remoteFrameMock, remoteValue);

            IVariableInformation result =
                await _evaluator.EvaluateExpressionAsync("14", varInfo, null, "myVar");

            Assert.That(await result.ValueAsync(), Is.EqualTo("14"));
            Assert.That(result.DisplayName, Is.EqualTo("myVar"));
        }

        [Test]
        public void EvaluateExpressionWithErrorRemoteValue()
        {
            RemoteValueFake remoteValue =
                RemoteValueFakeUtil.CreatePointer("MyType*", "myType", _memAddress);
            remoteValue.AddValueFromExpression("myVal",
                RemoteValueFakeUtil.CreateError("invalid expression"));

            IVariableInformation varInfo = _varInfoFactory.Create(_remoteFrameMock, remoteValue);
            var exception = Assert.ThrowsAsync<ExpressionEvaluationFailed>(
                async () => await _evaluator.EvaluateExpressionAsync(
                    "myVal", varInfo, null, "tmp"));

            Assert.That(exception.Message, Does.Contain("myVal"));
        }

        [Test]
        public void DereferencePointer()
        {
            var remoteValue = RemoteValueFakeUtil.CreatePointer("int*", "xPtr", _memAddress);
            var pointee = RemoteValueFakeUtil.CreateSimpleInt("x", 1);
            remoteValue.SetDereference(pointee);

            var varInfo = _varInfoFactory.Create(_remoteFrameMock, remoteValue);
            Assert.That(remoteValue.Dereference(), Is.EqualTo(pointee));
        }
    }
}