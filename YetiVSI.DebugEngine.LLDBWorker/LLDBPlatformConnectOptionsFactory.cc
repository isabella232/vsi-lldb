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

#include "LLDBPlatformConnectOptionsFactory.h"

#include <msclr/marshal_cppstd.h>

#include "LLDBPlatformConnectOptions.h"

namespace YetiVSI {
namespace DebugEngine {

SbPlatformConnectOptions ^
    LLDBPlatformConnectOptionsFactory::Create(System::String ^ url) {
  auto url_string = msclr::interop::marshal_as<std::string>(url);
  lldb::SBPlatformConnectOptions shell_command(url_string.c_str());
  // We can't check IsValid on the connect options object, because it doesn't
  // support that method.
  return gcnew LLDBPlatformConnectOptions(shell_command);
}
}  // namespace DebugEngine
}  // namespace YetiVSI
