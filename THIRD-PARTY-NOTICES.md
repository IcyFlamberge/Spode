# Third-Party Notices

Spode is MIT-licensed (see `LICENSE`). It includes or uses the following
third-party components, which remain under their own licenses.

## ModAPI.DLLInjector.dll

- Source: Spore ModAPI Launcher Kit
  <https://github.com/Spore-Community/ModAPI-Launcher-Kit>
- Copyright 2017-2024 Splitwirez
- Copyright 2024-2025 Spore Community
- License: MIT

## Spore ModAPI SDK / core DLLs

- Source: <https://github.com/Spore-Community/Spore-ModAPI>
- Copyright (C) 2019 Eric Mor and contributors
- License: **GNU General Public License v3.0** (stated in the source file headers)
- GPL text: <https://www.gnu.org/licenses/gpl-3.0.txt>

Spode does **not** bundle or redistribute the ModAPI core DLLs. At the user's
request, Spode downloads them from the official GitHub releases into the local
ModAPI cache and injects them into Spore. They are not covered by Spode's MIT
licence.

If you redistribute the ModAPI core DLLs yourself, you must comply with GPLv3:
provide the corresponding source, the GPL text, and the copyright notices, and
do not add restrictions that conflict with GPLv3.

## .NET 8 / WPF

Spode is built with .NET 8 and WPF, which are provided by Microsoft under their
respective licenses. See the .NET runtime and WPF license terms distributed
with the .NET SDK/runtime.

## MIT License text

The following MIT text applies to the MIT-licensed components listed above:

```text
MIT License

Copyright 2017-2024 Splitwirez
Copyright 2024-2025 Spore Community

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
```
