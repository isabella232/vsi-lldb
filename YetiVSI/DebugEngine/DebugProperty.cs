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

using DebuggerApi;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using YetiCommon.CastleAspects;
using YetiVSI.DebugEngine.AsyncOperations;
using YetiVSI.DebugEngine.CastleAspects;
using YetiVSI.DebugEngine.Interfaces;
using YetiVSI.DebugEngine.Variables;

namespace YetiVSI.DebugEngine
{
    public delegate IGgpDebugProperty CreateDebugPropertyDelegate(IVariableInformation varInfo);

    public interface IGgpDebugProperty : IDecoratorSelf<IGgpDebugProperty>, IDebugProperty3
    {
        /// <summary>
        /// The extension to IDebugProperty3 interface that:
        /// - Adds a workaround to enable Castle interception of EnumChildren()
        /// - Adds asynchronous version of GetPropertyInfo method.
        /// </summary>
        /// <remarks>
        /// IDebugProperty3.EnumChildren() would raise a AccessViolationException if decorated
        /// with a Castle interceptor when running in Visual Studio 2015.  See (internal) for more
        /// info.
        /// </remarks>
        [InteropBoundary]
        int EnumChildrenImpl(enum_DEBUGPROP_INFO_FLAGS fields, uint radix, Guid guidFilter,
                             enum_DBG_ATTRIB_FLAGS attributeFilter, string nameFilter, uint timeout,
                             out IEnumDebugPropertyInfo2 propertyEnum);

        Task<int> GetPropertyInfoAsync(enum_DEBUGPROP_INFO_FLAGS dwFields, uint dwRadix,
                                       uint dwTimeout, IDebugReference2[] rgpArgs, uint dwArgCount,
                                       DEBUG_PROPERTY_INFO[] pPropertyInfo);
    }

    public interface IGgpAsyncDebugProperty : IGgpDebugProperty, IDebugProperty157
    {
    }

    /// <summary>
    /// External usages of IVariableInformation goes through DebugProperty.
    /// As IVariableInformation has asynchronous method, we want to guarantee that two different
    /// methods are not accessing IVariableInformation simultaneously. Thus all calls to varInfo
    /// go through taskExecutor.
    /// </summary>
    public abstract class GgpCommonDebugProperty : SimpleDecoratorSelf<IGgpDebugProperty>,
        IGgpDebugProperty
    {
        protected readonly IChildrenProviderFactory _childrenProviderFactory;
        protected readonly IVariableInformation _varInfo;
        protected readonly ITaskExecutor _taskExecutor;

        readonly IVariableInformationEnumFactory _varInfoEnumFactory;
        readonly DebugCodeContext.Factory _codeContextFactory;
        readonly VsExpressionCreator _vsExpressionCreator;

        protected GgpCommonDebugProperty(ITaskExecutor taskExecutor,
                                         IChildrenProviderFactory childrenProviderFactory,
                                         IVariableInformationEnumFactory varInfoEnumFactory,
                                         IVariableInformation varInfo,
                                         DebugCodeContext.Factory codeContextFactory,
                                         VsExpressionCreator vsExpressionCreator)
        {
            _taskExecutor = taskExecutor;
            _childrenProviderFactory = childrenProviderFactory;
            _varInfoEnumFactory = varInfoEnumFactory;
            _varInfo = varInfo;
            _codeContextFactory = codeContextFactory;
            _vsExpressionCreator = vsExpressionCreator;
        }

        #region IDebugProperty3 functions

        public int EnumChildren(enum_DEBUGPROP_INFO_FLAGS fields, uint radix, ref Guid guidFilter,
                                enum_DBG_ATTRIB_FLAGS attributeFilter, string nameFilter,
                                uint timeout, out IEnumDebugPropertyInfo2 propertyEnum)
        {
            // Converts |guidFilter| to a non ref type arg so that Castle DynamicProxy doesn't fail
            // to assign values back to it.  See (internal) for more info.
            return Self.EnumChildrenImpl(fields, radix, guidFilter, attributeFilter, nameFilter,
                                         timeout, out propertyEnum);
        }

        public int EnumChildrenImpl(enum_DEBUGPROP_INFO_FLAGS fields, uint radix, Guid guidFilter,
                                    enum_DBG_ATTRIB_FLAGS attributeFilter, string nameFilter,
                                    uint timeout, out IEnumDebugPropertyInfo2 propertyEnum)
        {
            propertyEnum = _taskExecutor.Run(() =>
            {
                _varInfo.FallbackValueFormat = GetFallbackValueFormat(radix);
                IVariableInformation cachedVarInfo = _varInfo.GetCachedView();
                IChildrenProvider childrenProvider =
                    _childrenProviderFactory.Create(cachedVarInfo.GetChildAdapter(), fields, radix);

                return _varInfoEnumFactory.Create(childrenProvider);
            });

            return VSConstants.S_OK;
        }

        public int GetMemoryContext(out IDebugMemoryContext2 memoryContext)
        {
            ulong? maybeAddress = _varInfo.GetMemoryContextAddress();
            if (maybeAddress is ulong address)
            {
                memoryContext =
                    _taskExecutor.Run(
                        () => _codeContextFactory
                            .Create(address, _varInfo.DisplayName, null, Guid.Empty));

                return VSConstants.S_OK;
            }

            memoryContext = null;
            return AD7Constants.S_GETMEMORYCONTEXT_NO_MEMORY_CONTEXT;
        }

        public int GetPropertyInfo(enum_DEBUGPROP_INFO_FLAGS fields, uint radix, uint timeout,
                                   IDebugReference2[] args, uint argCount,
                                   DEBUG_PROPERTY_INFO[] propertyInfo)
        {
            return _taskExecutor.Run(async () =>
                                         await GetPropertyInfoAsync(
                                             fields, radix, timeout, args, argCount, propertyInfo));
        }

        public async Task<int> GetPropertyInfoAsync(enum_DEBUGPROP_INFO_FLAGS fields, uint radix,
                                                    uint timeout, IDebugReference2[] args,
                                                    uint argCount,
                                                    DEBUG_PROPERTY_INFO[] propertyInfo)
        {
            var info = new DEBUG_PROPERTY_INFO();
            _varInfo.FallbackValueFormat = GetFallbackValueFormat(radix);
            IVariableInformation cachedVarInfo = _varInfo.GetCachedView();

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_FULLNAME & fields) != 0)
            {
                string fullname = cachedVarInfo.Fullname();
                if (!string.IsNullOrWhiteSpace(fullname))
                {
                    info.bstrFullName = _vsExpressionCreator
                        .Create(fullname, cachedVarInfo.FormatSpecifier)
                        .ToString();

                    info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_FULLNAME;
                }
            }

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME & fields) != 0)
            {
                info.bstrName = cachedVarInfo.DisplayName;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_NAME;
            }

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE & fields) != 0)
            {
                info.bstrType = cachedVarInfo.TypeName;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_TYPE;
            }

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE & fields) != 0)
            {
                if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE_AUTOEXPAND & fields) != 0)
                {
                    // The value field is in read-only mode, so we can display additional
                    // information that would normally fail to parse as a proper expression.
                    info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE_AUTOEXPAND;

                    info.bstrValue = await ValueStringBuilder.BuildAsync(cachedVarInfo);
                }
                else
                {
                    // The value field is in editing mode, so only display the assignment value.
                    info.bstrValue = cachedVarInfo.AssignmentValue;
                }

                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE;
            }

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_PROP & fields) != 0)
            {
                info.pProperty = Self;
                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_PROP;
            }

            if ((enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB & fields) != 0)
            {
                if (cachedVarInfo.Error)
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_VALUE_ERROR;
                }

                if (cachedVarInfo.MightHaveChildren())
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_OBJ_IS_EXPANDABLE;
                }

                if (cachedVarInfo.IsReadOnly)
                {
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_VALUE_READONLY;
                }

                if (!string.IsNullOrEmpty(cachedVarInfo.StringView))
                {
                    // Causes Visual Studio to show the text visualizer selector. i.e. the
                    // magnifying glass with Text Visualizer, XML Visualizer, etc.
                    info.dwAttrib |= enum_DBG_ATTRIB_FLAGS.DBG_ATTRIB_VALUE_RAW_STRING;
                }

                info.dwFields |= enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_ATTRIB;
            }

            propertyInfo[0] = info;
            return VSConstants.S_OK;
        }

        public int SetValueAsString(string value, uint radix, uint timeout) =>
            SetValueAsStringWithError(value, radix, timeout, out string _);

        public int SetValueAsStringWithError(string value, uint radix, uint timeout,
                                             out string error)
        {
            string outError = null;
            int status = _taskExecutor.Run(() =>
            {
                if (_varInfo.Assign(value, out outError))
                {
                    return VSConstants.S_OK;
                }

                string logName = _varInfo.Fullname();
                if (string.IsNullOrWhiteSpace(logName))
                {
                    logName = _varInfo.DisplayName;
                }

                Trace.WriteLine($"Unable to set var '{logName}'='{value}'. Error: '{outError}'");
                return AD7Constants.E_SETVALUE_VALUE_CANNOT_BE_SET;
            });

            error = outError;
            return status;
        }

        public int GetStringCharLength(out uint pLen)
        {
            string stringView = _varInfo.GetCachedView().StringView ?? string.Empty;
            pLen = (uint) stringView.Length;
            return VSConstants.S_OK;
        }

        public int GetStringChars(uint buflen, ushort[] rgString, out uint pceltFetched)
        {
            string stringView = _varInfo.GetCachedView().StringView ?? string.Empty;
            uint numChars = Math.Min(buflen, (uint) stringView.Length);
            for (int i = 0; i < numChars; ++i)
            {
                rgString[i] = stringView[i];
            }

            pceltFetched = numChars;
            return VSConstants.S_OK;
        }

        #region Implementation not necessary

        public int GetDerivedMostProperty(out IDebugProperty2 derivedMost)
        {
            derivedMost = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetExtendedInfo(ref Guid guidExtendedInfo, out object extendedInfo)
        {
            extendedInfo = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetMemoryBytes(out IDebugMemoryBytes2 memoryBytes)
        {
            memoryBytes = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetParent(out IDebugProperty2 parent)
        {
            parent = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetReference(out IDebugReference2 reference)
        {
            reference = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetSize(out uint size)
        {
            size = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int SetValueAsReference(IDebugReference2[] args, uint argCount,
                                       IDebugReference2 value, uint timeout) =>
            VSConstants.E_NOTIMPL;

        public int CreateObjectID() => VSConstants.E_NOTIMPL;

        public int DestroyObjectID() => VSConstants.E_NOTIMPL;

        public int GetCustomViewerCount(out uint pcelt)
        {
            pcelt = 0;
            return VSConstants.E_NOTIMPL;
        }

        public int GetCustomViewerList(uint celtSkip, uint celtRequested,
                                       DEBUG_CUSTOM_VIEWER[] rgViewers, out uint pceltFetched)
        {
            pceltFetched = 0;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

        #endregion

        ValueFormat GetFallbackValueFormat(uint radix) =>
            radix == 16 ? ValueFormat.Hex : ValueFormat.Default;
    }

    public class DebugProperty : GgpCommonDebugProperty
    {
        public class Factory
        {
            readonly DebugCodeContext.Factory _codeContextFactory;
            readonly VsExpressionCreator _vsExpressionCreator;
            readonly IVariableInformationEnumFactory _varInfoEnumFactory;
            readonly IChildrenProviderFactory _childrenProviderFactory;
            readonly ITaskExecutor _taskExecutor;

            [Obsolete("This constructor only exists to support castle proxy.", error: true)]
            protected Factory()
            {
            }

            public Factory(IVariableInformationEnumFactory varInfoEnumFactory,
                           IChildrenProviderFactory childrenProviderFactory,
                           DebugCodeContext.Factory codeContextFactory,
                           VsExpressionCreator vsExpressionCreator, ITaskExecutor taskExecutor)
            {
                _varInfoEnumFactory = varInfoEnumFactory;
                _childrenProviderFactory = childrenProviderFactory;
                _codeContextFactory = codeContextFactory;
                _vsExpressionCreator = vsExpressionCreator;
                _taskExecutor = taskExecutor;
            }

            public virtual IGgpDebugProperty Create(IVariableInformation varInfo) =>
                new DebugProperty(_taskExecutor, _childrenProviderFactory, _varInfoEnumFactory,
                                  varInfo, _codeContextFactory, _vsExpressionCreator);
        }

        DebugProperty(ITaskExecutor taskExecutor, IChildrenProviderFactory childrenProviderFactory,
                      IVariableInformationEnumFactory varInfoEnumFactory,
                      IVariableInformation varInfo, DebugCodeContext.Factory codeContextFactory,
                      VsExpressionCreator vsExpressionCreator) : base(
            taskExecutor, childrenProviderFactory, varInfoEnumFactory, varInfo, codeContextFactory,
            vsExpressionCreator)
        {
        }
    }

    public class DebugAsyncProperty : GgpCommonDebugProperty, IGgpAsyncDebugProperty
    {
        public class Factory
        {
            readonly DebugCodeContext.Factory _codeContextFactory;
            readonly VsExpressionCreator _vsExpressionCreator;
            readonly IVariableInformationEnumFactory _varInfoEnumFactory;
            readonly IChildrenProviderFactory _childrenProviderFactory;
            readonly ITaskExecutor _taskExecutor;

            [Obsolete("This constructor only exists to support castle proxy.", error: true)]
            protected Factory()
            {
            }

            public Factory(IVariableInformationEnumFactory varInfoEnumFactory,
                           IChildrenProviderFactory childrenProviderFactory,
                           DebugCodeContext.Factory codeContextFactory,
                           VsExpressionCreator vsExpressionCreator, ITaskExecutor taskExecutor)
            {
                _varInfoEnumFactory = varInfoEnumFactory;
                _childrenProviderFactory = childrenProviderFactory;
                _codeContextFactory = codeContextFactory;
                _vsExpressionCreator = vsExpressionCreator;
                _taskExecutor = taskExecutor;
            }

            public virtual IGgpAsyncDebugProperty Create(IVariableInformation varInfo) =>
                new DebugAsyncProperty(_taskExecutor, _childrenProviderFactory, _varInfoEnumFactory,
                                       varInfo, _codeContextFactory, _vsExpressionCreator);
        }

        DebugAsyncProperty(ITaskExecutor taskExecutor,
                           IChildrenProviderFactory childrenProviderFactory,
                           IVariableInformationEnumFactory varInfoEnumFactory,
                           IVariableInformation varInfo,
                           DebugCodeContext.Factory codeContextFactory,
                           VsExpressionCreator vsExpressionCreator) : base(
            taskExecutor, childrenProviderFactory, varInfoEnumFactory, varInfo, codeContextFactory,
            vsExpressionCreator)
        {
        }

        public int GetChildPropertyProvider(uint dwFields, uint dwRadix, uint dwTimeout,
                                            out IAsyncDebugPropertyInfoProvider
                                                ppPropertiesProvider)
        {
            ppPropertiesProvider = _taskExecutor.Run(() =>
            {
                IVariableInformation cachedVarInfo = _varInfo.GetCachedView();
                IChildrenProvider childrenProvider =
                    _childrenProviderFactory.Create(cachedVarInfo.GetChildAdapter(),
                                                    (enum_DEBUGPROP_INFO_FLAGS) dwFields, dwRadix);

                return new AsyncDebugPropertyInfoProvider(childrenProvider, _taskExecutor);
            });

            return VSConstants.S_OK;
        }
    }
}