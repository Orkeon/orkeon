# Third-Party Notices

This file lists third-party dependencies that require an explicit license notice.
Sections 1-3 cover the `local-embeddings` feature
(`src/tools/Orkeon.Tools.Embeddings.Local/` + `src/analysis/Orkeon.Analysis.Abstractions/DependencyInjection/LocalEmbeddingOptions.cs`),
all distributed under the MIT License. Section 4 covers the HTML/CSS engine of
`src/tools/Orkeon.Tools.Web/`, distributed under the MIT License. Section 5 covers
the LanceDB remote integration (`src/core/Orkeon.Infrastructure/Memory/LanceDb/`),
distributed under the Apache License 2.0. Section 7 covers the cross-encoder
reranker model embedded in `src/rag/Orkeon.Rag.Onnx.Model/`, distributed under
the Apache License 2.0.

---

## 1. SmartComponents.LocalEmbeddings

- **Version**: `0.1.0-preview10148` (pinned in `Directory.Packages.props`)
- **License**: MIT (SPDX: `MIT`) — Copyright (c) .NET Foundation. All rights reserved.
- **Source**: https://github.com/dotnet/smartcomponents (canonical location; the package's
  `projectUrl` https://github.com/dotnet-smartcomponents/smartcomponents points to the
  original experimental repository, now archived, whose README redirects to the new one)
- **License text**: https://github.com/dotnet/smartcomponents/blob/main/LICENSE
- **Authors**: Microsoft (per the package's `<authors>` field in `SmartComponents.LocalEmbeddings.nuspec`)
- **Role**: ONNX inference wrapper that ships an embedded BGE-micro-v2 model and exposes
  the `LocalEmbedder` type consumed by `LocalEmbeddingProvider`.

License text reproduced verbatim — including the upstream file's indentation and the
absence of a trailing period on its last line — from
`https://raw.githubusercontent.com/dotnet/smartcomponents/main/LICENSE`
(file last changed by upstream commit `b7ed1a3c01e4e3b519b730964a6017c4d49720ac`,
2024-03-06; retrieved 2026-06-11; GitHub license detection reports SPDX `MIT`):

```
    MIT License

    Copyright (c) .NET Foundation. All rights reserved.

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
    SOFTWARE
```

Provenance note: the NuGet package metadata (`SmartComponents.LocalEmbeddings.nuspec`)
declares no `<license>`/`<licenseUrl>` element and its `projectUrl` points to
`dotnet-smartcomponents/smartcomponents`, which is archived and ships no LICENSE file.
That repository's README states: "The contents of this repository have now moved to
https://github.com/dotnet/smartcomponents", where the code was open-sourced under the
MIT LICENSE reproduced above. The transitive sibling package
`SmartComponents.Inference 0.1.0-preview10148` (same repository, same `<authors>`
field, same missing `<license>` element) is covered by the same license text.

---

## 2. bge-micro-v2 (ONNX model)

- **Version**: bundled inside `SmartComponents.LocalEmbeddings 0.1.0-preview10148`
- **License**: MIT — Copyright (c) 2023 Benjamin Anderson (per the repository's
  `LICENSE` file; the model card YAML frontmatter also declares `license: mit`)
- **Source**: https://huggingface.co/TaylorAI/bge-micro-v2
- **License text**: https://huggingface.co/TaylorAI/bge-micro-v2/blob/main/LICENSE
- **Author**: TaylorAI (HuggingFace quantization) — derived from BAAI's BGE family
  (Beijing Academy of Artificial Intelligence)
- **Role**: 384-dimensional sentence embedding model. Produces the vectors returned by
  `LocalEmbeddingProvider.EmbedBatchAsync(...)`.

License text reproduced verbatim from
`https://huggingface.co/TaylorAI/bge-micro-v2/raw/main/LICENSE`
(repository revision `3edf6d7de0faa426b09780416fe61009f26ae589`, last modified
2024-06-06; retrieved 2026-06-11):

```
MIT License

Copyright (c) 2023 Benjamin Anderson

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

Provenance note: per its model card, `bge-micro-v2` was "distilled in a 2-step training
process (bge-micro was step 1) from `BAAI/bge-small-en-v1.5`". The parent model
(https://huggingface.co/BAAI/bge-small-en-v1.5, by the Beijing Academy of Artificial
Intelligence) is itself published under MIT per its Hugging Face metadata
(`license: mit` tag); the BAAI repository ships no separate LICENSE file.

---

## 3. Microsoft.Extensions.AI.Abstractions

- **Version**: `10.6.0` (pinned in `Directory.Packages.props`)
- **License**: MIT
- **Source**: https://github.com/dotnet/extensions
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions
- **Role**: Standard .NET AI abstractions (`IEmbeddingGenerator<,>`, `EmbeddingGeneratorMetadata`)
  consumed by `LocalEmbeddingGenerator` (the optional bridge in
  `src/tools/Orkeon.Tools.Embeddings.Local/LocalEmbeddingGenerator.cs`).

```
The MIT License (MIT)

Copyright (c) .NET Foundation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

---

## 4. AngleSharp

- **Version**: `1.5.0` (pinned in `Directory.Packages.props`)
- **License**: MIT (SPDX: `MIT`, per the package's `<license type="expression">` element
  and GitHub's license detection) — Copyright (c) 2013 - 2026 AngleSharp
- **Source**: https://github.com/AngleSharp/AngleSharp
- **NuGet**: https://www.nuget.org/packages/AngleSharp
- **License text**: https://github.com/AngleSharp/AngleSharp/blob/devel/LICENSE
- **Authors**: AngleSharp (per the package's `<authors>` field in `AngleSharp.nuspec`;
  owner: Florian Rappl)
- **Role**: W3C-compliant HTML5 parser with a native CSS selector engine, used by
  `ScrapeElementTool` (`src/tools/Orkeon.Tools.Web/ScrapeElementTool.cs`) to match
  elements via `QuerySelectorAll`. Introduced by remediation chantier R1.4 as the
  MIT-licensed replacement for the LGPL-3.0 pair
  `Fizzler` / `Fizzler.Systems.HtmlAgilityPack`, which is no longer referenced.

License text reproduced verbatim from
`https://raw.githubusercontent.com/AngleSharp/AngleSharp/devel/LICENSE`
(file last changed by upstream commit `8033a5c690af9c2c443f58e34875343fa4cc1d07`,
2026-06-06; retrieved 2026-06-11):

```
The MIT License (MIT)

Copyright (c) 2013 - 2026 AngleSharp

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

Note (dependency footprint): on `net10.0` — the repository's target framework —
`AngleSharp 1.5.0` declares **no** package dependencies (its
`System.Text.Encoding.CodePages` dependency only applies to the
`netstandard2.0` / `.NET Framework` target groups).

---

## 5. Apache.Arrow

- **Version**: `23.0.0` (pinned in `Directory.Packages.props`)
- **License**: Apache License 2.0 (SPDX: `Apache-2.0`, per the package's `<license type="expression">Apache-2.0</license>`)
- **Copyright**: Copyright 2016-2025 The Apache Software Foundation (per the package's `<copyright>` field)
- **Source**: https://github.com/apache/arrow-dotnet (the nuspec `repository` URL still points
  to https://github.com/apache/arrow, but the pinned commit
  `79e3844aca811abb3f9c463c0c27d407723e13be` lives in the `apache/arrow-dotnet` repository,
  where the C# implementation moved)
- **NuGet**: https://www.nuget.org/packages/Apache.Arrow (transitive sibling
  `Apache.Arrow.Scalars 23.0.0`, same repository and license, is covered by the same text)
- **License text**: https://github.com/apache/arrow-dotnet/blob/79e3844aca811abb3f9c463c0c27d407723e13be/LICENSE.txt
- **Role**: Arrow IPC encode/decode for the LanceDB Cloud/Enterprise REST protocol
  (`src/core/Orkeon.Infrastructure/Memory/LanceDb/LanceDbArrowCodec.cs`): the LanceDB
  `insert`/`merge_insert`/`create` endpoints require `application/vnd.apache.arrow.stream`
  payloads and the `query` endpoint returns Arrow IPC file binary data.

License text reproduced verbatim from
`https://raw.githubusercontent.com/apache/arrow-dotnet/79e3844aca811abb3f9c463c0c27d407723e13be/LICENSE.txt`
(the commit pinned in the 23.0.0 nuspec; retrieved 2026-06-11). The upstream file appends a
FlatBuffers provenance note after the Apache-2.0 text; it is kept as-is:

```

                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright [yyyy] [name of copyright owner]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

--------------------------------------------------------------------------------
This product includes code from FlatBuffers:

* src/Apache.Arrow/Flatbuf/FlatBuffers/

Copyright: (C) 2014 Google Inc.
Home page: https://github.com/google/flatbuffers/
Origin: https://github.com/google/flatbuffers/tree/v23.5.9/net/FlatBuffers
License: https://www.apache.org/licenses/LICENSE-2.0
```

Per Apache-2.0 §4(d), the upstream `NOTICE.txt` is propagated verbatim
(`https://raw.githubusercontent.com/apache/arrow-dotnet/79e3844aca811abb3f9c463c0c27d407723e13be/NOTICE.txt`,
retrieved 2026-06-11):

```
Apache Arrow .NET
Copyright 2018-2025 The Apache Software Foundation

This product includes software developed at
The Apache Software Foundation (http://www.apache.org/).
```

---

## 6. Jint

- **Version**: `4.10.0` (pinned in `Directory.Packages.props`)
- **License**: BSD 2-Clause "Simplified" License (SPDX: `BSD-2-Clause`)
- **Copyright**: Copyright (c) 2013, Sebastien Ros — All rights reserved.
- **Source**: https://github.com/sebastienros/jint
- **NuGet**: https://www.nuget.org/packages/Jint
- **License text**: https://github.com/sebastienros/jint/blob/v4.10.0/LICENSE.txt
- **Role**: Embedded ECMAScript interpreter that executes the TypeScript-syntax
  scripting DSL (`.ork.ts`): `src/scripting/Orkeon.Scripting` transpiles scripts via
  esbuild and runs them on the Jint engine (e.g.
  `Adapters/JsCrewConfigurationAdapter.cs`, `Bindings/AgentBuilderBinding.cs`).

License text — canonical SPDX `BSD-2-Clause` text, with the copyright line from
upstream `LICENSE.txt` (retrieved 2026-06-21):

```
BSD 2-Clause License

Copyright (c) 2013, Sebastien Ros
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## 7. ms-marco-MiniLM-L-6-v2 (ONNX model, embedded)

- **Version**: int8-quantized ONNX export embedded in `Orkeon.Rag.Onnx.Model`
  (`src/rag/Orkeon.Rag.Onnx.Model/assets/msmarco-minilm-l6-v2.quant.onnx` +
  matching `vocab.txt`) — the only model weights redistributed by this repository.
- **License**: Apache License 2.0 (SPDX: `Apache-2.0`)
- **Source**: https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2
  (sentence-transformers, UKP Lab, Technische Universität Darmstadt); embedded
  artifact exported by https://huggingface.co/Xenova/ms-marco-MiniLM-L-6-v2
  (same Apache-2.0 license).
- **License text**: https://www.apache.org/licenses/LICENSE-2.0
- **Role**: cross-encoder reranker for the opt-in RAG reranking stage
  (`AddOrkeonOnnxReranker()`, `Orkeon.Rag.Onnx`).

Full provenance, retrieval date (2026-07-26) and SHA-256 integrity hashes are
recorded in the package-level notice file
[`src/rag/Orkeon.Rag.Onnx.Model/THIRD-PARTY-NOTICES.md`](src/rag/Orkeon.Rag.Onnx.Model/THIRD-PARTY-NOTICES.md),
which ships inside the NuGet package. Training data note: the model was trained
on the MS MARCO passage-ranking dataset (Microsoft; the dataset itself carries a
non-commercial research license, while the trained model is distributed by its
authors under Apache-2.0).
