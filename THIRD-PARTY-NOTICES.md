Simple Time Countdown - third-party notices
===========================================

Simple Time Countdown is licensed under the MIT License (see LICENSE.txt, or LICENSE in the source
repository).

Every distributed package (the portable zip, Setup.exe and the MSIX) is self-contained: it includes
the Microsoft .NET 8 runtime and the Windows Desktop runtime, which are redistributed under the licence
below. The app itself uses no other third-party libraries, fonts or artwork; it draws its text with
fonts that come with Windows.

When a package is built, the notices that Microsoft ships with each bundled runtime pack are appended
to this file verbatim, so the copy inside a package always matches the runtime version it contains.


.NET runtime and Windows Desktop runtime
----------------------------------------

Components: Microsoft.NETCore.App (the .NET runtime and base class libraries, including msquic.dll)
and Microsoft.WindowsDesktop.App (WPF and Windows Forms, including wpfgfx_cor3.dll, PenImc_cor3.dll,
PresentationNative_cor3.dll, D3DCompiler_47_cor3.dll and vcruntime140_cor3.dll), as distributed by
Microsoft in the runtime packs for this platform.

Source: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf and
https://github.com/dotnet/winforms

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
