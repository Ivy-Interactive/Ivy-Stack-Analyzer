# Third-Party Notices

Ivy.StackAnalyzer includes data derived from the following third-party projects.
Both are used under the MIT License. The derived files are generated, committed
data; the one-shot importer scripts used to generate them are not part of this
repository.

---

## github-linguist

- **Source:** https://github.com/github-linguist/linguist
- **License:** MIT
- **Derived files:** `src/Ivy.StackAnalyzer/data/languages.yml`,
  `src/Ivy.StackAnalyzer/data/vendor.yml`,
  `src/Ivy.StackAnalyzer/data/heuristics.yml`

`languages.yml` is transformed from linguist's `lib/linguist/languages.yml`
(language name, type, extensions, filenames, interpreters, color). `vendor.yml`
is derived from linguist's `lib/linguist/vendor.yml` plus common build/output and
lockfile patterns. `heuristics.yml` is ported from linguist's
`lib/linguist/heuristics.yml` (content-disambiguation rules for extensions shared
by multiple languages); the matching engine in `Scanning/Heuristics.cs` follows
linguist's `lib/linguist/heuristics.rb` algorithm.

```
The MIT License (MIT)

Copyright (c) 2017 GitHub, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

---

## specfy/stack-analyser

- **Source:** https://github.com/specfy/stack-analyser
- **License:** MIT
- **Derived files:** `src/Ivy.StackAnalyzer/data/detectors/*.yml`

The detector rules are transformed from specfy's `src/rules/**` `register({...})`
definitions (tech id, name, type, dependencies, files, extensions, dotenv). The
`.NET` (`dotnet.yml`) and Go (`golang.yml`) detector files are original to this
project.

```
MIT License

Copyright (c) Specfy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
